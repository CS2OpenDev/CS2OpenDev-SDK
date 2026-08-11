#region

using System.Text.Json;
using CS2SchemaGen.Diagnostics;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.SchemaLens;

// The staleness gates — the issue #6 §1 answer.
//
// Replay proves the migrations are internally coherent; these gates prove they
// still describe the WORLD. Three directions of drift are checked, each with
// its own diagnostic because each has its own remedy:
//
//   CS2_GEN_010 — a tracked name the schema no longer has (or no longer has
//                 uniquely). The schema dropped something the Lens serves.
//   CS2_GEN_011 — a rename whose retired name the schema has RE-GROWN. The
//                 schema un-dropped something a migration recorded as gone.
//   CS2_GEN_012 — a new schema field on a covered class that no migration has
//                 tracked or acknowledged. The schema added something the Lens
//                 has never looked at.
//
// Together they close the loop: a Valve patch that touches a covered class in
// any direction fails the regen instead of shipping a Lens that is quietly
// wrong about it.
internal static class LensGates
{
    internal static LensGateReport Run(
        LensState state,
        IReadOnlyList<LensRenameRecord> renames,
        SchemaRoot schema,
        string? committedStateJson)
    {
        SchemaIndex index = SchemaIndex.Build(schema);
        List<LensGateFailure> failures = [];
        Dictionary<string, LensResolvedClass> resolution = new(StringComparer.Ordinal);

        // ── Class + field resolution (CS2_GEN_010) ───────────────────────────
        foreach ((string className, LensClassState cls) in state.Classes)
        {
            List<ClassModel> candidates = index.ByName.TryGetValue(className, out List<ClassModel>? all)
                ? all
                : [];
            if (cls.ModulePin is { } pin)
            {
                candidates = candidates.Where(c => string.Equals(c.Module, pin, StringComparison.Ordinal)).ToList();
            }

            if (candidates.Count == 0)
            {
                string where = cls.ModulePin is { } p ? $" in module '{p}'" : "";
                failures.Add(Fail(Descriptors.UnresolvedLensField, className,
                    $"no schema class of that name exists{where}. Author a migration: removeClass if it is "
                    + "gone, or re-cover it under its new name if upstream renamed it — the Lens must never "
                    + "serve a stale name."));
                continue;
            }

            if (candidates.Count > 1)
            {
                string modules = string.Join(", ",
                    candidates.Select(c => $"'{c.Module}'").OrderBy(m => m, StringComparer.Ordinal));
                failures.Add(Fail(Descriptors.UnresolvedLensField, className,
                    $"the bare name matches classes in modules {modules}. Pin one with 'module' on the "
                    + "addClass op."));
                continue;
            }

            ClassModel resolved = candidates[0];
            Dictionary<string, TypeModel> fieldTypes = new(StringComparer.Ordinal);
            bool classOk = true;

            foreach (string canonical in cls.Fields.Keys)
            {
                if (index.TryResolvePath(resolved, canonical, out TypeModel? leaf, out string? failDetail))
                {
                    fieldTypes[canonical] = leaf;
                }
                else
                {
                    classOk = false;
                    failures.Add(Fail(Descriptors.UnresolvedLensField, $"{className}.{canonical}",
                        failDetail + " Author a migration: 'rename' if the member moved, 'removeField' if it "
                        + "is gone — the Lens must never silently serve a stale name."));
                }
            }

            if (classOk)
            {
                string[] observed = resolved.Fields
                    .Select(f => f.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToArray();
                resolution[className] = new LensResolvedClass(resolved, resolved.Module, fieldTypes, observed);
            }
        }

        // ── Superseded renames (CS2_GEN_011) ─────────────────────────────────
        //
        // Checked against the resolved class rather than re-resolving, so a
        // class that already failed above does not double-report.
        foreach (LensRenameRecord rename in renames)
        {
            if (!resolution.TryGetValue(rename.Class, out LensResolvedClass? host))
            {
                continue;
            }

            if (index.TryResolvePath(host.Class, rename.From, out _, out _))
            {
                failures.Add(Fail(Descriptors.LensRenameSuperseded, rename.MigrationId,
                    $"'{rename.Class}.{rename.From}' resolves in the current schema again, so the "
                    + $"{rename.Op} to '{rename.To}' no longer describes upstream. The re-grown field is a "
                    + "new declaration that needs its own addField (or ignoreField), and the alias from the "
                    + "old name must be retired."));
            }
        }

        // ── Unmigrated schema changes (CS2_GEN_012) ──────────────────────────
        //
        // Diffed against the COMMITTED state.json, not against this run's
        // in-memory state: the committed file is the last reviewed truth, and
        // "new since someone last looked" is the only definition of new that a
        // review gate can honestly enforce. First run — no committed file, or
        // a class not yet in it — has no baseline and is skipped; the regen
        // diff itself is the review surface for that case.
        Dictionary<string, HashSet<string>> committed = ReadCommittedObserved(committedStateJson);
        foreach ((string className, LensResolvedClass resolved) in resolution)
        {
            if (!committed.TryGetValue(className, out HashSet<string>? baseline))
            {
                continue;
            }

            LensClassState cls = state.Classes[className];
            HashSet<string> accounted = new(StringComparer.Ordinal);
            foreach (string canonical in cls.Fields.Keys)
            {
                accounted.Add(FirstSegment(canonical));
            }

            foreach (string ignored in cls.Ignored)
            {
                accounted.Add(FirstSegment(ignored));
            }

            foreach (string observed in resolved.ObservedFields)
            {
                if (!baseline.Contains(observed) && !accounted.Contains(observed))
                {
                    failures.Add(Fail(Descriptors.UnmigratedSchemaChange, className, observed));
                }
            }
        }

        return new LensGateReport(failures, resolution);
    }

    private static string FirstSegment(string path)
    {
        int dot = path.IndexOf('.');
        return dot >= 0 ? path.Substring(0, dot) : path;
    }

    private static LensGateFailure Fail(GeneratorDiagnostic descriptor, params object[] args) =>
        new(descriptor, descriptor.Format(args));

    // The committed file is read tolerantly: a missing or malformed state.json
    // degrades to "no baseline" — first-run semantics — because the very next
    // successful run rewrites the file, and failing the build over a file this
    // run is about to replace would leave no path forward.
    private static Dictionary<string, HashSet<string>> ReadCommittedObserved(string? json)
    {
        Dictionary<string, HashSet<string>> result = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json))
        {
            return result;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("classes", out JsonElement classes)
                || classes.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (JsonProperty cls in classes.EnumerateObject())
            {
                if (cls.Value.ValueKind != JsonValueKind.Object
                    || !cls.Value.TryGetProperty("observedFields", out JsonElement observed)
                    || observed.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                HashSet<string> names = new(StringComparer.Ordinal);
                foreach (JsonElement item in observed.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        names.Add(item.GetString()!);
                    }
                }

                result[cls.Name] = names;
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        return result;
    }

    // ── Schema lookup ────────────────────────────────────────────────────────

    private sealed class SchemaIndex
    {
        internal required Dictionary<string, List<ClassModel>> ByName { get; init; }

        internal required Dictionary<string, List<ClassModel>> ChildrenByParentName { get; init; }

        internal static SchemaIndex Build(SchemaRoot schema)
        {
            Dictionary<string, List<ClassModel>> byName = new(StringComparer.Ordinal);
            Dictionary<string, List<ClassModel>> children = new(StringComparer.Ordinal);
            foreach (ClassModel cls in schema.Classes)
            {
                if (!byName.TryGetValue(cls.Name, out List<ClassModel>? sameName))
                {
                    byName[cls.Name] = sameName = [];
                }

                sameName.Add(cls);

                foreach (ParentModel parent in cls.Parents)
                {
                    if (!children.TryGetValue(parent.Name, out List<ClassModel>? kids))
                    {
                        children[parent.Name] = kids = [];
                    }

                    kids.Add(cls);
                }
            }

            return new SchemaIndex { ByName = byName, ChildrenByParentName = children };
        }

        // Walks a dotted path from `root`. Each segment must name a field; the
        // search covers the class itself and its ancestors, and — after the
        // first pointer/embedded hop — its derived classes too. The asymmetry
        // is deliberate: a covered class names the CONCRETE networked type, so
        // a field found only on a subclass would belong to some other entity.
        // A sub-service pointer, by contrast, is typed as the engine's base
        // service (`CPlayer_ItemServices`) while the instance a game entity
        // actually carries is the game-specific derivation
        // (`CCSPlayer_ItemServices`) — the static type is a lower bound, and a
        // field on a derived service is exactly where CS2 keeps its data.
        internal bool TryResolvePath(
            ClassModel root,
            string path,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TypeModel? leaf,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? failDetail)
        {
            leaf = null;
            failDetail = null;

            string[] segments = path.Split('.');
            ClassModel current = root;
            bool allowDerived = false;

            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                FieldModel? field = FindField(current, segment, allowDerived);
                if (field is null)
                {
                    string scope = allowDerived
                        ? "the class, its ancestors and its derived classes"
                        : "the class and its ancestors";
                    failDetail = $"segment '{segment}' does not exist on '{current.Name}' (searched {scope}).";
                    return false;
                }

                if (i == segments.Length - 1)
                {
                    leaf = field.Type;
                    return true;
                }

                string? targetName = TraversalTarget(field.Type);
                if (targetName is null)
                {
                    failDetail = $"segment '{segment}' on '{current.Name}' is of type "
                                 + $"'{LensTypeRenderer.Render(field.Type)}', which cannot be traversed — only a "
                                 + "pointer to a declared class or an embedded declared class can carry a sub-path.";
                    return false;
                }

                ClassModel? next = ResolveClassPreferring(targetName, current.Module);
                if (next is null)
                {
                    failDetail = $"segment '{segment}' on '{current.Name}' points at class '{targetName}', "
                                 + "which does not exist in the schema.";
                    return false;
                }

                current = next;
                allowDerived = true;
            }

            failDetail = "empty path.";
            return false;
        }

        // A segment can only be traversed when its type bottoms out in a
        // declared class: a pointer (through any number of levels) or an
        // embedded class member. Builtins, atomics, arrays and enums have no
        // fields to walk into.
        private static string? TraversalTarget(TypeModel type) => type switch
        {
            PtrType p => TraversalTarget(p.Inner),
            DeclaredClassType c => c.Name,
            _ => null
        };

        // Prefer the module we are already walking in; a name that only exists
        // elsewhere is taken as-is; a tie is broken by ordinal module order so
        // resolution stays deterministic. Not an error: same-named classes in
        // client and server are near-identical mirrors, and refusing to cross
        // would fail real paths over a distinction that carries no information
        // at this depth.
        private ClassModel? ResolveClassPreferring(string name, string module)
        {
            if (!ByName.TryGetValue(name, out List<ClassModel>? candidates) || candidates.Count == 0)
            {
                return null;
            }

            List<ClassModel> preferred = candidates
                .Where(c => string.Equals(c.Module, module, StringComparison.Ordinal))
                .ToList();
            List<ClassModel> pool = preferred.Count > 0 ? preferred : candidates;
            return pool.OrderBy(c => c.Module, StringComparer.Ordinal).First();
        }

        private FieldModel? FindField(ClassModel cls, string name, bool allowDerived)
        {
            // Up first: self and ancestors, breadth-first, staying in the
            // class's own module wherever the parent name allows it.
            Queue<ClassModel> up = new();
            HashSet<(string, string)> visited = [];
            up.Enqueue(cls);
            while (up.Count > 0)
            {
                ClassModel c = up.Dequeue();
                if (!visited.Add((c.Name, c.Module)))
                {
                    continue;
                }

                foreach (FieldModel f in c.Fields)
                {
                    if (string.Equals(f.Name, name, StringComparison.Ordinal))
                    {
                        return f;
                    }
                }

                foreach (ParentModel p in c.Parents)
                {
                    ClassModel? parent = ResolveClassPreferring(p.Name, c.Module);
                    if (parent is not null)
                    {
                        up.Enqueue(parent);
                    }
                }
            }

            if (!allowDerived)
            {
                return null;
            }

            // Then down: derived classes, breadth-first, ordinal order at each
            // level for determinism, same module only — a derivation in the
            // other module mirrors a different entity graph.
            Queue<ClassModel> down = new();
            visited.Clear();
            foreach (ClassModel child in ChildrenOf(cls))
            {
                down.Enqueue(child);
            }

            while (down.Count > 0)
            {
                ClassModel c = down.Dequeue();
                if (!visited.Add((c.Name, c.Module)))
                {
                    continue;
                }

                foreach (FieldModel f in c.Fields)
                {
                    if (string.Equals(f.Name, name, StringComparison.Ordinal))
                    {
                        return f;
                    }
                }

                foreach (ClassModel child in ChildrenOf(c))
                {
                    down.Enqueue(child);
                }
            }

            return null;
        }

        private IEnumerable<ClassModel> ChildrenOf(ClassModel cls) =>
            ChildrenByParentName.TryGetValue(cls.Name, out List<ClassModel>? kids)
                ? kids.Where(k => string.Equals(k.Module, cls.Module, StringComparison.Ordinal))
                    .OrderBy(k => k.Name, StringComparer.Ordinal)
                : [];
    }
}

// One gate failure, pre-formatted. The descriptor rides along so the exporter
// can report under the right id and tests can assert on it without string
// matching.
internal sealed record LensGateFailure(GeneratorDiagnostic Descriptor, string Message);

// A covered class as the current schema sees it: the class record, the module
// that resolution landed on, the leaf schema type of every tracked canonical
// path, and the class's own field names. Everything the state writer needs
// that is NOT curated content.
internal sealed record LensResolvedClass(
    ClassModel Class,
    string Module,
    IReadOnlyDictionary<string, TypeModel> FieldTypes,
    string[] ObservedFields);

internal sealed record LensGateReport(
    IReadOnlyList<LensGateFailure> Failures,
    IReadOnlyDictionary<string, LensResolvedClass> Resolution);
