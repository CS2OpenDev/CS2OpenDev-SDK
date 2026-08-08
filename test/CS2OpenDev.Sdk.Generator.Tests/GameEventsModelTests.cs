using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — pure unit tests for GameEventsModel.Parse.
//
// Schema shape lives in upstream/docs/generated/downstream-codegen-schemas/
// gameevents_schema.json. These tests pin the small, fixed parser surface
// (top-level events list, per-event flags + fields, annotations) and the
// upstream-specific quirks (KV1 stringified bool flags) so a future schema
// format change is caught immediately.

public class GameEventsModelTests
{
    // ── Top-level ────────────────────────────────────────────────────────────

    /// <summary>Parses an empty events array into an empty <see cref="GameEventsRoot"/>.</summary>
    [Test]
    public async Task Parse_EmptyEvents()
    {
        GameEventsRoot root = GameEventsModel.Parse("""{ "events": [] }""");
        await Assert.That(root.Events).IsEmpty();
    }

    /// <summary>Treats a top-level document missing the <c>events</c> key as an empty registry.</summary>
    [Test]
    public async Task Parse_MissingEventsKey_YieldsEmptyRegistry()
    {
        GameEventsRoot root = GameEventsModel.Parse("""{ }""");
        await Assert.That(root.Events).IsEmpty();
    }

    // ── Event shape ──────────────────────────────────────────────────────────

