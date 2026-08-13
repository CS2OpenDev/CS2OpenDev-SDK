using CS2OpenDev.Sdk.Entities;

namespace CS2OpenDev.Sdk.Entities.Abstractions.Tests;

// Structural checks on the manifest. Every case here is reachable without
// constructing a wrapper, a reader or a world — which is the practical payoff of
// EntityClassBinding being pure data rather than carrying a factory delegate.
public class BindingConformanceTests
{
    private const string Origin = "m_CBodyComponent.m_pSceneNode.m_vecOrigin";

    private static EntityClassBinding Valid() => new(
        EngineClass: "CCSPlayerPawn",
        NetName: "CSPlayerPawn",
        CanonicalPaths: ["m_ArmorValue", Origin, "m_hOwnerEntity"],
        Aliases: new Dictionary<string, string> { ["m_vecOrigin"] = Origin },
        HandleOrdinals: [2]);

    /// <summary>A well-formed binding reports no problems.</summary>
    [Test]
    public async Task ValidBinding_HasNoProblems()
    {
        await Assert.That(BindingConformance.Validate(Valid())).IsEmpty();
    }

    /// <summary>Two ordinals addressing one field is caught: nothing would crash, one generated property would silently be a copy of another.</summary>
    [Test]
    public async Task DuplicateCanonicalPath_IsReported()
    {
        // The alias goes too: it targets a path this binding no longer carries, so
        // leaving it in would report a second, unrelated problem and stop this test
        // isolating the duplicate.
        EntityClassBinding binding = Valid() with
        {
            CanonicalPaths = ["m_ArmorValue", "m_ArmorValue", "m_hOwnerEntity"],
            Aliases = new Dictionary<string, string>(),
        };

        IReadOnlyList<string> problems = BindingConformance.Validate(binding);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("duplicates");
    }

    /// <summary>An empty path breaks the density the ordinal space depends on.</summary>
    [Test]
    public async Task EmptyCanonicalPath_IsReported()
    {
        EntityClassBinding binding = Valid() with
        {
            CanonicalPaths = ["m_ArmorValue", "", "m_hOwnerEntity"],
        };

        await Assert.That(BindingConformance.Validate(binding).Any(p => p.Contains("dense"))).IsTrue();
    }

    /// <summary>An alias pointing outside the ordinal space can never resolve — and would only show up on old demos, which is the worst time to find it.</summary>
    [Test]
    public async Task AliasTargetingAnUnknownPath_IsReported()
    {
        EntityClassBinding binding = Valid() with
        {
            Aliases = new Dictionary<string, string> { ["m_vecOrigin"] = "m_somewhereElse" },
        };

        await Assert.That(BindingConformance.Validate(binding)[0]).Contains("can never resolve");
    }

    /// <summary>An alias whose key is also a live canonical path would shadow the real field.</summary>
    [Test]
    public async Task AliasShadowingALiveField_IsReported()
    {
        EntityClassBinding binding = Valid() with
        {
            Aliases = new Dictionary<string, string> { ["m_ArmorValue"] = Origin },
        };

        await Assert.That(BindingConformance.Validate(binding).Any(p => p.Contains("shadow"))).IsTrue();
    }

    /// <summary>Handle ordinals outside the ordinal space are caught, since a generic graph walk would index straight off the end.</summary>
    [Test]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task HandleOrdinalOutOfRange_IsReported(int ordinal)
    {
        EntityClassBinding binding = Valid() with { HandleOrdinals = [ordinal] };

        await Assert.That(BindingConformance.Validate(binding)[0]).Contains("outside the ordinal space");
    }

    /// <summary>Duplicate handle ordinals would make a graph walk visit one edge twice.</summary>
    [Test]
    public async Task DuplicateHandleOrdinals_AreReported()
    {
        EntityClassBinding binding = Valid() with { HandleOrdinals = [2, 2] };

        await Assert.That(BindingConformance.Validate(binding)[0]).Contains("duplicates");
    }

    /// <summary>Identity fields must be populated.</summary>
    [Test]
    public async Task EmptyIdentity_IsReported()
    {
        EntityClassBinding binding = Valid() with { EngineClass = "", NetName = "  " };

        await Assert.That(BindingConformance.Validate(binding).Count).IsEqualTo(2);
    }

    /// <summary>Every problem across every binding is reported at once, because a malformed manifest usually has one cause with several symptoms.</summary>
    [Test]
    public async Task ThrowIfInvalid_NamesEveryProblemAndItsClass()
    {
        EntityClassBinding broken = Valid() with
        {
            EngineClass = "CBroken",
            HandleOrdinals = [99],
            Aliases = new Dictionary<string, string> { ["m_old"] = "m_nowhere" },
        };

        InvalidOperationException? ex = null;
        try
        {
            BindingConformance.ThrowIfInvalid([Valid(), broken]);
        }
        catch (InvalidOperationException caught)
        {
            ex = caught;
        }

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("CBroken");
        await Assert.That(ex.Message).Contains("outside the ordinal space");
        await Assert.That(ex.Message).Contains("can never resolve");
    }

    /// <summary>A conforming set passes silently.</summary>
    [Test]
    public async Task ThrowIfInvalid_AcceptsAConformingSet()
    {
        Exception? thrown = null;
        try
        {
            BindingConformance.ThrowIfInvalid([Valid(), Valid() with { EngineClass = "COther" }]);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        await Assert.That(thrown).IsNull();
    }
}
