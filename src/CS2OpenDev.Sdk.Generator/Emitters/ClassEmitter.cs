#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

internal static class ClassEmitter
{
    // B4: when two fields would collide on the cleaned-up property name (e.g. m_pParent
    // and m_hParent both stripping to "Parent"), every colliding field falls back to
    // access-only naming (PParent / HParent). The old "first declared wins, rest get
    // access-only" rule made a field reorder in the schema silently flip which alias
    // wins — bad for diff stability and binary-ish reproducibility.
    //
    // Two passes:
    //   1. Compute the clean candidate name for each field. Group by candidate; any
    //      group with more than one member is "ambiguous" and falls back to access-only.
    //   2. If two access-only names still collide (rare), append a stable ordinal suffix
    //      to all but the first occurrence so the file compiles.
    // Internal so SchemaNamesEmitter can produce const-string entries keyed by
    // the same property names ClassEmitter emits. Sharing the helper keeps the
    // B4 collision rule in one place.
    // `enclosingTypeName` is the emitted C# name of the class these fields
    // belong to. C# forbids a member sharing its enclosing type's name (CS0542),
    // and upstream does not: schema 2.0 introduced `TagStatus`, whose first
    // field is `m_TagStatus`, which projects straight onto `TagStatus`. Two
    // files failed to compile — the class and the SchemaNames table, both fed
    // from here, which is why the fix belongs here and not at either call site.
    //
    // Optional so hand-written fixtures and tests that only care about
    // field-vs-field collisions can keep calling it with one argument.
    internal static string[] ComputePropNames(FieldModel[] fields, string? enclosingTypeName = null)
    {
        int n = fields.Length;
        string[] result = new string[n];

        // Pass 1: candidate clean name → access-only fallback on collision.
        string[] candidate = new string[n];
        Dictionary<string, int> candidateCounts = new(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            candidate[i] = NameHelpers.Esc(NameHelpers.ToPropName(fields[i].Name));
            candidateCounts.TryGetValue(candidate[i], out int c);
            candidateCounts[candidate[i]] = c + 1;
        }

        for (int i = 0; i < n; i++)
        {
            result[i] = candidateCounts[candidate[i]] > 1
                ? NameHelpers.Esc(NameHelpers.ToPropNameAccessOnly(fields[i].Name))
                : candidate[i];
        }

        // Pass 1b: a member may not be named after its enclosing type. Suffixed
        // rather than access-only-fallbacked, because the collision is with the
        // type name and not with a sibling — the access-only form of
        // `m_TagStatus` is still `TagStatus`. Runs before pass 2 so that if the
        // suffixed name collides with a real sibling, the ordinal pass catches
        // it. `[NativeName]` still carries `m_TagStatus`, so nothing is lost.
        if (!string.IsNullOrEmpty(enclosingTypeName))
        {
            HashSet<string> taken = new(result, StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                if (!string.Equals(result[i], enclosingTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                // Step past any name a sibling already owns rather than letting
                // pass 2 resolve it. Pass 2 suffixes by order of appearance, so
                // a class X with fields m_X and m_XValue would hand `XValue` to
                // the renamed m_X and push the legitimate owner to `XValue2` —
                // exactly the "a field reorder silently flips which alias wins"
                // instability the B4 rule above exists to prevent. No class in
                // the current schema hits this; it costs three lines to not
                // depend on that.
                string renamed = result[i] + "Value";
                int suffix = 2;
                while (taken.Contains(renamed))
                {
                    renamed = result[i] + "Value" + suffix.ToString();
                    suffix++;
                }

                taken.Add(renamed);
                result[i] = NameHelpers.Esc(renamed);
            }
        }

        // Pass 2: belt-and-braces ordinal suffix if the access-only fallback itself
        // produces a duplicate (e.g. two siblings both named m_pFoo and m_pFoo would
        // both become "PFoo"). Compilation-safety only; not expected to fire today.
        Dictionary<string, int> seenIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            if (seenIndex.TryGetValue(result[i], out int idx))
            {
                idx++;
                seenIndex[result[i]] = idx;
                result[i] = result[i] + idx.ToString();
            }
            else
            {
                seenIndex[result[i]] = 1;
            }
        }

        return result;
    }

    internal static void Emit(StringBuilder sb, ClassModel cls)
    {
        // Use the name-map lookup so collision-disambiguated names (see ModuleEmitter
        // step 0) flow through to the actual class declaration.
        string csName = NameHelpers.Esc(TypeMapper.LookupCsName(cls.Name));

        bool alignKnown = cls.Alignment is > 0 and <= 128 &&
                          (cls.Alignment & cls.Alignment - 1) == 0;

        // ── XML documentation ────────────────────────────────────────────────────
        //
        // Summary leads with the curated annotation description when present,
        // else with the schema name. When the description wins, the schema name
        // moves into `<remarks>` so it isn't lost from the rendered docs.
        bool useDescription = NameHelpers.HasSummaryDescription(cls.Annotations, cls.Metadata);
        sb.AppendLine("/// <summary>");
        sb.Append("///     ").AppendLine(NameHelpers.ResolveSummaryText(cls.Name, cls.Annotations, cls.Metadata));
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.Append("///     ");
        if (useDescription)
        {
            sb.Append($"Native name: <c>{NameHelpers.XmlEscape(cls.Name)}</c>. ");
        }

        sb.Append($"Module: <c>{cls.Module}</c>");
        if (cls.Size > 0)
        {
            sb.Append($" — {cls.Size} bytes");
            if (alignKnown)
            {
                sb.Append($", align {cls.Alignment}");
            }
        }

        if (cls.IsAbstract)
        {
            sb.Append(" — abstract");
        }

        sb.AppendLine(".");
        NameHelpers.AppendAnnotationRemarks(sb, "", cls.Annotations);
        sb.AppendLine("/// </remarks>");

        // ── Attributes ───────────────────────────────────────────────────────────
        if (cls.Size > 0)
        {
            // Informational: documents the native C++ size, NOT a P/Invoke contract.
            // The managed class layout is unrelated; consumers must not assume binary
            // compatibility. (Replaces the older [StructLayout(... Size = N)] which
            // implied a marshaling promise the SDK doesn't make.)
            sb.AppendLine($"[NativeSize({cls.Size})]");
        }

        // Preserve original C++ name for runtime interop when it differs from the C# name
        if (csName != cls.Name && csName != "@" + cls.Name)
        {
            sb.AppendLine($"[NativeName(\"{cls.Name}\")]");
        }

        // CE-3: round-trip class-level schema metadata (MGetKV3ClassDefaults,
        // MPropertyFriendlyName, MNetworkVarNames, …) as [NativeMetadata]
        // attributes on the class. Mirrors the field-level emission below.
        // Without this 3000+ class-level metadata entries are silently dropped.
        foreach (MetadataEntry md in cls.Metadata)
        {
            NameHelpers.AppendNativeMetadata(sb, "", md);
        }

        // ── Declaration ──────────────────────────────────────────────────────────
        string abstractMod = cls.IsAbstract ? "abstract " : "";
        string inheritance = cls.Parents.Length > 0
            ? " : " + NameHelpers.Esc(TypeMapper.LookupCsName(cls.Parents[0].Name))
            : "";

        sb.AppendLine($"public {abstractMod}partial class {csName}{inheritance}");
        sb.AppendLine("{");

        if (cls.Parents.Length > 1)
        {
            List<string> extras = new(cls.Parents.Length - 1);
            for (int i = 1; i < cls.Parents.Length; i++)
            {
                extras.Add(NameHelpers.Esc(TypeMapper.LookupCsName(cls.Parents[i].Name)));
            }

            sb.AppendLine("    // C# does not support multiple inheritance. Additional parents: "
                          + string.Join(", ", extras));
        }

        string[] propNames = ComputePropNames(cls.Fields, csName);

        // Emit properties alphabetized by C# property name (formatter convention).
        // Offset-order is preserved in metadata via [NativeOffset]; source order is
        // for readability, not interop.
        int[] order = new int[cls.Fields.Length];
        for (int i = 0; i < cls.Fields.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => StringComparer.Ordinal.Compare(propNames[a], propNames[b]));

        for (int k = 0; k < order.Length; k++)
        {
            int i = order[k];
            FieldModel field = cls.Fields[i];
            string csType = TypeMapper.Map(field.Type);
            string propName = propNames[i];

            if (k > 0 || cls.Parents.Length > 1)
            {
                sb.AppendLine();
            }

            sb.AppendLine($"    /// <summary>");
            sb.Append("    ///     ").AppendLine(NameHelpers.ResolveSummaryText(
                $"Gets or sets {propName}", field.Annotations, field.Metadata));
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    /// <remarks>");
            sb.AppendLine($"    ///     Native field <c>{NameHelpers.XmlEscape(field.Name)}</c> at offset <c>0x{field.Offset:X}</c>.");
            NameHelpers.AppendAnnotationRemarks(sb, "    ", field.Annotations);
            sb.AppendLine($"    /// </remarks>");
            sb.AppendLine($"    [NativeOffset(0x{field.Offset:X})]");
            sb.AppendLine($"    [NativeName(\"{field.Name}\")]");
            // CE-2: round-trip every metadata entry from the schema as a separate
            // [NativeMetadata(...)] so downstream tooling can read the markers.
            // Long two-arg forms get pre-split across two lines so the formatter
            // doesn't have to do it on every pass.
            foreach (MetadataEntry md in field.Metadata)
            {
                NameHelpers.AppendNativeMetadata(sb, "    ", md);
            }

            sb.AppendLine($"    public {csType} {propName} {{ get; set; }}");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
