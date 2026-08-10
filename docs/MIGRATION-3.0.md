# Migrating to CS2OpenDev.Sdk 3.0

3.0 renames generated identifiers to idiomatic .NET casing. **That is the only
change** — no type moved namespace, none was added or removed, no signature or
projected type changed, and the schema pin is the same as 2.0.4's. If your code
compiles after the renames, it behaves exactly as it did.

Regenerate the tables below with:

```
python3 scripts/rename-diff.py OLD_SDK_DIR NEW_SDK_DIR --markdown
```

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

**329** members renamed, of 11539 matched by native name.
**109** type names replaced by **109** new ones.

## Renamed members

| Native name | Was | Now |
|---|---|---|
| `PlayerID` | `PlayerID` | `PlayerId` |
| `accountid` | `Accountid` | `AccountId` |
| `addonname` | `Addonname` | `AddonName` |
| `assistedflash` | `Assistedflash` | `AssistedFlash` |
| `attackerblind` | `Attackerblind` | `AttackerBlind` |
| `attackerid` | `Attackerid` | `AttackerId` |
| `attackerinair` | `Attackerinair` | `AttackerInAir` |
| `bRenderFullyUnlitAsFullbright` | `RenderFullyUnlitAsFullbright` | `RenderFullyUnlitAsFullBright` |
| `bodygroup` | `Bodygroup` | `BodyGroup` |
| `canzoom` | `Canzoom` | `CanZoom` |
| `classname` | `Classname` | `ClassName` |
| `cvarname` | `Cvarname` | `CVarName` |
| `cvarvalue` | `Cvarvalue` | `CVarValue` |
| `defindex` | `Defindex` | `DefIndex` |
| `dmgstate` | `Dmgstate` | `DmgState` |
| `endtime` | `Endtime` | `EndTime` |
| `entindex` | `Entindex` | `EntIndex` |
| `entindex_attacker` | `EntindexAttacker` | `EntIndexAttacker` |
| `entindex_inflictor` | `EntindexInflictor` | `EntIndexInflictor` |
| `entindex_killed` | `EntindexKilled` | `EntIndexKilled` |
| `entityid` | `Entityid` | `EntityId` |
| `entityname` | `Entityname` | `EntityName` |
| `fadein` | `Fadein` | `FadeIn` |
| `forceupload` | `Forceupload` | `ForceUpload` |
| `fraglimit` | `Fraglimit` | `FragLimit` |
| `globalname` | `Globalname` | `GlobalName` |
| `hasbomb` | `Hasbomb` | `HasBomb` |
| `haskit` | `Haskit` | `HasKit` |
| `hassilencer` | `Hassilencer` | `HasSilencer` |
| `hastracers` | `Hastracers` | `HasTracers` |
| `hint_activator_userid` | `HintActivatorUserid` | `HintActivatorUserId` |
| `hint_entindex` | `HintEntindex` | `HintEntIndex` |
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
| `m_BlockID` | `BlockID` | `BlockId` |
| `m_BodygroupOnOtherModels` | `BodygroupOnOtherModels` | `BodyGroupOnOtherModels` |
| `m_CHitboxComponent` | `CHitboxComponent` | `CHitBoxComponent` |
| `m_CallMethodID` | `CallMethodID` | `CallMethodId` |
| `m_DestinationFlowNodeID` | `DestinationFlowNodeID` | `DestinationFlowNodeId` |
| `m_HitboxSetName` | `HitboxSetName` | `HitBoxSetName` |
| `m_IDSets` | `IDSets` | `IdSets` |
| `m_IDValues` | `IDValues` | `IdValues` |
| `m_MinimapVerticalSectionHeights` | `MinimapVerticalSectionHeights` | `MiniMapVerticalSectionHeights` |
| `m_NodeID` | `NodeID` | `NodeId` |
| `m_OutflowID` | `OutflowID` | `OutflowId` |
| `m_PanelID` | `PanelID` | `PanelId` |
| `m_SourceFilename` | `SourceFilename` | `SourceFileName` |
| `m_additiveBaseFilename` | `AdditiveBaseFilename` | `AdditiveBaseFileName` |
| `m_alignmentBoneID` | `AlignmentBoneID` | `AlignmentBoneId` |
| `m_areaEnteredTimestamp` | `AreaEnteredTimestamp` | `AreaEnteredTimeStamp` |
| `m_arrForceSubtickMoveWhen` | `ArrForceSubtickMoveWhen` | `ArrForceSubTickMoveWhen` |
| `m_attachToBoneID` | `AttachToBoneID` | `AttachToBoneId` |
| `m_attackedTimestamp` | `AttackedTimestamp` | `AttackedTimeStamp` |
| `m_avoidTimestamp` | `AvoidTimestamp` | `AvoidTimeStamp` |
| `m_bApplyLayerMatchIDToModel` | `ApplyLayerMatchIDToModel` | `ApplyLayerMatchIdToModel` |
| `m_bFullbright` | `Fullbright` | `FullBright` |
| `m_bHasTonemapParams` | `HasTonemapParams` | `HasToneMapParams` |
| `m_bIsBoneID` | `IsBoneID` | `IsBoneId` |
| `m_bLegacyWorldspace` | `LegacyWorldspace` | `LegacyWorldSpace` |
| `m_bMaintainHitbox` | `MaintainHitbox` | `MaintainHitBox` |
| `m_bMatchOnlySpecificMarkerID` | `MatchOnlySpecificMarkerID` | `MatchOnlySpecificMarkerId` |
| `m_bRoundEndShowTimerDefend` | `RoundEndShowTimerDefend` | `RoundEndShowTimerDefEnd` |
| `m_bSetRopeSegmentID` | `SetRopeSegmentID` | `SetRopeSegmentId` |
| `m_bSetToEndpoint` | `SetToEndpoint` | `SetToEndPoint` |
| `m_bShouldDrawHitboxes` | `ShouldDrawHitboxes` | `ShouldDrawHitBoxes` |
| `m_bShouldHitboxesFallbackToCollisionHulls` | `ShouldHitboxesFallbackToCollisionHulls` | `ShouldHitBoxesFallbackToCollisionHulls` |
| `m_bShouldHitboxesFallbackToRenderBounds` | `ShouldHitboxesFallbackToRenderBounds` | `ShouldHitBoxesFallbackToRenderBounds` |
| `m_bShouldHitboxesFallbackToSnapshot` | `ShouldHitboxesFallbackToSnapshot` | `ShouldHitBoxesFallbackToSnapshot` |
| `m_bSortBySegmentID` | `SortBySegmentID` | `SortBySegmentId` |
| `m_bUseClosestPointOnHitbox` | `UseClosestPointOnHitbox` | `UseClosestPointOnHitBox` |
| `m_bUseHitboxes` | `UseHitboxes` | `UseHitBoxes` |
| `m_bUseHitboxesForRenderBox` | `UseHitboxesForRenderBox` | `UseHitBoxesForRenderBox` |
| `m_blendParamID` | `BlendParamID` | `BlendParamId` |
| `m_bombsiteCenterA` | `BombsiteCenterA` | `BombSiteCenterA` |
| `m_bombsiteCenterB` | `BombsiteCenterB` | `BombSiteCenterB` |
| `m_boneID` | `BoneID` | `BoneId` |
| `m_boneMaskID` | `BoneMaskID` | `BoneMaskId` |
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
| `m_endEffectorBoneID` | `EndEffectorBoneID` | `EndEffectorBoneId` |
| `m_endStateID` | `EndStateID` | `EndStateId` |
| `m_enemyDeathTimestamp` | `EnemyDeathTimestamp` | `EnemyDeathTimeStamp` |
| `m_entryStateID` | `EntryStateID` | `EntryStateId` |
| `m_enumParamID` | `EnumParamID` | `EnumParamId` |
| `m_eventID` | `EventID` | `EventId` |
| `m_fLifetimeMax` | `LifetimeMax` | `LifeTimeMax` |
| `m_fLifetimeMin` | `LifetimeMin` | `LifeTimeMin` |
| `m_fLifetimeRandExponent` | `LifetimeRandExponent` | `LifeTimeRandExponent` |
| `m_fallbackTargetPositionParamID` | `FallbackTargetPositionParamID` | `FallbackTargetPositionParamId` |
| `m_fireWeaponTimestamp` | `FireWeaponTimestamp` | `FireWeaponTimeStamp` |
| `m_firstSawEnemyTimestamp` | `FirstSawEnemyTimestamp` | `FirstSawEnemyTimeStamp` |
| `m_flClientHealthFadeChangeTimestamp` | `ClientHealthFadeChangeTimestamp` | `ClientHealthFadeChangeTimeStamp` |
| `m_flCurrentGustLifetime` | `CurrentGustLifetime` | `CurrentGustLifeTime` |
| `m_flDealtDamageToEnemyMostRecentTimestamp` | `DealtDamageToEnemyMostRecentTimestamp` | `DealtDamageToEnemyMostRecentTimeStamp` |
| `m_flDesiredTimescale` | `DesiredTimescale` | `DesiredTimeScale` |
| `m_flHitboxFireScale` | `HitboxFireScale` | `HitBoxFireScale` |
| `m_flHitboxVelocityScale` | `HitboxVelocityScale` | `HitBoxVelocityScale` |
| `m_flLifetime` | `Lifetime` | `LifeTime` |
| `m_flMaxspeed` | `Maxspeed` | `MaxSpeed` |
| `m_flTonemapEVSmoothingRange` | `TonemapEVSmoothingRange` | `ToneMapEVSmoothingRange` |
| `m_flViewmodelFOV` | `ViewmodelFOV` | `ViewModelFOV` |
| `m_flViewmodelOffsetX` | `ViewmodelOffsetX` | `ViewModelOffsetX` |
| `m_flViewmodelOffsetY` | `ViewmodelOffsetY` | `ViewModelOffsetY` |
| `m_flViewmodelOffsetZ` | `ViewmodelOffsetZ` | `ViewModelOffsetZ` |
| `m_flWeaponGameplayAnimStateTimestamp` | `WeaponGameplayAnimStateTimestamp` | `WeaponGameplayAnimStateTimeStamp` |
| `m_flZoomCooldownTimestamp` | `ZoomCooldownTimestamp` | `ZoomCooldownTimeStamp` |
| `m_followTimestamp` | `FollowTimestamp` | `FollowTimeStamp` |
| `m_forceWorldGroupID` | `ForceWorldGroupID` | `ForceWorldGroupId` |
| `m_forceupdate` | `Forceupdate` | `ForceUpdate` |
| `m_friendDeathTimestamp` | `FriendDeathTimestamp` | `FriendDeathTimeStamp` |
| `m_fromNodeID` | `FromNodeID` | `FromNodeId` |
| `m_globalstate` | `Globalstate` | `GlobalState` |
| `m_groundActionDirectionID` | `GroundActionDirectionID` | `GroundActionDirectionId` |
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
| `m_inhibitLookAroundTimestamp` | `InhibitLookAroundTimestamp` | `InhibitLookAroundTimeStamp` |
| `m_inputNodeID` | `InputNodeID` | `InputNodeId` |
| `m_inputPinID` | `InputPinID` | `InputPinId` |
| `m_iszAchievementEventID` | `AchievementEventID` | `AchievementEventId` |
| `m_jumpTimestamp` | `JumpTimestamp` | `JumpTimeStamp` |
| `m_lastRadioRecievedTimestamp` | `LastRadioRecievedTimestamp` | `LastRadioRecievedTimeStamp` |
| `m_lastRadioSentTimestamp` | `LastRadioSentTimestamp` | `LastRadioSentTimeStamp` |
| `m_lastSawEnemyTimestamp` | `LastSawEnemyTimestamp` | `LastSawEnemyTimeStamp` |
| `m_lastVictimID` | `LastVictimID` | `LastVictimId` |
| `m_leftEffectorBoneID` | `LeftEffectorBoneID` | `LeftEffectorBoneId` |
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
| `m_markerIDToMatch` | `MarkerIDToMatch` | `MarkerIdToMatch` |
| `m_maskID` | `MaskID` | `MaskId` |
| `m_matchID` | `MatchID` | `MatchId` |
| `m_mixgroup` | `Mixgroup` | `MixGroup` |
| `m_moveDirectionID` | `MoveDirectionID` | `MoveDirectionId` |
| `m_moveHeadingParamID` | `MoveHeadingParamID` | `MoveHeadingParamId` |
| `m_msQueuedModeDisconnectionTimestamp` | `MsQueuedModeDisconnectionTimestamp` | `MsQueuedModeDisconnectionTimeStamp` |
| `m_nActorID` | `ActorID` | `ActorId` |
| `m_nCTsAliveAtFreezetimeEnd` | `CTsAliveAtFreezetimeEnd` | `CTsAliveAtFreezeTimeEnd` |
| `m_nChildGroupID` | `ChildGroupID` | `ChildGroupId` |
| `m_nClipmapLevels` | `ClipmapLevels` | `ClipMapLevels` |
| `m_nCompileTimestamp` | `CompileTimestamp` | `CompileTimeStamp` |
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
| `m_nInstanceID` | `InstanceID` | `InstanceId` |
| `m_nKeychainDefID` | `KeychainDefID` | `KeychainDefId` |
| `m_nLastMatchTime_MatchID64` | `LastMatchTimeMatchID64` | `LastMatchTimeMatchId64` |
| `m_nLightmapGameVersionNumber` | `LightmapGameVersionNumber` | `LightMapGameVersionNumber` |
| `m_nLightmapVersionNumber` | `LightmapVersionNumber` | `LightMapVersionNumber` |
| `m_nModelID` | `ModelID` | `ModelId` |
| `m_nNextMapInMapgroup` | `NextMapInMapgroup` | `NextMapInMapGroup` |
| `m_nNodeID` | `NodeID` | `NodeId` |
| `m_nObjectID` | `ObjectID` | `ObjectId` |
| `m_nOutputSubmix` | `OutputSubmix` | `OutputSubMix` |
| `m_nSplitscreenFlags` | `SplitscreenFlags` | `SplitScreenFlags` |
| `m_nSubclassID` | `SubclassID` | `SubclassId` |
| `m_nTerroristsAliveAtFreezetimeEnd` | `TerroristsAliveAtFreezetimeEnd` | `TerroristsAliveAtFreezeTimeEnd` |
| `m_nTintID` | `TintID` | `TintId` |
| `m_nUniqueID` | `UniqueID` | `UniqueId` |
| `m_nValueNodeID` | `ValueNodeID` | `ValueNodeId` |
| `m_nWorldGroupID` | `WorldGroupID` | `WorldGroupId` |
| `m_namespace` | `Namespace` | `NameSpace` |
| `m_netlookupFilename` | `NetlookupFilename` | `NetlookupFileName` |
| `m_nextCleanupCheckTimestamp` | `NextCleanupCheckTimestamp` | `NextCleanupCheckTimeStamp` |
| `m_nodeID` | `NodeID` | `NodeId` |
| `m_noiseTimestamp` | `NoiseTimestamp` | `NoiseTimeStamp` |
| `m_optionalID` | `OptionalID` | `OptionalId` |
| `m_outputID` | `OutputID` | `OutputId` |
| `m_outputNodeID` | `OutputNodeID` | `OutputNodeId` |
| `m_outputPinID` | `OutputPinID` | `OutputPinId` |
| `m_overrideMaskID` | `OverrideMaskID` | `OverrideMaskId` |
| `m_pCPPClassname` | `CPPClassname` | `CPPClassName` |
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
| `m_spotCheckTimestamp` | `SpotCheckTimestamp` | `SpotCheckTimeStamp` |
| `m_startStateID` | `StartStateID` | `StartStateId` |
| `m_stateID` | `StateID` | `StateId` |
| `m_stateTimestamp` | `StateTimestamp` | `StateTimeStamp` |
| `m_steamID` | `SteamID` | `SteamId` |
| `m_stencilTestID` | `StencilTestID` | `StencilTestId` |
| `m_stencilWriteID` | `StencilWriteID` | `StencilWriteId` |
| `m_strNametagString` | `NametagString` | `NameTagString` |
| `m_strParentPathUniqueID` | `ParentPathUniqueID` | `ParentPathUniqueId` |
| `m_strTriggerID` | `TriggerID` | `TriggerId` |
| `m_stuckTimestamp` | `StuckTimestamp` | `StuckTimeStamp` |
| `m_subGraphFilename` | `SubGraphFilename` | `SubGraphFileName` |
| `m_syncID` | `SyncID` | `SyncId` |
| `m_szClanTeamname` | `ClanTeamname` | `ClanTeamName` |
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
| `m_ullLocalMatchID` | `UllLocalMatchID` | `UllLocalMatchId` |
| `m_ullRegisteredAsItemID` | `UllRegisteredAsItemID` | `UllRegisteredAsItemId` |
| `m_unAccountID` | `AccountID` | `AccountId` |
| `m_unFreezetimeEndEquipmentValue` | `FreezetimeEndEquipmentValue` | `FreezeTimeEndEquipmentValue` |
| `m_unMusicID` | `MusicID` | `MusicId` |
| `m_unTraceID` | `TraceID` | `TraceId` |
| `m_updateID` | `UpdateID` | `UpdateId` |
| `m_vLightmapUvScale` | `LightmapUvScale` | `LightMapUvScale` |
| `m_vMinimapMaxs` | `MinimapMaxs` | `MiniMapMaxs` |
| `m_vMinimapMins` | `MinimapMins` | `MiniMapMins` |
| `m_variationID` | `VariationID` | `VariationId` |
| `m_vecSubtreeDetailLayers` | `SubtreeDetailLayers` | `SubTreeDetailLayers` |
| `m_voiceEndTimestamp` | `VoiceEndTimestamp` | `VoiceEndTimeStamp` |
| `mapgroup` | `Mapgroup` | `MapGroup` |
| `mapname` | `Mapname` | `MapName` |
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
| `playerid` | `Playerid` | `PlayerId` |
| `roundslimit` | `Roundslimit` | `RoundsLimit` |
| `show_timer_defend` | `ShowTimerDefend` | `ShowTimerDefEnd` |
| `skirmishmode` | `Skirmishmode` | `SkirmishMode` |
| `splitscreenplayer` | `Splitscreenplayer` | `SplitScreenPlayer` |
| `starttime` | `Starttime` | `StartTime` |
| `steamID` | `SteamID` | `SteamId` |
| `steamid` | `Steamid` | `SteamId` |
| `teamid` | `Teamid` | `TeamId` |
| `teamname` | `Teamname` | `TeamName` |
| `thrusmoke` | `Thrusmoke` | `ThruSmoke` |
| `timelimit` | `Timelimit` | `TimeLimit` |
| `userid` | `Userid` | `UserId` |
| `victimid` | `Victimid` | `VictimId` |
| `votedata` | `Votedata` | `VoteData` |
| `weapon_itemid` | `WeaponItemid` | `WeaponItemId` |
| `weptype` | `Weptype` | `WepType` |

