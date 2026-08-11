# The handle type family

CS2's schema reflects entity and resource references through six atomic type
names. This document is the canonical list, written because every consumer that
touches them does so by **string-matching the type name**, and each one has been
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

Typed handles carry their template argument **in the atomic's `name`**, with
spaces inside the angle brackets:

```json
{
  "category": "ATOMIC",
  "name": "CHandle< CBaseEntity >",
  "inner": { "category": "DECLARED_CLASS", "module": "server.dll", "name": "CBaseEntity" }
}
```

Untyped handles have no `inner` and no template argument — the name is exactly
`CEntityHandle` or `CStrongHandleVoid`.

There is no `handle_kind` discriminator field. Older schema revisions carried
one and it was dropped; keying off the atomic name is the supported approach and
the names are stable. That decline is
[dispositioned upstream](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/4)
— demo files will never carry a discriminator either, since consumers decoding
`CSVCMsg_FlattenedSerializer` var-type symbols get the same bare strings.

### Matching trap: order your prefixes longest-first

Three of the six share a prefix. A naive `StartsWith("CStrongHandle")` matches
**`CStrongHandle`, `CStrongHandleCopyable` and `CStrongHandleVoid`** — including
the untyped one, whose width and semantics you probably branch on.

Test the longest names first:

```
CStrongHandleCopyable  →  CStrongHandleVoid  →  CStrongHandle
```

`CHandle` has no such conflict with the others, but note it is **not** a prefix
of `CEntityHandle` — they are separate names, not a family.

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
`windows-x86_64/entity_schema.json` at the same build yields **688**, the 20
extra all in `CStrongHandle` — the two artifacts do not cover an identical module
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

### Bit layout is deliberately not decoded

`CHandle<T>` and `CEntityHandle` expose the raw packed `Value` and nothing else.
The packing is `(serial << index_bits) | index`, currently 15 index bits and 17
serial bits — but that split is not documented authoritatively upstream, so the
SDK does not ship `EntityIndex` / `SerialNumber` accessors that would silently
become wrong if it changed. Decode it yourself if you need to, and pin the
assumption on your side. Adding those accessors later is a non-breaking change.

## Game events are a separate thing

Game-event fields use the KV1 tag `ehandle`, not these atomic names, and project
to a bare `uint`. See [MIGRATION-4.1](MIGRATION-4.1.md) for the player-reference
tags (`player_pawn`, `player_controller_and_pawn`) that also carry entity
handles.