    /// <summary>Surfaces every basic field on an event record (name, source, comment, fields).</summary>
    [Test]
    public async Task Parse_BasicEvent_PopulatesScalarFields()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "player_death",
                "comment": "fired on kill",
                "source": "core.gameevents",
                "fields": [
                  { "name": "userid", "type": "player_controller_and_pawn", "comment": "user ID who died" }
                ]
              }]
            }
            """);

        GameEventModel ev = root.Events[0];
        await Assert.That(ev.Name).IsEqualTo("player_death");
        await Assert.That(ev.Comment).IsEqualTo("fired on kill");
        await Assert.That(ev.Source).IsEqualTo("core.gameevents");
        await Assert.That(ev.Fields.Length).IsEqualTo(1);
        await Assert.That(ev.Fields[0].Name).IsEqualTo("userid");
        await Assert.That(ev.Fields[0].Type).IsEqualTo("player_controller_and_pawn");
        await Assert.That(ev.Fields[0].Comment).IsEqualTo("user ID who died");
    }

    /// <summary>Omits optional keys (comment, fields, annotations) without throwing.</summary>
    [Test]
    public async Task Parse_EventWithNoOptionalKeys_ProducesEmptyDefaults()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "demo_stop",
                "source": "core.gameevents"
              }]
            }
            """);

        GameEventModel ev = root.Events[0];
        await Assert.That(ev.Comment).IsNull();
        await Assert.That(ev.Fields).IsEmpty();
        await Assert.That(ev.Annotations).IsNull();
        await Assert.That(ev.Local).IsFalse();
        await Assert.That(ev.Reliable).IsFalse();
    }

    // ── KV1 stringified bool flags ───────────────────────────────────────────
    //
    // Upstream serialises `properties.local` / `properties.reliable` as the
    // strings "1" / "0" (a KV1 idiosyncrasy carried through the JSON mirror).
    // The parser deliberately accepts numbers, native booleans, and strings so
    // a future format normalisation upstream doesn't silently drop the flag.

    /// <summary>Reads <c>properties.local: "1"</c> as Local=true (KV1 stringified bool).</summary>
    [Test]
    public async Task Parse_LocalFlag_StringOne_IsTrue()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "demo_start", "source": "core.gameevents",
                "properties": { "local": "1" }
              }]
            }
            """);
        await Assert.That(root.Events[0].Local).IsTrue();
    }

    /// <summary>Reads <c>properties.reliable: "1"</c> as Reliable=true.</summary>
    [Test]
    public async Task Parse_ReliableFlag_StringOne_IsTrue()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "vote_passed", "source": "core.gameevents",
                "properties": { "reliable": "1" }
              }]
            }
            """);
        await Assert.That(root.Events[0].Reliable).IsTrue();
    }

    /// <summary>Treats <c>properties.local: "0"</c> as Local=false (KV1 stringified zero).</summary>
    [Test]
    public async Task Parse_LocalFlag_StringZero_IsFalse()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "properties": { "local": "0" }
              }]
            }
            """);
        await Assert.That(root.Events[0].Local).IsFalse();
    }

    /// <summary>Empty <c>properties</c> object leaves both flags false.</summary>
    [Test]
    public async Task Parse_EmptyProperties_LeavesFlagsFalse()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "properties": { }
              }]
            }
            """);
        await Assert.That(root.Events[0].Local).IsFalse();
        await Assert.That(root.Events[0].Reliable).IsFalse();
    }

    /// <summary>Forward-compat: numeric <c>properties.local: 1</c> is also accepted.</summary>
    [Test]
    public async Task Parse_LocalFlag_NumberOne_IsTrue()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "properties": { "local": 1 }
              }]
            }
            """);
        await Assert.That(root.Events[0].Local).IsTrue();
    }

    /// <summary>Forward-compat: native JSON boolean <c>properties.local: true</c> is also accepted.</summary>
    [Test]
    public async Task Parse_LocalFlag_NativeBoolTrue_IsTrue()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "properties": { "local": true }
              }]
            }
            """);
        await Assert.That(root.Events[0].Local).IsTrue();
    }

    // ── Annotations ──────────────────────────────────────────────────────────

    /// <summary>Reads event-level <c>annotations.description</c> into the model.</summary>
    [Test]
    public async Task Parse_EventAnnotation_DescriptionOnly()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "annotations": { "description": "fires when X happens" }
              }]
            }
            """);
        Annotations? ann = root.Events[0].Annotations;
        await Assert.That(ann).IsNotNull();
        await Assert.That(ann!.Description).IsEqualTo("fires when X happens");
        await Assert.That(ann.Notes).IsNull();
        await Assert.That(ann.Warning).IsNull();
    }

    /// <summary>Reads field-level annotations independently of the parent event's annotations.</summary>
    [Test]
    public async Task Parse_FieldAnnotation_IsParsedSeparately()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "fields": [{
                  "name": "userid", "type": "int",
                  "annotations": { "notes": "1-based; 0 means server" }
                }]
              }]
            }
            """);

        await Assert.That(root.Events[0].Annotations).IsNull();
        Annotations? fieldAnn = root.Events[0].Fields[0].Annotations;
        await Assert.That(fieldAnn).IsNotNull();
        await Assert.That(fieldAnn!.Notes).IsEqualTo("1-based; 0 means server");
    }

    /// <summary>An annotations block carrying no recognised keys is collapsed to null so emitters can skip cheaply.</summary>
    [Test]
    public async Task Parse_EmptyAnnotation_CollapsesToNull()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [{
                "name": "x", "source": "y.gameevents",
                "annotations": { }
              }]
            }
            """);
        await Assert.That(root.Events[0].Annotations).IsNull();
    }

    // ── Multiple events ──────────────────────────────────────────────────────

    /// <summary>Preserves insertion order of the events array — important for the source-priority disambiguator downstream.</summary>
    [Test]
    public async Task Parse_MultipleEvents_PreserveOrder()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            {
              "events": [
                { "name": "first",  "source": "core.gameevents" },
                { "name": "second", "source": "mod.gameevents" },
                { "name": "third",  "source": "game.gameevents" }
              ]
            }
            """);

        await Assert.That(root.Events.Length).IsEqualTo(3);
        await Assert.That(root.Events[0].Name).IsEqualTo("first");
        await Assert.That(root.Events[1].Name).IsEqualTo("second");
        await Assert.That(root.Events[2].Name).IsEqualTo("third");
    }

    // ── schema_format_version guard (CS2_GEN_004) ────────────────────────────
    //
    // gameevents_schema.json carries the same header key as cs2_schema.json and
    // moved to 2.0 in the same cutover, where field types went from KV1 integer
    // tags to named strings. Guarded independently so a future drift between the
    // two files still reports by name.

    /// <summary>A game-events schema declaring a different format major fails with the migration diagnostic.</summary>
    [Test]
    public async Task Parse_UnsupportedFormatMajor_ThrowsWithDiagnosticText()
    {
        NotSupportedException? ex = Assert.Throws<NotSupportedException>(() =>
            GameEventsModel.Parse("""
                { "schema_format_version": "2.0", "events": [] }
                """));

        await Assert.That(ex.Message).Contains("2.0");
        await Assert.That(ex.Message).Contains("docs/upstream/schematracker-migration.md");
    }

    /// <summary>Fixtures omit the key; absence must not block a parse.</summary>
    [Test]
    public async Task Parse_MissingFormatVersion_IsAccepted()
    {
        GameEventsRoot root = GameEventsModel.Parse("""
            { "events": [{ "name": "player_death", "source": "game.gameevents" }] }
            """);
        await Assert.That(root.Events.Length).IsEqualTo(1);
    }
}
