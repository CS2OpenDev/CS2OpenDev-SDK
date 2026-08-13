namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     A dictionary-backed <see cref="IEntityFieldReader"/> over hand-authored field values.
///     The reference implementation of the read contract, and the conformance kit a runtime
///     tests its own reader against.
/// </summary>
/// <remarks>
///     <para>
///         This exists to answer an objection rather than to serve production traffic. Hosting
///         entity wrappers here was declined on the grounds that this repository has no entity
///         to wrap and no intention of acquiring a parser — which is true and stays true. This
///         type makes it irrelevant: every generated wrapper can be tested against a
///         <c>Dictionary&lt;string, object?&gt;</c> of field values, exercising name mapping,
///         read policies, nullability, alias resolution and handle plumbing, without a single
///         byte of demo being parsed. The repository still never parses a demo.
///     </para>
///     <para>
///         Ships rather than staying in the test project so that it is also the conformance
///         kit: a third-party runtime can run the same assertions against its own reader and
///         know it agrees with this one about what the contract means.
///     </para>
///     <para>
///         <b>Semantics this pins down.</b> A key absent from <paramref name="values"/> is a
///         field never received, and every <c>TryRead*</c> returns <see langword="false"/>. A
///         key present with a <see langword="null"/> value is a field received as null:
///         <see cref="TryReadObject"/> returns <see langword="true"/> with
///         <see langword="null"/>, and the typed readers return <see langword="false"/>
///         because they have no value to hand back. That asymmetry is the contract, not an
///         accident of this implementation.
///     </para>
/// </remarks>
/// <param name="binding">The class binding defining the ordinal space.</param>
/// <param name="values">Field values keyed by canonical Schema Lens path.</param>
/// <param name="engineClassName">
///     Engine class name to report. Defaults to the binding's own <c>EngineClass</c>; override
///     it to model a subclass being read through a base class's binding.
/// </param>
public sealed class DictionaryEntityReader(
    EntityClassBinding binding,
    IReadOnlyDictionary<string, object?> values,
    string? engineClassName = null) : IEntityFieldReader
{
    private readonly EntityClassBinding _binding = binding
        ?? throw new ArgumentNullException(nameof(binding));

    private readonly IReadOnlyDictionary<string, object?> _values = values
        ?? throw new ArgumentNullException(nameof(values));

    /// <inheritdoc/>
    public string EngineClassName { get; } = engineClassName ?? binding?.EngineClass
        ?? throw new ArgumentNullException(nameof(binding));

    /// <inheritdoc/>
    public bool TryReadInt32(int ordinal, out int value) => TryConvert(ordinal, out value);

    /// <inheritdoc/>
    public bool TryReadUInt64(int ordinal, out ulong value) => TryConvert(ordinal, out value);

    /// <inheritdoc/>
    public bool TryReadSingle(int ordinal, out float value) => TryConvert(ordinal, out value);

    /// <inheritdoc/>
    public bool TryReadBool(int ordinal, out bool value)
    {
        // The wire has no bool: the engine transmits 0/1 in an integer field, and every parser
        // widens it somewhere. Accepting both a real bool and an integer here is what lets a
        // fixture be written the obvious way while still matching what a runtime produces.
        if (!TryReadRaw(ordinal, out object? raw) || raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case int i:
                value = i != 0;
                return true;
            case long l:
                value = l != 0;
                return true;
            case uint u:
                value = u != 0;
                return true;
            case ulong ul:
                value = ul != 0;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <inheritdoc/>
    public bool TryReadEntityHandle(int ordinal, out uint rawHandle) => TryConvert(ordinal, out rawHandle);

    /// <inheritdoc/>
    public bool TryReadVector3(int ordinal, out System.Numerics.Vector3 value)
    {
        if (TryReadRaw(ordinal, out object? raw) && raw is System.Numerics.Vector3 v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryReadQAngle(int ordinal, out QAngle value)
    {
        if (TryReadRaw(ordinal, out object? raw) && raw is QAngle q)
        {
            value = q;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryReadObject(int ordinal, out object? value) => TryReadRaw(ordinal, out value);

    /// <inheritdoc/>
    public bool TryReadByEnginePath(string enginePath, out object? value)
    {
        ArgumentNullException.ThrowIfNull(enginePath);

        if (_values.TryGetValue(enginePath, out value))
        {
            return true;
        }

        // Not a canonical path — try it as a historical spelling. This is the alias table
        // earning its place: a demo recorded before a rename names the field the old way, and
        // the binding is what knows the old way still means this field.
        if (_binding.Aliases.TryGetValue(enginePath, out string? canonical)
            && _values.TryGetValue(canonical, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private bool TryReadRaw(int ordinal, out object? value)
    {
        if (ordinal < 0 || ordinal >= _binding.CanonicalPaths.Count)
        {
            value = null;
            return false;
        }

        return _values.TryGetValue(_binding.CanonicalPaths[ordinal], out value);
    }

    // Widening conversions only, and never a lossy one: a fixture written with `1` for a
    // ulong field should work, but reading a float out of an int field should fail rather
    // than silently truncate. Failure here reports as "absent", which is the honest answer —
    // the reader has no value of the requested type to hand back.
    private bool TryConvert<T>(int ordinal, out T value) where T : struct
    {
        if (!TryReadRaw(ordinal, out object? raw) || raw is null)
        {
            value = default;
            return false;
        }

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        try
        {
            value = (T)Convert.ChangeType(raw, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            value = default;
            return false;
        }
    }
}
