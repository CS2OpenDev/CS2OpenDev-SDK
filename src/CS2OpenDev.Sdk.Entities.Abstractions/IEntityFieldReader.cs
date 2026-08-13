namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Read surface over one live entity's current field values, produced by a runtime
///     binding an <see cref="EntityClassBinding"/> to its own storage.
/// </summary>
/// <remarks>
///     <para>
///         Every ordinal-taking member addresses a field by its position in the binding's
///         <see cref="EntityClassBinding.CanonicalPaths"/>: ordinal <c>i</c> is the field whose
///         canonical Schema Lens path is <c>CanonicalPaths[i]</c>. Ordinals are an
///         implementation detail shared between a generated wrapper and the manifest emitted
///         beside it; they are not stable across releases, and a runtime that binds against
///         the array rather than against hard-coded numbers is immune to renumbering.
///     </para>
///     <para>
///         <b>Every <c>TryRead*</c> returns <see langword="false"/> when the field has never
///         been received on the wire.</b> "Absent" is distinct from a received default. That
///         distinction is not decoration: <c>m_lifeState</c>'s <c>0</c> means <c>LIFE_ALIVE</c>,
///         so a wrapper that cannot tell "alive" from "never transmitted" reports corpses as
///         healthy. Both read policies a generated property needs — default-when-absent and
///         null-when-absent — are one expression over a <c>TryRead</c>, which is why there are
///         no plain getters to duplicate the surface.
///     </para>
///     <para>
///         What is deliberately <i>not</i> here: storage, decode, and lifetime. No lane, slot,
///         dictionary or buffer appears in this contract; neither does FlattenedSerializer
///         parsing, delta replay, PVS state or baselines. Those are private engineering
///         decisions another parser would legitimately make differently, and a seam that
///         carried them would be one implementation wearing an interface.
///     </para>
/// </remarks>
public interface IEntityFieldReader
{
    /// <summary>The engine class name of the entity being read, e.g. <c>CCSPlayerPawn</c>.</summary>
    string EngineClassName { get; }

    /// <summary>Reads a 32-bit signed integer field.</summary>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadInt32(int ordinal, out int value);

    /// <summary>Reads a 64-bit unsigned integer field.</summary>
    /// <remarks>
    ///     Separate from <see cref="TryReadInt32"/> because the schema genuinely carries
    ///     64-bit fields that a narrower read would truncate — <c>m_steamID</c> is the
    ///     obvious one, and some are declared narrower than they are transmitted.
    /// </remarks>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadUInt64(int ordinal, out ulong value);

    /// <summary>Reads a single-precision float field.</summary>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadSingle(int ordinal, out float value);

    /// <summary>Reads a boolean field.</summary>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadBool(int ordinal, out bool value);

    /// <summary>
    ///     Reads an entity-handle field as its raw packed value, undecoded.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The packing is <c>(serial &lt;&lt; index_bits) | index</c>, and <b>how many bits
    ///         the index gets is not documented authoritatively upstream</b> — see
    ///         <c>docs/HANDLES.md</c>. Two implementations in this ecosystem already disagree
    ///         about it. That is precisely why this returns an undecoded <see cref="uint"/>:
    ///         had the contract decoded handles, one of those two readings would be frozen
    ///         into it.
    ///     </para>
    ///     <para>
    ///         Mask, sentinel encodings and serial validation are the runtime's policy. Turn
    ///         the value into an entity with <see cref="IEntityWorld.Resolve{T}"/>.
    ///     </para>
    /// </remarks>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadEntityHandle(int ordinal, out uint rawHandle);

    /// <summary>Reads a three-component vector field.</summary>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadVector3(int ordinal, out System.Numerics.Vector3 value);

    /// <summary>Reads an Euler-angle field.</summary>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadQAngle(int ordinal, out QAngle value);

    /// <summary>
    ///     Reads any field as a boxed value — the escape for composites with no first-class
    ///     representation here (typed arrays, strings, vectors of handles, sub-structures).
    /// </summary>
    /// <remarks>
    ///     Returns <see langword="true"/> with a <see langword="null"/> value when the wire
    ///     delivered an explicit null, which is why the absent case is the return value rather
    ///     than a null result.
    /// </remarks>
    /// <returns><see langword="false"/> if the field has never been received.</returns>
    bool TryReadObject(int ordinal, out object? value);

    /// <summary>
    ///     Reads a field by its raw wire path, bypassing the ordinal space entirely.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The escape hatch, and the only member that addresses by name rather than by
    ///         ordinal. The Schema Lens covers a curated subset of classes and fields by
    ///         design, so a consumer must be able to reach a field nobody has curated yet
    ///         without waiting on anyone.
    ///     </para>
    ///     <para>
    ///         <paramref name="enginePath"/> is the engine's own spelling, as the demo's
    ///         FlattenedSerializer writes it — <c>m_CBodyComponent.m_pSceneNode.m_vecOrigin</c>,
    ///         not the stable property name. Not for hot paths.
    ///     </para>
    /// </remarks>
    /// <returns><see langword="false"/> if the field has never been received, or the path is unknown.</returns>
    bool TryReadByEnginePath(string enginePath, out object? value);
}
