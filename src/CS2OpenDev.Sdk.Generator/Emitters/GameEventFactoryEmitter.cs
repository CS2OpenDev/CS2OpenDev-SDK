#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

// Emits the typed-construction half of the game-event decoder: one factory per
// event record, plus a native-name → factory registry.
//
// These land in CS2OpenDev.Sdk.GameEvents rather than CS2OpenDev.Sdk, because a
// factory reads its values out of a decoded `CMsgSource1LegacyGameEvent` and
// that would drag Google.Protobuf onto every consumer who only wanted schema
// types. CS2OpenDev.Sdk ships zero package dependencies and stays that way.
//
// Generated rather than reflective: a demo can fire millions of events, and the
// alternative — walking [NativeName] / [GameEventFieldType] attributes per
// event — costs a reflection lookup per field on the hot path. The metadata is
// still emitted on the records for consumers who want to introspect; the
// factories just don't pay for it at runtime.
internal static class GameEventFactoryEmitter
{
    internal static void EmitAll(
        IGeneratorSink sink,
        GameEventsRoot root,
        SchemaRoot? schemaForStamp,
        GameEventOverrides? overrides = null)
    {
        if (root.Events.Length == 0)
        {
            return;
        }

        Dictionary<GameEventModel, string> csNames = GameEventsEmitter.AssignTypeNames(root);

        // Deterministic order so the emitted file is byte-stable across runs —
        // the CI regen gate diffs this output.
        List<GameEventModel> ordered = [.. root.Events];
        ordered.Sort((a, b) => StringComparer.Ordinal.Compare(csNames[a], csNames[b]));

        sink.AddSource("Generated/GameEventFactories", BuildFactories(ordered, csNames, schemaForStamp, overrides));
        sink.AddSource("Generated/GameEventRegistry", BuildRegistry(ordered, csNames, schemaForStamp));
    }

