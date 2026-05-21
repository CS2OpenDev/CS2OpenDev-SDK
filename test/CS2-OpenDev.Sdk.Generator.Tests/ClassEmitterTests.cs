using System.Text;
using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 2 — emitter snapshot tests for ClassEmitter.
//
// We construct ClassModel instances by hand, run ClassEmitter.Emit, and assert
// on the emitted source. We use Contains/DoesNotContain rather than exact-text
// snapshots so that incidental layout changes don't break unrelated tests.
//
// All tests in this class share the static TypeMapper._nameMap via the Emit
// helper, so they serialize on the "NameMap" parallelism key — same key the
// TypeMapperTests use.

[NotInParallel("NameMap")]
public class ClassEmitterTests
{
    // ── Test data builders ───────────────────────────────────────────────────

    private static ClassModel MakeClass(
        string name = "CFoo",
        string module = "client",
        int size = 0,
        bool isAbstract = false,
        ParentModel[]? parents = null,
        FieldModel[]? fields = null) =>
        new(name, module, size, Alignment: 0, isAbstract,
            parents ?? [],
            fields ?? []);

    private static FieldModel IntField(string name, int offset) =>
        new(name, offset, new BuiltinType("int32"), Metadata: []);

    private static string Emit(ClassModel cls)
    {
        StringBuilder sb = new();
        // Reset the name map so DeclaredClass lookups (when present) don't pick up
        // state from another test in this run.
        TypeMapper.SetNameMap(new Dictionary<string, string>(StringComparer.Ordinal));
        ClassEmitter.Emit(sb, cls);
        return sb.ToString();
    }

    // ── Baseline shape ───────────────────────────────────────────────────────

    /// <summary>A field-bearing class emits a <c>public partial class</c> declaration with the expected C# name.</summary>
    [Test]
    public async Task Emit_BasicClass_ProducesPublicClassDeclaration()
    {
        string src = Emit(MakeClass(fields: [IntField("m_iHealth", 0)]));
        await Assert.That(src).Contains("public partial class CFoo");
    }

    /// <summary>Emits <c>[NativeSize(N)]</c> for a sized class and never the obsolete <c>[StructLayout(...)]</c> (Q2).</summary>
    [Test]
    public async Task Emit_ClassWithSize_EmitsNativeSizeAttribute()
    {
        // Q2: informational [NativeSize(N)] replaces the old [StructLayout(...)] that
        // implied a P/Invoke marshalling contract the managed class can't honour.
        string src = Emit(MakeClass(size: 16, fields: [IntField("m_iHealth", 0)]));
        await Assert.That(src).Contains("[NativeSize(16)]");
        await Assert.That(src).DoesNotContain("[StructLayout(");
    }

    /// <summary>Every emitted class carries the <c>partial</c> keyword so consumers can extend it (Q1).</summary>
    [Test]
    public async Task Emit_Class_IsPartial()
    {
        string src = Emit(MakeClass(fields: [IntField("m_iHealth", 0)]));
        await Assert.That(src).Contains("public partial class CFoo");
    }

    /// <summary>Adds the <c>abstract</c> modifier when the schema marks the class abstract.</summary>
    [Test]
    public async Task Emit_AbstractClass_HasAbstractModifier()
    {
        string src = Emit(MakeClass(isAbstract: true));
        await Assert.That(src).Contains("public abstract partial class CFoo");
    }

    // ── XML doc / native-name plumbing ───────────────────────────────────────

    /// <summary>Includes the raw C++ class name in the XML doc comment for discoverability.</summary>
    [Test]
    public async Task Emit_PreservesCppNameInXmlDoc()
    {
        string src = Emit(MakeClass(name: "CFoo_t"));
        // 4-space indent inside <summary> per the SDK formatter convention.
        await Assert.That(src).Contains("///     CFoo_t");
    }

    /// <summary>Stamps the class with <c>[NativeName]</c> when name transformation (e.g. <c>_t</c> strip) changed the identifier.</summary>
    [Test]
    public async Task Emit_AddsNativeNameAttribute_WhenCsNameDiffers()
    {
        // CFoo_t → CFoo via _t-strip; expect [NativeName("CFoo_t")] to round-trip the original.
        string src = Emit(MakeClass(name: "CFoo_t"));
        await Assert.That(src).Contains("[NativeName(\"CFoo_t\")]");
    }

    /// <summary>Skips the class-level <c>[NativeName]</c> when the C++ name and C# name are already identical (avoids redundant noise).</summary>
    [Test]
    public async Task Emit_OmitsNativeNameAttribute_WhenCsNameMatches()
    {
        // CFoo → CFoo: no rename, no [NativeName] on the class. (Field-level
        // [NativeName] still appears below — we assert only the *class* attribute
        // is absent by checking the declaration line.)
        string src = Emit(MakeClass(name: "CFoo", fields: []));
        int classDeclIdx = src.IndexOf("public partial class CFoo", StringComparison.Ordinal);
        string preamble = src.Substring(0, classDeclIdx);
        await Assert.That(preamble).DoesNotContain("[NativeName(\"CFoo\")]");
    }

    // ── Inheritance ──────────────────────────────────────────────────────────

