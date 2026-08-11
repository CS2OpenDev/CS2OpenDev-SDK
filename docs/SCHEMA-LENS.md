# Schema Lens — the curated class/field data layer

`schema-lens/` is an append-only migration history over the CS2 schema: which
classes this repo vouches for, which field paths, what each is called in .NET,
and what has happened to each across CS2 builds. The exporter replays the
migrations, gates the result against the current schema, and rewrites
`schema-lens/state.json` — the combined artifact consumers read.

This is the data layer accepted from issue #6. Two of that issue's questions
shape everything here:

* **§1 — how does the Lens avoid going stale?** Three gates, described below,
  make any drift between the Lens and the schema a hard regen failure. A Valve
  patch that touches a covered class fails CI until a human writes a migration
  saying what to do about it.
* **§3 — where do read semantics live?** Not here. This file carries history
  and naming ONLY. Transforms, storage lanes and fallback defaults are
  consumer-side (DemoViewer.NET keeps its own table), and the loader rejects
  any migration key from that side of the split by name.

## Directory layout

```
schema-lens/
  0000-20260811T0000Z-genesis.json   ← migrations, ordinal filename order
  0001-<YYYYMMDDTHHmmZ>-<desc>.json
  state.json                          ← OUTPUT — rewritten by every exporter run
```

Migration filenames follow `NNNN-<YYYYMMDDTHHmmZ>-<kebab-desc>.json` with a
fixed-width numeric prefix. The loader does not parse the prefix: ordinal
filename order IS the replay order, and the convention exists so a human
reading the directory sees the same order the loader uses. `state.json` is
skipped on load — it is a product of the directory, not an input to it.

## Migration file shape

```json
{
  "id": "0001-20260901T1200Z-example",
  "build": "14093",
  "appliedAt": "2026-09-01T12:00:00Z",
  "notes": "Why these changes exist, in prose.",
  "stateHash": "sha256:<hex of the canonical form AFTER this migration>",
  "changes": [ ...ops... ]
}
```

* `id` must equal the filename stem — hard error otherwise. Diagnostics quote
  the id; the filename orders the replay; the two must never diverge.
* `build` is the CS2 build the migration responds to (`"genesis"` for the
  baseline). It stamps `firstSeenBuild` on `addField` ops and the `build` on
  `typeShift` history entries.
* `appliedAt` and `notes` are informational; neither is replayed nor hashed.
* `stateHash` signs the CURATED state after this migration — see the canonical
  form below, and the authoring flow for how the value gets there.

## Ops

The vocabulary is closed. An unknown op is an error; an unknown key inside an
op is an error. Field paths are engine names, dotted for sub-service traversal
(`m_pInGameMoneyServices.m_iAccount`).

| op | required | optional | semantics |
|---|---|---|---|
| `addClass` | `class` | `netName`, `module` | Puts an engine class under coverage. `module` pins the schema module and is only needed when the bare name is ambiguous (e.g. `CCSPlayerController` exists in `client` and `server`). |
| `removeClass` | `class` | | Ends coverage. The class must be covered. |
| `addField` | `class`, `field` | `targetProperty` | Tracks a field path. Errors on duplicates, on an unknown class, and on colliding with an alias. Promotes a previously `ignoreField`-ed name to tracked. |
| `removeField` | `class`, `field` | | Stops tracking. Aliases pointing at the removed canonical are removed with it. |
| `rename` | `class`, `from`, `to` | | Moves the entry wholesale (`targetProperty`, `firstSeenBuild`, `typeHistory` all travel). Repoints every alias of `from` to `to`, keeps `from` as an alias of `to`, and adds a self-alias `to → to` so lookup by any historical name resolves. |
| `addAlias` | `class`, `canonical`, `alias` | | Errors if `canonical` is not tracked or `alias` collides with a canonical name. |
| `moveSubService` | `class`, `from`, `to` | | Identical mechanics to `rename`; a separate op so the history records *what happened* (a member migrated between service classes) rather than just *what changed*. |
| `typeShift` | `class`, `field`, `fromType`, `toType` | | Records the FACT of a schema type change, as rendered type-name strings. Deliberately no transform key — what a consumer does about a widened integer is read semantics (§3). |
| `ignoreField` | `class`, `field` | | Acknowledges a schema field the Lens deliberately does not track. Exists for the CS2_GEN_012 gate. Errors if the field is tracked or already ignored. |

