#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// Consumer-supplied adjustments to game-event emission.
//
// The generator's default projections are deliberately conservative: all three
// player-reference tags become `int` (the raw userid the engine emits), because
// the SDK has no opinion about what a consumer wants to do with an entity
// reference. A demo parser that has its own `PlayerRef` type would rather the
// records carried that, and patching the generator to get it is not a
// reasonable ask.
//
// So the projection is a hook. An overrides file re-points a KV1 type tag at a
// consumer type and says how to build one; the emitters honour it in both the
// record property and the factory that fills it, so the two cannot drift.
//
// Records are also `partial`, which covers the common case of *adding* members.
// This covers the case partial cannot: changing the type of a member that is
// already generated.

// One tag's projection.
//
// `ReadAs` names which decoded shape to pull off the wire — it must stay one of
// the reader's own accessors, because that is what the factory calls. `Wrap` is
// a composite format string applied to that value, with `{0}` the read
// expression; absent, the value is used as-is (which is how a consumer widens
// `short` to `int` without introducing a type).
internal record FieldTypeOverride(string CSharpType, string ReadAs, string? Wrap)
{
    // Applies the wrap to a reader call. Kept here rather than in the emitter so
    // the record-side and factory-side agree by construction.
    internal string Apply(string readExpression) =>
        string.IsNullOrEmpty(Wrap)
            ? readExpression
            : Wrap.Replace("{0}", readExpression, StringComparison.Ordinal);
}

internal record GameEventOverrides(
    // KV1 type tag → projection. Applies to every field carrying that tag.
    IReadOnlyDictionary<string, FieldTypeOverride> FieldTypes,
    // Extra `using` lines to emit in generated files, so an override can name a
    // type that lives in the consumer's own namespace.
    IReadOnlyList<string> Usings)
{
    internal static GameEventOverrides Empty { get; } =
        new(new Dictionary<string, FieldTypeOverride>(StringComparer.Ordinal), []);

    internal bool IsEmpty => FieldTypes.Count == 0 && Usings.Count == 0;

    // Resolves the projection for a tag, or null to use the built-in mapping.
    internal FieldTypeOverride? For(string typeTag) =>
        FieldTypes.TryGetValue(typeTag, out FieldTypeOverride? o) ? o : null;

    internal static GameEventOverrides Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        JsonElement root = doc.RootElement;

        Dictionary<string, FieldTypeOverride> fieldTypes = new(StringComparer.Ordinal);
        if (root.TryGetProperty("fieldTypes", out JsonElement ftEl) && ftEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in ftEl.EnumerateObject())
            {
                fieldTypes[prop.Name] = ParseFieldType(prop.Name, prop.Value);
            }
        }

        List<string> usings = [];
        if (root.TryGetProperty("usings", out JsonElement uEl) && uEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in uEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } ns)
                {
                    usings.Add(ns);
                }
            }
        }

        usings.Sort(StringComparer.Ordinal);
        return new GameEventOverrides(fieldTypes, usings);
    }

    private static FieldTypeOverride ParseFieldType(string tag, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"fieldTypes['{tag}'] must be an object with 'csharpType' and 'readAs'.");
        }

        string? csharpType = el.TryGetProperty("csharpType", out JsonElement t) ? t.GetString() : null;
        string? readAs = el.TryGetProperty("readAs", out JsonElement r) ? r.GetString() : null;
        string? wrap = el.TryGetProperty("wrap", out JsonElement w) ? w.GetString() : null;

        if (string.IsNullOrWhiteSpace(csharpType))
        {
            throw new InvalidOperationException($"fieldTypes['{tag}'] is missing 'csharpType'.");
        }

        if (string.IsNullOrWhiteSpace(readAs))
        {
            throw new InvalidOperationException($"fieldTypes['{tag}'] is missing 'readAs'.");
        }

        // Fail at generation time rather than emitting a factory that will not
        // compile — the error message a consumer gets from `readAs: "Intt"`
        // should name the typo, not a missing method on GameEventReader buried
        // in 2,000 lines of generated code.
        if (!ValidReadAs.Contains(readAs))
        {
            throw new InvalidOperationException(
                $"fieldTypes['{tag}'].readAs = '{readAs}' is not a GameEventReader accessor. "
                + $"Valid values: {string.Join(", ", ValidReadAs)}.");
        }

        return new FieldTypeOverride(csharpType, readAs, wrap);
    }

    // The reader accessors an override may name. Mirrors GameEventReader's public
    // surface; adding one there means adding it here.
    private static readonly HashSet<string> ValidReadAs = new(StringComparer.Ordinal)
    {
        "String", "Bool", "Byte", "Int16", "Int32", "Float", "UInt64", "Handle", "Bytes", "Raw"
    };
}
