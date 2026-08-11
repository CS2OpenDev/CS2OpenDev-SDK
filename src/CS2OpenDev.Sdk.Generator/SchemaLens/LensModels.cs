namespace CS2SchemaGen.SchemaLens;

// The Schema Lens data layer (issue #6).
//
// A migration is an append-only record of curation decisions over the schema:
// which classes a consumer is allowed to rely on, which field paths, what each
// one is called in .NET, and what happened to it across CS2 builds. Replaying
// every migration in order yields the current `LensState`; the exporter then
// checks that state against the live schema and emits `schema-lens/state.json`
// for consumers to read.
//
// The op vocabulary is deliberately CLOSED and carries history + naming ONLY.
// Read semantics — value transforms, storage lanes, fallback defaults — are the
// consumer's business (issue #6 §3): they depend on how a consumer stores
// decoded values, which this repo cannot know and must not guess. A migration
// that tries to smuggle a `transform` key in is rejected at parse time, not
// ignored, because a silently dropped key would let two sides believe two
// different contracts.

// One migration file, already validated against its filename. `AppliedAt` and
// `Notes` are informational prose for the reader of the file; neither takes
// part in replay or hashing.
internal sealed record LensMigration(
    string Id,
    string Build,
    string? AppliedAt,
    string? Notes,
    string StateHash,
    IReadOnlyList<LensOp> Changes)
{
    // The literal a migration author writes into `stateHash` before the real
    // hash exists. The exporter computes the hash, prints it, and fails — the
    // author pastes the printed value in and re-runs. Failing rather than
    // auto-filling is deliberate: the hash is a claim the author signs, and a
    // tool that signs on their behalf reduces the claim to a checksum.
    internal const string PlaceholderHash = "sha256:PLACEHOLDER";
}

// ── Ops ──────────────────────────────────────────────────────────────────────
//
// A tagged union, one record per op. Field paths are engine names, dotted for
// sub-service traversal (`m_pInGameMoneyServices.m_iAccount`).

internal abstract record LensOp;

// Puts a class under Lens coverage. `NetName` is the .NET-facing class name;
// when omitted it is derived by stripping a leading 'C' that is followed by
// another uppercase letter (CCSPlayerPawn → CSPlayerPawn). `Module` pins the
// schema module and is only needed when the bare engine name is ambiguous —
// CCSPlayerController exists in both `client` and `server`.
internal sealed record AddClassOp(string Class, string? NetName, string? Module) : LensOp;

internal sealed record RemoveClassOp(string Class) : LensOp;

// Tracks a field path. `TargetProperty` is the curated .NET property name; when
// omitted it is derived from the LAST path segment via the same fold the class
// emitters use (NameHelpers.ToPropName), so the derived name obeys the word
// vocabulary and the name lock like every other emitted identifier.
internal sealed record AddFieldOp(string Class, string Field, string? TargetProperty) : LensOp;

internal sealed record RemoveFieldOp(string Class, string Field) : LensOp;

// Moves a field entry wholesale to a new canonical path: target property, first
// seen build and type history all travel with it. Every alias that pointed at
// `From` is repointed, `From` itself becomes an alias of `To`, and `To` gains a
// self-alias so a lookup by any name the field has ever had lands on the same
// entry.
internal sealed record RenameOp(string Class, string From, string To) : LensOp;

internal sealed record AddAliasOp(string Class, string Canonical, string Alias) : LensOp;

// Identical mechanics to Rename. A separate op because the two record different
// authorial intent: a rename is Valve renaming a member, a sub-service move is
// a member migrating between service classes — and a future reader of the
// migration history should not have to reverse-engineer which happened.
internal sealed record MoveSubServiceOp(string Class, string From, string To) : LensOp;

// Records the FACT that a field's schema type changed between builds, as
// rendered type-name strings. Deliberately no transform key: what a consumer
// does about a widened integer is read semantics, and read semantics live on
// the consumer's side of the §3 split.
internal sealed record TypeShiftOp(string Class, string Field, string FromType, string ToType) : LensOp;

// Acknowledges a schema field on a covered class that the Lens deliberately
// does not track. Exists for the CS2_GEN_012 gate: a new upstream field is a
// hard error until a migration either tracks it or acks it, so "we looked at
// it and chose not to care" must be expressible.
internal sealed record IgnoreFieldOp(string Class, string Field) : LensOp;

// ── Replayed state ───────────────────────────────────────────────────────────
//
// Everything below is CURATED content — it comes only from migrations, never
// from the schema, and it is exactly the content the canonical hash covers.
// Schema-derived data (types, widths, observed fields) is attached at
// state.json render time and stays out of these types on purpose.

internal sealed class LensState
{
    internal SortedDictionary<string, LensClassState> Classes { get; } = new(StringComparer.Ordinal);
}

internal sealed class LensClassState
{
    internal required string NetName { get; set; }

    // The module the migration pinned via addClass, or null when the bare name
    // is expected to be unambiguous. Resolution enforces that expectation.
    internal string? ModulePin { get; set; }

    internal SortedDictionary<string, LensFieldEntry> Fields { get; } = new(StringComparer.Ordinal);

    // alias → canonical. May contain self-entries: rename adds `to → to` so the
    // alias table alone answers "what canonical does this name mean today" for
    // every name a field has ever carried.
    internal SortedDictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    internal SortedSet<string> Ignored { get; } = new(StringComparer.Ordinal);
}

internal sealed class LensFieldEntry
{
    internal required string TargetProperty { get; set; }

    internal required string FirstSeenBuild { get; set; }

    internal List<LensTypeShift> TypeHistory { get; } = [];
}

internal sealed record LensTypeShift(string Build, string FromType, string ToType);

// ── Replay result ────────────────────────────────────────────────────────────

// One per migration: the hash the file declares against the hash the replayed
// state actually has at that point in history. Kept as data rather than thrown,
// because the placeholder authoring flow needs the computed value to reach the
// console, and because a mismatch does not stop the replay from being
// well-defined — it stops the run from being trusted.
internal sealed record LensHashCheck(
    string MigrationId,
    string DeclaredHash,
    string ComputedHash,
    bool IsPlaceholder)
{
    internal bool Matches =>
        !IsPlaceholder && string.Equals(DeclaredHash, ComputedHash, StringComparison.Ordinal);
}

// A rename or moveSubService that history has applied, kept for the
// CS2_GEN_011 gate: if `From` ever resolves in a future schema again, the
// migration that retired it must be revisited. `Op` names which of the two ops
// it was, purely so the gate's message can quote the author's own vocabulary.
internal sealed record LensRenameRecord(string Class, string From, string To, string Op, string MigrationId);

internal sealed class LensReplayResult
{
    internal required LensState State { get; init; }

    internal required IReadOnlyList<LensHashCheck> HashChecks { get; init; }

    internal required IReadOnlyList<LensRenameRecord> Renames { get; init; }
}
