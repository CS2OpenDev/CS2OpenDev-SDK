#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// ── Top-level ────────────────────────────────────────────────────────────────

internal record SchemaRoot(
    ClassModel[] Classes,
    EnumModel[] Enums,
    // Source-traceability fields (F3 / ME-3) from the DumpSource2 dump:
    // numeric revision, plus the dump's wall-clock date/time. All optional —
    // older dumps and handwritten test fixtures may omit them.
    long? Revision = null,
    string? VersionDate = null,
    string? VersionTime = null);

// ── Classes ──────────────────────────────────────────────────────────────────

internal record ClassModel(
    string Name,
    string Module,
    int Size,
    byte Alignment, // 0 when absent from JSON (schema 1.x omits this; 2.0 carries it)
    // False when absent. Schema 1.x omits it entirely; schema 2.0 exposes the
    // runtime bitfield instead, where `flags & (1 << 1)` is
    // SCHEMA_CF1_IS_ABSTRACT — confirmed upstream against the pinned hl2sdk
    // (CS2OpenDev-SchemaTracker#2), and matching all three known-abstract
    // exemplars. Wire it up in the 2.0 migration; it restores ~142 abstract
    // projections that have been flat since the old pipeline dropped the field.
    bool IsAbstract,
    ParentModel[] Parents,
    FieldModel[] Fields,
    MetadataEntry[] Metadata, // class-level metadata (MGetKV3ClassDefaults, etc.)
    Annotations? Annotations = null);

internal record ParentModel(string Name, string Module, uint Offset);

internal record FieldModel(
    string Name,
    int Offset,
    TypeModel Type,
    MetadataEntry[] Metadata,
    Annotations? Annotations = null);

// Community-curated enrichment overlayed on classes / fields / enums / members.
// All three fields optional; absent annotation block ⇒ null Annotations record.
internal record Annotations(string? Description, string? Notes, string? Warning);

// ── Type discriminated union ─────────────────────────────────────────────────

internal abstract record TypeModel;

internal record BuiltinType(string Name) : TypeModel;

internal record PtrType(TypeModel Inner) : TypeModel; // nullable: true is implicit for all ptrs

internal record FixedArrayType(int Count, TypeModel Inner) : TypeModel;

internal record AtomicType(
    string Name,
    string? HandleKind, // "entity" | "weak" | "strong" | null
    bool Nullable,
    TypeModel? Inner,
    TypeModel? Inner2 // non-null only for TT (two-arg) types
) : TypeModel;

internal record DeclaredClassType(string Name, string Module) : TypeModel;

internal record DeclaredEnumType(string Name, string Module) : TypeModel;

internal record BitfieldType(int Count) : TypeModel;

internal record UnknownType(string? Category) : TypeModel; // fallback; Category = raw category string

// ── Enums ────────────────────────────────────────────────────────────────────

internal record EnumModel(
    string Name,
    string Module,
    string? Alignment, // "uint8_t" | "uint16_t" | "uint32_t" | "uint64_t" | null
    int? StorageSize, // 1 | 2 | 4 | 8 | null. Derived from Alignment when JSON omits it.
    // Always false against real upstream input, and permanently so — this is
    // not a gap waiting to be filled.
    //
    // Schema 1.x dropped the old `flags: true` marker. Schema 2.0 exposes the
    // runtime bitfield, but SchemaEnumFlags_t declares exactly three bits —
    // IS_REGISTERED (1), MODULE_LOCAL_TYPE_SCOPE (2), GLOBAL_TYPE_SCOPE (4) —
    // and none of them marks a flag-set (CS2OpenDev-SchemaTracker#2).
    //
    // We measured every bit against a power-of-two-membership oracle before
    // asking: bit 1 is set on all 610 enums (it is IS_REGISTERED), and the best
    // remaining candidate, bit 16, is unnamed in the SDK and scored 8 false
    // positives against 14 false negatives. Deriving [Flags] from the bitfield
    // would be pattern-matching noise. Only a consumer overlay can reintroduce
    // this.
    bool IsFlags,
    MemberModel[] Members,
    MetadataEntry[] Metadata,
    Annotations? Annotations = null);

internal record MemberModel(
    string Name,
    long Value,
    MetadataEntry[] Metadata,
    Annotations? Annotations = null);

internal record MetadataEntry(string Name, string? Value);

// ── JSON parser ──────────────────────────────────────────────────────────────

