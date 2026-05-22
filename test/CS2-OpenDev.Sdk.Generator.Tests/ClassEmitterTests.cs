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
        FieldModel[]? fields = null,
        Annotations? annotations = null,
        MetadataEntry[]? metadata = null) =>
        new(name, module, size, Alignment: 0, isAbstract,
            parents ?? [],
            fields ?? [],
            Metadata: metadata ?? [],
            annotations);

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

    // ── Annotation-driven summary handling ───────────────────────────────────

    /// <summary>When a class carries an annotation description, the description IS the summary and the schema name moves into the remarks block as `Native name: ...`.</summary>
    [Test]
    public async Task Emit_AnnotatedClass_DescriptionBecomesSummary_NameRelocatesToRemarks()
    {
        ClassModel cls = MakeClass(
            name: "CBaseCSGrenadeProjectile",
            module: "server",
            size: 2608,
            annotations: new Annotations(
                "Base class for all CS2 grenade projectile entities.",
                Notes: null, Warning: null));
        string src = Emit(cls);

        await Assert.That(src).Contains("///     Base class for all CS2 grenade projectile entities.");
        await Assert.That(src).Contains("Native name: <c>CBaseCSGrenadeProjectile</c>. Module: <c>server</c> — 2608 bytes.");
        // Old shape (schema name as summary, description as <para> tail) must be gone.
        await Assert.That(src).DoesNotContain("<para>Base class");
    }

    /// <summary>Unannotated classes keep the schema name as the summary and do NOT add a `Native name:` prefix to remarks.</summary>
    [Test]
    public async Task Emit_UnannotatedClass_SummaryIsSchemaName_NoRelocation()
    {
        string src = Emit(MakeClass(name: "CFoo", module: "client", size: 32));
        await Assert.That(src).Contains("///     CFoo.");
        await Assert.That(src).DoesNotContain("Native name: <c>CFoo</c>");
    }

    /// <summary>Annotation notes survive in remarks (as a `&lt;para&gt;`) even when description has been promoted to summary.</summary>
    [Test]
    public async Task Emit_AnnotatedClass_NotesAndWarningStayInRemarks()
    {
        ClassModel cls = MakeClass(
            name: "CFoo",
            annotations: new Annotations(
                Description: "A short description.",
                Notes: "Only valid during round-end.",
                Warning: "Do not rely on this in 5v5."));
        string src = Emit(cls);

        await Assert.That(src).Contains("<para>Only valid during round-end.</para>");
        await Assert.That(src).Contains("⚠ Warning: Do not rely on this in 5v5.");
    }

    /// <summary>Property summary leads with annotation description when present, replacing the "Gets or sets X." filler.</summary>
    [Test]
    public async Task Emit_AnnotatedField_DescriptionReplacesGetsOrSetsFiller()
    {
        FieldModel field = new("m_nBounces", 0x9D8, new BuiltinType("int32"),
            Metadata: [],
            Annotations: new Annotations(
                "Number of times the grenade has bounced off a surface so far.",
                Notes: null, Warning: null));
        string src = Emit(MakeClass(fields: [field]));

        await Assert.That(src).Contains("///     Number of times the grenade has bounced off a surface so far.");
        await Assert.That(src).DoesNotContain("Gets or sets Bounces.");
        await Assert.That(src).DoesNotContain("<para>Number of times");
    }

    /// <summary>Unannotated property summary still emits the standard "Gets or sets X." filler with a terminal period.</summary>
    [Test]
    public async Task Emit_UnannotatedField_KeepsGetsOrSetsFiller()
    {
        string src = Emit(MakeClass(fields: [IntField("m_iHealth", 0)]));
        await Assert.That(src).Contains("///     Gets or sets Health.");
    }

    // ── Class-level metadata round-trip (CE-3) ───────────────────────────────

    /// <summary>Class-level metadata entries are emitted as `[NativeMetadata]` attributes on the class declaration so 3000+ schema-carried markers (MGetKV3ClassDefaults, MPropertyFriendlyName, …) survive into the C# projection instead of being silently dropped.</summary>
    [Test]
    public async Task Emit_ClassMetadata_RoundTripsAsNativeMetadata()
    {
        ClassModel cls = MakeClass(metadata: [
            new MetadataEntry("MPropertyFriendlyName", "\"Friendly\""),
            new MetadataEntry("MGetKV3ClassDefaults", null)
        ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("[NativeMetadata(\"MPropertyFriendlyName\", \"\\\"Friendly\\\"\")]");
        await Assert.That(src).Contains("[NativeMetadata(\"MGetKV3ClassDefaults\")]");
    }

    // ── Source 2 metadata → XML summary promotion ────────────────────────────

    /// <summary>MPropertyDescription is promoted to the class XML summary (priority: annotation &gt; MPropertyDescription &gt; MPropertyFriendlyName &gt; default). Surrounding quotes from the KV3-stringified value are stripped.</summary>
    [Test]
    public async Task Emit_ClassWithMPropertyDescription_PromotesToSummary()
    {
        ClassModel cls = MakeClass(metadata: [
            new MetadataEntry("MPropertyDescription", "\"The grenade projectile base class.\"")
        ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("///     The grenade projectile base class.");
        // Schema name moves to remarks via Native name: prefix when promotion wins the summary slot.
        await Assert.That(src).Contains("Native name: <c>CFoo</c>");
    }

    /// <summary>Annotation description still beats MPropertyDescription when both are present. (Metadata still round-trips via [NativeMetadata], it just doesn't win the summary slot.)</summary>
    [Test]
    public async Task Emit_ClassWithBothAnnotationAndMetadata_AnnotationWins()
    {
        ClassModel cls = MakeClass(
            annotations: new Annotations("Curated annotation wins.", Notes: null, Warning: null),
            metadata: [new MetadataEntry("MPropertyDescription", "\"Editor metadata loses.\"")]);
        string src = Emit(cls);
        await Assert.That(src).Contains("///     Curated annotation wins.");
        // The metadata is still preserved via the [NativeMetadata] attribute
        // round-trip, but the summary line itself must not carry the metadata
        // text. Anchor on the `///     ` prefix to scope the check.
        await Assert.That(src).DoesNotContain("///     Editor metadata loses.");
    }

    /// <summary>MPropertyFriendlyName is used as a last-resort summary when neither annotation description nor MPropertyDescription is present.</summary>
    [Test]
    public async Task Emit_ClassWithOnlyFriendlyName_PromotesToSummary()
    {
        ClassModel cls = MakeClass(metadata: [
            new MetadataEntry("MPropertyFriendlyName", "\"Aim Camera Node\"")
        ]);
        string src = Emit(cls);
        await Assert.That(src).Contains("///     Aim Camera Node.");
    }

    /// <summary>Field-level MPropertyDescription is promoted to the property's XML summary, replacing the default "Gets or sets X" filler.</summary>
    [Test]
    public async Task Emit_FieldWithMPropertyDescription_PromotesToSummary()
    {
        FieldModel field = new("m_iHealth", 0, new BuiltinType("int32"),
            Metadata: [new MetadataEntry("MPropertyDescription", "\"Current health points.\"")]);
        string src = Emit(MakeClass(fields: [field]));
        await Assert.That(src).Contains("///     Current health points.");
        await Assert.That(src).DoesNotContain("Gets or sets Health.");
    }
}
