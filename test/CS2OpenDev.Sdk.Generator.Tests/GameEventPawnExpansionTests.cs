using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — the wire-key expansion for the two pawn-bearing player-reference
// tags (CS2OpenDev-SchemaTracker#6).
//
// The rule under test: the engine derives wire keys from the declared *type*,
// so `player_controller_and_pawn` occupies two keys and `player_pawn` occupies
// one key that is not its declared name. Both were previously read under the
// declared name, which dropped 59 pawn handles and made 11 properties decode as
// a constant 0.

public class GameEventPawnExpansionTests
{
    // Hoisted out of the assertions: CA1861 (warnings are errors here) rejects
    // constant array arguments at a call site.
    private static readonly string[] UseridAndCompanion = ["userid", "userid_pawn"];

    private static readonly string[] TwoRefsInOrder =
        ["userid", "userid_pawn", "weapon", "attacker", "attacker_pawn"];

    private static GameEventsRoot RootWith(params GameEventFieldModel[] fields) =>
        new([new GameEventModel("test_event", null, "core.gameevents", false, false, fields, null)]);

    private static GameEventFieldModel[] ExpandFields(params GameEventFieldModel[] fields) =>
        GameEventPawnExpansion.Expand(RootWith(fields)).Events[0].Fields;

    /// <summary><c>player_pawn</c> keeps its declared property name but moves to the <c>_pawn</c> wire key.</summary>
    [Test]
    public async Task PlayerPawn_KeepsName_MovesWireKey()
    {
        GameEventFieldModel[] fields =
            ExpandFields(new GameEventFieldModel("userid", "player_pawn", null, null));

        await Assert.That(fields.Length).IsEqualTo(1);
        await Assert.That(fields[0].Name).IsEqualTo("userid");
        await Assert.That(fields[0].WireKey).IsEqualTo("userid_pawn");
        await Assert.That(fields[0].IsPawnHandle).IsTrue();
        // The declared tag survives so [GameEventFieldType] still reports what
        // the schema said, not what the expansion inferred.
        await Assert.That(fields[0].Type).IsEqualTo("player_pawn");
    }

    /// <summary><c>player_controller_and_pawn</c> yields the original controller field plus a companion.</summary>
    [Test]
    public async Task ControllerAndPawn_AddsCompanion_LeavingControllerUntouched()
    {
        GameEventFieldModel controller = new("userid", "player_controller_and_pawn", "the player", null);
        GameEventFieldModel[] fields = ExpandFields(controller);

        await Assert.That(fields.Length).IsEqualTo(2);

        // Controller half is byte-for-byte what was emitted before the change —
        // this is what makes the companion purely additive for consumers.
        await Assert.That(fields[0]).IsEqualTo(controller);
        await Assert.That(fields[0].WireKey).IsEqualTo("userid");
        await Assert.That(fields[0].IsPawnHandle).IsFalse();

        await Assert.That(fields[1].Name).IsEqualTo("userid_pawn");
        await Assert.That(fields[1].WireKey).IsEqualTo("userid_pawn");
        await Assert.That(fields[1].IsPawnHandle).IsTrue();
    }

    /// <summary>The companion does not inherit the controller's prose, which describes a userid.</summary>
    [Test]
    public async Task Companion_DoesNotInheritControllerAnnotations()
    {
        GameEventFieldModel[] fields = ExpandFields(
            new GameEventFieldModel(
                "attacker",
                "player_controller_and_pawn",
                "who did the damage",
                new Annotations("the attacking player's userid", null, null)));

        await Assert.That(fields[1].Annotations).IsNull();
        await Assert.That(fields[1].Comment).IsNotEqualTo("who did the damage");
    }

    /// <summary><c>player_controller</c> has no pawn companion and is passed through unchanged.</summary>
    [Test]
    public async Task PlayerController_IsUntouched()
    {
        GameEventFieldModel controller = new("userid", "player_controller", null, null);
        GameEventFieldModel[] fields = ExpandFields(controller);

        await Assert.That(fields.Length).IsEqualTo(1);
        await Assert.That(fields[0]).IsEqualTo(controller);
        await Assert.That(fields[0].WireKey).IsEqualTo("userid");
    }

