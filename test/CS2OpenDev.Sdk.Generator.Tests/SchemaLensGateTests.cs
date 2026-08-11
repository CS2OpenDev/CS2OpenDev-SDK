using CS2SchemaGen.Models;
using CS2SchemaGen.SchemaLens;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// The staleness gates — the issue #6 §1 answer, tested against synthetic
// schemas small enough to reason about.
//
// The fixture mirrors the real shapes the gates have to handle: a field
// inherited from an ancestor (m_iHealth on a base class), a sub-service
// pointer whose STATIC type is the engine base while the field lives on the
// game-specific derivation (m_pItemServices → CItemServices, field on
// CCSItemServices) — the exact CPlayer_ItemServices / CCSPlayer_ItemServices
// shape CS2 ships — and a derived entity class whose fields must NOT satisfy a
// lookup on its base, because a covered class names the concrete networked
// type.
public class SchemaLensGateTests
{
    private const string Schema = """
        { "classes": [
            { "name": "CBaseThing", "projectName": "server", "fields": [
                { "name": "m_iHealth", "type": { "category": "builtin", "name": "int32" } }
            ] },
            { "name": "CThing", "projectName": "server",
              "parents": [ { "name": "CBaseThing", "module": "server" } ],
              "fields": [
                { "name": "m_flValue", "type": { "category": "builtin", "name": "float32" } },
                { "name": "m_pItemServices", "type": { "category": "ptr",
                    "inner": { "category": "declared_class", "name": "CItemServices", "module": "server" } } }
            ] },
            { "name": "CSpecialThing", "projectName": "server",
              "parents": [ { "name": "CThing", "module": "server" } ],
              "fields": [
                { "name": "m_iSpecialOnly", "type": { "category": "builtin", "name": "int32" } }
            ] },
            { "name": "CItemServices", "projectName": "server", "fields": [] },
            { "name": "CCSItemServices", "projectName": "server",
              "parents": [ { "name": "CItemServices", "module": "server" } ],
              "fields": [
                { "name": "m_bHasDefuser", "type": { "category": "builtin", "name": "bool" } }
            ] }
        ] }
        """;

    private static LensGateReport Gate(string changes, string schemaJson = Schema, string? committed = null,
        string build = "genesis")
    {
        LensMigration migration = LensMigrationLoader.Parse($$"""
            { "id": "0000-test", "build": "{{build}}", "stateHash": "sha256:PLACEHOLDER",
              "changes": [ {{changes}} ] }
            """, "0000-test");
        LensReplayResult replay = LensReplay.Replay([migration]);
        return LensGates.Run(replay.State, replay.Renames, SchemaModel.Parse(schemaJson), committed);
    }

    private static async Task AssertSingleFailure(LensGateReport report, string id, params string[] fragments)
    {
        await Assert.That(report.Failures).Count().IsEqualTo(1);
        await Assert.That(report.Failures[0].Descriptor.Id).IsEqualTo(id);
        foreach (string fragment in fragments)
        {
            await Assert.That(report.Failures[0].Message).Contains(fragment);
        }
    }

    // ── Resolution happy paths ───────────────────────────────────────────────

