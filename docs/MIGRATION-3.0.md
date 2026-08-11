# Migrating to CS2OpenDev.Sdk 3.0

3.0 renames generated identifiers to idiomatic .NET casing. **That is the only
change** — no type moved namespace, none was added or removed, no signature or
projected type changed, and the schema pin is the same as 2.0.4's. If your code
compiles after the renames, it behaves exactly as it did.

Regenerate the tables below with:

```
python3 scripts/rename-diff.py OLD_SDK_DIR NEW_SDK_DIR --markdown
```

> ## Correction — this document listed 574 of 1,108 renames
>
> **The tables below are missing 534 renamed enum members.** They are listed in
> full in [`MIGRATION-3.0-enum-members.md`](MIGRATION-3.0-enum-members.md).
>
> `scripts/rename-diff.py`, which generated these tables, matched a member only
> when the line began with `public `. Enum members are bare `Name = value` inside
> the enum body, so it never matched one — it reported 574 renames of 11,539
> members it could see, when the real figures were 1,108 of 15,920. Everything it
> did report is correct; it was blind, not wrong. Examples of what it missed:
> `AeClPlaysoundAttachment` → `AeClPlaySoundAttachment`, `AddFloatGametime` →
> `AddFloatGameTime`.
>
> The script is fixed as of 4.0, so re-running the command above against a 2.x
> and a 3.x tree now produces the complete list.
>
> This was found while fixing a related blind spot in the generator's own
> `CS2_GEN_006` diagnostic (see `MIGRATION-4.0.md`), which is to say: it was found
> because a downstream consumer reported six identifiers our tooling had told us
> did not exist.

## Why

Native CS2 names are run-together lowercase — `userid`, `thrusmoke`,
`attackerinair` — and 2.x folded them by capitalising the first letter only.
That produced `Userid`, `Thrusmoke`, `Attackerinair`: valid C#, but not names a
.NET developer would write, and `player_death.Userid` in particular reads as a
single word rather than "user id".

The generator now splits those runs into words against a curated vocabulary of
CS2 domain terms, and folds a trailing `ID` run to `Id`. Upstream spells that
suffix both ways (`m_nSubclassID` but `accountid`), so 2.x shipped `SubclassID`
next to `AccountId` on the same object; both are now `...Id`, which is the
spelling the BCL uses (`Process.Id`, `Activity.Id`).

**Nothing is guessed.** A run is rewritten only when it segments completely
into known words; anything else keeps its 2.x spelling exactly. The failure
mode is a missed improvement, never a wrong name.

**Nothing is left half-done either.** The generator reports every run-together
name it could not segment, and that report is now empty: each one is either
split, or explicitly declared a single word. `Assister`, `Hostage`, `Database`,
`Breakable`, `Flashbang`, `Deathmatch` and `Preset` are words, and stay whole.

**And these names are now pinned.** Every one of them is recorded in
`names.lock.json`, and the generator returns locked names verbatim rather than
re-deriving them. The word list that produced 3.0 can be extended for future CS2
fields without any risk of it quietly re-splitting a name you are already
compiling against — which is a real hazard, not a hypothetical: extending it is
what turned `Database` into `DataBase` twice during development. Changing a
shipped name now requires an explicit re-baseline and a new major.

## What did not change

- **`[NativeName]` still carries the native name**, unchanged, on every member.
  Reverse lookup through `SchemaNames` is unaffected, and so is anything that
  matches on native names.
- **Byte offsets, sizes and metadata** — `[NativeOffset]`, `[NativeSize]`,
  `[NativeMetadata]` are attribute values, never derived from identifiers.
  Interop is untouched.
- **Game-event decoding** reads by native key at runtime
  (`reader.GetInt32("userid")`), so the wire path does not depend on the
  property name.
- **Namespaces**, the type set, and every projected C# type.
- **`CS2OpenDev.Protos`** — no generated protobuf identifier changed. Its major
  moves to 3 only to stay in step with the other two, exactly as it did at 2.0.

## Versions

| Package | last 2.x | first 3.0 |
|---|---|---|
| `CS2OpenDev.Sdk` | 2.0.4 | **3.0.x** |
| `CS2OpenDev.Sdk.GameEvents` | 2.0.11 | **3.0.x** |
| `CS2OpenDev.Protos` | 2.0.1 | **3.0.x** |

The patch is Nerdbank.GitVersioning's git height and each package has its own,
so the three numbers agree on major and not below it — take the newest of each.

**`CS2OpenDev.Protos` 3.0 contains no change at all.** No generated protobuf
identifier moved; its major tracks the other two so the packages that ship
together read as one product, exactly as at 2.0.

These publish to **GitHub Packages** and the GitHub release page, not
NuGet.org — the publish credential is not configured. Each release's notes state
which feeds actually received that version.

## How to migrate

The rename is mechanical and the compiler finds every site. Build, and for each
error take the new name from the tables below. There is no behavioural change to
review — if it compiles, you are done.

The common ones, by a wide margin:

| Was | Now |
|---|---|
| `Userid` | `UserId` |
| `Attackerid`, `Entityid`, `Steamid`, `Playerid` | `AttackerId`, `EntityId`, `SteamId`, `PlayerId` |
| `Entindex` | `EntIndex` |
| `Hitgroup` | `HitGroup` |
| `Classname` | `ClassName` |
| anything ending `...ID` | `...Id` |

**574** members renamed, of 11539 matched by native name.
**207** type names replaced by **207** new ones.

## Renamed members

