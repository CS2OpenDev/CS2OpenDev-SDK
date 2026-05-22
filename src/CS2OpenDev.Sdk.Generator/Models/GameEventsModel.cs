#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// Mirrors gameevents_schema.json (CS2OpenDev-Docs). Parsed independently from
// cs2_schema.json — events live in a `core.gameevents` / `game.gameevents` /
// `mod.gameevents` KV1 registry, not in the DumpSource2 schema dump, so they
// have their own top-level record. The shape:
//
//   { "events": [ { name, comment, source, properties: {...}, fields: [...], annotations? } ] }
//
// `properties` is a free-form `{ local?: 1, reliable?: 1 }` map (only those two
// flags appear in practice). `fields[i].type` is one of a small fixed vocabulary
// of KV1 type tags (`short`, `string`, `player_controller_and_pawn`, …) — see
// GameEventTypeMapper for the C# projection.

internal record GameEventsRoot(GameEventModel[] Events);

internal record GameEventModel(
    string Name,
    string? Comment,
    string Source, // basename of the originating file, e.g. "core.gameevents"
    bool Local, // properties.local == 1
    bool Reliable, // properties.reliable == 1
    GameEventFieldModel[] Fields,
    Annotations? Annotations);

internal record GameEventFieldModel(
    string Name,
    string Type, // raw KV1 type tag — projected via GameEventTypeMapper
    string? Comment,
    Annotations? Annotations);

internal static class GameEventsModel
{
    internal static GameEventsRoot Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });
        JsonElement root = doc.RootElement;

        GameEventModel[] events = root.TryGetProperty("events", out JsonElement eEl)
            ? ParseEvents(eEl)
            : [];

        return new GameEventsRoot(events);
    }

    private static GameEventModel[] ParseEvents(JsonElement el)
    {
        List<GameEventModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            string? comment = OptStr(item, "comment");
            string source = Str(item, "source");

            // properties is `{ "local": "1", "reliable": "1" }` (or absent). KV1
            // stringifies its values during JSON serialisation, so the upstream
            // schema carries them as strings — treat any non-"0"/non-empty value
            // as truthy. Fall back to numeric parsing for forward compatibility
            // if the schema ever switches to native numbers.
            bool local = false;
            bool reliable = false;
            if (item.TryGetProperty("properties", out JsonElement pEl) && pEl.ValueKind == JsonValueKind.Object)
            {
                local = ReadKv1Bool(pEl, "local");
                reliable = ReadKv1Bool(pEl, "reliable");
            }

            GameEventFieldModel[] fields = item.TryGetProperty("fields", out JsonElement fEl)
                ? ParseFields(fEl)
                : [];

            Annotations? annotations = ParseAnnotations(item);

            list.Add(new GameEventModel(name, comment, source, local, reliable, fields, annotations));
        }

        return list.ToArray();
    }

    private static GameEventFieldModel[] ParseFields(JsonElement el)
    {
        List<GameEventFieldModel> list = [];
        foreach (JsonElement item in el.EnumerateArray())
        {
            string name = Str(item, "name");
            string type = Str(item, "type");
            string? comment = OptStr(item, "comment");
            Annotations? annotations = ParseAnnotations(item);
            list.Add(new GameEventFieldModel(name, type, comment, annotations));
        }

        return list.ToArray();
    }

    // Same annotations shape as cs2_schema.json — keep parsing localized here so
    // GameEventsModel doesn't depend on SchemaModel's internals.
    private static Annotations? ParseAnnotations(JsonElement e)
    {
        if (!e.TryGetProperty("annotations", out JsonElement aEl) || aEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? description = aEl.TryGetProperty("description", out JsonElement dEl) && dEl.ValueKind == JsonValueKind.String
            ? dEl.GetString()
            : null;
        string? notes = aEl.TryGetProperty("notes", out JsonElement nEl) && nEl.ValueKind == JsonValueKind.String
            ? nEl.GetString()
            : null;
        string? warning = aEl.TryGetProperty("warning", out JsonElement wEl) && wEl.ValueKind == JsonValueKind.String
            ? wEl.GetString()
            : null;

        if (description is null && notes is null && warning is null)
        {
            return null;
        }

        return new Annotations(description, notes, warning);
    }

    // KV1 booleans serialise as the strings "0" / "1" (sometimes as numbers
    // when the schema dumper normalises). Treat any non-empty, non-"0", non-
    // "false" value as true so a future schema-format change doesn't silently
    // drop the flag from emission.
    private static bool ReadKv1Bool(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out JsonElement el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() is { Length: > 0 } s
                                    && !s.Equals("0", StringComparison.Ordinal)
                                    && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => el.TryGetInt64(out long n) && n != 0,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private static string? OptStr(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";
}
