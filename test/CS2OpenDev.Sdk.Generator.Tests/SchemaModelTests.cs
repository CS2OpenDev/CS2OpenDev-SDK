using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — pure unit tests for SchemaModel.Parse.
//
// Each test feeds a minimal JSON fragment and asserts the parsed model shape.
// Use raw string literals (C# 11) for readability — schema fragments stay
// inline with the test that exercises them.

public class SchemaModelTests
{
    // ── Type-category dispatch ───────────────────────────────────────────────

    /// <summary>Dispatches <c>category: "builtin"</c> JSON into a <see cref="BuiltinType"/> carrying the primitive name.</summary>
    [Test]
    public async Task Parse_BuiltinType()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_x", "offset": 0,
                  "type": { "category": "builtin", "name": "int32" } }]
              }]
            }
            """);

        FieldModel field = root.Classes[0].Fields[0];
        await Assert.That(field.Type).IsTypeOf<BuiltinType>();
        await Assert.That(((BuiltinType)field.Type).Name).IsEqualTo("int32");
    }

    /// <summary>Dispatches <c>category: "ptr"</c> into a <see cref="PtrType"/> with its nested <c>inner</c> recursively parsed.</summary>
    [Test]
    public async Task Parse_PtrType()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_p", "offset": 0,
                  "type": { "category": "ptr",
                            "inner": { "category": "builtin", "name": "char" } } }]
              }]
            }
            """);

        PtrType ptr = (PtrType)root.Classes[0].Fields[0].Type;
        await Assert.That(ptr.Inner).IsTypeOf<BuiltinType>();
        await Assert.That(((BuiltinType)ptr.Inner).Name).IsEqualTo("char");
    }

    /// <summary>Dispatches <c>category: "fixed_array"</c> into a <see cref="FixedArrayType"/> with the explicit element count.</summary>
    [Test]
    public async Task Parse_FixedArrayType()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_arr", "offset": 0,
                  "type": { "category": "fixed_array", "count": 18,
                            "inner": { "category": "builtin", "name": "char" } } }]
              }]
            }
            """);

        FixedArrayType arr = (FixedArrayType)root.Classes[0].Fields[0].Type;
        await Assert.That(arr.Count).IsEqualTo(18);
        await Assert.That(((BuiltinType)arr.Inner).Name).IsEqualTo("char");
    }

    /// <summary>Plumbs the <c>"nullable": true</c> JSON marker through to <see cref="AtomicType.Nullable"/>.</summary>
    [Test]
    public async Task Parse_AtomicType_WithNullableFlag()
    {
        // Pins that the parser reads the "nullable" flag. The mapper currently
        // drops it (TM-1) — but the parser plumbs it through correctly today.
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_s", "offset": 0,
                  "type": { "category": "atomic", "name": "CUtlString", "nullable": true } }]
              }]
            }
            """);

        AtomicType at = (AtomicType)root.Classes[0].Fields[0].Type;
        await Assert.That(at.Name).IsEqualTo("CUtlString");
        await Assert.That(at.Nullable).IsTrue();
    }

    /// <summary>Captures <c>handle_kind</c> and the inner <c>declared_class</c> on a <c>CHandle</c>-shaped atomic.</summary>
    [Test]
    public async Task Parse_AtomicType_WithHandleKindAndInner()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_h", "offset": 0,
                  "type": { "category": "atomic", "name": "CHandle",
                            "handle_kind": "entity",
                            "inner": { "category": "declared_class", "name": "CBar", "module": "client" } } }]
              }]
            }
            """);

        AtomicType at = (AtomicType)root.Classes[0].Fields[0].Type;
        await Assert.That(at.HandleKind).IsEqualTo("entity");
        await Assert.That(at.Inner).IsTypeOf<DeclaredClassType>();
        await Assert.That(((DeclaredClassType)at.Inner!).Name).IsEqualTo("CBar");
    }

    /// <summary>Dispatches <c>declared_class</c> / <c>declared_enum</c> into the typed records and preserves the cross-module reference name.</summary>
    [Test]
    public async Task Parse_DeclaredClass_AndDeclaredEnum()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [
                  { "name": "m_a", "offset": 0,
                    "type": { "category": "declared_class", "name": "CBar", "module": "server" } },
                  { "name": "m_b", "offset": 8,
                    "type": { "category": "declared_enum", "name": "EState", "module": "client" } }
                ]
              }]
            }
            """);

        FieldModel[] fields = root.Classes[0].Fields;
        DeclaredClassType cls = (DeclaredClassType)fields[0].Type;
        DeclaredEnumType en = (DeclaredEnumType)fields[1].Type;
        await Assert.That(cls.Name).IsEqualTo("CBar");
        await Assert.That(cls.Module).IsEqualTo("server");
        await Assert.That(en.Name).IsEqualTo("EState");
    }

    /// <summary>Dispatches <c>category: "bitfield"</c> into a <see cref="BitfieldType"/> carrying the bit count.</summary>
    [Test]
    public async Task Parse_BitfieldType()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_bits", "offset": 0,
                  "type": { "category": "bitfield", "count": 3 } }]
              }]
            }
            """);

        BitfieldType bf = (BitfieldType)root.Classes[0].Fields[0].Type;
        await Assert.That(bf.Count).IsEqualTo(3);
    }

    /// <summary>An unrecognised <c>category</c> value falls back to <see cref="UnknownType"/> rather than throwing — future-proofing the parser against new dumper output.</summary>
    [Test]
    public async Task Parse_UnknownCategory_FallsBackToUnknownType()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{ "name": "m_x", "offset": 0,
                  "type": { "category": "future_category" } }]
              }]
            }
            """);

        UnknownType ut = (UnknownType)root.Classes[0].Fields[0].Type;
        await Assert.That(ut.Category).IsEqualTo("future_category");
    }

    // ── Class-level shape ────────────────────────────────────────────────────

    /// <summary>Reads top-level class metadata (size, alignment, abstract flag, parents) into the <see cref="ClassModel"/>.</summary>
    [Test]
    public async Task Parse_ClassWithParents_AndAbstract_AndAlignment()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CChild", "module": "client",
                "size": 32, "alignment": 8, "abstract": true,
                "parents": [{ "name": "CParent", "module": "client", "offset": 0 }],
                "fields": []
              }]
            }
            """);

        ClassModel cls = root.Classes[0];
        await Assert.That(cls.Size).IsEqualTo(32);
        await Assert.That(cls.Alignment).IsEqualTo((byte)8);
        await Assert.That(cls.IsAbstract).IsTrue();
        await Assert.That(cls.Parents.Length).IsEqualTo(1);
        await Assert.That(cls.Parents[0].Name).IsEqualTo("CParent");
    }

    /// <summary>Missing optional class fields (size, alignment, abstract, parents) default to zero/empty rather than null.</summary>
    [Test]
    public async Task Parse_ClassMissingOptionalFields_GetsSafeDefaults()
    {
        // Minimal class: no size, alignment, parents, abstract flag.
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{ "name": "CFoo", "module": "client", "fields": [] }]
            }
            """);

        ClassModel cls = root.Classes[0];
        await Assert.That(cls.Size).IsEqualTo(0);
        await Assert.That(cls.Alignment).IsEqualTo((byte)0);
        await Assert.That(cls.IsAbstract).IsFalse();
        await Assert.That(cls.Parents.Length).IsEqualTo(0);
    }

    // ── Enum-level shape ─────────────────────────────────────────────────────

    /// <summary>Reads enum-level <c>flags</c> and <c>storage_size</c> plus the full member list with values.</summary>
    [Test]
    public async Task Parse_Enum_FlagsAndStorageSize()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "enums": [{
                "name": "EMyFlags", "module": "client",
                "storage_size": 4, "flags": true,
                "members": [
                  { "name": "None", "value": 0 },
                  { "name": "A",    "value": 1 },
                  { "name": "B",    "value": 2 }
                ]
              }]
            }
            """);

        EnumModel en = root.Enums[0];
        await Assert.That(en.IsFlags).IsTrue();
        await Assert.That(en.StorageSize).IsEqualTo(4);
        await Assert.That(en.Members.Length).IsEqualTo(3);
        await Assert.That(en.Members[2].Value).IsEqualTo(2L);
    }

    // ── Metadata round-trip into the model ───────────────────────────────────

    /// <summary>Reads field-level <c>metadata</c> entries (with optional values) into <c>FieldModel.Metadata</c>.</summary>
    [Test]
    public async Task Parse_FieldMetadata_IsCaptured()
    {
        // The emitter currently discards this (CE-2). The parser is correct today.
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [{
                "name": "CFoo", "module": "client",
                "fields": [{
                  "name": "m_x", "offset": 0,
                  "type": { "category": "builtin", "name": "int32" },
                  "metadata": [
                    { "name": "MNotSaved" },
                    { "name": "MMaxValue", "value": "100" }
                  ]
                }]
              }]
            }
            """);

        MetadataEntry[] md = root.Classes[0].Fields[0].Metadata;
        await Assert.That(md.Length).IsEqualTo(2);
        await Assert.That(md[0].Name).IsEqualTo("MNotSaved");
        await Assert.That(md[0].Value).IsNull();
        await Assert.That(md[1].Name).IsEqualTo("MMaxValue");
        await Assert.That(md[1].Value).IsEqualTo("100");
    }

    /// <summary>Reads per-member <c>metadata</c> on enum members into <c>MemberModel.Metadata</c>.</summary>
    [Test]
    public async Task Parse_EnumMemberMetadata_IsCaptured()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "enums": [{
                "name": "EMyEnum", "module": "client",
                "members": [{
                  "name": "A", "value": 1,
                  "metadata": [{ "name": "MDescription", "value": "first" }]
                }]
              }]
            }
            """);

        MetadataEntry[] md = root.Enums[0].Members[0].Metadata;
        await Assert.That(md.Length).IsEqualTo(1);
        await Assert.That(md[0].Value).IsEqualTo("first");
    }

    // ── Empty / edge inputs ──────────────────────────────────────────────────

    /// <summary>An empty <c>{}</c> document yields empty <c>Classes</c> and <c>Enums</c> arrays without throwing.</summary>
    [Test]
    public async Task Parse_EmptyDocument_ReturnsEmptyArrays()
    {
        SchemaRoot root = SchemaModel.Parse("{}");
        await Assert.That(root.Classes.Length).IsEqualTo(0);
        await Assert.That(root.Enums.Length).IsEqualTo(0);
    }

    /// <summary>The parser accepts JSON with trailing commas (a feature of the dumper's output).</summary>
    [Test]
    public async Task Parse_TrailingCommas_AreAllowed()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "classes": [
                { "name": "CFoo", "module": "client", "fields": [], },
              ],
            }
            """);
        await Assert.That(root.Classes.Length).IsEqualTo(1);
    }

    // ── schema_format_version guard (CS2_GEN_004) ────────────────────────────
    //
    // Upstream moved to format 2.0 on 2026-08-06, which reshaped every record
    // (numerics became JSON strings, `category` uppercased, the namespace key
    // moved from `module` to `projectName`). Without this guard the mismatch
    // surfaced as an opaque Number/String error from the offset parse.

    /// <summary>A schema declaring a different format major fails with the migration diagnostic rather than an opaque type error.</summary>
    [Test]
    public async Task Parse_UnsupportedFormatMajor_ThrowsWithDiagnosticText()
    {
        NotSupportedException? ex = Assert.Throws<NotSupportedException>(() =>
            SchemaModel.Parse("""
                { "schema_format_version": "2.0", "classes": [], "enums": [] }
                """));

        await Assert.That(ex.Message).Contains("2.0");
        await Assert.That(ex.Message).Contains("docs/upstream/schematracker-migration.md");
    }

    /// <summary>The declared supported major parses normally.</summary>
    [Test]
    public async Task Parse_SupportedFormatMajor_IsAccepted()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "schema_format_version": "1.0",
              "classes": [{ "name": "CFoo", "module": "client", "fields": [] }]
            }
            """);
        await Assert.That(root.Classes.Length).IsEqualTo(1);
    }

    /// <summary>Fixtures and pre-1.0 dumps omit the key entirely; absence must not block a parse.</summary>
    [Test]
    public async Task Parse_MissingFormatVersion_IsAccepted()
    {
        SchemaRoot root = SchemaModel.Parse("""
            { "classes": [{ "name": "CFoo", "module": "client", "fields": [] }] }
            """);
        await Assert.That(root.Classes.Length).IsEqualTo(1);
    }

    /// <summary>An unreadable version string is treated as compatible — a format-string change alone must not hard-block a regen that would otherwise succeed.</summary>
    [Test]
    public async Task Parse_UnparseableFormatVersion_IsAccepted()
    {
        SchemaRoot root = SchemaModel.Parse("""
            {
              "schema_format_version": "v-next",
              "classes": [{ "name": "CFoo", "module": "client", "fields": [] }]
            }
            """);
        await Assert.That(root.Classes.Length).IsEqualTo(1);
    }
}
