using CS2_OpenDev.Sdk.Generator.Tests.Support;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 2 — emitter snapshot tests for GameEventsEmitter.
//
// Drives the emitter via GeneratorHarness.RunGameEvents and asserts on the
// produced source-text strings. Each test feeds a minimal events JSON and
// pins one observable behaviour:
//   • Per-event file shape (record declaration, namespace, attributes).
//   • Snake-case field-name normalisation.
//   • Cross-source duplicate-name disambiguation (mod > game > core priority).
//   • Local / Reliable property flags.
//   • Empty-fields events emit a parameterless record.
//   • SchemaEvents reverse-lookup registry contents.

public class GameEventsEmitterTests
{
    // ── Per-event file shape ──────────────────────────────────────────────────

    /// <summary>Emits a `public sealed partial record` declaration in the Events sub-namespace with the expected `{Name}Event` type name.</summary>
    [Test]
    public async Task Emit_BasicEvent_ProducesSealedRecordDeclaration()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "player_death",
                "source": "mod.gameevents",
                "fields": [
                  { "name": "userid", "type": "player_controller_and_pawn" }
                ]
              }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(src).Contains("namespace CS2Schema.Events;");
        await Assert.That(src).Contains("public sealed partial record PlayerDeathEvent");
    }

    /// <summary>Stamps the record with `[NativeName]` carrying the raw event name and `[GameEventSource]` carrying the originating `.gameevents` file.</summary>
    [Test]
    public async Task Emit_BasicEvent_HasNativeNameAndSourceAttributes()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "player_death", "source": "mod.gameevents",
                "fields": [{ "name": "userid", "type": "int" }]
              }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(src).Contains("[NativeName(\"player_death\")]");
        await Assert.That(src).Contains("[GameEventSource(\"mod.gameevents\")]");
    }

    /// <summary>Every property is `required init` so the compiler enforces full payload construction.</summary>
    [Test]
    public async Task Emit_Field_IsRequiredInit()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "fields": [{ "name": "userid", "type": "int" }]
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains("public required int Userid { get; init; }");
    }

    /// <summary>Field-level `[NativeName]` and `[GameEventFieldType]` round-trip the raw KV1 name and tag.</summary>
    [Test]
    public async Task Emit_Field_HasNativeNameAndFieldTypeAttributes()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "fields": [{ "name": "weapon_originalowner_xuid", "type": "string" }]
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains("[NativeName(\"weapon_originalowner_xuid\")]");
        await Assert.That(src).Contains("[GameEventFieldType(\"string\")]");
    }

    /// <summary>Snake-case field names fold into PascalCase property names (matches the rest of the SDK's name handling).</summary>
    [Test]
    [Arguments("userid", "Userid")]
    [Arguments("dmg_health", "DmgHealth")]
    [Arguments("weapon_originalowner_xuid", "WeaponOriginalownerXuid")]
    public async Task Emit_FieldName_FoldsSnakeToPascal(string rawName, string propName)
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents($$"""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "fields": [{ "name": "{{rawName}}", "type": "string" }]
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains($"public required string {propName} {{ get; init; }}");
    }

    // ── Type projection ───────────────────────────────────────────────────────

    /// <summary>Each KV1 type tag drives the C# property type via GameEventTypeMapper; this test catches accidental break-the-mapping refactors at the emitter level.</summary>
    [Test]
    [Arguments("string", "string")]
    [Arguments("bool", "bool")]
    [Arguments("byte", "byte")]
    [Arguments("short", "short")]
    [Arguments("float", "float")]
    [Arguments("uint64", "ulong")]
    [Arguments("ehandle", "uint")]
    [Arguments("player_controller_and_pawn", "int")]
    public async Task Emit_Field_TypeProjectsViaMapper(string tag, string expectedClrType)
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents($$"""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "fields": [{ "name": "field", "type": "{{tag}}" }]
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains($"public required {expectedClrType} Field {{ get; init; }}");
    }

    // ── Local / Reliable flags ────────────────────────────────────────────────

    /// <summary>`properties.local: "1"` lifts to `[GameEventLocal]` on the record declaration.</summary>
    [Test]
    public async Task Emit_LocalProperty_EmitsGameEventLocalAttribute()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "demo_start", "source": "core.gameevents",
                "properties": { "local": "1" }
              }]
            }
            """);

        string src = result.Files["Events/DemoStartEvent"];
        await Assert.That(src).Contains("[GameEventLocal]");
    }

    /// <summary>`properties.reliable: "1"` lifts to `[GameEventReliable]` on the record declaration.</summary>
    [Test]
    public async Task Emit_ReliableProperty_EmitsGameEventReliableAttribute()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "vote_passed", "source": "core.gameevents",
                "properties": { "reliable": "1" }
              }]
            }
            """);

        string src = result.Files["Events/VotePassedEvent"];
        await Assert.That(src).Contains("[GameEventReliable]");
    }

    /// <summary>Neither attribute appears when the corresponding flag is absent.</summary>
    [Test]
    public async Task Emit_NoProperties_OmitsFlagAttributes()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents"
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).DoesNotContain("[GameEventLocal]");
        await Assert.That(src).DoesNotContain("[GameEventReliable]");
    }

    // ── Empty events ──────────────────────────────────────────────────────────

    /// <summary>An event with no `fields` still emits a parameterless record so dispatchers can signal occurrence without a payload.</summary>
    [Test]
    public async Task Emit_NoFields_ProducesEmptyRecord()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{ "name": "demo_stop", "source": "core.gameevents" }]
            }
            """);

        string src = result.Files["Events/DemoStopEvent"];
        await Assert.That(src).Contains("public sealed partial record DemoStopEvent");
        // No `required` field properties at all — the body is just braces.
        await Assert.That(src).DoesNotContain("public required");
    }

    // ── Cross-source duplicate disambiguation ────────────────────────────────

    /// <summary>When the same name appears in multiple sources, the mod variant wins the unsuffixed name and the others get a source suffix.</summary>
    [Test]
    public async Task Emit_DuplicateName_ModWinsUnsuffixed_OthersGetSourceSuffix()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [
                { "name": "player_death", "source": "core.gameevents",
                  "fields": [{ "name": "userid", "type": "int" }] },
                { "name": "player_death", "source": "mod.gameevents",
                  "fields": [{ "name": "userid", "type": "int" }, { "name": "assister", "type": "int" }] }
              ]
            }
            """);

        await Assert.That(result.Files).ContainsKey("Events/PlayerDeathEvent");
        await Assert.That(result.Files).ContainsKey("Events/PlayerDeathCoreEvent");

        string modSrc = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(modSrc).Contains("[GameEventSource(\"mod.gameevents\")]");
        await Assert.That(modSrc).Contains("public required int Assister");

        string coreSrc = result.Files["Events/PlayerDeathCoreEvent"];
        await Assert.That(coreSrc).Contains("[GameEventSource(\"core.gameevents\")]");
        await Assert.That(coreSrc).DoesNotContain("Assister");
    }

    /// <summary>Source-priority ordering is mod > game > core regardless of insertion order in the schema array.</summary>
    [Test]
    public async Task Emit_DuplicateName_PriorityOrderIndependentOfInsertionOrder()
    {
        // Insert mod first, then core, then game — the unsuffixed winner must
        // still be mod (highest priority), not whichever came first.
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [
                { "name": "round_end", "source": "mod.gameevents" },
                { "name": "round_end", "source": "core.gameevents" },
                { "name": "round_end", "source": "game.gameevents" }
              ]
            }
            """);

        string modSrc = result.Files["Events/RoundEndEvent"];
        await Assert.That(modSrc).Contains("[GameEventSource(\"mod.gameevents\")]");
        await Assert.That(result.Files).ContainsKey("Events/RoundEndCoreEvent");
        await Assert.That(result.Files).ContainsKey("Events/RoundEndGameEvent");
    }

    // ── Doc comments + annotations ───────────────────────────────────────────

    /// <summary>An annotation description IS the summary (not wrapped in `para`) so IntelliSense tooltips lead with the curated text instead of the redundant schema name.</summary>
    [Test]
    public async Task Emit_EventAnnotationDescription_BecomesSummary()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "player_death", "source": "core.gameevents",
                "annotations": { "description": "Fired when a player is killed." }
              }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        // Description-first: summary body is the description verbatim, no <para> wrapper.
        await Assert.That(src).Contains("/// <summary>\n///     Fired when a player is killed.\n/// </summary>");
    }

    /// <summary>When the annotation description is used as summary, the schema name moves into remarks as `Native name:` so it isn't lost from rendered docs.</summary>
    [Test]
    public async Task Emit_EventAnnotationDescription_MovesNativeNameToRemarks()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "player_death", "source": "core.gameevents",
                "annotations": { "description": "Fired when a player is killed." }
              }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(src).Contains("Native name: <c>player_death</c>");
        // Specifically, the native-name prefix appears immediately before the
        // `Source:` clause in the remarks (single-line `///` paragraph) so it
        // reads as one sentence.
        await Assert.That(src).Contains("Native name: <c>player_death</c>. Source: <c>core.gameevents</c>");
    }

    /// <summary>Without an annotation description, the remarks block does NOT prefix `Native name:` because the schema name is already the summary text.</summary>
    [Test]
    public async Task Emit_EventWithoutAnnotation_DoesNotPrefixNativeNameInRemarks()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{ "name": "player_death", "source": "core.gameevents" }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(src).DoesNotContain("Native name: <c>player_death</c>");
    }

    /// <summary>Without an annotation description, the summary falls back to the schema name with a terminal period for consistent punctuation.</summary>
    [Test]
    public async Task Emit_EventWithoutAnnotation_SummaryIsSchemaNameWithPeriod()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{ "name": "player_death", "source": "core.gameevents" }]
            }
            """);

        string src = result.Files["Events/PlayerDeathEvent"];
        await Assert.That(src).Contains("/// <summary>\n///     player_death.\n/// </summary>");
        // No relocation when there's no description winning the summary slot.
        await Assert.That(src).DoesNotContain("Native name: <c>player_death</c>");
    }

    /// <summary>Field-level summary precedence: annotation description &gt; KV1 comment &gt; PascalCase property name.</summary>
    [Test]
    public async Task Emit_FieldSummary_AnnotationBeatsCommentBeatsPropName()
    {
        // Three fields: one with annotation+comment, one with comment only, one with neither.
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "fields": [
                  { "name": "a", "type": "string", "comment": "the comment",
                    "annotations": { "description": "the description" } },
                  { "name": "b", "type": "string", "comment": "comment only" },
                  { "name": "c", "type": "string" }
                ]
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        // Annotation wins (terminal period added because description lacked one).
        await Assert.That(src).Contains("///     the description.\n    /// </summary>");
        // Comment wins when annotation absent.
        await Assert.That(src).Contains("///     comment only.\n    /// </summary>");
        // Property name fallback when neither present.
        await Assert.That(src).Contains("///     C.\n    /// </summary>");
    }

    /// <summary>Annotation `warning` is prefixed with a visible marker so IntelliSense surfaces it differently from `notes`.</summary>
    [Test]
    public async Task Emit_EventAnnotationWarning_HasWarningMarker()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "annotations": { "warning": "Deprecated, do not use." }
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains("⚠ Warning: Deprecated, do not use.");
    }

    /// <summary>The event-level `comment` from the source `.gameevents` line is preserved in the remarks block.</summary>
    [Test]
    public async Task Emit_EventComment_AppearsInRemarks()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "x", "source": "core.gameevents",
                "comment": "a game event, name may be 32 charaters long"
              }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains("a game event, name may be 32 charaters long");
    }

    // ── SchemaEvents registry ────────────────────────────────────────────────

    /// <summary>Always emits a SchemaEvents reverse-lookup file when at least one event is present.</summary>
    [Test]
    public async Task Emit_AlwaysEmitsSchemaEventsRegistry()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{ "name": "x", "source": "core.gameevents" }]
            }
            """);

        await Assert.That(result.Files).ContainsKey("SchemaEvents");
        string src = result.Files["SchemaEvents"];
        await Assert.That(src).Contains("public static class SchemaEvents");
        await Assert.That(src).Contains("public static class XEvent");
        await Assert.That(src).Contains("public const string EventName = \"x\";");
    }

    /// <summary>SchemaEvents carries per-field const strings keyed by the C# property name → native KV1 field name.</summary>
    [Test]
    public async Task Emit_SchemaEventsRegistry_HasPerFieldConsts()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{
                "name": "player_death", "source": "mod.gameevents",
                "fields": [
                  { "name": "userid",   "type": "int" },
                  { "name": "dmg_health", "type": "short" }
                ]
              }]
            }
            """);

        string src = result.Files["SchemaEvents"];
        await Assert.That(src).Contains("public const string Userid = \"userid\";");
        await Assert.That(src).Contains("public const string DmgHealth = \"dmg_health\";");
    }

    /// <summary>Empty events list produces no emitted files — no per-event records, no registry.</summary>
    [Test]
    public async Task Emit_EmptyEvents_ProducesNoFiles()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""{ "events": [] }""");
        await Assert.That(result.Files).IsEmpty();
    }

    // ── Schema-revision stamp passthrough ────────────────────────────────────

    /// <summary>When a class-schema stamp is provided, each event file carries the same `Schema revision:` line — one stamp per CS2 build, shared between buckets.</summary>
    [Test]
    public async Task Emit_WithSchemaStamp_PropagatesRevisionLine()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents(
            eventsJson: """
                {
                  "events": [{ "name": "x", "source": "core.gameevents" }]
                }
                """,
            schemaForStampJson: """
                {
                  "revision": 10673343,
                  "version_date": "May 20 2026",
                  "version_time": "15:25:57",
                  "classes": [], "enums": []
                }
                """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).Contains("// Schema revision: 10673343 — May 20 2026 15:25:57");
    }

    /// <summary>Without a stamp source, the schema-revision line is omitted (test fixtures don't need it).</summary>
    [Test]
    public async Task Emit_WithoutSchemaStamp_OmitsRevisionLine()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunGameEvents("""
            {
              "events": [{ "name": "x", "source": "core.gameevents" }]
            }
            """);

        string src = result.Files["Events/XEvent"];
        await Assert.That(src).DoesNotContain("Schema revision:");
    }
}
