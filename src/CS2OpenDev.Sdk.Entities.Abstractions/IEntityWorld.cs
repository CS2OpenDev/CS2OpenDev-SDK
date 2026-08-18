namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     The runtime's cross-entity resolution service: turn a raw packed handle into a live
///     wrapper.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately one member. Enumeration, queries by class, seeking and snapshots are
///         all things a parser offers and a consumer gets from their parser. Putting them here
///         would grow this into "a parser interface", which is the over-reach that makes
///         contracts rot. The discipline for the whole abstraction is that it contains exactly
///         the operations generated code emits, and nothing else.
///     </para>
/// </remarks>
public interface IEntityWorld
{
    /// <summary>
    ///     Resolves a raw packed entity handle to its wrapper, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see langword="null"/> covers every way a handle can fail to name a live entity
    ///         of the requested type: the "no entity" sentinels, an empty slot, a stale serial,
    ///         and a target whose actual class is not <typeparamref name="T"/>. Which of those
    ///         apply, and how the index and serial are extracted from
    ///         <paramref name="rawHandle"/>, are the implementation's business; the bit split
    ///         is not authoritatively documented and this contract does not presume it.
    ///     </para>
    ///     <para>
    ///         Constrained to <see cref="EntityWrapper"/> rather than <c>class</c> on purpose.
    ///         A runtime's own resolution generic may well be looser for internal layering
    ///         reasons; that is a fact on its side of the seam. Resolving to something that is
    ///         not an entity wrapper is not a case this contract should admit, and tightening
    ///         a published constraint later is breaking where loosening it is additive.
    ///     </para>
    /// </remarks>
    T? Resolve<T>(uint rawHandle) where T : EntityWrapper;
}
