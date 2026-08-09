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

    // Raised when the upstream schema declares a `schema_format_version` whose
    // major differs from the one this generator was written against.
    //
    // Without this the mismatch surfaces as whatever the shape change happens
    // to break first — for the 1.0 → 2.0 move that was
    // "requires an element of type 'Number', but the target element has type
    // 'String'" out of the field-offset parse, which says nothing about the
    // actual cause. The scheduled upstream-tracking workflow re-runs every four
    // hours, so an opaque failure gets re-reported indefinitely; this one names
    // the versions and points at the migration notes.
    internal static readonly GeneratorDiagnostic UnsupportedSchemaFormat = new(
        "CS2_GEN_004",
        GeneratorDiagnosticSeverity.Error,
        "Upstream schema declares schema_format_version {0}, but this generator supports {1}.x only. "
        + "The schema shape changed and the generator has not been migrated — see "
        + "docs/upstream/schematracker-migration.md for the breaking surface and the upstream blockers.");

    // Raised when a consumer-supplied game-event overrides file is present but
    // unusable. Reported rather than ignored: silently falling back to the
    // built-in projections would produce an SDK that compiles and quietly
    // ignores what the consumer asked for.
    internal static readonly GeneratorDiagnostic InvalidOverrides = new(
        "CS2_GEN_005",
        GeneratorDiagnosticSeverity.Error,
        "Invalid game-event overrides in {0}: {1}");
}
