#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// Shared `schema_format_version` check for both upstream inputs.
//
// `cs2_schema.json` and `gameevents_schema.json` carry the same header key and
// upstream bumps them together, but they are separate files and could drift.
// Both are guarded so whichever is read first reports the mismatch by name
// rather than failing on whatever shape change happens to break first.
internal static class SchemaFormatGuard
{
    // The `schema_format_version` majors this generator can parse. Upstream
    // bumps the major when record shapes change incompatibly; the minor is
    // additive, so 1.0 and 1.1 are both fine and only the major is compared.
    //
    // Two are supported deliberately, not transitionally. 1.x is what the
    // pinned submodule serves and 2.0 is what Docs publishes at HEAD, and the
    // generator reads both because the pin cannot move until enum records carry
    // `projectName` (CS2OpenDev-SchemaTracker#1). Parsing 2.0 and bumping the
    // submodule are separate steps; this is the first.
    internal static readonly int[] SupportedMajors = [1, 2];

    // Absent or unparseable is deliberately allowed through: pre-1.0 dumps and
    // every hand-written test fixture omit the key, and they parse fine. A
    // format-string change alone must not hard-block a regen that would
    // otherwise have worked.
    internal static void ThrowIfUnsupported(JsonElement root)
    {
        if (!root.TryGetProperty("schema_format_version", out JsonElement el)
            || el.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string? declared = el.GetString();
        if (declared is null)
        {
            return;
        }

        // "2.0" → 2; a bare "2" parses too.
        ReadOnlySpan<char> majorSpan = declared.AsSpan();
        int dot = majorSpan.IndexOf('.');
        if (dot >= 0)
        {
            majorSpan = majorSpan[..dot];
        }

        if (!int.TryParse(majorSpan, out int major) || Array.IndexOf(SupportedMajors, major) >= 0)
        {
            return;
        }

        throw new NotSupportedException(
            Diagnostics.Descriptors.UnsupportedSchemaFormat.Format(
                declared, string.Join("/", SupportedMajors)));
    }
}