    private static string BuildFactories(
        List<GameEventModel> events,
        Dictionary<GameEventModel, string> csNames,
        SchemaRoot? schemaForStamp,
        GameEventOverrides? overrides)
    {
        StringBuilder sb = new(64 * 1024);
        AppendHeader(sb, "GameEventFactories", schemaForStamp);

        sb.AppendLine("using CS2OpenSchema.Events;");
        if (overrides is { Usings.Count: > 0 })
        {
            foreach (string ns in overrides.Usings)
            {
                sb.Append("using ").Append(ns).AppendLine(";");
            }
        }

        sb.AppendLine();
        sb.AppendLine("namespace CS2OpenDev.Sdk.GameEvents;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Typed constructors for every game-event record, one per event declared in");
        sb.AppendLine("///     the CS2 schema. Each reads its fields out of a decoded event by native key");
        sb.AppendLine("///     name; absent keys take the C# default rather than throwing, because the");
        sb.AppendLine("///     server is free to omit a key it has no value for.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class GameEventFactories");
        sb.AppendLine("{");

        bool first = true;
        foreach (GameEventModel ev in events)
        {
            if (!first)
            {
                sb.AppendLine();
            }

            first = false;

            string typeName = csNames[ev];
            sb.Append("    /// <summary>Builds a <see cref=\"").Append(typeName)
                .Append("\"/> from a decoded <c>").Append(NameHelpers.XmlEscape(ev.Name))
                .Append("</c> event.");
            if (ev.Supplemented)
            {
                // Same warning as the record's own remarks, repeated here because
                // this is the surface a decoder author reads. The key names below
                // come from wire observation, not from a schema declaration.
                sb.Append(" Curated supplement — key names observed, not declared.");
            }

            sb.AppendLine("</summary>");
            sb.Append("    public static ").Append(typeName).Append(' ').Append(typeName)
                .AppendLine("From(in GameEventReader reader) => new()");
            sb.AppendLine("    {");

            for (int i = 0; i < ev.Fields.Length; i++)
            {
                GameEventFieldModel field = ev.Fields[i];
                string prop = NameHelpers.ToPascalCaseFromSnake(field.Name);
                string accessor = ReaderCall(field.Type, field.Name, overrides);
                sb.Append("        ").Append(prop).Append(" = ").Append(accessor);
                sb.AppendLine(i == ev.Fields.Length - 1 ? "" : ",");
            }

            sb.AppendLine("    };");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Maps a KV1 schema type tag to the GameEventReader accessor that decodes it.
    //
    // Deliberately keyed off the *schema* tag rather than the wire `key_t.type`
    // field: the schema is the contract, and a server that writes a value into a
    // narrower slot than declared should still land in the declared C# property.
    // The reader handles that widening internally (see its integer fallback
    // chain); this table only decides which C# shape to ask for.
    //
    // Must stay in lockstep with GameEventTypeMapper, which decides the property
    // type. A mismatch here is a compile error in the generated factories rather
    // than a silent wrong value, which is the intended failure mode.
    private static string ReaderCall(string typeTag, string key, GameEventOverrides? overrides)
    {
        string k = "\"" + NameHelpers.EscAttrString(key) + "\"";

        // A consumer override replaces both the property type (via
        // GameEventTypeMapper) and the expression that fills it. Both come from
        // the same record, so they cannot drift apart.
        if (overrides?.For(typeTag) is { } o)
        {
            return o.Apply($"reader.Get{o.ReadAs}({k})");
        }

        return typeTag switch
        {
            "string" => $"reader.GetString({k})",
            "bool" => $"reader.GetBool({k})",
            "byte" => $"reader.GetByte({k})",
            "short" => $"reader.GetInt16({k})",
            "int" or "long" => $"reader.GetInt32({k})",
            "float" => $"reader.GetFloat({k})",
            "uint64" => $"reader.GetUInt64({k})",
            "ehandle" => $"reader.GetHandle({k})",
            // All three player-reference tags carry the raw userid the engine
            // emits; the distinction is preserved on the record via
            // [GameEventFieldType] so a consumer can resolve to the right entity
            // flavour without the decoder guessing for them.
            "player_controller" or "player_pawn" or "player_controller_and_pawn" => $"reader.GetInt32({k})",
            "local" => $"reader.GetBytes({k})",
            // Unknown tag — GameEventTypeMapper projects these to `object?`, so
            // hand back the raw key and let the consumer decide.
            _ => $"reader.GetRaw({k})"
        };
    }

    private static string BuildRegistry(
        List<GameEventModel> events,
        Dictionary<GameEventModel, string> csNames,
        SchemaRoot? schemaForStamp)
    {
        StringBuilder sb = new(32 * 1024);
        AppendHeader(sb, "GameEventRegistry", schemaForStamp);

        sb.AppendLine("namespace CS2OpenDev.Sdk.GameEvents;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Maps a native game-event name to the factory that materialises it.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         Demo decoding is name-driven: the wire carries an event name and a");
        sb.AppendLine("///         positional key list, so a dispatcher needs name → constructor.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("///     <para>");
        sb.AppendLine("///         Names are not unique. The same event can be declared in more than one");
        sb.AppendLine("///         <c>.gameevents</c> file with a different field set — <c>player_death</c>");
        sb.AppendLine("///         has 2 declarations and <c>round_end</c> has 3. <see cref=\"TryGetFactory\"/>");
        sb.AppendLine("///         resolves to the declaration CS2 actually fires (<c>mod</c> overrides");
        sb.AppendLine("///         <c>game</c> overrides <c>core</c>); <see cref=\"GetAllFactories\"/> returns");
        sb.AppendLine("///         every declaration for a name, so the older shapes stay reachable.");
        sb.AppendLine("///     </para>");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine("public static class GameEventRegistry");
        sb.AppendLine("{");

        // Preferred lookup: one entry per distinct native name.
        sb.AppendLine("    private static readonly Dictionary<string, GameEventFactory> Preferred =");
        sb.AppendLine("        new(StringComparer.Ordinal)");
        sb.AppendLine("        {");
        foreach (GameEventModel ev in events)
        {
            if (!GameEventsEmitter.IsPreferredForName(ev, csNames))
            {
                continue;
            }

            string typeName = csNames[ev];
            sb.Append("            [\"").Append(NameHelpers.EscAttrString(ev.Name)).Append("\"] = ")
                .Append("static (in GameEventReader r) => GameEventFactories.").Append(typeName)
                .AppendLine("From(r),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();

        // Every declaration, including the non-preferred ones.
        sb.AppendLine("    private static readonly Dictionary<string, GameEventDeclaration[]> AllByName =");
        sb.AppendLine("        new(StringComparer.Ordinal)");
        sb.AppendLine("        {");

        foreach (IGrouping<string, GameEventModel> group in events
                     .GroupBy(e => e.Name, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("            [\"").Append(NameHelpers.EscAttrString(group.Key)).AppendLine("\"] =");
            sb.AppendLine("            [");
            foreach (GameEventModel ev in group.OrderBy(e => csNames[e], StringComparer.Ordinal))
            {
                string typeName = csNames[ev];
                sb.Append("                new(\"").Append(NameHelpers.EscAttrString(ev.Source))
                    .Append("\", typeof(global::CS2OpenSchema.Events.").Append(typeName).Append("), ")
                    .Append("static (in GameEventReader r) => GameEventFactories.").Append(typeName)
                    .AppendLine("From(r)),");
            }

            sb.AppendLine("            ],");
        }

        sb.AppendLine("        };");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>Every native event name the schema declares.</summary>");
        sb.AppendLine("    public static IReadOnlyCollection<string> EventNames => AllByName.Keys;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Number of distinct native event names.</summary>");
        sb.Append("    public const int NameCount = ").Append(
            events.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count()).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Number of event declarations, counting duplicates across source files.</summary>");
        sb.Append("    public const int DeclarationCount = ").Append(events.Count).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Resolves the factory for the declaration CS2 fires for this name.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static bool TryGetFactory(string nativeName, out GameEventFactory factory) =>");
        sb.AppendLine("        Preferred.TryGetValue(nativeName, out factory!);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Every declaration of <paramref name=\"nativeName\"/>, highest-priority");
        sb.AppendLine("    ///     source first. Empty when the name is unknown.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IReadOnlyList<GameEventDeclaration> GetAllFactories(string nativeName) =>");
        sb.AppendLine("        AllByName.TryGetValue(nativeName, out GameEventDeclaration[]? all) ? all : [];");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, string what, SchemaRoot? schemaForStamp)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// CS2OpenDev.Sdk.GameEvents — " + what);
        sb.AppendLine("// Generated from upstream gameevents_schema.json. Regenerate with cs2-sdk-exporter.");
        sb.AppendLine("// Do not edit this file directly.");
        sb.AppendLine();
        if (schemaForStamp is not null)
        {
            int before = sb.Length;
            ModuleEmitter.AppendSchemaStamp(sb, schemaForStamp);
            if (sb.Length > before)
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }
}
