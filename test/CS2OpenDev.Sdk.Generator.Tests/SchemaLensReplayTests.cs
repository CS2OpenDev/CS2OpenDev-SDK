using CS2SchemaGen.SchemaLens;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Schema Lens migrations: loading, validation, replay (issue #6).
//
// Replay is the audit trail for every name the Lens serves, so most of what is
// worth testing here is refusal: the op vocabulary is closed, consumer-side
// keys are rejected by name, and an op that does not apply cleanly throws
// rather than best-effortsing.
public class SchemaLensReplayTests
{
    private static LensMigration Parse(string json, string id = "0000-test") =>
        LensMigrationLoader.Parse(json, id);

    private static LensReplayResult Replay(params LensMigration[] migrations) =>
        LensReplay.Replay(migrations);

    private static LensMigration Migration(string changes, string build = "genesis", string id = "0000-test") =>
        Parse($$"""
            { "id": "{{id}}", "build": "{{build}}", "stateHash": "sha256:PLACEHOLDER",
              "changes": [ {{changes}} ] }
            """, id);

    // File-level validation
    /// <summary>The id must equal the filename stem — diagnostics quote the id, replay order is the filename.</summary>
    [Test]
    public async Task Parse_IdDisagreesWithFilenameStem_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Parse("""
                { "id": "0001-something-else", "build": "b", "stateHash": "sha256:x", "changes": [] }
                """, "0001-the-actual-name"));

        await Assert.That(ex.Message).Contains("0001-something-else");
        await Assert.That(ex.Message).Contains("0001-the-actual-name");
    }

    /// <summary>The op vocabulary is closed; an unknown op is an error, never a skip.</summary>
    [Test]
    public async Task Parse_UnknownOp_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Migration("""{ "op": "transformField", "class": "CThing", "field": "m_x" }"""));

        await Assert.That(ex.Message).Contains("transformField");
        await Assert.That(ex.Message).Contains("consumer-side");
    }

    /// <summary>
    ///     A consumer-side key inside an op fails by name. This is the §3 split
    ///     with teeth: a `transform` pasted from a downstream lens table must not
    ///     be silently shed.
    /// </summary>
    [Test]
    public async Task Parse_ConsumerSideKeyInsideOp_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Migration("""
                { "op": "addField", "class": "CThing", "field": "m_bX", "transform": "BoolFromInt" }
                """));

        await Assert.That(ex.Message).Contains("transform");
    }

    /// <summary>Directory load is ordinal filename order, and state.json is output, not input.</summary>
    [Test]
    public async Task LoadDirectory_OrdinalOrder_SkippingStateFile()
    {
        string dir = Directory.CreateTempSubdirectory("lens-load").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "0001-second.json"), """
                { "id": "0001-second", "build": "b", "stateHash": "sha256:x", "changes": [] }
                """);
            File.WriteAllText(Path.Combine(dir, "0000-first.json"), """
                { "id": "0000-first", "build": "a", "stateHash": "sha256:x", "changes": [] }
                """);
            File.WriteAllText(Path.Combine(dir, "state.json"), """{ "not": "a migration" }""");

            IReadOnlyList<LensMigration> migrations = LensMigrationLoader.LoadDirectory(dir);

            await Assert.That(migrations.Select(m => m.Id))
                .IsEquivalentTo(["0000-first", "0001-second"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // addClass
    /// <summary>An omitted netName derives mechanically: strip a leading 'C' followed by an uppercase letter.</summary>
    [Test]
    public async Task Replay_AddClass_DerivesNetNameByStrippingLeadingC()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CCSPlayerPawn" },
            { "op": "addClass", "class": "CHEGrenadeProjectile" }
            """)).State;

        await Assert.That(state.Classes["CCSPlayerPawn"].NetName).IsEqualTo("CSPlayerPawn");
        await Assert.That(state.Classes["CHEGrenadeProjectile"].NetName).IsEqualTo("HEGrenadeProjectile");
    }

    /// <summary>
    ///     The strip rule never guesses: a 'C' followed by lowercase is a word
    ///     start, not a class prefix, and survives.
    /// </summary>
    [Test]
    public async Task Replay_AddClass_LeavesNonPrefixNamesAlone()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CircuitBoard" },
            { "op": "addClass", "class": "GameSceneNode" }
            """)).State;

        await Assert.That(state.Classes["CircuitBoard"].NetName).IsEqualTo("CircuitBoard");
        await Assert.That(state.Classes["GameSceneNode"].NetName).IsEqualTo("GameSceneNode");
    }

    /// <summary>An explicit netName is a curated override and always wins over derivation.</summary>
    [Test]
    public async Task Replay_AddClass_ExplicitNetNameWins()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CCSPlayerPawn", "netName": "PlayerPawn" }
            """)).State;

        await Assert.That(state.Classes["CCSPlayerPawn"].NetName).IsEqualTo("PlayerPawn");
    }

    /// <summary>The module pin rides on the class for the resolution gate to enforce.</summary>
    [Test]
    public async Task Replay_AddClass_KeepsModulePin()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CCSPlayerController", "module": "client" }
            """)).State;

        await Assert.That(state.Classes["CCSPlayerController"].ModulePin).IsEqualTo("client");
    }

    [Test]
    public async Task Replay_DuplicateAddClass_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "addClass", "class": "CThing" }
                """)));

        await Assert.That(ex.Message).Contains("already covered");
    }

    // addField
    /// <summary>
    ///     An omitted targetProperty derives through the SAME fold the class
    ///     emitters use — Hungarian strip, PascalCase, word split — so the Lens
    ///     and the SDK can never disagree about what a field is called.
    /// </summary>
    [Test]
    public async Task Replay_AddField_DerivesTargetPropertyViaTheEmitterFold()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iHealth" },
            { "op": "addField", "class": "CThing", "field": "m_bPawnIsAlive" },
            { "op": "addField", "class": "CThing", "field": "m_flStamina" }
            """)).State;

        await Assert.That(state.Classes["CThing"].Fields["m_iHealth"].TargetProperty).IsEqualTo("Health");
        await Assert.That(state.Classes["CThing"].Fields["m_bPawnIsAlive"].TargetProperty).IsEqualTo("PawnIsAlive");
        await Assert.That(state.Classes["CThing"].Fields["m_flStamina"].TargetProperty).IsEqualTo("Stamina");
    }

    /// <summary>A dotted path derives from its last segment: the property names the leaf, not the route.</summary>
    [Test]
    public async Task Replay_AddField_DerivesFromLastSegmentOfDottedPath()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_pMovementServices.m_flStamina" }
            """)).State;

        await Assert.That(state.Classes["CThing"].Fields["m_pMovementServices.m_flStamina"].TargetProperty)
            .IsEqualTo("Stamina");
    }

    /// <summary>`build` stamps firstSeenBuild on addField, and only the migration's own build.</summary>
    [Test]
    public async Task Replay_AddField_StampsFirstSeenBuild()
    {
        LensMigration first = Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iOld" }
            """, "genesis", "0000-genesis");
        LensMigration second = Migration("""
            { "op": "addField", "class": "CThing", "field": "m_iNew" }
            """, "14093", "0001-patch");

        LensState state = Replay(first, second).State;

        await Assert.That(state.Classes["CThing"].Fields["m_iOld"].FirstSeenBuild).IsEqualTo("genesis");
        await Assert.That(state.Classes["CThing"].Fields["m_iNew"].FirstSeenBuild).IsEqualTo("14093");
    }

    [Test]
    public async Task Replay_DuplicateAddField_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "addField", "class": "CThing", "field": "m_iX" },
                { "op": "addField", "class": "CThing", "field": "m_iX" }
                """)));

        await Assert.That(ex.Message).Contains("already tracked");
    }

    [Test]
    public async Task Replay_AddFieldToUnknownClass_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""{ "op": "addField", "class": "CGhost", "field": "m_iX" }""")));

        await Assert.That(ex.Message).Contains("CGhost");
        await Assert.That(ex.Message).Contains("addClass must precede");
    }

    // rename / moveSubService
    /// <summary>
    ///     A rename moves the entry wholesale (target property, first-seen
    ///     build and type history belong to the field, not to its spelling)
    ///     and leaves every historical name resolving through the alias table.
    /// </summary>
    [Test]
    public async Task Replay_Rename_MovesEntryWholesaleAndKeepsAllNamesAlive()
    {
        LensMigration genesis = Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flOld", "targetProperty": "Curated" },
            { "op": "addAlias", "class": "CThing", "canonical": "m_flOld", "alias": "m_flLegacy" },
            { "op": "typeShift", "class": "CThing", "field": "m_flOld", "fromType": "float32", "toType": "float64" }
            """, "genesis", "0000-genesis");
        LensMigration rename = Migration("""
            { "op": "rename", "class": "CThing", "from": "m_flOld", "to": "m_flNew" }
            """, "14093", "0001-rename");

        LensReplayResult result = Replay(genesis, rename);
        LensClassState cls = result.State.Classes["CThing"];

        await Assert.That(cls.Fields.ContainsKey("m_flOld")).IsFalse();
        await Assert.That(cls.Fields["m_flNew"].TargetProperty).IsEqualTo("Curated");
        await Assert.That(cls.Fields["m_flNew"].FirstSeenBuild).IsEqualTo("genesis");
        await Assert.That(cls.Fields["m_flNew"].TypeHistory).Count().IsEqualTo(1);

        // Repointed, retired-name and self entries — lookup by any name lands
        // on the same canonical.
        await Assert.That(cls.Aliases["m_flLegacy"]).IsEqualTo("m_flNew");
        await Assert.That(cls.Aliases["m_flOld"]).IsEqualTo("m_flNew");
        await Assert.That(cls.Aliases["m_flNew"]).IsEqualTo("m_flNew");

        // The record the CS2_GEN_011 gate replays later.
        await Assert.That(result.Renames).Count().IsEqualTo(1);
        await Assert.That(result.Renames[0].Op).IsEqualTo("rename");
        await Assert.That(result.Renames[0].From).IsEqualTo("m_flOld");
    }

    /// <summary>moveSubService is rename's mechanics under a different authorial label.</summary>
    [Test]
    public async Task Replay_MoveSubService_SameMechanicsAsRename()
    {
        LensReplayResult result = Replay(Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_pOldServices.m_iX", "targetProperty": "X" },
            { "op": "moveSubService", "class": "CThing", "from": "m_pOldServices.m_iX", "to": "m_pNewServices.m_iX" }
            """));
        LensClassState cls = result.State.Classes["CThing"];

        await Assert.That(cls.Fields["m_pNewServices.m_iX"].TargetProperty).IsEqualTo("X");
        await Assert.That(cls.Fields["m_pNewServices.m_iX"].FirstSeenBuild).IsEqualTo("genesis");
        await Assert.That(cls.Aliases["m_pOldServices.m_iX"]).IsEqualTo("m_pNewServices.m_iX");
        await Assert.That(cls.Aliases["m_pNewServices.m_iX"]).IsEqualTo("m_pNewServices.m_iX");
        await Assert.That(result.Renames[0].Op).IsEqualTo("moveSubService");
    }

    [Test]
    public async Task Replay_RenameOfUntrackedField_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "rename", "class": "CThing", "from": "m_iGhost", "to": "m_iNew" }
                """)));

        await Assert.That(ex.Message).Contains("m_iGhost");
    }

    // addAlias
    [Test]
    public async Task Replay_AddAliasForUnknownCanonical_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "addAlias", "class": "CThing", "canonical": "m_iGhost", "alias": "m_iOld" }
                """)));

        await Assert.That(ex.Message).Contains("m_iGhost");
    }

    /// <summary>Canonical names and aliases share one namespace; a collision is a lie waiting to be served.</summary>
    [Test]
    public async Task Replay_AliasCollidingWithCanonicalName_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "addField", "class": "CThing", "field": "m_iA" },
                { "op": "addField", "class": "CThing", "field": "m_iB" },
                { "op": "addAlias", "class": "CThing", "canonical": "m_iA", "alias": "m_iB" }
                """)));

        await Assert.That(ex.Message).Contains("collides");
    }

    // removeField
    /// <summary>A removed canonical takes its dependent aliases with it.</summary>
    [Test]
    public async Task Replay_RemoveField_RemovesDependentAliases()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iX" },
            { "op": "addAlias", "class": "CThing", "canonical": "m_iX", "alias": "m_iOldX" },
            { "op": "removeField", "class": "CThing", "field": "m_iX" }
            """)).State;

        await Assert.That(state.Classes["CThing"].Fields).IsEmpty();
        await Assert.That(state.Classes["CThing"].Aliases).IsEmpty();
    }

    // typeShift / ignoreField
    /// <summary>typeShift appends a history record stamped with the migration's build, and changes nothing else.</summary>
    [Test]
    public async Task Replay_TypeShift_AppendsHistoryWithBuild()
    {
        LensMigration genesis = Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_nCount" }
            """, "genesis", "0000-genesis");
        LensMigration shift = Migration("""
            { "op": "typeShift", "class": "CThing", "field": "m_nCount", "fromType": "int32", "toType": "int64" }
            """, "14100", "0001-shift");

        LensState state = Replay(genesis, shift).State;
        LensTypeShift entry = state.Classes["CThing"].Fields["m_nCount"].TypeHistory.Single();

        await Assert.That(entry.Build).IsEqualTo("14100");
        await Assert.That(entry.FromType).IsEqualTo("int32");
        await Assert.That(entry.ToType).IsEqualTo("int64");
    }

    /// <summary>A field cannot be both served and disowned.</summary>
    [Test]
    public async Task Replay_IgnoreOfTrackedField_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Replay(Migration("""
                { "op": "addClass", "class": "CThing" },
                { "op": "addField", "class": "CThing", "field": "m_iX" },
                { "op": "ignoreField", "class": "CThing", "field": "m_iX" }
                """)));

        await Assert.That(ex.Message).Contains("tracked");
    }

    /// <summary>Tracking supersedes acknowledgment: addField promotes a previously ignored field.</summary>
    [Test]
    public async Task Replay_AddField_PromotesAnIgnoredField()
    {
        LensState state = Replay(Migration("""
            { "op": "addClass", "class": "CThing" },
            { "op": "ignoreField", "class": "CThing", "field": "m_iX" },
            { "op": "addField", "class": "CThing", "field": "m_iX" }
            """)).State;

        await Assert.That(state.Classes["CThing"].Ignored).IsEmpty();
        await Assert.That(state.Classes["CThing"].Fields.ContainsKey("m_iX")).IsTrue();
    }

    // Hash checks
    /// <summary>
    ///     The placeholder is recognised as the authoring flow, a wrong hash as
    ///     a broken signature, and the computed value as the one to paste.
    /// </summary>
    [Test]
    public async Task Replay_HashChecks_DistinguishPlaceholderFromMismatchFromMatch()
    {
        const string changes = """
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_iX" }
            """;

        LensHashCheck placeholder = Replay(Migration(changes)).HashChecks.Single();
        await Assert.That(placeholder.IsPlaceholder).IsTrue();
        await Assert.That(placeholder.Matches).IsFalse();

        LensHashCheck wrong = Replay(Parse($$"""
            { "id": "0000-test", "build": "genesis", "stateHash": "sha256:0000",
              "changes": [ {{changes}} ] }
            """)).HashChecks.Single();
        await Assert.That(wrong.IsPlaceholder).IsFalse();
        await Assert.That(wrong.Matches).IsFalse();

        // Bake the computed hash back in (the authoring loop) and the check
        // goes green.
        LensHashCheck baked = Replay(Parse($$"""
            { "id": "0000-test", "build": "genesis", "stateHash": "{{placeholder.ComputedHash}}",
              "changes": [ {{changes}} ] }
            """)).HashChecks.Single();
        await Assert.That(baked.Matches).IsTrue();
    }
}
