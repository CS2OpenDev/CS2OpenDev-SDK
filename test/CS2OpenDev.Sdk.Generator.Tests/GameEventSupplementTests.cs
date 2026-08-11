using CS2SchemaGen.Models;
using CS2_OpenDev.Sdk.Generator.Tests.Support;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Curated event supplement (issue #3).
//
// Two properties carry the design, and both are asymmetric in a way that makes
// them easy to get subtly wrong:
//
//   * A supplement must be able to ADD a native name and never to REPLACE one.
//     The failure it prevents is not a wrong value — it is an invented event
//     shape outliving the real declaration, shipped forever under a type name
//     nobody notices is stale.
//   * An absent supplement must change nothing at all. The 289 extracted records
//     have to be byte-identical to what they were before this hook existed, or
//     every consumer takes a diff for a feature they did not ask for.
public class GameEventSupplementTests
{
    // The three names issue #3 reported as firing on the wire with no record.
    // Ordinal-sorted so the comparison against the shipped file is order-free.
    private static readonly string[] ReportedEventNames = ["game_restart", "halftime", "item_drop"];

    // A minimal stand-in for the extracted schema. `item_pickup` is here because
    // several tests need a name the supplement is not allowed to claim.
    private const string ExtractedJson = """
        {
          "events": [{
            "name": "item_pickup",
            "source": "mod.gameevents",
            "fields": [
              { "name": "userid", "type": "player_controller" },
              { "name": "item",   "type": "string" }
            ]
          }]
        }
        """;

    private const string SupplementJson = """
        {
          "events": [{
            "name": "item_drop",
            "fields": [
              { "name": "userid", "type": "player_controller" },
              { "name": "item",   "type": "string" }
            ],
            "annotations": { "description": "Fired when a player drops a weapon or item." }
          }]
        }
        """;

    // ── Absent supplement is a no-op ─────────────────────────────────────────

    /// <summary>
    ///     With no supplement file anywhere, resolution yields null — the caller's
    ///     signal to change nothing.
    /// </summary>
    /// <remarks>
    ///     Asserted against the resolver rather than against <c>Apply</c>, because
    ///     "absent" is a path-resolution outcome. A test that merged an empty root
    ///     and found nothing changed would pass even if the resolver were throwing
    ///     on a missing file.
    /// </remarks>
    [Test]
    public async Task ResolvePath_NoFileAnywhere_ReturnsNull()
    {
        string empty = Directory.CreateTempSubdirectory("supplement-absent").FullName;
        try
        {
            await Assert.That(GameEventSupplement.ResolvePath(empty, empty)).IsNull();
        }
        finally
        {
            Directory.Delete(empty, true);
        }
    }

    /// <summary>Schema-adjacent wins over the working directory when both exist.</summary>
    [Test]
    public async Task ResolvePath_PrefersSchemaDirectoryOverWorkingDirectory()
    {
        string schemaDir = Directory.CreateTempSubdirectory("supplement-schema").FullName;
        string workDir = Directory.CreateTempSubdirectory("supplement-cwd").FullName;
        try
        {
            string beside = Path.Combine(schemaDir, GameEventSupplement.FileName);
            File.WriteAllText(beside, SupplementJson);
            File.WriteAllText(Path.Combine(workDir, GameEventSupplement.FileName), SupplementJson);

            await Assert.That(GameEventSupplement.ResolvePath(schemaDir, workDir)).IsEqualTo(beside);

            // …and the working directory is still the fallback when it is the only copy.
            File.Delete(beside);
            await Assert.That(GameEventSupplement.ResolvePath(schemaDir, workDir))
                .IsEqualTo(Path.Combine(workDir, GameEventSupplement.FileName));
        }
        finally
        {
            Directory.Delete(schemaDir, true);
            Directory.Delete(workDir, true);
        }
    }

    /// <summary>An empty supplement leaves the extracted root untouched, reference and all.</summary>
    [Test]
    public async Task Apply_EmptySupplement_ReturnsExtractedUnchanged()
    {
        GameEventsRoot extracted = GameEventsModel.Parse(ExtractedJson);

        await Assert.That(GameEventSupplement.Apply(extracted, GameEventSupplement.Empty))
            .IsSameReferenceAs(extracted);
    }

    /// <summary>Emission without a supplement is unchanged — no stray records, no altered docs.</summary>
    [Test]
    public async Task Emit_WithoutSupplement_IsUnchanged()
    {
        GeneratorHarness.RunResult run = GeneratorHarness.RunGameEvents(ExtractedJson);

        await Assert.That(run.Files.ContainsKey("Events/ItemPickupEvent")).IsTrue();
        await Assert.That(run.Files.ContainsKey("Events/ItemDropEvent")).IsFalse();
        // The provenance block must not leak onto extracted records.
        await Assert.That(run.Files["Events/ItemPickupEvent"]).DoesNotContain("CURATED SUPPLEMENT");
    }

    // ── A supplement event is emitted ────────────────────────────────────────

    /// <summary>A supplemented event becomes a record beside the extracted ones.</summary>
    [Test]
    public async Task Emit_SupplementEvent_ProducesARecord()
    {
        string record = GeneratorHarness
            .RunGameEvents(ExtractedJson, supplementJson: SupplementJson)
            .Files["Events/ItemDropEvent"];

        await Assert.That(record).Contains("public sealed partial record ItemDropEvent");
        await Assert.That(record).Contains("[NativeName(\"item_drop\")]");
        // Field projections are the ordinary ones — a curated event is curated in
        // provenance only, never in how its tags are mapped.
        await Assert.That(record).Contains("public required int UserId { get; init; }");
        await Assert.That(record).Contains("public required string Item { get; init; }");
    }

    /// <summary>
    ///     The record says, in the doc a consumer reads, that it is curated and why.
    /// </summary>
    /// <remarks>
    ///     The failure this guards is a consumer treating an observed field list as
    ///     a schema contract. Nothing at the type level distinguishes the two, so
    ///     the distinction has to be in the prose or it does not exist.
    /// </remarks>
    [Test]
    public async Task Emit_SupplementEvent_DocumentsItsProvenance()
    {
        string record = GeneratorHarness
            .RunGameEvents(ExtractedJson, supplementJson: SupplementJson)
            .Files["Events/ItemDropEvent"];

        await Assert.That(record).Contains("CURATED SUPPLEMENT");
        await Assert.That(record).Contains("not present in the extracted CS2");
        await Assert.That(record).Contains("CMsgSource1LegacyGameEventList");
        // The exit condition is documented too: a reader must know the record is
        // temporary and what makes it go away.
        await Assert.That(record).Contains("generation");
        await Assert.That(record).Contains("deleted");
    }

    /// <summary>
    ///     The source is <c>sdk.supplement</c>, and the file cannot say otherwise.
    /// </summary>
    /// <remarks>
    ///     Provenance is the one field a consumer uses to judge how much to trust a
    ///     record's shape. If a supplement could stamp <c>mod.gameevents</c> on
    ///     itself, that field would be worthless.
    /// </remarks>
    [Test]
    public async Task Parse_StampsSupplementSource_AndRejectsAnyOther()
    {
        GameEventsRoot supplement = GameEventSupplement.Parse(SupplementJson);

        await Assert.That(supplement.Events[0].Source).IsEqualTo("sdk.supplement");
        await Assert.That(supplement.Events[0].Supplemented).IsTrue();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventSupplement.Parse("""
                { "events": [{ "name": "item_drop", "source": "mod.gameevents", "fields": [] }] }
                """));

        await Assert.That(ex.Message).Contains("mod.gameevents");
        await Assert.That(ex.Message).Contains("sdk.supplement");
    }

    /// <summary>An explicit, correct source is accepted — the file may state what it is.</summary>
    [Test]
    public async Task Parse_ExplicitSupplementSource_IsAccepted()
    {
        GameEventsRoot supplement = GameEventSupplement.Parse("""
            { "events": [{ "name": "item_drop", "source": "sdk.supplement", "fields": [] }] }
            """);

        await Assert.That(supplement.Events[0].Source).IsEqualTo("sdk.supplement");
    }

    /// <summary>The factory reads the supplemented event's keys like any other.</summary>
    [Test]
    public async Task Emit_SupplementEvent_ProducesAFactoryAndRegistryEntry()
    {
        GeneratorHarness.RunResult run = GeneratorHarness
            .RunGameEventFactories(ExtractedJson, supplementJson: SupplementJson);

        string factories = run.Files["Generated/GameEventFactories"];
        // Same reader calls the extracted item_pickup gets — the type tags were
        // copied from it precisely so these two lines match.
        await Assert.That(factories).Contains("UserId = reader.GetInt32(\"userid\")");
        await Assert.That(factories).Contains("Item = reader.GetString(\"item\")");
        await Assert.That(factories).Contains("Curated supplement");

        string registry = run.Files["Generated/GameEventRegistry"];
        await Assert.That(registry).Contains("[\"item_drop\"] = static (in GameEventReader r) "
                                             + "=> GameEventFactories.ItemDropEventFrom(r),");
        // A supplement is a declaration like any other, so it counts.
        await Assert.That(registry).Contains("NameCount = 2;");
        await Assert.That(registry).Contains("DeclarationCount = 2;");
    }

    /// <summary>
    ///     A supplement event is the only declaration of its name, so it takes the
    ///     unsuffixed type name and wins the preferred lookup.
    /// </summary>
    /// <remarks>
    ///     Guards the grouping path: both emitters group on <c>source</c>, and
    ///     <c>sdk.supplement</c> sits at priority 0 — below <c>core</c>. That is
    ///     only harmless while a supplemented name is never in a group with an
    ///     extracted one, which the supersede check enforces.
    /// </remarks>
    [Test]
    public async Task Emit_SupplementEvent_TakesTheUnsuffixedTypeName()
    {
        GeneratorHarness.RunResult run = GeneratorHarness
            .RunGameEventFactories(ExtractedJson, supplementJson: SupplementJson);

        string registry = run.Files["Generated/GameEventRegistry"];
        await Assert.That(registry).Contains("ItemDropEvent");
        // No source-suffixed variant: the group has exactly one member.
        await Assert.That(registry).DoesNotContain("ItemDropSdkEvent");
    }

    // ── Colliding with an extracted event fails loudly ───────────────────────

    /// <summary>
    ///     A supplement naming an event the schema already declares fails the build.
    /// </summary>
    /// <remarks>
    ///     This is the success condition reported as a failure. Upstream caught up,
    ///     the curated guess is obsolete, and the only correct response is to delete
    ///     the entry — so the message says that rather than describing a conflict.
    /// </remarks>
    [Test]
    public async Task Apply_NameAlreadyExtracted_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventSupplement.Apply(
                GameEventsModel.Parse(ExtractedJson),
                GameEventSupplement.Parse("""
                    { "events": [{ "name": "item_pickup", "fields": [] }] }
                    """)));

        await Assert.That(ex.Message).Contains("item_pickup");
        await Assert.That(ex.Message).Contains("deleted");
    }

    /// <summary>
    ///     The collision is on the native name alone, not on (name, source).
    /// </summary>
    /// <remarks>
    ///     The regression this guards is the one an identity check would introduce:
    ///     <c>item_pickup</c> at <c>sdk.supplement</c> and <c>item_pickup</c> at
    ///     <c>mod.gameevents</c> are distinct pairs, so an identity check lets both
    ///     through — and the duplicate-name machinery then emits the real record
    ///     plus a stale <c>ItemPickupSdkEvent</c> that ships an invented shape
    ///     indefinitely. Since a supplement's source is always
    ///     <c>sdk.supplement</c>, name-only is the only rule that can fire.
    /// </remarks>
    [Test]
    public async Task Apply_NameCollidesAcrossDifferentSources_StillThrows()
    {
        // The extracted declaration is `mod.gameevents`; the supplement's is
        // `sdk.supplement`. Different sources, same name — and it must still fail.
        Assert.Throws<InvalidOperationException>(() =>
            GameEventSupplement.Apply(
                GameEventsModel.Parse(ExtractedJson),
                GameEventSupplement.Parse("""
                    { "events": [{ "name": "item_pickup", "fields": [] }] }
                    """)));

        // And nothing is emitted for the colliding name under a suffixed alias.
        GeneratorHarness.RunResult run = GeneratorHarness.RunGameEvents(ExtractedJson);
        await Assert.That(run.Files.ContainsKey("Events/ItemPickupSdkEvent")).IsFalse();
    }

    // ── Supplement-internal validation ───────────────────────────────────────

    /// <summary>
    ///     Two supplement entries for one name are rejected at parse time.
    /// </summary>
    /// <remarks>
    ///     Not merely untidy. <c>GameEventModel</c> is a record with value equality
    ///     and the type-name map is keyed on the model, so two identical zero-field
    ///     entries collapse to one key: one record silently vanishes and the emitted
    ///     registry gets a duplicate dictionary key that throws at static init —
    ///     a consumer-side crash with no line pointing back here.
    /// </remarks>
    [Test]
    public async Task Parse_DuplicateNameWithinSupplement_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventSupplement.Parse("""
                { "events": [
                  { "name": "halftime", "fields": [] },
                  { "name": "halftime", "fields": [] }
                ] }
                """));

        await Assert.That(ex.Message).Contains("halftime");
    }

    /// <summary>A nameless entry is rejected — the name is the only thing a dispatcher keys on.</summary>
    [Test]
    public async Task Parse_MissingName_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventSupplement.Parse("""{ "events": [{ "fields": [] }] }"""));

        await Assert.That(ex.Message).Contains("name");
    }

    /// <summary>A supplement with no events parses and is a no-op.</summary>
    [Test]
    public async Task Parse_EmptyDocument_IsEmpty()
    {
        await Assert.That(GameEventSupplement.Parse("{}").Events).IsEmpty();
    }

    // ── The three events this repo actually ships ────────────────────────────

    /// <summary>
    ///     The shipped supplement carries exactly the three events issue #3 reported,
    ///     and they emit under the names the SDK promises.
    /// </summary>
    /// <remarks>
    ///     Reads the repo's own <c>game-event-supplement.json</c> rather than a
    ///     fixture. A typo there is not caught by anything else until someone
    ///     regenerates, and <c>HalfTimeEvent</c> in particular depends on the name
    ///     lock splitting <c>halftime</c> — the same split that gives the extracted
    ///     <c>start_halftime</c> its <c>StartHalfTimeEvent</c>.
    /// </remarks>
    [Test]
    public async Task ShippedSupplement_CarriesTheThreeReportedEvents()
    {
        // No file is a legitimate end state: when upstream declares all three,
        // the entries go and so does the file. Passing on absence is correct —
        // the mechanism's own behaviour is covered by the tests above, and the
        // alternative here is a FileNotFoundException that reads like a broken
        // test rather than like a finished migration.
        string? path = FindRepoFile(GameEventSupplement.FileName);
        if (path is null)
        {
            return;
        }

        GameEventsRoot supplement = GameEventSupplement.Parse(File.ReadAllText(path));

        string[] names = supplement.Events.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        await Assert.That(names).IsEquivalentTo(ReportedEventNames);

        GeneratorHarness.RunResult run = GeneratorHarness.RunGameEvents(
            ExtractedJson, supplementJson: File.ReadAllText(path));

        await Assert.That(run.Files.ContainsKey("Events/ItemDropEvent")).IsTrue();
        await Assert.That(run.Files.ContainsKey("Events/HalfTimeEvent")).IsTrue();
        await Assert.That(run.Files.ContainsKey("Events/GameRestartEvent")).IsTrue();

        // item_drop's tags are copied from item_pickup so the factory emits the
        // same reader calls; asserting the tags keeps that link explicit.
        GameEventModel itemDrop = supplement.Events.First(e => e.Name == "item_drop");
        await Assert.That(itemDrop.Fields.Single(f => f.Name == "userid").Type)
            .IsEqualTo("player_controller");
        await Assert.That(itemDrop.Fields.Single(f => f.Name == "item").Type).IsEqualTo("string");
    }

    // Walks up from the test binary to the repo root. The test project runs from
    // artifacts/bin/…, so a relative path from the working directory is not stable.
    // Null when no ancestor has the file.
    private static string? FindRepoFile(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
