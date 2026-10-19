# Migrating to CS2OpenDev.Sdk 0.11

**403 types and 224 members leave the public surface.** One upstream change accounts for most of
it: the `smartprops` schema module is gone from CS2, taking the whole `CS2OpenSchema.Smartprops`
namespace with it. The rest is a long tail across `Server`, `Common`, `Client` and the sound and
pulse namespaces, plus the `SchemaNames` constants for entity classes upstream dropped.

Measured against CS2 build 25966348 (from 25218825), a gap of five weeks of upstream churn.
Regenerate the inventory with:

```
dotnet run --configuration Release --project src/CS2OpenDev.Sdk.Exporter
```

This release documents removals only. It makes no claim that the surviving surface was reviewed
field by field: 241 types and 656 members were also added, and 18 property types changed, and
none of that was asked for or audited here.

## 1. `CS2OpenSchema.Smartprops` is gone entirely

167 types, the complete namespace. Upstream stopped emitting the `smartprops` module, so there is
nothing left to project. The directory `src/CS2OpenDev.Sdk/Smartprops/` went from 167 files to
zero.

Everything under it is affected, including `CSmartPropAPI`, the `CSmartPropAttribute*` family,
the `CSmartPropElement*` family and the enums they carried (`ApplyColorMode`,
`CSmartPropAttributeChoiceSelectionMode`, and the rest).

**There is no replacement.** This is not a rename or a move to another namespace; the schema no
longer describes smartprops at all. Anything reading these types has to drop the code path. If
you need the old projection, pin `0.10.1`, which stays on NuGet.org and keeps working against
demos from build 25218825 and earlier.

## 2. 132 `SchemaNames` constants

`SchemaNames` carries one stub class per engine class so consumers can name a class without
hardcoding a string. When upstream removes an entity class, its constant goes with it.

These are the classes that left: `CBaseMoveBehavior`, `CEnvWindController`, `CEnvWindVolume`,
`CLogicPlayerProxy`, `CReplicationParameters`, `CRotatorTarget` and 126 others, the full list
being the `SchemaNames` entries absent from the regenerated tree.

A stub removal is still a breaking change and still needs a version bump that says so. This
package is pre-1.0, so the minor is where that signal lives: 0.10 to 0.11, not a patch off git
height. If you referenced one of these constants, the class it named does not exist in this
build, so the reference had no live meaning left either way.

## 3. `CCSPlayerCamera` and the client preview API types

14 `Client` types leave, and two groups are worth naming.

`Client.CCSPlayerCamera` is gone. 0.10 documented this same class gaining an interposed
`CCSCustomPlayerCamera` base and losing two fields; this build removes the class outright. If you
followed `docs/MIGRATION-0.10.md` and moved to `CameraMode` on the new base, that base survives
and your code is unaffected. If you still referenced `CCSPlayerCamera` itself, there is nothing
to move to.

The `*API` preview types also leave: `CCSGOMapPreviewCameraPathAPI`,
`CCSGOMapPreviewCameraPathNodeAPI`, `CCSGOPreviewModelAPI`, `CCSGOPreviewPlayerAPI`,
`CCSGOTeamPreviewCameraAPI`, `CCsmFovOverrideAPI` and `CGrenadeTracerAPI`. These described the
main-menu preview scene, which is not demo-visible state, so a demo-reading consumer is unlikely
to have touched them.

## 4. The `Server`, `Common` and sound long tail

40 `Server` types, 28 `Common` types, 6 `PulseRuntimeLib`, 5 `PulseSystem`, 4
`SoundsystemLowlevel`, 3 `Particles`, 2 `Soundsystem`, 1 `Entity2` and 1 `Resourcesystem`.

99 of the 224 removed members are on surviving `Server` types. The largest clusters:

| Type | Members removed | Shape |
|---|---|---|
| `CEnvCombinedLightProbeVolume` | `EntityHLightProbeTextureSH2R` / `SH2G` / `SH2B` | second-order SH lightprobe textures |
| `CEnvLightProbeVolume` | the same three | same |
| `CCS2ChickenGraphController` | `IdleVariation`, `PanicVariation`, `RunVariation`, `SquatVariation`, `WaitingForCompletedEvent` | animation graph variation slots |
| `CCSWeaponBaseVData` | `CrossHairDeltaDistance`, `CrossHairMinDistance` | crosshair tuning |
| `CDecalInstance` | `Transform` | replaced by a different decal model |

