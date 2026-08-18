using System.Text.RegularExpressions;
using CS2SchemaGen.Models;
using CS2SchemaGen.SchemaLens;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// The canonical form and its hash (lens-canon-1).
//
// The hash is a signature over curated content, and both directions of that
// sentence need proving: identical decisions must hash identically no matter
// when or how often they are replayed, and schema-derived data — which changes
// every time Valve ships — must not be able to move the hash at all. A hash
// that revved on a Valve patch would turn every migration's stateHash into a
// moving target; a hash that missed a curated edit would let history be
// rewritten under a valid signature.
public partial class SchemaLensHashTests
{
    private static LensState StateOf(string changes)
    {
        LensMigration migration = LensMigrationLoader.Parse($$"""
            { "id": "0000-test", "build": "genesis", "stateHash": "sha256:PLACEHOLDER",
              "changes": [ {{changes}} ] }
            """, "0000-test");
        return LensReplay.Replay([migration]).State;
    }

    private const string BaselineChanges = """
        { "op": "addClass", "class": "CThing" },
        { "op": "addField", "class": "CThing", "field": "m_iHealth", "targetProperty": "Health" },
        { "op": "addAlias", "class": "CThing", "canonical": "m_iHealth", "alias": "m_iOldHealth" },
        { "op": "ignoreField", "class": "CThing", "field": "m_iScratch" }
        """;

    [GeneratedRegex("^sha256:[0-9a-f]{64}$")]
    private static partial Regex HashShape();

    /// <summary>Same decisions, same bytes, same hash — replay after replay.</summary>
    [Test]
    public async Task Hash_IsDeterministicAcrossReplays()
    {
        string first = LensCanonicalForm.Hash(StateOf(BaselineChanges));
        string second = LensCanonicalForm.Hash(StateOf(BaselineChanges));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(HashShape().IsMatch(first)).IsTrue();
    }

    /// <summary>The text form leads with its version line — the hash preimage names its own grammar.</summary>
    [Test]
    public async Task Render_LeadsWithTheVersionLine()
    {
        string rendered = LensCanonicalForm.Render(StateOf(BaselineChanges));

        await Assert.That(rendered.StartsWith("lens-canon-1\n", StringComparison.Ordinal)).IsTrue();
        // And carries the path/value grammar the docs promise.
        await Assert.That(rendered).Contains("class/CThing/netName = \"Thing\"\n");
        await Assert.That(rendered).Contains("class/CThing/field/m_iHealth/targetProperty = \"Health\"\n");
        await Assert.That(rendered).Contains("class/CThing/alias/m_iOldHealth = \"m_iHealth\"\n");
        await Assert.That(rendered).Contains("class/CThing/ignored/0 = \"m_iScratch\"\n");
    }

    /// <summary>Every curated ingredient moves the hash: a changed decision cannot hide under an old signature.</summary>
    [Test]
    public async Task Hash_ChangesWhenAnyCuratedValueChanges()
    {
        string baseline = LensCanonicalForm.Hash(StateOf(BaselineChanges));

        string renamedProperty = LensCanonicalForm.Hash(StateOf(BaselineChanges.Replace(
            "\"Health\"", "\"HitPoints\"")));
        string extraAlias = LensCanonicalForm.Hash(StateOf(BaselineChanges
            + """, { "op": "addAlias", "class": "CThing", "canonical": "m_iHealth", "alias": "m_iHp" }"""));
        string extraIgnore = LensCanonicalForm.Hash(StateOf(BaselineChanges
            + """, { "op": "ignoreField", "class": "CThing", "field": "m_iOther" }"""));

        await Assert.That(renamedProperty).IsNotEqualTo(baseline);
        await Assert.That(extraAlias).IsNotEqualTo(baseline);
        await Assert.That(extraIgnore).IsNotEqualTo(baseline);
    }

    /// <summary>
    ///     Schema-derived data cannot move the hash. Two schemas that disagree
    ///     about a field's type and a class's field census produce different
    ///     state.json bodies — different schemaType, different observedFields —
    ///     under the SAME lensHash.
    /// </summary>
    [Test]
    public async Task Hash_IgnoresSchemaDerivedInputs()
    {
        const string changes = """
            { "op": "addClass", "class": "CThing" },
            { "op": "addField", "class": "CThing", "field": "m_flValue", "targetProperty": "Value" }
            """;
        const string schemaA = """
            { "classes": [ { "name": "CThing", "projectName": "server", "fields": [
                { "name": "m_flValue", "type": { "category": "builtin", "name": "float32" } }
            ] } ] }
            """;
        const string schemaB = """
            { "classes": [ { "name": "CThing", "projectName": "server", "fields": [
                { "name": "m_flValue", "type": { "category": "builtin", "name": "float64" } },
                { "name": "m_iBrandNew", "type": { "category": "builtin", "name": "int32" } }
            ] } ] }
            """;

        string stateA = RenderAgainst(changes, schemaA);
        string stateB = RenderAgainst(changes, schemaB);

        // The derived layer genuinely differs…
        await Assert.That(stateA).Contains("\"schemaType\": \"float32\"");
        await Assert.That(stateB).Contains("\"schemaType\": \"float64\"");
        await Assert.That(stateB).Contains("m_iBrandNew");

        // …and the signature does not move.
        string hashA = ExtractLensHash(stateA);
        await Assert.That(hashA).IsEqualTo(ExtractLensHash(stateB));
        await Assert.That(HashShape().IsMatch(hashA)).IsTrue();
    }

    private static string RenderAgainst(string changes, string schemaJson)
    {
        LensState state = StateOf(changes);
        SchemaRoot schema = SchemaModel.Parse(schemaJson);
        LensGateReport gates = LensGates.Run(state, [], schema, committedStateJson: null);

        // A gate failure here would be a broken fixture, not a hash property.
        if (gates.Failures.Count > 0)
        {
            throw new InvalidOperationException(gates.Failures[0].Message);
        }

        return LensStateWriter.Render(state, LensCanonicalForm.Hash(state), schema, gates.Resolution);
    }

    private static string ExtractLensHash(string stateJson)
    {
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stateJson);
        return doc.RootElement.GetProperty("lensHash").GetString()!;
    }
}
