#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// ── Top-level ────────────────────────────────────────────────────────────────

internal record SchemaRoot(
    ClassModel[] Classes,
    EnumModel[] Enums,
    // Source-traceability fields (F3 / ME-3): the CS2 build id from the
    // header's `build_id`, plus the extraction's wall-clock date/time. All
    // optional — hand-written test fixtures omit them.
    long? Revision = null,
    string? VersionDate = null,
    string? VersionTime = null);

// ── Classes ──────────────────────────────────────────────────────────────────

internal record ClassModel(
    string Name,
    string Module,
    int Size,
    byte Alignment, // 0 when absent from JSON
    // Read from `flags & (1 << 1)` — SCHEMA_CF1_IS_ABSTRACT, confirmed upstream
    // against the pinned hl2sdk (CS2OpenDev-SchemaTracker#2). Restores 142
    // abstract projections that were flat while the old pipeline dropped the
    // field.
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
    TypeModel? Inner2, // non-null only for TT (two-arg) types
    // The engine's own SchemaAtomicCategory_t discriminator, verbatim:
    // ATOMIC_PLAIN / ATOMIC_T / ATOMIC_COLLECTION_OF_T / ATOMIC_TT / ATOMIC_I.
    // Present from schema_format_version 2.1 (SchemaTracker 0.9.0 walkers) and
    // null before it, so every use has to tolerate absence. Trailing with a
    // default so the existing construction sites and fixtures are unaffected.
    //
    // Read for drift detection only (CS2_GEN_015), NOT to pick a projection. The
    // discriminator describes C++ template arity; the projection is a C# design
    // decision, and the two deliberately disagree in places — CResourceArray and
    // CRelativeArray are ATOMIC_T upstream and collections here, CUtlStringMap is
    // ATOMIC_T upstream and a string-keyed map here. Those are not bugs to
    // reconcile; see TypeMapper.
    string? AtomicCategory = null
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
    int? StorageSize, // 1 | 2 | 4 | 8 | null. Derived from Alignment; upstream carries no such field.
    // Always false, and permanently so — this is not a gap waiting to be
    // filled.
    //
    // The header exposes a runtime bitfield on `flags`, but SchemaEnumFlags_t
    // declares exactly three bits — IS_REGISTERED (1), MODULE_LOCAL_TYPE_SCOPE
    // (2), GLOBAL_TYPE_SCOPE (4) — and none marks a flag-set
    // (CS2OpenDev-SchemaTracker#2).
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

        // `build_id` is the Steam CS2 game build. The header also carries a
        // `revision`, but it is a walker-identity string
        // ("hl2sdk-cs2/5f891c90…/v1/…") and must never be read here: this value
        // reaches the package version as SemVer 2 build metadata, where slashes
        // are not legal. Same key the CI metadata action reads.
        long? revision = root.TryGetProperty("build_id", out JsonElement bEl) && bEl.ValueKind == JsonValueKind.Number
            ? bEl.GetInt64()
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
        // The namespace key. `module` is the binary that registered the type
        // (`server.dll`, `!GlobalTypes`); `projectName` is the project
        // (`client`, `server`, `particles`) and is what the namespace layout is
        // built from. Falling back to `module` is not a compatibility shim —
        // enum records still ship without `projectName`
        // (CS2OpenDev-SchemaTracker#1), and the fallback is what makes that
        // degrade into one wrong namespace instead of an empty one.
        string module = Str(e, "projectName") is { Length: > 0 } project
            ? project
            : Str(e, "module");
        int size = NumInt(e, "size");
        byte alignment = e.TryGetProperty("alignment", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.Number
            ? (byte)aEl.GetInt32()
            : (byte)0;
        // Bit 1 of the class flags bitfield is SCHEMA_CF1_IS_ABSTRACT, confirmed
        // upstream against the pinned hl2sdk (CS2OpenDev-SchemaTracker#2).
        bool isAbstract = (Num(e, "flags") & (1 << 1)) != 0;

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
        // Same key preference as classes, and it does not fire yet: no enum
        // record carries projectName (0 of 610, measured against Docs 3053793),
        // so every enum falls back to `module` — the binary — which reads
        // `!GlobalTypes` for 591 of them and collapses them into one namespace.
        //
        // That is the release blocker, not a parser gap
        // (CS2OpenDev-SchemaTracker#1, fixed in their main at ba3bd0cf but not
        // yet in a published artifact). When it lands this line starts
        // resolving and the namespaces spread back out with no code change.
        string module = Str(e, "projectName") is { Length: > 0 } project
            ? project
            : Str(e, "module");
        string? alignment = e.TryGetProperty("alignment", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.String
            ? aEl.GetString()
            : null;
        // Upstream carries no `storage_size`; it is fully recoverable from the
        // `alignment` storage-type string.
        int? storageSize = DeriveStorageSize(alignment);
        // Always false, and deliberately not read from `flags`. That key is the
        // runtime bitfield, and SchemaEnumFlags_t declares three bits —
        // IS_REGISTERED, MODULE_LOCAL_TYPE_SCOPE, GLOBAL_TYPE_SCOPE — none of
        // which marks a flag-set (CS2OpenDev-SchemaTracker#2). See the note on
        // EnumModel.IsFlags for why deriving it anyway would be noise.
        const bool isFlags = false;
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

        // Upstream writes the discriminator uppercase ("BUILTIN"). Lowered
        // rather than matched uppercase so a case change on either side is a
        // non-event, because getting this wrong does not throw: every type
        // falls through to UnknownType, the emitters produce
        // `object /* BUILTIN */` and empty stub classes, and the regen
        // succeeds. A silent 100% degradation is worse than a crash — which is
        // how the 1.x → 2.0 case flip actually presented — so the tests assert
        // zero unknown categories on real input.
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
                string? atomicCategory =
                    e.TryGetProperty("atomicCategory", out JsonElement acEl)
                    && acEl.ValueKind == JsonValueKind.String
                        ? acEl.GetString()
                        : null;
                return new AtomicType(name, handleKind, nullable, inner, inner2, atomicCategory);
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

    // Upstream is not internally consistent about how it encodes integers: class
    // `size` and field `offset` are quoted strings ("24", "0") while class
    // `alignment` and enum `size` are JSON numbers. Both encodings are read here
    // rather than at each call site, because the failure mode of getting it
    // wrong is a throw from deep inside a field parse whose message names
    // neither the field nor the record — that is exactly how the 1.x → 2.0 break
    // originally presented ("requires an element of type 'Number', but the
    // target element has type 'String').
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