`EGCItemMsg` is the single largest cut in the release and is not in the table above because it
is an enum, not a field-carrying type: it loses **53 members**, 53 of the 99 `Server` removals.
`EMsgGCApplyStrangePart`, `EMsgGCDeliverGift`, `EMsgGCLookupAccount` and 50 others are gone.
`ECsgoGCMsg` loses 4 more, including `EMsgGCCStrike15_v2_GC2ClientRefuseSecureMode`, and
`CFuncRotator`, `ResponseFollowup` and `CFuncMover` lose 4, 4 and 3. Anything naming one of
those constants gets a compile error on upgrade.

**No replacement is offered for any of these, and none was traced.** The lightprobe and animation
members are renderer and authoring state rather than demo-visible state. `CCSWeaponBaseVData`'s
two crosshair fields are the ones most likely to matter to a consumer reading weapon data, and
the regenerated `CCSWeaponBaseVData` does not carry an obvious successor for them.

The remaining member removals are spread thin: `SchemaNames` 76, `Client` 12,
`SoundsystemLowlevel` 11, `Animlib` 8, `SounddocLib` 7, `Worldrenderer` 4, `Soundsystem` 3,
`Particles` 2, `Animdoclib` 1, `PulseRuntimeLib` 1
(`CPulseCellInflowMethod.ReturnType`). With the 99 on `Server` that is all 224.

## 5. `ModelMeshBufferUsage` changes its underlying type

`CS2OpenSchema.Modellib.ModelMeshBufferUsage` was `enum : byte` and is now `enum : ushort`. The
surface gate reports this as a declaration change rather than a removal, so it does not block,
but it is a breaking change for anything that casts the enum to `byte` or round-trips it through
one.

Six other types changed declaration. One of them loses a member without any gate noticing:

**`SoundsystemLowlevel.CVMixVsndInput` no longer derives from `CVMixInputBase`.** It declared no
members of its own before and declares one now, but `Name` came from that base, and the base
still exists. So `sdk_surface.py` sees no declared-member removal and stays silent, while
`new CVMixVsndInput().Name` stops compiling. The type also went from 32 bytes to 24. This is the
only reparent in the release that loses a reachable member; the others move members between
bases that both carry them.

The rest: `Animdoclib.CNmGraphDocTargetSelectorNode` reparents from `CNmGraphDocFlowNode` to
`CNmGraphDocVariationDataNode`, `Client.C_SoundEventPathCornerEntity` from `C_SoundEventEntity`
to `C_SoundEventMultiPointEntity`, and three others.

## 6. Members that only look removed

Six members stopped being declared on their own type and are inherited from a base instead:
`Server.CInstancedSceneEntity.RemoveOnCompletion`, and `CountMax`, `DistMaxSqr`, `DistanceMax`,
`DotProductMax` and `Playing` on `Server.CSoundEventPathCornerEntity`. These compile unchanged
and need nothing from you. They are listed because the surface gate reports them and a reader
comparing type declarations would otherwise count them as losses.

## Everything else in this build

Additions, which never block: 241 types and 656 members.

Reported and not blocked, and not reviewed as part of this release:

- 18 property type changes across 11 types, in 9 shapes. The largest are 7 `int` ->
  `CVMixDataOffset` and 4 `float` -> `CParticleCollectionFloatInput`, plus 1 `ushort` -> `uint`.
- 225 members changed in some way other than a property's type, and every one of them is an enum
  value renumbering: no accessor and no member-kind change in the release. They fall across 11
  enums, led by `PulseInstructionCode` (122), `Server.GameAnimEventIndex` (34),
  `VMixGraphCommandId` (32) and `ParticleFloatType` (23).

  Two of them are flag bits, which is the dangerous shape: `EPulseGraphExecutionHistory`
  moves `RequirementPass` from 8 to 16 and `RequirementFail` from 16 to 32. A value of 8
  persisted or compared under 0.10.1 now reads as `CursorCreateChild`. Enum constants are
  inlined at your compile time, so a renumber is a wrong answer at runtime rather than a
  compile error, and `scripts/sdk_surface.py` says as much about why it reports them. This
  release note is the only place they are reported.

The proto surface loses no top-level type and no field number against `CS2OpenDev.Protos` 0.9.6
(352 top-level types, 0 added), but it is **not** unchanged: field 11 of
`CEconItemPreviewDataBlock` is renamed and widened, which is a break that `proto_surface.py`'s
model cannot see. `CS2OpenDev.Protos` ships 0.10 for it. Read `docs/MIGRATION-0.10-protos.md`
before upgrading both packages together.

That is the complete accounting: 403 types and 224 members removed, 241 types and 656 members
added, 6 members hoisted, 7 type declarations changed, 18 property types changed, 225 members
otherwise changed.
