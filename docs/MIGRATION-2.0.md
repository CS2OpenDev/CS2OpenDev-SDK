# Migrating to CS2OpenDev.Sdk 2.0

**Status: draft, not released.** The numbers below are measured against Docs
`3053793`, and **the ones involving `CS2OpenSchema.GlobalTypes` are provisional** —
see [What is still moving](#what-is-still-moving). Regenerate the tables before
release with:

```
python3 scripts/namespace-diff.py OLD_SDK_DIR NEW_SDK_DIR --markdown
```

## Why 2.0

Upstream replaced the schema artifact. `cs2_schema.json` moved from
`schema_format_version` 1.1 to 2.0, which is not a reformat — it is a different
projection of the runtime, published by
[CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker)
rather than the old DumpSource2 pipeline. The SDK now reads 2.0 only.

The visible consequences:

| | 1.x | 2.0 |
|---|---|---|
| Public types | 4,002 | 4,989 |
| Types that moved namespace | — | 703 of 3,962 carried over |
| New types | — | 1,027 |
| Removed types | — | 40 |
| Abstract classes | 0 | 142 |

Two things drive the namespace moves, and they are worth separating because
only one of them is permanent.

**1. Real reattribution (187 types, permanent).** The namespace key changed from
the schema's `module` to its `projectName`, and upstream attributes types
differently from the old pipeline. `CCSPlayerController` and friends did not
move, but 119 debug/snapshot types moved from `Client` to `Server`, and 40
engine event types from `Client` to `Engine2`. These reflect where the types
actually live and will not change back.

**2. Enum attribution not yet published (516 types, temporary).** Schema 2.0
does not yet carry `projectName` on enum records, so they fall back to the
binary that registered them — `!GlobalTypes` for most — and land together in
`CS2OpenSchema.GlobalTypes`. Fixed upstream in
[SchemaTracker#1](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/1)
but not yet in a published artifact.

## Recovering abstract classes

142 classes are now emitted `public abstract partial class`, from the schema's
`SCHEMA_CF1_IS_ABSTRACT` flag bit. If you were instantiating one of these
directly it will no longer compile — that was always wrong against the native
type, and the old pipeline simply did not expose the flag.

## Type renames

One class gained a member that collides with its own name. C# forbids a member
matching its enclosing type, so `TagStatus.m_TagStatus` projects as
`TagStatus.TagStatusValue`. `[NativeName("m_TagStatus")]` still carries the
native name.

## Namespace moves — permanent

These 187 are settled and safe to act on now.

| From | To | Types | Names |
|---|---|---|---|
| `CS2OpenSchema.Client` | `CS2OpenSchema.Server` | 119 | `AIBaseNPCAnimGraphDebugSnapshotData`, `AIBaseNPCDebugSnapshotData`, `AIDefaultNPCDebugSnapshotData`, `AIDefaultNPCDebugSnapshotDataTPathQuery`, `AINavigatorDebugSnapshotData`, `AINavigatorDebugSnapshotDataTWaypoint`, … (+113) |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Engine2` | 40 | `EngineLoopState`, `EventAdvanceTick`, `EventAppShutdown`, `EventClientAdvanceNonRenderedFrame`, `EventClientAdvanceTick`, `EventClientFrameSimulate`, … (+34) |
| `CS2OpenSchema.Server` | `CS2OpenSchema.Serverdll` | 8 | `CFuncMoverFollowConstraint`, `CFuncMoverFollowEntityDirection`, `CFuncMoverMove`, `CFuncMoverOrientationUpdate`, `CFuncMoverTransitionToPathNodeAction`, `CFuncRotatorRotate`, … (+2) |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Common` | 6 | `CBuoyancyHelper`, `CCollisionProperty`, `CTimeline`, `SequenceHistory`, `ShardModelDesc`, `ViewAngleServerChange` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Vphysics2` | 5 | `ConstraintAxislimit`, `ConstraintBreakableparams`, `ConstraintHingeparams`, `IPhysicsBodyList`, `IPhysicsMotionController` |
| `CS2OpenSchema.PulseRuntimeLib` | `CS2OpenSchema.Animationsystemdll` | 3 | `PulseBestOutflowRules`, `PulseCursorCancelPriority`, `PulseMethodCallMode` |
| `CS2OpenSchema.PulseSystem` | `CS2OpenSchema.PulseSystemdll` | 2 | `PulseTestEnumColor`, `PulseTestEnumShape` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Clientdll` | 1 | `CBaseCombatCharacterWaterWakeMode` |
| `CS2OpenSchema.Server` | `CS2OpenSchema.Common` | 1 | `CBaseModelEntityAPI` |
| `CS2OpenSchema.Server` | `CS2OpenSchema.Physicslib` | 1 | `CGenericShapeProxy` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Physicslib` | 1 | `PhysicsParticleId` |

## What is still moving

The remaining 516 moves are all *into* `CS2OpenSchema.GlobalTypes` and are an
artifact of the unpublished enum fix, not a decision. Do not migrate `using`
lines against them — when the fixed artifact lands they will resolve back to
per-project namespaces (`Client`, `Particles`, `Animgraphlib`, …), and this
document is regenerated before release.

The release is deliberately held until then, so that consumers take one
namespace break rather than two.

## Unchanged

- `CS2OpenDev.Sdk.GameEvents` — the 13 game-event field types and the
  `[GameEventFieldType]` values are identical between 1.1 and 2.0. Event count
  moved 288 → 289.
- `CS2OpenDev.Protos` — on its own version clock, unaffected by the schema
  format, still 1.0.x.
- `[NativeName]`, `[NativeOffset]`, `[NativeSize]`, `[NativeMetadata]` and the
  `SchemaNames` reverse-lookup table all keep their shape.
