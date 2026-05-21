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
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"///     {NameHelpers.XmlEscape(en.Name)}");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.Append($"///     Module: <c>{en.Module}</c>");
        if (en.StorageSize.HasValue)
        {
            sb.Append($" — {en.StorageSize * 8}-bit");
        }

        if (en.IsFlags)
        {
            sb.Append(" — flags");
        }

        sb.AppendLine(".");
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
            sb.AppendLine($"    ///     {memberName}");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    /// <remarks>");
            sb.AppendLine($"    ///     Native name: <c>{NameHelpers.XmlEscape(member.Name)}</c>");
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
