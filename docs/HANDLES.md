# The handle type family

CS2's schema reflects entity and resource references through six atomic type
names. This document is the canonical list, written because every consumer that
touches them does so by string-matching the type name, and each one has been
re-deriving a slightly different subset from scratch.

Requested in
[CS2OpenDev-SchemaTracker#4](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/4):
a written spec the string match can be checked against.

## The six

| Atomic name | Width | Typed | Owns | What it references |
|---|---|---|---|---|
| `CHandle<T>` | 32-bit | yes | — | An **entity**. Packed index + serial number. |
| `CEntityHandle` | 32-bit | no | — | An entity, target type not carried in the schema. |
| `CStrongHandle<T>` | 64-bit | yes | yes | A **resource** (material, model, sound, …). |
| `CStrongHandleCopyable<T>` | 64-bit | yes | yes* | Same, but does not single-own its resource. |
| `CStrongHandleVoid` | 64-bit | no | yes | A resource, target type not carried. |
| `CWeakHandle<T>` | 64-bit | yes | no | A resource, non-owning. |

The split that matters for codegen is **typed vs untyped**, not entity vs
resource: the four typed names carry an `inner` node naming the referenced
declared-class, and the two untyped ones do not.

Entity handles and resource handles are different-width values with unrelated
sentinels, so a consumer that lumps all six together will misread one group or
the other.

## How they appear in the schema

Typed handles carry their template argument in the atomic's `name`, with
spaces inside the angle brackets:

```json
{
  "category": "ATOMIC",
  "name": "CHandle< CBaseEntity >",
  "inner": { "category": "DECLARED_CLASS", "module": "server.dll", "name": "CBaseEntity" }
}
```

Untyped handles have no `inner` and no template argument: the name is exactly
`CEntityHandle` or `CStrongHandleVoid`.

There is no `handle_kind` discriminator field. Older schema revisions carried
one and it was dropped; keying off the atomic name is the supported approach and
the names are stable. That decline is
[dispositioned upstream](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/4):
demo files will never carry a discriminator either, since consumers decoding
`CSVCMsg_FlattenedSerializer` var-type symbols get the same bare strings.

### Matching trap: order your prefixes longest-first

Three of the six share a prefix. A naive `StartsWith("CStrongHandle")` matches
`CStrongHandle`, `CStrongHandleCopyable` and `CStrongHandleVoid`, including
the untyped one, whose width and semantics you probably branch on.

Test the longest names first:

```
CStrongHandleCopyable  →  CStrongHandleVoid  →  CStrongHandle
```

`CHandle` has no such conflict with the others, but note it is *not* a prefix
of `CEntityHandle`; they are separate names, not a family.

### Spelling

Strip template arguments before comparing. Spellings observed in the wild
across artifacts and demo-derived strings:

```
CHandle< T >     — this repo's schema input (space inside both brackets)
CHandle<T>       — no spaces
CHandle <T>      — space before the bracket
CHandle&lt;T&gt; — XML/HTML-escaped, from doc-derived sources
```

Split on the first `<` (after unescaping) and trim, rather than matching a
literal `CHandle<`.

## Counts

At the schema this repo currently pins (CS2 build 24662694), counting `ATOMIC`
nodes by prefix over a recursive walk including `inner`:

| Name | Count |
|---|---|
| `CHandle` | 404 |
| `CStrongHandle` | 187 |
| `CWeakHandle` | 42 |
| `CEntityHandle` | 29 |
| `CStrongHandleCopyable` | 4 |
| `CStrongHandleVoid` | 2 |
| **total** | **668** |

Counted against this repo's input, `CS2OpenDev-Docs`'
`cs2_schema.json`. The same walk over SchemaTracker's raw
`windows-x86_64/entity_schema.json` at the same build yields 688, the 20
extra all in `CStrongHandle`. The two artifacts do not cover an identical module
set, so use the number that matches whichever one you consume rather than
treating either as *the* count.

## What this SDK projects them to

`CS2OpenDev.Sdk` emits a value struct per name (see `Handles.cs`). Each wraps the
raw packed value and exposes `Value`, `IsValid` and an `Invalid` sentinel:

| Atomic | C# type | Backing | Invalid sentinel |
|---|---|---|---|
| `CHandle<T>` | `CHandle<T>` | `uint` | `0xFFFFFFFF` |
| `CEntityHandle` | `CEntityHandle` | `uint` | `0xFFFFFFFF` |
| `CStrongHandle<T>` | `CStrongHandle<T>` | `ulong` | `0xFFFFFFFFFFFFFFFF` |
| `CStrongHandleCopyable<T>` | `CStrongHandleCopyable<T>` | `ulong` | `0xFFFFFFFFFFFFFFFF` |
| `CStrongHandleVoid` | `CStrongHandleVoid` | `ulong` | `0xFFFFFFFFFFFFFFFF` |
| `CWeakHandle<T>` | `CWeakHandle<T>` | `ulong` | `0xFFFFFFFFFFFFFFFF` |

`T` is the C# projection of the atomic's `inner` declared-class.

Worth stating because it was not true until 5.0: these projections now actually appear on
generated properties. Through 2.x, 3.x and 4.x the classification that produces them could not
match a templated atomic name, so every handle field was emitted as an empty stub class and
`CHandle<T>` was referenced by no generated property at all. This page described the intent
correctly and the output not at all. See [MIGRATION-5.0](MIGRATION-5.0.md).

### Bit layout is deliberately not decoded

`CHandle<T>` and `CEntityHandle` expose the raw packed `Value` and nothing else.
The packing is `(serial << index_bits) | index`. How many bits the index gets is
not something this repository can tell you, so the SDK ships no `EntityIndex` /
`SerialNumber` accessors that would silently become wrong. Decode it yourself if
you need to, and pin the assumption on your side. Adding those accessors later,
if the fact ever becomes citable, is a non-breaking change.

An earlier revision of this page named a specific split (15 index bits, 17 serial
bits). That number had no source and has been removed. Nothing in what this repo
consumes carries it: `cs2_schema.json` describes types, not engine limits, and
SchemaTracker's `engine_constants.json` at build 24701871 is 4,721 constants of
which every single one has `source: schema_enum`. There is no `MAX_EDICTS`, no
`NUM_ENT_ENTRY_BITS`, no entity-limit constant of any kind in it. Stating a split
in prose while declining to encode it in code was the page contradicting itself,
and the prose was the half without evidence behind it.

The disagreement is not hypothetical. DemoViewer.NET's `EntityTracker` masks
`handle & 0x3FFF` (14 index bits) while this page asserted 15. Two
implementations in one ecosystem, two different numbers, neither citing anything.
Raised in the [entity-abstraction thread](https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/6),
where it settles a design question rather than opening one: **generated wrapper
code must never decode a handle.** It passes the raw packed value to the runtime,
and mask, sentinel and serial-validation policy stay the runtime's business. Had
the SDK shipped accessors, one of those two readings would now be baked into
published API.

### What the upstream investigation found

[SchemaTracker#11](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/11) asked for the
constant to be extracted. The answer came back as a **verified negative**: it is not obtainable.

String pools on both platforms carry no `NUM_ENT_ENTRY_BITS`, `NUM_SERIAL_NUM_BITS` or `MAX_EDICT*`
under any spelling — the binaries do not name them even in assert text. `CEntityHandle` presents as
an opaque atomic with no bitfield decomposition, and `CEntityIdentity`'s schema-visible fields start
at offset 20, so the handle and serial bytes live in a prefix the type system deliberately hides.
What remains is mask immediates baked into optimised code, which would mean per-era, per-platform
disassembly matching — the "correct on every build tried so far" fragility this ecosystem exists to
avoid.

But the investigation turned up something better than the number: the two implementations may
both have been right, about different encodings.

`const.h` in the pinned hl2sdk defines `NUM_NETWORKED_EHANDLE_BITS = 14 + 10` — a 24-bit
networked handle, 14-bit index and 10-bit serial — separately from the in-memory 32-bit
`CEntityHandle`, whose entry width is implied at 15 by `MAX_TOTAL_ENTITIES = 0x8000`. So a 14-bit
mask matches the networked encoding, and 15 matches the in-memory one. If networked entities only
ever occupy entries below 16,384, the narrow mask also works on in-memory handles for every entity
a demo can reference — which is exactly how a wrong-in-principle mask passes every test anyone runs.

That is a hypothesis with evidence, not a fact, and this page will not promote it to one. Note that
the reference SDK contradicts itself even here: `const.h` says `NUM_ENT_ENTRY_BITS = 16` while its
own comment (`32 - NUM_ENT_ENTRY_BITS`, against `NUM_SERIAL_NUM_BITS = 17`) implies 15, so the
header cannot be cited for either value.

Upstream's guidance is that a curated number belongs in Docs' `well_known_constants.json` (cited,
and visibly not machine-extracted) rather than in an auto-extracted artifact. Until someone
curates it there, "implementation-defined" is the honest value and the one this page keeps. The
design conclusion is unchanged and now better supported: generated code never decodes a handle, and
whichever encoding a runtime is looking at is its own to know.

## Game events are a separate thing

Game-event fields use the KV1 tag `ehandle`, not these atomic names, and project
to a bare `uint`. See [MIGRATION-4.1](MIGRATION-4.1.md) for the player-reference
tags (`player_pawn`, `player_controller_and_pawn`) that also carry entity
handles.