    /// <summary>Non-player fields are passed through untouched.</summary>
    [Test]
    [Arguments("short")]
    [Arguments("string")]
    [Arguments("ehandle")]
    public async Task UnrelatedTags_AreUntouched(string tag)
    {
        GameEventFieldModel field = new("value", tag, null, null);
        GameEventFieldModel[] fields = ExpandFields(field);

        await Assert.That(fields.Length).IsEqualTo(1);
        await Assert.That(fields[0]).IsEqualTo(field);
    }

    /// <summary>Several player references in one event each get their own companion, in place.</summary>
    [Test]
    public async Task MultiplePlayerRefs_EachExpandIndependently_PreservingOrder()
    {
        GameEventFieldModel[] fields = ExpandFields(
            new GameEventFieldModel("userid", "player_controller_and_pawn", null, null),
            new GameEventFieldModel("weapon", "string", null, null),
            new GameEventFieldModel("attacker", "player_controller_and_pawn", null, null));

        string[] names = fields.Select(f => f.Name).ToArray();
        await Assert.That(names).IsEquivalentTo(TwoRefsInOrder);
    }

    /// <summary>An event with nothing to expand comes back as the same instance, not a copy.</summary>
    [Test]
    public async Task EventWithNoPlayerRefs_IsReturnedUnchanged()
    {
        GameEventsRoot root = RootWith(new GameEventFieldModel("weapon", "string", null, null));
        GameEventsRoot expanded = GameEventPawnExpansion.Expand(root);

        await Assert.That(expanded.Events[0]).IsSameReferenceAs(root.Events[0]);
    }

    /// <summary>Expansion is idempotent in the sense that re-running it does not double-add companions.</summary>
    /// <remarks>
    ///     Guards the pipeline order in Program.cs: the expansion runs once, after the supplement
    ///     merge. A second pass over already-expanded fields must not produce `userid_pawn_pawn`.
    /// </remarks>
    [Test]
    public async Task Expansion_IsIdempotent()
    {
        GameEventsRoot once = GameEventPawnExpansion.Expand(
            RootWith(new GameEventFieldModel("userid", "player_controller_and_pawn", null, null)));
        GameEventsRoot twice = GameEventPawnExpansion.Expand(once);

        string[] names = twice.Events[0].Fields.Select(f => f.Name).ToArray();
        await Assert.That(names).IsEquivalentTo(UseridAndCompanion);
    }

    /// <summary>
    ///     A real upstream declaration of the companion key wins: no synthesised duplicate is added.
    /// </summary>
    /// <remarks>
    ///     Same self-retiring shape as game-event-supplement.json. Nothing in the schema declares a
    ///     `*_pawn` field today, but if extraction ever catches up, emitting both would put two
    ///     properties with the same name on one record — a compile error in generated code.
    /// </remarks>
    [Test]
    public async Task DeclaredCompanion_SuppressesTheSynthesisedOne()
    {
        GameEventFieldModel[] fields = ExpandFields(
            new GameEventFieldModel("userid", "player_controller_and_pawn", null, null),
            new GameEventFieldModel("userid_pawn", "ehandle", "declared upstream", null));

        string[] names = fields.Select(f => f.Name).ToArray();
        await Assert.That(names).IsEquivalentTo(UseridAndCompanion);
        // The upstream declaration is the one that survived, untouched.
        await Assert.That(fields[1].Type).IsEqualTo("ehandle");
        await Assert.That(fields[1].Comment).IsEqualTo("declared upstream");
    }

    /// <summary>The default field carries no override, so WireKey falls back to the declared name.</summary>
    [Test]
    public async Task WireKey_DefaultsToName()
    {
        GameEventFieldModel field = new("userid", "player_controller", null, null);
        await Assert.That(field.WireKey).IsEqualTo("userid");
    }
}