## Derivation rules — and what an explicit value means

An **omitted** `targetProperty` derives mechanically from the LAST path
segment, through the same fold the class emitters use
(`NameHelpers.ToPropName`): access-prefix strip, Hungarian type-hint strip,
PascalCase, then the word splitter — which means the derived name obeys the
word vocabulary AND `names.lock.json` exactly like every property the SDK
emits. `m_iHealth → Health`, `m_bPawnIsAlive → PawnIsAlive`,
`m_pMovementServices.m_flStamina → Stamina`.

An **omitted** `netName` derives by stripping a leading `C` that is followed by
another uppercase letter: `CCSPlayerPawn → CSPlayerPawn`,
`CHEGrenadeProjectile → HEGrenadeProjectile`. A `C` followed by lowercase is a
word start and survives. Nothing else is stripped and nothing is guessed.

An **explicit** value is a curated override and always wins. The genesis
migration carries every `targetProperty` explicitly for exactly this reason:
those names were curated downstream, and the Lens records decisions, not
derivations that happen to coincide with them.

## The staleness gates (issue #6 §1)

Replay proves the migrations are internally coherent. The gates, run by the
exporter against the current schema and the previously **committed**
`state.json`, prove they still describe the world. All are errors; any failure
exits the exporter non-zero and leaves `state.json` unwritten.

**Resolution** — a covered class's bare engine name is matched across all
schema modules; exactly one match is required unless the migration pinned
`module`. Each tracked path is then walked segment by segment: a segment
resolves on the class's own fields or its ancestors', and traversal continues
through `PtrType → DeclaredClassType` and embedded `DeclaredClassType` by class
lookup. After the first hop the search also descends into derived classes,
because a sub-service pointer is statically typed as the engine base
(`CPlayer_ItemServices`) while the instance a CS2 entity carries is the game
derivation (`CCSPlayer_ItemServices`) — the static type is a lower bound. At
the ROOT no descent happens: a covered class names the concrete networked
type, and a field found only on a subclass belongs to some other entity.

* **CS2_GEN_010 UnresolvedLensField** — a covered class, or a tracked
  canonical path, no longer resolves (or no longer resolves uniquely). The
  remedy is always a migration: `rename` if the member moved, `removeField` /
  `removeClass` if it is gone, a `module` pin for an ambiguous class. The Lens
  must never silently serve a stale name.
* **CS2_GEN_011 LensRenameSuperseded** — a `rename` (or `moveSubService`)
  retired a path that the current schema declares AGAIN. The migration was
  right when written and has been overtaken: the re-grown name is a new
  declaration needing its own `addField` or `ignoreField`, and the old-name
  alias must be retired.
* **CS2_GEN_012 UnmigratedSchemaChange** — each covered class's freshly
  observed top-level field census is diffed against `observedFields` in the
  committed `state.json`. Any NEW field not accounted for by a migration
  (`addField` tracking it, or `ignoreField` acknowledging it) is a hard error.
  This is the tripwire that makes a Valve patch fail CI instead of shipping a
  stale Lens. Removals of untracked fields are not errors — nothing a consumer
  reads broke — they update `observedFields`, and the regen diff surfaces them
  in review. A class (or repo) with no committed baseline skips this gate; the
  first committed `state.json` becomes the baseline.

Two more diagnostics guard the mechanism itself: **CS2_GEN_013
InvalidLensMigration** (unparseable file, unknown op, consumer-side key, op
that does not apply cleanly) and **CS2_GEN_014 LensHashMismatch** (declared
`stateHash` disagrees with the replayed state — including the deliberate
placeholder case below).

## Canonical form and hash

`stateHash` and `state.json`'s `lensHash` are SHA-256 over the UTF-8 bytes of
a canonical text form, prefixed `sha256:` with lowercase hex. The form is
versioned by its first line.

Grammar — one record per line, `<path> = <json-value>`, LF separators, LF
after the last line:

