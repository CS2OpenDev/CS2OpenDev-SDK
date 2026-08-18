#region

using System.Globalization;
using System.Text;
using CS2SchemaGen.Diagnostics;
using CS2SchemaGen.Models;
using CS2SchemaGen.SchemaLens;

#endregion

namespace CS2SchemaGen.Emitters;

// Emits the typed entity wrappers and their binding manifests against
// CS2OpenDev.Sdk.Entities.Abstractions.
//
// The generated code touches the contract and nothing else: property bodies are
// one expression over an IEntityFieldReader, handle resolution is one call to
// IEntityWorld, and no storage, decode or lifetime concept appears anywhere. That
// is what makes the output usable over any runtime implementing the seam rather
// than over one particular parser.
//
// The emitted classes mirror the schema's curated hierarchy, and the ordinal
// layout law is what makes that correct (SDK#30):
//
//   layout(C) = layout(nearestCuratedAncestor(C)) ++ ordinal-sort(ownFields(C))
//
// — single-inheritance object layout applied to ordinal spaces. The base
// chain's ordinal space is a verbatim prefix of every descendant's binding, so
// a base property's compile-time ordinal constant addresses the same field
// through every derived binding, exactly as a C++ base subobject sits at
// offset 0 of every derived object. The ancestor walk follows Parents[0] to
// the nearest curated class; uncurated intermediates contribute nothing.
//
// What this deliberately gives up: a binding's whole path array is no longer
// globally ordinal-sorted — only a root's is, and each class's own suffix.
// That was always a repo convention, never a contract term: the contract
// requires only that ordinal i is the field at CanonicalPaths[i], dense from
// zero. The prefix property is the stronger invariant, and the one the tests
// state.
//
// Ordinal constants and CanonicalPaths are two projections of the one layout
// computation, emitted in the same pass, so they agree by construction rather
// than by convention. The constants stay private, because they are not API: a
// curation change to a base renumbers the own-segment of every descendant,
// which is safe only because each wrapper and its manifest move together.
//
// What none of this can prove: that the wire agrees. Prefix layout is correct
// only because a real flattened serializer carries exactly the class's true
// ancestry — measured by DemoViewer.NET on live entities (SDK#30: gun-chain
// classes carry all composed paths, shotguns carry the base's 8 and none of
// the gun's 3), not derivable here. A manifest routed through a non-ancestor
// would emit ordinals that read absent on real data, which is why the ancestor
// walk must follow the schema's real parent chain and nothing else.
internal static class EntityWrapperEmitter
{
    // Fields whose 0-default read would present absence as a plausible value,
    // so the property is nullable and absence is null. The value is the emitted
    // <remarks> body: each entry carries its own justification, because the trap
    // differs per field and a generic remark would state the wrong reason.
    //
    // Curation, not inference: no schema fact distinguishes "0 is a sentinel"
    // from "0 is a state". Two ways in so far:
    //
    //  - m_lifeState: a received 0 is a state (LIFE_ALIVE), so a 0-default
    //    getter makes a pawn that never transmitted the field indistinguishable
    //    from a live one.
    //  - the relocated origin canonical, on all three classes carrying it: the
    //    path names a struct whose leaves are what the wire carries, so the
    //    parent path never materialises over a GOTV demo and the 0-default
    //    presented that structural absence as the world origin. Measured, not
    //    theorised: none of DemoViewer.NET's 2,539 real-demo ordinal comparisons
    //    ever found an origin ordinal present (SDK#25, finding F3). Nullability
    //    only stops the wrapper stating a position it never received. The cell
    //    leaves themselves are now curated beside it (SDK#41, below); world-
    //    coordinate synthesis from them stays open deliberately, because decode
    //    arithmetic is read semantics and lives consumer-side (SDK#6 §3).
    //  - the quantized-origin leaves, on the same three classes: unlike the
    //    struct parent these do arrive on the wire, but a 0 that was never
    //    received is not a missing position; it is a position. Cell 0 is a
    //    legal world cell, and the consumer-side reconstruction is
    //    (cell − 32) × 512 + offset, so a 0-default would place the entity at
    //    −16384 on that axis with full confidence. These were born nullable, so
    //    the growth warning below does not apply to them: no consumer was ever
    //    typed against a non-nullable spelling of these properties.
    //
    // Growing this set is a breaking change for consumers typed against the
    // non-nullable property — DemoViewer.NET has the four m_pInGameMoneyServices
    // money fields staged and commented out for exactly that reason. Treat an
    // addition as a major with a deprecation cycle, never a silent flip; a type
    // flip on one name cannot carry a deprecation shim, so the version signal is
    // all there is. The origin entries are why the package's 0.1 became 0.2 —
    // pre-1.0, the minor is where that signal lives.
    // Shared by the three classes carrying the relocated origin canonical. The
    // remark has to say why null is the normal case on real demos, because a
    // consumer who reads "nullable" without the struct fact will treat null as a
    // transmission gap rather than as the wire's actual shape.
    //
    // Declared before SeenAwareFields because static initializers run in
    // declaration order; the other way round, the dictionary captures null.
    private static readonly string[] StructOriginRemark =
    [
        "Nullable because this canonical path names a struct",
        "(<c>CNetworkOriginCellCoordQuantizedVector</c>) whose leaves (<c>m_cellX/Y/Z</c>,",
        "<c>m_vecX/Y/Z</c>) are what the wire actually carries. The struct-valued parent",
        "path never materialises over a GOTV demo, so a zero default would present that",
        "absence as the world origin. <see langword=\"null\"/> means no value is stored",
        "under this path, not that the entity is at <c>(0,0,0)</c>. A runtime",
        "that reconstructs world coordinates from the cell leaves and stores the result",
        "under this path serves it through this property."
    ];

