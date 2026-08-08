#region

using CS2OpenSchema.Protos;

#endregion

namespace CS2OpenDev.Sdk.GameEvents;

/// <summary>
///     Reads typed values out of a decoded <see cref="CMsgSource1LegacyGameEvent"/>
///     by native key name.
/// </summary>
/// <remarks>
///     <para>
///         The wire message carries a <em>positional</em> key list with no names on
///         it — names live in the <c>CMsgSource1LegacyGameEventList</c> descriptor
///         table the server sends once per demo. This type pairs the two, so a
///         generated factory can ask for <c>"attacker"</c> without knowing it is
///         key 3 of this particular event.
///     </para>
///     <para>
///         A struct holding two references: constructing one per event allocates
///         nothing, which matters because a competitive demo fires on the order of
///         10⁵–10⁶ events.
///     </para>
/// </remarks>
public readonly struct GameEventReader
{
    private readonly CMsgSource1LegacyGameEvent _event;
    private readonly IReadOnlyList<string> _keyNames;

    internal GameEventReader(CMsgSource1LegacyGameEvent ev, IReadOnlyList<string> keyNames)
    {
        _event = ev;
        _keyNames = keyNames;
    }

    /// <summary>
    ///     Whether this reader is bound to an event. <see langword="false" /> for
    ///     a <see langword="default" /> instance.
    /// </summary>
    /// <remarks>
    ///     Structs are always default-constructible, so a public struct has to
    ///     behave for <c>default(GameEventReader)</c> whether or not that is a
    ///     sensible thing to write. Every accessor treats the unbound case as
    ///     "no keys present" and returns defaults, which is the same contract as
    ///     a bound reader whose event omitted the key.
    /// </remarks>
    public bool IsBound => _event is not null && _keyNames is not null;

    /// <summary>The event's native name, as carried by the descriptor table.</summary>
    public string EventName => _event?.EventName ?? string.Empty;

    /// <summary>The engine's event id for this fire.</summary>
    public int EventId => _event?.Eventid ?? 0;

    /// <summary>Server tick the event was fired on, or 0 when the server did not stamp one.</summary>
    public int ServerTick => _event?.ServerTick ?? 0;

    /// <summary>Number of keys present on the wire for this fire.</summary>
    public int KeyCount => _event?.Keys.Count ?? 0;

    /// <summary>
    ///     Locates a key by native name. Absence is normal — the server omits keys
    ///     it has no value for — so this returns <see langword="false"/> rather
    ///     than throwing.
    /// </summary>
    public bool TryGetKey(string name, out CMsgSource1LegacyGameEvent.Types.key_t key)
    {
        if (_event is null || _keyNames is null)
        {
            key = null!;
            return false;
        }

        // Linear scan rather than a dictionary: events carry a handful of keys
        // (the widest in the schema has 18), and a per-event dictionary would
        // cost more to build than the scan costs to walk.
        //
        // Bounded by whichever of the two lists is shorter. A fire may carry more
        // values than the descriptor names (or fewer), and neither is an error —
        // the join is only defined over the overlap.
        int count = _keyNames.Count < _event.Keys.Count ? _keyNames.Count : _event.Keys.Count;
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(_keyNames[i], name, StringComparison.Ordinal))
            {
                key = _event.Keys[i];
                return true;
            }
        }

        key = null!;
        return false;
    }

    /// <summary>The raw key for <paramref name="name"/>, or <see langword="null"/> when absent.</summary>
    public object? GetRaw(string name) => TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key) ? key : null;

    /// <summary>Reads a string-valued key. Absent keys yield the empty string.</summary>
    public string GetString(string name) =>
        TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key) ? key.ValString ?? string.Empty : string.Empty;

    /// <summary>Reads a float-valued key. Absent keys yield 0.</summary>
    public float GetFloat(string name)
    {
        if (!TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key))
        {
            return 0f;
        }

        // A server that has an integral value for a float-declared key writes it
        // to the integer slot rather than converting, so fall through the chain
        // instead of returning 0 for a key that plainly has a value.
        return key.HasValFloat ? key.ValFloat : Widen(key);
    }

    /// <summary>Reads a bool-valued key. Absent keys yield <see langword="false"/>.</summary>
    public bool GetBool(string name)
    {
        if (!TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key))
        {
            return false;
        }

        return key.HasValBool ? key.ValBool : Widen(key) != 0;
    }

    /// <summary>Reads a byte-valued key, saturating rather than wrapping on overflow.</summary>
    public byte GetByte(string name) => (byte)Math.Clamp(GetInt32(name), byte.MinValue, byte.MaxValue);

    /// <summary>Reads a short-valued key, saturating rather than wrapping on overflow.</summary>
    public short GetInt16(string name) => (short)Math.Clamp(GetInt32(name), short.MinValue, short.MaxValue);

    /// <summary>Reads an integer-valued key. Absent keys yield 0.</summary>
    public int GetInt32(string name) =>
        TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key) ? Widen(key) : 0;

    /// <summary>Reads a 64-bit key. Absent keys yield 0.</summary>
    public ulong GetUInt64(string name)
    {
        if (!TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key))
        {
            return 0;
        }

        return key.HasValUint64 ? key.ValUint64 : unchecked((ulong)Widen(key));
    }

    /// <summary>Reads an entity-handle key. Absent keys yield 0.</summary>
    public uint GetHandle(string name) => unchecked((uint)GetInt32(name));

    /// <summary>Reads an opaque byte payload. Absent keys yield <see langword="null"/>.</summary>
    public byte[]? GetBytes(string name) =>
        TryGetKey(name, out CMsgSource1LegacyGameEvent.Types.key_t key) && key.HasValString
            ? System.Text.Encoding.UTF8.GetBytes(key.ValString)
            : null;

    /// <summary>
    ///     Collapses whichever integer slot the server actually used into an
    ///     <see cref="int"/>.
    /// </summary>
    /// <remarks>
    ///     The wire format has four integer widths plus a bool, and the server is
    ///     not obliged to use the one the schema declares — it writes the narrowest
    ///     slot the value fits. Reading only the declared slot silently yields 0
    ///     for values that are plainly present, which is the single most common
    ///     way a hand-rolled decoder goes wrong. Widest-first so a value that
    ///     genuinely needed the wider slot is never truncated.
    /// </remarks>
    private static int Widen(CMsgSource1LegacyGameEvent.Types.key_t key)
    {
        if (key.HasValLong)
        {
            return key.ValLong;
        }

        if (key.HasValShort)
        {
            return key.ValShort;
        }

        if (key.HasValByte)
        {
            return key.ValByte;
        }

        if (key.HasValUint64)
        {
            return unchecked((int)key.ValUint64);
        }

        if (key.HasValBool)
        {
            return key.ValBool ? 1 : 0;
        }

        if (key.HasValFloat)
        {
            return (int)key.ValFloat;
        }

        return 0;
    }
}
