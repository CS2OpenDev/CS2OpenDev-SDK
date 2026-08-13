using System.Numerics;
using CS2OpenDev.Sdk.Entities;

namespace CS2OpenDev.Sdk.Entities.Abstractions.Tests;

// The conformance suite for IEntityFieldReader, exercised against the reference
// implementation. These assertions are the contract's meaning rather than this
// implementation's behaviour, which is why DictionaryEntityReader ships: a runtime
// can point the same cases at its own reader and find out whether it agrees.
//
// The case that matters most is absent-vs-received-default. Everything else here is
// ordinary plumbing; that one is where a reader whose storage does not track
// per-field presence silently reports LIFE_ALIVE for an entity that never sent the
// field at all.
public class ReadContractTests
{
    private const string Origin = "m_CBodyComponent.m_pSceneNode.m_vecOrigin";

    private static EntityClassBinding Binding() => new(
        EngineClass: "CCSPlayerPawn",
        NetName: "CSPlayerPawn",
        CanonicalPaths: ["m_ArmorValue", Origin, "m_angEyeAngles", "m_lifeState", "m_hOwnerEntity", "m_steamID", "m_bSpotted"],
        Aliases: new Dictionary<string, string> { ["m_vecOrigin"] = Origin },
        HandleOrdinals: [4]);

    private static DictionaryEntityReader Reader(Dictionary<string, object?> values) =>
        new(Binding(), values);

    // ── Absent, received-null, and present are three different things ─────────

    /// <summary>A field never received reads as absent from every typed accessor — not as the type's default.</summary>
    [Test]
    public async Task NeverReceived_ReadsAsAbsentRatherThanDefault()
    {
        DictionaryEntityReader reader = Reader([]);

        await Assert.That(reader.TryReadInt32(3, out int life)).IsFalse();
        await Assert.That(life).IsEqualTo(0); // the out value is default, but the return said so
        await Assert.That(reader.TryReadObject(3, out _)).IsFalse();
    }

    /// <summary>A received zero is reported as present, so a consumer can tell LIFE_ALIVE from silence.</summary>
    [Test]
    public async Task ReceivedZero_IsPresent()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_lifeState"] = 0 });

