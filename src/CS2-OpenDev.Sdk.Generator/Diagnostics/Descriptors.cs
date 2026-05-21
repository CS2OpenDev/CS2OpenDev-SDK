namespace CS2SchemaGen.Diagnostics;

internal static class Descriptors
{
    // Raised by the CLI host when `SchemaModel.Parse` throws. Single source of
    // truth for the message format so the stderr line and any future structured
    // logger stay in sync.
    internal static readonly GeneratorDiagnostic ParseFailed = new(
        "CS2_GEN_001",
        GeneratorDiagnosticSeverity.Error,
        "Failed to parse schemas.json: {0}");

    // Raised by the CLI host when both `output/schemas.json` and the repo-root
    // `schemas.json` exist — the resolver silently prefers the dump-layout copy
    // but should tell the user which one it chose.
    internal static readonly GeneratorDiagnostic MultipleSchemaFiles = new(
        "CS2_GEN_002",
        GeneratorDiagnosticSeverity.Warning,
        "Multiple schemas.json inputs found; using {0}");

    // TM-2: a new schema-dumper type the TypeMapper hasn't classified yet falls
    // through to a stub class. Reported so the maintainer sees the gap instead
    // of finding an empty stub at consumer build time.
    internal static readonly GeneratorDiagnostic UnknownAtomicType = new(
        "CS2_GEN_003",
        GeneratorDiagnosticSeverity.Info,
        "Unknown atomic type '{0}' — emitted as empty stub class. Add it to TypeMapper if it has a meaningful C# projection.");
}
