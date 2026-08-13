namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Everything a runtime needs to serve one generated wrapper class, as pure data.
/// </summary>
/// <remarks>
///     <para>
///         Emitted from the Schema Lens <c>state.json</c> alongside the wrapper it describes.
///         A runtime consumes it once, at class-bind time, to build its own
///         <c>ordinal → wherever-it-keeps-that-field</c> map; nothing here is touched on a
///         read.
///     </para>
///     <para>
///         Deliberately carries no factory delegate. An earlier draft held a
///         <c>Func&lt;IEntityFieldReader, IEntityWorld, EntityWrapper&gt;</c>, which made the
///         manifest un-serialisable, un-inspectable, impossible to enumerate statically, and
///         awkward for trimming and AOT — in a package whose entire job is to be consumed by
///         other people's runtimes. Construction lives on the generated registry instead, and
///         this stays data. The payoff shows up immediately in testing: every invariant below
///         can be checked without constructing anything.
///     </para>
/// </remarks>
/// <param name="EngineClass">The engine's class name, e.g. <c>CCSPlayerPawn</c>.</param>
/// <param name="NetName">The stable .NET name the wrapper type carries, e.g. <c>CSPlayerPawn</c>.</param>
/// <param name="CanonicalPaths">
///     Ordinal to canonical Schema Lens field path, dense from zero. This array <i>defines</i>
///     the ordinal space the wrapper's property bodies address. The wrapper and this array are
///     emitted from the same state into the same assembly and agree by construction; a runtime
///     that binds against the array — the only supported way — is unaffected by any
///     renumbering between releases.
/// </param>
/// <param name="Aliases">
///     Historical wire spellings: alias engine path to the canonical path it now means. A
///     runtime binding an ordinal tries the canonical path against the demo's serializer first,
///     then any alias targeting it. This is what lets a wrapper generated today read a demo
///     recorded before Valve renamed the field — demos are historical artifacts, and the Lens
///     alias table is the only structure in this ecosystem that records the old spellings.
/// </param>
/// <param name="HandleOrdinals">
///     Ordinals of the entity-handle-typed fields, so a runtime can walk the entity reference
///     graph generically — snapshot freezing, relationship inspection, cycle detection — with
///     no per-class generated hook. Data doing a job emitted code would otherwise do, which is
///     strictly more portable.
/// </param>
public sealed record EntityClassBinding(
    string EngineClass,
    string NetName,
    IReadOnlyList<string> CanonicalPaths,
    IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyList<int> HandleOrdinals);
