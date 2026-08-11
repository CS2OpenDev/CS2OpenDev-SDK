namespace CS2SchemaGen.Models;

// Locates the optional, hand-maintained JSON files that sit *beside* generation
// rather than inside it — `game-event-overrides.json` and
// `game-event-supplement.json`.
//
// Both follow the same two-step search, and they have to: a consumer who put one
// next to their schema dump and the other in their working directory would
// otherwise get one honoured and one silently ignored. Sharing the resolution
// makes "the same way" true by construction instead of by two copies of four
// lines staying in step.
//
// It lives in the generator library rather than the CLI host purely so it can be
// tested — "an absent file is a no-op" is a property of the resolver, and the
// host is top-level statements that no test can drive.
internal static class SideCarFile
{
    // Next to the schema first, working directory second. Schema-adjacent wins
    // because that is the copy that travels with a specific schema dump; the
    // working-directory copy is the repo-wide default (this repo keeps its own
    // supplement at the root, where the CI regen runs from).
    //
    // Returns null when neither exists, which every caller treats as "no file,
    // change nothing" — never as an error.
    internal static string? Resolve(string fileName, string? schemaDirectory, string workingDirectory)
    {
        string beside = Path.GetFullPath(Path.Combine(
            string.IsNullOrEmpty(schemaDirectory) ? "." : schemaDirectory, fileName));
        if (File.Exists(beside))
        {
            return beside;
        }

        string cwd = Path.GetFullPath(Path.Combine(workingDirectory, fileName));
        return File.Exists(cwd) ? cwd : null;
    }
}
