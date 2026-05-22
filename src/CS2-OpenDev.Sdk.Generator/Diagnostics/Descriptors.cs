namespace CS2SchemaGen.Diagnostics;

internal static class Descriptors
{
    // Raised by the CLI host when `SchemaModel.Parse` or `GameEventsModel.Parse`
    // throws. Single source of truth for the message format so the stderr line
    // and any future structured logger stay in sync.
    internal static readonly GeneratorDiagnostic ParseFailed = new(
        "CS2_GEN_001",
        GeneratorDiagnosticSeverity.Error,
        "Failed to parse schema JSON: {0}");

    // Raised by the CLI host when the resolver finds multiple candidate schema
    // paths and has to pick one. Today this fires when both `output/schemas.json`
    // (legacy DumpSource2 output layout) and a repo-root `schemas.json` (legacy
    // vendored copy) exist — the upstream submodule path is always preferred
    // over either, so this case only surfaces for offline / migration scenarios.
    internal static readonly GeneratorDiagnostic MultipleSchemaFiles = new(
        "CS2_GEN_002",
        GeneratorDiagnosticSeverity.Warning,
        "Multiple local schema inputs found; using {0}. Prefer the upstream submodule path.");

    // TM-2: a new schema-dumper type the TypeMapper hasn't classified yet falls
    // through to a stub class. Reported so the maintainer sees the gap instead
    // of finding an empty stub at consumer build time.
    internal static readonly GeneratorDiagnostic UnknownAtomicType = new(
        "CS2_GEN_003",
        GeneratorDiagnosticSeverity.Info,
        "Unknown atomic type '{0}' — emitted as empty stub class. Add it to TypeMapper if it has a meaningful C# projection.");
}