## Renamed types

| Was | Now |
|---|---|
| `AnimComponentID` | `AnimComponentId` |
| `AnimNodeID` | `AnimNodeId` |
| `AnimNodeOutputID` | `AnimNodeOutputId` |
| `AnimParamID` | `AnimParamId` |
| `AnimStateID` | `AnimStateId` |
| `AnimTagID` | `AnimTagId` |
| `CCSGOEndOfMatchLineupEndpoint` | `CCSGOEndOfMatchLineupEndPoint` |
| `CCSGOPreviewModelAliasCsgoItemPreviewmodel` | `CCSGOPreviewModelAliasCsgoItemPreviewModel` |
| `CCSGOPreviewPlayerAliasCsgoPlayerPreviewmodel` | `CCSGOPreviewPlayerAliasCsgoPlayerPreviewModel` |
| `CCSMinimapBoundary` | `CCSMiniMapBoundary` |
| `CDSPMixgroupModifier` | `CDSPMixGroupModifier` |
| `CDSPPresetMixgroupModifierTable` | `CDSPPresetMixGroupModifierTable` |
| `CFuncTimescale` | `CFuncTimeScale` |
| `CHitboxComponent` | `CHitBoxComponent` |
| `CINITInitialVelocityFromHitbox` | `CINITInitialVelocityFromHitBox` |
| `CINITSetHitboxToClosest` | `CINITSetHitBoxToClosest` |
| `CINITSetHitboxToModel` | `CINITSetHitBoxToModel` |
| `CItemHealthshot` | `CItemHealthShot` |
| `CModelConfigElementSetBodygroup` | `CModelConfigElementSetBodyGroup` |
| `CModelConfigElementSetBodygroupOnAttachedModels` | `CModelConfigElementSetBodyGroupOnAttachedModels` |
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
| `COPMoveToHitbox` | `COPMoveToHitBox` |
| `COPRemapAverageHitboxSpeedtoCP` | `COPRemapAverageHitBoxSpeedtoCP` |
| `CPulseCellInflowBaseEntrypoint` | `CPulseCellInflowBaseEntryPoint` |
| `CSosGroupActionSetSoundeventParameterSchema` | `CSosGroupActionSetSoundEventParameterSchema` |
| `CSosGroupActionSoundeventClusterSchema` | `CSosGroupActionSoundEventClusterSchema` |
| `CSosGroupActionSoundeventCountSchema` | `CSosGroupActionSoundEventCountSchema` |
| `CSosGroupActionSoundeventMinMaxValuesSchema` | `CSosGroupActionSoundEventMinMaxValuesSchema` |
| `CSosGroupActionSoundeventPrioritySchema` | `CSosGroupActionSoundEventPrioritySchema` |
| `CSWeaponNameID` | `CSWeaponNameId` |
| `CTeamplayRules` | `CTeamPlayRules` |
| `CTonemapController2` | `CToneMapController2` |
| `CTonemapController2AliasEnvTonemapController2` | `CToneMapController2AliasEnvToneMapController2` |
| `CTonemapTrigger` | `CToneMapTrigger` |
| `EndmatchCmmStartRevealItemsEvent` | `EndMatchCmmStartRevealItemsEvent` |
| `EndmatchMapvoteSelectingMapEvent` | `EndMatchMapVoteSelectingMapEvent` |
| `Fieldtype` | `FieldType` |
| `GameNewmapCoreEvent` | `GameNewMapCoreEvent` |
| `GameNewmapEvent` | `GameNewMapEvent` |
| `Globalentity` | `GlobalEntity` |
| `Globalentitydatabase` | `GlobalEntityDatabase` |
| `HitboxLerpType` | `HitBoxLerpType` |
| `HltvVersioninfoEvent` | `HltvVersionInfoEvent` |
| `HostnameChangedEvent` | `HostNameChangedEvent` |
| `Levellist` | `LevelList` |
| `ModelHitboxType` | `ModelHitBoxType` |
| `NextlevelChangedEvent` | `NextLevelChangedEvent` |
| `ParticleHitboxBiasType` | `ParticleHitBoxBiasType` |
| `ParticleHitboxDataSelection` | `ParticleHitBoxDataSelection` |
| `PlayerChangenameEvent` | `PlayerChangeNameEvent` |
| `PlayerHintmessageEvent` | `PlayerHintMessageEvent` |
| `PostProcessingTonemapParameters` | `PostProcessingToneMapParameters` |
| `PulseCursorID` | `PulseCursorId` |
| `PulseDocNodeID` | `PulseDocNodeId` |
| `PulseGraphInstanceID` | `PulseGraphInstanceId` |
| `PulseRuntimeEntrypointIndex` | `PulseRuntimeEntryPointIndex` |
| `Screenfade` | `ScreenFade` |
| `Screenshake` | `ScreenShake` |
| `SmokegrenadeDetonateEvent` | `SmokeGrenadeDetonateEvent` |
| `SmokegrenadeExpiredEvent` | `SmokeGrenadeExpiredEvent` |
| `SoundeventPathCornerPairNetworked` | `SoundEventPathCornerPairNetworked` |
| `Soundlevel` | `SoundLevel` |
| `StartHalftimeEvent` | `StartHalfTimeEvent` |
| `TeamchangePendingEvent` | `TeamChangePendingEvent` |
| `TeamplayBroadcastAudioEvent` | `TeamPlayBroadcastAudioEvent` |
| `TeamplayRoundStartEvent` | `TeamPlayRoundStartEvent` |
| `VMixGraphCommandID` | `VMixGraphCommandId` |