        await Assert.That(reader.TryReadInt32(3, out int life)).IsTrue();
        await Assert.That(life).IsEqualTo(0);
    }

    /// <summary>An explicitly transmitted null is present to the boxed reader and absent to the typed ones, which have no value to return.</summary>
    [Test]
    public async Task ReceivedNull_IsPresentToObjectReadAndAbsentToTypedReads()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_ArmorValue"] = null });

        await Assert.That(reader.TryReadObject(0, out object? boxed)).IsTrue();
        await Assert.That(boxed).IsNull();
        await Assert.That(reader.TryReadInt32(0, out _)).IsFalse();
    }

    // ── Ordinal addressing ────────────────────────────────────────────────────

    /// <summary>Ordinals outside the binding's space read as absent instead of throwing, so a stale wrapper degrades rather than crashing.</summary>
    [Test]
    [Arguments(-1)]
    [Arguments(7)]
    [Arguments(999)]
    public async Task OrdinalOutsideTheBinding_ReadsAsAbsent(int ordinal)
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_ArmorValue"] = 100 });

        await Assert.That(reader.TryReadInt32(ordinal, out _)).IsFalse();
    }

    /// <summary>Each ordinal addresses the canonical path at that index.</summary>
    [Test]
    public async Task OrdinalsAddressTheirCanonicalPath()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?>
        {
            ["m_ArmorValue"] = 100,
            [Origin] = new Vector3(1, 2, 3),
        });

        await Assert.That(reader.TryReadInt32(0, out int armor)).IsTrue();
        await Assert.That(armor).IsEqualTo(100);
        await Assert.That(reader.TryReadVector3(1, out Vector3 origin)).IsTrue();
        await Assert.That(origin).IsEqualTo(new Vector3(1, 2, 3));
    }

    // ── Typed reads ───────────────────────────────────────────────────────────

    /// <summary>Reads a 64-bit field without truncating it, which is why the contract carries a UInt64 accessor at all.</summary>
    [Test]
    public async Task UInt64_ReadsWideValuesIntact()
    {
        const ulong steamId = 76561198000000000UL;
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_steamID"] = steamId });

        await Assert.That(reader.TryReadUInt64(5, out ulong actual)).IsTrue();
        await Assert.That(actual).IsEqualTo(steamId);
    }

    /// <summary>Accepts the engine's integer encoding of a boolean as well as a real bool — the wire has no bool type.</summary>
    [Test]
    [Arguments(1, true)]
    [Arguments(0, false)]
    public async Task Bool_AcceptsTheWiresIntegerEncoding(int wire, bool expected)
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_bSpotted"] = wire });

        await Assert.That(reader.TryReadBool(6, out bool actual)).IsTrue();
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Reads a handle as its raw packed value, undecoded — no mask, no sentinel interpretation.</summary>
    [Test]
    public async Task EntityHandle_CrossesUndecoded()
    {
        const uint packed = 0x0004_2A1Fu;
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_hOwnerEntity"] = packed });

        await Assert.That(reader.TryReadEntityHandle(4, out uint raw)).IsTrue();
        await Assert.That(raw).IsEqualTo(packed);
    }

    /// <summary>QAngle round-trips in the engine's own component order.</summary>
    [Test]
    public async Task QAngle_RoundTripsPitchYawRoll()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?>
        {
            ["m_angEyeAngles"] = new QAngle(10f, 20f, 30f),
        });

        await Assert.That(reader.TryReadQAngle(2, out QAngle a)).IsTrue();
        await Assert.That(a.Pitch).IsEqualTo(10f);
        await Assert.That(a.Yaw).IsEqualTo(20f);
        await Assert.That(a.Roll).IsEqualTo(30f);
    }

    /// <summary>A value of the wrong shape reads as absent rather than being coerced into nonsense.</summary>
    [Test]
    public async Task WrongShape_ReadsAsAbsent()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { ["m_angEyeAngles"] = "not an angle" });

        await Assert.That(reader.TryReadQAngle(2, out _)).IsFalse();
        await Assert.That(reader.TryReadVector3(2, out _)).IsFalse();
    }

    // ── The engine-path escape hatch ──────────────────────────────────────────

    /// <summary>Reads a field by its canonical wire path, bypassing the ordinal space.</summary>
    [Test]
    public async Task ByEnginePath_ReadsTheCanonicalSpelling()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { [Origin] = new Vector3(4, 5, 6) });

        await Assert.That(reader.TryReadByEnginePath(Origin, out object? v)).IsTrue();
        await Assert.That(v).IsEqualTo(new Vector3(4, 5, 6));
    }

    /// <summary>Resolves a historical spelling through the binding's alias table — the mechanism that lets a current wrapper read a demo recorded before a rename.</summary>
    [Test]
    public async Task ByEnginePath_ResolvesAHistoricalSpelling()
    {
        DictionaryEntityReader reader = Reader(new Dictionary<string, object?> { [Origin] = new Vector3(7, 8, 9) });

        await Assert.That(reader.TryReadByEnginePath("m_vecOrigin", out object? v)).IsTrue();
        await Assert.That(v).IsEqualTo(new Vector3(7, 8, 9));
    }

    /// <summary>An unknown path reads as absent.</summary>
    [Test]
    public async Task ByEnginePath_UnknownPathIsAbsent()
    {
        DictionaryEntityReader reader = Reader([]);

        await Assert.That(reader.TryReadByEnginePath("m_nowhere", out _)).IsFalse();
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Reports the binding's engine class by default, and an override when one entity is read through a base class's binding.</summary>
    [Test]
    public async Task EngineClassName_DefaultsToTheBindingAndCanBeOverridden()
    {
        await Assert.That(Reader([]).EngineClassName).IsEqualTo("CCSPlayerPawn");

        DictionaryEntityReader subclass = new(Binding(), new Dictionary<string, object?>(), "CCSPlayerPawnBase");
        await Assert.That(subclass.EngineClassName).IsEqualTo("CCSPlayerPawnBase");
    }
}