    /// <summary>
    ///     An inherited field and a derived-sub-service field both resolve: up
    ///     the ancestor chain from the covered class, and down into derivations
    ///     after a pointer hop.
    /// </summary>
    [Test]
    public async Task Gates_ResolveThroughAncestorsAndDerivedSubServices()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iHealth" },
            { "op": "addField", "class": "CThing", "field": "m_pItemServices.m_bHasDefuser" }
            """);

        await Assert.That(report.Failures).IsEmpty();

        LensResolvedClass resolved = report.Resolution["CThing"];
        await Assert.That(LensTypeRenderer.Render(resolved.FieldTypes["m_iHealth"])).IsEqualTo("int32");
        await Assert.That(LensTypeRenderer.WidthBytes(resolved.FieldTypes["m_iHealth"])).IsEqualTo(4);
        await Assert.That(LensTypeRenderer.Render(resolved.FieldTypes["m_pItemServices.m_bHasDefuser"]))
            .IsEqualTo("bool");
        await Assert.That(LensTypeRenderer.WidthBytes(resolved.FieldTypes["m_pItemServices.m_bHasDefuser"]))
            .IsEqualTo(1);

        // observedFields is the class's OWN census — inherited names stay with
        // their declaring class, or every base-class patch would trip every
        // covered descendant.
        await Assert.That(resolved.ObservedFields).IsEquivalentTo(["m_flValue", "m_pItemServices"]);
    }

    // ── CS2_GEN_010 ──────────────────────────────────────────────────────────

    /// <summary>A tracked field the schema has dropped is an error that names the remedy ops.</summary>
    [Test]
    public async Task Gates_UnresolvedTrackedField_FailsWith010()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iArmor" }
            """);

        await AssertSingleFailure(report, "CS2_GEN_010", "CThing.m_iArmor", "rename", "removeField");
    }

    /// <summary>A covered class names the CONCRETE type: a subclass field must not satisfy the root lookup.</summary>
    [Test]
    public async Task Gates_RootLookupDoesNotSearchDerivedClasses()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iSpecialOnly" }
            """);

        await AssertSingleFailure(report, "CS2_GEN_010", "m_iSpecialOnly");
    }

    /// <summary>A path into a non-class type has nowhere to go, and says so.</summary>
    [Test]
    public async Task Gates_PathThroughNonTraversableType_FailsWith010()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flValue.m_iInner" }
            """);

        await AssertSingleFailure(report, "CS2_GEN_010", "cannot be traversed");
    }

    /// <summary>A covered class the schema no longer declares fails with the class-level remedy.</summary>
    [Test]
    public async Task Gates_UnresolvedClass_FailsWith010()
    {
        LensGateReport report = Gate("""{ "op": "addClass", "class": "CGone" }""");

        await AssertSingleFailure(report, "CS2_GEN_010", "CGone", "removeClass");
    }

    /// <summary>
    ///     A bare name in two modules refuses to guess and directs to the
    ///     module pin; the pin then resolves it.
    /// </summary>
    [Test]
    public async Task Gates_AmbiguousClassNameErrorsUntilPinned()
    {
        const string dualSchema = """
            { "classes": [
                { "name": "CDual", "projectName": "server", "fields": [
                    { "name": "m_iX", "type": { "category": "builtin", "name": "int32" } } ] },
                { "name": "CDual", "projectName": "client", "fields": [
                    { "name": "m_iX", "type": { "category": "builtin", "name": "int32" } } ] }
            ] }
            """;

        LensGateReport bare = Gate("""{ "op": "addClass", "class": "CDual" }""", dualSchema);
        await AssertSingleFailure(bare, "CS2_GEN_010", "'client'", "'server'", "module");

        LensGateReport pinned = Gate(
            """{ "op": "addClass", "class": "CDual", "module": "client" }""", dualSchema);
        await Assert.That(pinned.Failures).IsEmpty();
        await Assert.That(pinned.Resolution["CDual"].Module).IsEqualTo("client");
    }

    // ── CS2_GEN_011 ──────────────────────────────────────────────────────────

    /// <summary>
    ///     A rename whose retired name the schema declares AGAIN is superseded:
    ///     upstream re-grew the old name, and the migration must be revisited.
    /// </summary>
    [Test]
    public async Task Gates_RenameWhoseFromResolvesAgain_FailsWith011()
    {
        // The schema carries BOTH spellings: the rename's target resolves (so
        // no CS2_GEN_010 noise) and its retired source resolves too (the 011
        // condition).
        const string regrownSchema = """
            { "classes": [
                { "name": "CThing", "projectName": "server", "fields": [
                    { "name": "m_flValue",    "type": { "category": "builtin", "name": "float32" } },
                    { "name": "m_flValueNew", "type": { "category": "builtin", "name": "float32" } }
                ] }
            ] }
            """;

        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flValue" },
            { "op": "rename", "class": "CThing", "from": "m_flValue", "to": "m_flValueNew" }
            """, regrownSchema);

        await AssertSingleFailure(report, "CS2_GEN_011", "0000-test", "m_flValue", "resolves in the current schema again");
    }

    // ── CS2_GEN_012 ──────────────────────────────────────────────────────────

    private const string CommittedState = """
        { "classes": { "CThing": { "observedFields": [ "m_pItemServices" ] } } }
        """;

    /// <summary>
    ///     A new schema field on a covered class, measured against the COMMITTED
    ///     state, is a hard error until a migration accounts for it — this is
    ///     what fails CI when Valve touches a covered class.
    /// </summary>
    [Test]
    public async Task Gates_NewObservedField_FailsWith012()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" }
            """, committed: CommittedState);

        // m_flValue is in the schema but absent from the committed census and
        // unaccounted by any op.
        await AssertSingleFailure(report, "CS2_GEN_012", "CThing", "m_flValue");
    }

    /// <summary>addField accounts for the new field — tracking it IS the decision.</summary>
    [Test]
    public async Task Gates_NewObservedField_SilencedByAddField()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flValue" }
            """, committed: CommittedState);

        await Assert.That(report.Failures).IsEmpty();
    }

    /// <summary>ignoreField accounts for it too — "deliberately not tracked" is a decision as well.</summary>
    [Test]
    public async Task Gates_NewObservedField_SilencedByIgnoreField()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "ignoreField", "class": "CThing", "field": "m_flValue" }
            """, committed: CommittedState);

        await Assert.That(report.Failures).IsEmpty();
    }

    /// <summary>
    ///     Removals of untracked fields are not errors: nothing a consumer reads
    ///     broke, and the state.json diff carries the news into review.
    /// </summary>
    [Test]
    public async Task Gates_RemovedUntrackedField_IsNotAnError()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flValue" }
            """, committed: """
                { "classes": { "CThing": { "observedFields": [
                    "m_flValue", "m_pItemServices", "m_iGoneUntracked" ] } } }
                """);

        await Assert.That(report.Failures).IsEmpty();
    }

    /// <summary>No committed state, no baseline, no 012 — the first regen's diff is the review surface.</summary>
    [Test]
    public async Task Gates_FirstRunWithoutCommittedState_Skips012()
    {
        LensGateReport report = Gate("""
            { "op": "addClass", "class": "CThing" }
            """, committed: null);

        await Assert.That(report.Failures).IsEmpty();
    }
}