| Native name | Was | Now |
|---|---|---|
| `PlayerID` | `PlayerID` | `PlayerId` |
| `accountid` | `Accountid` | `AccountId` |
| `addonname` | `Addonname` | `AddonName` |
| `animgraph` | `Animgraph` | `AnimGraph` |
| `assistedflash` | `Assistedflash` | `AssistedFlash` |
| `attackerblind` | `Attackerblind` | `AttackerBlind` |
| `attackerid` | `Attackerid` | `AttackerId` |
| `attackerinair` | `Attackerinair` | `AttackerInAir` |
| `bRenderFullyUnlitAsFullbright` | `RenderFullyUnlitAsFullbright` | `RenderFullyUnlitAsFullBright` |
| `bSelectionOutlineDepth` | `SelectionOutlineDepth` | `SelectionOutLineDepth` |
| `bShowSelectionOutline` | `ShowSelectionOutline` | `ShowSelectionOutLine` |
| `bodygroup` | `Bodygroup` | `BodyGroup` |
| `canbuy` | `Canbuy` | `CanBuy` |
| `canzoom` | `Canzoom` | `CanZoom` |
| `classname` | `Classname` | `ClassName` |
| `cvarname` | `Cvarname` | `CVarName` |
| `cvarvalue` | `Cvarvalue` | `CVarValue` |
| `defaultSubgraph` | `DefaultSubgraph` | `DefaultSubGraph` |
| `defindex` | `Defindex` | `DefIndex` |
| `dmgstate` | `Dmgstate` | `DmgState` |
| `edictindex` | `Edictindex` | `EdictIndex` |
| `endframe` | `Endframe` | `EndFrame` |
| `endtime` | `Endtime` | `EndTime` |
| `entindex` | `Entindex` | `EntIndex` |
| `entindex_attacker` | `EntindexAttacker` | `EntIndexAttacker` |
| `entindex_inflictor` | `EntindexInflictor` | `EntIndexInflictor` |
| `entindex_killed` | `EntindexKilled` | `EntIndexKilled` |
| `entityid` | `Entityid` | `EntityId` |
| `entityname` | `Entityname` | `EntityName` |
| `fadein` | `Fadein` | `FadeIn` |
| `fadeout` | `Fadeout` | `FadeOut` |
| `followup_entityiodelay` | `FollowupEntityiodelay` | `FollowupEntityIoDelay` |
| `followup_entityioinput` | `FollowupEntityioinput` | `FollowupEntityIoInput` |
| `followup_entityiotarget` | `FollowupEntityiotarget` | `FollowupEntityIoTarget` |
| `forceupload` | `Forceupload` | `ForceUpload` |
| `fraglimit` | `Fraglimit` | `FragLimit` |
| `framestalltime` | `Framestalltime` | `FrameStallTime` |
| `globalname` | `Globalname` | `GlobalName` |
| `hasbomb` | `Hasbomb` | `HasBomb` |
| `haskit` | `Haskit` | `HasKit` |
| `hassilencer` | `Hassilencer` | `HasSilencer` |
| `hastracers` | `Hastracers` | `HasTracers` |
| `hint_activator_userid` | `HintActivatorUserid` | `HintActivatorUserId` |
| `hint_entindex` | `HintEntindex` | `HintEntIndex` |
| `hint_forcecaption` | `HintForcecaption` | `HintForceCaption` |
| `hint_icon_offscreen` | `HintIconOffscreen` | `HintIconOffScreen` |
| `hint_icon_onscreen` | `HintIconOnscreen` | `HintIconOnScreen` |
| `hint_nooffscreen` | `HintNooffscreen` | `HintNoOffScreen` |
| `hint_timeout` | `HintTimeout` | `HintTimeOut` |
| `hintmessage` | `Hintmessage` | `HintMessage` |
| `hitgroup` | `Hitgroup` | `HitGroup` |
| `hostname` | `Hostname` | `HostName` |
| `inrestart` | `Inrestart` | `InRestart` |
| `is_pathcorner` | `IsPathcorner` | `IsPathCorner` |
| `ispainted` | `Ispainted` | `IsPainted` |
| `isplanted` | `Isplanted` | `IsPlanted` |
| `issilenced` | `Issilenced` | `IsSilenced` |
| `itemdef` | `Itemdef` | `ItemDef` |
| `itemid` | `Itemid` | `ItemId` |
| `last_waypoint_pos` | `LastWaypointPos` | `LastWayPointPos` |
| `lerptime` | `Lerptime` | `LerpTime` |
| `lfofrac` | `Lfofrac` | `LfoFrac` |
| `lfomodpitch` | `Lfomodpitch` | `LfoModPitch` |
| `lfomodvol` | `Lfomodvol` | `LfoModVol` |
| `lfomult` | `Lfomult` | `LfoMult` |
| `lforate` | `Lforate` | `LfoRate` |
| `lightfill` | `Lightfill` | `LightFill` |
| `lightsun` | `Lightsun` | `LightSun` |
| `locallightscale` | `Locallightscale` | `LocalLightScale` |
| `m_AnimgraphParameterNameOrientation` | `AnimgraphParameterNameOrientation` | `AnimGraphParameterNameOrientation` |
| `m_AnimgraphParameterNamePosition` | `AnimgraphParameterNamePosition` | `AnimGraphParameterNamePosition` |
| `m_BlackboardReferences` | `BlackboardReferences` | `BlackBoardReferences` |
| `m_BlackboardResource` | `BlackboardResource` | `BlackBoardResource` |
| `m_BlockID` | `BlockID` | `BlockId` |
| `m_BodygroupOnOtherModels` | `BodygroupOnOtherModels` | `BodyGroupOnOtherModels` |
| `m_BtGlobalBlackboard` | `BtGlobalBlackboard` | `BtGlobalBlackBoard` |
| `m_CHitboxComponent` | `CHitboxComponent` | `CHitBoxComponent` |
| `m_CallMethodID` | `CallMethodID` | `CallMethodId` |
| `m_DebuggerBreakpointDisabledImg` | `DebuggerBreakpointDisabledImg` | `DebuggerBreakPointDisabledImg` |
| `m_DebuggerBreakpointImg` | `DebuggerBreakpointImg` | `DebuggerBreakPointImg` |
| `m_DestinationFlowNodeID` | `DestinationFlowNodeID` | `DestinationFlowNodeId` |
| `m_Entity_bCopyDiffuseFromDefaultCubemap` | `EntityBCopyDiffuseFromDefaultCubemap` | `EntityBCopyDiffuseFromDefaultCubeMap` |
| `m_Entity_bCustomCubemapTexture` | `EntityBCustomCubemapTexture` | `EntityBCustomCubeMapTexture` |
| `m_Entity_bMoveable` | `EntityBMoveable` | `EntityBMoveAble` |
| `m_Entity_hCubemapTexture` | `EntityHCubemapTexture` | `EntityHCubeMapTexture` |
| `m_HitboxSetName` | `HitboxSetName` | `HitBoxSetName` |
| `m_IDSets` | `IDSets` | `IdSets` |
| `m_IDValues` | `IDValues` | `IdValues` |
| `m_Inparams` | `Inparams` | `InParams` |
| `m_InparamsWhichCanBeMoved` | `InparamsWhichCanBeMoved` | `InParamsWhichCanBeMoved` |
| `m_MinimapVerticalSectionHeights` | `MinimapVerticalSectionHeights` | `MiniMapVerticalSectionHeights` |
| `m_NodeID` | `NodeID` | `NodeId` |
| `m_OnTimeout` | `OnTimeout` | `OnTimeOut` |
| `m_OutflowID` | `OutflowID` | `OutflowId` |
| `m_OutlineColor` | `OutlineColor` | `OutLineColor` |
| `m_Outparams` | `Outparams` | `OutParams` |
| `m_PanelID` | `PanelID` | `PanelId` |
| `m_SourceFilename` | `SourceFilename` | `SourceFileName` |
| `m_SubassetTypes` | `SubassetTypes` | `SubAssetTypes` |
| `m_SuppressSubstrings` | `SuppressSubstrings` | `SuppressSubStrings` |
| `m_Tracepoints` | `Tracepoints` | `TracePoints` |
| `m_additiveBaseFilename` | `AdditiveBaseFilename` | `AdditiveBaseFileName` |
| `m_alignmentBoneID` | `AlignmentBoneID` | `AlignmentBoneId` |
| `m_animgraphCharacterModeString` | `AnimgraphCharacterModeString` | `AnimGraphCharacterModeString` |
| `m_areaEnteredTimestamp` | `AreaEnteredTimestamp` | `AreaEnteredTimeStamp` |
| `m_arrForceSubtickMoveWhen` | `ArrForceSubtickMoveWhen` | `ArrForceSubTickMoveWhen` |
| `m_attachToBoneID` | `AttachToBoneID` | `AttachToBoneId` |
| `m_attackedTimestamp` | `AttackedTimestamp` | `AttackedTimeStamp` |
| `m_avoidTimestamp` | `AvoidTimestamp` | `AvoidTimeStamp` |
| `m_bApplyLayerMatchIDToModel` | `ApplyLayerMatchIDToModel` | `ApplyLayerMatchIdToModel` |
| `m_bAutogenerated` | `Autogenerated` | `AutoGenerated` |
| `m_bAutoplay` | `Autoplay` | `AutoPlay` |
| `m_bBasechecked` | `Basechecked` | `BaseChecked` |
| `m_bCanHighlightSubassets` | `CanHighlightSubassets` | `CanHighlightSubAssets` |
| `m_bCannotBeAMultiParentChildCompile` | `CannotBeAMultiParentChildCompile` | `CanNotBeAMultiParentChildCompile` |
| `m_bCannotBeDefused` | `CannotBeDefused` | `CanNotBeDefused` |
| `m_bCannotBeKicked` | `CannotBeKicked` | `CanNotBeKicked` |
| `m_bCannotBeRefracted` | `CannotBeRefracted` | `CanNotBeRefracted` |
| `m_bCannotBeShown` | `CannotBeShown` | `CanNotBeShown` |
| `m_bCannotShootUnderwater` | `CannotShootUnderwater` | `CanNotShootUnderwater` |
| `m_bClientside` | `Clientside` | `ClientSide` |
| `m_bConstrainBetweenEndpoints` | `ConstrainBetweenEndpoints` | `ConstrainBetweenEndPoints` |
| `m_bCullOutside` | `CullOutside` | `CullOutSide` |
| `m_bDebugCommandline` | `DebugCommandline` | `DebugCommandLine` |
| `m_bDestructiblePartInitialStateDestructed0_GenerateBreakpieces` | `DestructiblePartInitialStateDestructed0GenerateBreakpieces` | `DestructiblePartInitialStateDestructed0GenerateBreakPieces` |
| `m_bDestructiblePartInitialStateDestructed1_GenerateBreakpieces` | `DestructiblePartInitialStateDestructed1GenerateBreakpieces` | `DestructiblePartInitialStateDestructed1GenerateBreakPieces` |
| `m_bDestructiblePartInitialStateDestructed2_GenerateBreakpieces` | `DestructiblePartInitialStateDestructed2GenerateBreakpieces` | `DestructiblePartInitialStateDestructed2GenerateBreakPieces` |
| `m_bDestructiblePartInitialStateDestructed3_GenerateBreakpieces` | `DestructiblePartInitialStateDestructed3GenerateBreakpieces` | `DestructiblePartInitialStateDestructed3GenerateBreakPieces` |
| `m_bDestructiblePartInitialStateDestructed4_GenerateBreakpieces` | `DestructiblePartInitialStateDestructed4GenerateBreakpieces` | `DestructiblePartInitialStateDestructed4GenerateBreakPieces` |
| `m_bDisableDepthPrepass` | `DisableDepthPrepass` | `DisableDepthPrePass` |
| `m_bDoDecalLightmapping` | `DoDecalLightmapping` | `DoDecalLightMapping` |
| `m_bEnableEndcap` | `EnableEndcap` | `EnableEndCap` |
| `m_bEnableIndirect` | `EnableIndirect` | `EnableInDirect` |
| `m_bEnableLoopcap` | `EnableLoopcap` | `EnableLoopCap` |
| `m_bExplicitTimeStepping` | `ExplicitTimeStepping` | `ExplicitTimeStepPing` |
| `m_bFullbright` | `Fullbright` | `FullBright` |
| `m_bHasLightmaps` | `HasLightmaps` | `HasLightMaps` |
| `m_bHasTonemapParams` | `HasTonemapParams` | `HasToneMapParams` |
| `m_bIndirectUseLPVs` | `IndirectUseLPVs` | `InDirectUseLPVs` |
| `m_bInitialBoneSetup` | `InitialBoneSetup` | `InitialBoneSetUp` |
| `m_bIsBoneID` | `IsBoneID` | `IsBoneId` |
| `m_bIsPublicBlackboardVariable` | `IsPublicBlackboardVariable` | `IsPublicBlackBoardVariable` |
| `m_bIsSubgraph` | `IsSubgraph` | `IsSubGraph` |
| `m_bIsSubgraphNode` | `IsSubgraphNode` | `IsSubGraphNode` |
| `m_bKeepAnimgraphLockedPost` | `KeepAnimgraphLockedPost` | `KeepAnimGraphLockedPost` |
| `m_bKillonContact` | `KillonContact` | `KillOnContact` |
| `m_bLegacyRealtime` | `LegacyRealtime` | `LegacyRealTime` |
| `m_bLegacyWorldspace` | `LegacyWorldspace` | `LegacyWorldSpace` |
| `m_bLifespanDecay` | `LifespanDecay` | `LifeSpanDecay` |
| `m_bLoadingRoundBackupData` | `LoadingRoundBackupData` | `LoadingRoundBackUpData` |
| `m_bMaintainHitbox` | `MaintainHitbox` | `MaintainHitBox` |
| `m_bMatchOnlySpecificMarkerID` | `MatchOnlySpecificMarkerID` | `MatchOnlySpecificMarkerId` |
| `m_bMultisampleEnable` | `MultisampleEnable` | `MultiSampleEnable` |
| `m_bNoOffscreen` | `NoOffscreen` | `NoOffScreen` |
| `m_bOutline` | `Outline` | `OutLine` |
| `m_bOutside` | `Outside` | `OutSide` |
| `m_bOverrideIndirectLightStrength` | `OverrideIndirectLightStrength` | `OverrideInDirectLightStrength` |
| `m_bPlayEndcapOnStop` | `PlayEndcapOnStop` | `PlayEndCapOnStop` |
| `m_bPlayerWindblock` | `PlayerWindblock` | `PlayerWindBlock` |
| `m_bPrecomputedFieldsValid` | `PrecomputedFieldsValid` | `PreComputedFieldsValid` |
| `m_bPrepopulateOnSpawn` | `PrepopulateOnSpawn` | `PrePopulateOnSpawn` |
| `m_bPreventLoopback` | `PreventLoopback` | `PreventLoopBack` |
| `m_bQueueSetupPathMover` | `QueueSetupPathMover` | `QueueSetUpPathMover` |
| `m_bQuietTracepoints` | `QuietTracepoints` | `QuietTracePoints` |
| `m_bRealtime` | `Realtime` | `RealTime` |
| `m_bRemoveable` | `Removeable` | `RemoveAble` |
| `m_bRenderBackface` | `RenderBackface` | `RenderBackFace` |
| `m_bRenderToCubemaps` | `RenderToCubemaps` | `RenderToCubeMaps` |
| `m_bRestoreCustomMaterialAfterPrecache` | `RestoreCustomMaterialAfterPrecache` | `RestoreCustomMaterialAfterPreCache` |
| `m_bRoundEndShowTimerDefend` | `RoundEndShowTimerDefend` | `RoundEndShowTimerDefEnd` |
| `m_bSHLightmaps` | `SHLightmaps` | `SHLightMaps` |
| `m_bSetRopeSegmentID` | `SetRopeSegmentID` | `SetRopeSegmentId` |
| `m_bSetToEndpoint` | `SetToEndpoint` | `SetToEndPoint` |
| `m_bShouldAutobuyDMWeapons` | `ShouldAutobuyDMWeapons` | `ShouldAutoBuyDMWeapons` |
| `m_bShouldDrawHitboxes` | `ShouldDrawHitboxes` | `ShouldDrawHitBoxes` |
| `m_bShouldHitboxesFallbackToCollisionHulls` | `ShouldHitboxesFallbackToCollisionHulls` | `ShouldHitBoxesFallbackToCollisionHulls` |
| `m_bShouldHitboxesFallbackToRenderBounds` | `ShouldHitboxesFallbackToRenderBounds` | `ShouldHitBoxesFallbackToRenderBounds` |
| `m_bShouldHitboxesFallbackToSnapshot` | `ShouldHitboxesFallbackToSnapshot` | `ShouldHitBoxesFallbackToSnapshot` |
| `m_bShouldWraparound` | `ShouldWraparound` | `ShouldWrapAround` |
| `m_bSortBySegmentID` | `SortBySegmentID` | `SortBySegmentId` |
| `m_bStopUpdatingWaypointPos` | `StopUpdatingWaypointPos` | `StopUpdatingWayPointPos` |
| `m_bTimeoutFired` | `TimeoutFired` | `TimeOutFired` |
| `m_bUnderCrosshair` | `UnderCrosshair` | `UnderCrossHair` |
| `m_bUseClosestPointOnHitbox` | `UseClosestPointOnHitbox` | `UseClosestPointOnHitBox` |
| `m_bUseHitboxes` | `UseHitboxes` | `UseHitBoxes` |
| `m_bUseHitboxesForRenderBox` | `UseHitboxesForRenderBox` | `UseHitBoxesForRenderBox` |
| `m_blendParamID` | `BlendParamID` | `BlendParamId` |
| `m_bombsiteCenterA` | `BombsiteCenterA` | `BombSiteCenterA` |
| `m_bombsiteCenterB` | `BombsiteCenterB` | `BombSiteCenterB` |
| `m_boneID` | `BoneID` | `BoneId` |
| `m_boneMaskID` | `BoneMaskID` | `BoneMaskId` |
| `m_boneSetupMask` | `BoneSetupMask` | `BoneSetUpMask` |
| `m_boolParamID` | `BoolParamID` | `BoolParamId` |
| `m_cloneSourceStateID` | `CloneSourceStateID` | `CloneSourceStateId` |
| `m_comparisonParamID` | `ComparisonParamID` | `ComparisonParamId` |
| `m_componentID` | `ComponentID` | `ComponentId` |
| `m_currentEnemyAcquireTimestamp` | `CurrentEnemyAcquireTimestamp` | `CurrentEnemyAcquireTimeStamp` |
| `m_cursorIDToEventID` | `CursorIDToEventID` | `CursorIdToEventId` |
| `m_debugEffectorBoneID` | `DebugEffectorBoneID` | `DebugEffectorBoneId` |
| `m_defaultID` | `DefaultID` | `DefaultId` |
| `m_delayTargetIDTimer` | `DelayTargetIDTimer` | `DelayTargetIdTimer` |
| `m_desiredMoveHeadingParamID` | `DesiredMoveHeadingParamID` | `DesiredMoveHeadingParamId` |
| `m_dictionaryIDSetIDs` | `DictionaryIDSetIDs` | `DictionaryIdSetIDs` |
| `m_disableTagID` | `DisableTagID` | `DisableTagId` |
| `m_effectorBoneID` | `EffectorBoneID` | `EffectorBoneId` |
| `m_embeddedKeyvalues` | `EmbeddedKeyvalues` | `EmbeddedKeyValues` |
| `m_endEffectorBoneID` | `EndEffectorBoneID` | `EndEffectorBoneId` |
| `m_endStateID` | `EndStateID` | `EndStateId` |
| `m_endcapVsnd` | `EndcapVsnd` | `EndCapVsnd` |
| `m_enemyDeathTimestamp` | `EnemyDeathTimestamp` | `EnemyDeathTimeStamp` |
| `m_entryStateID` | `EntryStateID` | `EntryStateId` |
| `m_enumParamID` | `EnumParamID` | `EnumParamId` |
| `m_eventID` | `EventID` | `EventId` |
| `m_fAutobalanceDisplayTime` | `AutobalanceDisplayTime` | `AutoBalanceDisplayTime` |
| `m_fCubemapScale` | `CubemapScale` | `CubeMapScale` |
| `m_fIndirectLightStrength` | `IndirectLightStrength` | `InDirectLightStrength` |
| `m_fIsosurfaceThreshold` | `IsosurfaceThreshold` | `IsoSurfaceThreshold` |
| `m_fLifetimeMax` | `LifetimeMax` | `LifeTimeMax` |
| `m_fLifetimeMin` | `LifetimeMin` | `LifeTimeMin` |
| `m_fLifetimeRandExponent` | `LifetimeRandExponent` | `LifeTimeRandExponent` |
| `m_fallbackTargetPositionParamID` | `FallbackTargetPositionParamID` | `FallbackTargetPositionParamId` |
| `m_fireWeaponTimestamp` | `FireWeaponTimestamp` | `FireWeaponTimeStamp` |
| `m_firstSawEnemyTimestamp` | `FirstSawEnemyTimestamp` | `FirstSawEnemyTimeStamp` |
| `m_flAttackMovespeedFactor` | `AttackMovespeedFactor` | `AttackMoveSpeedFactor` |
| `m_flBakeSpecularToCubemapsScale` | `BakeSpecularToCubemapsScale` | `BakeSpecularToCubeMapsScale` |
| `m_flClientHealthFadeChangeTimestamp` | `ClientHealthFadeChangeTimestamp` | `ClientHealthFadeChangeTimeStamp` |
| `m_flConnectionInparamOffset` | `ConnectionInparamOffset` | `ConnectionInParamOffset` |
| `m_flConnectionInparamOffsetArray` | `ConnectionInparamOffsetArray` | `ConnectionInParamOffsetArray` |
| `m_flConstantLifespan` | `ConstantLifespan` | `ConstantLifeSpan` |
| `m_flCrossfadeTime` | `CrossfadeTime` | `CrossFadeTime` |
| `m_flCrosshairDistance` | `CrosshairDistance` | `CrossHairDistance` |
| `m_flCurrentGustLifetime` | `CurrentGustLifetime` | `CurrentGustLifeTime` |
| `m_flDealtDamageToEnemyMostRecentTimestamp` | `DealtDamageToEnemyMostRecentTimestamp` | `DealtDamageToEnemyMostRecentTimeStamp` |
| `m_flDesiredTimescale` | `DesiredTimescale` | `DesiredTimeScale` |
| `m_flEndcapTime` | `EndcapTime` | `EndCapTime` |
| `m_flFeedforwardGain` | `FeedforwardGain` | `FeedForwardGain` |
| `m_flFramerate` | `Framerate` | `FrameRate` |
| `m_flGlowBackfaceMult` | `GlowBackfaceMult` | `GlowBackFaceMult` |
| `m_flGrainCrossfadeAmount` | `GrainCrossfadeAmount` | `GrainCrossFadeAmount` |
| `m_flHitboxFireScale` | `HitboxFireScale` | `HitBoxFireScale` |
| `m_flHitboxVelocityScale` | `HitboxVelocityScale` | `HitBoxVelocityScale` |
| `m_flIndirectStrength` | `IndirectStrength` | `InDirectStrength` |
| `m_flLastCameraSetupTime` | `LastCameraSetupTime` | `LastCameraSetUpTime` |
| `m_flLifespanOverlap` | `LifespanOverlap` | `LifeSpanOverlap` |
| `m_flLifetime` | `Lifetime` | `LifeTime` |
| `m_flMaxspeed` | `Maxspeed` | `MaxSpeed` |
| `m_flMemberLifespanTime` | `MemberLifespanTime` | `MemberLifeSpanTime` |
| `m_flMuzzleSmokeTimeout` | `MuzzleSmokeTimeout` | `MuzzleSmokeTimeOut` |
| `m_flNotchedOutputOutside` | `NotchedOutputOutside` | `NotchedOutputOutSide` |
| `m_flOffscreenTime` | `OffscreenTime` | `OffScreenTime` |
| `m_flOutlineEnd0` | `OutlineEnd0` | `OutLineEnd0` |
| `m_flOutlineEnd1` | `OutlineEnd1` | `OutLineEnd1` |
| `m_flOutlineStart0` | `OutlineStart0` | `OutLineStart0` |
| `m_flOutlineStart1` | `OutlineStart1` | `OutLineStart1` |
| `m_flOutsideThreshold` | `OutsideThreshold` | `OutSideThreshold` |
| `m_flPrecomputedMaxRange` | `PrecomputedMaxRange` | `PreComputedMaxRange` |
| `m_flRecentExecTimeoutSec` | `RecentExecTimeoutSec` | `RecentExecTimeOutSec` |
| `m_flSpriteFramerate` | `SpriteFramerate` | `SpriteFrameRate` |
| `m_flTimeoutDuration` | `TimeoutDuration` | `TimeOutDuration` |
| `m_flTimeoutInterval` | `TimeoutInterval` | `TimeOutInterval` |
| `m_flTonemapEVSmoothingRange` | `TonemapEVSmoothingRange` | `ToneMapEVSmoothingRange` |
| `m_flViewkick` | `Viewkick` | `ViewKick` |
| `m_flViewmodelFOV` | `ViewmodelFOV` | `ViewModelFOV` |
| `m_flViewmodelOffsetX` | `ViewmodelOffsetX` | `ViewModelOffsetX` |
| `m_flViewmodelOffsetY` | `ViewmodelOffsetY` | `ViewModelOffsetY` |
| `m_flViewmodelOffsetZ` | `ViewmodelOffsetZ` | `ViewModelOffsetZ` |
| `m_flWeaponGameplayAnimStateTimestamp` | `WeaponGameplayAnimStateTimestamp` | `WeaponGameplayAnimStateTimeStamp` |
| `m_flZoomCooldownTimestamp` | `ZoomCooldownTimestamp` | `ZoomCooldownTimeStamp` |
| `m_followTimestamp` | `FollowTimestamp` | `FollowTimeStamp` |
| `m_forceWorldGroupID` | `ForceWorldGroupID` | `ForceWorldGroupId` |
| `m_forceupdate` | `Forceupdate` | `ForceUpdate` |
| `m_frameblockArray` | `FrameblockArray` | `FrameBlockArray` |
| `m_friendDeathTimestamp` | `FriendDeathTimestamp` | `FriendDeathTimeStamp` |
| `m_fromNodeID` | `FromNodeID` | `FromNodeId` |
| `m_globalstate` | `Globalstate` | `GlobalState` |
| `m_groundActionDirectionID` | `GroundActionDirectionID` | `GroundActionDirectionId` |
| `m_hBlackboardResource` | `HBlackboardResource` | `HBlackBoardResource` |
| `m_hDefuserMultimeter` | `DefuserMultimeter` | `DefuserMultiMeter` |
| `m_hFogCubemapTexture` | `FogCubemapTexture` | `FogCubeMapTexture` |
| `m_hFogIndirectTexture` | `FogIndirectTexture` | `FogInDirectTexture` |
| `m_hInfernoClimbingOutlinePointsSnapshot` | `InfernoClimbingOutlinePointsSnapshot` | `InfernoClimbingOutLinePointsSnapshot` |
| `m_hInfernoOutlinePointsSnapshot` | `InfernoOutlinePointsSnapshot` | `InfernoOutLinePointsSnapshot` |
| `m_hTimeoutParticleEffect` | `TimeoutParticleEffect` | `TimeOutParticleEffect` |
| `m_hTonemapController` | `TonemapController` | `ToneMapController` |
| `m_hViewmodelAttachment` | `ViewmodelAttachment` | `ViewModelAttachment` |
| `m_hitboxSetName` | `HitboxSetName` | `HitBoxSetName` |
| `m_holdTargetIDTimer` | `HoldTargetIDTimer` | `HoldTargetIdTimer` |
| `m_hostageEscortCountTimestamp` | `HostageEscortCountTimestamp` | `HostageEscortCountTimeStamp` |
| `m_iAccountID` | `AccountID` | `AccountId` |
| `m_iClanID` | `ClanID` | `ClanId` |
| `m_iGlobalname` | `Globalname` | `GlobalName` |
| `m_iIDEntIndex` | `IDEntIndex` | `IdEntIndex` |
| `m_iItemID` | `ItemID` | `ItemId` |
| `m_iItemIDHigh` | `ItemIDHigh` | `ItemIdHigh` |
| `m_iItemIDLow` | `ItemIDLow` | `ItemIdLow` |
| `m_iLastWeaponFireUsercmd` | `LastWeaponFireUsercmd` | `LastWeaponFireUserCmd` |
| `m_iMusicKitID` | `MusicKitID` | `MusicKitId` |
| `m_iNumHitboxFires` | `NumHitboxFires` | `NumHitBoxFires` |
| `m_iOldIDEntIndex` | `OldIDEntIndex` | `OldIdEntIndex` |
| `m_iPawnLifetimeEnd` | `PawnLifetimeEnd` | `PawnLifeTimeEnd` |
| `m_iPawnLifetimeStart` | `PawnLifetimeStart` | `PawnLifeTimeStart` |
| `m_iSilencerBodygroup` | `SilencerBodygroup` | `SilencerBodyGroup` |
| `m_iTimeout` | `Timeout` | `TimeOut` |
| `m_inPrecache` | `InPrecache` | `InPreCache` |
| `m_inhibitLookAroundTimestamp` | `InhibitLookAroundTimestamp` | `InhibitLookAroundTimeStamp` |
| `m_initialstate` | `Initialstate` | `InitialState` |
| `m_inputNodeID` | `InputNodeID` | `InputNodeId` |
| `m_inputPinID` | `InputPinID` | `InputPinId` |
| `m_iszAchievementEventID` | `AchievementEventID` | `AchievementEventId` |
| `m_iszIcon_Offscreen` | `IconOffscreen` | `IconOffScreen` |
| `m_iszIcon_Onscreen` | `IconOnscreen` | `IconOnScreen` |
| `m_jumpTimestamp` | `JumpTimestamp` | `JumpTimeStamp` |
| `m_lastRadioRecievedTimestamp` | `LastRadioRecievedTimestamp` | `LastRadioRecievedTimeStamp` |
| `m_lastRadioSentTimestamp` | `LastRadioSentTimestamp` | `LastRadioSentTimeStamp` |
| `m_lastSawEnemyTimestamp` | `LastSawEnemyTimestamp` | `LastSawEnemyTimeStamp` |
| `m_lastVictimID` | `LastVictimID` | `LastVictimId` |
| `m_leftEffectorBoneID` | `LeftEffectorBoneID` | `LeftEffectorBoneId` |
| `m_localIKAutoplayLockArray` | `LocalIKAutoplayLockArray` | `LocalIKAutoPlayLockArray` |
| `m_lookAroundStateTimestamp` | `LookAroundStateTimestamp` | `LookAroundStateTimeStamp` |
| `m_lookAtSpotTimestamp` | `LookAtSpotTimestamp` | `LookAtSpotTimeStamp` |
| `m_lookDirectionID` | `LookDirectionID` | `LookDirectionId` |
| `m_lookDistanceID` | `LookDistanceID` | `LookDistanceId` |
| `m_lookHeadingID` | `LookHeadingID` | `LookHeadingId` |
| `m_lookHeadingNormalizedID` | `LookHeadingNormalizedID` | `LookHeadingNormalizedId` |
| `m_lookHeadingVelocityID` | `LookHeadingVelocityID` | `LookHeadingVelocityId` |
| `m_lookPitchID` | `LookPitchID` | `LookPitchId` |
| `m_lookTargetID` | `LookTargetID` | `LookTargetId` |
| `m_lookTargetWorldSpaceID` | `LookTargetWorldSpaceID` | `LookTargetWorldSpaceId` |
| `m_lookupFilename` | `LookupFilename` | `LookupFileName` |
| `m_loopcapVsnd` | `LoopcapVsnd` | `LoopCapVsnd` |
| `m_markerIDToMatch` | `MarkerIDToMatch` | `MarkerIdToMatch` |
| `m_maskID` | `MaskID` | `MaskId` |
| `m_matchID` | `MatchID` | `MatchId` |
| `m_mixgroup` | `Mixgroup` | `MixGroup` |
| `m_moveDirectionID` | `MoveDirectionID` | `MoveDirectionId` |
| `m_moveHeadingParamID` | `MoveHeadingParamID` | `MoveHeadingParamId` |
| `m_msQueuedModeDisconnectionTimestamp` | `MsQueuedModeDisconnectionTimestamp` | `MsQueuedModeDisconnectionTimeStamp` |
| `m_nActorID` | `ActorID` | `ActorId` |
| `m_nAmbisonicsOrderOutsideField` | `AmbisonicsOrderOutsideField` | `AmbisonicsOrderOutSideField` |
| `m_nBakeSpecularToCubemaps` | `BakeSpecularToCubemaps` | `BakeSpecularToCubeMaps` |
| `m_nBlackboardIndex` | `BlackboardIndex` | `BlackBoardIndex` |
| `m_nBlackboardReference` | `BlackboardReference` | `BlackBoardReference` |
| `m_nBlackboardReferenceIdx` | `BlackboardReferenceIdx` | `BlackBoardReferenceIdx` |
| `m_nCTsAliveAtFreezetimeEnd` | `CTsAliveAtFreezetimeEnd` | `CTsAliveAtFreezeTimeEnd` |
| `m_nChildGroupID` | `ChildGroupID` | `ChildGroupId` |
| `m_nClipmapLevels` | `ClipmapLevels` | `ClipMapLevels` |
| `m_nCompileTimestamp` | `CompileTimestamp` | `CompileTimeStamp` |
| `m_nCrosshairDeltaDistance` | `CrosshairDeltaDistance` | `CrossHairDeltaDistance` |
| `m_nCrosshairMinDistance` | `CrosshairMinDistance` | `CrossHairMinDistance` |
| `m_nCubeMapPrecomputedHandshake` | `CubeMapPrecomputedHandshake` | `CubeMapPreComputedHandshake` |
| `m_nCubemapSourceType` | `CubemapSourceType` | `CubeMapSourceType` |
| `m_nDesiredHitbox` | `DesiredHitbox` | `DesiredHitBox` |
| `m_nEditorNodeID` | `EditorNodeID` | `EditorNodeId` |
| `m_nElementID` | `ElementID` | `ElementId` |
| `m_nEventID` | `EventID` | `EventId` |
| `m_nFireLifetime` | `FireLifetime` | `FireLifeTime` |
| `m_nFlowNodeID` | `FlowNodeID` | `FlowNodeId` |
| `m_nGroomGroupID` | `GroomGroupID` | `GroomGroupId` |
| `m_nGroupID` | `GroupID` | `GroupId` |
| `m_nHitbox` | `Hitbox` | `HitBox` |
| `m_nHitboxDataType` | `HitboxDataType` | `HitBoxDataType` |
| `m_nHitboxSet` | `HitboxSet` | `HitBoxSet` |
| `m_nHitboxValueFromControlPointIndex` | `HitboxValueFromControlPointIndex` | `HitBoxValueFromControlPointIndex` |
| `m_nIndirectTextureDimX` | `IndirectTextureDimX` | `InDirectTextureDimX` |
| `m_nIndirectTextureDimY` | `IndirectTextureDimY` | `InDirectTextureDimY` |
| `m_nIndirectTextureDimZ` | `IndirectTextureDimZ` | `InDirectTextureDimZ` |
| `m_nInstanceID` | `InstanceID` | `InstanceId` |
| `m_nKeychainDefID` | `KeychainDefID` | `KeyChainDefId` |
| `m_nKeychainSeed` | `KeychainSeed` | `KeyChainSeed` |
| `m_nLODSetupIndex` | `LODSetupIndex` | `LODSetUpIndex` |
| `m_nLastMatchTime_MatchID64` | `LastMatchTimeMatchID64` | `LastMatchTimeMatchId64` |
| `m_nLightProbeVolumePrecomputedHandshake` | `LightProbeVolumePrecomputedHandshake` | `LightProbeVolumePreComputedHandshake` |
| `m_nLightmapGameVersionNumber` | `LightmapGameVersionNumber` | `LightMapGameVersionNumber` |
| `m_nLightmapVersionNumber` | `LightmapVersionNumber` | `LightMapVersionNumber` |
| `m_nLocalBonemask` | `LocalBonemask` | `LocalBoneMask` |
| `m_nLocalWeightlist` | `LocalWeightlist` | `LocalWeightList` |
| `m_nModelID` | `ModelID` | `ModelId` |
| `m_nMultisampleNumSamples` | `MultisampleNumSamples` | `MultiSampleNumSamples` |
| `m_nNextMapInMapgroup` | `NextMapInMapgroup` | `NextMapInMapGroup` |
| `m_nNodeBaseJiggleboneDependsCount` | `NodeBaseJiggleboneDependsCount` | `NodeBaseJiggleBoneDependsCount` |
| `m_nNodeID` | `NodeID` | `NodeId` |
| `m_nObjectID` | `ObjectID` | `ObjectId` |
| `m_nOutlineAlpha` | `OutlineAlpha` | `OutLineAlpha` |
| `m_nOutputSubmix` | `OutputSubmix` | `OutputSubMix` |
| `m_nOutsideWorld` | `OutsideWorld` | `OutSideWorld` |
| `m_nOversampleFactor` | `OversampleFactor` | `OverSampleFactor` |
| `m_nPrecomputedSubFrusta` | `PrecomputedSubFrusta` | `PreComputedSubFrusta` |
| `m_nSplitscreenFlags` | `SplitscreenFlags` | `SplitScreenFlags` |
| `m_nStepside` | `Stepside` | `StepSide` |
| `m_nSubclassID` | `SubclassID` | `SubclassId` |
| `m_nTerroristsAliveAtFreezetimeEnd` | `TerroristsAliveAtFreezetimeEnd` | `TerroristsAliveAtFreezeTimeEnd` |
| `m_nTintID` | `TintID` | `TintId` |
| `m_nUniqueID` | `UniqueID` | `UniqueId` |
| `m_nValueNodeID` | `ValueNodeID` | `ValueNodeId` |
| `m_nWorldGroupID` | `WorldGroupID` | `WorldGroupId` |
| `m_netlookupFilename` | `NetlookupFilename` | `NetlookupFileName` |
| `m_nextCleanupCheckTimestamp` | `NextCleanupCheckTimestamp` | `NextCleanupCheckTimeStamp` |
| `m_nodeID` | `NodeID` | `NodeId` |
| `m_noiseTimestamp` | `NoiseTimestamp` | `NoiseTimeStamp` |
| `m_optionalID` | `OptionalID` | `OptionalId` |
| `m_outputID` | `OutputID` | `OutputId` |
| `m_outputNodeID` | `OutputNodeID` | `OutputNodeId` |
| `m_outputPinID` | `OutputPinID` | `OutputPinId` |
| `m_overrideMaskID` | `OverrideMaskID` | `OverrideMaskId` |
| `m_pAutoaimServices` | `AutoaimServices` | `AutoAimServices` |
| `m_pCPPClassname` | `CPPClassname` | `CPPClassName` |
| `m_pClientsideRagdoll` | `ClientsideRagdoll` | `ClientSideRagdoll` |
| `m_pKeyframe` | `Keyframe` | `KeyFrame` |
| `m_pPrecacheEntityKeys` | `PrecacheEntityKeys` | `PreCacheEntityKeys` |
| `m_pTimeoutScriptFunction` | `TimeoutScriptFunction` | `TimeOutScriptFunction` |
| `m_pTimeoutSoundEffect` | `TimeoutSoundEffect` | `TimeOutSoundEffect` |
| `m_paramID` | `ParamID` | `ParamId` |
| `m_parentID` | `ParentID` | `ParentId` |
| `m_peripheralTimestamp` | `PeripheralTimestamp` | `PeripheralTimeStamp` |
| `m_pinID` | `PinID` | `PinId` |
| `m_previewStartBoneID` | `PreviewStartBoneID` | `PreviewStartBoneId` |
| `m_pszTargetLayerID` | `TargetLayerID` | `TargetLayerId` |
| `m_refPhysicsHitboxData` | `RefPhysicsHitboxData` | `RefPhysicsHitBoxData` |
| `m_requireRuleID` | `RequireRuleID` | `RequireRuleId` |
| `m_rightEffectorBoneID` | `RightEffectorBoneID` | `RightEffectorBoneId` |
| `m_sActiveExternalChoreoGraphSlotID` | `SActiveExternalChoreoGraphSlotID` | `SActiveExternalChoreoGraphSlotId` |
| `m_sUniqueHammerID` | `SUniqueHammerID` | `SUniqueHammerId` |
| `m_scriptFilename` | `ScriptFilename` | `ScriptFileName` |
| `m_secondaryID` | `SecondaryID` | `SecondaryId` |
| `m_setID` | `SetID` | `SetId` |
| `m_slopeAngleFrontID` | `SlopeAngleFrontID` | `SlopeAngleFrontId` |
| `m_slopeAngleID` | `SlopeAngleID` | `SlopeAngleId` |
| `m_slopeAngleSideID` | `SlopeAngleSideID` | `SlopeAngleSideId` |
| `m_slopeHeadingID` | `SlopeHeadingID` | `SlopeHeadingId` |
| `m_slopeNormalID` | `SlopeNormalID` | `SlopeNormalId` |
| `m_slopeNormal_WorldSpaceID` | `SlopeNormalWorldSpaceID` | `SlopeNormalWorldSpaceId` |
| `m_slotID` | `SlotID` | `SlotId` |
| `m_spawnflags` | `Spawnflags` | `SpawnFlags` |
| `m_spotCheckTimestamp` | `SpotCheckTimestamp` | `SpotCheckTimeStamp` |
| `m_startStateID` | `StartStateID` | `StartStateId` |
| `m_stateID` | `StateID` | `StateId` |
| `m_stateTimestamp` | `StateTimestamp` | `StateTimeStamp` |
| `m_steamID` | `SteamID` | `SteamId` |
| `m_stencilTestID` | `StencilTestID` | `StencilTestId` |
| `m_stencilWriteID` | `StencilWriteID` | `StencilWriteId` |
| `m_strNametagString` | `NametagString` | `NameTagString` |
| `m_strParentPathUniqueID` | `ParentPathUniqueID` | `ParentPathUniqueId` |
| `m_strSnapshotSubset` | `SnapshotSubset` | `SnapshotSubSet` |
| `m_strTriggerID` | `TriggerID` | `TriggerId` |
| `m_stuckTimestamp` | `StuckTimestamp` | `StuckTimeStamp` |
| `m_subGraphFilename` | `SubGraphFilename` | `SubGraphFileName` |
| `m_subgraphName` | `SubgraphName` | `SubGraphName` |
| `m_subgraphs` | `Subgraphs` | `SubGraphs` |
| `m_syncID` | `SyncID` | `SyncId` |
| `m_szClanTeamname` | `ClanTeamname` | `ClanTeamName` |
| `m_szCrosshairCodes` | `CrosshairCodes` | `CrossHairCodes` |
| `m_szNetworkIDString` | `NetworkIDString` | `NetworkIdString` |
| `m_szParentPathUniqueID` | `ParentPathUniqueID` | `ParentPathUniqueId` |
| `m_szTeamname` | `Teamname` | `TeamName` |
| `m_tagID` | `TagID` | `TagId` |
| `m_targetFacePositionParamID` | `TargetFacePositionParamID` | `TargetFacePositionParamId` |
| `m_targetOffsetParamID` | `TargetOffsetParamID` | `TargetOffsetParamId` |
| `m_targetParamID` | `TargetParamID` | `TargetParamId` |
| `m_targetPositionParamID` | `TargetPositionParamID` | `TargetPositionParamId` |
| `m_targetSyncIDNodeIdx` | `TargetSyncIDNodeIdx` | `TargetSyncIdNodeIdx` |
| `m_targetUpVectorParamID` | `TargetUpVectorParamID` | `TargetUpVectorParamId` |
| `m_timescale` | `Timescale` | `TimeScale` |
| `m_timestamp` | `Timestamp` | `TimeStamp` |
| `m_toNodeID` | `ToNodeID` | `ToNodeId` |
| `m_tokLayerMatchID` | `TokLayerMatchID` | `TokLayerMatchId` |
| `m_tonemapControllerName` | `TonemapControllerName` | `ToneMapControllerName` |
| `m_triggermode` | `Triggermode` | `TriggerMode` |
| `m_ullLocalMatchID` | `UllLocalMatchID` | `UllLocalMatchId` |
| `m_ullRegisteredAsItemID` | `UllRegisteredAsItemID` | `UllRegisteredAsItemId` |
| `m_unAccountID` | `AccountID` | `AccountId` |
| `m_unFreezetimeEndEquipmentValue` | `FreezetimeEndEquipmentValue` | `FreezeTimeEndEquipmentValue` |
| `m_unMusicID` | `MusicID` | `MusicId` |
| `m_unTraceID` | `TraceID` | `TraceId` |
| `m_updateID` | `UpdateID` | `UpdateId` |
| `m_vBakeSpecularToCubemapsSize` | `BakeSpecularToCubemapsSize` | `BakeSpecularToCubeMapsSize` |
| `m_vLightmapUvScale` | `LightmapUvScale` | `LightMapUvScale` |
| `m_vMidpointPositionMS` | `MidpointPositionMS` | `MidPointPositionMS` |
| `m_vMinimapMaxs` | `MinimapMaxs` | `MiniMapMaxs` |
| `m_vMinimapMins` | `MinimapMins` | `MiniMapMins` |
| `m_vPrecomputedBoundsMaxs` | `PrecomputedBoundsMaxs` | `PreComputedBoundsMaxs` |
| `m_vPrecomputedBoundsMins` | `PrecomputedBoundsMins` | `PreComputedBoundsMins` |
| `m_vPrecomputedOBBAngles` | `PrecomputedOBBAngles` | `PreComputedOBBAngles` |
| `m_vPrecomputedOBBAngles0` | `PrecomputedOBBAngles0` | `PreComputedOBBAngles0` |
| `m_vPrecomputedOBBAngles1` | `PrecomputedOBBAngles1` | `PreComputedOBBAngles1` |
| `m_vPrecomputedOBBAngles2` | `PrecomputedOBBAngles2` | `PreComputedOBBAngles2` |
| `m_vPrecomputedOBBAngles3` | `PrecomputedOBBAngles3` | `PreComputedOBBAngles3` |
| `m_vPrecomputedOBBAngles4` | `PrecomputedOBBAngles4` | `PreComputedOBBAngles4` |
| `m_vPrecomputedOBBAngles5` | `PrecomputedOBBAngles5` | `PreComputedOBBAngles5` |
| `m_vPrecomputedOBBExtent` | `PrecomputedOBBExtent` | `PreComputedOBBExtent` |
| `m_vPrecomputedOBBExtent0` | `PrecomputedOBBExtent0` | `PreComputedOBBExtent0` |
| `m_vPrecomputedOBBExtent1` | `PrecomputedOBBExtent1` | `PreComputedOBBExtent1` |
| `m_vPrecomputedOBBExtent2` | `PrecomputedOBBExtent2` | `PreComputedOBBExtent2` |
| `m_vPrecomputedOBBExtent3` | `PrecomputedOBBExtent3` | `PreComputedOBBExtent3` |
| `m_vPrecomputedOBBExtent4` | `PrecomputedOBBExtent4` | `PreComputedOBBExtent4` |
| `m_vPrecomputedOBBExtent5` | `PrecomputedOBBExtent5` | `PreComputedOBBExtent5` |
| `m_vPrecomputedOBBOrigin` | `PrecomputedOBBOrigin` | `PreComputedOBBOrigin` |
| `m_vPrecomputedOBBOrigin0` | `PrecomputedOBBOrigin0` | `PreComputedOBBOrigin0` |
| `m_vPrecomputedOBBOrigin1` | `PrecomputedOBBOrigin1` | `PreComputedOBBOrigin1` |
| `m_vPrecomputedOBBOrigin2` | `PrecomputedOBBOrigin2` | `PreComputedOBBOrigin2` |
| `m_vPrecomputedOBBOrigin3` | `PrecomputedOBBOrigin3` | `PreComputedOBBOrigin3` |
| `m_vPrecomputedOBBOrigin4` | `PrecomputedOBBOrigin4` | `PreComputedOBBOrigin4` |
| `m_vPrecomputedOBBOrigin5` | `PrecomputedOBBOrigin5` | `PreComputedOBBOrigin5` |
| `m_vWaypointPosWS` | `WaypointPosWS` | `WayPointPosWS` |
| `m_variationID` | `VariationID` | `VariationId` |
| `m_vecLastCameraSetupLocalOrigin` | `LastCameraSetupLocalOrigin` | `LastCameraSetUpLocalOrigin` |
| `m_vecNetworkableLoadout` | `NetworkableLoadout` | `NetworkAbleLoadout` |
| `m_vecOutsideField` | `OutsideField` | `OutSideField` |
| `m_vecSellbackPurchaseEntries` | `SellbackPurchaseEntries` | `SellBackPurchaseEntries` |
| `m_vecSubtreeDetailLayers` | `SubtreeDetailLayers` | `SubTreeDetailLayers` |
| `m_voiceEndTimestamp` | `VoiceEndTimestamp` | `VoiceEndTimeStamp` |
| `mapgroup` | `Mapgroup` | `MapGroup` |
| `mapname` | `Mapname` | `MapName` |
| `maxdensity` | `Maxdensity` | `MaxDensity` |
| `maxdensityLerpTo` | `MaxdensityLerpTo` | `MaxDensityLerpTo` |
| `maxplayers` | `Maxplayers` | `MaxPlayers` |
| `motionflags` | `Motionflags` | `MotionFlags` |
| `musickitid` | `Musickitid` | `MusicKitId` |
| `musickitmvps` | `Musickitmvps` | `MusicKitMvps` |
| `nCursorID` | `CursorID` | `CursorId` |
| `nEditorID` | `EditorID` | `EditorId` |
| `nRetiredAtNodeID` | `RetiredAtNodeID` | `RetiredAtNodeId` |
| `nSpawnNodeID` | `SpawnNodeID` | `SpawnNodeId` |
| `nVarDefID` | `VarDefID` | `VarDefId` |
| `nViewportFontSize` | `ViewportFontSize` | `ViewPortFontSize` |
| `networkid` | `Networkid` | `NetworkId` |
| `newmode` | `Newmode` | `NewMode` |
| `newname` | `Newname` | `NewName` |
| `nextlevel` | `Nextlevel` | `NextLevel` |
| `nomusic` | `Nomusic` | `NoMusic` |
| `noscope` | `Noscope` | `NoScope` |
| `numadvanced` | `Numadvanced` | `NumAdvanced` |
| `numbronze` | `Numbronze` | `NumBronze` |
| `numgold` | `Numgold` | `NumGold` |
| `numsilver` | `Numsilver` | `NumSilver` |
| `oldmode` | `Oldmode` | `OldMode` |
| `oldname` | `Oldname` | `OldName` |
| `oldteam` | `Oldteam` | `OldTeam` |
| `otherid` | `Otherid` | `OtherId` |
| `othertype` | `Othertype` | `OtherType` |
| `pitchfrac` | `Pitchfrac` | `PitchFrac` |
| `pitchrun` | `Pitchrun` | `PitchRun` |
| `pitchstart` | `Pitchstart` | `PitchStart` |
| `playerid` | `Playerid` | `PlayerId` |
| `roundslimit` | `Roundslimit` | `RoundsLimit` |
| `saveentityindex` | `Saveentityindex` | `SaveEntityIndex` |
| `show_timer_defend` | `ShowTimerDefend` | `ShowTimerDefEnd` |
| `skirmishmode` | `Skirmishmode` | `SkirmishMode` |
| `spindown` | `Spindown` | `SpinDown` |
| `spindownsav` | `Spindownsav` | `SpinDownSav` |
| `spinup` | `Spinup` | `SpinUp` |
| `spinupsav` | `Spinupsav` | `SpinUpSav` |
| `splitscreenplayer` | `Splitscreenplayer` | `SplitScreenPlayer` |
| `starttime` | `Starttime` | `StartTime` |
| `steamID` | `SteamID` | `SteamId` |
| `steamid` | `Steamid` | `SteamId` |
| `subgraphFile` | `SubgraphFile` | `SubGraphFile` |
| `subgraphName` | `SubgraphName` | `SubGraphName` |
| `subgraphs` | `Subgraphs` | `SubGraphs` |
| `teamid` | `Teamid` | `TeamId` |
| `teamname` | `Teamname` | `TeamName` |
| `teamonly` | `Teamonly` | `TeamOnly` |
| `tempent_renderamt` | `TempentRenderamt` | `TempEntRenderamt` |
| `thrusmoke` | `Thrusmoke` | `ThruSmoke` |
| `timelimit` | `Timelimit` | `TimeLimit` |
| `userid` | `Userid` | `UserId` |
| `victimid` | `Victimid` | `VictimId` |
| `volfrac` | `Volfrac` | `VolFrac` |
| `volrun` | `Volrun` | `VolRun` |
| `volstart` | `Volstart` | `VolStart` |
| `votedata` | `Votedata` | `VoteData` |
| `waypoints` | `Waypoints` | `WayPoints` |
| `weapon_itemid` | `WeaponItemid` | `WeaponItemId` |
| `weapon_originalowner_xuid` | `WeaponOriginalownerXuid` | `WeaponOriginalOwnerXuid` |
| `weptype` | `Weptype` | `WepType` |