```
lens-canon-1
class/<engineClass>/netName = <json>
class/<engineClass>/field/<canonicalPath>/targetProperty = <json>
class/<engineClass>/field/<canonicalPath>/firstSeenBuild = <json>
class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/build = <json>
class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/fromType = <json>
class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/toType = <json>
class/<engineClass>/alias/<alias> = <json>
class/<engineClass>/ignored/<i> = <json>
```

Ordering: classes, fields and aliases in ordinal key order; `ignored` values in
ordinal order, zero-indexed; `typeHistory` in applied order. Values are
JSON-native and lowercase: strings double-quoted, escaping ONLY what JSON
requires (quote, backslash, control characters); `null` as `null`; booleans
`true`/`false`; integers in decimal. No C# enum names, no CLR type tags.

The hash covers CURATED content only — classes, `netName`, per-field
`targetProperty` / `firstSeenBuild` / `typeHistory`, aliases, `ignored`. It
excludes everything derived from the schema (`observedFields`, `schemaType`,
`widthBytes`, `module`, `schemaBuild`): those change when Valve ships, and a
hash that revved on every patch would make each migration's signature a moving
target instead of a record of decisions.

## Authoring flow — the placeholder hash

A new migration declares the literal `"stateHash": "sha256:PLACEHOLDER"`.
Running the exporter then:

1. replays everything, computes the real hash,
2. prints it under CS2_GEN_014 (`Computed: sha256:… — paste it into the
   migration's stateHash and re-run`), and
3. **fails** (exit 1) — a placeholder must never survive into a green build.

Paste the printed value in, re-run, commit the migration together with the
rewritten `state.json`. The regen-diff gate in CI enforces that pairing.

## state.json

```json
{
  "lensHash": "sha256:…",
  "schemaBuild": "24537688",
  "classes": {
    "<engineClass>": {
      "netName": "…",
      "module": "<resolved module>",
      "fields": {
        "<canonicalPath>": {
          "targetProperty": "…",
          "schemaType": "<rendered current type>",
          "widthBytes": 4,
          "firstSeenBuild": "…",
          "typeHistory": [ { "build": "…", "fromType": "…", "toType": "…" } ]
        }
      },
      "aliases": { "<alias>": "<canonical>" },
      "ignored": [ "<fieldPath>" ],
      "observedFields": [ "<every top-level field currently declared on the class itself, sorted>" ]
    }
  }
}
```

Deterministic to the byte: ordinal-sorted map keys, fixed member order,
two-space indent, LF, one trailing newline. `typeHistory` is omitted when
empty — its presence is itself the signal. `schemaType` renders the schema's
own vocabulary (builtin name; `*` pointer suffix; `[N]` fixed array; atomic
template text as-is; declared class/enum by name). `widthBytes` is the
effective wire width where a builtin leaf makes it derivable (through pointer
and atomic wrappers; `int8`/`uint8`/`bool` = 1, `int16`/`uint16` = 2,
`int32`/`uint32`/`float32` = 4, `int64`/`uint64`/`float64` = 8) and `null`
where it honestly is not.

**Non-.NET consumers** need no package: fetch the file raw —
`https://raw.githubusercontent.com/<org>/CS2OpenDev-SDK/main/schema-lens/state.json`
— and key on `lensHash` to know when curation changed versus `schemaBuild` to
know when the world did.

## The transform split (issue #6 §3)

This repo answers "what is this field called, where does it live, what
happened to it". A consumer answers "how do I read it": value transforms
(handle unwrapping, bool-from-int), storage lanes, fallback defaults.
DemoViewer.NET keeps its transform table downstream and joins it to this
file's canonical paths. The loader enforces the boundary mechanically — a
`transform`, `wireType` or `fallbackDefault` key in a migration is a build
error naming the key, because a silently dropped key would let both sides
believe two different contracts.

## Versioning policy

Curated names are published API. Re-pointing a shipped stable name — changing
what an existing `targetProperty` or `netName` refers to, or removing one —
is a **major-version event**, exactly the policy `names.lock.json` applies to
emitted identifiers. Additive migrations (new classes, new fields, new
aliases, acknowledgments) ride in any release. History ops (`rename`,
`moveSubService`, `typeShift`) are additive by construction: the stable name
keeps working through the alias table, which is the point of carrying it.
