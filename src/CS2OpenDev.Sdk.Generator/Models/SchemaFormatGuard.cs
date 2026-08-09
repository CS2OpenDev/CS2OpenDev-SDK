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
    // The `schema_format_version` major this generator parses. Upstream bumps
    // the major when record shapes change incompatibly; the minor is additive,
    // so 2.0 and a future 2.1 are both fine and only the major is compared.
    //
    // 1.x support was deliberate but temporary, and is gone: it existed only to
    // keep the pinned submodule building while the parser learned 2.0, and
    // upstream no longer publishes it. Keeping both would have meant carrying
    // two shapes of every numeric, every category spelling and every namespace
    // key indefinitely for a format nothing produces.
    internal const int SupportedMajor = 2;

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

        if (!int.TryParse(majorSpan, out int major) || major == SupportedMajor)
        {
            return;
        }

        throw new NotSupportedException(
            Diagnostics.Descriptors.UnsupportedSchemaFormat.Format(
                declared, SupportedMajor + ".x"));
    }
}
