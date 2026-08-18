# Migrating to CS2OpenDev.Sdk 2.0

Measured against Docs `48bb6ee` / SchemaTracker `dfa5b30`, CS2 build `24537688`,
schema dated 2026-08-03. Every number here is generated, not estimated:

```
python3 scripts/namespace-diff.py OLD_SDK_DIR NEW_SDK_DIR --markdown
```

## Why 2.0

Upstream replaced the schema artifact. `cs2_schema.json` moved from
`schema_format_version` 1.1 to 2.0. That is not a reformat; it is a different
projection of the runtime, published by
[CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker)
rather than the old DumpSource2 pipeline. The SDK reads 2.0 only.

| | 1.x | 2.0 |
|---|---|---|
| Public types | 4,002 | 4,991 |
| Types that moved namespace | — | 297 of 3,962 carried over |
| New types | — | 1,029 |
| Removed types | — | 40 |
| Abstract classes | 0 | 142 |
| Namespaces | 49 | 50 |

Three changes account for essentially all of the work:
namespace re-attribution, 40 removed types, and 142 classes that became
`abstract`. Removed types are the only one that cannot be fixed by editing a
`using` line.

## Namespace moves

The namespace key changed from the schema's `module` — the binary a type was
registered in — to its `projectName`, the project that declares it. For types
with global type scope the two disagree, and 2.0 is the more accurate of the
two.

**This is not confined to debug plumbing.** The largest group, `Client` →
`Server` at 243 types, includes gameplay enums that consuming code actually
touches: `AmmoFlags`, `AmmoTypeInfo`, `AnimLoopMode`, `AnimGraphDebugDrawType`.
Upstream reports these as `module: "!GlobalTypes"`, `projectName: "server"`;
under 1.1 the same types reported `module: "client"`. They were attributed to
the wrong binary before, so this direction is settled and will not revert.

| From | To | Types | Names |
|---|---|---|---|
| `CS2OpenSchema.Client` | `CS2OpenSchema.Server` | 243 | `AIBaseNPCAnimGraphDebugSnapshotData`, `AIBaseNPCDebugSnapshotData`, `AIDefaultNPCDebugSnapshotData`, `AIDefaultNPCDebugSnapshotDataTPathQuery`, `AINavigatorDebugSnapshotData`, `AINavigatorDebugSnapshotDataTWaypoint`, … (+237) |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Engine2` | 40 | `EngineLoopState`, `EventAdvanceTick`, `EventAppShutdown`, `EventClientAdvanceNonRenderedFrame`, `EventClientAdvanceTick`, `EventClientFrameSimulate`, … (+34) |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Vphysics2` | 6 | `ConstraintAxislimit`, `ConstraintBreakableparams`, `ConstraintHingeparams`, `IPhysicsBodyList`, `IPhysicsMotionController`, `PhysInterfaceId` |
| `CS2OpenSchema.Server` | `CS2OpenSchema.Physicslib` | 2 | `CGenericShapeProxy`, `PhysGenericShapeType` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Scenesystem` | 2 | `DecalRtEncoding`, `ESceneViewDebugOverlaysListenerDataType` |
| `CS2OpenSchema.Server` | `CS2OpenSchema.Common` | 1 | `CBaseModelEntityAPI` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Common` | 1 | `CBuoyancyHelper` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Physicslib` | 1 | `PhysicsParticleId` |
| `CS2OpenSchema.Client` | `CS2OpenSchema.Soundsystem` | 1 | `Soundlevel` |

`CCSPlayerController`, `CCSPlayerPawn` and the rest of the entity hierarchy did
not move. Most consumers change one or two `using` lines.

The `.dll`-suffixed namespaces that appeared in pre-release builds
(`Serverdll`, `Clientdll`, `Animationsystemdll`, `PulseSystemdll`) are **not**
in the release. They were an artifact of an interim schema that omitted
`projectName` on enum records; the shipped SDK has none.

## Removed types

40 types no longer appear in the schema and are gone from the SDK. Code
referencing any of these stops compiling, so they are listed in full rather
than counted. Most are the `SndSeq*` sequencer family and the scene-request
types; `Attribute_t` and `BeamClipStyle` are the notable singletons.

`AIMotorDebugSnapshotData`, `AIMotorGroundAnimgraphDebugSnapshotData`, `AIMotorGroundAnimgraphDebugSnapshotDataTEvent`, `Attribute_t`, `BeamClipStyle`, `BodySectionAuthority`, `CAnimEventListener`, `CAnimEventListenerBase`, `CAnimEventQueueListener`, `CCompressorGroup`, `CInfoInteraction`, `CPulseAnimFuncs`, `CPulseCellWaitForCursorsWithTagBaseCursorState`, `CSceneCriteria`, `CSceneOpportunity`, `CScenePayloadVData`, `CSceneRequest`, `CTriggerToggleSave`, `CVoiceContainerEnvelope`, `CastSphereSATParams`, `ChickenActivity`, `ENPCBehaviorOverride`, `ESceneRequestState`, `EntityDisolveType`, `ExternalAnimGraph`, `InteractionPassive`, `InteractionPriority`, `PulseCursorExecResult`, `PulseObservableBoolExpression`, `SceneInterestTags`, `SceneOpportunityActor`, `SceneOpportunityHandle`, `SceneRequestHandle`, `SceneRequestTargetMapPair`, `SndSeqMidiStatusType`, `SndSeqPlayerType`, `SndSeqQuantizeType`, `SndSeqRegionType`, `SndSeqSyncType`, `SndSeqTrackPlaybackType`

## Abstract classes

142 classes are now emitted `public abstract partial class`, from the schema's
`SCHEMA_CF1_IS_ABSTRACT` flag bit. Instantiating one of these no longer
compiles. That was always wrong against the native type; the 1.x pipeline
simply did not expose the flag.

## Type renames

One class gained a member colliding with its own name. C# forbids a member
matching its enclosing type, so `TagStatus.m_TagStatus` projects as
`TagStatus.TagStatusValue`. `[NativeName("m_TagStatus")]` still carries the
native name, so `SchemaNames` lookups are unaffected.

## Game events — additive only

`CS2OpenDev.Sdk.GameEvents` ships **2.0.x** alongside the SDK, because it
project-references `CS2OpenDev.Sdk` and cannot be consumed against a 1.x SDK.
Nothing in it breaks. Against 1.0.5 the generated surface changed in exactly
three ways, all additive:

- `gameui_hidden` is new, taking the event count 288 → 289. It carries no
  fields; `GameuiHiddenEventFrom` and the `GameEventRegistry` entry are new.
- `bomb_defused`, `bomb_exploded` and `bomb_planted` each gained a `C4` field
  (`int16`), read via `reader.GetInt16("c4")`. Existing members are untouched.
- The `// Schema revision:` header now reads `24537688 — 2026-08-03T18:18:10Z`.

