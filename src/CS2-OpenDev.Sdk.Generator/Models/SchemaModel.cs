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
    byte Alignment, // 0 when absent from JSON
    bool IsAbstract,
    ParentModel[] Parents,
    FieldModel[] Fields);

internal record ParentModel(string Name, string Module, uint Offset);

internal record FieldModel(string Name, int Offset, TypeModel Type, MetadataEntry[] Metadata);

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
    int? StorageSize, // 1 | 2 | 4 | 8 | null
    bool IsFlags,
    MemberModel[] Members);

internal record MemberModel(string Name, long Value, MetadataEntry[] Metadata);

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

        ClassModel[] classes = root.TryGetProperty("classes", out JsonElement cEl)
            ? ParseClasses(cEl)
            : [];

        EnumModel[] enums = root.TryGetProperty("enums", out JsonElement eEl)
            ? ParseEnums(eEl)
            : [];

        long? revision = root.TryGetProperty("revision", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.Number
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
        string module = Str(e, "module");
        int size = e.TryGetProperty("size", out JsonElement sEl) ? sEl.GetInt32() : 0;
        byte alignment = e.TryGetProperty("alignment", out JsonElement aEl) ? (byte)aEl.GetInt32() : (byte)0;
        bool isAbstract = e.TryGetProperty("abstract", out JsonElement abEl) && abEl.GetBoolean();

        ParentModel[] parents = e.TryGetProperty("parents", out JsonElement pEl)
            ? ParseParents(pEl)
            : [];

        FieldModel[] fields = e.TryGetProperty("fields", out JsonElement fEl)
            ? ParseFields(fEl)
            : [];

        return new ClassModel(name, module, size, alignment, isAbstract, parents, fields);
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
        string module = Str(e, "module");
        string? alignment = e.TryGetProperty("alignment", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.String
            ? aEl.GetString()
            : null;
        int? storageSize = e.TryGetProperty("storage_size", out JsonElement ssEl) && ssEl.ValueKind == JsonValueKind.Number
            ? ssEl.GetInt32()
            : (int?)null;
        bool isFlags = e.TryGetProperty("flags", out JsonElement fEl) && fEl.GetBoolean();
        MemberModel[] members = e.TryGetProperty("members", out JsonElement mEl)
            ? ParseMembers(mEl)
            : [];

        return new EnumModel(name, module, alignment, storageSize, isFlags, members);
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
            int offset = item.TryGetProperty("offset", out JsonElement oEl) ? oEl.GetInt32() : 0;
            TypeModel type = item.TryGetProperty("type", out JsonElement tEl)
                ? ParseType(tEl)
                : new UnknownType(null);
            MetadataEntry[] metadata = item.TryGetProperty("metadata", out JsonElement mEl)
                ? ParseMetadata(mEl)
                : [];
            list.Add(new FieldModel(name, offset, type, metadata));
        }

        return list.ToArray();
    }

    private static MemberModel[] ParseMembers(JsonElement el)
    {
        List<MemberModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            long value = item.TryGetProperty("value", out JsonElement vEl) ? vEl.GetInt64() : 0L;
            MetadataEntry[] metadata = item.TryGetProperty("metadata", out JsonElement mEl)
                ? ParseMetadata(mEl)
                : [];
            list.Add(new MemberModel(name, value, metadata));
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
            uint offset = item.TryGetProperty("offset", out JsonElement oEl)
                ? (uint)oEl.GetInt64()
                : 0u;
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

        string category = Str(e, "category");
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
                int count = e.TryGetProperty("count", out JsonElement cEl) ? cEl.GetInt32() : 0;
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
                int count = e.TryGetProperty("count", out JsonElement cEl) ? cEl.GetInt32() : 0;
                return new BitfieldType(count);
            }
            default:
                return new UnknownType(category);
        }
    }

    // ── Helpers ──

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";
}
