using System.Text;
using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 2 — emitter snapshot tests for EnumEmitter.

public class EnumEmitterTests
{
    private static EnumModel MakeEnum(
        string name = "EFoo",
        string module = "client",
        bool isFlags = false,
        int? storageSize = 4,
        MemberModel[]? members = null,
        Annotations? annotations = null) =>
        new(name, module, Alignment: null, storageSize, isFlags,
            members ?? [new MemberModel("A", 1L, [])],
            Metadata: [],
            annotations);

    private static string Emit(EnumModel en)
    {
        StringBuilder sb = new();
        EnumEmitter.Emit(sb, en);
        return sb.ToString();
    }

    // Baseline shape
    /// <summary>Emits the underlying CLR type clause (e.g. <c>: byte</c>) derived from the enum's <c>storage_size</c>.</summary>
    [Test]
    public async Task Emit_BasicEnum_HasUnderlyingTypeFromStorageSize()
    {
        string src = Emit(MakeEnum(storageSize: 1));
        await Assert.That(src).Contains("public enum EFoo : byte");
    }

    /// <summary>Maps each schema <c>storage_size</c> (1/2/4/8) to the corresponding unsigned CLR underlying type.</summary>
    [Test]
    [Arguments(1, "byte")]
    [Arguments(2, "ushort")]
    [Arguments(4, "uint")]
    [Arguments(8, "ulong")]
    public async Task Emit_StorageSizes_MapToUnsignedUnderlyingTypes(int storage, string clrType)
    {
        string src = Emit(MakeEnum(storageSize: storage));
        await Assert.That(src).Contains($"public enum EFoo : {clrType}");
    }

    /// <summary>Emits <c>[Flags]</c> and pluralises the type name (<c>EMyFlag</c> → <c>EMyFlags</c>) when the schema marks the enum as flags.</summary>
    [Test]
    public async Task Emit_FlagsEnum_HasFlagsAttribute()
    {
        EnumModel en = MakeEnum(name: "EMyFlag", isFlags: true);
        string src = Emit(en);
        await Assert.That(src).Contains("[Flags]");
        // ToTypeName with isFlags=true should produce "EMyFlags" (singular Flag → plural)
        await Assert.That(src).Contains("public enum EMyFlags");
    }

    /// <summary>Omits <c>[Flags]</c> when the enum is not a flag set.</summary>
    [Test]
    public async Task Emit_NonFlagsEnum_NoFlagsAttribute()
    {
        string src = Emit(MakeEnum(isFlags: false));
        await Assert.That(src).DoesNotContain("[Flags]");
    }

    // Member shape
    /// <summary>Stamps each enum member with <c>[NativeName]</c> carrying the raw C++ identifier and strips the enum-type prefix from the C# member name.</summary>
    [Test]
    public async Task Emit_Member_EmitsNativeNameAttribute()
    {
        EnumModel en = MakeEnum(members: [new MemberModel("EFoo_A", 1L, [])]);
        string src = Emit(en);
        await Assert.That(src).Contains("[NativeName(\"EFoo_A\")]");
        // EFoo_A on enum named EFoo → strip "EFoo_" prefix → "A". Last member: no trailing comma.
        await Assert.That(src).Contains("A = 1\n");
    }

    /// <summary>Reinterprets a negative member value as the unsigned equivalent for the enum's underlying width.</summary>
    [Test]
    public async Task Emit_NegativeValue_FormatsUnsignedForStorageSize()
    {
        EnumModel en = MakeEnum(
            storageSize: 4,
            members: [new MemberModel("A", -1L, [])]);
        string src = Emit(en);
        // Single-member enum: this IS the last member, so no trailing comma.
        await Assert.That(src).Contains("A = 4294967295\n");
    }

    // CA1069 duplicate-value handling
    /// <summary>Marks the second member of a duplicate-value pair with <c>[Obsolete("Alias for …")]</c> to satisfy CA1069.</summary>
    [Test]
    public async Task Emit_DuplicateValue_MarksLaterMemberAsObsoleteAlias()
    {
        EnumModel en = MakeEnum(members: [
            new MemberModel("Primary",   1L, []),
            new MemberModel("Aliased",   1L, [])
        ]);
        string src = Emit(en);
        await Assert.That(src).Contains("[Obsolete(\"Alias for Primary.\")]");
    }