internal static class SchemaModel
{
    internal static SchemaRoot Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });
        JsonElement root = doc.RootElement;

        SchemaFormatGuard.ThrowIfUnsupported(root);

        ClassModel[] classes = root.TryGetProperty("classes", out JsonElement cEl)
            ? ParseClasses(cEl)
            : [];

        EnumModel[] enums = root.TryGetProperty("enums", out JsonElement eEl)
            ? ParseEnums(eEl)
            : [];

        // 1.x: `revision` is the numeric mirror id. 2.0: `build_id` is the Steam
        // CS2 game build and `revision` was repurposed as a walker-identity
        // string ("hl2sdk-cs2/5f891c90…/v1/…"). Take build_id first, and require
        // a Number from `revision` so the 2.0 string can never land here — it
        // reaches the package version as SemVer 2 build metadata, where slashes
        // are not legal. Same precedence the CI metadata action uses.
        long? revision = root.TryGetProperty("build_id", out JsonElement bEl) && bEl.ValueKind == JsonValueKind.Number
            ? bEl.GetInt64()
            : root.TryGetProperty("revision", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.Number
                ? rEl.GetInt64()
                : null;
        string? versionDate = root.TryGetProperty("version_date", out JsonElement vdEl) && vdEl.ValueKind == JsonValueKind.String
            ? vdEl.GetString()
            : null;
        string? versionTime = root.TryGetProperty("version_time", out JsonElement vtEl) && vtEl.ValueKind == JsonValueKind.String
            ? vtEl.GetString()
            : null;

        return new SchemaRoot(classes, enums, revision, versionDate, versionTime);
    }

    private static ClassModel ParseClass(JsonElement e)
    {
        string name = Str(e, "name");
        // The namespace key. In 1.x `module` is the project (`client`, `server`,
        // `particles`); in 2.0 `module` became the binary that registered the
        // type (`server.dll`, `!GlobalTypes`) and `projectName` took over the
        // project role. Read projectName first so both shapes produce the same
        // namespace layout — this is the whole reason class records survive the
        // format change without a namespace break.
        //
        // Enum records in 2.0 carry no projectName at all, which is what blocks
        // the submodule bump rather than this parser
        // (CS2OpenDev-SchemaTracker#1). See ParseEnum.
        string module = Str(e, "projectName") is { Length: > 0 } project
            ? project
            : Str(e, "module");
        int size = NumInt(e, "size");
        // Schema 1.x omits class-level alignment; 2.0 carries it. Absence is 0
        // so emitters see consistent input either way.
        byte alignment = e.TryGetProperty("alignment", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.Number
            ? (byte)aEl.GetInt32()
            : (byte)0;
        // 1.x carries an `abstract` boolean. 2.0 dropped it and exposes the
        // runtime bitfield instead, where bit 1 is SCHEMA_CF1_IS_ABSTRACT —
        // confirmed upstream against the pinned hl2sdk
        // (CS2OpenDev-SchemaTracker#2). Prefer the explicit boolean where it
        // exists so 1.x and hand-written fixtures are unaffected.
        bool isAbstract = e.TryGetProperty("abstract", out JsonElement abEl)
                          && abEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? abEl.GetBoolean()
            : (Num(e, "flags") & (1 << 1)) != 0;

        ParentModel[] parents = e.TryGetProperty("parents", out JsonElement pEl)
            ? ParseParents(pEl)
            : [];

        FieldModel[] fields = e.TryGetProperty("fields", out JsonElement fEl)
            ? ParseFields(fEl)
            : [];

        MetadataEntry[] metadata = e.TryGetProperty("metadata", out JsonElement mEl)
            ? ParseMetadata(mEl)
            : [];

        Annotations? annotations = ParseAnnotations(e);

        return new ClassModel(name, module, size, alignment, isAbstract, parents, fields, metadata, annotations);
    }

    // ── Classes ──

    private static ClassModel[] ParseClasses(JsonElement el)
    {
        List<ClassModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            list.Add(ParseClass(item));
        }

        return list.ToArray();
    }

    private static EnumModel ParseEnum(JsonElement e)
    {
        string name = Str(e, "name");
        // Same key preference as classes, and it currently never fires: no 2.0
        // enum record carries projectName (0 of 610, measured against Docs
        // 3053793), so every enum falls back to `module` — which in 2.0 is the
        // binary, and reads `!GlobalTypes` for 591 of them.
        //
        // That is why the submodule pin has not moved. Parsing 2.0 is correct
        // and complete; regenerating *from* 2.0 would collapse 591 enums into
        // one namespace and break every consumer's `using` lines. The preference
        // is written now so the bump is a one-line pin change once
        // CS2OpenDev-SchemaTracker#1 ships in an artifact.
        string module = Str(e, "projectName") is { Length: > 0 } project
            ? project
            : Str(e, "module");
        string? alignment = e.TryGetProperty("alignment", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.String
            ? aEl.GetString()
            : null;
        // Upstream cs2_schema.json no longer carries `storage_size` — it's fully
        // recoverable from the `alignment` storage-type string. Honour the
        // explicit field if present (old schemas.json + test fixtures), else
        // derive from the alignment type so emitter logic is unchanged.
        int? storageSize = e.TryGetProperty("storage_size", out JsonElement ssEl) && ssEl.ValueKind == JsonValueKind.Number
            ? ssEl.GetInt32()
            : DeriveStorageSize(alignment);
        // 1.x wrote `flags: true` to mark a flag-set. 2.0 reuses the same key for
        // the runtime bitfield, an integer — so an unguarded GetBoolean() throws
        // on every one of the 610 enum records rather than on some edge case.
        //
        // The bitfield deliberately does not feed IsFlags. SchemaEnumFlags_t
        // declares three bits — IS_REGISTERED, MODULE_LOCAL_TYPE_SCOPE,
        // GLOBAL_TYPE_SCOPE — and none marks a flag-set
        // (CS2OpenDev-SchemaTracker#2), so there is nothing here to read. See
        // the note on EnumModel.IsFlags.
        bool isFlags = e.TryGetProperty("flags", out JsonElement fEl)
                       && fEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                       && fEl.GetBoolean();
        MemberModel[] members = e.TryGetProperty("members", out JsonElement mEl)
            ? ParseMembers(mEl)
            : [];

        MetadataEntry[] metadata = e.TryGetProperty("metadata", out JsonElement mdEl)
            ? ParseMetadata(mdEl)
            : [];

        Annotations? annotations = ParseAnnotations(e);

        return new EnumModel(name, module, alignment, storageSize, isFlags, members, metadata, annotations);
    }

    private static int? DeriveStorageSize(string? alignment) =>
        alignment switch
        {
            "uint8_t" or "int8_t" or "char" or "uchar" or "bool" => 1,
            "uint16_t" or "int16_t" or "short" or "ushort" => 2,
            "uint32_t" or "int32_t" or "int" or "uint" => 4,
            "uint64_t" or "int64_t" or "long" or "ulong" => 8,
            _ => null
        };

    // Per cs2_schema.json format reference: `annotations` is an optional object with
    // `description`, `notes`, and `warning` string fields. Any subset may appear.
    // Returns null when the key is absent or carries no recognised fields so the
    // emitters can cheaply skip output for un-annotated entities.
    private static Annotations? ParseAnnotations(JsonElement e)
    {
        if (!e.TryGetProperty("annotations", out JsonElement aEl) || aEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? description = aEl.TryGetProperty("description", out JsonElement dEl) && dEl.ValueKind == JsonValueKind.String
            ? dEl.GetString()
            : null;
        string? notes = aEl.TryGetProperty("notes", out JsonElement nEl) && nEl.ValueKind == JsonValueKind.String
            ? nEl.GetString()
            : null;
        string? warning = aEl.TryGetProperty("warning", out JsonElement wEl) && wEl.ValueKind == JsonValueKind.String
            ? wEl.GetString()
            : null;

        if (description is null && notes is null && warning is null)
        {
            return null;
        }

        return new Annotations(description, notes, warning);
    }

    // ── Enums ──

    private static EnumModel[] ParseEnums(JsonElement el)
    {
        List<EnumModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            list.Add(ParseEnum(item));
        }

        return list.ToArray();
    }

    private static FieldModel[] ParseFields(JsonElement el)
    {
        List<FieldModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            int offset = NumInt(item, "offset");
            TypeModel type = item.TryGetProperty("type", out JsonElement tEl)
                ? ParseType(tEl)
                : new UnknownType(null);
            MetadataEntry[] metadata = item.TryGetProperty("metadata", out JsonElement mEl)
                ? ParseMetadata(mEl)
                : [];
            Annotations? annotations = ParseAnnotations(item);
            list.Add(new FieldModel(name, offset, type, metadata, annotations));
        }

        return list.ToArray();
    }

    private static MemberModel[] ParseMembers(JsonElement el)
    {
        List<MemberModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            long value = Num(item, "value");
            MetadataEntry[] metadata = item.TryGetProperty("metadata", out JsonElement mEl)
                ? ParseMetadata(mEl)
                : [];
            Annotations? annotations = ParseAnnotations(item);
            list.Add(new MemberModel(name, value, metadata, annotations));
        }

        return list.ToArray();
    }

    private static MetadataEntry[] ParseMetadata(JsonElement el)
    {
        List<MetadataEntry> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            string? value = item.TryGetProperty("value", out JsonElement vEl) && vEl.ValueKind == JsonValueKind.String
                ? vEl.GetString()
                : null;
            list.Add(new MetadataEntry(name, value));
        }

        return list.ToArray();
    }

    private static ParentModel[] ParseParents(JsonElement el)
    {
        List<ParentModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            string module = Str(item, "module");
            uint offset = (uint)Num(item, "offset");
            list.Add(new ParentModel(name, module, offset));
        }

        return list.ToArray();
    }

    private static TypeModel ParseType(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return new UnknownType(null);
        }

        // 1.x writes the discriminator lowercase ("builtin"), 2.0 uppercase
        // ("BUILTIN"). Normalise rather than adding arms, because getting this
        // wrong does not throw — every type falls through to UnknownType, the
        // emitters produce `object /* BUILTIN */` and empty stub classes, and
        // the regen succeeds. A silent 100% degradation is worse than a crash,
        // so the tests assert zero unknown categories on real input.
        string rawCategory = Str(e, "category");
        string category = rawCategory.ToLowerInvariant();
        switch (category)
        {
            case "builtin":
                return new BuiltinType(Str(e, "name"));

            case "ptr":
            {
                TypeModel inner = e.TryGetProperty("inner", out JsonElement iEl)
                    ? ParseType(iEl)
                    : new UnknownType("ptr-inner");
                return new PtrType(inner);
            }
            case "fixed_array":
            {
                int count = NumInt(e, "count");
                TypeModel inner = e.TryGetProperty("inner", out JsonElement iEl)
                    ? ParseType(iEl)
                    : new UnknownType("fixed_array-inner");
                return new FixedArrayType(count, inner);
            }
            case "atomic":
            {
                string name = Str(e, "name");
                string? handleKind = e.TryGetProperty("handle_kind", out JsonElement hkEl) ? hkEl.GetString() : null;
                bool nullable = e.TryGetProperty("nullable", out JsonElement nbEl) && nbEl.GetBoolean();
                TypeModel? inner = e.TryGetProperty("inner", out JsonElement iEl) ? ParseType(iEl) : null;
                TypeModel? inner2 = e.TryGetProperty("inner2", out JsonElement i2El) ? ParseType(i2El) : null;
                return new AtomicType(name, handleKind, nullable, inner, inner2);
            }
            case "declared_class":
                return new DeclaredClassType(Str(e, "name"), Str(e, "module"));

            case "declared_enum":
                return new DeclaredEnumType(Str(e, "name"), Str(e, "module"));

            case "bitfield":
            {
                int count = NumInt(e, "count");
                return new BitfieldType(count);
            }
            default:
                return new UnknownType(rawCategory);
        }
    }

    // ── Helpers ──

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    // Schema 1.x writes sizes, offsets, counts and enum member values as JSON
    // numbers; 2.0 writes the same values as quoted strings ("24", "0"). Both
    // are read here rather than at each call site, because the failure mode of
    // getting it wrong is a throw from deep inside a field parse whose message
    // names neither the field nor the record — that is exactly how the 1.x → 2.0
    // break originally presented ("requires an element of type 'Number', but the
    // target element has type 'String'").
    //
    // A malformed string yields the fallback rather than throwing: these feed
    // struct layout metadata, not correctness of the emitted type, and a single
    // unparseable offset should not take down a 3,769-class regen.
    private static long Num(JsonElement e, string key, long fallback = 0)
    {
        if (!e.TryGetProperty(key, out JsonElement el))
        {
            return fallback;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt64(out long n) ? n : fallback,
            JsonValueKind.String => long.TryParse(el.GetString(), out long s) ? s : fallback,
            _ => fallback
        };
    }

    private static int NumInt(JsonElement e, string key, int fallback = 0)
    {
        long v = Num(e, key, fallback);
        return v is >= int.MinValue and <= int.MaxValue ? (int)v : fallback;
    }
}
