#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.SchemaLens;

// Loads and validates `schema-lens/` migration files.
//
// Validation is strict on purpose, and the strictness is the contract:
//
//   * The op vocabulary is closed. An op this loader does not know is an error,
//     not a skip — a skipped op would replay to a state whose hash still
//     matches nothing, and the author would be debugging a hash mismatch
//     instead of reading the actual problem.
//   * Unknown keys inside an op are errors too. This is what physically
//     enforces the issue #6 §3 split: a consumer-side key like `transform` or
//     `fallbackDefault` pasted into an upstream migration fails the build with
//     its own name in the message, rather than being silently shed and letting
//     the author believe it took effect.
//   * The `id` must equal the filename stem. The filename carries replay order
//     and the id is what diagnostics and hash-mismatch messages quote; if the
//     two could drift, an error message would point at a file that does not
//     exist.
//
// Errors are thrown as InvalidOperationException with the full story in the
// message; the exporter reports them under CS2_GEN_013.
internal static class LensMigrationLoader
{
    // Lives inside the migrations directory but is output, not input: it is the
    // rendered current state, rewritten by every exporter run. Skipped here so
    // a directory listing stays the natural home for both.
    internal const string StateFileName = "state.json";

    // Ordinal filename order is the replay order. The `NNNN-` prefix convention
    // makes that ordering explicit to a human reading the directory, but the
    // loader deliberately does not parse the prefix — the filesystem sort is
    // the single authority, and a second opinion could only ever disagree.
    internal static IReadOnlyList<LensMigration> LoadDirectory(string directory)
    {
        List<LensMigration> migrations = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            if (string.Equals(fileName, StateFileName, StringComparison.Ordinal))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(path);
            migrations.Add(Parse(File.ReadAllText(path), stem));
        }

        return migrations;
    }

    internal static LensMigration Parse(string json, string expectedId)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });
        JsonElement root = doc.RootElement;

        RejectUnknownKeys(root, expectedId, "migration",
            ["id", "build", "appliedAt", "notes", "stateHash", "changes"]);

        string id = RequiredString(root, "id", expectedId);
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"migration id '{id}' does not match its filename stem '{expectedId}'. The filename is "
                + "the replay order and the id is what every diagnostic quotes; they must be the same string.");
        }

        string build = RequiredString(root, "build", expectedId);
        string stateHash = RequiredString(root, "stateHash", expectedId);
        string? appliedAt = OptionalString(root, "appliedAt");
        string? notes = OptionalString(root, "notes");

        if (!root.TryGetProperty("changes", out JsonElement changesEl) || changesEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"migration '{expectedId}' has no 'changes' array. A migration with nothing to say should not exist.");
        }

        List<LensOp> ops = [];
        foreach (JsonElement opEl in changesEl.EnumerateArray())
        {
            ops.Add(ParseOp(opEl, expectedId));
        }

        return new LensMigration(id, build, appliedAt, notes, stateHash, ops);
    }

    private static LensOp ParseOp(JsonElement e, string migrationId)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"migration '{migrationId}' has a change that is not an object.");
        }

        string op = RequiredString(e, "op", migrationId);
        switch (op)
        {
            case "addClass":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "netName", "module"]);
                return new AddClassOp(
                    RequiredString(e, "class", migrationId),
                    OptionalString(e, "netName"),
                    OptionalString(e, "module"));

            case "removeClass":
                RejectUnknownKeys(e, migrationId, op, ["op", "class"]);
                return new RemoveClassOp(RequiredString(e, "class", migrationId));

            case "addField":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "field", "targetProperty"]);
                return new AddFieldOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "field", migrationId),
                    OptionalString(e, "targetProperty"));

            case "removeField":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "field"]);
                return new RemoveFieldOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "field", migrationId));

            case "rename":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "from", "to"]);
                return new RenameOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "from", migrationId),
                    RequiredString(e, "to", migrationId));

            case "addAlias":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "canonical", "alias"]);
                return new AddAliasOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "canonical", migrationId),
                    RequiredString(e, "alias", migrationId));

            case "moveSubService":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "from", "to"]);
                return new MoveSubServiceOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "from", migrationId),
                    RequiredString(e, "to", migrationId));

            case "typeShift":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "field", "fromType", "toType"]);
                return new TypeShiftOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "field", migrationId),
                    RequiredString(e, "fromType", migrationId),
                    RequiredString(e, "toType", migrationId));

            case "ignoreField":
                RejectUnknownKeys(e, migrationId, op, ["op", "class", "field"]);
                return new IgnoreFieldOp(
                    RequiredString(e, "class", migrationId),
                    RequiredString(e, "field", migrationId));

            default:
                throw new InvalidOperationException(
                    $"migration '{migrationId}' uses unknown op '{op}'. The vocabulary is closed: addClass, "
                    + "removeClass, addField, removeField, rename, addAlias, moveSubService, typeShift, "
                    + "ignoreField. Read semantics (transforms, lanes, defaults) are consumer-side and have "
                    + "no op here by design.");
        }
    }

    private static void RejectUnknownKeys(JsonElement e, string migrationId, string context, string[] allowed)
    {
        foreach (JsonProperty p in e.EnumerateObject())
        {
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"migration '{migrationId}' carries unknown key '{p.Name}' in a {context} entry. This file "
                    + "records history and naming only; keys that describe how to READ a value (transforms, "
                    + "wire types, fallback defaults) belong to the consumer and are rejected rather than "
                    + "silently dropped.");
            }
        }
    }

    private static string RequiredString(JsonElement e, string key, string migrationId)
    {
        if (e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
                                                     && el.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new InvalidOperationException(
            $"migration '{migrationId}' is missing required string '{key}'.");
    }

    private static string? OptionalString(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
