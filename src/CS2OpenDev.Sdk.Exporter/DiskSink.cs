using System.Text;
using CS2SchemaGen;

namespace CS2OpenDev.SdkExporter;

// IGeneratorSink that writes each emitted source to `{outputDir}/{relativePath}.cs`,
// creating subdirectories as needed. Removes each written path from the
// stale-candidates set so Program's post-emission sweep only deletes files this
// run did not produce. Diagnostics are buffered for the host to print after
// emission finishes.
internal sealed class DiskSink(string outputDir, HashSet<string> staleCandidates) : IGeneratorSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public int WrittenCount { get; private set; }

    public List<(string Id, GeneratorDiagnosticSeverity Severity, string Message)> Diagnostics { get; } = new();

    // Every path this run has written, so a second write to the same path is a
    // hard error rather than a silent overwrite.
    //
    // It cost an afternoon to learn why that matters. Word-splitting identifiers
    // collapsed 16 distinct schema types onto 8 output paths — `smokegrenade_*`
    // and `smoke_grenade_*` both became `SmokeGrenade*` — and the second write
    // simply won. The run stayed green, reported "Exported 4612 file(s)" while
    // leaving 4596 on disk, and the loss only surfaced as CS0246 on a reference
    // to a type that no longer existed. The count was in the output the whole
    // time and said nothing, because nothing compared it to reality.
    private readonly Dictionary<string, string> _written = new(StringComparer.Ordinal);

    public void AddSource(string relativePath, string source)
    {
        string destPath = Path.GetFullPath(Path.Combine(outputDir, relativePath + ".cs"));

        if (_written.TryGetValue(destPath, out string? firstPath))
        {
            throw new InvalidOperationException(
                $"Two emissions target the same file: '{relativePath}' collides with " +
                $"'{firstPath}'. Distinct schema types must not fold onto one C# " +
                $"identifier — the second would silently replace the first, and the " +
                $"loss surfaces later as an unresolved type reference. Add the losing " +
                $"word to WordSplitter.Atomic, or give the pair distinct names.");
        }

        _written[destPath] = relativePath;

        string? destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);

            // Force the on-disk casing to match what we are emitting.
            //
            // macOS and Windows resolve paths case-insensitively, so writing
            // `SoundLevel.cs` over an existing `Soundlevel.cs` updates the old
            // inode and KEEPS THE OLD NAME. The write appears to succeed. Then
            // the stale sweep — which holds the old casing and is told only the
            // new one — does not see the path as claimed, and deletes the file
            // that was just written.
            //
            // That is how word-splitting silently dropped 16 types here while
            // the same generator produced a correct tree on Linux CI. A
            // case-only rename is exactly the shape this change produces, so
            // this is not a corner case for it. Deleting first makes the rename
            // real on every filesystem; on Linux the sibling never matches and
            // this costs one directory listing per file.
            string destName = Path.GetFileName(destPath);
            foreach (string sibling in Directory.EnumerateFiles(destDir, "*.cs"))
            {
                string siblingName = Path.GetFileName(sibling);
                if (!string.Equals(siblingName, destName, StringComparison.Ordinal) &&
                    string.Equals(siblingName, destName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(sibling);
                    staleCandidates.Remove(Path.GetFullPath(sibling));
                }
            }
        }

        // Normalise line endings — emitters use AppendLine which writes the
        // platform's native separator. The committed SDK source is LF-only so
        // diffs stay stable across Windows / macOS / Linux contributors.
        if (source.Contains("\r\n", StringComparison.Ordinal))
        {
            source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        // Normalise trailing whitespace: emitters tend to leave an extra blank
        // line after the last `}` (each emitter ends with `AppendLine()` for
        // multi-class spacing). End every file with exactly one `\n` so the
        // committed SDK source is whitespace-stable.
        source = source.TrimEnd() + "\n";

        File.WriteAllText(destPath, source, Utf8NoBom);
        staleCandidates.Remove(destPath);
        WrittenCount++;
    }

    public void ReportDiagnostic(GeneratorDiagnostic diagnostic, params object[] messageArgs) =>
        Diagnostics.Add((diagnostic.Id, diagnostic.Severity, diagnostic.Format(messageArgs)));
}