## Renamed types

| Was | Now |
|---|---|
| `AggregateLODSetup` | `AggregateLODSetUp` |
| `AIMotorServicesDebugSnapshotDataTMotorPathWaypoint` | `AIMotorServicesDebugSnapshotDataTMotorPathWayPoint` |
| `AINavigatorDebugSnapshotDataTWaypoint` | `AINavigatorDebugSnapshotDataTWayPoint` |
| `AnimComponentID` | `AnimComponentId` |
| `AnimNodeID` | `AnimNodeId` |
| `AnimNodeOutputID` | `AnimNodeOutputId` |
| `AnimParamID` | `AnimParamId` |
| `AnimStateID` | `AnimStateId` |
| `AnimTagID` | `AnimTagId` |
| `Audioparams` | `AudioParams` |
| `BuytimeEndedEvent` | `BuyTimeEndedEvent` |
| `CAudioBoxverbNodeDesc` | `CAudioBoxVerbNodeDesc` |
| `CAudioFreeverbNodeDesc` | `CAudioFreeVerbNodeDesc` |
| `CAudioPlateverbNodeDesc` | `CAudioPlateVerbNodeDesc` |
| `CAudioSubgraphNodeDesc` | `CAudioSubGraphNodeDesc` |
| `CAudioSubgraphSwitchNodeDesc` | `CAudioSubGraphSwitchNodeDesc` |
| `CBaseAnimGraphAliasBaseanimating` | `CBaseAnimGraphAliasBaseAnimating` |
| `CControlCrossfadeNodeDesc` | `CControlCrossFadeNodeDesc` |
| `CCSGOEndOfMatchLineupEnd` | `CCSGOEndOfMatchLineUpEnd` |
| `CCSGOEndOfMatchLineupEndpoint` | `CCSGOEndOfMatchLineUpEndPoint` |
| `CCSGOEndOfMatchLineupStart` | `CCSGOEndOfMatchLineUpStart` |
| `CCSGOPreviewModelAliasCsgoItemPreviewmodel` | `CCSGOPreviewModelAliasCsgoItemPreviewModel` |
| `CCSGOPreviewPlayerAliasCsgoPlayerPreviewmodel` | `CCSGOPreviewPlayerAliasCsgoPlayerPreviewModel` |
| `CCSMinimapBoundary` | `CCSMiniMapBoundary` |
| `CDSPMixgroupModifier` | `CDSPMixGroupModifier` |
| `CDSPPresetMixgroupModifierTable` | `CDSPPresetMixGroupModifierTable` |
| `CEnvCubemap` | `CEnvCubeMap` |
| `CEnvCubemapAPI` | `CEnvCubeMapAPI` |
| `CEnvCubemapBox` | `CEnvCubeMapBox` |
| `CEnvCubemapFog` | `CEnvCubeMapFog` |
| `CEnvWindClientside` | `CEnvWindClientSide` |
| `CFogplayerparams` | `CFogPlayerParams` |
| `CFuncLadderAliasFuncUseableladder` | `CFuncLadderAliasFuncUseableLadder` |
| `CFuncShatterglass` | `CFuncShatterGlass` |
| `CFuncTimescale` | `CFuncTimeScale` |
| `CHitboxComponent` | `CHitBoxComponent` |
| `ChoreoExternalAnimgraphControlState` | `ChoreoExternalAnimGraphControlState` |
| `CInfoOffscreenPanoramaTexture` | `CInfoOffScreenPanoramaTexture` |
| `CINITInitialVelocityFromHitbox` | `CINITInitialVelocityFromHitBox` |
| `CINITLifespanFromVelocity` | `CINITLifeSpanFromVelocity` |
| `CINITSetHitboxToClosest` | `CINITSetHitBoxToClosest` |
| `CINITSetHitboxToModel` | `CINITSetHitBoxToModel` |
| `CItemHealthshot` | `CItemHealthShot` |
| `CKeychainModule` | `CKeyChainModule` |
| `ClientsideLessonClosedEvent` | `ClientSideLessonClosedEvent` |
| `ClientsideReloadCustomEconEvent` | `ClientSideReloadCustomEconEvent` |
| `CLogicActiveAutosave` | `CLogicActiveAutoSave` |
| `CLogicAutosave` | `CLogicAutoSave` |
| `CLogicDistanceAutosave` | `CLogicDistanceAutoSave` |
| `CMixBoxverb` | `CMixBoxVerb` |
| `CMixControlCrossfade` | `CMixControlCrossFade` |
| `CMixFreeverb` | `CMixFreeVerb` |
| `CMixPlateverb` | `CMixPlateVerb` |
| `CMixSubgraph` | `CMixSubGraph` |
| `CMixSubgraphSwitch` | `CMixSubGraphSwitch` |
| `CModelConfigElementSetBodygroup` | `CModelConfigElementSetBodyGroup` |
| `CModelConfigElementSetBodygroupOnAttachedModels` | `CModelConfigElementSetBodyGroupOnAttachedModels` |
| `CMultimeter` | `CMultiMeter` |
| `CNametagModule` | `CNameTagModule` |
| `CNmCachedIDNodeCDefinition` | `CNmCachedIdNodeCDefinition` |
| `CNmClipDocEventID` | `CNmClipDocEventId` |
| `CNmConstIDNodeCDefinition` | `CNmConstIdNodeCDefinition` |
| `CNmControlParameterIDNodeCDefinition` | `CNmControlParameterIdNodeCDefinition` |
| `CNmCurrentSyncEventIDNodeCDefinition` | `CNmCurrentSyncEventIdNodeCDefinition` |
| `CNmFootstepEventIDNodeCDefinition` | `CNmFootstepEventIdNodeCDefinition` |
| `CNmGraphDocCachedIDNode` | `CNmGraphDocCachedIdNode` |
| `CnmGraphDocConstIDNode` | `CnmGraphDocConstIdNode` |
| `CNmGraphDocCurrentSyncEventIDNode` | `CNmGraphDocCurrentSyncEventIdNode` |
| `CNmGraphDocDataDictionaryIDSet` | `CNmGraphDocDataDictionaryIdSet` |
| `CNmGraphDocFootstepEventIDNode` | `CNmGraphDocFootstepEventIdNode` |
| `CNmGraphDocIDBasedClipSelectorNode` | `CNmGraphDocIdBasedClipSelectorNode` |
| `CNmGraphDocIDBasedSelectorNode` | `CNmGraphDocIdBasedSelectorNode` |
| `CNmGraphDocIDComparisonNode` | `CNmGraphDocIdComparisonNode` |
| `CNmGraphDocIDControlParameterNode` | `CNmGraphDocIdControlParameterNode` |
| `CNmGraphDocIDEventConditionNode` | `CNmGraphDocIdEventConditionNode` |
| `CNmGraphDocIDEventConditionNodeSearchRule` | `CNmGraphDocIdEventConditionNodeSearchRule` |
| `CNmGraphDocIDEventNode` | `CNmGraphDocIdEventNode` |
| `CNmGraphDocIDEventPercentageThroughNode` | `CNmGraphDocIdEventPercentageThroughNode` |
| `CNmGraphDocIDParameterReferenceNode` | `CNmGraphDocIdParameterReferenceNode` |
| `CNmGraphDocIDResultNode` | `CNmGraphDocIdResultNode` |
| `CNmGraphDocIDSelectorNode` | `CNmGraphDocIdSelectorNode` |
| `CNmGraphDocIDSwitchNode` | `CNmGraphDocIdSwitchNode` |
| `CNmGraphDocIDToFloatNode` | `CNmGraphDocIdToFloatNode` |
| `CNmGraphDocIDToFloatNodeMapping` | `CNmGraphDocIdToFloatNodeMapping` |
| `CNmGraphDocIDVirtualParameterNode` | `CNmGraphDocIdVirtualParameterNode` |
| `CNmGraphDocVariationIDComparisonNode` | `CNmGraphDocVariationIdComparisonNode` |
| `CNmGraphDocVariationIDComparisonNodeCData` | `CNmGraphDocVariationIdComparisonNodeCData` |
| `CNmIDBasedClipSelectorNodeCDefinition` | `CNmIdBasedClipSelectorNodeCDefinition` |
| `CNmIDBasedSelectorNodeCDefinition` | `CNmIdBasedSelectorNodeCDefinition` |
| `CNmIDComparisonNodeCDefinition` | `CNmIdComparisonNodeCDefinition` |
| `CNmIDComparisonNodeComparison` | `CNmIdComparisonNodeComparison` |
| `CNmIDEvent` | `CNmIdEvent` |
| `CNmIDEventConditionNodeCDefinition` | `CNmIdEventConditionNodeCDefinition` |
| `CNmIDEventNodeCDefinition` | `CNmIdEventNodeCDefinition` |
| `CNmIDEventPercentageThroughNodeCDefinition` | `CNmIdEventPercentageThroughNodeCDefinition` |
| `CNmIDSelectorNodeCDefinition` | `CNmIdSelectorNodeCDefinition` |
| `CNmIDSwitchNodeCDefinition` | `CNmIdSwitchNodeCDefinition` |
| `CNmIDToFloatNodeCDefinition` | `CNmIdToFloatNodeCDefinition` |
| `CNmIDValueNodeCDefinition` | `CNmIdValueNodeCDefinition` |
| `CNmVirtualParameterIDNodeCDefinition` | `CNmVirtualParameterIdNodeCDefinition` |
| `ConstraintAxislimit` | `ConstraintAxisLimit` |
| `ConstraintBreakableparams` | `ConstraintBreakableParams` |
| `ConstraintHingeparams` | `ConstraintHingeParams` |
| `COPControlpointLight` | `COPControlPointLight` |
| `COPDecayOffscreen` | `COPDecayOffScreen` |
| `COPMoveToHitbox` | `COPMoveToHitBox` |
| `COPRemapAverageHitboxSpeedtoCP` | `COPRemapAverageHitBoxSpeedtoCP` |
| `CPathParticleRopeAliasPathParticleRopeClientside` | `CPathParticleRopeAliasPathParticleRopeClientSide` |
| `CPhysPropClientside` | `CPhysPropClientSide` |
| `CPlayerAutoaimServices` | `CPlayerAutoAimServices` |
| `CPointGamestatsCounter` | `CPointGameStatsCounter` |
| `CPulseArraylib` | `CPulseArrayLib` |
| `CPulseBlackboardReference` | `CPulseBlackBoardReference` |
| `CPulseBreakpointLocation` | `CPulseBreakPointLocation` |
| `CPulseCellInflowBaseEntrypoint` | `CPulseCellInflowBaseEntryPoint` |
| `CPulseCellOutflowListenForAnimgraphTag` | `CPulseCellOutflowListenForAnimGraphTag` |
| `CPulseCellStepTestDomainTracepoint` | `CPulseCellStepTestDomainTracePoint` |
| `CPulseCellTestWaitWithAutoTracepoints` | `CPulseCellTestWaitWithAutoTracePoints` |
| `CPulseEnumlib` | `CPulseEnumLib` |
| `CPulseGameBlackboard` | `CPulseGameBlackBoard` |
| `CPulseGraphInstanceGameBlackboard` | `CPulseGraphInstanceGameBlackBoard` |
| `CPulseGraphInstanceTestDomainUseReadOnlyBlackboardView` | `CPulseGraphInstanceTestDomainUseReadOnlyBlackBoardView` |
| `CPulseMathlib` | `CPulseMathLib` |
| `CRopeKeyframe` | `CRopeKeyFrame` |
| `CRopeKeyframeAliasMoveRope` | `CRopeKeyFrameAliasMoveRope` |
| `CRopeKeyframeCPhysicsDelegate` | `CRopeKeyFrameCPhysicsDelegate` |
| `CSelectableSubgraph` | `CSelectableSubGraph` |
| `CSmartPropElementMidpointDeformer` | `CSmartPropElementMidPointDeformer` |
| `CSosGroupActionSetSoundeventParameterSchema` | `CSosGroupActionSetSoundEventParameterSchema` |
| `CSosGroupActionSoundeventClusterSchema` | `CSosGroupActionSoundEventClusterSchema` |
| `CSosGroupActionSoundeventCountSchema` | `CSosGroupActionSoundEventCountSchema` |
| `CSosGroupActionSoundeventMinMaxValuesSchema` | `CSosGroupActionSoundEventMinMaxValuesSchema` |
| `CSosGroupActionSoundeventPrioritySchema` | `CSosGroupActionSoundEventPrioritySchema` |
| `CSubassetTypeInfo` | `CSubAssetTypeInfo` |
| `CSWeaponNameID` | `CSWeaponNameId` |
| `CTeamplayRules` | `CTeamPlayRules` |
| `CTonemapController2` | `CToneMapController2` |
| `CTonemapController2AliasEnvTonemapController2` | `CToneMapController2AliasEnvToneMapController2` |
| `CTonemapTrigger` | `CToneMapTrigger` |
| `CVMixBoxverbProcessorDesc` | `CVMixBoxVerbProcessorDesc` |
| `CVMixFreeverbProcessorDesc` | `CVMixFreeVerbProcessorDesc` |
| `CVMixSubgraphSwitchProcessorDesc` | `CVMixSubGraphSwitchProcessorDesc` |
| `CVoiceContainerRealtimeFMSineWave` | `CVoiceContainerRealTimeFMSineWave` |
| `Dynpitchvol` | `DynPitchVol` |
| `DynpitchvolBase` | `DynPitchVolBase` |
| `EndmatchCmmStartRevealItemsEvent` | `EndMatchCmmStartRevealItemsEvent` |
| `EndmatchMapvoteSelectingMapEvent` | `EndMatchMapVoteSelectingMapEvent` |
| `Entitytable` | `EntityTable` |
| `Fieldtype` | `FieldType` |
| `Fogparams` | `FogParams` |
| `Fogplayerparams` | `FogPlayerParams` |
| `GameinstructorDrawEvent` | `GameInstructorDrawEvent` |
| `GameinstructorNodrawEvent` | `GameInstructorNodrawEvent` |
| `GameNewmapCoreEvent` | `GameNewMapCoreEvent` |
| `GameNewmapEvent` | `GameNewMapEvent` |
| `Globalentity` | `GlobalEntity` |
| `Globalentitydatabase` | `GlobalEntityDatabase` |
| `HitboxLerpType` | `HitBoxLerpType` |
| `HltvVersioninfoEvent` | `HltvVersionInfoEvent` |
| `HostnameChangedEvent` | `HostNameChangedEvent` |
| `Hudtextparms` | `HudTextParms` |
| `Lerpdata` | `LerpData` |
| `Levellist` | `LevelList` |
| `Locksound` | `LockSound` |
| `ModelHitboxType` | `ModelHitBoxType` |
| `Modifiedconvars` | `ModifiedConvars` |
| `Navproperties` | `NavProperties` |
| `NextlevelChangedEvent` | `NextLevelChangedEvent` |
| `ParticleEndcapMode` | `ParticleEndCapMode` |
| `ParticleHitboxBiasType` | `ParticleHitBoxBiasType` |
| `ParticleHitboxDataSelection` | `ParticleHitBoxDataSelection` |
| `PhysSoftbodyDesc` | `PhysSoftBodyDesc` |
| `PlayerChangenameEvent` | `PlayerChangeNameEvent` |
| `PlayerHintmessageEvent` | `PlayerHintMessageEvent` |
| `PostProcessingTonemapParameters` | `PostProcessingToneMapParameters` |
| `PulseCursorID` | `PulseCursorId` |
| `PulseDocNodeID` | `PulseDocNodeId` |
| `PulseGraphInstanceID` | `PulseGraphInstanceId` |
| `PulseRuntimeBlackboardReferenceIndex` | `PulseRuntimeBlackBoardReferenceIndex` |
| `PulseRuntimeEntrypointIndex` | `PulseRuntimeEntryPointIndex` |
| `Ragdollelement` | `RagdollElement` |
| `Ragdollhierarchyjoint` | `RagdollHierarchyJoint` |
| `RenderMultisampleType` | `RenderMultiSampleType` |
| `RnSoftbodyCapsule` | `RnSoftBodyCapsule` |
| `RnSoftbodyParticle` | `RnSoftBodyParticle` |
| `RnSoftbodySpring` | `RnSoftBodySpring` |
| `RoundPrestartEvent` | `RoundPreStartEvent` |
| `Screenfade` | `ScreenFade` |
| `Screenshake` | `ScreenShake` |
| `SeasoncoinLevelupEvent` | `SeasoncoinLevelUpEvent` |
| `SellbackPurchaseEntry` | `SellBackPurchaseEntry` |
| `SmokegrenadeDetonateEvent` | `SmokeGrenadeDetonateEvent` |
| `SmokegrenadeExpiredEvent` | `SmokeGrenadeExpiredEvent` |
| `Soundcommands` | `SoundCommands` |
| `SoundeventPathCornerPairNetworked` | `SoundEventPathCornerPairNetworked` |
| `Soundlevel` | `SoundLevel` |
| `StartHalftimeEvent` | `StartHalfTimeEvent` |
| `TeamchangePendingEvent` | `TeamChangePendingEvent` |
| `TeamplayBroadcastAudioEvent` | `TeamPlayBroadcastAudioEvent` |
| `TeamplayRoundStartEvent` | `TeamPlayRoundStartEvent` |
| `VMixBoxverbDesc` | `VMixBoxVerbDesc` |
| `VMixFreeverbDesc` | `VMixFreeVerbDesc` |
| `VMixGraphCommandID` | `VMixGraphCommandId` |
| `VMixPlateverbDesc` | `VMixPlateVerbDesc` |
| `VMixSubgraphSwitchDesc` | `VMixSubGraphSwitchDesc` |
| `VMixSubgraphSwitchInterpolationType` | `VMixSubGraphSwitchInterpolationType` |
| `WeaponhudSelectionEvent` | `WeaponHudSelectionEvent` |
