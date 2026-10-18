using CS2_OpenDev.Sdk.Generator.Tests.Support;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// The seen-aware table is keyed on real engine class names, so the fixture uses
// them. Synthetic otherwise: one field per policy, nothing from the tracked schema.
public class EntityWrapperEmitterTests
{
    private const string Schema = """
        { "classes": [
            { "name": "CCSPlayerPawn", "projectName": "server", "fields": [
                { "name": "m_fFlags", "type": { "category": "builtin", "name": "uint32" } },
                { "name": "m_iHealth", "type": { "category": "builtin", "name": "int32" } }
            ] },
            { "name": "CBaseCSGrenade", "projectName": "server", "fields": [
                { "name": "m_flThrowStrength", "type": { "category": "builtin", "name": "float32" } }
            ] }
        ] }
        """;

    /// <summary>The <c>SeenAwareFields</c> entries emit nullable properties; a field outside the table keeps the 0 default.</summary>
    [Test]
    public async Task SeenAwareEntries_EmitNullable()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.RunEntityWrappers(Schema, """
            { "op": "addClass", "class": "CCSPlayerPawn" },
            { "op": "addClass", "class": "CBaseCSGrenade" },
            { "op": "addField", "class": "CCSPlayerPawn", "field": "m_fFlags", "targetProperty": "Flags" },
            { "op": "addField", "class": "CCSPlayerPawn", "field": "m_iHealth", "targetProperty": "Health" },
            { "op": "addField", "class": "CBaseCSGrenade", "field": "m_flThrowStrength", "targetProperty": "ThrowStrength" }
            """);

        await Assert.That(result.Diagnostics).IsEmpty();

        string pawn = result.Files["CSPlayerPawn"];
        string grenade = result.Files["BaseCSGrenade"];

        await Assert.That(pawn).Contains("public int? Flags => ");
        await Assert.That(pawn).Contains("public int Health => ");
        await Assert.That(grenade).Contains("public float? ThrowStrength => ");
    }
}
