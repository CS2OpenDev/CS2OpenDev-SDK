#region

using System.Text;
using CS2SchemaGen;
using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;

#endregion

namespace CS2_OpenDev.Sdk.Generator.Tests.Support;

// Drives the emitters via an in-memory CapturingSink. Replaced the previous
// CSharpGeneratorDriver-based harness when the architecture moved off the
// Roslyn source-generator pattern (Option C of the architecture-explore notes).
internal static class GeneratorHarness
{
    internal static RunResult Run(string schemasJson, string? customNamespace = null)
    {
        SchemaRoot schema = SchemaModel.Parse(schemasJson);
        string ns = customNamespace ?? "CS2Schema";

        CapturingSink sink = new();
        ModuleEmitter.EmitAll(sink, schema, ns);

        return new RunResult(sink.Files, sink.Diagnostics);
    }

    internal sealed record TestDiagnostic(string Id, GeneratorDiagnosticSeverity Severity, string Message);

    internal sealed record RunResult(
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyList<TestDiagnostic> Diagnostics)
    {
        /// <summary>True if at least one per-type file exists for <paramref name="module"/>.</summary>
        internal bool HasModule(string module)
        {
            string moduleDir = ModuleDir(module);
            string prefix = moduleDir + "/";
            foreach (string key in Files.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Concatenates every per-type file for <paramref name="module"/> in deterministic key order.</summary>
        internal string GetModuleSource(string module)
        {
            string moduleDir = ModuleDir(module);
            string prefix = moduleDir + "/";
            StringBuilder sb = new();
            foreach (KeyValuePair<string, string> kv in Files
                         .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                         .OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine(kv.Value);
            }

            return sb.ToString();
        }

        // Mirrors ModuleEmitter.ToNamespacePart — keeps the "shared" → "Common"
        // remap and the underscore-PascalCase rule in sync without exposing the
        // emitter helper.
        private static string ModuleDir(string module)
        {
            if (module.Equals("shared", StringComparison.OrdinalIgnoreCase))
            {
                return "Common";
            }

            StringBuilder sb = new();
            foreach (string segment in module.Split('_'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                sb.Append(char.ToUpperInvariant(segment[0]));
                if (segment.Length > 1)
                {
                    sb.Append(segment, 1, segment.Length - 1);
                }
            }

            return sb.Length > 0 ? sb.ToString() : module;
        }
    }

    private sealed class CapturingSink : IGeneratorSink
    {
        internal Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        internal List<TestDiagnostic> Diagnostics { get; } = new();

        public void AddSource(string relativePath, string source) => Files[relativePath] = source;

        public void ReportDiagnostic(GeneratorDiagnostic diagnostic, params object[] messageArgs) =>
            Diagnostics.Add(new TestDiagnostic(diagnostic.Id, diagnostic.Severity, diagnostic.Format(messageArgs)));
    }
}
