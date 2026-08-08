#region

using CS2OpenDev.Sdk.GameEvents;

#endregion

namespace CS2OpenDev.Sdk.GameEvents.Tests;

// A default(GameEventReader) is always constructible — it is a public struct —
// so every accessor has to behave on the unbound instance. This is not a
// hypothetical: the generated factories are invoked through a delegate, and
// anything that hands them a default reader (a registry probe, an array slot)
// would otherwise take a NullReferenceException from inside generated code.
public class GameEventReaderTests
{
    /// <summary>An unbound reader reports itself as such.</summary>
    [Test]
    public async Task Default_IsNotBound()
    {
        GameEventReader reader = default;
        await Assert.That(reader.IsBound).IsFalse();
    }

    /// <summary>Every accessor on an unbound reader returns a default rather than throwing.</summary>
    [Test]
    public async Task Default_AccessorsReturnDefaults()
    {
        GameEventReader reader = default;

        await Assert.That(reader.EventName).IsEqualTo(string.Empty);
        await Assert.That(reader.EventId).IsEqualTo(0);
        await Assert.That(reader.ServerTick).IsEqualTo(0);
        await Assert.That(reader.KeyCount).IsEqualTo(0);
        await Assert.That(reader.TryGetKey("anything", out _)).IsFalse();
        await Assert.That(reader.GetString("x")).IsEqualTo(string.Empty);
        await Assert.That(reader.GetInt32("x")).IsEqualTo(0);
        await Assert.That(reader.GetInt16("x")).IsEqualTo((short)0);
        await Assert.That(reader.GetByte("x")).IsEqualTo((byte)0);
        await Assert.That(reader.GetBool("x")).IsFalse();
        await Assert.That(reader.GetFloat("x")).IsEqualTo(0f);
        await Assert.That(reader.GetUInt64("x")).IsEqualTo(0UL);
        await Assert.That(reader.GetHandle("x")).IsEqualTo(0U);
        await Assert.That(reader.GetBytes("x")).IsNull();
        await Assert.That(reader.GetRaw("x")).IsNull();
    }
}
