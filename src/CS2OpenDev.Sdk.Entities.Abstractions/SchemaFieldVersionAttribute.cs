namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Records the CS2 build range over which the field behind a generated wrapper property
///     existed, so a consumer replaying an older demo can tell what to expect.
/// </summary>
/// <remarks>
///     <para>
///         Demos are historical artifacts. A wrapper generated against today's schema is
///         routinely pointed at a recording from before a field existed, and the property has
///         to mean something in that case — which is why post-launch fields are emitted
///         nullable. This attribute is how a consumer finds out which those are without
///         reading the migrations.
///     </para>
///     <para>
///         Named for the field rather than the schema because it describes one field's history,
///         not the schema version the assembly was built from. That belongs on the generated
///         registry, once per assembly, alongside the Lens hash.
///     </para>
/// </remarks>
/// <param name="since">
///     The CS2 build the field first appeared in, or <c>genesis</c> for fields present when the
///     Lens began tracking the class. <c>genesis</c> is deliberately not a build number: the
///     Lens starts at the schema it was created against and does not replay CS2's history, so
///     claiming a specific first build for those fields would be an assertion nobody checked.
/// </param>
/// <param name="until">
///     The last CS2 build the field appeared in, or <see langword="null"/> while it is still
///     present. Set when a field is removed and the property enters its deprecation window.
/// </param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SchemaFieldVersionAttribute(string since, string? until = null) : Attribute
{
    /// <summary>The CS2 build the field first appeared in, or <c>genesis</c>.</summary>
    public string Since { get; } = since ?? throw new ArgumentNullException(nameof(since));

    /// <summary>The last CS2 build carrying the field, or <see langword="null"/> if still present.</summary>
    public string? Until { get; } = until;
}
