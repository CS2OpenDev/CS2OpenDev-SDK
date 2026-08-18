namespace CS2OpenDev.Sdk.Entities;

/// <summary>
///     Structural checks a well-formed <see cref="EntityClassBinding"/> must pass.
/// </summary>
/// <remarks>
///     <para>
///         Ships rather than living in this repository's tests, because the manifests a runtime
///         consumes are not all ours: anyone emitting bindings (a fork, a future generator, a
///         hand-written fixture in a consumer's test suite) can check them against the same
///         rules the generated ones satisfy. Running this at startup over a binding set costs
///         microseconds and turns a class of silent mis-binding into an exception with a
///         sentence attached.
///     </para>
///     <para>
///         Everything here is checkable without constructing a wrapper, a reader or a world.
///         That is a direct consequence of <see cref="EntityClassBinding"/> being pure data,
///         and it is the reason the factory delegate came out of it.
///     </para>
/// </remarks>
public static class BindingConformance
{
    /// <summary>
    ///     Returns every structural problem with <paramref name="binding"/>, or an empty list.
    /// </summary>
    /// <remarks>
    ///     Returns all findings rather than throwing on the first, because a malformed manifest
    ///     usually has one cause with several symptoms and seeing them together names the cause.
    /// </remarks>
    public static IReadOnlyList<string> Validate(EntityClassBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(binding.EngineClass))
        {
            problems.Add("EngineClass is empty.");
        }

        if (string.IsNullOrWhiteSpace(binding.NetName))
        {
            problems.Add("NetName is empty.");
        }

        // A duplicate path means two ordinals address one field. Both would read the same
        // value, so nothing crashes and one generated property is silently a copy of another.
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < binding.CanonicalPaths.Count; i++)
        {
            string path = binding.CanonicalPaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                problems.Add($"CanonicalPaths[{i}] is empty — the ordinal space must be dense.");
            }
            else if (!seen.Add(path))
            {
                problems.Add($"CanonicalPaths[{i}] duplicates '{path}'; two ordinals address one field.");
            }
        }

        // An alias pointing at a path that is not in the ordinal space resolves to nothing. It
        // is the failure mode that only shows up on old demos, which is the worst time to find
        // it: the alias exists precisely to serve recordings nobody is testing against today.
        foreach (KeyValuePair<string, string> alias in binding.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Key))
            {
                problems.Add("Aliases contains an empty engine path.");
            }

            if (!seen.Contains(alias.Value))
            {
                problems.Add(
                    $"Alias '{alias.Key}' targets '{alias.Value}', which is not in CanonicalPaths — "
                    + "it can never resolve.");
            }

            if (seen.Contains(alias.Key))
            {
                problems.Add(
                    $"Alias '{alias.Key}' is also a canonical path; the alias would shadow a live field.");
            }
        }

        foreach (int ordinal in binding.HandleOrdinals)
        {
            if (ordinal < 0 || ordinal >= binding.CanonicalPaths.Count)
            {
                problems.Add(
                    $"HandleOrdinals contains {ordinal}, outside the ordinal space "
                    + $"[0, {binding.CanonicalPaths.Count}).");
            }
        }

        if (binding.HandleOrdinals.Distinct().Count() != binding.HandleOrdinals.Count)
        {
            problems.Add("HandleOrdinals contains duplicates.");
        }

        return problems;
    }

    /// <summary>
    ///     Throws if any binding in <paramref name="bindings"/> is structurally invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     One or more bindings failed, with every problem named in the message.
    /// </exception>
    public static void ThrowIfInvalid(IEnumerable<EntityClassBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        List<string> all = [];
        foreach (EntityClassBinding binding in bindings)
        {
            foreach (string problem in Validate(binding))
            {
                all.Add($"{binding.EngineClass}: {problem}");
            }
        }

        if (all.Count > 0)
        {
            throw new InvalidOperationException(
                "Entity class bindings failed conformance:" + Environment.NewLine
                + string.Join(Environment.NewLine, all));
        }
    }
}