The 13 game-event field types and every `[GameEventFieldType]` value are
identical between 1.1 and 2.0. No factory signature changed, so existing call
sites compile unmodified.

### If you built against the 1.x event numbers

Four counts published alongside 1.0.5 have moved. They matter only if you hold
your own event table rather than using `GameEventRegistry`:

| | 1.x | 2.0 |
|---|---|---|
| Event records | 288 | **289** |
| Distinct native names | 272 | **273** |
| Names resolving to exactly one record | 257 | **258** |
| `player_death` fields (`mod.gameevents`) | 18 | **22** |

The last one is the one to check. A hand-written `player_death` decoder built
against the 18-field declaration still compiles and still reads the fields it
knows; it silently ignores four it does not.

The duplicate-name situation is exactly as before: **15** native
names carry more than one record, across **31** records, and
`GameEventRegistry` still resolves each to the declaration CS2 actually fires
(`mod` > `game` > `core`), with the others reachable explicitly.

## Versions

All three packages move to major **2**.

| Package | last 1.x | first 2.0 |
|---|---|---|
| `CS2OpenDev.Sdk` | 1.0.5 | **2.0.3** |
| `CS2OpenDev.Sdk.GameEvents` | 1.0.5 | **2.0.3** |
| `CS2OpenDev.Protos` | 1.0.7 | **2.0.x** |

The patch component is Nerdbank.GitVersioning's git height, not a hand-set
number, so it advances on every regen; take the newest `2.0.x`, not `2.0.3`
specifically. Each package has its own `version.json` and its own height, so
the three patch numbers differ and are not meant to match.

> **`CS2OpenDev.Sdk.GameEvents` 2.0.4 is unusable — take 2.0.10 or newer.** Its
> GitHub release asset and its feed copy are different files with different
> `CS2OpenDev.Protos` dependencies. Cause and fix are on the 2.0.4 release page.

**`CS2OpenDev.Protos` 2.0 contains no breaking change.** Its `.proto` content
is identical to 1.0.7; the major moved only so the three packages that ship
together carry one major. It keeps its own patch clock (a schema regen that
leaves the `.proto` files alone still does not bump it), so the three version
numbers are aligned on major but will not match digit for digit. Take the
newest of each.

**These are published to GitHub Packages and attached to each GitHub release,
not to NuGet.org**; the publish credential is not configured. Each release's
notes state which feeds actually received that version.

## Unchanged

- `[NativeName]`, `[NativeOffset]`, `[NativeSize]`, `[NativeMetadata]` and the
  `SchemaNames` reverse-lookup table all keep their shape.
- Every `.proto` message and field in `CS2OpenDev.Protos`.
