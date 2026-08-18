namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Base class for a generated typed entity wrapper. Holds the reader it projects and the
///     world it resolves handles against, and adds nothing else.
/// </summary>
/// <remarks>
///     <para>
///         Generated property bodies are one expression over <see cref="Reader"/>, with a read
///         policy chosen per field:
///     </para>
///     <code>
///         public int Health     =&gt; Reader.TryReadInt32(Ord.Health, out int v) ? v : 0;
///         public int? LifeState =&gt; Reader.TryReadInt32(Ord.LifeState, out int v) ? v : null;
///     </code>
///     <para>
///         The two differ because <c>0</c> is a meaningful received value for
///         <c>m_lifeState</c> and a harmless default for <c>m_iHealth</c>. Which policy a field
///         gets is an emit decision recorded beside the generator, not something a runtime is
///         asked about.
///     </para>
/// </remarks>
/// <param name="reader">The bound read surface for one entity.</param>
/// <param name="world">The runtime's handle-resolution service.</param>
public abstract class EntityWrapper(IEntityFieldReader reader, IEntityWorld world)
{
    /// <summary>The bound read surface for the wrapped entity.</summary>
    protected IEntityFieldReader Reader { get; } = reader
        ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>The runtime service used to resolve entity handles this wrapper exposes.</summary>
    protected IEntityWorld World { get; } = world
        ?? throw new ArgumentNullException(nameof(world));

    /// <summary>The engine class name of the wrapped entity, e.g. <c>CCSPlayerPawn</c>.</summary>
    public string EngineClassName => Reader.EngineClassName;

    /// <summary>
    ///     Reads any field by its raw wire path, including fields no wrapper property covers.
    /// </summary>
    /// <remarks>
    ///     The curated set is a subset by design, so this is how a consumer reaches the rest
    ///     without waiting on curation. Returns <see langword="null"/> both for "never received"
    ///     and for "received as null"; use
    ///     <see cref="IEntityFieldReader.TryReadByEnginePath"/> directly when that difference
    ///     matters.
    /// </remarks>
    public object? this[string enginePath]
        => Reader.TryReadByEnginePath(enginePath, out object? v) ? v : null;
}
