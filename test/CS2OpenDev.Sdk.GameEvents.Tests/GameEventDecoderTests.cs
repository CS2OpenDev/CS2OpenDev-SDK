#region

using CS2OpenDev.Sdk.GameEvents;
using CS2OpenSchema.Events;
using CS2OpenSchema.Protos;

#endregion

namespace CS2OpenDev.Sdk.GameEvents.Tests;

// End-to-end decoder tests, built on real protobuf messages rather than mocks.
//
// The decode path is the thing every CS2 demo parser reimplements, so these
// tests target the parts that are easy to get subtly wrong: the descriptor-table
// join (keys arrive positionally with no names), the integer fallback chain (the
// server writes the narrowest slot a value fits, not the declared one), and the
// duplicate-name resolution.
public class GameEventDecoderTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static CMsgSource1LegacyGameEventList DescriptorList(int eventId, string name, params string[] keys)
    {
        CMsgSource1LegacyGameEventList.Types.descriptor_t descriptor = new()
        {
            Eventid = eventId,
            Name = name
        };
        foreach (string key in keys)
        {
            descriptor.Keys.Add(new CMsgSource1LegacyGameEventList.Types.key_t { Name = key });
        }

        CMsgSource1LegacyGameEventList list = new();
        list.Descriptors.Add(descriptor);
        return list;
    }

    private static CMsgSource1LegacyGameEvent Fire(
        int eventId,
        params CMsgSource1LegacyGameEvent.Types.key_t[] keys)
    {
        CMsgSource1LegacyGameEvent ev = new() { Eventid = eventId };
        ev.Keys.AddRange(keys);
        return ev;
    }

    private static CMsgSource1LegacyGameEvent.Types.key_t Long(int v) => new() { ValLong = v };

    private static CMsgSource1LegacyGameEvent.Types.key_t Short(int v) => new() { ValShort = v };

    private static CMsgSource1LegacyGameEvent.Types.key_t Byte(int v) => new() { ValByte = v };

    private static CMsgSource1LegacyGameEvent.Types.key_t Bool(bool v) => new() { ValBool = v };

    private static CMsgSource1LegacyGameEvent.Types.key_t Str(string v) => new() { ValString = v };

    private static CMsgSource1LegacyGameEvent.Types.key_t Float(float v) => new() { ValFloat = v };

    // ── The core path ────────────────────────────────────────────────────────

    /// <summary>A descriptor table plus a positional fire decodes into the typed record with fields on the right properties.</summary>
    [Test]
    public async Task Decode_PlayerDeath_MapsPositionalKeysByName()
    {
        GameEventDecoder decoder = new();
        // Deliberately not schema order: the decoder must join on the descriptor's
        // key names, not on the order the schema happens to declare fields in.
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "attacker", "userid", "headshot", "weapon"));

        CMsgSource1LegacyGameEvent ev = Fire(55, Long(7), Long(3), Bool(true), Str("ak47"));

        await Assert.That(decoder.TryDecode(ev, out object? payload)).IsTrue();
        PlayerDeathEvent death = (PlayerDeathEvent)payload!;

        await Assert.That(death.Attacker).IsEqualTo(7);
        await Assert.That(death.UserId).IsEqualTo(3);
        await Assert.That(death.Headshot).IsTrue();
        await Assert.That(death.Weapon).IsEqualTo("ak47");
    }

    /// <summary>Keys the server omitted come back as defaults rather than throwing — a fire need not carry every declared key.</summary>
    [Test]
    public async Task Decode_MissingKeys_YieldDefaults()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "attacker"));

        await Assert.That(decoder.TryDecode(Fire(55, Long(7)), out object? payload)).IsTrue();
        PlayerDeathEvent death = (PlayerDeathEvent)payload!;

        await Assert.That(death.Attacker).IsEqualTo(7);
        await Assert.That(death.UserId).IsEqualTo(0);
        await Assert.That(death.Weapon).IsEqualTo(string.Empty);
        await Assert.That(death.Headshot).IsFalse();
    }

    // ── The integer fallback chain ───────────────────────────────────────────
    //
    // This is the failure mode that makes hand-rolled decoders return zeroes for
    // values that are plainly on the wire. `userid` is declared
    // player_controller_and_pawn (an int), but a server with a small value writes
    // val_byte or val_short. Reading only val_long silently yields 0.

    /// <summary>An int-declared field decodes whichever integer slot the server actually used.</summary>
    [Test]
    [Arguments("byte")]
    [Arguments("short")]
    [Arguments("long")]
    public async Task Decode_IntegerField_ReadsWhicheverSlotServerUsed(string slot)
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        CMsgSource1LegacyGameEvent.Types.key_t key = slot switch
        {
            "byte" => Byte(42),
            "short" => Short(42),
            _ => Long(42)
        };

        await Assert.That(decoder.TryDecode(Fire(55, key), out object? payload)).IsTrue();
        await Assert.That(((PlayerDeathEvent)payload!).UserId).IsEqualTo(42);
    }

    /// <summary>A bool-declared field accepts an integer slot, which is how servers commonly encode flags.</summary>
    [Test]
    public async Task Decode_BoolField_AcceptsIntegerEncoding()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "headshot"));

        await Assert.That(decoder.TryDecode(Fire(55, Byte(1)), out object? payload)).IsTrue();
        await Assert.That(((PlayerDeathEvent)payload!).Headshot).IsTrue();
    }

    /// <summary>A float-declared field falls back to an integer slot rather than reporting 0.</summary>
    [Test]
    public async Task Decode_FloatField_FallsBackToIntegerSlot()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(4, "player_blind", "blind_duration"));

        await Assert.That(decoder.TryDecode(Fire(4, Short(3)), out object? payload)).IsTrue();
        await Assert.That(((PlayerBlindEvent)payload!).BlindDuration).IsEqualTo(3f);
    }

    /// <summary>A float in its own slot is read exactly, not routed through the integer chain.</summary>
    [Test]
    public async Task Decode_FloatField_PrefersFloatSlot()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(4, "player_blind", "blind_duration"));

        await Assert.That(decoder.TryDecode(Fire(4, Float(1.75f)), out object? payload)).IsTrue();
        await Assert.That(((PlayerBlindEvent)payload!).BlindDuration).IsEqualTo(1.75f);
    }

    /// <summary>Narrow fields saturate instead of wrapping, so an out-of-range value never silently becomes a plausible small one.</summary>
    [Test]
    public async Task Decode_NarrowField_SaturatesRatherThanWraps()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(1, "achievement_earned", "achievement"));

        // 40000 exceeds short.MaxValue; wrapping would yield a negative number
        // that looks like a legitimate achievement id.
        await Assert.That(decoder.TryDecode(Fire(1, Long(40000)), out object? payload)).IsTrue();
        await Assert.That(((AchievementEarnedEvent)payload!).Achievement).IsEqualTo(short.MaxValue);
    }

    // ── Descriptor-table behaviour ───────────────────────────────────────────

    /// <summary>Without a descriptor table the fire is undecodable — keys have no names — and that is reported, not thrown.</summary>
    [Test]
    public async Task Decode_WithoutDescriptors_ReturnsFalse()
    {
        GameEventDecoder decoder = new();
        await Assert.That(decoder.TryDecode(Fire(55, Long(7)), out object? payload)).IsFalse();
        await Assert.That(payload).IsNull();
    }

    /// <summary>A fire carrying its own event_name decodes even when its id was never in the descriptor table.</summary>
    [Test]
    public async Task Decode_UnknownId_FallsBackToEventName()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        CMsgSource1LegacyGameEvent ev = Fire(999, Long(11));
        ev.EventName = "player_death";

        await Assert.That(decoder.TryDecode(ev, out object? payload)).IsTrue();
        await Assert.That(((PlayerDeathEvent)payload!).UserId).IsEqualTo(11);
    }

    /// <summary>An event the SDK generates no record for is reported, not thrown — a demo may predate this SDK build.</summary>
    [Test]
    public async Task Decode_UnknownEventName_ReturnsFalse()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(77, "some_event_from_the_future", "x"));

        await Assert.That(decoder.TryDecode(Fire(77, Long(1)), out object? _)).IsFalse();
    }

    /// <summary>More wire keys than the descriptor names does not overrun; the extras are ignored.</summary>
    [Test]
    public async Task Decode_MoreKeysThanDescriptorNames_DoesNotOverrun()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        await Assert.That(decoder.TryDecode(Fire(55, Long(5), Long(6), Long(7)), out object? payload)).IsTrue();
        await Assert.That(((PlayerDeathEvent)payload!).UserId).IsEqualTo(5);
    }

    /// <summary>Reloading descriptors replaces the earlier table for the same id.</summary>
    [Test]
    public async Task LoadDescriptors_IsIdempotentPerId()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "attacker"));

        await Assert.That(decoder.DescriptorCount).IsEqualTo(1);
        await Assert.That(decoder.TryDecode(Fire(55, Long(9)), out object? payload)).IsTrue();

        PlayerDeathEvent death = (PlayerDeathEvent)payload!;
        await Assert.That(death.Attacker).IsEqualTo(9);
        await Assert.That(death.UserId).IsEqualTo(0);
    }

    /// <summary>ResolveName reports the native name for a loaded id.</summary>
    [Test]
    public async Task ResolveName_ReturnsNativeName()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        await Assert.That(decoder.ResolveName(Fire(55))).IsEqualTo("player_death");
        await Assert.That(decoder.ResolveName(Fire(999))).IsNull();
    }

    // ── Envelope (B3) ────────────────────────────────────────────────────────

    /// <summary>The typed overload attaches demo transport context the event message does not carry.</summary>
    [Test]
    public async Task Decode_Envelope_CarriesTransportContext()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        CMsgSource1LegacyGameEvent ev = Fire(55, Long(4));
        ev.ServerTick = 12345;

        await Assert.That(decoder.TryDecode(ev, gameTick: 999, frameNumber: 42, out GameEventEnvelope<PlayerDeathEvent> envelope)).IsTrue();

        await Assert.That(envelope.Payload.UserId).IsEqualTo(4);
        await Assert.That(envelope.EventId).IsEqualTo(55);
        await Assert.That(envelope.ServerTick).IsEqualTo(12345);
        await Assert.That(envelope.GameTick).IsEqualTo(999);
        await Assert.That(envelope.FrameNumber).IsEqualTo(42);
    }

    /// <summary>Asking for the wrong record type fails rather than throwing an InvalidCastException at the call site.</summary>
    [Test]
    public async Task Decode_Envelope_WrongTypeReturnsFalse()
    {
        GameEventDecoder decoder = new();
        decoder.LoadDescriptors(DescriptorList(55, "player_death", "userid"));

        await Assert.That(decoder.TryDecode(Fire(55, Long(4)), 0, 0, out GameEventEnvelope<PlayerBlindEvent> _)).IsFalse();
    }
}