    // CA1708 case-only collision
    /// <summary>Resolves case-only collisions (<c>Foo</c>/<c>foo</c>) by appending the raw C++ name as a suffix rather than an ordinal counter (NH-3).</summary>
    [Test]
    public async Task Emit_CaseCollidingMembers_DisambiguatorIsRawNativeName()
    {
        // NH-3: case-only collisions disambiguate using the raw C++ member name as
        // the suffix, not a numeric counter. Carries meaning, stable across reorders.
        EnumModel en = MakeEnum(members: [
            new MemberModel("Foo", 1L, []),
            new MemberModel("foo", 2L, []) // same name modulo case after PascalCasing
        ]);
        string src = Emit(en);
        await Assert.That(src).Contains("Foo = 1,");
        // Last member: no trailing comma.
        await Assert.That(src).Contains("Foo_foo = 2\n");
        await Assert.That(src).DoesNotContain("Foo2 = 2");
    }

    // EE-1: enum-member metadata round-trip
    /// <summary>Round-trips <c>MemberModel.Metadata</c> entries as <c>[NativeMetadata("Name", "Value")]</c> on the emitted enum member (EE-1).</summary>
    [Test]
    public async Task Emit_MemberMetadata_RoundTripsAsNativeMetadata()
    {
        EnumModel en = MakeEnum(members: [
            new MemberModel("A", 1L, [new MetadataEntry("MDescription", "first")])
        ]);
        string src = Emit(en);
        await Assert.That(src).Contains("[NativeMetadata(\"MDescription\", \"first\")]");
    }

    // Defensive edge cases
    /// <summary>Omits the <c>: type</c> clause when the schema provides no <c>storage_size</c>.</summary>
    [Test]
    public async Task Emit_EnumWithoutStorageSize_OmitsUnderlyingType()
    {
        // No "storage_size" in the JSON → no " : type" clause on the enum declaration.
        string src = Emit(MakeEnum(storageSize: null));
        await Assert.That(src).Contains("public enum EFoo\n");
        await Assert.That(src).DoesNotContain("public enum EFoo :");
    }

    /// <summary>Defensive: an enum with zero members still emits a syntactically valid empty declaration.</summary>
    [Test]
    public async Task Emit_EmptyEnum_StillProducesValidDeclaration()
    {
        // Defensive: an enum with no members compiles as `public enum X : type { }`.
        EnumModel en = MakeEnum(members: []);
        string src = Emit(en);
        await Assert.That(src).Contains("public enum EFoo");
        await Assert.That(src).Contains("{");
        await Assert.That(src).Contains("}");
    }

    // Annotation-driven summary handling
    /// <summary>Enum annotation description becomes the summary; schema name relocates to remarks as `Native name:`.</summary>
    [Test]
    public async Task Emit_AnnotatedEnum_DescriptionBecomesSummary_NameRelocatesToRemarks()
    {
        EnumModel en = MakeEnum(
            name: "EHitGroup",
            annotations: new Annotations(
                "Body region hit by a bullet trace.",
                Notes: null, Warning: null));
        string src = Emit(en);

        await Assert.That(src).Contains("///     Body region hit by a bullet trace.");
        await Assert.That(src).Contains("Native name: <c>EHitGroup</c>. Module: <c>client</c>");
        await Assert.That(src).DoesNotContain("<para>Body region");
    }

    /// <summary>Unannotated enums keep the schema name as the summary with a terminal period and no native-name prefix in remarks.</summary>
    [Test]
    public async Task Emit_UnannotatedEnum_SummaryIsSchemaNameWithPeriod()
    {
        string src = Emit(MakeEnum(name: "EFoo"));
        await Assert.That(src).Contains("///     EFoo.");
        await Assert.That(src).DoesNotContain("Native name: <c>EFoo</c>");
    }

    /// <summary>Annotated enum member promotes its description to summary; native name stays in remarks as before.</summary>
    [Test]
    public async Task Emit_AnnotatedMember_DescriptionBecomesSummary()
    {
        EnumModel en = MakeEnum(members: [
            new MemberModel("EFoo_HEAD", 1L, [],
                Annotations: new Annotations("Headshot region.", Notes: null, Warning: null))
        ]);
        string src = Emit(en);

        await Assert.That(src).Contains("///     Headshot region.");
        // Native name remains in member remarks for round-trip.
        await Assert.That(src).Contains("Native name: <c>EFoo_HEAD</c>.");
    }

    /// <summary>Enum members consistently terminate the remarks `Native name:` line with a period.</summary>
    [Test]
    public async Task Emit_Member_NativeNameRemarkHasTerminalPeriod()
    {
        string src = Emit(MakeEnum(members: [new MemberModel("EFoo_X", 1L, [])]));
        await Assert.That(src).Contains("Native name: <c>EFoo_X</c>.");
    }
}