    // Shared by all six leaves of the quantized origin, on the same three
    // classes. One remark, because the trap is identical on every axis: zero is
    // a coordinate, not an absence.
    private static readonly string[] OriginLeafRemark =
    [
        "Nullable because <c>0</c> is a real value on every axis of the quantized",
        "origin: cell 0 names a legal world cell, and the consumer-side",
        "reconstruction is <c>(cell − 32) × 512 + offset</c>, so a fabricated zero",
        "would read as a real position at −16384 on that axis.",
        "<see langword=\"null\"/> means this leaf has never been received on the",
        "wire. Unlike the struct-valued origin canonical, these leaves are exactly",
        "what a demo transmits, so on live entities presence is the normal case.",
        "World-coordinate synthesis from them is deliberately left to the consumer",
        "(SDK#6 §3); this property is the raw wire value only."
    ];

    private static readonly Dictionary<(string Class, string Field), string[]> SeenAwareFields = new()
    {
        [("CCSPlayerPawn", "m_lifeState")] =
        [
            "Nullable because a received <c>0</c> is a meaningful value for this field,",
            "so absence cannot be reported as zero. <see langword=\"null\"/> means the",
            "field has never been received on the wire."
        ],
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin")] = StructOriginRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin")] = StructOriginRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin")] = StructOriginRemark,

        // The quantized-origin leaves (SDK#41), spelled out entry by entry so a
        // grep for any full canonical path lands here — the table is the record
        // of the decision, and a loop would hide eighteen decisions behind one.
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellX")] = OriginLeafRemark,
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellY")] = OriginLeafRemark,
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellZ")] = OriginLeafRemark,
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecX")] = OriginLeafRemark,
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecY")] = OriginLeafRemark,
        [("CCSPlayerPawn", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecZ")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellX")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellY")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellZ")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecX")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecY")] = OriginLeafRemark,
        [("CBaseCSGrenadeProjectile", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecZ")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellX")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellY")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellZ")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecX")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecY")] = OriginLeafRemark,
        [("CPlantedC4", "m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecZ")] = OriginLeafRemark
    };

    // Abstract bases that are curated so the type hierarchy is complete (they
    // are usable as base types and as Resolve<T> targets) but that never appear
    // as a live entity's class name in a demo. They get a wrapper and no registry
    // case, because a factory for them would be dead.
    //
    // Not derivable: the schema does not mark "networked but never instantiated
    // as its own serializer".
    private static readonly HashSet<string> NoFactoryRegistration = new(StringComparer.Ordinal)
    {
        "CCSWeaponBaseShotgun",
        "CBaseCSGrenade"
    };

    internal static void EmitAll(
        IGeneratorSink sink,
        LensState state,
        SchemaRoot schema,
        IReadOnlyDictionary<string, LensResolvedClass> resolution,
        Func<string, int?> declaredClassWidth,
        string lensHash,
        string schemaBuild,
        string ns)
    {
        // One Parents[0] map for both hierarchy questions — which curated
        // classes have curated descendants (sealing), and which curated ancestor
        // a class's layout prefixes (the layout law). Sharing the map is what
        // makes the two walks unable to disagree; the schema's second parents on
        // these chains are mixin interfaces the wire does not flatten, and a
        // full-graph walk that consulted them could route a class through a
        // non-ancestor — the exact manifest shape the shotgun measurement
        // proves reads absent on real data.
        Dictionary<string, string> firstParent = new(StringComparer.Ordinal);
        foreach (ClassModel c in schema.Classes)
        {
            if (c.Parents.Length > 0)
            {
                firstParent[c.Name] = c.Parents[0].Name;
            }
        }

        HashSet<string> hasCuratedDescendant = new(StringComparer.Ordinal);
        foreach (string curated in state.Classes.Keys)
        {
            string cursor = curated;
            while (firstParent.TryGetValue(cursor, out string? parent))
            {
                if (state.Classes.ContainsKey(parent))
                {
                    hasCuratedDescendant.Add(parent);
                }

                cursor = parent;
            }
        }

        // The nearest curated ancestor, walking Parents[0] past uncurated
        // intermediates (CCSWeaponBase, CEconEntity, ...), which contribute
        // nothing to any layout.
        Dictionary<string, string?> nearestCuratedAncestor = new(StringComparer.Ordinal);
        foreach (string curated in state.Classes.Keys)
        {
            string cursor = curated;
            string? found = null;
            while (firstParent.TryGetValue(cursor, out string? parent))
            {
                if (state.Classes.ContainsKey(parent))
                {
                    found = parent;
                    break;
                }

                cursor = parent;
            }

            nearestCuratedAncestor[curated] = found;
        }

        // The layout computation, memoized per class because every descendant
        // shares its ancestors' layouts by reference. The chain gates fire in
        // here — once per class, at the level that introduces the conflict —
        // and report at error severity so the exporter's post-3.0.7 guard turns
        // them into a non-zero exit. Emission still proceeds: the written tree
        // is what a maintainer diagnosing the failure wants to diff.
        Dictionary<string, ClassLayout> layouts = new(StringComparer.Ordinal);

        ClassLayout Layout(string engineClass)
        {
            if (layouts.TryGetValue(engineClass, out ClassLayout? done))
            {
                return done;
            }

            string? baseClass = nearestCuratedAncestor[engineClass];
            ClassLayout layout = ComposeLayout(
                sink,
                engineClass,
                state.Classes[engineClass],
                resolution[engineClass],
                declaredClassWidth,
                baseClass is null ? null : Layout(baseClass),
                baseClass is null ? null : state.Classes[baseClass].NetName);
            layouts[engineClass] = layout;
            return layout;
        }

        List<BindingPlan> plans = [];

        foreach ((string engineClass, LensClassState cls) in state.Classes)
        {
            if (!resolution.ContainsKey(engineClass))
            {
                continue;
            }

            ClassLayout layout = Layout(engineClass);
            BindingPlan plan = new(
                engineClass,
                cls.NetName,
                layout.BaseNetName,
                !hasCuratedDescendant.Contains(engineClass),
                layout.Fields,
                layout.InheritedCount,
                layout.Aliases,
                !NoFactoryRegistration.Contains(engineClass),
                state);
            plans.Add(plan);
            sink.AddSource(plan.NetName, EmitWrapper(plan, ns));
        }

        sink.AddSource("EntityWrapperRegistry", EmitRegistry(plans, lensHash, schemaBuild, ns));
    }

    // ── Planning ─────────────────────────────────────────────────────────────

    // One class's full layout under the prefix law: the base layout's FieldPlans
    // verbatim — their ordinals are already correct, because the prefix is the
    // base's ordinal space — then own fields at ordinals offset by the prefix
    // length. Aliases merge the same way, inherited first.
    private static ClassLayout ComposeLayout(
        IGeneratorSink sink,
        string engineClass,
        LensClassState cls,
        LensResolvedClass resolved,
        Func<string, int?> declaredClassWidth,
        ClassLayout? baseLayout,
        string? baseNetName)
    {
        List<FieldPlan> fields = baseLayout is null ? [] : [.. baseLayout.Fields];
        int inheritedCount = fields.Count;

        HashSet<string> chainPaths = new(fields.Select(f => f.Canonical), StringComparer.Ordinal);
        HashSet<string> chainProperties = new(fields.Select(f => f.Property), StringComparer.Ordinal);
        HashSet<string> ownPaths = new(StringComparer.Ordinal);

        int ordinal = inheritedCount;

        // Fields is a SortedDictionary keyed StringComparer.Ordinal, so iteration
        // order is the own-segment's ordinal order. Stated rather than relied on
        // silently, because the manifest's CanonicalPaths is built from the same
        // walk.
        foreach ((string canonical, LensFieldEntry entry) in cls.Fields)
        {
            if (!chainPaths.Add(canonical))
            {
                sink.ReportDiagnostic(Descriptors.DuplicateCurationAcrossChain, engineClass,
                    $"canonical path '{canonical}' is curated here and on a curated ancestor.");
            }

            if (!chainProperties.Add(entry.TargetProperty))
            {
                sink.ReportDiagnostic(Descriptors.DuplicateCurationAcrossChain, engineClass,
                    $"targetProperty '{entry.TargetProperty}' is curated here and on a curated ancestor.");
            }

            ownPaths.Add(canonical);

            TypeModel type = resolved.FieldTypes[canonical];
            string schemaType = LensTypeRenderer.Render(type);
            int? width = LensTypeRenderer.WidthBytes(type, declaredClassWidth);

            fields.Add(new FieldPlan(
                ordinal++,
                canonical,
                entry.TargetProperty,
                schemaType,
                entry.FirstSeenBuild,
                Dispatch(schemaType, width),
                SeenAwareFields.ContainsKey((engineClass, canonical))));
        }

        Dictionary<string, string> aliases = baseLayout is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(baseLayout.Aliases, StringComparer.Ordinal);

        // Identity entries (canonical → canonical) are a lookup convenience in
        // the replay model and must not reach the manifest: an alias whose key is
        // a live canonical path would shadow the field, and BindingConformance
        // rightly rejects it.
        foreach ((string alias, string target) in cls.Aliases)
        {
            if (string.Equals(alias, target, StringComparison.Ordinal)
                || !cls.Fields.ContainsKey(target))
            {
                continue;
            }

            if (aliases.TryGetValue(alias, out string? inherited))
            {
                sink.ReportDiagnostic(Descriptors.AliasConflictAcrossChain, engineClass,
                    $"alias '{alias}' targets '{target}' here and '{inherited}' on a curated ancestor.");
                continue;
            }

            // An own alias key naming an inherited canonical path. The
            // own-vs-own case cannot reach here — the Lens replay rejects it —
            // so a hit is always cross-level.
            if (chainPaths.Contains(alias))
            {
                sink.ReportDiagnostic(Descriptors.AliasConflictAcrossChain, engineClass,
                    $"alias '{alias}' (targeting '{target}') is also a canonical path on a curated ancestor.");
                continue;
            }

            aliases[alias] = target;
        }

        // The other direction: an inherited alias key that this level curates
        // as a canonical path. Within one class the Lens replay forbids the
        // collision; the chain is where it can now happen, and this level is
        // the one that introduces it — descendants inherit the conflict but do
        // not re-report it, because their layout memoizes this one.
        if (baseLayout is not null)
        {
            foreach ((string alias, string target) in baseLayout.Aliases)
            {
                if (ownPaths.Contains(alias))
                {
                    sink.ReportDiagnostic(Descriptors.AliasConflictAcrossChain, engineClass,
                        $"inherited alias '{alias}' (targeting '{target}') collides with a canonical path curated here.");
                }
            }
        }

        return new ClassLayout(baseNetName, fields, inheritedCount, aliases);
    }

    // The whole type dispatch. Every branch names the reader member the emitted
    // property calls, which is the decision the contract asks the emitter to make
    // rather than asking a runtime to infer.
    private static Reads Dispatch(string schemaType, int? widthBytes) => schemaType switch
    {
        "bool" => new Reads("bool", "TryReadBool", "false", "bool"),

        // CNetworkedQuantizedFloat is compression policy around a float, not a
        // shape of its own: the networked payload is one float32, quantized to
        // a bit width this layer cannot see (which is also why widthBytes stays
        // honestly null for it). Same projection TypeMapper makes for the main
        // SDK. Without this branch the quantized-origin leaves would fall to
        // the boxed composite default — the exact allocation the SDK#41 ask
        // exists to remove.
        "float32" or "GameTime_t" or "CNetworkedQuantizedFloat"
            => new Reads("float", "TryReadSingle", "0f", "float"),
        "QAngle" => new Reads("QAngle", "TryReadQAngle", "default", "QAngle"),

        "Vector" or "VectorWS"
            or "CNetworkOriginCellCoordQuantizedVector"
            or "CNetworkVelocityVector" => new Reads("Vector3", "TryReadVector3", "default", "Vector3"),

        _ when schemaType.StartsWith("CHandle<", StringComparison.Ordinal)
            => new Reads("uint", "TryReadEntityHandle", "0u", "uint"),

        "int32" or "uint8" or "uint16" or "uint32" or "GameTick_t"
            or "PlayerConnectedState" or "CSPlayerState" => new Reads("int", "TryReadInt32", "0", "int"),

        "uint64" => new Reads("ulong", "TryReadUInt64", "0ul", "ulong"),

        // The derived-width branch, and the reason the curated "wide int" table
        // every consumer maintained does not exist here. m_pMovementServices.
        // m_nButtons is declared CInButtonState — a struct — and carries uint64
        // on the wire; upstream's effectiveBuiltin is what makes that derivable
        // rather than something a human has to remember.
        _ when widthBytes == 8 => new Reads("ulong", "TryReadUInt64", "0ul", "ulong"),

        // Composites with no first-class representation on the seam: typed
        // arrays, strings, sub-structures, vectors of handles. Boxed is the
        // honest projection — inventing a shape for them would be worse.
        _ => new Reads("object?", "TryReadObject", "null", "object?")
    };

    // ── Wrapper emission ─────────────────────────────────────────────────────

    private static string EmitWrapper(BindingPlan plan, string ns)
    {
        StringBuilder sb = new();
        Header(sb, plan.EngineClass);

        sb.AppendLine("using System.CodeDom.Compiler;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"///     Typed read surface over <c>{NameHelpers.XmlEscape(plan.EngineClass)}</c>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[GeneratedCode(\"CS2OpenDev.Sdk.Exporter\", \"{ns}\")]");
        string sealedModifier = plan.Sealed ? "sealed " : "";
        sb.AppendLine($"public {sealedModifier}class {plan.NetName}(IEntityFieldReader reader, IEntityWorld world)");
        sb.AppendLine($"    : {plan.BaseNetName ?? "EntityWrapper"}(reader, world)");
        sb.AppendLine("{");

        // Own fields only: inherited properties come from the base class.
        // Their base-class ordinal constants stay correct through this class's
        // binding because the base layout is a verbatim prefix of it.
        IEnumerable<FieldPlan> ownFields = plan.Fields.Skip(plan.InheritedCount);

        foreach (FieldPlan f in ownFields)
        {
            EmitProperty(sb, f, plan);
        }

        if (plan.Fields.Count > plan.InheritedCount)
        {
            sb.AppendLine("    // Ordinals into the binding's CanonicalPaths — the own segment, after the");
            sb.AppendLine("    // inherited prefix. Private because they are not API: a curation change on");
            sb.AppendLine("    // an ancestor renumbers every own segment below it.");
            sb.AppendLine("    private static class Ord");
            sb.AppendLine("    {");
            foreach (FieldPlan f in ownFields)
            {
                sb.AppendLine($"        internal const int {f.Property} = {f.Ordinal};");
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitProperty(StringBuilder sb, FieldPlan f, BindingPlan plan)
    {
        // The canonical engine path in the doc comment is the grep bridge between
        // demo-facing names and .NET names: the thing someone reading a wire
        // dump needs in order to find this property.
        sb.AppendLine($"    /// <summary><c>{NameHelpers.XmlEscape(f.Canonical)}</c> ({NameHelpers.XmlEscape(f.SchemaType)}).</summary>");

        if (f.SeenAware)
        {
            // The per-field remark from the curated set: why this field's absence
            // cannot be a zero, in the artifact a consumer actually reads.
            sb.AppendLine("    /// <remarks>");
            foreach (string line in SeenAwareFields[(plan.EngineClass, f.Canonical)])
            {
                sb.AppendLine($"    ///     {line}");
            }

            sb.AppendLine("    /// </remarks>");
        }

        sb.AppendLine($"    [SchemaFieldVersion(\"{f.FirstSeenBuild}\")]");

        string type = f.SeenAware ? NullableOf(f.Reads.CsType) : f.Reads.CsType;
        string fallback = f.SeenAware ? "null" : f.Reads.Default;

        sb.AppendLine(
            $"    public {type} {f.Property} => "
            + $"Reader.{f.Reads.Member}(Ord.{f.Property}, out {f.Reads.OutType} v) ? v : {fallback};");
        sb.AppendLine();

        // A handle whose target is itself curated gets the resolved companion.
        // An uncurated target — CBaseEntity, C_CSPlayerPawn — gets the raw handle
        // only: mapping a client-side class onto its server-side sibling is an
        // equivalence the schema does not state, and guessing it here would put a
        // wrong type on a public property.
        if (f.Reads.Member == "TryReadEntityHandle"
            && CompanionType(f, plan) is { } target
            && f.Property.EndsWith("Handle", StringComparison.Ordinal))
        {
            string resolvedName = f.Property[..^"Handle".Length];
            sb.AppendLine($"    /// <summary><see cref=\"{f.Property}\"/> resolved by the runtime.</summary>");
            sb.AppendLine("    /// <remarks>");
            sb.AppendLine("    ///     <see langword=\"null\"/> when the handle names no live entity of this type —");
            sb.AppendLine("    ///     unset, invalid, stale, or pointing at a different class. Which encodings");
            sb.AppendLine("    ///     mean which is the runtime's policy, not this wrapper's.");
            sb.AppendLine("    /// </remarks>");
            sb.AppendLine(
                $"    public {target}? {resolvedName} => World.Resolve<{target}>({f.Property});");
            sb.AppendLine();
        }
    }

    // The C# type a resolved companion should carry, or null for no companion.
    private static string? CompanionType(FieldPlan f, BindingPlan plan)
    {
        int lt = f.SchemaType.IndexOf('<');
        int gt = f.SchemaType.LastIndexOf('>');
        if (lt < 0 || gt <= lt)
        {
            return null;
        }

        string declared = f.SchemaType[(lt + 1)..gt].Trim();

        // A handle declared against the client-side projection of a class names the
        // same entity as its server-side sibling: `C_CSPlayerPawn` and
        // `CCSPlayerPawn` are one pawn seen from two modules, and the Lens curates
        // whichever side it curates. CCSPlayerController.m_hPlayerPawn is declared
        // client-side, so matching only the literal spelling silently denied a
        // companion for the single most-used traversal there is — controller to
        // pawn. Reported from the consuming seat (SDK#25, finding F2).
        //
        // Only the C_ prefix is folded. Nothing else about the two names is
        // assumed equivalent, and a target curated under neither spelling still
        // gets no companion.
        string? curated =
            plan.State.Classes.ContainsKey(declared) ? declared
            : declared.StartsWith("C_", StringComparison.Ordinal)
              && plan.State.Classes.ContainsKey("C" + declared[2..]) ? "C" + declared[2..]
            : null;

        if (curated is null)
        {
            return null;
        }

        // The declared target's own net name, even when it has curated
        // descendants. A runtime dispatches the handle to the concrete class's
        // wrapper — an active weapon resolves to `SmokeGrenade`, not to
        // `BasePlayerWeapon` — and that used to force `EntityWrapper` here,
        // because the emitted types were flat and the typed fold failed for
        // every real entity (SDK#25, finding F1). Now that the wrappers mirror
        // the curated hierarchy, `SmokeGrenade` is a `BasePlayerWeapon` and the
        // typed fold succeeds for exactly the classes the wire can deliver.
        return plan.State.Classes[curated].NetName;
    }

    // ── Registry emission ────────────────────────────────────────────────────

    private static string EmitRegistry(
        IReadOnlyList<BindingPlan> plans, string lensHash, string schemaBuild, string ns)
    {
        StringBuilder sb = new();
        Header(sb, "the binding manifests and the wrapper factory");

        sb.AppendLine("using System.CodeDom.Compiler;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     The binding manifests for every wrapper in this assembly, and the factory");
        sb.AppendLine("///     that constructs them.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         Bind once per class at startup: hand <see cref=\"Bindings\"/> to your runtime so");
        sb.AppendLine("///         it can build its own ordinal-to-storage map, then call <see cref=\"Create\"/> when");
        sb.AppendLine("///         an entity of a known class appears.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         <see cref=\"LensHash\"/> and <see cref=\"SchemaBuild\"/> identify the curated state");
        sb.AppendLine("///         these wrappers were generated from. It is the hash of this repository's");
        sb.AppendLine("///         <c>schema-lens/state.json</c>, under its own canonical form.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         <b>Do not compare it against a hash your own runtime computes.</b> An");
        sb.AppendLine("///         implementation that maintains its own Schema Lens hashes a different preimage");
        sb.AppendLine("///         (different fields, different canonical form), so the two numbers are never");
        sb.AppendLine("///         comparable and a mismatch tells you nothing. Assert");
        sb.AppendLine("///         your hash against your state, and this one against the <c>state.json</c> this");
        sb.AppendLine("///         package was published beside.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         Compatibility across the seam is established by canonical path, not by hash:");
        sb.AppendLine("///         two curated states can describe the same field under different spellings, and");
        sb.AppendLine("///         the alias tables are what reconcile them.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine($"[GeneratedCode(\"CS2OpenDev.Sdk.Exporter\", \"{ns}\")]");
        sb.AppendLine("public static class EntityWrapperRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Curation hash of the Schema Lens state these wrappers were emitted from.</summary>");
        sb.AppendLine($"    public const string LensHash = \"{lensHash}\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The CS2 build the emitting schema described.</summary>");
        sb.AppendLine($"    public const string SchemaBuild = \"{schemaBuild}\";");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>Every class this assembly wraps, with the data a runtime needs to bind it.</summary>");
        sb.AppendLine("    public static IReadOnlyList<EntityClassBinding> Bindings { get; } =");
        sb.AppendLine("    [");
        foreach (BindingPlan p in plans)
        {
            EmitBinding(sb, p);
        }

        sb.AppendLine("    ];");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>Constructs the wrapper for an engine class, or <see langword=\"null\"/> if none is generated.</summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    ///     A generated switch rather than a delegate on the binding, so the manifests stay");
        sb.AppendLine("    ///     pure data — serialisable, statically enumerable, and trim-friendly. Abstract");
        sb.AppendLine("    ///     bases that never appear as a live entity's class have a wrapper type but no case");
        sb.AppendLine("    ///     here, because a factory for them would be dead.");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    public static EntityWrapper? Create(string engineClass, IEntityFieldReader reader, IEntityWorld world) =>");
        sb.AppendLine("        engineClass switch");
        sb.AppendLine("        {");
        foreach (BindingPlan p in plans.Where(p => p.Registers))
        {
            sb.AppendLine($"            \"{p.EngineClass}\" => new {p.NetName}(reader, world),");
        }

        sb.AppendLine("            _ => null");
        sb.AppendLine("        };");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitBinding(StringBuilder sb, BindingPlan p)
    {
        sb.AppendLine("        new(");
        sb.AppendLine($"            EngineClass: \"{p.EngineClass}\",");
        sb.AppendLine($"            NetName: \"{p.NetName}\",");

        sb.AppendLine("            CanonicalPaths:");
        sb.AppendLine("            [");
        foreach (FieldPlan f in p.Fields)
        {
            sb.AppendLine($"                \"{f.Canonical}\",");
        }

        sb.AppendLine("            ],");

        if (p.Aliases.Count == 0)
        {
            sb.AppendLine("            Aliases: new Dictionary<string, string>(),");
        }
        else
        {
            sb.AppendLine("            Aliases: new Dictionary<string, string>");
            sb.AppendLine("            {");
            foreach ((string alias, string target) in p.Aliases)
            {
                sb.AppendLine($"                [\"{alias}\"] = \"{target}\",");
            }

            sb.AppendLine("            },");
        }

        int[] handleOrdinals = p.Fields
            .Where(f => f.Reads.Member == "TryReadEntityHandle")
            .Select(f => f.Ordinal)
            .ToArray();

        sb.AppendLine(handleOrdinals.Length == 0
            ? "            HandleOrdinals: []),"
            : $"            HandleOrdinals: [{string.Join(", ", handleOrdinals.Select(o => o.ToString(CultureInfo.InvariantCulture)))}]),");
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private static void Header(StringBuilder sb, string what)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// {what}");
        sb.AppendLine("//");
        sb.AppendLine("// Generated from schema-lens/state.json. Regenerate with cs2-sdk-exporter.");
        sb.AppendLine("// Do not edit this file directly.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private static string NullableOf(string csType) =>
        csType.EndsWith('?') ? csType : csType + "?";

    // OutType is the reader member's own out-parameter type. Usually the property
    // type with any nullability stripped — but TryReadObject genuinely takes
    // `out object?`, so deriving it from the property type gets that one wrong.
    private sealed record Reads(string CsType, string Member, string Default, string OutType);

    private sealed record FieldPlan(
        int Ordinal,
        string Canonical,
        string Property,
        string SchemaType,
        string FirstSeenBuild,
        Reads Reads,
        bool SeenAware);

    // One class's composed layout under the prefix law. `Fields` is the whole
    // ordinal space — the inherited prefix verbatim, then own fields — and
    // `InheritedCount` is the boundary: the binding emits all of it, the
    // wrapper emits only what is after the boundary. Descendants embed this
    // list by reference, which is what "verbatim prefix" means mechanically.
    private sealed record ClassLayout(
        string? BaseNetName,
        IReadOnlyList<FieldPlan> Fields,
        int InheritedCount,
        IReadOnlyDictionary<string, string> Aliases);

    private sealed record BindingPlan(
        string EngineClass,
        string NetName,
        string? BaseNetName,
        bool Sealed,
        IReadOnlyList<FieldPlan> Fields,
        int InheritedCount,
        IReadOnlyDictionary<string, string> Aliases,
        bool Registers,
        LensState State);
}
