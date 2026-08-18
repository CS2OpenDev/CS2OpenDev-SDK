# Migrating to CS2OpenDev.Sdk 6.0

**50 properties across 19 classes change type, 5 stub classes leave the public surface, and one
struct (`RnSphere`) is added.** As in 5.0, nothing is renamed and nothing moves namespace, and
every retyped property was typed as an empty stub class before (a name with no members), so no
working code can have read a value through one.

Same CS2 build as 5.0, 24701871. The inventory regenerates with:

```
dotnet run --configuration Release --project src/CS2OpenDev.Sdk.Exporter
```

## Why 6.0

The 5.0 repair left an eleven-entry residue: atomics that were genuinely unclassified rather than
broken, each needing a deliberate decision about what it should project to
([#33](https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/33)). This release decides all eleven.
Four get projections; four templates (seven of the eleven names) are recorded as deliberately
stubbed. Retyping a public property is a breaking change, so the family major moves.

## The eleven decisions

| Atom | Decision | Now |
|---|---|---|
| `CGameSoundEventName` | classify | `string` — sound-event name; sibling of `CSoundEventName`, which has projected `string` all along |
| `CUtlStringTokenNoRegistration` | classify | `string` — `CUtlStringToken` minus the debug-registry side effect; keys `CEntityAttributeTable`'s maps |
| `CUtlDict< GameTime_t >` | classify | `Dictionary<string, GameTime>` — `CUtlDict` is `CUtlMap` with a const-char* key; schema entries carry only the value type, same as `CUtlStringMap` |
| `CUtlDict< CPhysicsBodyGameMarkup >` | classify | `Dictionary<string, CPhysicsBodyGameMarkup>` — same entry; classification keys on the bare template name |
| `RnSphere_t` | classify | `RnSphere` struct — `Vector Center` + `float Radius`; shape pinned by `RnSphereDesc_t`'s layout and the reflected sibling `RnCapsule_t` |
| `CPulseObservableExpression< bool >` | stub, deliberately | 120 bytes at every use site — it carries the observable expression, not a `bool` |
| `CPulseObservableExpression< float32 >` | stub, deliberately | same template, same decision |
| `CPulseObservableExpression< CUtlString >` | stub, deliberately | same template, same decision |
| `HPulseCell< CPulseCell_TestWaitWithCursorState >` | stub, deliberately | Pulse VM handle with no sourced layout; sole use is a Test cell's cursor state |
| `HPulseCellBase` | stub, deliberately | same |
| `HYieldedCursor` | stub, deliberately | 12 bytes by offset arithmetic — provably not a pointer, so `nint` would lie |

The deliberate stubs are recorded in `TypeMapper.DeliberatelyStubbedAtoms` with the full evidence;
their generated summaries in `Stubs.cs` point there. They no longer appear in `CS2_GEN_003`, which
now reports **zero**. The report is a to-do list; it reads empty because everything on it has been
decided, not because nobody is looking. A schema that re-categorises one of them as a
container still trips the Error-severity `CS2_GEN_015`.

## What you have to do

Most consumers, again: nothing. The five removed types (`CGameSoundEventName`,
`CUtlStringTokenNoRegistration`, `CUtlDict__GameTime_t__`, `CUtlDict__CPhysicsBodyGameMarkup__`,
`RnSphere_t`) were empty classes, so no code could have read a value out of one. If you never
referenced any of those names, your code compiles unchanged and the 50 properties start carrying
data.

If you referenced a removed stub by name, replace it with the projection from the table above; the
index below lists every affected property by declaring class.

If you referenced `RnSphere_t`, it is now the readonly struct `RnSphere` in the SDK root
namespace, with `Center` and `Radius` members.

The three `CPulseObservableExpression__*__` stubs, `HPulseCell__CPulseCell_TestWaitWithCursorState__`,
`HPulseCellBase` and `HYieldedCursor` still exist and their 7 properties are unchanged.

## What did not change

Same list as 5.0:

- No renames, no namespace moves. Every property keeps its name; only its type moves.
- `[NativeName]` and `[NativeOffset]` carry the native identity, independent of the C# projection.
- `SchemaNames` — no property name moved.
- Game events. `CS2OpenDev.Sdk.GameEvents` decodes by native KV1 key at runtime; its major moves
  only to keep the family in step, and the same goes for `CS2OpenDev.Protos`.
- The entity read contract. `CS2OpenDev.Sdk.Entities.Abstractions` stays on its own 0.x clock, and
  none of the 19 classes here crosses that seam.

## Index of changed properties, by declaring class

### `CEnvSoundscape`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SoundEventName` | `CGameSoundEventName` | `string` |

### `CEntityAttributeTable`  <sub>Entity2</sub>

| Property | Was | Now |
|---|---|---|
| `Attributes` | `Dictionary<CUtlStringTokenNoRegistration, Attribute>` | `Dictionary<string, Attribute>` |
| `Names` | `Dictionary<CUtlStringTokenNoRegistration, string>` | `Dictionary<string, string>` |

### `RnCompound`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Spheres` | `RnSphere_t[]` | `RnSphere[]` |

### `RnSphereDesc`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Sphere` | `RnSphere_t` | `RnSphere` |

### `CAIExpresser`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ConceptCooldowns` | `CUtlDict__GameTime_t__` | `Dictionary<string, GameTime>` |
| `RuleCooldowns` | `CUtlDict__GameTime_t__` | `Dictionary<string, GameTime>` |

### `CAmbientGeneric`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Sound` | `CGameSoundEventName` | `string` |

### `CBaseButton`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SLockedSound` | `CGameSoundEventName` | `string` |
| `SUnlockedSound` | `CGameSoundEventName` | `string` |
| `SUseSound` | `CGameSoundEventName` | `string` |

### `CBaseDoor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `NoiseArrived` | `CGameSoundEventName` | `string` |
| `NoiseArrivedClosed` | `CGameSoundEventName` | `string` |
| `NoiseMoving` | `CGameSoundEventName` | `string` |
| `NoiseMovingClosed` | `CGameSoundEventName` | `string` |

### `CBasePlatTrain`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `NoiseArrived` | `CGameSoundEventName` | `string` |
| `NoiseMoving` | `CGameSoundEventName` | `string` |

### `CBasePropDoor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SoundClose` | `CGameSoundEventName` | `string` |
| `SoundJiggle` | `CGameSoundEventName` | `string` |
| `SoundLatch` | `CGameSoundEventName` | `string` |
| `SoundLock` | `CGameSoundEventName` | `string` |
| `SoundLockedAnim` | `CGameSoundEventName` | `string` |
| `SoundMoving` | `CGameSoundEventName` | `string` |
| `SoundOpen` | `CGameSoundEventName` | `string` |
| `SoundPound` | `CGameSoundEventName` | `string` |
| `SoundUnlock` | `CGameSoundEventName` | `string` |

### `CEnvSoundscape`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SoundEventName` | `CGameSoundEventName` | `string` |

### `CFuncMoveLinear`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SoundStart` | `CGameSoundEventName` | `string` |
| `SoundStop` | `CGameSoundEventName` | `string` |

### `CFuncMover`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ArriveAtDestinationSound` | `CGameSoundEventName` | `string` |
| `LoopForwardSound` | `CGameSoundEventName` | `string` |
| `LoopReverseSound` | `CGameSoundEventName` | `string` |
| `StartForwardSound` | `CGameSoundEventName` | `string` |
| `StartReverseSound` | `CGameSoundEventName` | `string` |
| `StopForwardSound` | `CGameSoundEventName` | `string` |
| `StopReverseSound` | `CGameSoundEventName` | `string` |

### `CFuncRotating`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `NoiseRunning` | `CGameSoundEventName` | `string` |

### `CFuncRotator`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LoopSound` | `CGameSoundEventName` | `string` |
| `StartSound` | `CGameSoundEventName` | `string` |
| `StopSound` | `CGameSoundEventName` | `string` |

### `CFuncTrackTrain`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathTarget` | `CGameSoundEventName` | `string` |
| `SoundMove` | `CGameSoundEventName` | `string` |
| `SoundMovePing` | `CGameSoundEventName` | `string` |
| `SoundStart` | `CGameSoundEventName` | `string` |
| `SoundStop` | `CGameSoundEventName` | `string` |

### `CMessage`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SNoise` | `CGameSoundEventName` | `string` |

### `CPhysConstraint`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `BreakSound` | `CGameSoundEventName` | `string` |

### `CPhysicsBodyGameMarkupData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PhysicsBodyMarkupByBoneName` | `CUtlDict__CPhysicsBodyGameMarkup__` | `Dictionary<string, CPhysicsBodyGameMarkup>` |

### `LockSound`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SLockedSound` | `CGameSoundEventName` | `string` |
| `SUnlockedSound` | `CGameSoundEventName` | `string` |
