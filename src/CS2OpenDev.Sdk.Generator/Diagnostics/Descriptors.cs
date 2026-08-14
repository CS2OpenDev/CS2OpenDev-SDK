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
    //
    // Deliberately-stubbed atomics (TypeMapper.DeliberatelyStubbedAtoms) take
    // the stub path without the report: an entry there is a decision, not a
    // gap, and a diagnostic that re-reports decisions on every regen is how
    // this one spent three majors at 770 lines nobody read. The list this
    // prints is a to-do list, and it reads empty when everything is decided.
    internal static readonly GeneratorDiagnostic UnknownAtomicType = new(
        "CS2_GEN_003",
        GeneratorDiagnosticSeverity.Info,
        "Unknown atomic type '{0}' — emitted as empty stub class. Add it to TypeMapper if it has a meaningful C# projection, or to DeliberatelyStubbedAtoms with the reasoning if it does not.");

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
        "Upstream schema declares schema_format_version {0}, but this generator supports {1}. "
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

    // A run-together lowercase word WordSplitter could not segment, so it was
    // emitted as-is: `Somenewcompound` rather than `SomeNewCompound`.
    //
    // Reported because the failure is invisible otherwise. Not splitting is the
    // safe outcome by design — an unknown compound must never be guessed at —
    // but "safe" here means "silently keeps the old shape", and CS2 adds fields
    // every patch. Without this the vocabulary would rot: each new upstream name
    // that missed would look exactly like a name deliberately left alone.
    //
    // Info, not warning. Every entry is a candidate for the vocabulary, not a
    // defect — plenty are single words that are already correct.
    internal static readonly GeneratorDiagnostic UnsegmentedWord = new(
        "CS2_GEN_006",
        GeneratorDiagnosticSeverity.Info,
        "Could not split '{0}' into known words — emitted unsegmented. Add its parts to WordSplitter.Vocabulary if it is a compound, or to Atomic if it is one word.");

    // A `game-event-supplement.json` is present but unusable. Same reasoning as
    // InvalidOverrides: continuing would emit an SDK that compiles and quietly
    // omits the events the maintainer asked for — which is precisely the invisible
    // failure the supplement exists to prevent.
    internal static readonly GeneratorDiagnostic InvalidSupplement = new(
        "CS2_GEN_007",
        GeneratorDiagnosticSeverity.Error,
        "Invalid game-event supplement in {0}: {1}");

    // The supplement declares an event the extracted schema now declares too.
    //
    // Its own id rather than folding into CS2_GEN_007, because this is not a
    // maintainer mistake — it is the success condition. Upstream caught up, and
    // the fix is to delete the entry, not to correct it. A distinct id lets the
    // scheduled upstream-tracking workflow recognise the case on sight.
    internal static readonly GeneratorDiagnostic SupplementSuperseded = new(
        "CS2_GEN_008",
        GeneratorDiagnosticSeverity.Error,
        "Game-event supplement in {0} is superseded: {1}");

    // A run pinned in names.lock.json to a spelling the current vocabulary no
    // longer agrees with. The lock still wins — the emitted name is exactly what
    // it was — so this reports a divergence, never a change.
    //
    // Exists because the lock made CS2_GEN_006 structurally blind to anything
    // already shipped. A locked run short-circuits before segmentation, so once
    // a bad split is pinned it is pinned silently and forever; `isbot` shipped as
    // `Isbot` in 3.0.3 and no amount of vocabulary work could have surfaced it,
    // because the vocabulary was never consulted for that run again. A downstream
    // consumer reported it instead.
    //
    // Warning rather than Info, unlike CS2_GEN_006. That one lists candidates —
    // most entries are single words already spelled correctly. This one is a
    // concrete claim that a published identifier is wrong by the project's own
    // current rules, and it self-clears: it goes quiet the moment someone either
    // rebaselines (`--rebaseline-names`, a major version bump) or decides the
    // shipped spelling was right after all and adds the run to Atomic.
    internal static readonly GeneratorDiagnostic StaleLockedName = new(
        "CS2_GEN_009",
        GeneratorDiagnosticSeverity.Warning,
        "Locked name '{0}' is pinned as '{1}' but the vocabulary now reads it as '{2}'. "
        + "The lock wins and the emitted name is unchanged. Rebaseline with --rebaseline-names "
        + "(renames published API — major version bump) to adopt it, or add '{0}' to "
        + "WordSplitter.Atomic if the pinned spelling is the correct one.");

    // A name the Schema Lens tracks — a covered class, or a tracked field path
    // — no longer resolves against the current schema, or no longer resolves
    // uniquely.
    //
    // Error, and the load-bearing kind: this is the staleness gate issue #6 §1
    // asked for. The Lens serves names to consumers who key runtime lookups on
    // them, so a tracked name the schema has dropped is not a cosmetic drift —
    // it is a lookup that will silently miss on every entity of that class.
    // The one thing the Lens must never do is keep serving it. The fix is
    // always a migration: `rename` when the member moved, `removeField` /
    // `removeClass` when it is gone, a `module` pin when a bare class name
    // stopped being unique. The message carries the case-specific story in {1}
    // because the three cases have three different remedies.
    internal static readonly GeneratorDiagnostic UnresolvedLensField = new(
        "CS2_GEN_010",
        GeneratorDiagnosticSeverity.Error,
        "Schema Lens tracks '{0}', which does not resolve in the current schema: {1}");

    // A rename (or moveSubService) retired a path that the current schema
    // declares AGAIN.
    //
    // Its own id rather than folding into CS2_GEN_010, for the same reason
    // CS2_GEN_008 is not CS2_GEN_007: this is the self-retiring shape. The
    // migration was correct when written — upstream dropped the old name, the
    // rename recorded it — and now upstream has re-grown that name, so the
    // recorded history no longer describes the world. Nothing is "wrong" in
    // the file; it has been overtaken, and the response is to revisit the
    // migration (usually: the re-grown field is a NEW field that needs its own
    // addField, and the old-name alias must go). A distinct id lets tooling
    // and maintainers recognise the case on sight.
    internal static readonly GeneratorDiagnostic LensRenameSuperseded = new(
        "CS2_GEN_011",
        GeneratorDiagnosticSeverity.Error,
        "Schema Lens migration '{0}' is superseded: {1}");

    // A covered class gained a schema field that no migration has either
    // tracked (addField) or acknowledged (ignoreField).
    //
    // This is the tripwire that makes a Valve patch touching a covered class
    // FAIL CI instead of shipping a stale Lens. Without it, staleness is
    // one-sided: CS2_GEN_010 catches names the schema dropped, but a field the
    // schema ADDED simply doesn't exist as far as the Lens is concerned, and
    // "we never looked at it" is indistinguishable from "we chose not to track
    // it". The gate forces every new field through a human decision, and
    // ignoreField exists precisely so that decision can be "no, deliberately".
    // Removals of untracked fields are not errors — nothing a consumer reads
    // broke — they just update observedFields, and the regen diff surfaces
    // them in review.
    internal static readonly GeneratorDiagnostic UnmigratedSchemaChange = new(
        "CS2_GEN_012",
        GeneratorDiagnosticSeverity.Error,
        "Schema change not covered by a Lens migration: class '{0}' gained field '{1}'. "
        + "Track it with addField or acknowledge it with ignoreField — a covered class "
        + "must never drift past the Lens unremarked.");

    // A `schema-lens/` migration file is present but unusable — malformed
    // JSON, an unknown op, a key from the consumer's side of the §3 split, an
    // id that disagrees with its filename, or an op that does not apply
    // cleanly during replay. Same reasoning as CS2_GEN_007: continuing would
    // emit a state.json that quietly disagrees with what the maintainer wrote,
    // which is precisely the invisible failure the Lens exists to prevent.
    internal static readonly GeneratorDiagnostic InvalidLensMigration = new(
        "CS2_GEN_013",
        GeneratorDiagnosticSeverity.Error,
        "Invalid schema-lens migration in {0}: {1}");

    // A migration's declared stateHash does not match the hash of the replayed
    // state at that point in history.
    //
    // The hash is the author's signature over the curated content, so a
    // mismatch means the file changed after it was signed — hand-edited,
    // merge-mangled, or replayed by an implementation that disagrees about the
    // canonical form. All are worth stopping the build for. The deliberate
    // exception is the authoring flow: a brand-new migration declares the
    // literal placeholder, and this diagnostic is how the computed hash
    // reaches the author to be pasted in. That flow fails the run too — a
    // placeholder must never survive into a green build.
    internal static readonly GeneratorDiagnostic LensHashMismatch = new(
        "CS2_GEN_014",
        GeneratorDiagnosticSeverity.Error,
        "Schema Lens state hash for '{0}': {1}");

    // Upstream's own `atomicCategory` says this atomic is a container shape --
    // ATOMIC_COLLECTION_OF_T or ATOMIC_TT -- and TypeMapper does not project it as
    // one. Available from schema_format_version 2.1; before that the field is
    // absent and this can never fire.
    //
    // Deliberately one-directional. The reverse case, where this repo projects a
    // collection that upstream calls ATOMIC_T or ATOMIC_PLAIN, is not drift and is
    // not reported: CResourceArray and CRelativeArray are arrays by intent,
    // CUtlStringMap and CUtlDict are maps with an implied string key,
    // CUtlVectorSIMDPaddedVector ships without an `inner` at all. Those are C#
    // projection decisions, and the discriminator only describes C++ template
    // arity. Reporting them would train people to ignore this id.
    //
    // Error, and it fires zero times. That is the point.
    //
    // It shipped as a Warning describing a live defect, which is now repaired: the
    // count went 10 -> 0 when classification moved to the bare template name. A
    // count of zero is the invariant worth holding, so the severity moves with it,
    // and the exporter now exits non-zero when any Error-severity diagnostic
    // reaches the sink. A future schema major that changes the name shape again
    // fails the regen instead of adding lines to a log nobody reads — which is
    // exactly how the last one went unnoticed for three majors.
    //
    // The history it was written against, kept because it is the argument for the
    // severity: schema 2.0 made an atomic's `name` fully templated
    // (`CUtlVector< CGlobalSymbol >`) while TypeMapper's classification sets are
    // still keyed on the bare template name, so no templated atomic matches any of
    // them. 1,931 of 5,013 field-level atomics are templated and every one falls
    // through to a stub -- 880 CUtlVector, 331 CHandle, 173 CStrongHandle. The
    // CHandle<T> and CStrongHandle<T> value structs this generator emits are
    // referenced by exactly zero generated properties as a result.
    //
    // CS2_GEN_003 has been reporting the individual names all along, 770 per regen
    // at Info severity, which is a volume nobody reads. This one is deliberately
    // aggregated to the bare name and carries the field count, so the size of the
    // problem is legible: eight lines instead of eight hundred.
    //
    internal static readonly GeneratorDiagnostic AtomicCategoryDrift = new(
        "CS2_GEN_015",
        GeneratorDiagnosticSeverity.Error,
        "Upstream declares atomic '{0}' as {1} on {2} field(s), but TypeMapper stubs it "
        + "instead of projecting a collection or map. Either the schema changed the shape "
        + "of atomic names again, or a new container template arrived that no set in "
        + "TypeMapper knows. Classify it, or record why the projection deliberately differs.");

    // The same canonical path, or the same targetProperty, curated at two levels
    // of one curated inheritance chain.
    //
    // Legal before prefix layout, because every class owned its whole ordinal
    // space. Under layout(C) = layout(base) ++ own, a re-curated path would put
    // one field at two ordinals of one binding, and a re-curated targetProperty
    // would emit a derived C# property shadowing the base's — two members, one
    // name, which value you read decided by the static type of the reference.
    // BindingConformance's duplicate check would catch the path case, but at
    // some consumer's startup; the shadowing case the C# compiler reports only
    // as a warning. Both belong to the emitter, at generation, as errors — the
    // exporter exits non-zero on error-severity diagnostics (the post-3.0.7
    // guard), so an unattended regen cannot publish either shape.
    internal static readonly GeneratorDiagnostic DuplicateCurationAcrossChain = new(
        "CS2_GEN_016",
        GeneratorDiagnosticSeverity.Error,
        "Curated inheritance chain of '{0}': {1} Curate it at exactly one level — "
        + "under prefix layout a descendant's binding already carries everything its "
        + "curated ancestors track.");

    // An alias key colliding with a canonical path elsewhere in its chain, or
    // one alias key curated at two levels of the chain.
    //
    // Within one class the Lens replay already forbids both. Across levels
    // nothing did, and the failure is the quiet kind: a consumer routing reads
    // by path resolves the canonical spelling and silently ignores the alias,
    // while the aliased ordinal's candidate walk probes the colliding spelling
    // and can read a DIFFERENT field's storage. Readable-wrong, not loud —
    // DemoViewer.NET named this gate load-bearing for their by-path routing
    // (SDK#30), which is why it is an error here rather than a startup check
    // in someone else's process.
    internal static readonly GeneratorDiagnostic AliasConflictAcrossChain = new(
        "CS2_GEN_017",
        GeneratorDiagnosticSeverity.Error,
        "Curated inheritance chain of '{0}': {1} Alias keys and canonical paths share "
        + "one namespace per binding, and under prefix layout a binding's namespace is "
        + "its whole chain's.");
}
