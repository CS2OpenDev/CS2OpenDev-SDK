using CS2SchemaGen.Emitters;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — pure unit tests for GameEventTypeMapper.Map.
//
// The KV1 type-tag vocabulary is small and fixed; this class pins every
// recognised tag's C# projection so an accidental edit (e.g. changing
// "uint64" → "ulong" to "long") is caught immediately. The unknown-tag
// fallback is exercised separately so forward-compat behaviour (don't break
// emission when a new tag shows up) stays explicit.

public class GameEventTypeMapperTests
{
    /// <summary>Each KV1 type tag in the schema vocabulary maps to its canonical C# projection.</summary>
    [Test]
    [Arguments("string", "string")]
    [Arguments("bool", "bool")]
    [Arguments("byte", "byte")]
    [Arguments("short", "short")]
    [Arguments("int", "int")]
    [Arguments("float", "float")]
    [Arguments("uint64", "ulong")]
    [Arguments("ehandle", "uint")]
    public async Task Map_KnownTags_ProjectToExpectedClrTypes(string tag, string expected)
    {
        await Assert.That(GameEventTypeMapper.Map(tag)).IsEqualTo(expected);
    }

    /// <summary>Projects all three player-reference tags to <c>int</c> (the raw userid), per the SDK design decision.</summary>
    [Test]
    [Arguments("player_controller")]
    [Arguments("player_pawn")]
    [Arguments("player_controller_and_pawn")]
    public async Task Map_PlayerRefTags_ProjectToInt(string tag)
    {
        await Assert.That(GameEventTypeMapper.Map(tag)).IsEqualTo("int");
    }

    /// <summary>Maps KV1 <c>long</c> to .NET <c>int</c>: Valve's <c>long</c> is 32-bit, not Int64.</summary>
    [Test]
    public async Task Map_LongTag_IsThirtyTwoBitInt()
    {
        await Assert.That(GameEventTypeMapper.Map("long")).IsEqualTo("int");
    }

    /// <summary>Projects the rare <c>local</c> tag (opaque protobuf blob) to nullable byte array.</summary>
    [Test]
    public async Task Map_LocalTag_IsNullableByteArray()
    {
        await Assert.That(GameEventTypeMapper.Map("local")).IsEqualTo("byte[]?");
    }

    /// <summary>Unknown tags fall through to <c>object?</c> so emission still compiles when upstream introduces a new tag.</summary>
    [Test]
    [Arguments("totally_made_up_tag")]
    [Arguments("future_handle_kind")]
    [Arguments("")]
    public async Task Map_UnknownTag_FallsBackToObjectNullable(string tag)
    {
        await Assert.That(GameEventTypeMapper.Map(tag)).IsEqualTo("object?");
    }
}
