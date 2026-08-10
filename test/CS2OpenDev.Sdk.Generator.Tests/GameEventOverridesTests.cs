using CS2SchemaGen.Models;
using CS2_OpenDev.Sdk.Generator.Tests.Support;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// B4 — consumer override hook.
//
// The load-bearing property is that an override changes the record property and
// the factory that fills it *together*. Changing one without the other emits an
// SDK that does not compile, which is a tolerable failure; changing the type
// without changing the read would be a silent wrong value, which is not.
public class GameEventOverridesTests
{
    private const string EventsJson = """
        {
          "events": [{
            "name": "player_death",
            "source": "mod.gameevents",
            "fields": [
              { "name": "userid",   "type": "player_controller_and_pawn" },
              { "name": "attacker", "type": "player_controller" },
              { "name": "weapon",   "type": "string" }
            ]
          }]
        }
        """;

    private const string OverridesJson = """
        {
          "usings": ["MyGame.Model"],
          "fieldTypes": {
            "player_controller_and_pawn": {
              "csharpType": "PlayerRef",
              "readAs": "Int32",
              "wrap": "new PlayerRef({0})"
            },
            "player_controller": {
              "csharpType": "PlayerRef",
              "readAs": "Int32",
              "wrap": "new PlayerRef({0})"
            }
          }
        }
        """;

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>An overrides file parses into the tag map and the usings list.</summary>
    [Test]
    public async Task Parse_ReadsFieldTypesAndUsings()
    {
        GameEventOverrides o = GameEventOverrides.Parse(OverridesJson);

        await Assert.That(o.FieldTypes.Count).IsEqualTo(2);
        await Assert.That(o.Usings).Contains("MyGame.Model");
        await Assert.That(o.For("player_controller")!.CSharpType).IsEqualTo("PlayerRef");
        await Assert.That(o.For("string")).IsNull();
    }

    /// <summary>An empty document is valid and changes nothing.</summary>
    [Test]
    public async Task Parse_EmptyDocument_IsEmpty()
    {
        await Assert.That(GameEventOverrides.Parse("{}").IsEmpty).IsTrue();
    }

    /// <summary>A readAs that names no reader accessor fails at generation time, not in generated code.</summary>
    [Test]
    public async Task Parse_UnknownReadAs_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventOverrides.Parse("""
                { "fieldTypes": { "short": { "csharpType": "int", "readAs": "Intt" } } }
                """));

        // The message must name the typo and the valid set — the whole point is
        // that a consumer does not have to go read generated code to find it.
        await Assert.That(ex.Message).Contains("Intt");
        await Assert.That(ex.Message).Contains("Int32");
    }

    /// <summary>A field-type entry missing csharpType is rejected.</summary>
    [Test]
    public async Task Parse_MissingCSharpType_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GameEventOverrides.Parse("""
                { "fieldTypes": { "short": { "readAs": "Int32" } } }
                """));

        await Assert.That(ex.Message).Contains("csharpType");
    }

    // ── Applied to emission ──────────────────────────────────────────────────

    /// <summary>Without overrides, the built-in projection is unchanged.</summary>
    [Test]
    public async Task Emit_WithoutOverrides_UsesBuiltInProjection()
    {
        string record = GeneratorHarness.RunGameEvents(EventsJson).Files["Events/PlayerDeathEvent"];
        await Assert.That(record).Contains("public required int UserId { get; init; }");
        await Assert.That(record).DoesNotContain("PlayerRef");
    }

    /// <summary>An override changes the record's property type and pulls in the consumer's using.</summary>
    [Test]
    public async Task Emit_WithOverrides_ChangesRecordPropertyType()
    {
        string record = GeneratorHarness
            .RunGameEvents(EventsJson, overrides: GameEventOverrides.Parse(OverridesJson))
            .Files["Events/PlayerDeathEvent"];
        await Assert.That(record).Contains("using MyGame.Model;");
        await Assert.That(record).Contains("public required PlayerRef UserId { get; init; }");
        await Assert.That(record).Contains("public required PlayerRef Attacker { get; init; }");
        // Untouched tags keep their built-in projection.
        await Assert.That(record).Contains("public required string Weapon { get; init; }");
        // The raw tag is still recorded, so a consumer can still recover the wire shape.
        await Assert.That(record).Contains("[GameEventFieldType(\"player_controller_and_pawn\")]");
    }

    /// <summary>The same override rewrites the factory that fills the property.</summary>
    [Test]
    public async Task Emit_WithOverrides_ChangesFactoryExpression()
    {
        string factories = GeneratorHarness
            .RunGameEventFactories(EventsJson, overrides: GameEventOverrides.Parse(OverridesJson))
            .Files["Generated/GameEventFactories"];
        await Assert.That(factories).Contains("using MyGame.Model;");
        await Assert.That(factories).Contains("UserId = new PlayerRef(reader.GetInt32(\"userid\"))");
        await Assert.That(factories).Contains("Attacker = new PlayerRef(reader.GetInt32(\"attacker\"))");
        await Assert.That(factories).Contains("Weapon = reader.GetString(\"weapon\")");
    }

    /// <summary>
    ///     Record and factory agree on the type for every overridden tag.
    /// </summary>
    /// <remarks>
    ///     The regression this guards: an override applied to only one of the two
    ///     emitters. Asserted by construction rather than by eyeballing both
    ///     outputs, because the failure is silent in the emitter that was missed.
    /// </remarks>
    [Test]
    public async Task Emit_RecordAndFactory_AgreeOnOverriddenType()
    {
        GameEventOverrides o = GameEventOverrides.Parse(OverridesJson);

        string record = GeneratorHarness.RunGameEvents(EventsJson, overrides: o)
            .Files["Events/PlayerDeathEvent"];
        string factory = GeneratorHarness.RunGameEventFactories(EventsJson, overrides: o)
            .Files["Generated/GameEventFactories"];

        foreach (string tag in o.FieldTypes.Keys)
        {
            FieldTypeOverride ov = o.For(tag)!;
            // If the record declares the overridden type, the factory must be
            // building it — not still handing back the built-in projection.
            await Assert.That(record).Contains(ov.CSharpType);
            await Assert.That(factory).Contains("new " + ov.CSharpType + "(");
        }
    }

    /// <summary>A wrap-less override is a pure retype, useful for widening without a wrapper type.</summary>
    [Test]
    public async Task Emit_OverrideWithoutWrap_EmitsBareReaderCall()
    {
        GameEventOverrides o = GameEventOverrides.Parse("""
            { "fieldTypes": { "short": { "csharpType": "int", "readAs": "Int32" } } }
            """);

        string factories = GeneratorHarness.RunGameEventFactories(
            """
            { "events": [{ "name": "e", "source": "mod.gameevents",
              "fields": [{ "name": "f", "type": "short" }] }] }
            """,
            overrides: o).Files["Generated/GameEventFactories"];

        await Assert.That(factories).Contains("F = reader.GetInt32(\"f\")");
    }
}
