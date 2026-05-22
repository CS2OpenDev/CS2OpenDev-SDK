#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

internal static class EnumEmitter
{
    internal static void Emit(StringBuilder sb, EnumModel en)
    {
        // Use the name-map lookup so collision-disambiguated names flow through.
        string csName = NameHelpers.Esc(TypeMapper.LookupCsName(en.Name, true, en.IsFlags));

        // ── XML documentation ────────────────────────────────────────────────────
        //
        // Mirrors ClassEmitter: description-first summary when annotated, native
        // name relocated into `<remarks>`. The schema name is preserved through
        // `[NativeName]` on the declaration as well, but surfacing it in remarks
        // keeps the rendered docs self-contained.
        bool useDescription = NameHelpers.HasSummaryDescription(en.Annotations, en.Metadata);
        sb.AppendLine("/// <summary>");
        sb.Append("///     ").AppendLine(NameHelpers.ResolveSummaryText(en.Name, en.Annotations, en.Metadata));
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.Append("///     ");
        if (useDescription)
        {
            sb.Append($"Native name: <c>{NameHelpers.XmlEscape(en.Name)}</c>. ");
        }

        sb.Append($"Module: <c>{en.Module}</c>");
        if (en.StorageSize.HasValue)
        {
            sb.Append($" — {en.StorageSize * 8}-bit");
        }

        if (en.IsFlags)
        {
            sb.Append(" — flags");
        }

        sb.AppendLine(".");
        NameHelpers.AppendAnnotationRemarks(sb, "", en.Annotations);
        sb.AppendLine("/// </remarks>");

        // ── Attributes & declaration ─────────────────────────────────────────────
        if (en.IsFlags)
        {
            sb.AppendLine("[Flags]");
        }

        // Preserve original C++ name for runtime interop when it differs from the C# name
        if (csName != en.Name && csName != "@" + en.Name)
        {
            sb.AppendLine($"[NativeName(\"{en.Name}\")]");
        }

        // EE-2: round-trip enum-level metadata as [NativeMetadata] attributes.
        // The current upstream schema carries zero enum-level metadata, but the
        // field is parsed and reflected — emitting it defensively keeps parity
        // with class-level metadata and means a future schema bump that adds it
        // doesn't silently disappear.
        foreach (MetadataEntry md in en.Metadata)
        {
            NameHelpers.AppendNativeMetadata(sb, "", md);
        }

        string underlying = en.StorageSize switch
        {
            1 => " : byte",
            2 => " : ushort",
            4 => " : uint",
            8 => " : ulong",
            _ => ""
        };

        sb.AppendLine($"public enum {csName}{underlying}");
        sb.AppendLine("{");

        // Track seen values (for CA1069) and names (for CA1708)
        Dictionary<string, string> seenValues = new(); // formatted value → first C# member name
        HashSet<string> seenNamesCi = new(StringComparer.OrdinalIgnoreCase);

        for (int idx = 0; idx < en.Members.Length; idx++)
        {
            MemberModel member = en.Members[idx];
            bool isLast = idx == en.Members.Length - 1;

            string memberName = NameHelpers.Esc(
                NameHelpers.ToEnumMemberName(en.Name, member.Name));
            if (memberName.Length > 0 && char.IsDigit(memberName[0]))
            {
                memberName = "_" + memberName;
            }

            string valueStr = TypeMapper.FormatEnumValue(member.Value, en.StorageSize);

            // CA1708: disambiguate case-only collisions using the raw C++ member name
            // as a suffix. Stable across reorders and carries semantic meaning, unlike
            // the previous numeric counter (Foo / Foo2). On the rare double-collision,
            // fall back to a counter so the output stays unique.
            if (!seenNamesCi.Add(memberName))
            {
                string disambiguated = memberName + "_" + NameHelpers.SanitizeName(member.Name);
                int n = 2;
                while (!seenNamesCi.Add(disambiguated))
                {
                    disambiguated = memberName + "_" + NameHelpers.SanitizeName(member.Name) + n++;
                }

                memberName = disambiguated;
            }

            if (idx > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine($"    /// <summary>");
            sb.Append("    ///     ").AppendLine(NameHelpers.ResolveSummaryText(memberName, member.Annotations, member.Metadata));
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    /// <remarks>");
            sb.AppendLine($"    ///     Native name: <c>{NameHelpers.XmlEscape(member.Name)}</c>.");
            NameHelpers.AppendAnnotationRemarks(sb, "    ", member.Annotations);
            sb.AppendLine($"    /// </remarks>");

            // CA1069: mark duplicate values as obsolete aliases
            if (seenValues.TryGetValue(valueStr, out string? firstMemberName))
            {
                sb.AppendLine($"    [Obsolete(\"Alias for {firstMemberName}.\")]");
            }
            else
            {
                seenValues[valueStr] = memberName;
            }

            sb.AppendLine($"    [NativeName(\"{member.Name}\")]");
            // EE-1: round-trip every metadata entry from the schema as a separate
            // [NativeMetadata(...)] so downstream tooling can read the markers.
            foreach (MetadataEntry md in member.Metadata)
            {
                NameHelpers.AppendNativeMetadata(sb, "    ", md);
            }

            // Last member: no trailing comma (C# allows it; formatter strips it).
            sb.AppendLine(isLast
                ? $"    {memberName} = {valueStr}"
                : $"    {memberName} = {valueStr},");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
