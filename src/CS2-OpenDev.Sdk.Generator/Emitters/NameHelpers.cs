#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

internal static class NameHelpers
{
    // Maximum line length the IDE formatter accepts before splitting attribute args
    // across lines. Matches the threshold the formatter uses when it wraps a long
    // `[NativeMetadata("Name", "very long value")]` onto two lines; emitting that
    // shape pre-split keeps regen output stable against a follow-up formatter pass.
    private const int AttributeLineWrapThreshold = 120;

    // CA1711 reserved suffixes for CLASS types. Note: "Flag"/"Flags" are only reserved
    // for ENUM types — classes may end in Flag without violating CA1711.
    private static readonly string[] ClassReservedSuffixes = ["Attribute", "Queue", "Stack"];

    // ── C# keyword table ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
        "value",
        "yield",
        "var",
        "dynamic",
        "record",
        "with",
        "init",
        "required",
        "file",
        "scoped"
    };

    // Hungarian-style type-hint prefixes, ordered longest-first so that a compound
    // prefix like "isz" matches before its single-char shadow "i". The longest-first
    // invariant is load-bearing in two call sites — StripFieldPrefix (below) and
    // ToEnumMemberName's prefix loop. Reordering this array can silently regress
    // either one.
    //
    // Match rules (see StripFieldPrefix):
    //   - Multi-character hints match case-insensitively, so "m_VecPos" works the
    //     same as "m_vecPos" (this is the B2 fix).
    //   - Single-character hints match case-sensitively, because an uppercase first
    //     letter on a single-char hint is overwhelmingly an acronym start
    //     ("m_DBPath", "m_NCount") rather than capitalised Hungarian.
    //   - In both cases, the character immediately after the hint must be uppercase
    //     so that names like "flags" / "bytes" / "value" don't get half-stripped.
    private static readonly string[] TypeHints =
    [
        // 3-char compounds
        "isz", "psz", "str", "vec", "ang", "rgb", "clr",
        // 2-char compounds
        "iv", "iz", "iy", "ix", "uc", "ch", "fn",
        "dw", "sz", "fl", "un", "ul",
        // single-char hints (case-sensitive)
        "h", "n", "i", "b", "v", "p",
        "e", "f", "c", "d", "w", "q"
    ];

    private static readonly string[] TypeSuffixes = ["_t", "_s", "_e"];

    // Emits a `[NativeMetadata(...)]` attribute line (or two-line wrap when the
    // joined form would exceed the formatter's line-length threshold). Used by
    // both ClassEmitter (field metadata, CE-2) and EnumEmitter (member metadata,
    // EE-1). `indent` is the body indent of the enclosing class/enum (typically
    // four spaces); the wrapped second line gets an additional four-space
    // continuation indent under it.
    internal static void AppendNativeMetadata(StringBuilder sb, string indent, MetadataEntry md)
    {
        string name = EscAttrString(md.Name);
        if (md.Value is null)
        {
            sb.Append(indent).Append("[NativeMetadata(\"").Append(name).Append("\")]").AppendLine();
            return;
        }

        string value = EscAttrString(md.Value);
        // 19 = "[NativeMetadata(\"" + "\", \"" + "\")]" overhead (constant width).
        int joinedLength = indent.Length + 19 + name.Length + value.Length;
        if (joinedLength > AttributeLineWrapThreshold)
        {
            sb.Append(indent).Append("[NativeMetadata(\"").Append(name).Append("\",").AppendLine();
            sb.Append(indent).Append("    \"").Append(value).Append("\")]").AppendLine();
        }
        else
        {
            sb.Append(indent).Append("[NativeMetadata(\"").Append(name).Append("\", \"").Append(value).Append("\")]").AppendLine();
        }
    }

    internal static string Esc(string id) => Keywords.Contains(id) ? "@" + id : id;

    // Escapes characters that would terminate a `"..."` C# string literal. Used for
    // metadata values (CE-2 / EE-1) and any other content that comes from the schema
    // and gets embedded into a generated attribute argument. Backslash MUST be replaced
    // first so the substitutions for the other escapes aren't themselves re-escaped.
    internal static string EscAttrString(string s) =>
        s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

    internal static string SanitizeFilename(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }
    // ── Identifier sanitization ──────────────────────────────────────────────────
    //
    // Replaces characters that aren't valid in a C# identifier. The C++ scope operator
    // `::` collapses to a single underscore (one conceptual separator). Every other
    // non-identifier character (template angle brackets, commas, spaces, …) becomes
    // its own underscore — adjacent non-identifiers DO NOT collapse, so the result
    // is stable and reversible from the original input shape.

    internal static string SanitizeName(string name)
    {
        string s = name.Replace("::", "_");
        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }

    // ── Enum member name transformation ─────────────────────────────────────────
    //
    // Converts a C++ enum member name to a .NET PascalCase identifier.
    //
    // Step 1   — strip k_ prefix.
    // Step 2   — strip E{BaseName}_ or {BaseName}_ (case-insensitive match so that
    //            ALL_CAPS member prefixes match a PascalCase base name).
    // Step 2.5 — strip type-hint code from lowercase-start remainder.
    // Step 3   — if ALL_CAPS_SNAKE → PascalCase each segment.
    // Step 4   — PascalCase first letter.

    internal static string ToEnumMemberName(string enumCppTypeName, string memberName)
    {
        string rest = memberName;

        // Step 1: strip k_
        if (rest.StartsWith("k_", StringComparison.Ordinal))
        {
            rest = rest.Substring(2);
        }

        // Step 2: strip enum-type-derived prefix (case-insensitive so SCREAMING_SNAKE
        // member names match the PascalCase base name derived from the C++ type name)
        string baseName = DeriveEnumBaseName(enumCppTypeName);
        if (baseName.Length > 0)
        {
            string p1 = "E" + baseName + "_";
            string p2 = baseName + "_";
            if (rest.Length > p1.Length &&
                rest.StartsWith(p1, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest.Substring(p1.Length);
            }
            else if (rest.Length > p2.Length &&
                     rest.StartsWith(p2, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest.Substring(p2.Length);
            }
        }

        if (rest.Length == 0)
        {
            return PascalFirst(memberName);
        }

        // Step 2.5: strip type-hint code from lowercase-start names
        if (char.IsLower(rest[0]))
        {
            foreach (string hint in TypeHints)
            {
                if (rest.Length > hint.Length &&
                    rest.StartsWith(hint, StringComparison.Ordinal) &&
                    char.IsUpper(rest[hint.Length]))
                {
                    rest = rest.Substring(hint.Length);
                    break;
                }
            }
        }

        if (rest.Length == 0)
        {
            return PascalFirst(memberName);
        }

        // Step 3: ALL_CAPS_SNAKE → PascalCase per segment
        if (IsUpperSnakeCase(rest))
        {
            return SnakeToPascal(rest);
        }

        // Step 4: PascalCase first letter
        return PascalFirst(rest);
    }

    // ── Field / property name transformation ────────────────────────────────────
    //
    // Converts a C++ field name to a .NET PascalCase property name.
    //
    // Step 1 — strip access prefix:
    //   single lower + _  →  m_, s_, t_, g_
    //   two upper   + _  →  RS_, CS_, CB_, ...
    //   three upper + _  →  NPC_, DMG_, ... (only when not the whole name)
    //
    // Step 2 — strip type-hint code (only when followed by an uppercase letter so
    //   we don't over-strip names like "flags", "bytes", "value"):
    //   vec, ang, rgb, clr, dw, sz, fl  →  multi-char hints (tried first)
    //   h, n, i, b, v, p, e, f, c, d, w, q  →  single-char hints
    //
    // Step 3 — PascalCase the remainder; normalise any remaining underscores.
    //
    // Examples:
    //   m_flRadius    →  Radius       m_nCount               →  Count
    //   m_hEntity     →  Entity       m_nSetValue_Value       →  SetValueValue
    //   m_vecPos      →  Pos          m_Movement_type_desired →  MovementTypeDesired

    internal static string ToPropName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        string s = StripFieldPrefix(name);
        if (string.IsNullOrEmpty(s))
        {
            return "_";
        }

        if (char.IsDigit(s[0]))
        {
            return "_" + s;
        }

        return s;
    }

    // Strips only the access prefix (m_, s_, RS_, …) without touching the type-hint
    // code. Used as a fallback when full stripping would produce a duplicate name.
    internal static string ToPropNameAccessOnly(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        string rest = name;

        if (rest is [_, '_', ..] && char.IsLower(rest[0]))
        {
            rest = rest.Substring(2);
        }
        else if (rest is [_, _, '_', ..] &&
                 char.IsUpper(rest[0]) && char.IsUpper(rest[1]))
        {
            rest = rest.Substring(3);
        }
        else if (rest is [_, _, _, '_', ..] &&
                 char.IsUpper(rest[0]) && char.IsUpper(rest[1]) && char.IsUpper(rest[2]) &&
                 rest.Length > 4)
        {
            rest = rest.Substring(4);
        }

        if (rest.Length == 0)
        {
            return PascalFirst(name);
        }

        string s = NormalizeSegments(PascalFirst(rest));
        if (char.IsDigit(s[0]))
        {
            return "_" + s;
        }

        return s;
    }

    // ── Type name transformation ─────────────────────────────────────────────────
    //
    // Converts a C++ type name to an idiomatic .NET PascalCase type name.
    //
    // Step 1 — replace C++ scope operator (::) with underscore.
    // Step 2 — strip common C++ typedef suffixes (_t, _s, _e).
    // Step 3 — split on underscores and PascalCase each segment.
    // Step 4 — ensure first character is uppercase.
    // Step 5 — strip CA1711-triggering suffixes based on type kind:
    //           Enum types:   strip "Enum"; Flags→ append "s" to "Flag"; non-Flags→ strip "Flag"
    //           Class types:  strip "Attribute", "Queue", "Stack", "Flag"
    //
    // Examples:
    //   AABB_t                        → AABB
    //   CAnimDesc_Flag  (class)       → CAnimDesc
    //   matrix3x4_t                   → Matrix3x4
    //   NavAttributeEnum  (enum)      → Nav
    //   EPulseGraphExecutionHistoryFlag ([Flags] enum) → EPulseGraphExecutionHistoryFlags
    //   CNmClipDocEvent_EntityAttribute (class) → CNmClipDocEventEntity
    //   CompositeMaterial_t           → CompositeMaterial

    internal static string ToTypeName(string cppName, bool isEnum = false, bool isFlags = false)
    {
        string s = cppName.Replace("::", "_");

        // Strip C++ type-alias suffixes
        foreach (string suffix in TypeSuffixes)
        {
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - suffix.Length);
                break;
            }
        }

        // Normalise underscore-separated segments to PascalCase
        s = NormalizeSegments(s);

        // Ensure leading character is uppercase (handles all-lowercase names like "fltx4")
        if (s.Length > 0 && char.IsLower(s[0]))
        {
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // Strip CA1711-triggering reserved suffixes
        if (isEnum)
        {
            // Strip redundant "Enum" suffix (type is already an enum)
            if (s.EndsWith("Enum", StringComparison.Ordinal) && s.Length > 4)
            {
                s = s.Substring(0, s.Length - 4);
            }

            // After stripping "Enum", strip "Attribute" if now exposed (e.g. NavAttributeEnum → Nav)
            if (s.EndsWith("Attribute", StringComparison.Ordinal) && s.Length > 9)
            {
                s = s.Substring(0, s.Length - 9);
            }

            if (isFlags)
            {
                // [Flags] enums should end in "Flags" (plural), not "Flag" (singular)
                if (s.EndsWith("Flag", StringComparison.Ordinal) &&
                    !s.EndsWith("Flags", StringComparison.Ordinal) && s.Length > 4)
                {
                    s += "s";
                }
            }
            else
            {
                if (s.EndsWith("Flag", StringComparison.Ordinal) && s.Length > 4)
                {
                    s = s.Substring(0, s.Length - 4);
                }
            }
        }
        else
        {
            // Classes: strip suffixes that conflict with BCL type-naming conventions (CA1711)
            foreach (string suffix in ClassReservedSuffixes)
            {
                if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - suffix.Length);
                    break;
                }
            }
        }

        return s.Length > 0 ? s : cppName;
    }

    // ── XML helpers ───────────────────────────────────────────────────────────────

    internal static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // "EGameUIState_t" → "GameUIState",  "DamageTypes_t" → "DamageTypes",
    // "EMyEnum" → "MyEnum",  "SolidType_t" → "SolidType"
    private static string DeriveEnumBaseName(string cppTypeName)
    {
        string s = cppTypeName;
        foreach (string suffix in TypeSuffixes)
        {
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - suffix.Length);
                break;
            }
        }

        if (s.Length > 1 && s[0] == 'E' && char.IsUpper(s[1]))
        {
            s = s.Substring(1);
        }

        return s;
    }

    // ToDo: Seems we should be able to do this more efficiently
    private static bool IsUpperSnakeCase(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsUpper(c) && c != '_' && !char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    // Converts underscore-separated segments to PascalCase.
    // No-ops if the string has no underscores.
    private static string NormalizeSegments(string s)
    {
        if (!s.Contains('_'))
        {
            return s;
        }

        StringBuilder sb = new(s.Length);
        foreach (string seg in s.Split('_'))
        {
            if (seg.Length == 0)
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(seg[0]));
            if (seg.Length > 1)
            {
                sb.Append(seg, 1, seg.Length - 1);
            }
        }

        return sb.Length > 0 ? sb.ToString() : s;
    }

    private static string PascalFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string SnakeToPascal(string s)
    {
        StringBuilder sb = new(s.Length);
        bool cap = true;
        foreach (char c in s)
        {
            if (c == '_')
            {
                cap = true;
                continue;
            }

            sb.Append(cap ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            cap = false;
        }

        return sb.ToString();
    }

    private static string StripFieldPrefix(string name)
    {
        string rest = name;

        // Step 1: strip access prefix
        if (rest is [_, '_', ..] && char.IsLower(rest[0]))
        {
            rest = rest.Substring(2);
        }
        else if (rest is [_, _, '_', ..] &&
                 char.IsUpper(rest[0]) && char.IsUpper(rest[1]))
        {
            rest = rest.Substring(3);
        }
        else if (rest is [_, _, _, '_', ..] &&
                 char.IsUpper(rest[0]) && char.IsUpper(rest[1]) && char.IsUpper(rest[2]) &&
                 rest.Length > 4)
        {
            rest = rest.Substring(4);
        }

        if (rest.Length == 0)
        {
            rest = name;
        }

        // Step 2: strip type-hint prefix — single pass. The longest-first ordering of
        // TypeHints ensures compound prefixes (e.g. "isz") are tried before their
        // single-char shadow ("i"), so one pass is sufficient for legitimate Hungarian.
        //
        // We deliberately do NOT chain strips (e.g. "p" → "vec") because chaining can
        // mis-strip meaningful word starts: m_iUnBalancedRounds is "number of unbalanced
        // rounds", and chaining "i" then "un" would invert that to "BalancedRounds".
        foreach (string hint in TypeHints)
        {
            if (rest.Length <= hint.Length ||
                !char.IsUpper(rest[hint.Length]))
            {
                continue;
            }

            // Multi-char hints match case-insensitively (B2); single-char hints
            // stay case-sensitive to avoid eating the first letter of an acronym.
            StringComparison cmp = hint.Length > 1
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (rest.StartsWith(hint, cmp))
            {
                rest = rest.Substring(hint.Length);
                break;
            }
        }

        if (rest.Length == 0)
        {
            return name;
        }

        // Step 3: PascalCase first letter, then normalise any remaining underscores
        string result = char.ToUpperInvariant(rest[0]) + rest.Substring(1);
        return NormalizeSegments(result);
    }
}
