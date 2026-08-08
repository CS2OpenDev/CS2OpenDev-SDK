#region

using CS2OpenSchema.Protos;

#endregion

namespace CS2OpenDev.Sdk.GameEvents;

/// <summary>
///     Decodes CS2's legacy game-event wire messages into the SDK's typed event
///     records.
/// </summary>
/// <remarks>
///     <para>
///         The wire format splits an event across two messages. A
///         <c>CMsgSource1LegacyGameEventList</c> arrives once and declares, per
///         event id, the event's name and its key names in order. Every subsequent
///         <c>CMsgSource1LegacyGameEvent</c> carries only an event id and a
///         <em>positional</em> list of values. Neither message alone is decodable:
///         without the descriptor table the keys have no names, and the table
///         never repeats.
///     </para>
///     <para>
///         So a decoder is stateful for the lifetime of a demo. Feed it the
///         descriptor list when you see it, then feed it events.
///     </para>
///     <example>
///         <code>
///         var decoder = new GameEventDecoder();
///         decoder.LoadDescriptors(list);            // once, from the demo stream
///
///         if (decoder.TryDecode(msg, out object? ev) &amp;&amp; ev is PlayerDeathEvent death)
///         {
///             Console.WriteLine(death.Attacker);
///         }
///         </code>
///     </example>
/// </remarks>
public sealed class GameEventDecoder
{
    // eventid → (name, ordered key names). The engine reuses ids across demos but
    // not within one, so a plain dictionary keyed on id is correct for a single
    // demo's lifetime.
    private readonly Dictionary<int, Descriptor> _byId = [];

    // Fallback path for streams that carry event_name on the fire itself. Rare,
    // but a demo whose descriptor list was truncated is still partly decodable
    // this way, and returning nothing would be worse than returning what we can.
    private readonly Dictionary<string, Descriptor> _byName = new(StringComparer.Ordinal);

    /// <summary>Number of event descriptors currently loaded.</summary>
    public int DescriptorCount => _byId.Count;

    /// <summary>
    ///     Loads an event-descriptor table. Safe to call more than once; later
    ///     descriptors replace earlier ones for the same id.
    /// </summary>
    public void LoadDescriptors(CMsgSource1LegacyGameEventList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        foreach (CMsgSource1LegacyGameEventList.Types.descriptor_t d in list.Descriptors)
        {
            string[] keys = new string[d.Keys.Count];
            for (int i = 0; i < d.Keys.Count; i++)
            {
                keys[i] = d.Keys[i].Name ?? string.Empty;
            }

            Descriptor descriptor = new(d.Name ?? string.Empty, keys);
            _byId[d.Eventid] = descriptor;
            if (descriptor.Name.Length > 0)
            {
                _byName[descriptor.Name] = descriptor;
            }
        }
    }

    /// <summary>
    ///     Resolves the native event name for a fire, or <see langword="null" />
    ///     when no descriptor has been loaded for it.
    /// </summary>
    public string? ResolveName(CMsgSource1LegacyGameEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        if (_byId.TryGetValue(ev.Eventid, out Descriptor? d) && d.Name.Length > 0)
        {
            return d.Name;
        }

        return string.IsNullOrEmpty(ev.EventName) ? null : ev.EventName;
    }

    /// <summary>
    ///     Decodes a fire into its typed record.
    /// </summary>
    /// <returns>
    ///     <see langword="false" /> when the event has no loaded descriptor, or
    ///     when its name is not one the SDK generates a record for. Both are
    ///     expected during normal parsing — a demo may fire events this SDK build
    ///     predates — so neither throws.
    /// </returns>
    public bool TryDecode(CMsgSource1LegacyGameEvent ev, out object? payload)
    {
        ArgumentNullException.ThrowIfNull(ev);

        payload = null;
        if (!TryResolve(ev, out Descriptor? descriptor) || descriptor is null)
        {
            return false;
        }

        if (!GameEventRegistry.TryGetFactory(descriptor.Name, out GameEventFactory factory))
        {
            return false;
        }

        GameEventReader reader = new(ev, descriptor.KeyNames);
        payload = factory(in reader);
        return true;
    }

    /// <summary>
    ///     Decodes a fire and wraps it with the per-fire transport context the
    ///     caller supplies from the demo container.
    /// </summary>
    /// <typeparam name="T">Expected record type.</typeparam>
    /// <returns>
    ///     <see langword="false" /> when the event cannot be decoded, or decodes
    ///     to a record that is not <typeparamref name="T" />.
    /// </returns>
    public bool TryDecode<T>(
        CMsgSource1LegacyGameEvent ev,
        int gameTick,
        int frameNumber,
        out GameEventEnvelope<T> envelope)
        where T : class
    {
        envelope = default;
        if (!TryDecode(ev, out object? payload) || payload is not T typed)
        {
            return false;
        }

        envelope = new GameEventEnvelope<T>(typed, ev.Eventid, ev.ServerTick, gameTick, frameNumber);
        return true;
    }

    /// <summary>
    ///     Decodes a fire against a specific declaration, for the events whose
    ///     native name has more than one.
    /// </summary>
    /// <remarks>
    ///     <see cref="TryDecode(CMsgSource1LegacyGameEvent, out object?)" />
    ///     resolves to the declaration CS2 actually fires. Use this only when you
    ///     specifically want one of the others — see <see cref="GameEventRegistry" />.
    /// </remarks>
    public bool TryDecodeAs(CMsgSource1LegacyGameEvent ev, GameEventDeclaration declaration, out object? payload)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(declaration);

        payload = null;
        if (!TryResolve(ev, out Descriptor? descriptor) || descriptor is null)
        {
            return false;
        }

        GameEventReader reader = new(ev, descriptor.KeyNames);
        payload = declaration.Factory(in reader);
        return true;
    }

    private bool TryResolve(CMsgSource1LegacyGameEvent ev, out Descriptor? descriptor)
    {
        if (_byId.TryGetValue(ev.Eventid, out descriptor))
        {
            return true;
        }

        return !string.IsNullOrEmpty(ev.EventName) && _byName.TryGetValue(ev.EventName, out descriptor);
    }

    private sealed record Descriptor(string Name, string[] KeyNames);
}
