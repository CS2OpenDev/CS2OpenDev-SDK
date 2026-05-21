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

    public void AddSource(string relativePath, string source)
    {
        string destPath = Path.GetFullPath(Path.Combine(outputDir, relativePath + ".cs"));

        string? destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
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
