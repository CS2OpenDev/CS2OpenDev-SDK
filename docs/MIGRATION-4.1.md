# Migrating to CS2OpenDev.Sdk 4.1

4.1 fixes a decoding bug in the player-reference game-event fields. It **adds 59
properties** and **changes the type of 11**. Nothing is renamed, nothing moved
namespace, nothing was removed.

The 11 type changes are breaking on paper. In practice no working code can break
on them: those properties decoded as a constant `0` in every release up to and
including 4.0.1, because they read a key that is not on the wire. That is the bug.

Reported as
[CS2OpenDev-SchemaTracker#6](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/6),
where the wire keys were measured against GOTV demos.

## What was wrong

The schema declares three player-reference field types, and the engine derives
wire keys from the *type*, not from the field list. Only one of the three maps to
a single key of the same name:

| Declared type | Fields | Wire key(s) |
|---|---|---|
| `player_controller` | 82 | `<name>` — a userid |
| `player_controller_and_pawn` | 59 | `<name>` (userid) **and** `<name>_pawn` (entity handle) |
| `player_pawn` | 11 | `<name>_pawn` (entity handle) — **no `<name>` key at all** |

Through 4.0.1 the generator read `<name>` for all three. So:

- Every `player_controller_and_pawn` field silently dropped its pawn handle —
  the SDK exposed no property for it, and 59 wire keys were unreadable through
  the typed API.
- Every `player_pawn` field read a key that is never present. `GameEventReader`
  yields `0` for an absent key by design, so the miss surfaced as a plausible
  value rather than a failure.

Nothing in the extracted schema names the `_pawn` keys — they are a property of
the declared type, one representation level above the field list — which is why
this survived three majors.

## The 11 changed properties

Each keeps its name and its `[NativeName]`. The type changes from `int` to
`uint` (matching the existing `ehandle` projection), and the value now comes from
`<name>_pawn`.

| Event | Record | Property |
|---|---|---|
| `bomb_pickup` | `BombPickupEvent` | `UserId` |
| `break_breakable` | `BreakBreakableEvent` | `UserId` |
| `break_prop` | `BreakPropEvent`, `BreakPropCoreEvent` | `UserId` |
| `broken_breakable` | `BrokenBreakableEvent` | `UserId` |
| `decoy_started` | `DecoyStartedEvent` | `UserId` |
| `door_close` | `DoorCloseEvent` | `UserId` |
| `door_closed` | `DoorClosedEvent` | `UserId` |
| `door_open` | `DoorOpenEvent` | `UserId` |
| `player_decal` | `PlayerDecalEvent` | `UserId` |
| `player_footstep` | `PlayerFootstepCoreEvent` | `UserId` |

**What to do:** if you read any of these, you were reading `0`. Change the local
type to `uint` and treat the value as an entity handle, not a userid — it
identifies a pawn, so there is no controller slot to look up. If you had a
workaround that read the raw key yourself, delete it.

Note the property is a *handle* even though it is still called `UserId`: the
name is what the schema declares, and renaming it would be a second breaking
change on top of a bug fix. `[GameEventFieldType("player_pawn")]` on the property
is what tells you which flavour it is.

## The 59 added properties

Every `player_controller_and_pawn` field gains a companion named
`<Property>Pawn`, typed `uint`, reading `<name>_pawn`. The original property is
untouched — same name, same `int` type, same `<name>` key — so this half is
purely additive.

```csharp
// PlayerDeathEvent, before 4.1
public required int UserId { get; init; }        // the victim's userid
public required int Attacker { get; init; }
public required int Assister { get; init; }

// PlayerDeathEvent, 4.1
public required int  UserId       { get; init; } // unchanged
public required uint UserIdPawn   { get; init; } // new — victim's pawn handle
public required int  Attacker     { get; init; } // unchanged
public required uint AttackerPawn { get; init; } // new
public required int  Assister     { get; init; } // unchanged
public required uint AssisterPawn { get; init; } // new
```

The companions are spread across 50 events: 51 are `<name>Pawn` for `userid`,
5 for `attacker`, 2 for `victim`, 1 for `assister`.

On the companions, `[NativeName]` carries the wire key (`userid_pawn`) because
that is the identifier the engine emits and the one a reverse lookup needs — but
the XML remarks say **"Wire key … derived by the engine … rather than declared as
a field"**, not "Native name", because nothing in the schema declares it. The 11
`player_pawn` properties keep the ordinary "Native name" wording and their
declared name, since those *are* declared. If you reflect over `[NativeName]` to
decide what is schema-backed, use `[GameEventFieldType]` plus the property name
suffix rather than assuming every `[NativeName]` corresponds to a declared field.

Records use `required` init-only properties, so **if you construct event records
by hand** — in tests, fixtures, or fakes — the new properties are required and
your object initialisers will not compile until you set them. Decoding through
the generated factories is unaffected.

## `SchemaEvents` key constants

The per-event key tables now carry the **wire** key rather than the declared
name, because their job is to get you from a C# property to something you can
look up on the wire:

```csharp
// before                                    // 4.1
SchemaEvents.DoorCloseEvent.UserId           SchemaEvents.DoorCloseEvent.UserId
  == "userid"     // never present             == "userid_pawn"   // the real key
```

Only the 11 `player_pawn` fields change here. `[NativeName]` on the property
still reports the declared name, so both facts remain available and they are
deliberately different for these fields.

## Unaffected

`player_controller` (82 fields) is unchanged in every respect — one key, same
name, still `int`. No other type tag, namespace, or record is touched, and the
schema pin moves independently of this change.
