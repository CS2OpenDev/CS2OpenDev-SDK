using CS2SchemaGen.Emitters;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — pure unit tests for NameHelpers.
//
// Tabular tests via TUnit's [Arguments(...)]. Each [Skip("Pending: …")] marker
// names the issue from improvement-plan.md that, when fixed, will make the
// test pass. Removing the marker is part of the corresponding fix step.

public class NameHelpersTests
{
    // ── ToTypeName: C++ type → C# type name ──────────────────────────────────

    /// <summary>Strips the C++ <c>_t</c> typedef suffix and PascalCases the result for class/struct names.</summary>
    [Test]
    [Arguments("AABB_t",              "AABB",              false, false)]
    [Arguments("matrix3x4_t",         "Matrix3x4",         false, false)]
    [Arguments("CompositeMaterial_t", "CompositeMaterial", false, false)]
    [Arguments("fltx4",               "Fltx4",             false, false)]
    [Arguments("CFoo",                "CFoo",              false, false)]
    public async Task ToTypeName_StripsCppSuffixAndPascalCases(string cpp, string expected, bool isEnum, bool isFlags)
    {
        string actual = NameHelpers.ToTypeName(cpp, isEnum, isFlags);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Treats the C++ <c>::</c> scope operator as a segment break and concatenates the PascalCased segments.</summary>
    [Test]
    [Arguments("Foo::Bar",        "FooBar")]      // :: → _, then PascalCase across segments
    [Arguments("Outer::Inner::X", "OuterInnerX")]
    public async Task ToTypeName_ReplacesScopeOperatorAndJoinsSegments(string cpp, string expected)
    {
        string actual = NameHelpers.ToTypeName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Strips redundant suffixes (<c>Enum</c>, <c>Attribute</c>, <c>_t</c>) from enum type names.</summary>
    [Test]
    [Arguments("MyEnum",            "My",          true,  false)] // "Enum" suffix is redundant on an enum type
    [Arguments("NavAttributeEnum",  "Nav",         true,  false)] // strip "Enum" then exposed "Attribute"
    [Arguments("EGameUIState_t",    "EGameUIState", true, false)]
    [Arguments("SolidType_t",       "SolidType",   true,  false)]
    public async Task ToTypeName_StripsEnumSuffixes(string cpp, string expected, bool isEnum, bool isFlags)
    {
        string actual = NameHelpers.ToTypeName(cpp, isEnum, isFlags);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>For flag enums, pluralises a trailing singular <c>Flag</c> to <c>Flags</c>; strips <c>Flag</c> from non-flag enums.</summary>
    [Test]
    [Arguments("MyFlag",                          "My",                     true, false)]
    [Arguments("MyFlag",                          "MyFlags",                true, true)]
    [Arguments("MyFlags",                         "MyFlags",                true, true)]
    [Arguments("EPulseGraphExecutionHistoryFlag", "EPulseGraphExecutionHistoryFlags", true, true)]
    public async Task ToTypeName_FlagsAwareSuffix(string cpp, string expected, bool isEnum, bool isFlags)
    {
        string actual = NameHelpers.ToTypeName(cpp, isEnum, isFlags);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Strips CA1711-reserved suffixes (<c>Attribute</c>, <c>Queue</c>, <c>Stack</c>) from class names but leaves enum-only suffixes (<c>Flag</c>) intact on classes.</summary>
    [Test]
    [Arguments("CFooAttribute", "CFoo")]
    [Arguments("CFooQueue",     "CFoo")]
    [Arguments("CFooStack",     "CFoo")]
    [Arguments("CFooFlag",      "CFooFlag")] // "Flag"/"Flags" only reserved for enums, not classes
    public async Task ToTypeName_StripsClassReservedSuffixes(string cpp, string expected)
    {
        string actual = NameHelpers.ToTypeName(cpp, isEnum: false);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── ToPropName: C++ field → C# property name ─────────────────────────────

    // Every prefix currently in NameHelpers.TypeHints — single-char (h/n/i/b/p/...) and
    // multi-char (vec/ang/rgb/clr/dw/sz/fl/un/ul). These all work today; this test pins
    // their behaviour so a regression in TypeHints is caught.
    /// <summary>Strips every single-char and multi-char Hungarian prefix in <c>TypeHints</c> (fl/n/h/p/b/i/sz/dw/vec/ang/rgb/clr/un/ul).</summary>
    [Test]
    [Arguments("m_flRadius",     "Radius")]
    [Arguments("m_nCount",       "Count")]
    [Arguments("m_hEntity",      "Entity")]
    [Arguments("m_pParent",      "Parent")]
    [Arguments("m_bEnabled",     "Enabled")]
    [Arguments("m_iHealth",      "Health")]
    [Arguments("m_szName",       "Name")]
    [Arguments("m_dwFlags",      "Flags")]
    [Arguments("m_vecPos",       "Pos")]
    [Arguments("m_angVelocity",  "Velocity")]
    [Arguments("m_rgbColor",     "Color")]
    [Arguments("m_clrColor",     "Color")]
    [Arguments("m_unVoiceFlags", "VoiceFlags")]
    [Arguments("m_ulSteamID",    "SteamID")]
    public async Task ToPropName_StripsKnownHungarianPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>When the field has no recognisable Hungarian prefix after stripping <c>m_</c>, leaves the remainder untouched.</summary>
    [Test]
    [Arguments("m_ArmorValue",        "ArmorValue")]
    [Arguments("m_TotalRoundsPlayed", "TotalRoundsPlayed")]
    [Arguments("m_CSlot",             "CSlot")]
    public async Task ToPropName_NoHungarianToStrip_PreservesName(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // Regression guard: an earlier draft of Step 2 chained type-hint strips in a
    // do-while loop, which mis-stripped "Un" out of m_iUnBalancedRounds (number of
    // unbalanced rounds → "BalancedRounds", inverting the meaning). Single-pass
    // stripping preserves the trailing word.
    //
    // The second row pins that we ALSO don't chain when a chain would be cleaner —
    // m_pVecRelationships keeps "VecRelationships" instead of stripping the trailing
    // "vec" down to "Relationships". The trade-off is intentional: one always-correct
    // pass beats a sometimes-cleaner chain that occasionally inverts meaning.
    /// <summary>Regression guard: hint stripping is single-pass so a leading <c>Un</c> in <c>UnBalancedRounds</c> isn't lopped off (which would invert meaning to <c>BalancedRounds</c>).</summary>
    [Test]
    [Arguments("m_iUnBalancedRounds",  "UnBalancedRounds")]
    [Arguments("m_pVecRelationships",  "VecRelationships")]
    public async Task ToPropName_DoesNotChainHintStripsAcrossWordBoundary(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Strips capital-letter access prefixes (<c>RS_</c>, <c>CS_</c>, <c>CB_</c>) that act like <c>m_</c> but use uppercase scoping.</summary>
    [Test]
    [Arguments("RS_FooBar",  "FooBar")]  // 2-upper access prefix
    [Arguments("CS_Status",  "Status")]
    [Arguments("CB_OnHit",   "OnHit")]
    public async Task ToPropName_StripsMultiUpperAccessPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // Compound Hungarian prefixes added by B1/NH-1.
    /// <summary>Strips compound Hungarian prefixes (<c>isz</c>, <c>psz</c>, <c>str</c>, <c>iv</c>, <c>ix</c>) added by B1/NH-1 — these match before their single-char shadows.</summary>
    [Test]
    [Arguments("m_iszPlayerName", "PlayerName")]
    [Arguments("m_iszEntityName", "EntityName")]
    [Arguments("m_pszPath",       "Path")]
    [Arguments("m_strSearchName", "SearchName")]
    [Arguments("m_ivPosition",    "Position")]
    [Arguments("m_ixIndex",       "Index")]
    public async Task ToPropName_StripsCompoundHungarianPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // B2: hint comparison is case-insensitive for multi-char hints so capitalised
    // Hungarian (m_VecNormPos, m_FlRadius) strips the same as the lowercase form.
    /// <summary>Multi-char Hungarian prefixes match case-insensitively (B2), so capitalised forms (<c>m_VecNormPos</c>) strip the same as lowercase.</summary>
    [Test]
    [Arguments("m_VecNormPos",   "NormPos")]
    [Arguments("m_AngRotation",  "Rotation")]
    [Arguments("m_FlRadius",     "Radius")]
    public async Task ToPropName_StripsCapitalizedHungarianPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>PascalCases each underscore-delimited segment in a property name after access/Hungarian-prefix removal.</summary>
    [Test]
    [Arguments("m_Movement_type_desired", "MovementTypeDesired")]
    [Arguments("m_nSet_Value_Value",      "SetValueValue")]
    public async Task ToPropName_PascalCasesUnderscoreSegments(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropName(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── ToPropNameAccessOnly: fallback used on collision ─────────────────────

    /// <summary>Collision-fallback variant: strips ONLY the access prefix (<c>m_</c>) and keeps the Hungarian hint in the name (B4's stable form).</summary>
    [Test]
    [Arguments("m_pParent",  "PParent")]
    [Arguments("m_hParent",  "HParent")]
    [Arguments("m_iszName",  "IszName")]
    [Arguments("m_flValue",  "FlValue")]
    public async Task ToPropNameAccessOnly_StripsOnlyAccessPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropNameAccessOnly(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>The access-only fallback also recognises capital-letter access prefixes (<c>RS_</c>, <c>NPC_</c>).</summary>
    [Test]
    [Arguments("RS_Foo", "Foo")]
    [Arguments("NPC_X",  "X")]
    public async Task ToPropNameAccessOnly_StripsMultiUpperAccessPrefix(string cpp, string expected)
    {
        string actual = NameHelpers.ToPropNameAccessOnly(cpp);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── ToEnumMemberName ────────────────────────────────────────────────────

    /// <summary>Strips both the <c>k_</c> Valve prefix and the enum-type-derived base-name prefix (matched case-insensitively) from member names.</summary>
    [Test]
    [Arguments("EGameUIState_t",   "k_EGameUIState_Loading",       "Loading")]      // k_ + base name match
    [Arguments("MoveType_t",       "MOVETYPE_WALK",                "Walk")]         // base-name match (case-insensitive, no underscores)
    [Arguments("SimpleEnum",       "Value",                        "Value")]
    public async Task ToEnumMemberName_StripsPrefixes(string enumType, string memberName, string expected)
    {
        string actual = NameHelpers.ToEnumMemberName(enumType, memberName);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // Documents the current behaviour when the SCREAMING_SNAKE prefix doesn't match the
    // enum base name shape (different underscoring or unrelated abbreviation). The
    // PascalCased member retains the prefix — this is a future polish opportunity,
    // not flagged in the bug report.
    /// <summary>Documents current behaviour: when the SCREAMING_SNAKE prefix doesn't match the enum base name, the helper falls back to PascalCasing the whole member name (prefix retained).</summary>
    [Test]
    [Arguments("DamageTypes_t",    "DMG_BULLET",                   "DmgBullet")]    // DMG_ != DamageTypes_
    [Arguments("SolidType_t",      "SOLID_TYPE_NONE",              "SolidTypeNone")] // base name has no internal _
    public async Task ToEnumMemberName_UnmatchedPrefix_PascalCasesWholeName(string enumType, string memberName, string expected)
    {
        string actual = NameHelpers.ToEnumMemberName(enumType, memberName);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>ALL_CAPS_SNAKE_CASE member names PascalCase each underscore-delimited segment.</summary>
    [Test]
    [Arguments("MyEnum", "MY_VALUE_A", "MyValueA")]
    [Arguments("MyEnum", "OTHER_NAME", "OtherName")]
    public async Task ToEnumMemberName_AllCapsSnakeCase_PascalCasesSegments(string enumType, string memberName, string expected)
    {
        string actual = NameHelpers.ToEnumMemberName(enumType, memberName);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── SanitizeName / SanitizeFilename / Esc / XmlEscape ────────────────────

    /// <summary>Collapses each <c>::</c> scope operator to a single underscore (one underscore per separator, not two).</summary>
    [Test]
    [Arguments("Foo::Bar",        "Foo_Bar")]
    [Arguments("Outer::Inner::X", "Outer_Inner_X")]
    [Arguments("Plain",           "Plain")]
    public async Task SanitizeName_ReplacesScopeOperator(string input, string expected)
    {
        string actual = NameHelpers.SanitizeName(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // Each non-identifier character becomes its own `_`, with one documented exception:
    // the C++ scope operator `::` is treated as a single separator and collapses to
    // one `_` (pinned by SanitizeName_ReplacesScopeOperator above). Every OTHER
    // non-identifier char stays one-to-one — so "Map<K, V>" → "Map_K__V_" has two
    // underscores between K and V (one for ',', one for ' ').
    /// <summary>Defensively replaces every non-identifier character with <c>_</c> (NH-2) so template / space-containing C++ names never produce invalid C#.</summary>
    [Test]
    [Arguments("Foo<T>",       "Foo_T_")]
    [Arguments("Foo Bar",      "Foo_Bar")]
    [Arguments("Map<K, V>",    "Map_K__V_")]
    public async Task SanitizeName_ReplacesAllNonIdentifierChars(string input, string expected)
    {
        string actual = NameHelpers.SanitizeName(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Replaces filesystem-unsafe characters (slash, dot) with underscores in module filenames.</summary>
    [Test]
    [Arguments("client",   "client")]
    [Arguments("foo/bar",  "foo_bar")]
    [Arguments("a.b.c",    "a_b_c")]
    public async Task SanitizeFilename_ReplacesUnsafeChars(string input, string expected)
    {
        string actual = NameHelpers.SanitizeFilename(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Prefixes C# reserved words and contextual keywords with <c>@</c> so they can be used as identifiers.</summary>
    [Test]
    [Arguments("class",   "@class")]
    [Arguments("string",  "@string")]
    [Arguments("MyType",  "MyType")]
    [Arguments("value",   "@value")]
    public async Task Esc_PrefixesKeywordsWithAt(string input, string expected)
    {
        string actual = NameHelpers.Esc(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Escapes <c>&amp;</c> and angle brackets for safe embedding inside XML doc comments.</summary>
    [Test]
    [Arguments("Foo<Bar>",   "Foo&lt;Bar&gt;")]
    [Arguments("A & B",      "A &amp; B")]
    [Arguments("plain text", "plain text")]
    public async Task XmlEscape_EscapesAmpAndAngles(string input, string expected)
    {
        string actual = NameHelpers.XmlEscape(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── Defensive edge cases ────────────────────────────────────────────────

    /// <summary>Defensive: an empty input returns <c>_</c> rather than throwing or producing an empty identifier.</summary>
    [Test]
    public async Task ToPropName_EmptyInput_ReturnsUnderscore()
    {
        // The emitter never feeds empty names today, but the helper must not crash.
        await Assert.That(NameHelpers.ToPropName("")).IsEqualTo("_");
    }

    /// <summary>Defensive: a name starting with a digit gets a leading <c>_</c> so the result is a valid C# identifier.</summary>
    [Test]
    public async Task ToPropName_NameStartingWithDigit_GetsLeadingUnderscore()
    {
        // C# identifiers can't start with a digit. The helper prepends "_".
        string actual = NameHelpers.ToPropName("42abc");
        await Assert.That(actual).IsEqualTo("_42abc");
    }

    /// <summary>Defensive: after the access prefix strip, a digit-starting remainder still gets the leading <c>_</c>.</summary>
    [Test]
    public async Task ToPropName_AccessPrefixWithDigitTail_GetsLeadingUnderscore()
    {
        // m_1abc → strip m_ → "1abc" → starts with digit → "_1abc"
        string actual = NameHelpers.ToPropName("m_1abc");
        await Assert.That(actual).IsEqualTo("_1abc");
    }

    /// <summary>Strips a bare <c>k_</c> prefix (no base-name match) and PascalCases the remainder.</summary>
    [Test]
    public async Task ToEnumMemberName_BareKPrefix_PascalCasesRemainder()
    {
        // "k_Foo" → strip k_ → "Foo" → no base-name match → PascalFirst → "Foo"
        string actual = NameHelpers.ToEnumMemberName("EBar", "k_Foo");
        await Assert.That(actual).IsEqualTo("Foo");
    }

    /// <summary>Defensive: an empty input round-trips to an empty string rather than throwing.</summary>
    [Test]
    public async Task ToTypeName_EmptyInput_ReturnsOriginal()
    {
        // Defensive: ToTypeName falls back to the original input when normalisation
        // would produce an empty string.
        string actual = NameHelpers.ToTypeName("");
        await Assert.That(actual).IsEqualTo("");
    }

    /// <summary>PascalCasing a C# keyword like <c>class</c> produces <c>Class</c>, which is no longer a keyword — so <see cref="NameHelpers.Esc"/> isn't required on type names.</summary>
    [Test]
    public async Task ToTypeName_KeywordInput_PascalizationRemovesKeywordConflict()
    {
        // "class" is a keyword but after PascalCasing it becomes "Class" — no longer
        // a keyword. ToTypeName doesn't need to involve Esc on the result.
        string actual = NameHelpers.ToTypeName("class");
        await Assert.That(actual).IsEqualTo("Class");
    }

    // ── EscAttrString: full C# string-literal escape ────────────────────────────
    //
    // Schema metadata values (CE-2 / EE-1) get embedded as raw C# string literals
    // inside [NativeMetadata("…")] attributes. Real-world dumps contain MProperty
    // descriptions with embedded newlines and quotes; without escaping these, the
    // emitted .g.cs files won't compile.

    /// <summary>Escapes every character (<c>\</c>, <c>"</c>, <c>\r</c>, <c>\n</c>, <c>\t</c>) that would otherwise break a C# string literal embedded inside <c>[NativeMetadata("…")]</c>.</summary>
    [Test]
    [Arguments("plain",              "plain")]
    [Arguments("with \"quotes\"",    "with \\\"quotes\\\"")]
    [Arguments("with\\backslash",    "with\\\\backslash")]
    [Arguments("two\nlines",         "two\\nlines")]
    [Arguments("cr\rcr",             "cr\\rcr")]
    [Arguments("a\tb",               "a\\tb")]
    [Arguments("\\\"\n",             "\\\\\\\"\\n")]
    public async Task EscAttrString_EscapesAllStringLiteralBreakers(string input, string expected)
    {
        string actual = NameHelpers.EscAttrString(input);
        await Assert.That(actual).IsEqualTo(expected);
    }
}