    /// <summary>Single-parent inheritance is emitted as a base-class clause on the class declaration.</summary>
    [Test]
    public async Task Emit_SingleParent_EmitsBaseClass()
    {
        ClassModel cls = MakeClass(
            name: "CChild",
            parents: [new ParentModel("CParent", "client", 0)]);
        string src = Emit(cls);
        await Assert.That(src).Contains("public partial class CChild : CParent");
    }

    /// <summary>Multiple-inheritance is flattened to first-parent-only in the declaration with the extras listed in a comment.</summary>
    [Test]
    public async Task Emit_MultipleParents_EmitsCommentForExtras()
    {
        ClassModel cls = MakeClass(
            name: "CChild",
            parents: [
                new ParentModel("CParent1", "client", 0),
                new ParentModel("CParent2", "client", 8),
                new ParentModel("CParent3", "client", 16)
            ]);
        string src = Emit(cls);
        await Assert.That(src).Contains(": CParent1");
        await Assert.That(src).Contains("Additional parents: CParent2, CParent3");
    }

    // ── Field property emission ──────────────────────────────────────────────

    /// <summary>Each field becomes a public property with paired <c>[NativeName]</c> and <c>[NativeOffset]</c> attributes.</summary>
    [Test]
    public async Task Emit_Field_EmitsPropertyWithNativeAttributes()
    {
        ClassModel cls = MakeClass(fields: [IntField("m_iHealth", 0x10)]);
        string src = Emit(cls);
        await Assert.That(src).Contains("public int Health { get; set; }");
        await Assert.That(src).Contains("[NativeName(\"m_iHealth\")]");
        await Assert.That(src).Contains("[NativeOffset(0x10)]");
    }

    // ── B4: collision should make BOTH fields use access-only naming ─────────

    /// <summary>When two fields strip to the same clean name (e.g. <c>m_pParent</c>/<c>m_hParent</c>), <em>both</em> fall back to access-only naming for order-stable output (B4).</summary>
    [Test]
    public async Task Emit_PHParentCollision_BothFieldsUseAccessOnlyNaming()
    {
        // m_pParent and m_hParent both strip to "Parent" — today, declaration order
        // makes m_pParent → Parent and m_hParent → HParent (unstable). The fix:
        // both fall back to access-only → PParent and HParent. Stable.
        ClassModel cls = MakeClass(
            name: "CNode",
            fields: [
                new FieldModel("m_pParent", 0x38, new DeclaredClassType("CNode", "client"), []),
                new FieldModel("m_hParent", 0x40, new DeclaredClassType("CNodeHandle", "client"), [])
            ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("PParent");
        await Assert.That(src).Contains("HParent");
        // No bare " Parent " property (with surrounding whitespace) should exist.
        await Assert.That(src).DoesNotContain(" Parent { get; set; }");
    }

    /// <summary>Sanity check: when no collision exists, every field keeps its prefix-stripped clean name.</summary>
    [Test]
    public async Task Emit_NoCollision_UsesCleanName()
    {
        // Sanity check: when names don't collide, both stay clean.
        ClassModel cls = MakeClass(fields: [
            IntField("m_iHealth", 0),
            IntField("m_iMaxHealth", 4)
        ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("public int Health { get; set; }");
        await Assert.That(src).Contains("public int MaxHealth { get; set; }");
    }

    // ── CE-2: metadata round-trip ────────────────────────────────────────────

    /// <summary>Round-trips each field-level metadata entry as <c>[NativeMetadata("Name")]</c> or <c>[NativeMetadata("Name", "Value")]</c> (CE-2).</summary>
    [Test]
    public async Task Emit_FieldMetadata_RoundTripsAsNativeMetadata()
    {
        ClassModel cls = MakeClass(fields: [
            new FieldModel(
                Name: "m_iHealth", Offset: 0,
                Type: new BuiltinType("int32"),
                Metadata: [
                    new MetadataEntry("MNotSaved", null),
                    new MetadataEntry("MMaxValue", "100")
                ])
        ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("[NativeMetadata(\"MNotSaved\")]");
        await Assert.That(src).Contains("[NativeMetadata(\"MMaxValue\", \"100\")]");
    }

    /// <summary>Omits <c>[NativeMetadata]</c> entirely on fields whose schema entry carries no metadata.</summary>
    [Test]
    public async Task Emit_NoFieldMetadata_NoNativeMetadataAttribute()
    {
        string src = Emit(MakeClass(fields: [IntField("m_iHealth", 0)]));
        await Assert.That(src).DoesNotContain("[NativeMetadata(");
    }

    // ── Combined modifier / parent path ──────────────────────────────────────

    /// <summary>Combines <c>abstract</c> modifier and inheritance clause correctly on a single class declaration.</summary>
    [Test]
    public async Task Emit_AbstractClassWithParent_EmitsBothModifierAndBase()
    {
        ClassModel cls = MakeClass(
            name: "CChild",
            isAbstract: true,
            parents: [new ParentModel("CParent", "client", 0)]);
        string src = Emit(cls);
        await Assert.That(src).Contains("public abstract partial class CChild : CParent");
    }
}
