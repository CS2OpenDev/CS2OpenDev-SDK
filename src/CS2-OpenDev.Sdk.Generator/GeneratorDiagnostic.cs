#region

using System.Globalization;

#endregion

namespace CS2SchemaGen;

// Replaces Roslyn's `DiagnosticDescriptor` so the Generator project has no
// Microsoft.CodeAnalysis dependency. `MessageFormat` is a composite format
// string filled in by callers at report time.
internal sealed record GeneratorDiagnostic(
    string Id,
    GeneratorDiagnosticSeverity Severity,
    string MessageFormat)
{
    // Fills the composite format with `args`. Invariant culture so the emitted
    // message is identical on every locale.
    internal string Format(params object[] args) =>
        args.Length == 0
            ? MessageFormat
            : string.Format(CultureInfo.InvariantCulture, MessageFormat, args);
}

internal enum GeneratorDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
