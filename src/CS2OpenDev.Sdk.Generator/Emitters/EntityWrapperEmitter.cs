#region

using System.Globalization;
using System.Text;
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
// Ordinals are the ordinal-sort position of a class's canonical Lens paths, which
// is exactly how the binding's CanonicalPaths array is built here — the two are
// emitted from the same state in the same pass, so they agree by construction
// rather than by convention. They are private, because they are not API: a rename
// re-sorts the space and renumbers everything after it, which is safe only
// because the wrapper and its manifest move together.
internal static class EntityWrapperEmitter
{
    // Fields where a received 0 is a meaningful value rather than a harmless
    // default, so the property is nullable and absence is null.
    //
    // Curation, not inference: no schema fact distinguishes "0 is a sentinel"
    // from "0 is a state". m_lifeState's 0 is LIFE_ALIVE, so a 0-default getter
    // makes a pawn that never transmitted the field indistinguishable from a
    // live one.
    //
    // Growing this set is a BREAKING change for consumers typed against the
    // non-nullable property — DemoViewer.NET has the four m_pInGameMoneyServices
    // money fields staged and commented out for exactly that reason. Treat an
    // addition as a major with a deprecation cycle, never a silent flip.
    private static readonly HashSet<(string Class, string Field)> SeenAwareFields = new()
    {
        ("CCSPlayerPawn", "m_lifeState")
    };

    // Abstract bases that are curated so the type hierarchy is complete — they
    // are usable as base types and as Resolve<T> targets — but that never appear
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
        IReadOnlyDictionary<string, LensResolvedClass> resolution,
        Func<string, int?> declaredClassWidth,
        string lensHash,
        string schemaBuild,
        string ns)
    {
        List<BindingPlan> plans = [];

        foreach ((string engineClass, LensClassState cls) in state.Classes)
        {
            if (!resolution.TryGetValue(engineClass, out LensResolvedClass? resolved))
            {
                continue;
            }

            BindingPlan plan = Plan(engineClass, cls, resolved, declaredClassWidth, state);
            plans.Add(plan);
            sink.AddSource(plan.NetName, EmitWrapper(plan, ns));
        }

        sink.AddSource("EntityWrapperRegistry", EmitRegistry(plans, lensHash, schemaBuild, ns));
    }

    // ── Planning ─────────────────────────────────────────────────────────────

    private static BindingPlan Plan(
        string engineClass,
        LensClassState cls,
        LensResolvedClass resolved,
        Func<string, int?> declaredClassWidth,
        LensState state)
    {
        List<FieldPlan> fields = [];
        int ordinal = 0;

        // Fields is a SortedDictionary keyed StringComparer.Ordinal, so iteration
        // order IS the ordinal space. Stated rather than relied on silently,
        // because the manifest's CanonicalPaths is built from the same walk.
        foreach ((string canonical, LensFieldEntry entry) in cls.Fields)
        {
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
                SeenAwareFields.Contains((engineClass, canonical))));
        }

        // Identity entries (canonical → canonical) are a lookup convenience in
        // the replay model and must NOT reach the manifest: an alias whose key is
        // a live canonical path would shadow the field, and BindingConformance
        // rightly rejects it.
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach ((string alias, string target) in cls.Aliases)
        {
            if (!string.Equals(alias, target, StringComparison.Ordinal)
                && cls.Fields.ContainsKey(target))
            {
                aliases[alias] = target;
            }
        }

        return new BindingPlan(
            engineClass,
            cls.NetName,
            fields,
            aliases,
            !NoFactoryRegistration.Contains(engineClass),
            state);
    }

    // The whole type dispatch. Every branch names the reader member the emitted
    // property calls, which is the decision the contract asks the emitter to make
    // rather than asking a runtime to infer.
    private static Reads Dispatch(string schemaType, int? widthBytes) => schemaType switch
    {
        "bool" => new Reads("bool", "TryReadBool", "false", "bool"),
        "float32" or "GameTime_t" => new Reads("float", "TryReadSingle", "0f", "float"),
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
        sb.AppendLine($"public sealed class {plan.NetName}(IEntityFieldReader reader, IEntityWorld world)");
        sb.AppendLine("    : EntityWrapper(reader, world)");
        sb.AppendLine("{");

        foreach (FieldPlan f in plan.Fields)
        {
            EmitProperty(sb, f, plan);
        }

        if (plan.Fields.Count > 0)
        {
            sb.AppendLine("    // Ordinals into the binding's CanonicalPaths. Private because they are");
            sb.AppendLine("    // not API: a rename re-sorts the space and renumbers everything after it.");
            sb.AppendLine("    private static class Ord");
            sb.AppendLine("    {");
            foreach (FieldPlan f in plan.Fields)
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
        // demo-facing names and .NET names — the thing someone reading a wire
        // dump needs in order to find this property.
        sb.AppendLine($"    /// <summary><c>{NameHelpers.XmlEscape(f.Canonical)}</c> ({NameHelpers.XmlEscape(f.SchemaType)}).</summary>");

        if (f.SeenAware)
        {
            sb.AppendLine("    /// <remarks>");
            sb.AppendLine("    ///     Nullable because a received <c>0</c> is a meaningful value for this field,");
            sb.AppendLine("    ///     so absence cannot be reported as zero. <see langword=\"null\"/> means the");
            sb.AppendLine("    ///     field has never been received on the wire.");
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
            && ResolvedTarget(f, plan) is { } target
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

    private static string? ResolvedTarget(FieldPlan f, BindingPlan plan)
    {
        int lt = f.SchemaType.IndexOf('<');
        int gt = f.SchemaType.LastIndexOf('>');
        if (lt < 0 || gt <= lt)
        {
            return null;
        }

        string target = f.SchemaType[(lt + 1)..gt].Trim();
        return plan.State.Classes.TryGetValue(target, out LensClassState? cls) ? cls.NetName : null;
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
        sb.AppendLine("///         these wrappers were generated from. A runtime that also loads the Schema Lens");
        sb.AppendLine("///         should compare its own hash against this one at startup — a mismatch means the");
        sb.AppendLine("///         curation moved without the wrappers being regenerated, which is skew that");
        sb.AppendLine("///         otherwise surfaces as fields silently reading absent.");
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

    private sealed record BindingPlan(
        string EngineClass,
        string NetName,
        IReadOnlyList<FieldPlan> Fields,
        IReadOnlyDictionary<string, string> Aliases,
        bool Registers,
        LensState State);
}
