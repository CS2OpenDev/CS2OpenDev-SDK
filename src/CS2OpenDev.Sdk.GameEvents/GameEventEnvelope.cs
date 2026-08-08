namespace CS2OpenDev.Sdk.GameEvents;

/// <summary>
///     Constructs a typed event record from a decoded game event.
/// </summary>
/// <remarks>
///     Takes the reader by <see langword="in" /> so dispatching through the
///     registry does not copy it.
/// </remarks>
public delegate object GameEventFactory(in GameEventReader reader);

/// <summary>
///     One declaration of a native event name: which <c>.gameevents</c> file
///     declared it, the record type it materialises as, and its factory.
/// </summary>
/// <param name="Source">Originating file, e.g. <c>mod.gameevents</c>.</param>
/// <param name="RecordType">The generated record type this declaration produces.</param>
/// <param name="Factory">Constructs that record from a decoded event.</param>
/// <remarks>
///     Exists because native event names are not unique — see
///     <see cref="GameEventRegistry" />.
/// </remarks>
public sealed record GameEventDeclaration(string Source, Type RecordType, GameEventFactory Factory);

/// <summary>
///     A materialised event plus the per-fire transport context that is not part
///     of the event's own payload.
/// </summary>
/// <typeparam name="T">The generated event record type.</typeparam>
/// <remarks>
///     <para>
///         The generated records model exactly what the schema declares, and
///         nothing else — <c>player_death</c> has an <c>attacker</c> because the
///         schema says so. When it happened is a property of the fire, not of the
///         event, and it comes from the demo container rather than the event
///         message. Folding tick and frame numbers into the records would put
///         transport concerns inside payload shapes and make them wrong for any
///         consumer reading events from somewhere other than a demo file.
///     </para>
///     <para>
///         Records are also <c>partial</c>, so a consumer who would rather carry
///         this context on the record itself can add it in a sibling file — the
///         properties are init-only and property-based rather than positional, so
///         a partial can extend them without touching a primary constructor. This
///         envelope is the recommended shape, not the only one.
///     </para>
/// </remarks>
public readonly record struct GameEventEnvelope<T>
    where T : class
{
    /// <summary>Creates an envelope around a materialised event record.</summary>
    public GameEventEnvelope(T payload, int eventId, int serverTick, int gameTick, int frameNumber)
    {
        Payload = payload;
        EventId = eventId;
        ServerTick = serverTick;
        GameTick = gameTick;
        FrameNumber = frameNumber;
    }

    /// <summary>The decoded event.</summary>
    public T Payload { get; }

    /// <summary>The engine's event id for this fire.</summary>
    public int EventId { get; }

    /// <summary>Server tick the event was stamped with, or 0 when unstamped.</summary>
    public int ServerTick { get; }

    /// <summary>
    ///     Demo tick this fire was observed on. Supplied by the caller from the
    ///     demo container — the event message does not carry it.
    /// </summary>
    public int GameTick { get; }

    /// <summary>
    ///     Demo frame this fire was observed in. Supplied by the caller, as with
    ///     <see cref="GameTick" />.
    /// </summary>
    public int FrameNumber { get; }

    /// <summary>Unwraps the payload.</summary>
    public static implicit operator T(GameEventEnvelope<T> envelope) => envelope.Payload;
}
