namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Euler angles in the engine's own order and units: degrees, pitch then yaw then roll.
/// </summary>
/// <remarks>
///     <para>
///         Defined here rather than left to <see cref="System.Numerics.Vector3"/> because
///         <c>X/Y/Z</c> names on an angle triple invite exactly the mistake they look like they
///         prevent — the engine's <c>QAngle</c> is not a position, and pitch is not <c>X</c> in
///         any sense a reader should have to remember.
///     </para>
///     <para>
///         This and <see cref="System.Numerics.Vector3"/> are the entire composite vocabulary
///         of <see cref="IEntityFieldReader"/>, closed deliberately at two. They are the
///         composites whose wire encoding is identical for every parser, and whose absence
///         would otherwise leave the most-read fields in the schema — <c>m_vecOrigin</c>,
///         <c>m_angEyeAngles</c> — typed <c>object</c> forever. Everything else goes through
///         <see cref="IEntityFieldReader.TryReadObject"/>. Widening the set later is additive,
///         so starting minimal costs nothing.
///     </para>
/// </remarks>
/// <param name="Pitch">Rotation about the right axis, in degrees. Positive looks down.</param>
/// <param name="Yaw">Rotation about the up axis, in degrees.</param>
/// <param name="Roll">Rotation about the forward axis, in degrees.</param>
public readonly record struct QAngle(float Pitch, float Yaw, float Roll)
{
    /// <summary>All three components zero.</summary>
    public static QAngle Zero => default;
}
