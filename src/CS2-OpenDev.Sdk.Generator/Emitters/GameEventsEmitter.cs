#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

// Emits one `public sealed record {Name}Event` per entry in gameevents_schema.json
// into a `CS2OpenSchema.Events` namespace, plus a `GameEvents` static registry of
// every event's native name.
//
// Source priority for the (rare) cross-file duplicate names — `mod` > `game` >
// `core` — mirrors the runtime layering: `mod.gameevents` extends `game.gameevents`
// extends `core.gameevents`, so the mod variant is what CS2 actually fires. The
// "winner" of a name gets the unsuffixed C# type; non-winners get a source suffix
// (e.g. `PlayerDeathCoreEvent`) so consumers can still reach the older shape.
internal static class GameEventsEmitter
{
    // Per-event filename + namespace constants kept in one place so both the
    // emit loop and the SchemaEvents reverse-lookup table use the same values.
    private const string EventsNamespaceSegment = "Events";
    private const string EventTypeSuffix = "Event";

    // mod overrides game overrides core (CS2-specific layering). Anything else
    // gets a default low priority so unrecognised sources still emit, just last.
    private static readonly Dictionary<string, int> SourcePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mod.gameevents"] = 3,
        ["game.gameevents"] = 2,
        ["core.gameevents"] = 1
    };

    // `schemaForStamp` is the parsed cs2_schema.json — gameevents_schema.json
    // carries no `revision` / `version_date` of its own, so we reuse the class-
    // schema stamp on every emitted event file. One stamp per CS2 build keeps
    // the per-file headers consistent across both buckets; if upstream ever adds
    // its own revision metadata to the events schema, swap the source here.
    internal static void EmitAll(IGeneratorSink sink, GameEventsRoot root, string rootNs, SchemaRoot? schemaForStamp)
    {
        if (root.Events.Length == 0)
        {
            return;
        }

        string eventsNs = rootNs + "." + EventsNamespaceSegment;

        // Group by raw event name; pick a winner per group; assign final C# type
        // names so the (uncommon) name-collision case gets deterministic suffixes
        // without disturbing the 273 events whose names are unique.
        Dictionary<string, List<GameEventModel>> byName = new(StringComparer.Ordinal);
        foreach (GameEventModel ev in root.Events)
        {
            if (!byName.TryGetValue(ev.Name, out List<GameEventModel>? list))
            {
                byName[ev.Name] = list = [];
            }

            list.Add(ev);
        }

        // Stable assignment of C# type names. Within a name-group, sort by source
        // priority descending; the winner takes "{Pascal}Event"; subsequent entries
        // take "{Pascal}{SourcePascal}Event". Sorting by source name as the tie-
        // breaker keeps the assignment stable when a new source file is introduced.
        Dictionary<GameEventModel, string> csNames = new();
        foreach (List<GameEventModel> group in byName.Values)
        {
            group.Sort((a, b) =>
            {
                int pa = SourcePriority.TryGetValue(a.Source, out int va) ? va : 0;
                int pb = SourcePriority.TryGetValue(b.Source, out int vb) ? vb : 0;
                int byPriority = pb.CompareTo(pa);
                return byPriority != 0 ? byPriority : StringComparer.Ordinal.Compare(a.Source, b.Source);
            });

            string baseName = NameHelpers.ToPascalCaseFromSnake(group[0].Name);
            csNames[group[0]] = baseName + EventTypeSuffix;
            for (int i = 1; i < group.Count; i++)
            {
                string sourcePascal = PascalSource(group[i].Source);
                csNames[group[i]] = baseName + sourcePascal + EventTypeSuffix;
            }
        }

        // Emit one file per event. Filename is `Events/{TypeName}.g.cs` mirroring
        // the per-module class layout, so the Exporter's `DiskSink` lands them at
        // `src/CS2-OpenDev.Sdk/Events/{TypeName}.cs`.
        foreach (GameEventModel ev in root.Events)
        {
            string typeName = csNames[ev];
            string relativePath = EventsNamespaceSegment + "/" + typeName;
            string source = BuildEventSource(ev, typeName, eventsNs, schemaForStamp);
            sink.AddSource(relativePath, source);
        }

        // SchemaEvents reverse-lookup: { TypeName → "native_event_name", plus
        // per-event field-name tables } — same shape as SchemaNames so consumers
        // can switch on a C# property name and recover the raw KV1 identifier.
        string registry = BuildRegistrySource(root.Events, csNames, rootNs, schemaForStamp);
        sink.AddSource("SchemaEvents", registry);
    }

    private static string BuildEventSource(GameEventModel ev, string typeName, string ns, SchemaRoot? schemaForStamp)
    {
        StringBuilder sb = new(2048);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// CS2OpenSchema — " + EventsNamespaceSegment + "/" + typeName);
        sb.AppendLine("// Generated from upstream gameevents_schema.json. Regenerate with cs2-sdk-exporter.");
        sb.AppendLine("// Do not edit this file directly.");
        sb.AppendLine();
        if (schemaForStamp is not null)
        {
            int posBefore = sb.Length;
            ModuleEmitter.AppendSchemaStamp(sb, schemaForStamp);
            if (sb.Length > posBefore)
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        // ── Type-level documentation ─────────────────────────────────────────
        //
        // Mirrors ClassEmitter / EnumEmitter: description-first summary when the
        // annotation overlay supplies one, with the schema event name relocated
        // to `<remarks>`. The KV1 `comment` field from the source `.gameevents`
        // line stays in `<remarks>` regardless — it's incidental metadata, not
        // a primary description (most are boilerplate like "a game event…").
        bool useDescription = NameHelpers.HasSummaryDescription(ev.Annotations);
        sb.AppendLine("/// <summary>");
        sb.Append("///     ").AppendLine(NameHelpers.ResolveSummaryText(ev.Name, ev.Annotations));
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.Append("///     ");
        if (useDescription)
        {
            sb.Append($"Native name: <c>{NameHelpers.XmlEscape(ev.Name)}</c>. ");
        }

        sb.Append("Source: <c>").Append(NameHelpers.XmlEscape(ev.Source)).Append("</c>");
        if (ev.Local)
        {
            sb.Append(" — local");
        }

        if (ev.Reliable)
        {
            sb.Append(" — reliable");
        }

        sb.AppendLine(".");
        if (!string.IsNullOrWhiteSpace(ev.Comment))
        {
            sb.Append("///     <para>").Append(NameHelpers.XmlEscape(ev.Comment!)).AppendLine("</para>");
        }

        NameHelpers.AppendAnnotationRemarks(sb, "", ev.Annotations);
        sb.AppendLine("/// </remarks>");

        // ── Attributes ───────────────────────────────────────────────────────
        sb.AppendLine($"[NativeName(\"{NameHelpers.EscAttrString(ev.Name)}\")]");
        sb.AppendLine($"[GameEventSource(\"{NameHelpers.EscAttrString(ev.Source)}\")]");
        if (ev.Local)
        {
            sb.AppendLine("[GameEventLocal]");
        }

        if (ev.Reliable)
        {
            sb.AppendLine("[GameEventReliable]");
        }

        // ── Declaration ──────────────────────────────────────────────────────
        // `record` with `init` accessors so the engine-fired payload is immutable
        // post-construction; `sealed` because no event derives from another. An
        // empty event (no `fields`) still emits — parameterless `new XEvent()` is
        // valid and lets dispatchers signal occurrence without a payload.
        sb.AppendLine($"public sealed record {typeName}");
        sb.AppendLine("{");

        // Stable alphabetised emission of properties (matches the formatter
        // convention used by ClassEmitter). The native-order is preserved in
        // metadata via `[NativeName]`; source order is for readability only.
        string[] propNames = ComputeFieldPropNames(ev.Fields);
        int[] order = new int[ev.Fields.Length];
        for (int i = 0; i < ev.Fields.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => StringComparer.Ordinal.Compare(propNames[a], propNames[b]));

        for (int k = 0; k < order.Length; k++)
        {
            int i = order[k];
            GameEventFieldModel field = ev.Fields[i];
            string propName = propNames[i];
            string csType = GameEventTypeMapper.Map(field.Type);

            if (k > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine("    /// <summary>");
            // Field-level summary precedence: curated annotation description >
            // the KV1 `comment` carried by the source `.gameevents` line >
            // PascalCased property name. Each layer is treated as prose so
            // ResolveSummaryText adds a terminal period and escapes XML.
            string fallback = !string.IsNullOrWhiteSpace(field.Comment) ? field.Comment! : propName;
            sb.Append("    ///     ").AppendLine(NameHelpers.ResolveSummaryText(fallback, field.Annotations));
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    /// <remarks>");
            sb.Append("    ///     Native name: <c>").Append(NameHelpers.XmlEscape(field.Name))
                .Append("</c> — KV1 type <c>").Append(NameHelpers.XmlEscape(field.Type)).AppendLine("</c>.");
            NameHelpers.AppendAnnotationRemarks(sb, "    ", field.Annotations);
            sb.AppendLine("    /// </remarks>");
            sb.AppendLine($"    [NativeName(\"{NameHelpers.EscAttrString(field.Name)}\")]");
            sb.AppendLine($"    [GameEventFieldType(\"{NameHelpers.EscAttrString(field.Type)}\")]");
            sb.AppendLine($"    public required {csType} {propName} {{ get; init; }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildRegistrySource(GameEventModel[] events, Dictionary<GameEventModel, string> csNames,
        string rootNs, SchemaRoot? schemaForStamp)
    {
        StringBuilder sb = new();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// CS2OpenSchema — reverse-lookup table of native game-event names");
        sb.AppendLine("// Generated from upstream gameevents_schema.json. Regenerate with cs2-sdk-exporter.");
        sb.AppendLine("// Do not edit this file directly.");
        sb.AppendLine();
        if (schemaForStamp is not null)
        {
            int posBefore = sb.Length;
            ModuleEmitter.AppendSchemaStamp(sb, schemaForStamp);
            if (sb.Length > posBefore)
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("#nullable enable");
        // CS1591: SchemaEvents is a mechanical reverse-lookup with hundreds of
        // const-string entries — XML doc per member would dwarf the data without
        // adding signal. Matches the suppression on SchemaNames.
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNs};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Reverse-lookup table from generated event-record names to the original");
        sb.AppendLine("///     KV1 identifiers as they appear in <c>gameevents_schema.json</c>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class SchemaEvents");
        sb.AppendLine("{");

        // Sort by emitted C# type name for stable diffs.
        GameEventModel[] sorted = (GameEventModel[])events.Clone();
        Array.Sort(sorted, (a, b) => StringComparer.Ordinal.Compare(csNames[a], csNames[b]));

        bool firstEvent = true;
        foreach (GameEventModel ev in sorted)
        {
            if (!firstEvent)
            {
                sb.AppendLine();
            }

            firstEvent = false;

            string typeName = csNames[ev];
            sb.AppendLine($"    public static class {typeName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const string EventName = \"{NameHelpers.EscAttrString(ev.Name)}\";");

            if (ev.Fields.Length > 0)
            {
                sb.AppendLine();
                string[] propNames = ComputeFieldPropNames(ev.Fields);
                int[] order = new int[ev.Fields.Length];
                for (int i = 0; i < ev.Fields.Length; i++)
                {
                    order[i] = i;
                }

                Array.Sort(order, (a, b) => StringComparer.Ordinal.Compare(propNames[a], propNames[b]));
                foreach (int i in order)
                {
                    sb.AppendLine($"        public const string {propNames[i]} = \"{NameHelpers.EscAttrString(ev.Fields[i].Name)}\";");
                }
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Per-event field-name resolution. Field names are snake_case in the source;
    // we PascalCase them. If two fields collide post-fold (extremely unlikely —
    // none in the current schema), append a stable ordinal suffix.
    private static string[] ComputeFieldPropNames(GameEventFieldModel[] fields)
    {
        string[] result = new string[fields.Length];
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < fields.Length; i++)
        {
            string candidate = NameHelpers.Esc(NameHelpers.ToPascalCaseFromSnake(fields[i].Name));
            if (candidate.Length == 0)
            {
                candidate = "Field" + i;
            }

            if (char.IsDigit(candidate[0]))
            {
                candidate = "_" + candidate;
            }

            if (seen.TryGetValue(candidate, out int n))
            {
                seen[candidate] = n + 1;
                result[i] = candidate + (n + 1);
            }
            else
            {
                seen[candidate] = 1;
                result[i] = candidate;
            }
        }

        return result;
    }

    // "core.gameevents" → "Core", "mod.gameevents" → "Mod", "game.gameevents" → "Game".
    // Used only for the duplicate-name disambiguation suffix; the full source string
    // is still preserved via [GameEventSource(...)] on the record.
    private static string PascalSource(string source)
    {
        int dot = source.IndexOf('.');
        string head = dot > 0 ? source.Substring(0, dot) : source;
        return NameHelpers.ToPascalCaseFromSnake(head);
    }
}
