# Migrating to CS2OpenDev.Sdk 5.0

**1,923 properties across 820 classes change type.** Nothing is renamed, nothing moves
namespace, no type is added or removed. Every change is the same change: a property that was typed
as an empty stub class is now typed as what it actually holds.

Measured against CS2 build 24701871. Regenerate the inventory with:

```
dotnet run --configuration Release --project src/CS2OpenDev.Sdk.Exporter
```

## Why 5.0

Schema 2.0 made an atomic type's `name` fully templated — `CUtlVector< CGlobalSymbol >` where 1.x
carried a bare `CUtlVector` plus a separate `inner`. Every classification set in the generator's
`TypeMapper` was keyed on the bare name, and none of them was updated. From 2.0 until now, **no
templated atomic matched any classification branch**, so all of them fell through to the
unresolved path and were emitted as empty stub classes named after the mangled instantiation.

That is why `CCSPlayerPawn.m_hOwnerEntity` was typed `CHandle__CBaseEntity__` — a class with no
members — instead of `CHandle<BaseEntity>`, and why a `CUtlVector< CUtlString >` field was an
opaque stub rather than `string[]`.

The `CHandle<T>` and `CStrongHandle<T>` value structs the generator emits were referenced by
**zero** generated properties as a result. `docs/HANDLES.md` and the handle-family spec described a
projection that never happened on any field.

| | Before | After |
|---|---|---|
| Properties typed as a mangled stub | 1,923 | 8 |
| `CHandle<T>`-typed properties | 0 | 403 |
| Stub classes in `Stubs.cs` | 770 | 12 |
| `CS2_GEN_003` diagnostics per regen | 770 | 11 |
| `CS2_GEN_015` diagnostics per regen | 10 | **0** |

The 8 remaining stub-typed properties and 11 remaining unknown atomics are genuinely unclassified
types — `CUtlDict<T>`, `HPulseCell<T>`, `RnSphere_t` and similar — that were previously invisible
inside the 770. They are a curation backlog, not a bug: each needs a deliberate decision about what
it should project to.

## What you have to do

**Most consumers: nothing.** The old types were empty classes with no members, so no code could
have read a value out of one. If you never referenced a `*__*` type by name, your code compiles
unchanged and starts doing something useful.

DemoViewer.NET measured its own exposure at zero before this landed — its `CS2OpenDev.Sdk` usage is
`SchemaNames.*` constants plus event records, neither of which this touches.

**If you did reference a stub type by name**, replace it with the real type. The index below is by
declaring class, because that is how the audit actually gets done: you grep your code for the class
names you use, then check what moved under them.

**If you stored one in a variable or field**, the declaration changes and the value becomes usable:

```csharp
// before — an empty class, so this was as far as you could get
CHandle__CBaseEntity__ owner = pawn.OwnerEntity;

// after
CHandle<BaseEntity> owner = pawn.OwnerEntity;
uint raw = owner.Value;          // the packed handle
bool set = owner.IsValid;
```

## The shapes that changed

Every change falls into one of these:

| Count | Was | Now |
|---|---|---|
| 140 | `CHandle<…>` | `CHandle<CBaseEntity>` |
| 113 | `CUtlVector<…>` | `string[]` |
| 76 | `CStrongHandle<…>` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| 70 | `CUtlVector<…>` | `float[]` |
| 57 | `CAnimGraph2ParamOptionalRef<…>` | `float?` |
| 38 | `CResourceNameTyped<…>` | `string` |
| 37 | `CUtlVector<…>` | `int[]` |
| 37 | `CAnimGraph2ParamOptionalRef<…>` | `string?` |
| 36 | `CHandle<…>` | `CHandle<C_BaseEntity>` |
| 34 | `CStrongHandle<…>` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| 30 | `CEntityOutputTemplate<…>` | `float?` |
| 27 | `CAnimNetVar<…>` | `float` |
| 26 | `CEntityOutputTemplate<…>` | `string?` |
| 24 | `CUtlVector<…>` | `uint[]` |

…and 716 further shapes with fewer occurrences.

## What did not change

- **No renames.** Every property keeps its name; only its type moves.
- **No namespace moves.**
- **`[NativeName]`, `[NativeOffset]`, `[NativeSize]` are untouched.** They carry the native
  identity, which is independent of the C# projection.
- **`SchemaNames` is untouched.** It maps C# property names to native field names, and no property
  name moved.
- **Game events are untouched.** `CS2OpenDev.Sdk.GameEvents` decodes by native KV1 key at runtime.
- **The entity read contract is untouched.** `CS2OpenDev.Sdk.Entities.Abstractions` passes handles
  across the seam as a raw `uint` regardless of whether `CHandle<T>` exists on schema classes —
  that decision rested on keeping the contract BCL-only and on the handle bit split being
  unspecified, and both reasons survive this repair.

## Why it took three majors to notice

It was reported the whole time. `CS2_GEN_003` named every affected type on every regen — 770 lines
at `Info` severity, which is a volume nobody reads.

The guard written for exactly this class of break sits one level too high. `SchemaModel.ParseType`
carries this comment, about the 1.x → 2.0 case flip:

> A silent 100% degradation is worse than a crash — which is how the 1.x → 2.0 case flip actually
> presented — so the tests assert zero unknown **categories** on real input.

The category is `atomic`, and it stayed known. The **name** inside it is what stopped resolving.
The test passed.

Two things changed so the next one presents as a failure rather than a log:

- **`CS2_GEN_015` is now `Error` severity and fires zero times.** It reports an atomic that
  upstream's own `atomicCategory` calls a container while `TypeMapper` stubs it — so a future
  schema major that changes the name shape again trips it immediately.
- **The exporter now exits non-zero when any error-severity diagnostic reaches the sink.** Until
  now, severity on a reported diagnostic was decoration: only descriptors that threw at their own
  site stopped anything. Reporting an error and then succeeding is the same failure one level up.

## Index of changed properties, by declaring class

### `AnimationDecodeDebugDump`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `Elems` | `CUtlVector__AnimationDecodeDebugDumpElement_t__` | `AnimationDecodeDebugDumpElement[]` |

### `AnimationDecodeDebugDumpElement`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `DecodeOps` | `CUtlVector__CUtlString__` | `string[]` |
| `DecodedAnims` | `CUtlVector__CUtlString__` | `string[]` |
| `InternalOps` | `CUtlVector__CUtlString__` | `string[]` |
| `PoseParams` | `CUtlVector__CUtlString__` | `string[]` |

### `AnimationSnapshotBase`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `BoneSetUpMask` | `CUtlVector__uint32__` | `uint[]` |
| `BoneTransforms` | `CUtlVector__matrix3x4a_t__` | `Matrix3x4a[]` |
| `FlexControllers` | `CUtlVector__float32__` | `float[]` |

### `CAnimData`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `AnimArray` | `CUtlVector__CAnimDesc__` | `CAnimDesc[]` |
| `DecoderArray` | `CUtlVector__CAnimDecoder__` | `CAnimDecoder[]` |
| `SegmentArray` | `CUtlVector__CAnimFrameSegment__` | `CAnimFrameSegment[]` |

### `CAnimDataChannelDesc`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ElementIndexArray` | `CUtlVector__int32__` | `int[]` |
| `ElementMaskArray` | `CUtlVector__uint32__` | `uint[]` |
| `ElementNameArray` | `CUtlVector__CBufferString__` | `string[]` |

### `CAnimDesc`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ActivityArray` | `CUtlVector__CAnimActivity__` | `CAnimActivity[]` |
| `BoneWorldMax` | `CUtlVector__Vector__` | `Vector[]` |
| `BoneWorldMin` | `CUtlVector__Vector__` | `Vector[]` |
| `EventArray` | `CUtlVector__CAnimEventDefinition__` | `CAnimEventDefinition[]` |
| `HierarchyArray` | `CUtlVector__CAnimLocalHierarchy__` | `CAnimLocalHierarchy[]` |
| `MovementArray` | `CUtlVector__CAnimMovement__` | `CAnimMovement[]` |

### `CAnimEncodeDifference`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `BoneArray` | `CUtlVector__CAnimBoneDifference__` | `CAnimBoneDifference[]` |
| `HasMorphBitArray` | `CUtlVector__uint8__` | `byte[]` |
| `HasMovementBitArray` | `CUtlVector__uint8__` | `byte[]` |
| `HasRotationBitArray` | `CUtlVector__uint8__` | `byte[]` |
| `HasUserBitArray` | `CUtlVector__uint8__` | `byte[]` |
| `MorphArray` | `CUtlVector__CAnimMorphDifference__` | `CAnimMorphDifference[]` |
| `UserArray` | `CUtlVector__CAnimUserDifference__` | `CAnimUserDifference[]` |

### `CAnimEncodedFrames`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `FrameBlockArray` | `CUtlVector__CAnimFrameBlockAnim__` | `CAnimFrameBlockAnim[]` |

### `CAnimFrameBlockAnim`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `SegmentIndexArray` | `CUtlVector__int32__` | `int[]` |

### `CAnimKeyData`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `BoneArray` | `CUtlVector__CAnimBone__` | `CAnimBone[]` |
| `DataChannelArray` | `CUtlVector__CAnimDataChannelDesc__` | `CAnimDataChannelDesc[]` |
| `MorphArray` | `CUtlVector__CBufferString__` | `string[]` |
| `UserArray` | `CUtlVector__CAnimUser__` | `CAnimUser[]` |

### `CAnimationGroup`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `AdditionalExtRefs` | `CUtlVector__CStrongHandleVoid__` | `CStrongHandleVoid[]` |
| `DirectHSeqGroupHandle` | `CStrongHandle__InfoForResourceTypeCSequenceGroupData__` | `CStrongHandle<InfoForResourceTypeCSequenceGroupData>` |
| `IncludedGroupArrayHandle` | `CUtlVector__CStrongHandle__InfoForResourceTypeCAnimationGroup____` | `CStrongHandle<InfoForResourceTypeCAnimationGroup>[]` |
| `LocalHAnimArrayHandle` | `CUtlVector__CStrongHandle__InfoForResourceTypeCAnimData____` | `CStrongHandle<InfoForResourceTypeCAnimData>[]` |
| `Scripts` | `CUtlVector__CBufferString__` | `string[]` |

### `CMoodVData`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `AnimationLayers` | `CUtlVector__MoodAnimationLayer_t__` | `MoodAnimationLayer[]` |
| `SModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CSeqBoneMaskList`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `BoneWeightArray` | `CUtlVector__float32__` | `float[]` |
| `LocalBoneArray` | `CUtlVector__int16__` | `short[]` |
| `MorphCtrlWeightArray` | `CUtlVector__std_pair__CBufferString__float32____` | `(string, float)[]` |

### `CSeqCmdSeqDesc`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ActivityArray` | `CUtlVector__CAnimActivity__` | `CAnimActivity[]` |
| `CmdLayerArray` | `CUtlVector__CSeqCmdLayer__` | `CSeqCmdLayer[]` |
| `EventArray` | `CUtlVector__CAnimEventDefinition__` | `CAnimEventDefinition[]` |
| `PoseSettingArray` | `CUtlVector__CSeqPoseSetting__` | `CSeqPoseSetting[]` |

### `CSeqMultiFetch`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `LocalReferenceArray` | `CUtlVector__int16__` | `short[]` |
| `PoseKeyArray0` | `CUtlVector__float32__` | `float[]` |
| `PoseKeyArray1` | `CUtlVector__float32__` | `float[]` |

### `CSeqS1SeqDesc`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ActivityArray` | `CUtlVector__CAnimActivity__` | `CAnimActivity[]` |
| `AutoLayerArray` | `CUtlVector__CSeqAutoLayer__` | `CSeqAutoLayer[]` |
| `FootMotion` | `CUtlVector__CFootMotion__` | `CFootMotion[]` |
| `IKLockArray` | `CUtlVector__CSeqIKLock__` | `CSeqIKLock[]` |

### `CSeqScaleSet`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `BoneScaleArray` | `CUtlVector__float32__` | `float[]` |
| `LocalBoneArray` | `CUtlVector__int16__` | `short[]` |

### `CSeqSynthAnimDesc`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ActivityArray` | `CUtlVector__CAnimActivity__` | `CAnimActivity[]` |

### `CSequenceGroupData`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `LocalBoneMaskArray` | `CUtlVector__CSeqBoneMaskList__` | `CSeqBoneMaskList[]` |
| `LocalBoneNameArray` | `CUtlVector__CBufferString__` | `string[]` |
| `LocalCmdSeqDescArray` | `CUtlVector__CSeqCmdSeqDesc__` | `CSeqCmdSeqDesc[]` |
| `LocalIKAutoPlayLockArray` | `CUtlVector__CSeqIKLock__` | `CSeqIKLock[]` |
| `LocalMultiSeqDescArray` | `CUtlVector__CSeqS1SeqDesc__` | `CSeqS1SeqDesc[]` |
| `LocalPoseParamArray` | `CUtlVector__CSeqPoseParamDesc__` | `CSeqPoseParamDesc[]` |
| `LocalS1SeqDescArray` | `CUtlVector__CSeqS1SeqDesc__` | `CSeqS1SeqDesc[]` |
| `LocalScaleSetArray` | `CUtlVector__CSeqScaleSet__` | `CSeqScaleSet[]` |
| `LocalSequenceNameArray` | `CUtlVector__CBufferString__` | `string[]` |
| `LocalSynthAnimDescArray` | `CUtlVector__CSeqSynthAnimDesc__` | `CSeqSynthAnimDesc[]` |

### `MoodAnimationLayer`  <sub>Animationsystem</sub>

| Property | Was | Now |
|---|---|---|
| `LayerAnimations` | `CUtlVector__MoodAnimation_t__` | `MoodAnimation[]` |

### `CNmBlendSpace1D`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Points` | `CUtlVector__CNmBlendSpace1D_Point_t__` | `CNmBlendSpace1DPoint[]` |

### `CNmBlendSpace2D`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `HullIndices` | `CUtlVector__uint8__` | `byte[]` |
| `Indices` | `CUtlVector__uint8__` | `byte[]` |
| `PointNames` | `CUtlVector__CUtlString__` | `string[]` |
| `Points` | `CUtlVector__Vector2D__` | `Vector2D[]` |

### `CNmClipDocEventTrack`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Events` | `CUtlVector__CNmClipDocEvent___` | `CNmClipDocEvent?[]` |

### `CNmClipDocument`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `BonesToSampleInModelSpace` | `CUtlVector__CUtlString__` | `string[]` |
| `EventTracks` | `CUtlLeanVector__CNmClipDocEventTrack__` | `CNmClipDocEventTrack[]` |
| `SecondaryAnimationSkeletonNames` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocBoneMaskSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Options` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocClipNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `GraphEvents` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocDataDictionary`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `IdSets` | `CUtlVector__CNmGraphDocDataDictionary_IDSet_t__` | `CNmGraphDocDataDictionaryIdSet[]` |
| `ParameterSets` | `CUtlVector__CNmGraphDocDataDictionary_ParameterSet_t__` | `CNmGraphDocDataDictionaryParameterSet[]` |

### `CNmGraphDocDataDictionaryIdSet`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `GraphIDs` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocDataDictionaryParameter`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ExpectedValues` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocDataDictionaryParameterSet`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Parameters` | `CUtlVector__CNmGraphDocDataDictionary_Parameter_t__` | `CNmGraphDocDataDictionaryParameter[]` |

### `CNmGraphDocEntryStateOverrideConditionsNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `PinToStateMapping` | `CUtlVector__V_uuid_t__` | `Guid[]` |

### `CNmGraphDocFloatSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Options` | `CUtlVector__CNmGraphDocFloatSelectorNode_Option_t__` | `CNmGraphDocFloatSelectorNodeOption[]` |

### `CNmGraphDocFlowGraph`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Connections` | `CUtlVector__CNmGraphDocFlowGraph_Connection_t__` | `CNmGraphDocFlowGraphConnection[]` |

### `CNmGraphDocFlowNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `InputPins` | `CUtlLeanVectorFixedGrowable__NmGraphDocPin_t__4__` | `NmGraphDocPin[]` |
| `OutputPins` | `CUtlLeanVectorFixedGrowable__NmGraphDocPin_t__1__` | `NmGraphDocPin[]` |

### `CNmGraphDocGraph`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Nodes` | `CUtlVector__CNmGraphDocNode___` | `CNmGraphDocNode?[]` |

### `CNmGraphDocGraphEventConditionNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Conditions` | `CUtlVector__CNmGraphDocGraphEventConditionNode_Condition_t__` | `CNmGraphDocGraphEventConditionNodeCondition[]` |

### `CNmGraphDocIdBasedClipSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocIdBasedSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocIdComparisonNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Values` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocIdControlParameterNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ExpectedValues` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocIdEventConditionNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `EventIDs` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocIdSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Options` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocIdToFloatNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Mappings` | `CUtlVector__CNmGraphDocIDToFloatNode_Mapping_t__` | `CNmGraphDocIdToFloatNodeMapping[]` |

### `CNmGraphDocParameterizedClipSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocParameterizedClipSelectorNodeCData`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionWeights` | `CUtlVector__uint8__` | `byte[]` |

### `CNmGraphDocParameterizedSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocParameterizedSelectorNodeCData`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionWeights` | `CUtlVector__uint8__` | `byte[]` |

### `CNmGraphDocSelectorBaseNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocStateNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `EntryEvents` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `Events` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `ExecuteEvents` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `ExitEvents` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `StateEvents` | `CUtlVector__CNmGraphDocStateNode_StateEvent_t__` | `CNmGraphDocStateNodeStateEvent[]` |
| `TimeElapsedEvents` | `CUtlVector__CNmGraphDocStateNode_TimedStateEvent_t__` | `CNmGraphDocStateNodeTimedStateEvent[]` |
| `TimeRemainingEvents` | `CUtlVector__CNmGraphDocStateNode_TimedStateEvent_t__` | `CNmGraphDocStateNodeTimedStateEvent[]` |
| `TimedStateEvents` | `CUtlVector__CNmGraphDocStateNode_TimedStateEvent_t__` | `CNmGraphDocStateNodeTimedStateEvent[]` |

### `CNmGraphDocTargetSelectorNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionLabels` | `CUtlVector__CUtlString__` | `string[]` |

### `CNmGraphDocVariationDataNode`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Overrides` | `CUtlVector__CNmGraphDocVariationDataNode_OverrideValue_t__` | `CNmGraphDocVariationDataNodeOverrideValue[]` |

### `CNmGraphDocVariationIdComparisonNodeCData`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Values` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CNmGraphDocument`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `DebugParameterSets` | `CUtlLeanVector__CNmGraphDocument_DebugParameterSet_t__` | `CNmGraphDocumentDebugParameterSet[]` |
| `DictionaryIdSetIDs` | `CUtlVector__V_uuid_t__` | `Guid[]` |

### `CNmGraphDocumentDebugParameterSet`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `BoolValues` | `CUtlLeanVector__std_pair__CGlobalSymbol__bool____` | `(string, bool)[]` |
| `FloatValues` | `CUtlLeanVector__std_pair__CGlobalSymbol__float32____` | `(string, float)[]` |
| `IdValues` | `CUtlLeanVector__std_pair__CGlobalSymbol__CGlobalSymbol____` | `(string, string)[]` |
| `TargetValues` | `CUtlLeanVector__std_pair__CGlobalSymbol__CNmTarget____` | `(string, CNmTarget)[]` |
| `VectorValues` | `CUtlLeanVector__std_pair__CGlobalSymbol__Vector____` | `(string, Vector)[]` |

### `CNmPreviewArchetype`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `SecondarySkeletonSettings` | `CUtlVector__CNmPreviewArchetype_SecondarySkeleton_t__` | `CNmPreviewArchetypeSecondarySkeleton[]` |

### `CNmSkeletonDocument`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneMaskSetDefinitions` | `CUtlVector__NmBoneMaskSetDefinition_t__` | `NmBoneMaskSetDefinition[]` |
| `FloatChannelSets` | `CUtlVector__CNmFloatChannelSet_t__` | `CNmFloatChannelSet[]` |
| `GameplayRelevantBones` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `HighLODBones` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `SecondarySkeletons` | `CUtlVector__CNmSkeletonDocument_SecondarySkeleton_t__` | `CNmSkeletonDocumentSecondarySkeleton[]` |

### `CNmVariationHierarchy`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Variations` | `CUtlVector__NmVariation_t__` | `NmVariation[]` |

### `CnmGraphDocChainLookatNodeCData`  <sub>Animdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ChainWeights` | `CUtlVector__float32__` | `float[]` |

### `CActionComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Action____` | `CAnimGraphDocAction?[]` |

### `CAnimGraphDocAimCameraNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `PropJoints` | `CUtlVector__CAnimGraphDoc_AimCameraNode_PropJoint__` | `CAnimGraphDocAimCameraNodePropJoint[]` |

### `CAnimGraphDocBlend2DNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Blend2DItem____` | `CAnimGraphDocBlend2DItem?[]` |
| `ParamSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_ParamSpan____` | `CAnimGraphDocParamSpan?[]` |
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocBlendNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CBlendNodeChild__` | `CBlendNodeChild[]` |

### `CAnimGraphDocChoiceNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CChoiceNodeChild__` | `CChoiceNodeChild[]` |

### `CAnimGraphDocClipData`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocClipDataManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ItemTable` | `CUtlHashtable__CUtlString__CSmartPtr__CAnimGraphDoc_ClipData____` | `Dictionary<string, CAnimGraphDocClipData?>` |

### `CAnimGraphDocComponentManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Components` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Component____` | `CAnimGraphDocComponent?[]` |

### `CAnimGraphDocConditionContainer`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Conditions` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Condition____` | `CAnimGraphDocCondition?[]` |

### `CAnimGraphDocConflictManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Conflicts` | `CUtlVector__CSmartPtr__CAnimConflictBase____` | `CAnimConflictBase?[]` |

### `CAnimGraphDocContainerNodeBase`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `InputConnectionMap` | `CUtlHashtable__AnimNodeOutputID__CAnimGraphDoc_NodeConnection__` | `Dictionary<AnimNodeOutputId, CAnimGraphDocNodeConnection>` |

### `CAnimGraphDocCycleControlClipNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocFootAdjustmentNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Clips` | `CUtlVector__CUtlString__` | `string[]` |

### `CAnimGraphDocFootCycleMetric`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Feet` | `CUtlVector__CUtlString__` | `string[]` |

### `CAnimGraphDocFootLockNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CFootLockItem__` | `CFootLockItem[]` |

### `CAnimGraphDocFootPinningNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CFootPinningItem__` | `CFootPinningItem[]` |

### `CAnimGraphDocFootPositionMetric`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Feet` | `CUtlVector__CUtlString__` | `string[]` |

### `CAnimGraphDocFootStepTriggerNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CFootStepTriggerItem__` | `CFootStepTriggerItem[]` |

### `CAnimGraphDocGraph`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `SettingsManager` | `CSmartPtr__CAnimGraphSettingsManager__` | `CAnimGraphSettingsManager?` |

### `CAnimGraphDocJiggleBoneNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CJiggleBoneItem__` | `CJiggleBoneItem[]` |

### `CAnimGraphDocMotionItem`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `BlockSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |
| `ParamSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_ParamSpan____` | `CAnimGraphDocParamSpan?[]` |
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocMotionItemGroup`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Motions` | `CUtlVector__CSmartPtr__CAnimGraphDoc_MotionItem____` | `CAnimGraphDocMotionItem?[]` |

### `CAnimGraphDocMotionMatchingNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Groups` | `CUtlVector__CSmartPtr__CAnimGraphDoc_MotionItemGroup____` | `CAnimGraphDocMotionItemGroup?[]` |
| `Metrics` | `CUtlVector__CSmartPtr__CAnimGraphDoc_MotionMetric____` | `CAnimGraphDocMotionMetric?[]` |

### `CAnimGraphDocMotionParameterManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Params` | `CUtlVector__CSmartPtr__CAnimGraphDoc_MotionParameter____` | `CAnimGraphDocMotionParameter?[]` |

### `CAnimGraphDocNodeList`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Nodes` | `CUtlVector__CAnimGraphDoc_Node___` | `CAnimGraphDocNode?[]` |

### `CAnimGraphDocNodeManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Nodes` | `CUtlHashtable__AnimNodeID__CSmartPtr__CAnimGraphDoc_Node____` | `Dictionary<AnimNodeId, CAnimGraphDocNode?>` |

### `CAnimGraphDocParamSpan`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Samples` | `CUtlVector__CAnimGraphDoc_ParamSpanSample__` | `CAnimGraphDocParamSpanSample[]` |

### `CAnimGraphDocParameterManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Parameters` | `CUtlVector__CSmartPtr__CAnimParameterBase____` | `CAnimParameterBase?[]` |

### `CAnimGraphDocPathMetric`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `PathSamples` | `CUtlVector__float32__` | `float[]` |

### `CAnimGraphDocPlayerInputMotor`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `SampleTimes` | `CUtlVector__float32__` | `float[]` |

### `CAnimGraphDocProxyNodeBase`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ProxyItems` | `CUtlVector__CConnectionProxyItem__` | `CConnectionProxyItem[]` |

### `CAnimGraphDocRigidBodyWeightList`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Weights` | `CUtlVector__CRigidBodyWeight__` | `CRigidBodyWeight[]` |

### `CAnimGraphDocSelectorNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CAnimGraphDoc_NodeConnection__` | `CAnimGraphDocNodeConnection[]` |
| `Tags` | `CUtlVector__AnimTagID__` | `AnimTagId[]` |

### `CAnimGraphDocSequenceBlend2DItem`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocSequenceNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ParamSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_ParamSpan____` | `CAnimGraphDocParamSpan?[]` |
| `TagSpans` | `CUtlVector__CSmartPtr__CAnimGraphDoc_TagSpan____` | `CAnimGraphDocTagSpan?[]` |

### `CAnimGraphDocSingleFrameNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Action____` | `CAnimGraphDocAction?[]` |

### `CAnimGraphDocSolveIKChainNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `IkChains` | `CUtlVector__CSolveIKChainAnimNodeChainData__` | `CSolveIKChainAnimNodeChainData[]` |

### `CAnimGraphDocState`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CStateAction__` | `CStateAction[]` |
| `Transitions` | `CUtlVector__CSmartPtr__CAnimGraphDoc_StateTransition____` | `CAnimGraphDocStateTransition?[]` |

### `CAnimGraphDocStateList`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `States` | `CUtlVector__CAnimGraphDoc_State___` | `CAnimGraphDocState?[]` |

### `CAnimGraphDocStateMachine`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `States` | `CUtlVector__CSmartPtr__CAnimGraphDoc_State____` | `CAnimGraphDocState?[]` |

### `CAnimGraphDocStepsRemainingMetric`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Feet` | `CUtlVector__CUtlString__` | `string[]` |

### `CAnimGraphDocSubGraph`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `LocalParameters` | `CUtlVector__CSmartPtr__CAnimParameterBase____` | `CAnimParameterBase?[]` |
| `LocalTags` | `CUtlVector__CSmartPtr__CAnimTagBase____` | `CAnimTagBase?[]` |
| `ReferencedParamGroups` | `CUtlVector__CUtlString__` | `string[]` |
| `ReferencedTagGroups` | `CUtlVector__CUtlString__` | `string[]` |

### `CAnimGraphDocSubGraphNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `AnimNameMap` | `CUtlHashtable__CUtlString__CUtlString__` | `Dictionary<string, string>` |

### `CAnimGraphDocTagManager`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__CSmartPtr__CAnimTagBase____` | `CAnimTagBase?[]` |

### `CAnimGraphDocTargetSelectorNode`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CTargetSelectorChild__` | `CTargetSelectorChild[]` |

### `CCPPScriptComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `ScriptsToRun` | `CUtlVector__CUtlString__` | `string[]` |

### `CDampedValueComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CDampedValueItem__` | `CDampedValueItem[]` |

### `CFootStepTriggerItem`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `TagIDs` | `CUtlVector__AnimTagID__` | `AnimTagId[]` |
| `TagNames` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CMovementComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Motors` | `CUtlVector__CSmartPtr__CAnimGraphDoc_Motor____` | `CAnimGraphDocMotor?[]` |

### `CRagdollComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `WeightLists` | `CUtlVector__CAnimGraphDoc_RigidBodyWeightList__` | `CAnimGraphDocRigidBodyWeightList[]` |

### `CRemapValueComponent`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CRemapValueItem__` | `CRemapValueItem[]` |

### `CStateAction`  <sub>Animgraphdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CSmartPtr__CAnimGraphDoc_Action__` | `CAnimGraphDocAction?` |

### `AimCameraOpFixedSettings`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `PropJoints` | `CUtlVector__int32__` | `int[]` |

### `BlendItem`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CActionComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CSmartPtr__CAnimActionUpdater____` | `CAnimActionUpdater?[]` |

### `CAnimDemoCaptureSettings`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Bones` | `CUtlVector__BoneDemoCaptureSettings_t__` | `BoneDemoCaptureSettings[]` |
| `IkChains` | `CUtlVector__IKDemoCaptureSettings_t__` | `IKDemoCaptureSettings[]` |

### `CAnimGraphDebugReplay`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FrameList` | `CUtlVector__CSmartPtr__CAnimReplayFrame____` | `CAnimReplayFrame?[]` |

### `CAnimGraphModelBinding`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `SharedData` | `CSmartPtr__CAnimUpdateSharedData__` | `CAnimUpdateSharedData?` |

### `CAnimGraphSettingsManager`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `SettingsGroups` | `CUtlVector__CSmartPtr__CAnimGraphSettingsGroup____` | `CAnimGraphSettingsGroup?[]` |

### `CAnimParamHandleMap`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `List` | `CUtlHashtable__uint16__int16__` | `Dictionary<ushort, short>` |

### `CAnimParameterManagerUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `AutoResetMap` | `CUtlHashtable__CAnimParamHandle__int16__` | `Dictionary<CAnimParamHandle, short>` |
| `AutoResetParams` | `CUtlVector__std_pair__CAnimParamHandle__CAnimVariant____` | `(CAnimParamHandle, object?)[]` |
| `IdToIndexMap` | `CUtlHashtable__AnimParamID__int32__` | `Dictionary<AnimParamId, int>` |
| `IndexToHandle` | `CUtlVector__CAnimParamHandle__` | `CAnimParamHandle[]` |
| `NameToIndexMap` | `CUtlHashtable__CUtlString__int32__` | `Dictionary<string, int>` |
| `Parameters` | `CUtlVector__CSmartPtr__CAnimParameterBase____` | `CAnimParameterBase?[]` |

### `CAnimReplayFrame`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `InputDataBlocks` | `CUtlVector__CUtlBinaryBlock__` | `byte[][]` |

### `CAnimScriptManager`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ScriptInfo` | `CUtlVector__ScriptInfo_t__` | `ScriptInfo[]` |

### `CAnimStateMachineUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `States` | `CUtlVector__CStateUpdateData__` | `CStateUpdateData[]` |
| `Transitions` | `CUtlVector__CTransitionUpdateData__` | `CTransitionUpdateData[]` |

### `CAnimTagManagerUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__CSmartPtr__CAnimTagBase____` | `CAnimTagBase?[]` |

### `CAnimUpdateSharedData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Components` | `CUtlVector__CSmartPtr__CAnimComponentUpdater____` | `CAnimComponentUpdater?[]` |
| `NodeIndexMap` | `CUtlHashtable__CAnimNodePath__int32__` | `Dictionary<CAnimNodePath, int>` |
| `Nodes` | `CUtlVector__CSmartPtr__CAnimUpdateNodeBase____` | `CAnimUpdateNodeBase?[]` |
| `ParamListUpdater` | `CSmartPtr__CAnimParameterManagerUpdater__` | `CAnimParameterManagerUpdater?` |
| `ScriptManager` | `CSmartPtr__CAnimScriptManager__` | `CAnimScriptManager?` |
| `Skeleton` | `CSmartPtr__CAnimSkeleton__` | `CAnimSkeleton?` |
| `StaticPoseCache` | `CSmartPtr__CStaticPoseCacheBuilder__` | `CStaticPoseCacheBuilder?` |
| `TagManagerUpdater` | `CSmartPtr__CAnimTagManagerUpdater__` | `CAnimTagManagerUpdater?` |

### `CAnimationLayer`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Cycle` | `CAnimNetVar__float32__` | `float` |
| `Order` | `CAnimNetVar__int32__` | `int` |
| `Sequence` | `CAnimNetVar__int32__` | `int` |
| `Weight` | `CAnimNetVar__float32__` | `float` |

### `CBlend2DUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__BlendItem_t__` | `BlendItem[]` |
| `NodeItemIndices` | `CUtlVector__int32__` | `int[]` |
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CBlendNodeInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendValue` | `CAnimNetVar__float32__` | `float` |
| `ResetCount` | `CAnimNetVar__uint8__` | `byte` |

### `CBlendUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CAnimUpdateNodeRef__` | `CAnimUpdateNodeRef[]` |
| `SortedOrder` | `CUtlVector__uint8__` | `byte[]` |
| `TargetValues` | `CUtlVector__float32__` | `float[]` |

### `CBodyGroupAnimTag`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BodyGroupSettings` | `CUtlVector__CBodyGroupSetting__` | `CBodyGroupSetting[]` |

### `CCPPScriptComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ScriptsToRun` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CCachedPose`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `MorphWeights` | `CUtlVector__float32__` | `float[]` |
| `Transforms` | `CUtlVector__CTransform__` | `CTransform[]` |

### `CChoiceInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ClipStartTime` | `CAnimNetVar__float32__` | `float` |
| `CurrentChoice` | `CAnimNetVar__int32__` | `int` |

### `CChoiceUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendTimes` | `CUtlVector__float32__` | `float[]` |
| `Children` | `CUtlVector__CAnimUpdateNodeRef__` | `CAnimUpdateNodeRef[]` |
| `Weights` | `CUtlVector__float32__` | `float[]` |

### `CCycleClipInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Cycle` | `CAnimNetVar__float32__` | `float` |
| `PrevCycle` | `CAnimNetVar__float32__` | `float` |

### `CCycleControlClipUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CDampedValueComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CDampedValueUpdateItem__` | `CDampedValueUpdateItem[]` |

### `CDirectPlaybackInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentSequenceData` | `CAnimNetVar__uint64__` | `ulong` |
| `ForcedCycle` | `CAnimNetVar__float32__` | `float` |
| `SequenceCycleZeroTime` | `CAnimNetVar__float32__` | `float` |

### `CDirectPlaybackTagData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CDirectPlaybackUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `AllTags` | `CUtlVector__CDirectPlaybackTagData__` | `CDirectPlaybackTagData[]` |

### `CDirectionalBlendInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CycleZeroTime` | `CAnimNetVar__float32__` | `float` |
| `PlaybackRate` | `CAnimNetVar__float32__` | `float` |
| `ResetCount` | `CAnimNetVar__float32__` | `float` |
| `ResetCycleValue` | `CAnimNetVar__float32__` | `float` |

### `CEnumAnimParameter`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `EnumOptions` | `CUtlVector__CUtlString__` | `string[]` |
| `EnumReferenced` | `CUtlVector__uint64__` | `ulong[]` |

### `CFollowPathInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `PredictionScale` | `CAnimNetVar__float32__` | `float` |
| `XLastPredictedTransformsDeltas` | `CRelativeArray__CMotionTransform__` | `byte[]?[]` |

### `CFootAdjustmentInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Duration` | `CAnimNetVar__float32__` | `float` |
| `StartTime` | `CAnimNetVar__float32__` | `float` |

### `CFootAdjustmentUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Clips` | `CUtlVector__HSequence__` | `HSequence[]` |

### `CFootCycleMetricEvaluator`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootIndices` | `CUtlVector__int32__` | `int[]` |

### `CFootLockUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootSettings` | `CUtlVector__FootFixedSettings__` | `FootFixedSettings[]` |

### `CFootPinningUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Params` | `CUtlVector__CAnimParamHandle__` | `CAnimParamHandle[]` |

### `CFootPositionMetricEvaluator`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootIndices` | `CUtlVector__int32__` | `int[]` |

### `CFootStepTriggerUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Triggers` | `CUtlVector__FootStepTrigger__` | `FootStepTrigger[]` |

### `CMotionDataSet`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Groups` | `CUtlVector__CMotionGraphGroup__` | `CMotionGraphGroup[]` |

### `CMotionGraph`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `RootNode` | `CSmartPtr__CMotionNode__` | `CMotionNode?` |
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CMotionGraphGroup`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `MotionGraphConfigs` | `CUtlVector__CMotionGraphConfig__` | `CMotionGraphConfig[]` |
| `MotionGraphs` | `CUtlVector__CSmartPtr__CMotionGraph____` | `CMotionGraph?[]` |
| `SampleToConfig` | `CUtlVector__int32__` | `int[]` |

### `CMotionGraphUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `MotionGraph` | `CSmartPtr__CMotionGraph__` | `CMotionGraph?` |

### `CMotionMatchingUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Metrics` | `CUtlVector__CSmartPtr__CMotionMetricEvaluator____` | `CMotionMetricEvaluator?[]` |
| `Weights` | `CUtlVector__float32__` | `float[]` |

### `CMotionMetricEvaluator`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Means` | `CUtlVector__float32__` | `float[]` |
| `StandardDeviations` | `CUtlVector__float32__` | `float[]` |

### `CMotionNodeBlend1D`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendItems` | `CUtlVector__MotionBlendItem__` | `MotionBlendItem[]` |

### `CMotionNodeSequence`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CMotionSearchDB`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CodeIndices` | `CUtlVector__MotionDBIndex__` | `MotionDBIndex[]` |

### `CMotionSearchNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CMotionSearchNode___` | `CMotionSearchNode?[]` |
| `SampleCodes` | `CUtlVector__CUtlVector__SampleCode____` | `SampleCode[][]` |
| `SampleIndices` | `CUtlVector__CUtlVector__int32____` | `int[][]` |
| `SelectableSamples` | `CUtlVector__int32__` | `int[]` |

### `CMovementComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Motors` | `CUtlVector__CSmartPtr__CAnimMotorUpdaterBase____` | `CAnimMotorUpdaterBase?[]` |

### `CNetworkedCycle`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CycleZeroTime` | `CAnimNetVar__float32__` | `float` |
| `CyclesPerSecond` | `CAnimNetVar__float32__` | `float` |
| `ResetCount` | `CAnimNetVar__uint8__` | `byte` |

### `CParamSpanUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Spans` | `CUtlVector__ParamSpan_t__` | `ParamSpan[]` |

### `CParticleAnimTag`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ParticleSystem` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `CPathMetricEvaluator`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `PathTimeSamples` | `CUtlVector__float32__` | `float[]` |

### `CPlayerInputAnimMotorUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `SampleTimes` | `CUtlVector__float32__` | `float[]` |

### `CProductQuantizer`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `SubQuantizers` | `CUtlVector__CVectorQuantizer__` | `CVectorQuantizer[]` |

### `CRagdollComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneIndices` | `CUtlVector__int32__` | `int[]` |
| `BoneNames` | `CUtlVector__CUtlString__` | `string[]` |
| `BoneToWeightIndices` | `CUtlVector__int32__` | `int[]` |
| `FollowAttachmentNodePaths` | `CUtlVector__CAnimNodePath__` | `CAnimNodePath[]` |
| `RagdollNodePaths` | `CUtlVector__CAnimNodePath__` | `CAnimNodePath[]` |
| `WeightLists` | `CUtlVector__WeightList__` | `WeightList[]` |

### `CRemapValueComponentUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Items` | `CUtlVector__CRemapValueUpdateItem__` | `CRemapValueUpdateItem[]` |

### `CSelectorUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendTime` | `CAnimValue__float32__` | `float` |
| `Children` | `CUtlVector__CAnimUpdateNodeRef__` | `CAnimUpdateNodeRef[]` |
| `Tags` | `CUtlVector__int8__` | `sbyte[]` |

### `CSequenceTagSpans`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CSequenceUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__TagSpan_t__` | `TagSpan[]` |

### `CSingleFrameUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CSmartPtr__CAnimActionUpdater____` | `CAnimActionUpdater?[]` |

### `CSolveIKChainUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `TargetHandles` | `CUtlVector__CSolveIKTargetHandle_t__` | `CSolveIKTargetHandle[]` |

### `CStanceOverrideUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootStanceInfo` | `CUtlVector__StanceInfo_t__` | `StanceInfo[]` |

### `CStateActionUpdater`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CSmartPtr__CAnimActionUpdater__` | `CAnimActionUpdater?` |

### `CStateMachineInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentTransitionIndex` | `CAnimNetVar__int32__` | `int` |

### `CStateMachineUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `StateData` | `CUtlVector__CStateNodeStateData__` | `CStateNodeStateData[]` |
| `TransitionData` | `CUtlVector__CStateNodeTransitionData__` | `CStateNodeTransitionData[]` |

### `CStateNodeInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentStateStartTime` | `CAnimNetVar__float32__` | `float` |
| `ResetCount` | `CAnimNetVar__uint8__` | `byte` |
| `StateWeights` | `CRelativeArray__float32__` | `float[]` |

### `CStateNodeTransitionData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendDuration` | `CAnimValue__float32__` | `float` |
| `ResetCycleValue` | `CAnimValue__float32__` | `float` |

### `CStateUpdateData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CStateActionUpdater__` | `CStateActionUpdater[]` |
| `TransitionIndices` | `CUtlVector__int32__` | `int[]` |

### `CStaticPoseCache`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Poses` | `CUtlVector__CCachedPose__` | `CCachedPose[]` |

### `CStepsRemainingMetricEvaluator`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootIndices` | `CUtlVector__int32__` | `int[]` |

### `CTargetSelectorUpdateNode`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CAnimUpdateNodeRef__` | `CAnimUpdateNodeRef[]` |

### `CVectorQuantizer`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CentroidVectors` | `CUtlVector__float32__` | `float[]` |

### `FootLockPoseOpFixedSettings`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootInfo` | `CUtlVector__FootFixedData_t__` | `FootFixedData[]` |

### `FootPinningPoseOpFixedData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `FootInfo` | `CUtlVector__FootFixedData_t__` | `FootFixedData[]` |

### `FootStepTrigger`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Tags` | `CUtlVector__int32__` | `int[]` |

### `JiggleBoneSettingsList`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneSettings` | `CUtlVector__JiggleBoneSettings_t__` | `JiggleBoneSettings[]` |

### `LookAtOpFixedSettings`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Bones` | `CUtlVector__LookAtBone_t__` | `LookAtBone[]` |

### `LookData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `LookTarget` | `CAnimNetVar__Vector__` | `Vector` |

### `MotionBlendItem`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Child` | `CSmartPtr__CMotionNode__` | `CMotionNode?` |

### `MotionSelection`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CycleZeroTime` | `CAnimNetVar__float32__` | `float` |
| `PlaybackSpeed` | `CAnimNetVar__float32__` | `float` |
| `StartTime` | `CAnimNetVar__float32__` | `float` |

### `MovementData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Acceleration` | `CAnimNetVar__Vector__` | `Vector` |
| `ActiveMotorIndex` | `CAnimNetVar__int32__` | `int` |
| `BoundaryRadius` | `CAnimNetVar__float32__` | `float` |
| `CurrentMoveSpeed` | `CAnimNetVar__float32__` | `float` |
| `FacingHeading` | `CAnimNetVar__float32__` | `float` |
| `FacingMode` | `CAnimNetVar__uint8__` | `byte` |
| `FacingPosition` | `CAnimNetVar__Vector__` | `Vector` |
| `ForceFacing` | `CAnimNetVar__bool__` | `bool` |
| `GoalDistance` | `CAnimNetVar__float32__` | `float` |
| `HasPath` | `CAnimNetVar__bool__` | `bool` |
| `MoveDir` | `CAnimNetVar__Vector__` | `Vector` |
| `OnGround` | `CAnimNetVar__bool__` | `bool` |
| `TargetMoveSpeed` | `CAnimNetVar__float32__` | `float` |

### `NetVarConfigIndex`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Index` | `CAnimNetVar__uint32__` | `uint` |

### `PairedSequence`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Sequence` | `CAnimNetVar__uint32__` | `uint` |

### `ParamSpan`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Samples` | `CUtlVector__ParamSpanSample_t__` | `ParamSpanSample[]` |

### `ScriptInfo`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ParamsModified` | `CUtlVector__CAnimParamHandle__` | `CAnimParamHandle[]` |
| `ProxyReadParams` | `CUtlVector__int32__` | `int[]` |
| `ProxyWriteParams` | `CUtlVector__int32__` | `int[]` |

### `SelectorInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentIndexStartTime` | `CAnimNetVar__float32__` | `float` |
| `Weights` | `CRelativeArray__float32__` | `float[]` |

### `SolveIKChainPoseOpFixedSettings`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `ChainsToSolveData` | `CUtlVector__ChainToSolveData_t__` | `ChainToSolveData[]` |

### `TargetSelectorInstanceData`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentIndex` | `CAnimNetVar__int32__` | `int` |
| `MSRootMotionAnlyzerTarget` | `CAnimNetVar__Vector__` | `Vector` |

### `WeightList`  <sub>Animgraphlib</sub>

| Property | Was | Now |
|---|---|---|
| `Weights` | `CUtlVector__float32__` | `float[]` |

### `CNmAndNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__4__` | `short[]` |

### `CNmBlend2DNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `HullIndices` | `CUtlLeanVectorFixedGrowable__uint8__10__` | `byte[]` |
| `Indices` | `CUtlLeanVectorFixedGrowable__uint8__30__` | `byte[]` |
| `SourceNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |
| `Values` | `CUtlLeanVectorFixedGrowable__Vector2D__10__` | `Vector2D[]` |

### `CNmBoneMaskSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `MaskNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |
| `ParameterValues` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__7__` | `string[]` |

### `CNmBoneWeightList`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneIDs` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `Weights` | `CUtlVector__float32__` | `float[]` |

### `CNmChainLookatNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ChainWeights` | `CUtlVectorFixedGrowable__float32__5__` | `float[]` |

### `CNmClip`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `CompressedPoseOffsets` | `CUtlVector__uint32__` | `uint[]` |
| `FloatChannelData` | `CUtlVectorFixedGrowable__CNmFloatChannelData___2__` | `CNmFloatChannelData?[]` |
| `ModelSpaceBoneSamplingIndices` | `CUtlVector__int32__` | `int[]` |
| `ModelSpaceSamplingChain` | `CUtlVector__CNmClip_ModelSpaceSamplingChainLink_t__` | `CNmClipModelSpaceSamplingChainLink[]` |
| `SecondaryAnimations` | `CUtlVectorFixedGrowable__CNmClip___1__` | `CNmClip?[]` |
| `Skeleton` | `CStrongHandle__InfoForResourceTypeCNmSkeleton__` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>` |
| `TrackCompressionSettings` | `CUtlVector__NmCompressionSettings_t__` | `NmCompressionSettings[]` |

### `CNmClipNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `GraphEvents` | `CUtlVectorFixedGrowable__CGlobalSymbol__2__` | `string[]` |

### `CNmClipSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |

### `CNmFloatChannelData`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ChannelSettings` | `CUtlVector__CNmFloatChannelData_ChannelSettings_t__` | `CNmFloatChannelDataChannelSettings[]` |
| `CompressedData` | `CUtlVector__uint16__` | `ushort[]` |
| `CompressedOffsets` | `CUtlVector__uint32__` | `uint[]` |
| `Skeleton` | `CStrongHandle__InfoForResourceTypeCNmSkeleton__` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>` |

### `CNmFloatChannelSet`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ChannelIDs` | `CUtlLeanVector__CGlobalSymbol__` | `string[]` |

### `CNmFloatSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |
| `Values` | `CUtlLeanVectorFixedGrowable__float32__5__` | `float[]` |

### `CNmGraphDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ControlParameterIDs` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `ExternalGraphSlots` | `CUtlVector__CNmGraphDefinition_ExternalGraphSlot_t__` | `CNmGraphDefinitionExternalGraphSlot[]` |
| `ExternalPoseSlots` | `CUtlVector__CNmGraphDefinition_ExternalPoseSlot_t__` | `CNmGraphDefinitionExternalPoseSlot[]` |
| `NodePaths` | `CUtlVector__CUtlString__` | `string[]` |
| `PersistentNodeIndices` | `CUtlVector__int16__` | `short[]` |
| `ReferencedGraphSlots` | `CUtlVector__CNmGraphDefinition_ReferencedGraphSlot_t__` | `CNmGraphDefinitionReferencedGraphSlot[]` |
| `Resources` | `CUtlVector__CStrongHandleVoid__` | `CStrongHandleVoid[]` |
| `Skeleton` | `CStrongHandle__InfoForResourceTypeCNmSkeleton__` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>` |
| `SupportedSecondarySkeletons` | `CUtlVector__CStrongHandle__InfoForResourceTypeCNmSkeleton____` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>[]` |
| `VirtualParameterIDs` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `VirtualParameterNodeIndices` | `CUtlVector__int16__` | `short[]` |

### `CNmGraphEventConditionNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `Conditions` | `CUtlVectorFixedGrowable__CNmGraphEventConditionNode_Condition_t__5__` | `CNmGraphEventConditionNodeCondition[]` |

### `CNmIdBasedClipSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionIDs` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__5__` | `string[]` |
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |

### `CNmIdBasedSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionIDs` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__5__` | `string[]` |
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |

### `CNmIdComparisonNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ComparisionIDs` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__4__` | `string[]` |

### `CNmIdEventConditionNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `EventIDs` | `CUtlVectorFixedGrowable__CGlobalSymbol__5__` | `string[]` |

### `CNmIdSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |
| `Values` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__5__` | `string[]` |

### `CNmIdToFloatNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `IDs` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__5__` | `string[]` |
| `Values` | `CUtlLeanVectorFixedGrowable__float32__5__` | `float[]` |

### `CNmLayerBlendNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `LayerDefinition` | `CUtlLeanVectorFixedGrowable__CNmLayerBlendNode_LayerDefinition_t__3__` | `CNmLayerBlendNodeLayerDefinition[]` |

### `CNmOrNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__4__` | `short[]` |

### `CNmParameterizedBlendNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `SourceNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__5__` | `short[]` |

### `CNmParameterizedBlendNodeParameterization`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `BlendRanges` | `CUtlLeanVectorFixedGrowable__CNmParameterizedBlendNode_BlendRange_t__5__` | `CNmParameterizedBlendNodeBlendRange[]` |

### `CNmParameterizedClipSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |
| `OptionWeights` | `CUtlLeanVectorFixedGrowable__uint8__8__` | `byte[]` |

### `CNmParameterizedSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |
| `OptionWeights` | `CUtlLeanVectorFixedGrowable__uint8__8__` | `byte[]` |

### `CNmParticleEvent`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ParticleSystem` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `CNmRootMotionData`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `Transforms` | `CUtlVector__CTransform__` | `CTransform[]` |

### `CNmSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |

### `CNmSkeleton`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneIDs` | `CUtlLeanVector__CGlobalSymbol__` | `string[]` |
| `FloatChannelSets` | `CUtlLeanVector__CNmFloatChannelSet_t__` | `CNmFloatChannelSet[]` |
| `MaskDefinitions` | `CUtlLeanVector__NmBoneMaskSetDefinition_t__` | `NmBoneMaskSetDefinition[]` |
| `ModelSpaceReferencePose` | `CUtlVector__CTransform__` | `CTransform[]` |
| `ParentIndices` | `CUtlVector__int32__` | `int[]` |
| `ParentSpaceReferencePose` | `CUtlVector__CTransform__` | `CTransform[]` |
| `SecondarySkeletons` | `CUtlLeanVector__CNmSkeleton_SecondarySkeleton_t__` | `CNmSkeletonSecondarySkeleton[]` |

### `CNmSkeletonSecondarySkeleton`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `Skeleton` | `CStrongHandle__InfoForResourceTypeCNmSkeleton__` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>` |

### `CNmStateMachineNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `StateDefinitions` | `CUtlLeanVectorFixedGrowable__CNmStateMachineNode_StateDefinition_t__5__` | `CNmStateMachineNodeStateDefinition[]` |

### `CNmStateMachineNodeStateDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `TransitionDefinitions` | `CUtlLeanVectorFixedGrowable__CNmStateMachineNode_TransitionDefinition_t__5__` | `CNmStateMachineNodeTransitionDefinition[]` |

### `CNmStateNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `EntryEvents` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__3__` | `string[]` |
| `ExecuteEvents` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__3__` | `string[]` |
| `ExitEvents` | `CUtlLeanVectorFixedGrowable__CGlobalSymbol__3__` | `string[]` |
| `TimedElapsedEvents` | `CUtlLeanVectorFixedGrowable__CNmStateNode_TimedEvent_t__1__` | `CNmStateNodeTimedEvent[]` |
| `TimedRemainingEvents` | `CUtlLeanVectorFixedGrowable__CNmStateNode_TimedEvent_t__1__` | `CNmStateNodeTimedEvent[]` |

### `CNmSyncTrack`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `SyncEvents` | `CUtlLeanVectorFixedGrowable__CNmSyncTrack_Event_t__10__` | `CNmSyncTrackEvent[]` |

### `CNmTargetSelectorNodeCDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `OptionNodeIndices` | `CUtlLeanVectorFixedGrowable__int16__8__` | `short[]` |

### `NmBoneMaskSetDefinition`  <sub>Animlib</sub>

| Property | Was | Now |
|---|---|---|
| `SecondaryWeightLists` | `CUtlLeanVector__CNmBoneWeightList__` | `CNmBoneWeightList[]` |

### `ActiveModelConfig`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AssociatedEntities` | `C_NetworkUtlVectorBase__CHandle__C_BaseModelEntity____` | `CHandle<C_BaseModelEntity>[]` |
| `AssociatedEntityNames` | `C_NetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |

### `CAttributeList`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Attributes` | `C_UtlVectorEmbeddedNetworkVar__CEconItemAttribute__` | `CEconItem[]` |

### `CAttributeManager`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CachedResults` | `CUtlVector__CAttributeManager_cached_attribute_float_t__` | `CAttributeManagerCachedAttributeFloat[]` |
| `Outer` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `Providers` | `CUtlVector__CHandle__C_BaseEntity____` | `CHandle<C_BaseEntity>[]` |

### `CBaseAnimGraph`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `OnLayerCycleUpdated` | `CEntityOutputTemplate__float32__` | `float?` |

### `CBaseAnimGraphController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ExternalClipIds` | `C_NetworkUtlVectorBase__ResourceId_t__` | `ResourceId[]` |
| `ExternalGraphIds` | `C_NetworkUtlVectorBase__ResourceId_t__` | `ResourceId[]` |
| `GraphDefinitionAG2` | `CStrongHandle__InfoForResourceTypeCNmGraphDefinition__` | `CStrongHandle<InfoForResourceTypeCNmGraphDefinition>` |
| `SecondarySkeletonSlotIDs` | `C_NetworkUtlVectorBase__CGlobalSymbol__` | `string[]` |
| `SecondarySkeletons` | `C_NetworkUtlVectorBase__CHandle__CBaseAnimGraph____` | `CHandle<CBaseAnimGraph>[]` |
| `SerializePoseRecipeAG2Dynamic` | `C_NetworkUtlVectorBase__uint8__` | `byte[]` |
| `SerializePoseRecipeAG2Slots` | `C_UtlVectorEmbeddedNetworkVar__AnimGraph2SerializedPoseRecipeSlot_t__` | `AnimGraph2SerializedPoseRecipeSlot[]` |

### `CBasePlayerController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Pawn` | `CHandle__C_BasePlayerPawn__` | `CHandle<C_BasePlayerPawn>` |
| `PredictedPawn` | `CHandle__C_BasePlayerPawn__` | `CHandle<C_BasePlayerPawn>` |
| `SplitOwner` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `SplitScreenPlayers` | `CUtlVector__CHandle__CBasePlayerController____` | `CHandle<CBasePlayerController>[]` |

### `CBasePlayerVData`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `SModelNameAg2Override` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CBasePlayerWeaponVData`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AShootSounds` | `CUtlOrderedMap__WeaponSound_t__CSoundEventName__` | `Dictionary<WeaponSound, string>` |
| `BarrelSmokeParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `MuzzleFlashParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `SToolsOnlyOwnerModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `WorldModel` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `WorldModelAg2Override` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CBulletHitModel`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PlayerParent` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CCS2PawnGraphController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AimPitchAngle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AimYawAngle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AirAction` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `AirHeightAboveGround` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `CrouchAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `FlashedAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `FlinchBody` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `FlinchBodyRestart` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `FlinchHead` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `FlinchHeadRestart` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `FlinchIsOnFire` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `GroundAction` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `GroundActionDirectionId` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `GroundTurnAngleOrVelocity` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IsDefusing` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `IsWalking` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `LadderCycle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LadderYaw` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LadderYawBackwards` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LeftFootTarget` | `CAnimGraph2ParamOptionalRef__CNmTarget__` | `CNmTarget?` |
| `MoveDirectionId` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `MoveSpeedHorizontal` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveSpeedX` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveSpeedY` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `PreviousMoveSpeedHorizontal` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `RightFootTarget` | `CAnimGraph2ParamOptionalRef__CNmTarget__` | `CNmTarget?` |
| `WeaponDropAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |

### `CCS2UIPawnGraphController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `AnimationSeed` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `BannerAnimation` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `CT` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `CharacterMode` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `CharacterModeReset` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `EndOfMatchCelebration` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `InspectTurnAngle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `TeamPreviewPosition` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `TeamPreviewRandom` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `TeamPreviewVariant` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponCategory` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponState` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |

### `CCS2WeaponGraphController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `ActionReset` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `AttackThrowStrength` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AttackType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `AttackVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `DeployVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IdleVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `InspectExtraInfo` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `InspectVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IsUsingLegacyModel` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `ReloadStage` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponActionSpeedScale` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmo` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmoMax` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmoReserve` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponCategory` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponExtraInfo` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponIronsightAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponIsSilenced` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `WeaponType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |

### `CCSObserverCameraServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PrevPostProcessingVolume` | `CHandle__C_PostProcessingVolume__` | `CHandle<C_PostProcessingVolume>` |

### `CCSPlayerActionTrackingServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `LastWeaponBeforeC4AutoSwitch` | `CHandle__C_BasePlayerWeapon__` | `CHandle<C_BasePlayerWeapon>` |

### `CCSPlayerBaseCameraServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ZoomOwner` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CCSPlayerBuyServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SellBackPurchaseEntries` | `C_UtlVectorEmbeddedNetworkVar__SellbackPurchaseEntry_t__` | `SellBackPurchaseEntry[]` |

### `CCSPlayerController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ObserverPawn` | `CHandle__C_CSObserverPawn__` | `CHandle<C_CSObserverPawn>` |
| `OriginalControllerOfCurrentPawn` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerPawn` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `CCSPlayerControllerActionTrackingServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PerRoundStats` | `C_UtlVectorEmbeddedNetworkVar__CSPerRoundStats_t__` | `CSPerRoundStats[]` |

### `CCSPlayerControllerDamageServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `DamageList` | `C_UtlVectorEmbeddedNetworkVar__CDamageRecord__` | `CDamageRecord[]` |

### `CCSPlayerControllerInventoryServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `NetworkAbleLoadout` | `CUtlVector__CCSPlayerController_InventoryServices_NetworkedLoadoutSlot_t__` | `CCSPlayerControllerInventoryServicesNetworkedLoadoutSlot[]` |
| `ServerAuthoritativeWeaponSlots` | `C_UtlVectorEmbeddedNetworkVar__ServerAuthoritativeWeaponSlot_t__` | `ServerAuthoritativeWeaponSlot[]` |

### `CCSPlayerHostageServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CarriedHostage` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `CarriedHostageProp` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CCSPlayerPingServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PlayerPing` | `CHandle__C_PlayerPing__` | `CHandle<C_PlayerPing>` |

### `CCSPlayerWeaponServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `NetworkAnimTiming` | `C_NetworkUtlVectorBase__uint8__` | `byte[]` |

### `CCSWeaponBaseVData`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AnimSkeleton` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCNmSkeleton____` | `string` |
| `TracerParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |

### `CChoreoComponent`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__C_BaseModelEntity__` | `CHandle<C_BaseModelEntity>` |

### `CDamageRecord`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PlayerControllerDamager` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerControllerRecipient` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerDamager` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |
| `PlayerRecipient` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `CDestructiblePartsComponent`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `DamageTakenByHitGroup` | `CUtlVector__uint16__` | `ushort[]` |
| `Owner` | `CHandle__C_BaseModelEntity__` | `CHandle<C_BaseModelEntity>` |

### `CEnvSoundscape`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ProxySoundscape` | `CHandle__CEnvSoundscape__` | `CHandle<CEnvSoundscape>` |

### `CFilterMultiple`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__C_BaseEntity__[]` | `CHandle<C_BaseEntity>[]` |

### `CFlashlightEffect`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `FlashlightTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `MuzzleFlashTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CFogPlayerParams`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Ctrl` | `CHandle__C_FogController__` | `CHandle<C_FogController>` |

### `CGlobalLightBase`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EnvSky` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `EnvWind` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CInfoDynamicShadowHint`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Light` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CInfoOffScreenPanoramaTexture`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AdditionalTargetEntities` | `CUtlVector__CHandle__C_BaseModelEntity____` | `CHandle<C_BaseModelEntity>[]` |
| `CSSClasses` | `C_NetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |
| `TargetEntities` | `C_NetworkUtlVectorBase__CHandle__C_BaseModelEntity____` | `CHandle<C_BaseModelEntity>[]` |

### `CLightComponent`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `LightCookie` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CMultiMeter`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `TargetC4` | `CHandle__C_PlantedC4__` | `CHandle<C_PlantedC4>` |

### `CPathNode`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Path` | `CHandle__CPathWithDynamicNodes__` | `CHandle<CPathWithDynamicNodes>` |

### `CPathWithDynamicNodes`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PathNodes` | `C_NetworkUtlVectorBase__CHandle__CPathNode____` | `CHandle<CPathNode>[]` |

### `CPlayerCameraServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ActivePostProcessingVolume` | `CHandle__C_PostProcessingVolume__` | `CHandle<C_PostProcessingVolume>` |
| `ColorCorrectionCtrl` | `CHandle__C_ColorCorrection__` | `CHandle<C_ColorCorrection>` |
| `OldFogController` | `CHandle__C_FogController__` | `CHandle<C_FogController>` |
| `PostProcessingVolumes` | `C_NetworkUtlVectorBase__CHandle__C_PostProcessingVolume____` | `CHandle<C_PostProcessingVolume>[]` |
| `ToneMapController` | `CHandle__C_TonemapController2__` | `CHandle<C_TonemapController2>` |
| `ViewEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CPlayerObserverServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ObserverTarget` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CPlayerWeaponServices`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ActiveWeapon` | `CHandle__C_BasePlayerWeapon__` | `CHandle<C_BasePlayerWeapon>` |
| `LastWeapon` | `CHandle__C_BasePlayerWeapon__` | `CHandle<C_BasePlayerWeapon>` |
| `MyWeapons` | `C_NetworkUtlVectorBase__CHandle__C_BasePlayerWeapon____` | `CHandle<C_BasePlayerWeapon>[]` |

### `CPointClientUIHUD`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CSSClasses` | `C_NetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |

### `CPointOrient`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Target` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `CPointTemplate`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CreatedSpawnGroupHandles` | `CUtlVector__uint32__` | `uint[]` |
| `OnEntitySpawned` | `CEntityOutputTemplate__CUtlVector__CEntityHandle____` | `CEntityHandle[]?` |
| `SpawnedEntityHandles` | `CUtlVector__CEntityHandle__` | `CEntityHandle[]` |

### `CPrecipitationVData`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ParticlePrecipitationEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `ParticlePrecipitationPostEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `ParticlePrecipitationPuddleEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |

### `CPulseCellLerpCameraSettingsCursorState`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Camera` | `CHandle__C_PointCamera__` | `CHandle<C_PointCamera>` |

### `CPulseCellPlaySequenceCursorState`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Target` | `CHandle__CBaseAnimGraph__` | `CHandle<CBaseAnimGraph>` |

### `CSkyboxReference`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SkyCamera` | `CHandle__C_SkyCamera__` | `CHandle<C_SkyCamera>` |

### `CTriggerFan`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `InfoFan` | `CHandle__CInfoFan__` | `CHandle<CInfoFan>` |

### `C_BarnLight`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `LightCookie` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `LightStyleEvents` | `C_NetworkUtlVectorBase__CUtlString__` | `string[]` |
| `LightStyleTargets` | `C_NetworkUtlVectorBase__CHandle__C_BaseModelEntity____` | `CHandle<C_BaseModelEntity>[]` |
| `QueuedLightStyleStrings` | `C_NetworkUtlVectorBase__CUtlString__` | `string[]` |
| `VisClusters` | `C_NetworkUtlVectorBase__uint16__` | `ushort[]` |

### `C_BaseButton`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `GlowEntity` | `CHandle__C_BaseModelEntity__` | `CHandle<C_BaseModelEntity>` |

### `C_BaseCSGrenade`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SwitchToWeaponAfterThrow` | `CHandle__C_CSWeaponBase__` | `CHandle<C_CSWeaponBase>` |

### `C_BaseCSGrenadeProjectile`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ArrTrajectoryTrailPointCreationTimes` | `CUtlVector__float32__` | `float[]` |
| `ArrTrajectoryTrailPoints` | `CUtlVector__Vector__` | `Vector[]` |
| `ExplodeEffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `SnapshotTrajectoryParticleSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |

### `C_BaseCombatCharacter`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `MyWearables` | `C_NetworkUtlVectorBase__CHandle__C_EconWearable____` | `CHandle<C_EconWearable>[]` |

### `C_BaseEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AThinkFunctions` | `CUtlVector__thinkfunc_t__` | `Thinkfunc[]` |
| `Dependencies` | `CUtlVector__CEntityHandle__` | `CEntityHandle[]` |
| `EffectEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `GroundEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `OldMoveParent` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `OwnerEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `SceneObjectController` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_BaseGrenade`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `OriginalThrower` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |
| `Thrower` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_BaseModelEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `BodyGroupChoices` | `CUtlOrderedMap__CGlobalSymbol__int32__` | `Dictionary<string, int>` |
| `RenderAttributes` | `C_UtlVectorEmbeddedNetworkVar__EntityRenderAttribute_t__` | `EntityRender[]` |

### `C_BasePlayerPawn`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Controller` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `DefaultController` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `ServerViewAngleChanges` | `C_UtlVectorEmbeddedNetworkVar__ViewAngleServerChange_t__` | `ViewAngleServerChange[]` |

### `C_BasePropDoor`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Master` | `CHandle__C_BasePropDoor__` | `CHandle<C_BasePropDoor>` |

### `C_BaseTrigger`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |
| `TouchingEntities` | `CUtlVector__CHandle__C_BaseEntity____` | `CHandle<C_BaseEntity>[]` |

### `C_Beam`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AttachEntity` | `CHandle__C_BaseEntity__[]` | `CHandle<C_BaseEntity>[]` |
| `BaseMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `EndEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `HaloIndex` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `C_BreakableProp`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Breaker` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `LastAttacker` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `OnHealthChanged` | `CEntityOutputTemplate__float32__` | `float?` |
| `PhysicsAttacker` | `CHandle__C_BasePlayerPawn__` | `CHandle<C_BasePlayerPawn>` |

### `C_CSPlayerPawn`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `BulletHitModels` | `CUtlVector__C_BulletHitModel___` | `CBulletHitModel?[]` |
| `HudModelArms` | `CHandle__C_CS2HudModelArms__` | `CHandle<CCS2HudModelArms>` |

### `C_CSPlayerPawnBase`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `OriginalController` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |

### `C_CSWeaponBase`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PrevOwner` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_Chicken`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Leader` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_EconEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AttachedModels` | `CUtlVector__C_EconEntity_AttachedModelData_t__` | `CEconEntityAttachedModelData[]` |
| `AttachedParticles` | `CUtlVector__int32__` | `int[]` |
| `OldProvidee` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `ViewModelAttachment` | `CHandle__CBaseAnimGraph__` | `CHandle<CBaseAnimGraph>` |

### `C_EntityFlame`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EntAttached` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `OldAttached` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_EnvCombinedLightProbeVolume`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightIndicesTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightScalarsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightShadowsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureAmbientCube` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSDF` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2B` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2DC` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2G` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2R` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_EnvCubemap`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_EnvCubemapFog`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `FogCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `SkyMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `C_EnvDecal`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `DecalMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `C_EnvLightProbeVolume`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHLightProbeDirectLightIndicesTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightScalarsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightShadowsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureAmbientCube` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSDF` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2B` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2DC` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2G` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2R` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_EnvParticleGlow`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `TextureOverride` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_EnvSky`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `SkyMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `SkyMaterialLightingOnly` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `C_EnvVolumetricFogController`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `FogInDirectTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_EnvWindShared`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EntOwner` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_FuncConveyor`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ConveyorModels` | `C_NetworkUtlVectorBase__CHandle__C_BaseEntity____` | `CHandle<C_BaseEntity>[]` |

### `C_FuncLadder`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Dismounts` | `CUtlVector__CHandle__C_InfoLadderDismount____` | `CHandle<C_InfoLadderDismount>[]` |

### `C_FuncMonitor`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `HTargetCamera` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_GradientFog`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `GradientFogTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `C_HandleTest`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Handle` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_Hostage`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `HostageGrabber` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |
| `Leader` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_Inferno`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `InfernoClimbingOutLinePointsSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |
| `InfernoDecalsSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |
| `InfernoFillerPointsSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |
| `InfernoOutLinePointsSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |
| `InfernoPointsSnapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |

### `C_ItemDogtags`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `KillingPlayer` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |
| `OwningPlayer` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_ParticleSystem`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ControlPointEnts` | `CHandle__C_BaseEntity__[]` | `CHandle<C_BaseEntity>[]` |
| `EffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `C_PathParticleRope`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `PathNodesColor` | `C_NetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesName` | `CUtlVector__CUtlSymbolLarge__` | `string[]` |
| `PathNodesPinEnabled` | `C_NetworkUtlVectorBase__bool__` | `bool[]` |
| `PathNodesPosition` | `C_NetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesRadiusScale` | `C_NetworkUtlVectorBase__float32__` | `float[]` |
| `PathNodesTangentIn` | `C_NetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesTangentOut` | `C_NetworkUtlVectorBase__Vector__` | `Vector[]` |

### `C_PhysMagnet`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AAttachedObjects` | `CUtlVector__CHandle__C_BaseEntity____` | `CHandle<C_BaseEntity>[]` |
| `AAttachedObjectsFromServer` | `CUtlVector__int32__` | `int[]` |

### `C_PlantedC4`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `DefuserMultiMeter` | `CHandle__C_Multimeter__` | `CHandle<CMultiMeter>` |
| `HBombDefuser` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |
| `PBombDefuser` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_PlayerPing`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PingedEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `Player` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_PointClientUIDialog`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_PointClientUIWorldPanel`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CSSClasses` | `C_NetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |

### `C_PointCommentaryNode`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ViewPosition` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_PointValueRemapper`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `OutputEntities` | `C_NetworkUtlVectorBase__CHandle__C_BaseEntity____` | `CHandle<C_BaseEntity>[]` |
| `RemapLineEnd` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `RemapLineStart` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_PostProcessingVolume`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PostSettings` | `CStrongHandle__InfoForResourceTypeCPostProcessingResource__` | `CStrongHandle<InfoForResourceTypeCPostProcessingResource>` |

### `C_RagdollProp`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ParentPhysicsBoneIndices` | `CUtlVector__int32__` | `int[]` |
| `RagAngles` | `C_NetworkUtlVectorBase__QAngle__` | `QAngle[]` |
| `RagEnabled` | `C_NetworkUtlVectorBase__bool__` | `bool[]` |
| `RagPos` | `C_NetworkUtlVectorBase__Vector__` | `Vector[]` |
| `RagdollSource` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `WorldSpaceBoneComputationOrder` | `CUtlVector__int32__` | `int[]` |

### `C_RetakeGameRules`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `BombPlanter` | `CHandle__C_CSPlayerPawn__` | `CHandle<C_CSPlayerPawn>` |

### `C_RopeKeyframe`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `EndPoint` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `LinksTouchingSomething` | `CBitVec__10__` | `byte[]` |
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `RopeMaterialModelIndex` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `StartPoint` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |

### `C_SceneEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `ActorList` | `C_NetworkUtlVectorBase__CHandle__C_BaseModelEntity____` | `CHandle<C_BaseModelEntity>[]` |
| `Owner` | `CHandle__C_BaseModelEntity__` | `CHandle<C_BaseModelEntity>` |
| `QueuedEvents` | `CUtlVector__C_SceneEntity_QueuedEvents_t__` | `CSceneEntityQueuedEvents[]` |

### `C_SmokeGrenadeProjectile`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `VoxelFrameData` | `C_NetworkUtlVectorBase__uint8__` | `byte[]` |

### `C_SoundEventEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `OnGUIDChanged` | `CEntityOutputTemplate__SndOpEventGuid_t__` | `string?` |

### `C_SoundEventPathCornerEntity`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `CornerPairsNetworked` | `C_NetworkUtlVectorBase__SoundeventPathCornerPairNetworked_t__` | `SoundEventPathCornerPairNetworked[]` |

### `C_Sprite`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `AttachedToEntity` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `SpriteMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `C_Team`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `APlayerControllers` | `C_NetworkUtlVectorBase__CHandle__CBasePlayerController____` | `CHandle<CBasePlayerController>[]` |
| `APlayers` | `C_NetworkUtlVectorBase__CHandle__C_BasePlayerPawn____` | `CHandle<C_BasePlayerPawn>[]` |

### `C_TextureBasedAnimatable`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `PositionKeys` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `RotationKeys` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `PhysicsRagdollPose`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__C_BaseEntity__` | `CHandle<C_BaseEntity>` |
| `Transforms` | `C_NetworkUtlVectorBase__CTransform__` | `CTransform[]` |

### `ShardModelDesc`  <sub>Client</sub>

| Property | Was | Now |
|---|---|---|
| `InitialPanelVertices` | `C_NetworkUtlVectorBase__Vector4D__` | `Vector4D[]` |
| `MaterialBase` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `MaterialDamageOverlay` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `PanelVertices` | `C_NetworkUtlVectorBase__Vector2D__` | `Vector2D[]` |

### `CBuoyancyHelper`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `FractionOfWheelSubmergedForWheelDrag` | `CUtlVector__float32__` | `float[]` |
| `FractionOfWheelSubmergedForWheelFriction` | `CUtlVector__float32__` | `float[]` |
| `WheelDrag` | `CUtlVector__float32__` | `float[]` |
| `WheelFrictionScales` | `CUtlVector__float32__` | `float[]` |

### `CCSGameModeRulesArmsRace`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `WeaponSequence` | `C_NetworkUtlVectorBase__CUtlString__` | `string[]` |

### `CEffectData`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `EffectIndex` | `CWeakHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CWeakHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `CExplosionTypeData`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `ParticleEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |

### `CModelState`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `BodyGroupChoices` | `C_NetworkUtlVectorBase__int32__` | `int[]` |
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `WeaponPurchaseTracker`  <sub>Common</sub>

| Property | Was | Now |
|---|---|---|
| `WeaponPurchases` | `C_UtlVectorEmbeddedNetworkVar__WeaponPurchaseCount_t__` | `WeaponPurchaseCount[]` |

### `CCompositeMaterialEditorDoc`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `Points` | `CUtlVector__CompositeMaterialEditorPoint_t__` | `CompositeMaterialEditorPoint[]` |

### `CompMatPropertyMutator`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `ConditionalMutators` | `CUtlVector__CompMatPropertyMutator_t__` | `CompMatPropertyMutator[]` |
| `Conditions` | `CUtlVector__CompMatMutatorCondition_t__` | `CompMatMutatorCondition[]` |
| `RandomRollInputVarsInputVarsToRoll` | `CUtlVector__CUtlString__` | `string[]` |
| `TexGenInstructions` | `CUtlVector__CompMatPropertyMutator_t__` | `CompMatPropertyMutator[]` |

### `CompositeMaterial`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `GeneratedTextures` | `CUtlVector__GeneratedTextureHandle_t__` | `GeneratedTextureHandle[]` |

### `CompositeMaterialAssemblyProcedure`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `CompMatIncludes` | `CUtlVector__CResourceNameTyped__CWeakHandle__InfoForResourceTypeCCompositeMaterialKit______` | `string[]` |
| `CompositeInputContainers` | `CUtlVector__CompositeMaterialInputContainer_t__` | `CompositeMaterialInputContainer[]` |
| `MatchFilters` | `CUtlVector__CompositeMaterialMatchFilter_t__` | `CompositeMaterialMatchFilter[]` |
| `PropertyMutators` | `CUtlVector__CompMatPropertyMutator_t__` | `CompMatPropertyMutator[]` |

### `CompositeMaterialEditorPoint`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `ChildModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `CompositeMaterialAssemblyProcedures` | `CUtlVector__CompositeMaterialAssemblyProcedure_t__` | `CompositeMaterialAssemblyProcedure[]` |
| `CompositeMaterials` | `CUtlVector__CompositeMaterial_t__` | `CompositeMaterial[]` |
| `ModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CompositeMaterialInputContainer`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `LooseVariables` | `CUtlVector__CompositeMaterialInputLooseVariable_t__` | `CompositeMaterialInputLooseVariable[]` |
| `SpecificContainerMaterial` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIMaterial2____` | `string` |

### `CompositeMaterialInputLooseVariable`  <sub>Compositematerialslib</sub>

| Property | Was | Now |
|---|---|---|
| `ResourceMaterial` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIMaterial2____` | `string` |
| `TextureRuntimeResourcePath` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCTextureBase____` | `string` |

### `CEntityAttributeTable`  <sub>Entity2</sub>

| Property | Was | Now |
|---|---|---|
| `Attributes` | `CUtlOrderedMap__CUtlStringTokenNoRegistration__Attribute_t__` | `Dictionary<CUtlStringTokenNoRegistration, Attribute>` |
| `Names` | `CUtlOrderedMap__CUtlStringTokenNoRegistration__CUtlString__` | `Dictionary<CUtlStringTokenNoRegistration, string>` |

### `EntityIOQueuePrioritizedEvent`  <sub>Entity2</sub>

| Property | Was | Now |
|---|---|---|
| `VariantValue` | `CVariantBase__CVariantDefaultAllocator__` | `CVariantDefaultAllocator` |

### `EmptyTestScript`  <sub>Host</sub>

| Property | Was | Now |
|---|---|---|
| `Test` | `CAnimScriptParam__float32__` | `float` |

### `CSprayedDataPreset`  <sub>Mapdoclib</sub>

| Property | Was | Now |
|---|---|---|
| `Elements` | `CUtlVector__CSprayedDataPresetElement__` | `CSprayedDataPresetElement[]` |

### `MaterialParamTexture`  <sub>Materialsystem2</sub>

| Property | Was | Now |
|---|---|---|
| `Value` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `MaterialResourceData`  <sub>Materialsystem2</sub>

| Property | Was | Now |
|---|---|---|
| `DynamicParams` | `CUtlVector__MaterialParamBuffer_t__` | `MaterialParamBuffer[]` |
| `DynamicTextureParams` | `CUtlVector__MaterialParamBuffer_t__` | `MaterialParamBuffer[]` |
| `FloatAttributes` | `CUtlVector__MaterialParamFloat_t__` | `MaterialParamFloat[]` |
| `FloatParams` | `CUtlVector__MaterialParamFloat_t__` | `MaterialParamFloat[]` |
| `IntAttributes` | `CUtlVector__MaterialParamInt_t__` | `MaterialParamInt[]` |
| `IntParams` | `CUtlVector__MaterialParamInt_t__` | `MaterialParamInt[]` |
| `RenderAttributesUsed` | `CUtlVector__CUtlString__` | `string[]` |
| `StringAttributes` | `CUtlVector__MaterialParamString_t__` | `MaterialParamString[]` |
| `TextureAttributes` | `CUtlVector__MaterialParamTexture_t__` | `MaterialParamTexture[]` |
| `TextureParams` | `CUtlVector__MaterialParamTexture_t__` | `MaterialParamTexture[]` |
| `VectorAttributes` | `CUtlVector__MaterialParamVector_t__` | `MaterialParamVector[]` |
| `VectorParams` | `CUtlVector__MaterialParamVector_t__` | `MaterialParamVector[]` |

### `CFuseProgram`  <sub>MathlibExtended</sub>

| Property | Was | Now |
|---|---|---|
| `ProgramBuffer` | `CUtlVector__uint8__` | `byte[]` |
| `VariablesRead` | `CUtlVector__FuseVariableIndex_t__` | `FuseVariableIndex[]` |
| `VariablesWritten` | `CUtlVector__FuseVariableIndex_t__` | `FuseVariableIndex[]` |

### `CFuseSymbolTable`  <sub>MathlibExtended</sub>

| Property | Was | Now |
|---|---|---|
| `ConstantMap` | `CUtlHashtable__CUtlStringToken__int32__` | `Dictionary<string, int>` |
| `Constants` | `CUtlVector__ConstantInfo_t__` | `ConstantInfo[]` |
| `FunctionMap` | `CUtlHashtable__CUtlStringToken__int32__` | `Dictionary<string, int>` |
| `Functions` | `CUtlVector__FunctionInfo_t__` | `FunctionInfo[]` |
| `VariableMap` | `CUtlHashtable__CUtlStringToken__int32__` | `Dictionary<string, int>` |
| `Variables` | `CUtlVector__VariableInfo_t__` | `VariableInfo[]` |

### `EMaterialLayer`  <sub>Met</sub>

| Property | Was | Now |
|---|---|---|
| `HiddenVariableUiNames` | `CUtlVector__std_pair__CUtlString__CUtlString____` | `(string, string)[]` |
| `VariableNames` | `CUtlVector__CUtlString__` | `string[]` |

### `EMaterialVariables`  <sub>Met</sub>

| Property | Was | Now |
|---|---|---|
| `Layers` | `CUtlVector__EMaterialLayer_t__` | `EMaterialLayer[]` |
| `Variables` | `CUtlVector__EMaterialVariable_t__` | `EMaterialVariable[]` |

### `CMotionAnalysisSettings`  <sub>ModeldocEditor</sub>

| Property | Was | Now |
|---|---|---|
| `Feet` | `CUtlStringMap__CMotionAnalysisSettings_Foot__` | `Dictionary<string, CMotionAnalysisSettingsFoot>` |

### `CMotionAnalysisSettingsFoot`  <sub>ModeldocEditor</sub>

| Property | Was | Now |
|---|---|---|
| `AnkleBoneNames` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `AttachmentNames` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CAnimSkeleton`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneNames` | `CUtlVector__CUtlString__` | `string[]` |
| `Children` | `CUtlVector__CUtlVector__int32____` | `int[][]` |
| `Feet` | `CUtlVector__CAnimFoot__` | `CAnimFoot[]` |
| `LocalSpaceTransforms` | `CUtlVector__CTransform__` | `CTransform[]` |
| `LodBoneCounts` | `CUtlVector__int32__` | `int[]` |
| `ModelSpaceTransforms` | `CUtlVector__CTransform__` | `CTransform[]` |
| `MorphNames` | `CUtlVector__CUtlString__` | `string[]` |
| `Parents` | `CUtlVector__int32__` | `int[]` |

### `CBaseConstraint`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Slaves` | `CUtlLeanVector__CConstraintSlave__` | `CConstraintSlave[]` |
| `Targets` | `CUtlVector__CConstraintTarget__` | `CConstraintTarget[]` |

### `CBoneConstraintPoseSpaceBone`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `InputList` | `CUtlVector__CBoneConstraintPoseSpaceBone_Input_t__` | `CBoneConstraintPoseSpaceBoneInput[]` |

### `CBoneConstraintPoseSpaceBoneInput`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `OutputTransformList` | `CUtlVector__CTransform__` | `CTransform[]` |

### `CBoneConstraintPoseSpaceMorph`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `InputList` | `CUtlVector__CBoneConstraintPoseSpaceMorph_Input_t__` | `CBoneConstraintPoseSpaceMorphInput[]` |
| `OutputMorph` | `CUtlVector__CUtlString__` | `string[]` |

### `CBoneConstraintPoseSpaceMorphInput`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `OutputWeightList` | `CUtlVector__float32__` | `float[]` |

### `CBoneConstraintRbf`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `InputBones` | `CUtlVector__std_pair__CUtlString__uint32____` | `(string, uint)[]` |
| `OutputBones` | `CUtlVector__std_pair__CUtlString__uint32____` | `(string, uint)[]` |

### `CFlexRule`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `FlexOps` | `CUtlVector__CFlexOp__` | `CFlexOp[]` |

### `CFootMotion`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Strides` | `CUtlVector__CFootStride__` | `CFootStride[]` |

### `CFootTrajectories`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Trajectories` | `CUtlVector__CFootTrajectory__` | `CFootTrajectory[]` |

### `CHitBoxSet`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `HitBoxes` | `CUtlVector__CHitBox__` | `CHitBox[]` |

### `CHitBoxSetList`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `HitBoxSets` | `CUtlVector__CHitBoxSet__` | `CHitBoxSet[]` |

### `CMaterialDrawDescriptor`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `RigidMeshParts` | `CUtlLeanVector__CMaterialDrawDescriptor_RigidMeshPart_t__` | `CMaterialDrawDescriptorRigidMeshPart[]` |
| `RootBvhNodes` | `CUtlLeanVector__uint16__` | `ushort[]` |

### `CModelConfig`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Elements` | `CUtlVector__CModelConfigElement___` | `CModelConfigElement?[]` |

### `CModelConfigElement`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `NestedElements` | `CUtlVector__CModelConfigElement___` | `CModelConfigElement?[]` |

### `CModelConfigElementAttachedModel`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `CModelConfigElementRandomPick`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `ChoiceWeights` | `CUtlVector__float32__` | `float[]` |
| `Choices` | `CUtlVector__CUtlString__` | `string[]` |

### `CModelConfigElementUserPick`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Choices` | `CUtlVector__CUtlString__` | `string[]` |

### `CModelConfigList`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Configs` | `CUtlVector__CModelConfig___` | `CModelConfig?[]` |

### `CMorphBundleData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Offsets` | `CUtlVector__float32__` | `float[]` |
| `Ranges` | `CUtlVector__float32__` | `float[]` |

### `CMorphData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `MorphRectDatas` | `CUtlVector__CMorphRectData__` | `CMorphRectData[]` |

### `CMorphRectData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BundleDatas` | `CUtlVector__CMorphBundleData__` | `CMorphBundleData[]` |

### `CMorphSetData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BundleTypes` | `CUtlVector__MorphBundleType_t__` | `MorphBundleType[]` |
| `FlexControllers` | `CUtlVector__CFlexController__` | `CFlexController[]` |
| `FlexDesc` | `CUtlVector__CFlexDesc__` | `CFlexDesc[]` |
| `FlexRules` | `CUtlVector__CFlexRule__` | `CFlexRule[]` |
| `MorphDatas` | `CUtlVector__CMorphData__` | `CMorphData[]` |
| `TextureAtlas` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CRenderGroom`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `HairPositionOffsets` | `CUtlVector__uint32__` | `uint[]` |
| `Hairs` | `CUtlVector__RenderHairStrandInfo_t__` | `RenderHairStrandInfo[]` |
| `SimParamsMat` | `CStrongHandleCopyable__InfoForResourceTypeIMaterial2__` | `CStrongHandleCopyable<InfoForResourceTypeIMaterial2>` |
| `StrandSegmentCountHist` | `CUtlVector__int32__` | `int[]` |

### `CRenderMesh`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Constraints` | `CUtlLeanVector__CBaseConstraint___` | `CBaseConstraint?[]` |
| `SceneObjects` | `CUtlLeanVectorFixedGrowable__CSceneObjectData__1__` | `CSceneObjectData[]` |

### `CRenderSkeleton`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneParents` | `CUtlVector__int32__` | `int[]` |
| `Bones` | `CUtlVector__RenderSkeletonBone_t__` | `RenderSkeletonBone[]` |

### `CSceneObjectData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `DrawBounds` | `CUtlLeanVector__AABB_t__` | `AABB[]` |
| `DrawCalls` | `CUtlLeanVector__CMaterialDrawDescriptor__` | `CMaterialDrawDescriptor[]` |
| `Meshlets` | `CUtlLeanVector__CMeshletDescriptor__` | `CMeshletDescriptor[]` |
| `RtProxyDrawCalls` | `CUtlLeanVector__CSceneObjectData_RTProxyDrawDescriptor_t__` | `CSceneObjectDataRTProxyDrawDescriptor[]` |

### `CVPhysXSurfacePropertiesList`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `SurfacePropertiesList` | `CUtlVector__CPhysSurfaceProperties___` | `CPhysSurfaceProperties?[]` |

### `MaterialGroup`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Materials` | `CUtlVector__CStrongHandle__InfoForResourceTypeIMaterial2____` | `CStrongHandle<InfoForResourceTypeIMaterial2>[]` |

### `ModelAnimGraph2Ref`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Graph` | `CStrongHandle__InfoForResourceTypeCNmGraphDefinition__` | `CStrongHandle<InfoForResourceTypeCNmGraphDefinition>` |

### `ModelBoneFlexDriver`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Controls` | `CUtlVector__ModelBoneFlexDriverControl_t__` | `ModelBoneFlexDriverControl[]` |

### `ModelEmbeddedMesh`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `IndexBuffers` | `CUtlVector__ModelMeshBufferData_t__` | `ModelMeshBufferData[]` |
| `ToolsBuffers` | `CUtlVector__ModelMeshBufferData_t__` | `ModelMeshBufferData[]` |
| `VertexBuffers` | `CUtlVector__ModelMeshBufferData_t__` | `ModelMeshBufferData[]` |

### `ModelMeshBufferData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `InputLayoutFields` | `CUtlVector__RenderInputLayoutField_t__` | `RenderInputLayoutField[]` |

### `ModelSkeletonData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BoneName` | `CUtlVector__CUtlString__` | `string[]` |
| `BonePosParent` | `CUtlVector__Vector__` | `Vector[]` |
| `BoneRotParent` | `CUtlVector__QuaternionStorage__` | `QuaternionStorage[]` |
| `BoneScaleParent` | `CUtlVector__float32__` | `float[]` |
| `BoneSphere` | `CUtlVector__float32__` | `float[]` |
| `Flag` | `CUtlVector__uint32__` | `uint[]` |
| `Parent` | `CUtlVector__int16__` | `short[]` |

### `PermModelData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `AnimGraph2Refs` | `CUtlVector__ModelAnimGraph2Ref_t__` | `ModelAnimGraph2Ref[]` |
| `AnimatedMaterialAttributes` | `CUtlVector__PermModelDataAnimatedMaterialAttribute_t__` | `PermModelDataAnimatedMaterial[]` |
| `BodyGroupsHiddenInTools` | `CUtlVector__CUtlString__` | `string[]` |
| `BoneFlexDrivers` | `CUtlVector__ModelBoneFlexDriver_t__` | `ModelBoneFlexDriver[]` |
| `ExtParts` | `CUtlVector__PermModelExtPart_t__` | `PermModelExtPart[]` |
| `LodGroupSwitchDistances` | `CUtlVector__float32__` | `float[]` |
| `MaterialGroups` | `CUtlVector__MaterialGroup_t__` | `MaterialGroup[]` |
| `MeshGroups` | `CUtlVector__CUtlString__` | `string[]` |
| `NmSkeletonRefs` | `CUtlVector__CStrongHandle__InfoForResourceTypeCNmSkeleton____` | `CStrongHandle<InfoForResourceTypeCNmSkeleton>[]` |
| `RefAnimGroups` | `CUtlVector__CStrongHandle__InfoForResourceTypeCAnimationGroup____` | `CStrongHandle<InfoForResourceTypeCAnimationGroup>[]` |
| `RefAnimIncludeModels` | `CUtlVector__CStrongHandle__InfoForResourceTypeCModel____` | `CStrongHandle<InfoForResourceTypeCModel>[]` |
| `RefLODGroupMasks` | `CUtlVector__uint8__` | `byte[]` |
| `RefMeshGroupMasks` | `CUtlVector__uint64__` | `ulong[]` |
| `RefMeshes` | `CUtlVector__CStrongHandle__InfoForResourceTypeCRenderMesh____` | `CStrongHandle<InfoForResourceTypeCRenderMesh>[]` |
| `RefPhysGroupMasks` | `CUtlVector__uint64__` | `ulong[]` |
| `RefPhysicsData` | `CUtlVector__CStrongHandle__InfoForResourceTypeCPhysAggregateData____` | `CStrongHandle<InfoForResourceTypeCPhysAggregateData>[]` |
| `RefPhysicsHitBoxData` | `CUtlVector__CStrongHandle__InfoForResourceTypeCPhysAggregateData____` | `CStrongHandle<InfoForResourceTypeCPhysAggregateData>[]` |
| `RefSequenceGroups` | `CUtlVector__CStrongHandle__InfoForResourceTypeCSequenceGroupData____` | `CStrongHandle<InfoForResourceTypeCSequenceGroupData>[]` |
| `RemappingTable` | `CUtlVector__int16__` | `short[]` |
| `RemappingTableStarts` | `CUtlVector__uint16__` | `ushort[]` |

### `PermModelExtPart`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `RefModel` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `PhysSoftBodyDesc`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Capsules` | `CUtlVector__RnSoftbodyCapsule_t__` | `RnSoftBodyCapsule[]` |
| `InitPose` | `CUtlVector__CTransform__` | `CTransform[]` |
| `ParticleBoneHash` | `CUtlVector__uint32__` | `uint[]` |
| `ParticleBoneName` | `CUtlVector__CUtlString__` | `string[]` |
| `Particles` | `CUtlVector__RnSoftbodyParticle_t__` | `RnSoftBodyParticle[]` |
| `Springs` | `CUtlVector__RnSoftbodySpring_t__` | `RnSoftBodySpring[]` |

### `SkeletonAnimCapture`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `FeModelInitPose` | `CUtlVector__SkeletonAnimCapture_t_Bone_t__` | `SkeletonAnimCaptureTBone[]` |
| `Frames` | `CUtlVector__SkeletonAnimCapture_t_Frame_t__` | `SkeletonAnimCaptureTFrame[]` |
| `ImportedCollision` | `CUtlVector__CEntityIndex__` | `int[]` |
| `ModelBindPose` | `CUtlVector__SkeletonAnimCapture_t_Bone_t__` | `SkeletonAnimCaptureTBone[]` |

### `SkeletonAnimCaptureTFrame`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `CompositeBones` | `CUtlVector__CTransform__` | `CTransform[]` |
| `FeModelAnims` | `CUtlVector__CTransform__` | `CTransform[]` |
| `FeModelPos` | `CUtlVector__VectorAligned__` | `VectorAligned[]` |
| `FlexControllerWeights` | `CUtlVector__float32__` | `float[]` |
| `SimStateBones` | `CUtlVector__CTransform__` | `CTransform[]` |

### `SkeletonDemoDb`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `AnimCaptures` | `CUtlVector__SkeletonAnimCapture_t___` | `SkeletonAnimCapture?[]` |
| `CameraTrack` | `CUtlVector__SkeletonAnimCapture_t_Camera_t__` | `SkeletonAnimCaptureTCamera[]` |

### `VPhysXAggregateData`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `BindPose` | `CUtlVector__matrix3x4a_t__` | `Matrix3x4a[]` |
| `BoneNames` | `CUtlVector__CUtlString__` | `string[]` |
| `BoneParents` | `CUtlVector__uint16__` | `ushort[]` |
| `BonesHash` | `CUtlVector__uint32__` | `uint[]` |
| `CollisionAttributes` | `CUtlVector__VPhysXCollisionAttributes_t__` | `VPhysXCollisionAttributes[]` |
| `Constraints2` | `CUtlVector__VPhysXConstraint2_t__` | `VPhysXConstraint2[]` |
| `DebugPartNames` | `CUtlVector__CUtlString__` | `string[]` |
| `IndexHash` | `CUtlVector__uint16__` | `ushort[]` |
| `IndexNames` | `CUtlVector__uint16__` | `ushort[]` |
| `Joints` | `CUtlVector__VPhysXJoint_t__` | `VPhysXJoint[]` |
| `Parts` | `CUtlVector__VPhysXBodyPart_t__` | `VPhysXBodyPart[]` |
| `ShapeMarkups` | `CUtlVector__PhysShapeMarkup_t__` | `PhysShapeMarkup[]` |
| `SurfacePropertyHashes` | `CUtlVector__uint32__` | `uint[]` |

### `VPhysXCollisionAttributes`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `DetailLayerStrings` | `CUtlVector__CUtlString__` | `string[]` |
| `DetailLayers` | `CUtlVector__uint32__` | `uint[]` |
| `InteractAs` | `CUtlVector__uint32__` | `uint[]` |
| `InteractAsStrings` | `CUtlVector__CUtlString__` | `string[]` |
| `InteractExclude` | `CUtlVector__uint32__` | `uint[]` |
| `InteractExcludeStrings` | `CUtlVector__CUtlString__` | `string[]` |
| `InteractWith` | `CUtlVector__uint32__` | `uint[]` |
| `InteractWithStrings` | `CUtlVector__CUtlString__` | `string[]` |

### `VPhysics2ShapeDef`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `Capsules` | `CUtlVector__RnCapsuleDesc_t__` | `RnCapsuleDesc[]` |
| `CollisionAttributeIndices` | `CUtlVector__uint16__` | `ushort[]` |
| `Hulls` | `CUtlVector__RnHullDesc_t__` | `RnHullDesc[]` |
| `Meshes` | `CUtlVector__RnMeshDesc_t__` | `RnMeshDesc[]` |
| `Spheres` | `CUtlVector__RnSphereDesc_t__` | `RnSphereDesc[]` |

### `VsInputSignature`  <sub>Modellib</sub>

| Property | Was | Now |
|---|---|---|
| `DepthElems` | `CUtlVector__VsInputSignatureElement_t__` | `VsInputSignatureElement[]` |
| `Elems` | `CUtlVector__VsInputSignatureElement_t__` | `VsInputSignatureElement[]` |

### `CNavHullPresetVData`  <sub>Navlib</sub>

| Property | Was | Now |
|---|---|---|
| `NavHulls` | `CUtlVector__CUtlString__` | `string[]` |

### `CBaseRendererSource2`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `TexturesInput` | `CUtlLeanVector__TextureGroup_t__` | `TextureGroup[]` |

### `CINITPointList`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `PointList` | `CUtlVector__PointDefinition_t__` | `PointDefinition[]` |

### `CINITRandomModelSequence`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `CINITRandomNamedModelElement`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `Names` | `CUtlVector__CUtlString__` | `string[]` |

### `CINITRandomSequence`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `WeightedList` | `CUtlVector__SequenceWeightedList_t__` | `SequenceWeightedList[]` |

### `CINITRemapNamedModelElementToScalar`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `Names` | `CUtlVector__CUtlString__` | `string[]` |
| `Values` | `CUtlVector__float32__` | `float[]` |

### `CINITRemapParticleCountToNamedModelElementScalar`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `COPConstrainDistanceToUserSpecifiedPath`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `PointList` | `CUtlVector__PointDefinitionWithTimeValues_t__` | `PointDefinitionWithTimeValues[]` |

### `COPCreateParticleSystemRenderer`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `CPs` | `CUtlLeanVector__CPAssignment_t__` | `CPAssignment[]` |
| `Effect` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `COPLockToPointList`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `PointList` | `CUtlVector__PointDefinition_t__` | `PointDefinition[]` |

### `COPMultiSegmentDisplaySnapshotGenerator`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `SpecialCharList` | `CUtlVector__ParticleMultiSegmentSpecialCharacter_t__` | `ParticleMultiSegmentSpecialCharacter[]` |

### `COPRemapNamedModelElementEndCap`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `FallbackNames` | `CUtlVector__CUtlString__` | `string[]` |
| `InNames` | `CUtlVector__CUtlString__` | `string[]` |
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `OutNames` | `CUtlVector__CUtlString__` | `string[]` |

### `COPRemapNamedModelElementOnceTimed`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `FallbackNames` | `CUtlVector__CUtlString__` | `string[]` |
| `InNames` | `CUtlVector__CUtlString__` | `string[]` |
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `OutNames` | `CUtlVector__CUtlString__` | `string[]` |

### `COPRenderAsModels`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `ModelList` | `CUtlVector__ModelReference_t__` | `ModelReference[]` |

### `COPRenderBlobs`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `MaterialVars` | `CUtlVector__MaterialVariable_t__` | `MaterialVariable[]` |

### `COPRenderCables`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `MaterialFloatVars` | `CUtlLeanVector__FloatInputMaterialVariable_t__` | `FloatInputMaterialVariable[]` |
| `MaterialVecVars` | `CUtlLeanVector__VecInputMaterialVariable_t__` | `VecInputMaterialVariable[]` |

### `COPRenderDeferredLight`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Texture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `COPRenderGpuImplicit`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `COPRenderMaterialProxy`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialVars` | `CUtlVector__MaterialVariable_t__` | `MaterialVariable[]` |
| `OverrideMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `COPRenderModels`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialVars` | `CUtlVector__MaterialVariable_t__` | `MaterialVariable[]` |
| `ModelList` | `CUtlVector__ModelReference_t__` | `ModelReference[]` |
| `OverrideMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `COPRenderOmni2Light`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `LightCookie` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `COPRenderPoints`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `COPRenderPostProcessing`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `PostTexture` | `CStrongHandle__InfoForResourceTypeCPostProcessingResource__` | `CStrongHandle<InfoForResourceTypeCPostProcessingResource>` |

### `COPRenderProjected`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialVars` | `CUtlVector__MaterialVariable_t__` | `MaterialVariable[]` |
| `ProjectedMaterials` | `CUtlVector__RenderProjectedMaterial_t__` | `RenderProjectedMaterial[]` |

### `COPRenderSimpleModelCollection`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `COPRenderStatusEffect`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `TextureColorWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureDetail2` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureDiffuseWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureEnvMap` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureFresnelColorWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureFresnelWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureSpecularWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `COPRenderStatusEffectCitadel`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `TextureColorWarp` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureDetail` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureMetalness` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureNormal` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureRoughness` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `TextureSelfIllum` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CParticleSystemDefinition`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__ParticleChildrenInfo_t__` | `ParticleChildrenInfo[]` |
| `Constraints` | `CUtlVector__CParticleFunctionConstraint___` | `CParticleFunctionConstraint?[]` |
| `ControlPointConfigurations` | `CUtlVector__ParticleControlPointConfiguration_t__` | `ParticleControlPointConfiguration[]` |
| `CullReplacementName` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `Emitters` | `CUtlVector__CParticleFunctionEmitter___` | `CParticleFunctionEmitter?[]` |
| `Fallback` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `ForceGenerators` | `CUtlVector__CParticleFunctionForce___` | `CParticleFunctionForce?[]` |
| `Initializers` | `CUtlVector__CParticleFunctionInitializer___` | `CParticleFunctionInitializer?[]` |
| `LowViolenceDef` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `NamedValueLocals` | `CUtlVector__ParticleNamedValueSource_t___` | `ParticleNamedValueSource?[]` |
| `Operators` | `CUtlVector__CParticleFunctionOperator___` | `CParticleFunctionOperator?[]` |
| `PreEmissionOperators` | `CUtlVector__CParticleFunctionPreEmission___` | `CParticleFunctionPreEmission?[]` |
| `ReferenceReplacement` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `Renderers` | `CUtlVector__CParticleFunctionRenderer___` | `CParticleFunctionRenderer?[]` |
| `Snapshot` | `CStrongHandle__InfoForResourceTypeIParticleSnapshot__` | `CStrongHandle<InfoForResourceTypeIParticleSnapshot>` |

### `ModelReference`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `ParticleChildrenInfo`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `ChildRef` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `ParticleControlPointConfiguration`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Drivers` | `CUtlVector__ParticleControlPointDriver_t__` | `ParticleControlPointDriver[]` |

### `ParticlePreviewState`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `BodyGroups` | `CUtlVector__ParticlePreviewBodyGroup_t__` | `ParticlePreviewBodyGroup[]` |

### `RenderProjectedMaterial`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `TextureGroup`  <sub>Particles</sub>

| Property | Was | Now |
|---|---|---|
| `Texture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CFeMorphLayer`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `GoalDamping` | `CUtlVector__float32__` | `float[]` |
| `GoalStrength` | `CUtlVector__float32__` | `float[]` |
| `Gravity` | `CUtlVector__float32__` | `float[]` |
| `InitPos` | `CUtlVector__Vector__` | `Vector[]` |
| `Nodes` | `CUtlVector__uint16__` | `ushort[]` |

### `CFeVertexMapBuildArray`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Array` | `CUtlVector__FeVertexMapBuild_t___` | `FeVertexMapBuild?[]` |

### `CGenericShapeProxy`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Verts` | `CUtlLeanVectorFixedGrowable__Vector__8__` | `Vector[]` |

### `CRegionSVM`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Nodes` | `CUtlVector__uint32__` | `uint[]` |
| `Planes` | `CUtlVector__RnPlane_t__` | `RnPlane[]` |

### `CollisionDetailLayerInfo`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `SubTreeDetailLayers` | `CUtlVector__CollisionDetailLayerInfo_t_Name_t__` | `CollisionDetailLayerInfoTName[]` |

### `FeAntiTunnelProbeBuild`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `TargetNodes` | `CUtlVector__uint16__` | `ushort[]` |

### `FeModelSelfCollisionLayer`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Nodes` | `CUtlVector__uint16__` | `ushort[]` |

### `FeMorphLayerDepr`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `GoalDamping` | `CUtlVector__float32__` | `float[]` |
| `GoalStrength` | `CUtlVector__float32__` | `float[]` |
| `Gravity` | `CUtlVector__float32__` | `float[]` |
| `InitPos` | `CUtlVector__Vector__` | `Vector[]` |
| `Nodes` | `CUtlVector__uint16__` | `ushort[]` |

### `FeSDFRigid`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Distances` | `CUtlVector__float32__` | `float[]` |

### `FeVertexMapBuild`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Weights` | `CUtlVector__float32__` | `float[]` |

### `PhysFeModelDesc`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `AnimStrayRadii` | `CUtlVector__FeAnimStrayRadius_t__` | `FeAnimStrayRadius[]` |
| `AntiTunnelBytecode` | `CUtlVector__uint32__` | `uint[]` |
| `AntiTunnelProbes` | `CUtlVector__FeAntiTunnelProbe_t__` | `FeAntiTunnelProbe[]` |
| `AntiTunnelTargetNodes` | `CUtlVector__uint16__` | `ushort[]` |
| `AxialEdges` | `CUtlVector__FeAxialEdgeBend_t__` | `FeAxialEdgeBend[]` |
| `BoneMergeLinks` | `CUtlVector__FeBoneMergeLink_t__` | `FeBoneMergeLink[]` |
| `BoxRigids` | `CUtlVector__FeBoxRigid_t__` | `FeBoxRigid[]` |
| `CollisionPlanes` | `CUtlVector__FeCollisionPlane_t__` | `FeCollisionPlane[]` |
| `CtrlHash` | `CUtlVector__uint32__` | `uint[]` |
| `CtrlName` | `CUtlVector__CUtlString__` | `string[]` |
| `CtrlOffsets` | `CUtlVector__FeCtrlOffset_t__` | `FeCtrlOffset[]` |
| `CtrlOsOffsets` | `CUtlVector__FeCtrlOsOffset_t__` | `FeCtrlOsOffset[]` |
| `CtrlSoftOffsets` | `CUtlVector__FeCtrlSoftOffset_t__` | `FeCtrlSoftOffset[]` |
| `DynKinLinks` | `CUtlVector__FeDynKinLink_t__` | `FeDynKinLink[]` |
| `DynNodeFriction` | `CUtlVector__float32__` | `float[]` |
| `DynNodeVertexSet` | `CUtlVector__uint8__` | `byte[]` |
| `DynNodeWindBases` | `CUtlVector__FeNodeWindBase_t__` | `FeNodeWindBase[]` |
| `Effects` | `CUtlVector__FeEffectDesc_t__` | `FeEffectDesc[]` |
| `FitMatrices` | `CUtlVector__FeFitMatrix_t__` | `FeFitMatrix[]` |
| `FitWeights` | `CUtlVector__FeFitWeight_t__` | `FeFitWeight[]` |
| `FollowNodes` | `CUtlVector__FeFollowNode_t__` | `FeFollowNode[]` |
| `FreeNodes` | `CUtlVector__uint16__` | `ushort[]` |
| `GoalDampedSpringIntegrators` | `CUtlVector__uint32__` | `uint[]` |
| `HingeLimits` | `CUtlVector__FeHingeLimit_t__` | `FeHingeLimit[]` |
| `InitPose` | `CUtlVector__CTransform__` | `CTransform[]` |
| `JiggleBones` | `CUtlVector__CFeIndexedJiggleBone__` | `CFeIndexedJiggleBone[]` |
| `KelagerBends` | `CUtlVector__FeKelagerBend2_t__` | `FeKelagerBend2[]` |
| `LegacyStretchForce` | `CUtlVector__float32__` | `float[]` |
| `LocalForce` | `CUtlVector__float32__` | `float[]` |
| `LocalRotation` | `CUtlVector__float32__` | `float[]` |
| `LockToGoal` | `CUtlVector__uint16__` | `ushort[]` |
| `LockToParent` | `CUtlVector__FeCtrlOffset_t__` | `FeCtrlOffset[]` |
| `MorphLayers` | `CUtlVector__FeMorphLayerDepr_t__` | `FeMorphLayerDepr[]` |
| `MorphSetData` | `CUtlVector__uint8__` | `byte[]` |
| `NodeBases` | `CUtlVector__FeNodeBase_t__` | `FeNodeBase[]` |
| `NodeCollisionRadii` | `CUtlVector__float32__` | `float[]` |
| `NodeIntegrator` | `CUtlVector__FeNodeIntegrator_t__` | `FeNodeIntegrator[]` |
| `NodeInvMasses` | `CUtlVector__float32__` | `float[]` |
| `NodeStrayBoxes` | `CUtlVector__FeNodeStrayBox_t__` | `FeNodeStrayBox[]` |
| `Quads` | `CUtlVector__FeQuad_t__` | `FeQuad[]` |
| `ReverseOffsets` | `CUtlVector__FeNodeReverseOffset_t__` | `FeNodeReverseOffset[]` |
| `RigidColliderPriorities` | `CUtlVector__FeRigidColliderIndices_t__` | `FeRigidColliderIndices[]` |
| `Rods` | `CUtlVector__FeRodConstraint_t__` | `FeRodConstraint[]` |
| `Ropes` | `CUtlVector__uint16__` | `ushort[]` |
| `SDFRigids` | `CUtlVector__FeSDFRigid_t__` | `FeSDFRigid[]` |
| `SelfCollisionLayers` | `CUtlVector__FeModelSelfCollisionLayer_t__` | `FeModelSelfCollisionLayer[]` |
| `SimdAnimStrayRadii` | `CUtlVector__FeSimdAnimStrayRadius_t__` | `FeSimdAnimStrayRadius[]` |
| `SimdNodeBases` | `CUtlVector__FeSimdNodeBase_t__` | `FeSimdNodeBase[]` |
| `SimdQuads` | `CUtlVector__FeSimdQuad_t__` | `FeSimdQuad[]` |
| `SimdRods` | `CUtlVector__FeSimdRodConstraint_t__` | `FeSimdRodConstraint[]` |
| `SimdRodsAnim` | `CUtlVector__FeSimdRodConstraintAnim_t__` | `FeSimdRodConstraintAnim[]` |
| `SimdSpringIntegrator` | `CUtlVector__FeSimdSpringIntegrator_t__` | `FeSimdSpringIntegrator[]` |
| `SimdTris` | `CUtlVector__FeSimdTri_t__` | `FeSimdTri[]` |
| `SkelParents` | `CUtlVector__int16__` | `short[]` |
| `SourceElems` | `CUtlVector__uint16__` | `ushort[]` |
| `SphereRigids` | `CUtlVector__FeSphereRigid_t__` | `FeSphereRigid[]` |
| `SpringIntegrator` | `CUtlVector__FeSpringIntegrator_t__` | `FeSpringIntegrator[]` |
| `TaperedCapsuleRigids` | `CUtlVector__FeTaperedCapsuleRigid_t__` | `FeTaperedCapsuleRigid[]` |
| `TaperedCapsuleStretches` | `CUtlVector__FeTaperedCapsuleStretch_t__` | `FeTaperedCapsuleStretch[]` |
| `TreeChildren` | `CUtlVector__FeTreeChildren_t__` | `FeTreeChildren[]` |
| `TreeCollisionMasks` | `CUtlVector__uint16__` | `ushort[]` |
| `TreeParents` | `CUtlVector__uint16__` | `ushort[]` |
| `Tris` | `CUtlVector__FeTri_t__` | `FeTri[]` |
| `Twists` | `CUtlVector__FeTwistConstraint_t__` | `FeTwistConstraint[]` |
| `VertexMapValues` | `CUtlVector__uint8__` | `byte[]` |
| `VertexMaps` | `CUtlVector__FeVertexMapDesc_t__` | `FeVertexMapDesc[]` |
| `VertexSetNames` | `CUtlVector__uint32__` | `uint[]` |
| `WorldCollisionNodes` | `CUtlVector__uint16__` | `ushort[]` |
| `WorldCollisionParams` | `CUtlVector__FeWorldCollisionParams_t__` | `FeWorldCollisionParams[]` |

### `RnCompound`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Capsules` | `CUtlVector__RnCapsule_t__` | `RnCapsule[]` |
| `Hulls` | `CUtlVector__RnHull_t__` | `RnHull[]` |
| `Meshes` | `CUtlVector__RnMesh_t__` | `RnMesh[]` |
| `Spheres` | `CUtlVector__RnSphere_t__` | `RnSphere_t[]` |

### `RnHull`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Edges` | `CUtlVector__RnHalfEdge_t__` | `RnHalfEdge[]` |
| `FacePlanes` | `CUtlVector__RnPlane_t__` | `RnPlane[]` |
| `Faces` | `CUtlVector__RnFace_t__` | `RnFace[]` |
| `VertexPositions` | `CUtlVector__Vector__` | `Vector[]` |
| `Vertices` | `CUtlVector__RnVertex_t__` | `RnVertex[]` |

### `RnMesh`  <sub>Physicslib</sub>

| Property | Was | Now |
|---|---|---|
| `Materials` | `CUtlVector__uint8__` | `byte[]` |
| `Nodes` | `CUtlVector__RnNode_t__` | `RnNode[]` |
| `TriangleEdgeFlags` | `CUtlVector__uint8__` | `byte[]` |
| `Triangles` | `CUtlVector__RnTriangle_t__` | `RnTriangle[]` |
| `Wings` | `CUtlVector__RnWing_t__` | `RnWing[]` |

### `CPulseBlackBoardReference`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `HBlackBoardResource` | `CStrongHandle__InfoForResourceTypeIPulseGraphDef__` | `CStrongHandle<InfoForResourceTypeIPulseGraphDef>` |

### `CPulseCellFireCursors`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outflows` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellInflowMethod`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Args` | `CUtlLeanVector__CPulseRuntimeMethodArg__` | `CPulseRuntimeMethodArg[]` |

### `CPulseCellOutflowCycleOrdered`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outputs` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellOutflowCycleRandom`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outputs` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellOutflowCycleShuffled`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outputs` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellOutflowCycleShuffledInstanceState`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Shuffle` | `CUtlVectorFixedGrowable__uint8__8__` | `byte[]` |

### `CPulseCellStepCallExternalMethod`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `ExpectedArgs` | `CUtlLeanVector__CPulseRuntimeMethodArg__` | `CPulseRuntimeMethodArg[]` |

### `CPulseCellTimeline`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `TimelineEvents` | `CUtlVector__CPulseCell_Timeline_TimelineEvent_t__` | `CPulseCellTimelineTimelineEvent[]` |

### `CPulseChunk`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `InstructionDebugInfos` | `CUtlLeanVector__CPulse_InstructionDebug__` | `CPulseInstructionDebug[]` |
| `Instructions` | `CUtlLeanVector__PGDInstruction_t__` | `PGDInstruction[]` |
| `Registers` | `CUtlLeanVector__CPulse_RegisterInfo__` | `CPulseRegisterInfo[]` |

### `CPulseGraphDef`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `BlackBoardReferences` | `CUtlVector__CPulse_BlackboardReference__` | `CPulseBlackBoardReference[]` |
| `CallInfos` | `CUtlVector__CPulse_CallInfo___` | `CPulseCallInfo?[]` |
| `Cells` | `CUtlVector__CPulseCell_Base___` | `CPulseCellBase?[]` |
| `Chunks` | `CUtlVector__CPulse_Chunk___` | `CPulseChunk?[]` |
| `Constants` | `CUtlVector__CPulse_Constant__` | `CPulseConstant[]` |
| `DomainValues` | `CUtlVector__CPulse_DomainValue__` | `CPulseDomainValue[]` |
| `InvokeBindings` | `CUtlVector__CPulse_InvokeBinding___` | `CPulseInvokeBinding?[]` |
| `OutputConnections` | `CUtlVector__CPulse_OutputConnection___` | `CPulseOutputConnection?[]` |
| `PublicOutputs` | `CUtlVector__CPulse_PublicOutput__` | `CPulsePublicOutput[]` |
| `Vars` | `CUtlVector__CPulse_Variable__` | `CPulseVariable[]` |

### `CPulseGraphExecutionHistory`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `History` | `CUtlVector__PulseGraphExecutionHistoryEntry_t___` | `PulseGraphExecutionHistoryEntry?[]` |
| `MapCellDesc` | `CUtlOrderedMap__PulseDocNodeID_t__PulseGraphExecutionHistoryNodeDesc_t___` | `Dictionary<PulseDocNodeId, PulseGraphExecutionHistoryNodeDesc?>` |
| `MapCursorDesc` | `CUtlOrderedMap__PulseCursorID_t__PulseGraphExecutionHistoryCursorDesc_t___` | `Dictionary<PulseCursorId, PulseGraphExecutionHistoryCursorDesc?>` |

### `CPulsePublicOutput`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Args` | `CUtlLeanVector__CPulseRuntimeMethodArg__` | `CPulseRuntimeMethodArg[]` |

### `OutflowWithRequirements`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `CursorStateBlockIndex` | `CUtlVector__int32__` | `int[]` |
| `RequirementNodeIDs` | `CUtlVector__PulseDocNodeID_t__` | `PulseDocNodeId[]` |

### `PulseGraphExecutionHistoryCursorDesc`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `AncestorCursorIDs` | `CUtlVector__PulseCursorID_t__` | `PulseCursorId[]` |

### `PulseNodeDynamicOutflows`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outflows` | `CUtlVector__PulseNodeDynamicOutflows_t_DynamicOutflow_t__` | `PulseNodeDynamicOutflowsTDynamicOutflow[]` |

### `PulseSelectorOutflowList`  <sub>PulseRuntimeLib</sub>

| Property | Was | Now |
|---|---|---|
| `Outflows` | `CUtlVector__OutflowWithRequirements_t__` | `OutflowWithRequirements[]` |

### `CPulseGraphInstanceTestDomain`  <sub>PulseSystem</sub>

| Property | Was | Now |
|---|---|---|
| `TracePoints` | `CUtlVector__CUtlString__` | `string[]` |

### `CColorLookupColorCorrectionLayer`  <sub>Resourcecompiler</sub>

| Property | Was | Now |
|---|---|---|
| `Lut` | `CUtlVector__float32__` | `float[]` |

### `CCurvesColorCorrectionLayer`  <sub>Resourcecompiler</sub>

| Property | Was | Now |
|---|---|---|
| `CurvePointsB` | `CUtlVector__Vector2D__` | `Vector2D[]` |
| `CurvePointsG` | `CUtlVector__Vector2D__` | `Vector2D[]` |
| `CurvePointsR` | `CUtlVector__Vector2D__` | `Vector2D[]` |
| `CurvePointsRGB` | `CUtlVector__Vector2D__` | `Vector2D[]` |

### `CPostProcessData`  <sub>Resourcecompiler</sub>

| Property | Was | Now |
|---|---|---|
| `Layers` | `CUtlVector__CColorCorrectionLayer___` | `CColorCorrectionLayer?[]` |

### `ManifestTestResource`  <sub>Resourcesystem</sub>

| Property | Was | Now |
|---|---|---|
| `Child` | `CStrongHandle__InfoForResourceTypeManifestTestResource_t__` | `CStrongHandle<InfoForResourceTypeManifestTestResource>` |

### `CSSDSMsgEndFrame`  <sub>Scenesystem</sub>

| Property | Was | Now |
|---|---|---|
| `Views` | `CUtlVector__CSSDSEndFrameViewInfo__` | `CSSDSEndFrameViewInfo[]` |

### `CSSDSMsgViewTargetList`  <sub>Scenesystem</sub>

| Property | Was | Now |
|---|---|---|
| `Targets` | `CUtlVector__CSSDSMsg_ViewTarget__` | `CSSDSMsgViewTarget[]` |

### `CSchemaSystemInternalRegistration`  <sub>Schemasystem</sub>

| Property | Was | Now |
|---|---|---|
| `ResourceTypes` | `CResourceArray__CResourcePointer__CResourceString____` | `string[]` |

### `AIBaseNPCDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AnimEvents` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `Conditions` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `CurrentEnemy` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `AIDefaultNPCDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathQueriesSpeculative` | `CUtlVector__AI_DefaultNPC_DebugSnapshotData_t_PathQuery_t__` | `AIDefaultNPCDebugSnapshotDataTPathQuery[]` |
| `TacticInterruptConditions` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `AIGroundRootMotionMotorDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `VecEvents` | `CUtlVector__AI_GroundRootMotionMotor_DebugSnapshotData_t_Event_t__` | `AIGroundRootMotionMotorDebugSnapshotDataTEvent[]` |

### `AIMotorServicesDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MotorPath` | `CUtlVector__AI_MotorServices_DebugSnapshotData_t_MotorPathWaypoint_t__` | `AIMotorServicesDebugSnapshotDataTMotorPathWayPoint[]` |

### `AINavigatorDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `WayPoints` | `CUtlVector__AI_Navigator_DebugSnapshotData_t_Waypoint_t__` | `AINavigatorDebugSnapshotDataTWayPoint[]` |

### `ActiveModelConfig`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AssociatedEntities` | `CNetworkUtlVectorBase__CHandle__CBaseModelEntity____` | `CHandle<CBaseModelEntity>[]` |
| `AssociatedEntityNames` | `CNetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |

### `ActorMapping`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CAmbientGeneric`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SoundSource` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CAnimGraphControllerManager`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Controllers` | `CUtlVector__CAnimGraphControllerBase___` | `CAnimGraphControllerBase?[]` |

### `CAttributeList`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Attributes` | `CUtlVectorEmbeddedNetworkVar__CEconItemAttribute__` | `CEconItem[]` |

### `CAttributeManager`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CachedResults` | `CUtlVector__CAttributeManager_cached_attribute_float_t__` | `CAttributeManagerCachedAttributeFloat[]` |
| `Outer` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Providers` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CBarnLight`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LightCookie` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `LightStyleEvents` | `CNetworkUtlVectorBase__CUtlString__` | `string[]` |
| `LightStyleTargets` | `CNetworkUtlVectorBase__CHandle__CBaseModelEntity____` | `CHandle<CBaseModelEntity>[]` |
| `QueuedLightStyleStrings` | `CNetworkUtlVectorBase__CUtlString__` | `string[]` |
| `VisClusters` | `CNetworkUtlVectorBase__uint16__` | `ushort[]` |

### `CBaseAnimGraph`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnLayerCycleUpdated` | `CEntityOutputTemplate__float32__` | `float?` |

### `CBaseAnimGraphController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ExternalClipIds` | `CNetworkUtlVectorBase__ResourceId_t__` | `ResourceId[]` |
| `ExternalGraphIds` | `CNetworkUtlVectorBase__ResourceId_t__` | `ResourceId[]` |
| `GraphDefinitionAG2` | `CStrongHandle__InfoForResourceTypeCNmGraphDefinition__` | `CStrongHandle<InfoForResourceTypeCNmGraphDefinition>` |
| `SecondarySkeletonSlotIDs` | `CNetworkUtlVectorBase__CGlobalSymbol__` | `string[]` |
| `SecondarySkeletons` | `CNetworkUtlVectorBase__CHandle__CBaseAnimGraph____` | `CHandle<CBaseAnimGraph>[]` |
| `SerializePoseRecipeAG2Dynamic` | `CNetworkUtlVectorBase__uint8__` | `byte[]` |
| `SerializePoseRecipeAG2Slots` | `CUtlVectorEmbeddedNetworkVar__AnimGraph2SerializedPoseRecipeSlot_t__` | `AnimGraph2SerializedPoseRecipeSlot[]` |

### `CBaseButton`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `GlowEntity` | `CHandle__CBaseModelEntity__` | `CHandle<CBaseModelEntity>` |

### `CBaseCSGrenade`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SwitchToWeaponAfterThrow` | `CHandle__CCSWeaponBase__` | `CHandle<CCSWeaponBase>` |

### `CBaseCSGrenadeProjectile`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ExplodeEffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `CBaseClientUIEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CustomOutput0` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput1` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput2` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput3` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput4` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput5` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput6` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput7` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput8` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `CustomOutput9` | `CEntityOutputTemplate__CUtlString__` | `string?` |

### `CBaseCombatCharacter`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MyWearables` | `CNetworkUtlVectorBase__CHandle__CEconWearable____` | `CHandle<CEconWearable>[]` |
| `VecRelationships` | `CUtlVector__RelationshipOverride_t__` | `RelationshipOverride[]` |

### `CBaseEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AThinkFunctions` | `CUtlVector__thinkfunc_t__` | `Thinkfunc[]` |
| `Blocker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `DamageFilter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |
| `EffectEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `GroundEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `IsSteadyState` | `CTypedBitVec__64__` | `byte[]` |
| `OwnerEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `ResponseContexts` | `CUtlVector__ResponseContext_t__` | `ResponseContext[]` |

### `CBaseGrenade`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OriginalThrower` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `Thrower` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CBaseModelEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `BodyGroupChoices` | `CUtlOrderedMap__CGlobalSymbol__int32__` | `Dictionary<string, int>` |
| `OnDestructibleHitGroupDamageLevelChanged` | `CEntityOutputTemplate__CBaseModelEntity_OnDamageLevelChangedArgs_t__` | `CBaseModelEntityOnDamageLevelChangedArgs?` |
| `RenderAttributes` | `CUtlVectorEmbeddedNetworkVar__EntityRenderAttribute_t__` | `EntityRender[]` |

### `CBaseMoveBehavior`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentKeyFrame` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |
| `PostKeyFrame` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |
| `PreKeyFrame` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |
| `TargetKeyFrame` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |

### `CBasePlayerController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Pawn` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |
| `SplitOwner` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `SplitScreenPlayers` | `CUtlVector__CHandle__CBasePlayerController____` | `CHandle<CBasePlayerController>[]` |

### `CBasePlayerPawn`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Controller` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `DefaultController` | `CHandle__CBasePlayerController__` | `CHandle<CBasePlayerController>` |
| `ServerViewAngleChanges` | `CUtlVectorEmbeddedNetworkVar__ViewAngleServerChange_t__` | `ViewAngleServerChange[]` |
| `SndOpvarLatchData` | `CUtlVector__sndopvarlatchdata_t__` | `Sndopvarlatchdata[]` |

### `CBasePlayerVData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `SModelNameAg2Override` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CBasePlayerWeaponVData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AShootSounds` | `CUtlOrderedMap__WeaponSound_t__CSoundEventName__` | `Dictionary<WeaponSound, string>` |
| `BarrelSmokeParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `MuzzleFlashParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `SToolsOnlyOwnerModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `WorldModel` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |
| `WorldModelAg2Override` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CBasePropDoor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Blocker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `DoorList` | `CUtlVector__CHandle__CBasePropDoor____` | `CHandle<CBasePropDoor>[]` |
| `Master` | `CHandle__CBasePropDoor__` | `CHandle<CBasePropDoor>` |

### `CBaseToggle`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CBaseTrigger`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |
| `TouchingEntities` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CBeam`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AttachEntity` | `CHandle__CBaseEntity__[]` | `CHandle<CBaseEntity>[]` |
| `BaseMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `EndEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HaloIndex` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CBombTarget`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `InstructorHint` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CBreakable`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Breaker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `OnHealthChanged` | `CEntityOutputTemplate__float32__` | `float?` |
| `PhysicsAttacker` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |

### `CBreakableProp`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Breaker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `LastAttacker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `OnHealthChanged` | `CEntityOutputTemplate__float32__` | `float?` |
| `PhysicsAttacker` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |

### `CCS2ChickenGraphController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `IdleVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `InWater` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `PanicVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `RunVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `SquatVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |

### `CCS2PawnGraphController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AimPitchAngle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AimYawAngle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AirAction` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `AirHeightAboveGround` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `CrouchAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `FlashedAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `FlinchBody` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `FlinchBodyRestart` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `FlinchHead` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `FlinchHeadRestart` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `FlinchIsOnFire` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `GroundAction` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `GroundActionDirectionId` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `GroundTurnAngleOrVelocity` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IsDefusing` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `IsWalking` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `LadderCycle` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LadderYaw` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LadderYawBackwards` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `LeftFootTarget` | `CAnimGraph2ParamOptionalRef__CNmTarget__` | `CNmTarget?` |
| `MoveDirectionId` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `MoveSpeedHorizontal` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveSpeedX` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveSpeedY` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `MoveType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `PreviousMoveSpeedHorizontal` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `RightFootTarget` | `CAnimGraph2ParamOptionalRef__CNmTarget__` | `CNmTarget?` |
| `WeaponDropAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |

### `CCS2WeaponGraphController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Action` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `ActionReset` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `AttackThrowStrength` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `AttackType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `AttackVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `DeployVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IdleVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `InspectExtraInfo` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `InspectVariation` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `IsUsingLegacyModel` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `ReloadStage` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponActionSpeedScale` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmo` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmoMax` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponAmmoReserve` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponCategory` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponExtraInfo` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `WeaponIronsightAmount` | `CAnimGraph2ParamOptionalRef__float32__` | `float?` |
| `WeaponIsSilenced` | `CAnimGraph2ParamOptionalRef__bool__` | `bool?` |
| `WeaponType` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |

### `CCSBot`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Attacker` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `Avoid` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Bomber` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `ClosestVisibleFriend` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `ClosestVisibleHumanFriend` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `Enemy` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `GoalEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Leader` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `RadioSubject` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `TaskEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CCSGameRules`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ArrSelectedHostageSpawnIndices` | `CUtlVector__int32__` | `int[]` |
| `ArrTeamUniqueKillWeaponsMatch` | `CUtlVector__int32__[]` | `int[][]` |
| `CTSpawnPoints` | `CUtlVector__CHandle__SpawnPoint____` | `CHandle<SpawnPoint>[]` |
| `CTSpawnPointsMasterList` | `CUtlVector__CHandle__SpawnPoint____` | `CHandle<SpawnPoint>[]` |
| `EndMatchTiedVotes` | `CUtlVector__int32__` | `int[]` |
| `PlayerResource` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `TerroristSpawnPoints` | `CUtlVector__CHandle__SpawnPoint____` | `CHandle<SpawnPoint>[]` |
| `TerroristSpawnPointsMasterList` | `CUtlVector__CHandle__SpawnPoint____` | `CHandle<SpawnPoint>[]` |

### `CCSPlayerActionTrackingServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LastWeaponBeforeC4AutoSwitch` | `CHandle__CBasePlayerWeapon__` | `CHandle<CBasePlayerWeapon>` |

### `CCSPlayerBaseCameraServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LastFogTrigger` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `TriggerFogList` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |
| `ZoomOwner` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CCSPlayerBuyServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SellBackPurchaseEntries` | `CUtlVectorEmbeddedNetworkVar__SellbackPurchaseEntry_t__` | `SellBackPurchaseEntry[]` |

### `CCSPlayerController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ObserverPawn` | `CHandle__CCSObserverPawn__` | `CHandle<CCSObserverPawn>` |
| `OriginalControllerOfCurrentPawn` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerPawn` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CCSPlayerControllerActionTrackingServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PerRoundStats` | `CUtlVectorEmbeddedNetworkVar__CSPerRoundStats_t__` | `CSPerRoundStats[]` |

### `CCSPlayerControllerDamageServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DamageList` | `CUtlVectorEmbeddedNetworkVar__CDamageRecord__` | `CDamageRecord[]` |

### `CCSPlayerControllerInventoryServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ServerAuthoritativeWeaponSlots` | `CUtlVectorEmbeddedNetworkVar__ServerAuthoritativeWeaponSlot_t__` | `ServerAuthoritativeWeaponSlot[]` |

### `CCSPlayerHostageServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CarriedHostage` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `CarriedHostageProp` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CCSPlayerPawn`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TouchingBuyZones` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CCSPlayerPawnBase`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OriginalController` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |

### `CCSPlayerPingServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PlayerPing` | `CHandle__CPlayerPing__` | `CHandle<CPlayerPing>` |

### `CCSPlayerUseServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LastKnownUseEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CCSPlayerWeaponServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `NetworkAnimTiming` | `CNetworkUtlVectorBase__uint8__` | `byte[]` |
| `SavedWeapon` | `CHandle__CBasePlayerWeapon__` | `CHandle<CBasePlayerWeapon>` |

### `CCSWeaponBase`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PrevOwner` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CCSWeaponBaseVData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AnimSkeleton` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCNmSkeleton____` | `string` |
| `TracerParticle` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |

### `CChicken`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `FleeFrom` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Leader` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CChoreoComponent`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__CBaseModelEntity__` | `CHandle<CBaseModelEntity>` |

### `CChoreoGraphController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ChoreoState` | `CAnimGraph2ParamOptionalRef__CGlobalSymbol__` | `string?` |
| `TChoreoExitWarp` | `CAnimGraph2ParamOptionalRef__CTransform__` | `CTransform?` |
| `TChoreoTargetWarp` | `CAnimGraph2ParamOptionalRef__CTransform__` | `CTransform?` |

### `CCommentarySystem`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ActiveCommentaryNode` | `CHandle__CPointCommentaryNode__` | `CHandle<CPointCommentaryNode>` |
| `CurrentNode` | `CHandle__CPointCommentaryNode__` | `CHandle<CPointCommentaryNode>` |
| `LastCommentaryNode` | `CHandle__CPointCommentaryNode__` | `CHandle<CPointCommentaryNode>` |
| `ModifiedConvars` | `CUtlVector__modifiedconvars_t__` | `ModifiedConvars[]` |
| `Nodes` | `CUtlVector__CHandle__CPointCommentaryNode____` | `CHandle<CPointCommentaryNode>[]` |

### `CCopyRecipientFilter`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Recipients` | `CUtlVector__CPlayerSlot__` | `int[]` |

### `CDamageRecord`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PlayerControllerDamager` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerControllerRecipient` | `CHandle__CCSPlayerController__` | `CHandle<CCSPlayerController>` |
| `PlayerDamager` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `PlayerRecipient` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CDebugDrawHistoryData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Bools` | `CUtlLeanVector__bool__` | `bool[]` |
| `Colors` | `CUtlLeanVector__Color__` | `Color[]` |
| `Dimensions` | `CUtlLeanVector__float32__` | `float[]` |
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Strings` | `CUtlLeanVector__CUtlString__` | `string[]` |
| `Times` | `CUtlLeanVector__float64__` | `double[]` |
| `Uint64s` | `CUtlLeanVector__uint64__` | `ulong[]` |
| `Vectors` | `CUtlLeanVector__Vector4D__` | `Vector4D[]` |

### `CDebugSnapshotData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlLeanVector__CDebugSnapshotData_t__` | `CDebugSnapshotData[]` |
| `DebugOverlayData` | `CUtlVector__CDebugDrawHistoryData___` | `CDebugDrawHistoryData?[]` |
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CDecalGroupVData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Options` | `CUtlVector__DecalGroupOption_t__` | `DecalGroupOption[]` |

### `CDecalInstance`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CDestructiblePart`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DamageLevels` | `CUtlVector__CDestructiblePart_DamageLevel__` | `CDestructiblePartDamageLevel[]` |
| `OtherHitGroupsToDestroyWhenFullyDestructed` | `CUtlVector__HitGroup_t__` | `HitGroup[]` |

### `CDestructiblePartsComponent`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DamageTakenByHitGroup` | `CUtlVector__uint16__` | `ushort[]` |
| `Owner` | `CHandle__CBaseModelEntity__` | `CHandle<CBaseModelEntity>` |

### `CDestructiblePartsSystemData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PartsDataByHitGroup` | `CUtlOrderedMap__HitGroup_t__CDestructiblePart__` | `Dictionary<HitGroup, CDestructiblePart>` |

### `CDynamicNavConnectionsVolume`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Connections` | `CUtlVector__DynamicVolumeDef_t__` | `DynamicVolumeDef[]` |

### `CEconEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OldProvidee` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CEntityFlame`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Attacker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `EntAttached` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CEnvBeam`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `SpriteTexture` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CEnvCombinedLightProbeVolume`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightIndicesTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightScalarsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightShadowsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureAmbientCube` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSDF` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2B` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2DC` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2G` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2R` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CEnvCubeMap`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CEnvCubeMapFog`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `FogCubeMapTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `SkyMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CEnvDecal`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DecalMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CEnvEntityMaker`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentBlocker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `CurrentInstance` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CEnvExplosion`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityIgnore` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Inflictor` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CEnvGlobal`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OutCounter` | `CEntityOutputTemplate__int32__` | `int?` |

### `CEnvLaser`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Sprite` | `CHandle__CSprite__` | `CHandle<CSprite>` |

### `CEnvLightProbeVolume`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHLightProbeDirectLightIndicesTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightScalarsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeDirectLightShadowsTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureAmbientCube` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSDF` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2B` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2DC` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2G` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `EntityHLightProbeTextureSH2R` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CEnvParticleGlow`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TextureOverride` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CEnvSky`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SkyMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `SkyMaterialLightingOnly` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CEnvSoundscape`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ProxySoundscape` | `CHandle__CEnvSoundscape__` | `CHandle<CEnvSoundscape>` |

### `CEnvVolumetricFogController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `FogInDirectTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CEnvWindShared`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntOwner` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CFilterMultiple`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseEntity__[]` | `CHandle<CBaseEntity>[]` |

### `CFish`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Pool` | `CHandle__CFishPool__` | `CHandle<CFishPool>` |
| `Visible` | `CUtlVector__CFish___` | `CFish?[]` |

### `CFishPool`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Fishes` | `CUtlVector__CHandle__CFish____` | `CHandle<CFish>[]` |

### `CFuncConveyor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HConveyorModels` | `CNetworkUtlVectorBase__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CFuncLadder`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Dismounts` | `CUtlVector__CHandle__CInfoLadderDismount____` | `CHandle<CInfoLadderDismount>[]` |

### `CFuncMonitor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HTargetCamera` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CFuncMover`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `FollowEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `FollowMover` | `CHandle__CFuncMover__` | `CHandle<CFuncMover>` |
| `OnNodePassed` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `OrientationFaceEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `OrientationMatchEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `PathMover` | `CHandle__CPathMover__` | `CHandle<CPathMover>` |
| `PrevPathMover` | `CHandle__CPathMover__` | `CHandle<CPathMover>` |
| `StopAtNode` | `CHandle__CMoverPathNode__` | `CHandle<CMoverPathNode>` |

### `CFuncMoverRouter`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathMover` | `CHandle__CPathMover__` | `CHandle<CPathMover>` |

### `CFuncRotator`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HRotatorTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `LocalRotationHistory` | `CUtlVector__Quaternion__` | `Quaternion[]` |

### `CFuncShatterGlass`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ExtraDamagePositions` | `CUtlVector__VectorWS__` | `VectorWS[]` |
| `InitialDamagePositions` | `CUtlVector__VectorWS__` | `VectorWS[]` |
| `InitialPanelVertices` | `CUtlVector__Vector4D__` | `Vector4D[]` |
| `MaterialDamageBase` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `ShatterGlassShards` | `CUtlVector__uint32__` | `uint[]` |

### `CFuncTrackChange`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TrackBottom` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |
| `TrackTop` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |
| `Train` | `CHandle__CFuncTrackTrain__` | `CHandle<CFuncTrackTrain>` |

### `CFuncTrackTrain`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Ppath` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |

### `CFuncTrain`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CurrentTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Enemy` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CGameChoreoServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__CBaseModelEntity__` | `CHandle<CBaseModelEntity>` |
| `ScriptedSequence` | `CHandle__CScriptedSequence__` | `CHandle<CScriptedSequence>` |

### `CGamePlayerZone`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PlayersInCount` | `CEntityOutputTemplate__int32__` | `int?` |
| `PlayersOutCount` | `CEntityOutputTemplate__int32__` | `int?` |

### `CGameScriptedMoveData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DestEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CGameScriptedMoveDef`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DestEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CGradientFog`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `GradientFogTexture` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CGunTarget`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TargetEnt` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CHandleTest`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Handle` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CHintMessage`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Args` | `CUtlVector__char___` | `string?[]` |

### `CHintMessageQueue`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Messages` | `CUtlVector__CHintMessage___` | `CHintMessage?[]` |

### `CHostage`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HostageGrabber` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `LastLeader` | `CHandle__CCSPlayerPawnBase__` | `CHandle<CCSPlayerPawnBase>` |
| `Leader` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CInfoChoreoAnchor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TargetEntries` | `CUtlVector__CInfoChoreoAnchorPosition__` | `CInfoChoreoAnchorPosition[]` |
| `TargetWarps` | `CUtlVector__CInfoChoreoAnchorPosition__` | `CInfoChoreoAnchorPosition[]` |

### `CInfoChoreoAnchorPosition`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Parent` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CInfoDynamicShadowHint`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Light` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CInfoOffScreenPanoramaTexture`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AdditionalTargetEntities` | `CUtlVector__CHandle__CBaseModelEntity____` | `CHandle<CBaseModelEntity>[]` |
| `CSSClasses` | `CNetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |
| `TargetEntities` | `CNetworkUtlVectorBase__CHandle__CBaseModelEntity____` | `CHandle<CBaseModelEntity>[]` |

### `CInstancedSceneEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CInstructorEventEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TargetPlayer` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |

### `CItemDogtags`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `KillingPlayer` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |
| `OwningPlayer` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CItemGeneric`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PickupFilter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |
| `PickupParticleEffect` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `SpawnParticleEffect` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `TimeOutParticleEffect` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `TriggerHelper` | `CHandle__CItemGenericTriggerHelper__` | `CHandle<CItemGenericTriggerHelper>` |

### `CItemGenericTriggerHelper`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ParentItem` | `CHandle__CItemGeneric__` | `CHandle<CItemGeneric>` |

### `CKeepUpright`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AttachedObject` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CLightComponent`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LightCookie` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CLogicBranch`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Listeners` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CLogicBranchList`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LogicBranchList` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CLogicCase`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnDefault` | `CEntityOutputTemplate__CUtlString__` | `string?` |

### `CLogicCompare`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnEqualTo` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnGreaterThan` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnLessThan` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnNotEqualTo` | `CEntityOutputTemplate__float32__` | `float?` |

### `CLogicEventListener`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnEventFired` | `CEntityOutputTemplate__CUtlString__` | `string?` |

### `CLogicLineToEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EndEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Line` | `CEntityOutputTemplate__Vector__` | `Vector?` |
| `StartEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CLogicMeasureMovement`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HMeasureReference` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HMeasureTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTargetReference` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CLogicNPCCounter`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnFactor1` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnFactor2` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnFactor3` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnFactorAll` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnMinPlayerDist1` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnMinPlayerDist2` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnMinPlayerDist3` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnMinPlayerDistAll` | `CEntityOutputTemplate__float32__` | `float?` |

### `CLogicPlayerProxy`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Player` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `RequestedPlayerHealth` | `CEntityOutputTemplate__int32__` | `int?` |

### `CMapVetoPickController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnLevelTransition` | `CEntityOutputTemplate__int32__` | `int?` |
| `OnMapPicked` | `CEntityOutputTemplate__CUtlSymbolLarge__` | `string?` |
| `OnMapVetoed` | `CEntityOutputTemplate__CUtlSymbolLarge__` | `string?` |
| `OnNewPhaseStarted` | `CEntityOutputTemplate__int32__` | `int?` |
| `OnSidesPicked` | `CEntityOutputTemplate__int32__` | `int?` |

### `CMarkupVolumeTagged`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `GroupNames` | `CUtlVector__CGlobalSymbol__` | `string[]` |
| `Tags` | `CUtlVector__CGlobalSymbol__` | `string[]` |

### `CMathColorBlend`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OutValue` | `CEntityOutputTemplate__Color__` | `Color?` |

### `CMathCounter`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnGetValue` | `CEntityOutputTemplate__float32__` | `float?` |
| `OutValue` | `CEntityOutputTemplate__float32__` | `float?` |

### `CMathRemap`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OutValue` | `CEntityOutputTemplate__float32__` | `float?` |

### `CMomentaryRotButton`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Position` | `CEntityOutputTemplate__float32__` | `float?` |

### `CMoverPathNode`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnPassThrough` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `OnPassThroughForward` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `OnPassThroughReverse` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `OnStartFromOrInSegment` | `CEntityOutputTemplate__CUtlString__` | `string?` |
| `OnStoppedAtOrInSegment` | `CEntityOutputTemplate__CUtlString__` | `string?` |

### `CMultiLightProxy`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Lights` | `CUtlVector__CHandle__CLightEntity____` | `CHandle<CLightEntity>[]` |

### `CMultiSource`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `RgEntities` | `CHandle__CBaseEntity__[]` | `CHandle<CBaseEntity>[]` |

### `CParticleSystem`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ControlPointEnts` | `CHandle__CBaseEntity__[]` | `CHandle<CBaseEntity>[]` |
| `EffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |

### `CPathKeyFrame`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PNextKey` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |
| `PrevKey` | `CHandle__CPathKeyFrame__` | `CHandle<CPathKeyFrame>` |

### `CPathMover`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MoverRouter` | `CHandle__CFuncMoverRouter__` | `CHandle<CFuncMoverRouter>` |
| `Movers` | `CUtlVector__CHandle__CFuncMover____` | `CHandle<CFuncMover>[]` |
| `Spawners` | `CUtlVector__CHandle__CPathMoverEntitySpawner____` | `CHandle<CPathMoverEntitySpawner>[]` |

### `CPathMoverEntitySpawner`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MapSpawnedMoverTemplates` | `CUtlHashtable__CHandle__CFuncMover____PathMoverEntitySpawn__` | `Dictionary<CHandle<CFuncMover>, PathMoverEntitySpawn>` |
| `PathMover` | `CHandle__CPathMover__` | `CHandle<CPathMover>` |
| `QueuedRemovals` | `CUtlVector__CHandle__CFuncMover____` | `CHandle<CFuncMover>[]` |

### `CPathNode`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Path` | `CHandle__CPathWithDynamicNodes__` | `CHandle<CPathWithDynamicNodes>` |

### `CPathParticleRope`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EffectIndex` | `CStrongHandle__InfoForResourceTypeIParticleSystemDefinition__` | `CStrongHandle<InfoForResourceTypeIParticleSystemDefinition>` |
| `PathNodesColor` | `CNetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesName` | `CUtlVector__CUtlSymbolLarge__` | `string[]` |
| `PathNodesPinEnabled` | `CNetworkUtlVectorBase__bool__` | `bool[]` |
| `PathNodesPosition` | `CNetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesRadiusScale` | `CNetworkUtlVectorBase__float32__` | `float[]` |
| `PathNodesTangentIn` | `CNetworkUtlVectorBase__Vector__` | `Vector[]` |
| `PathNodesTangentOut` | `CNetworkUtlVectorBase__Vector__` | `Vector[]` |

### `CPathQueryUtil`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathSampleDistances` | `CUtlVector__float32__` | `float[]` |
| `PathSampleParameters` | `CUtlVector__float32__` | `float[]` |
| `PathSamplePositions` | `CUtlVector__Vector__` | `Vector[]` |

### `CPathTrack`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Paltpath` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |
| `Pnext` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |
| `Pprevious` | `CHandle__CPathTrack__` | `CHandle<CPathTrack>` |

### `CPathWithDynamicNodes`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathNodes` | `CNetworkUtlVectorBase__CHandle__CPathNode____` | `CHandle<CPathNode>[]` |

### `CPhysConstraint`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Attach1` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Attach2` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPhysForce`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AttachedObject` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPhysMagnet`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MagnettedEntities` | `CUtlVector__magnetted_objects_t__` | `MagnettedObjects[]` |

### `CPhysMotor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AnchorObject` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `AttachedObject` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPhysWheelConstraint`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SteeringMimicsEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPhysicsEntitySolver`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `MovingEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `PhysicsBlocker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPlantedC4`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `BombDefuser` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CPlatTrigger`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Platform` | `CHandle__CFuncPlat__` | `CHandle<CFuncPlat>` |

### `CPlayerCameraServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ColorCorrectionCtrl` | `CHandle__CColorCorrection__` | `CHandle<CColorCorrection>` |
| `PostProcessingVolumes` | `CNetworkUtlVectorBase__CHandle__CPostProcessingVolume____` | `CHandle<CPostProcessingVolume>[]` |
| `ToneMapController` | `CHandle__CTonemapController2__` | `CHandle<CToneMapController2>` |
| `TriggerSoundscapeList` | `CUtlVector__CHandle__CEnvSoundscapeTriggerable____` | `CHandle<CEnvSoundscapeTriggerable>[]` |
| `ViewEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPlayerObserverServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ObserverTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPlayerPing`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PingedEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Player` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CPlayerWeaponServices`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ActiveWeapon` | `CHandle__CBasePlayerWeapon__` | `CHandle<CBasePlayerWeapon>` |
| `LastWeapon` | `CHandle__CBasePlayerWeapon__` | `CHandle<CBasePlayerWeapon>` |
| `MyWeapons` | `CNetworkUtlVectorBase__CHandle__CBasePlayerWeapon____` | `CHandle<CBasePlayerWeapon>[]` |

### `CPointAngleSensor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `FacingPercentage` | `CEntityOutputTemplate__float32__` | `float?` |
| `LookAtEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `TargetDir` | `CEntityOutputTemplate__Vector__` | `Vector?` |
| `TargetEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointAngularVelocitySensor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AngularVelocity` | `CEntityOutputTemplate__float32__` | `float?` |
| `TargetEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointClientUIDialog`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointClientUIWorldPanel`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CSSClasses` | `CNetworkUtlVectorBase__CUtlSymbolLarge__` | `string[]` |

### `CPointCommentaryNode`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HViewPosition` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HViewTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `ViewPositionMover` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `ViewTargetAngles` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointEntityFinder`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Filter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |
| `Reference` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointGiveAmmo`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointHurt`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointOrient`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointPrefab`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AssociatedRelayEntity` | `CHandle__CPointPrefab__` | `CHandle<CPointPrefab>` |
| `ProceduralRelaySources` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CPointProximitySensor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Distance` | `CEntityOutputTemplate__float32__` | `float?` |
| `TargetEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPointPush`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |

### `CPointTemplate`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CreatedSpawnGroupHandles` | `CUtlVector__uint32__` | `uint[]` |
| `OnEntitySpawned` | `CEntityOutputTemplate__CUtlVector__CEntityHandle____` | `CEntityHandle[]?` |
| `SpawnedEntityHandles` | `CUtlVector__CEntityHandle__` | `CEntityHandle[]` |

### `CPointValueRemapper`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OutputEntities` | `CNetworkUtlVectorBase__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |
| `Position` | `CEntityOutputTemplate__float32__` | `float?` |
| `PositionDelta` | `CEntityOutputTemplate__float32__` | `float?` |
| `RemapLineEnd` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `RemapLineStart` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `UsingPlayer` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |

### `CPointVelocitySensor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TargetEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Velocity` | `CEntityOutputTemplate__float32__` | `float?` |

### `CPostProcessingVolume`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PostSettings` | `CStrongHandle__InfoForResourceTypeCPostProcessingResource__` | `CStrongHandle<InfoForResourceTypeCPostProcessingResource>` |

### `CPrecipitationVData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ParticlePrecipitationEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `ParticlePrecipitationPostEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |
| `ParticlePrecipitationPuddleEffect` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIParticleSystemDefinition____` | `string` |

### `CPropDoorRotating`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityBlocker` | `CHandle__CEntityBlocker__` | `CHandle<CEntityBlocker>` |

### `CPropDoorRotatingBreakable`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DamageStates` | `CUtlVector__CUtlSymbolLarge__` | `string[]` |

### `CPulseCellLerpCameraSettingsCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Camera` | `CHandle__CPointCamera__` | `CHandle<CPointCamera>` |

### `CPulseCellOutflowListenForEntityOutputCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPulseCellOutflowPlaySceneBase`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Triggers` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellOutflowPlaySceneBaseCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CursorIdToEventId` | `CUtlHashtable__PulseCursorID_t__int32__` | `Dictionary<PulseCursorId, int>` |
| `MainActor` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `SceneInstance` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPulseCellOutflowPlayVCD`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ChoreoScene` | `CStrongHandle__InfoForResourceTypeCChoreoSceneResource__` | `CStrongHandle<InfoForResourceTypeCChoreoSceneResource>` |
| `OutRequirements` | `CUtlVector__CPulseCell_Outflow_PlayVCD_VCDRequirementInfo_t__` | `CPulseCellOutflowPlayVCDVCDRequirementInfo[]` |

### `CPulseCellOutflowPlayVOLineCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SceneInstance` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPulseCellOutflowScriptedSequence`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AdditionalActors` | `CUtlVector__PulseScriptedSequenceData_t__` | `PulseScriptedSequenceData[]` |
| `Triggers` | `CUtlVector__CPulse_OutflowConnection__` | `CPulseOutflowConnection[]` |

### `CPulseCellOutflowScriptedSequenceCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ScriptedSequence` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPulseCellPlaySequenceCursorState`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Target` | `CHandle__CBaseAnimGraph__` | `CHandle<CBaseAnimGraph>` |

### `CPulseGraphInstanceServerEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CPulseServerCursor`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Caller` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CRagdollProp`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DamageEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Killer` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `NavObstacles` | `CUtlVector__INavObstacle___` | `INavObstacle?[]` |
| `PhysicsAttacker` | `CHandle__CBasePlayerPawn__` | `CHandle<CBasePlayerPawn>` |
| `RagAngles` | `CNetworkUtlVectorBase__QAngle__` | `QAngle[]` |
| `RagEnabled` | `CNetworkUtlVectorBase__bool__` | `bool[]` |
| `RagPos` | `CNetworkUtlVectorBase__Vector__` | `Vector[]` |
| `RagdollMaxs` | `CUtlVector__Vector__` | `Vector[]` |
| `RagdollMins` | `CUtlVector__Vector__` | `Vector[]` |

### `CRelativeLocation`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CRelativeTransform`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CResponse`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ExpresserTargets` | `CUtlVector__CAI_Expresser___` | `CAIExpresser?[]` |

### `CRetakeGameRules`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `BombPlanter` | `CHandle__CCSPlayerPawn__` | `CHandle<CCSPlayerPawn>` |

### `CRopeKeyFrame`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EndPoint` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `RopeMaterialModelIndex` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `StartPoint` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CRopeOverlapHit`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `OverlappingLinks` | `CUtlVector__int32__` | `int[]` |

### `CSceneEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Activator` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Actor` | `CHandle__CBaseModelEntity__` | `CHandle<CBaseModelEntity>` |
| `ActorList` | `CNetworkUtlVectorBase__CHandle__CBaseModelEntity____` | `CHandle<CBaseModelEntity>[]` |
| `ActorMap` | `CUtlVector__ActorMapping_t__` | `ActorMapping[]` |
| `HTarget1` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget2` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget3` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget4` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget5` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget6` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget7` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HTarget8` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `InterruptScene` | `CHandle__CSceneEntity__` | `CHandle<CSceneEntity>` |
| `ListManagers` | `CUtlVector__CHandle__CSceneListManager____` | `CHandle<CSceneListManager>[]` |
| `LocatorOrigin` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `NotifySceneCompletion` | `CUtlVector__CHandle__CSceneEntity____` | `CHandle<CSceneEntity>[]` |
| `RemoveActorList` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CSceneEventInfo`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AnimClip` | `CStrongHandle__InfoForResourceTypeCNmClip__` | `CStrongHandle<InfoForResourceTypeCNmClip>` |
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CSceneListManager`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HScenes` | `CHandle__CBaseEntity__[]` | `CHandle<CBaseEntity>[]` |
| `ListManagers` | `CUtlVector__CHandle__CSceneListManager____` | `CHandle<CSceneListManager>[]` |

### `CScriptedSequence`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `ForcedTarget` | `CHandle__CBaseAnimGraph__` | `CHandle<CBaseAnimGraph>` |
| `InteractionMainEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `NextCine` | `CHandle__CScriptedSequence__` | `CHandle<CScriptedSequence>` |
| `TargetEnt` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CShatterGlassShard`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityHittingMe` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Model` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `Neighbors` | `CUtlVector__uint32__` | `uint[]` |
| `PanelVertices` | `CUtlVector__Vector2D__` | `Vector2D[]` |
| `ParentPanel` | `CHandle__CFuncShatterglass__` | `CHandle<CFuncShatterGlass>` |
| `PhysicsEntity` | `CHandle__CShatterGlassShardPhysics__` | `CHandle<CShatterGlassShardPhysics>` |

### `CSkyboxReference`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SkyCamera` | `CHandle__CSkyCamera__` | `CHandle<CSkyCamera>` |

### `CSmokeGrenadeProjectile`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `VoxelFrameData` | `CNetworkUtlVectorBase__uint8__` | `byte[]` |

### `CSoundEventEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnGUIDChanged` | `CEntityOutputTemplate__SndOpEventGuid_t__` | `string?` |

### `CSoundEventPathCornerEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `CornerPairsNetworked` | `CNetworkUtlVectorBase__SoundeventPathCornerPairNetworked_t__` | `SoundEventPathCornerPairNetworked[]` |

### `CSoundOpvarSetAutoRoomEntity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DoorwayPairs` | `CUtlVector__AutoRoomDoorwayPairs_t__` | `AutoRoomDoorwayPairs[]` |
| `TraceResults` | `CUtlVector__SoundOpvarTraceResult_t__` | `SoundOpvarTraceResult[]` |

### `CSoundPatch`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Ent` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CSplineConstraint`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `SplineEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CSprite`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `AttachedToEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `SpriteMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CTakeDamageInfo`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Ability` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Attacker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `DestructibleHitGroupRequests` | `CUtlLeanVector__DestructiblePartDamageRequest_t__` | `DestructiblePartDamageRequest[]` |
| `Inflictor` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CTakeDamageResult`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `DestructibleHitGroupRequests` | `CUtlLeanVector__DestructiblePartDamageRequest_t__` | `DestructiblePartDamageRequest[]` |

### `CTakeDamageSummaryScopeGuard`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Summaries` | `CUtlVector__SummaryTakeDamageInfo_t___` | `SummaryTakeDamageInfo?[]` |

### `CTankTargetChange`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `NewTarget` | `CVariantBase__CVariantDefaultAllocator__` | `CVariantDefaultAllocator` |

### `CTankTrainAI`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TargetEntity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Train` | `CHandle__CFuncTrackTrain__` | `CHandle<CFuncTrackTrain>` |

### `CTeam`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `APlayerControllers` | `CNetworkUtlVectorBase__CHandle__CBasePlayerController____` | `CHandle<CBasePlayerController>[]` |
| `APlayers` | `CNetworkUtlVectorBase__CHandle__CBasePlayerPawn____` | `CHandle<CBasePlayerPawn>[]` |

### `CTestEffect`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PBeam` | `CHandle__CBeam__[]` | `CHandle<CBeam>[]` |

### `CTestPulseIO`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnInternalTestBool` | `CEntityOutputTemplate__bool__` | `bool?` |
| `OnInternalTestColor` | `CEntityOutputTemplate__Color__` | `Color?` |
| `OnInternalTestEntityHandle` | `CEntityOutputTemplate__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>?` |
| `OnInternalTestEntityHandleInt` | `CEntityOutputTemplate__CTestPulseIO_EntityHandleIntArgs_t__` | `CTestPulseIOEntityHandleIntArgs?` |
| `OnInternalTestEntityName` | `CEntityOutputTemplate__CEntityNameString__` | `string?` |
| `OnInternalTestEntityNameString` | `CEntityOutputTemplate__CTestPulseIO_EntityNameStringArgs_t__` | `CTestPulseIOEntityNameStringArgs?` |
| `OnInternalTestFloat` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnInternalTestFloatString` | `CEntityOutputTemplate__CTestPulseIO_FloatStringArgs_t__` | `CTestPulseIOFloatStringArgs?` |
| `OnInternalTestInt` | `CEntityOutputTemplate__int32__` | `int?` |
| `OnInternalTestSchemaEnum` | `CEntityOutputTemplate__TestInputOutputCombinationsEnum_t__` | `TestInputOutputCombinations?` |
| `OnInternalTestString` | `CEntityOutputTemplate__CUtlSymbolLarge__` | `string?` |
| `OnInternalTestStringStringString` | `CEntityOutputTemplate__CTestPulseIO_ThreeStringArgs_t__` | `CTestPulseIOThreeStringArgs?` |
| `OnInternalTestVector` | `CEntityOutputTemplate__Vector__` | `Vector?` |
| `OnVariantBool` | `CEntityOutputTemplate__bool__` | `bool?` |
| `OnVariantColor` | `CEntityOutputTemplate__Color__` | `Color?` |
| `OnVariantFloat` | `CEntityOutputTemplate__float32__` | `float?` |
| `OnVariantInt` | `CEntityOutputTemplate__int32__` | `int?` |
| `OnVariantString` | `CEntityOutputTemplate__CUtlSymbolLarge__` | `string?` |
| `OnVariantVector` | `CEntityOutputTemplate__Vector__` | `Vector?` |

### `CTestPulseIOComponent`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OnComponentTestFunc` | `CEntityOutputTemplate__CUtlSymbolLarge__` | `string?` |

### `CTextureBasedAnimatable`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PositionKeys` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |
| `RotationKeys` | `CStrongHandle__InfoForResourceTypeCTextureBase__` | `CStrongHandle<InfoForResourceTypeCTextureBase>` |

### `CTriggerFan`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HInfoFan` | `CHandle__CInfoFan__` | `CHandle<CInfoFan>` |

### `CTriggerHurt`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HurtEntities` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CTriggerImpact`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `OutputForce` | `CEntityOutputTemplate__Vector__` | `Vector?` |

### `CTriggerLerpObject`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `EntityToWaitForDisconnect` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `HLerpTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `LerpingObjects` | `CUtlVector__lerpdata_t__` | `LerpData[]` |

### `CTriggerLook`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `LookTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CTriggerProximity`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `HMeasureTarget` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `NearestEntityDistance` | `CEntityOutputTemplate__float32__` | `float?` |

### `CTriggerPush`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathSimple` | `CHandle__CPathSimple__` | `CHandle<CPathSimple>` |

### `CTriggerSndSosOpvar`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `TouchingPlayers` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `CTriggerSoundscape`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Soundscape` | `CHandle__CEnvSoundscapeTriggerable__` | `CHandle<CEnvSoundscapeTriggerable>` |
| `Spectators` | `CUtlVector__CHandle__CBasePlayerPawn____` | `CHandle<CBasePlayerPawn>[]` |

### `CTriggerVolume`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Filter` | `CHandle__CBaseFilter__` | `CHandle<CBaseFilter>` |

### `CVoteController`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PotentialIssues` | `CUtlVector__CBaseIssue___` | `CBaseIssue?[]` |
| `VoteOptions` | `CUtlVector__char___` | `string?[]` |

### `DebugDrawBoneTransforms`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Bones` | `CUtlVectorFixedGrowable__CTransform__128__` | `CTransform[]` |

### `DecalGroupOption`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandleCopyable__InfoForResourceTypeIMaterial2__` | `CStrongHandleCopyable<InfoForResourceTypeIMaterial2>` |

### `DestructiblePartDamageRequest`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Attacker` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `DynamicVolumeDef`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Source` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `FogPlayerParams`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Ctrl` | `CHandle__CFogController__` | `CHandle<CFogController>` |

### `FuncMoverMovementSummary`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `PathMover` | `CHandle__CPathMover__` | `CHandle<CPathMover>` |

### `GlobalEntityDatabase`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `List` | `CUtlVector__globalentity_t__` | `GlobalEntity[]` |

### `LerpData`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Ent` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `MagnettedObjects`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `ParticleNode`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `PathMoverEntitySpawn`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Mover` | `CHandle__CFuncMover__` | `CHandle<CFuncMover>` |
| `OtherEntities` | `CUtlVector__CHandle__CBaseEntity____` | `CHandle<CBaseEntity>[]` |

### `PhysObjectHeader`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `PhysicsRagdollPose`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Owner` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |
| `Transforms` | `CNetworkUtlVectorBase__CTransform__` | `CTransform[]` |

### `Ragdoll`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `BoneIndex` | `CUtlVector__int32__` | `int[]` |
| `HierarchyJoints` | `CUtlVector__ragdollhierarchyjoint_t__` | `RagdollHierarchyJoint[]` |
| `List` | `CUtlVector__ragdollelement_t__` | `RagdollElement[]` |

### `RelationshipOverride`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Entity` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `ShardModelDesc`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `InitialPanelVertices` | `CNetworkUtlVectorBase__Vector4D__` | `Vector4D[]` |
| `MaterialBase` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `MaterialDamageOverlay` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |
| `PanelVertices` | `CNetworkUtlVectorBase__Vector2D__` | `Vector2D[]` |

### `SummaryTakeDamageInfo`  <sub>Server</sub>

| Property | Was | Now |
|---|---|---|
| `Target` | `CHandle__CBaseEntity__` | `CHandle<CBaseEntity>` |

### `CSmartPropChoice`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `Options` | `CUtlVector__CSmartPropChoiceOption__` | `CSmartPropChoiceOption[]` |

### `CSmartPropChoiceOption`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `VariableValues` | `CUtlVector__CSmartPropAttributeVariableValue__` | `object?[]` |

### `CSmartPropElement`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `Modifiers` | `CUtlVector__CSmartPropModifier___` | `CSmartPropModifier?[]` |
| `SelectionCriteria` | `CUtlVector__CSmartPropSelectionCriteria___` | `CSmartPropSelectionCriteria?[]` |

### `CSmartPropElementGroup`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CSmartPropElement___` | `CSmartPropElement?[]` |

### `CSmartPropElementPlaceOnPath`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `DefaultPath` | `CUtlVector__CSmartPropAttributeVector__` | `Vector?[]` |

### `CSmartPropElementSmartProp`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `SSmartProp` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCSmartProp____` | `string` |

### `CSmartPropFilterMaterialAttributes`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `AllowedMaterialAttributes` | `CUtlVector__CUtlString__` | `string[]` |
| `DisallowedMaterialAttributes` | `CUtlVector__CUtlString__` | `string[]` |

### `CSmartPropFilterSurfaceProperties`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `AllowedSurfaceProperties` | `CUtlVector__CUtlString__` | `string[]` |
| `DisallowedSurfaceProperties` | `CUtlVector__CUtlString__` | `string[]` |

### `CSmartPropOperationMaterialOverride`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialReplacements` | `CUtlVector__CSmartPropMaterialReplacement__` | `CSmartPropMaterialReplacement[]` |

### `CSmartPropOperationSetMateraialGroupChoice`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialGroupChoices` | `CUtlVector__MaterialGroupChoice_t__` | `MaterialGroupChoice[]` |

### `CSmartPropOperationSetTintColor`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `ColorChoices` | `CUtlVector__ColorChoice_t__` | `ColorChoice[]` |

### `CSmartPropPulseSmartProp`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `SmartProp` | `CStrongHandle__InfoForResourceTypeCSmartProp__` | `CStrongHandle<InfoForResourceTypeCSmartProp>` |

### `CSmartPropRoot`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `Children` | `CUtlVector__CSmartPropElement___` | `CSmartPropElement?[]` |
| `Choices` | `CUtlVector__CSmartPropChoice___` | `CSmartPropChoice?[]` |
| `Modifiers` | `CUtlVector__CSmartPropModifier___` | `CSmartPropModifier?[]` |
| `PulseGraph` | `CStrongHandle__InfoForResourceTypeIPulseGraphDef__` | `CStrongHandle<InfoForResourceTypeIPulseGraphDef>` |
| `Variables` | `CUtlVector__CSmartPropVariable___` | `CSmartPropVariable?[]` |

### `CSmartPropVariableMaterial`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `DefaultValue` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeIMaterial2____` | `string` |

### `CSmartPropVariableMaterialGroup`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `SModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CSmartPropVariableModel`  <sub>Smartprops</sub>

| Property | Was | Now |
|---|---|---|
| `DefaultValue` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CMixControlInputArray`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `VflData` | `CUtlVector__float32__` | `float[]` |

### `CMixSteamAudioDirect`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `Transmission` | `CUtlVector__float32__` | `float[]` |

### `CMixSteamAudioHybridReverb`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `ReverbTime` | `CUtlVector__float32__` | `float[]` |

### `CMixSteamAudioPathing`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `PathingCoeffs` | `CUtlVector__float32__` | `float[]` |
| `VecPathingEQ` | `CUtlVector__float32__` | `float[]` |

### `CMixSubGraphSwitch`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `SubGraphs` | `CUtlVector__CSelectableSubgraph__` | `CSelectableSubGraph[]` |

### `CPreviewList`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `Sounds` | `CUtlVector__CPreviewEntry__` | `CPreviewEntry[]` |

### `CVMixToolGraph`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `EditorEdges` | `CUtlVector__CVMixEditorEdge__` | `CVMixEditorEdge[]` |
| `EditorNodes` | `CUtlVector__CVMixEditorNode__` | `CVMixEditorNode[]` |

### `CVNodeTypeDesc`  <sub>SounddocLib</sub>

| Property | Was | Now |
|---|---|---|
| `InputNames` | `CUtlVector__CUtlString__` | `string[]` |
| `InputTypeIds` | `CUtlVector__int32__` | `int[]` |
| `OutputNames` | `CUtlVector__CUtlString__` | `string[]` |
| `OutputTypeIds` | `CUtlVector__int32__` | `int[]` |

### `CDSPPresetMixGroupModifierTable`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `Table` | `CUtlVector__CDspPresetModifierList__` | `CDspPresetModifierList[]` |

### `CDspPresetModifierList`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `Modifiers` | `CUtlVector__CDSPMixgroupModifier__` | `CDSPMixGroupModifier[]` |

### `CSndBeatPattern`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `PatternFloats` | `CUtlVector__SndBeatEventKeyedFloats_t__` | `SndBeatEventKeyedFloats[]` |
| `PatternKeys` | `CUtlVector__SndBeatEventKeys_t__` | `SndBeatEventKeys[]` |
| `PatternMidi` | `CUtlVector__SndBeatEventKeyedMidiNotes_t__` | `SndBeatEventKeyedMidiNotes[]` |
| `PatternSndEvts` | `CUtlVector__SndBeatEventKeyedSndEvts_t__` | `SndBeatEventKeyedSndEvts[]` |

### `CSndBeatPatternManager`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `ActiveTracks` | `CUtlVector__CSndBeatTrack__` | `CSndBeatTrack[]` |
| `Patterns` | `CUtlVector__CSndBeatPattern__` | `CSndBeatPattern[]` |

### `CSosSoundEventGroupSchema`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `Actions` | `CUtlVector__CSosGroupActionSchema___` | `CSosGroupActionSchema?[]` |

### `CSoundEventMetaData`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `SoundEventVMix` | `CStrongHandle__InfoForResourceTypeCVMixListResource__` | `CStrongHandle<InfoForResourceTypeCVMixListResource>` |

### `SelectedEditItemInfo`  <sub>Soundsystem</sub>

| Property | Was | Now |
|---|---|---|
| `EditItems` | `CUtlVector__SosEditItemInfo_t__` | `SosEditItemInfo[]` |

### `VMixSubGraphSwitchDesc`  <sub>SoundsystemLowlevel</sub>

| Property | Was | Now |
|---|---|---|
| `SubGraphs` | `CUtlVector__CUtlString__` | `string[]` |

### `CAudioMorphData`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `NameHashCodes` | `CUtlVector__uint32__` | `uint[]` |
| `NameStrings` | `CUtlVector__CUtlString__` | `string[]` |
| `Samples` | `CUtlVector__CUtlVector__float32____` | `float[][]` |
| `Times` | `CUtlVector__float32__` | `float[]` |

### `CAudioSentence`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `EmphasisSamples` | `CUtlVector__CAudioEmphasisSample__` | `CAudioEmphasisSample[]` |
| `RunTimePhonemes` | `CUtlVector__CAudioPhonemeTag__` | `CAudioPhonemeTag[]` |

### `CSoundContainerReference`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `Sound` | `CStrongHandle__InfoForResourceTypeCVoiceContainerBase__` | `CStrongHandle<InfoForResourceTypeCVoiceContainerBase>` |

### `CSoundContainerReferenceArray`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `PSounds` | `CUtlVector__CVoiceContainerBase___` | `CVoiceContainerBase?[]` |
| `Sounds` | `CUtlVector__CStrongHandle__InfoForResourceTypeCVoiceContainerBase____` | `CStrongHandle<InfoForResourceTypeCVoiceContainerBase>[]` |

### `CVSound`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `Sentences` | `CUtlLeanVector__CAudioSentence__` | `CAudioSentence[]` |

### `CVoiceContainerGranulator`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `SourceAudio` | `CStrongHandle__InfoForResourceTypeCVoiceContainerBase__` | `CStrongHandle<InfoForResourceTypeCVoiceContainerBase>` |

### `CVoiceContainerRandomSampler`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `GrainResources` | `CUtlVector__CStrongHandle__InfoForResourceTypeCVoiceContainerBase____` | `CStrongHandle<InfoForResourceTypeCVoiceContainerBase>[]` |

### `CVoiceContainerSelector`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `ProbabilityWeights` | `CUtlVector__float32__` | `float[]` |

### `CVoiceContainerSet`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `SoundsToPlay` | `CUtlVector__CVoiceContainerSetElement__` | `CVoiceContainerSetElement[]` |

### `CVoiceContainerStaticAdditiveSynth`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `Tones` | `CUtlVector__CVoiceContainerStaticAdditiveSynth_CTone__` | `CVoiceContainerStaticAdditiveSynthCTone[]` |

### `CVoiceContainerStaticAdditiveSynthCTone`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `Harmonics` | `CUtlVector__CVoiceContainerStaticAdditiveSynth_CHarmonic__` | `CVoiceContainerStaticAdditiveSynthCHarmonic[]` |

### `CVoiceContainerSwitch`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `SoundsToPlay` | `CUtlVector__CSoundContainerReference__` | `CSoundContainerReference[]` |

### `CVoiceContainerTapePlayer`  <sub>SoundsystemVoicecontainers</sub>

| Property | Was | Now |
|---|---|---|
| `SourceAudio` | `CStrongHandle__InfoForResourceTypeCVoiceContainerBase__` | `CStrongHandle<InfoForResourceTypeCVoiceContainerBase>` |

### `CSteamAudioAmbisonicsField`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `Field` | `CUtlVector__float32__` | `float[]` |

### `CSteamAudioBakedDimensionsData`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `InOut` | `CUtlVector__float32__` | `float[]` |
| `InsideSmallSizeField` | `CUtlVector__CSteamAudioAmbisonicsField__` | `CSteamAudioAmbisonicsField[]` |
| `Movables` | `CSteamAudioMovableBakedData__CSteamAudioBakedDimensionsData__` | `CSteamAudioBakedDimensionsData` |
| `OutSideField` | `CUtlVector__CSteamAudioAmbisonicsField__` | `CSteamAudioAmbisonicsField[]` |
| `Size` | `CUtlVector__float32__` | `float[]` |

### `CSteamAudioBakedMaterialsData`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `MaterialTokens` | `CUtlVector__uint32__` | `uint[]` |
| `MaterialWeights` | `CUtlVector__float32__` | `float[]` |

### `CSteamAudioBakedOcclusionData`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `PathingDeviation` | `CUtlVector__float32__` | `float[]` |
| `PathingRatio` | `CUtlVector__float32__` | `float[]` |
| `ReflectionEnergy` | `CUtlVector__float32__` | `float[]` |

### `CSteamAudioBakedPathingData`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `Movables` | `CSteamAudioMovableBakedData__CSteamAudioBakedPathingData__` | `CSteamAudioBakedPathingData` |

### `CSteamAudioBakedReverbData`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `ClusterForProbe` | `CUtlVector__int16__` | `short[]` |
| `Movables` | `CSteamAudioMovableBakedData__CSteamAudioBakedReverbData__` | `CSteamAudioBakedReverbData` |

### `CSteamAudioCompressedReverb`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `Dictionary` | `CUtlVector__float32__` | `float[]` |
| `NumSingularValues` | `CUtlVector__int32__` | `int[]` |
| `VecCompressedData` | `CUtlVector__float32__` | `float[]` |

### `CSteamAudioProbeGrid`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `LineSegments` | `CUtlVector__CSteamAudioProbeLineSegment__` | `CSteamAudioProbeLineSegment[]` |
| `Probes` | `CUtlVector__Vector__` | `Vector[]` |

### `CSteamAudioProbeLineSegment`  <sub>Steamaudio</sub>

| Property | Was | Now |
|---|---|---|
| `Intervals` | `CUtlVector__float32__` | `float[]` |
| `ProbeIndices` | `CUtlVector__int32__` | `int[]` |

### `CTextureSheetDoc`  <sub>Texturelib</sub>

| Property | Was | Now |
|---|---|---|
| `Sequences` | `CUtlStringMap__CTextureSheetDoc_Sequence___` | `Dictionary<string, CTextureSheetDocSequence?>` |

### `CTextureSheetDocSequence`  <sub>Texturelib</sub>

| Property | Was | Now |
|---|---|---|
| `Frames` | `CUtlVector__CTextureSheetDoc_Frame__` | `CTextureSheetDocFrame[]` |

### `CLightRigPostProcessing`  <sub>Toolscene</sub>

| Property | Was | Now |
|---|---|---|
| `PostProcessing` | `CStrongHandle__InfoForResourceTypeCPostProcessingResource__` | `CStrongHandle<InfoForResourceTypeCPostProcessingResource>` |

### `CLightRigSky`  <sub>Toolscene</sub>

| Property | Was | Now |
|---|---|---|
| `SkyMaterial` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `CLightRigVMap`  <sub>Toolscene</sub>

| Property | Was | Now |
|---|---|---|
| `MapName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeVMapResourceData_t____` | `string` |

### `CToolSceneLightRig`  <sub>Toolscene</sub>

| Property | Was | Now |
|---|---|---|
| `PointLights` | `CUtlVector__CLightRigPointLight__` | `CLightRigPointLight[]` |
| `SpotLights` | `CUtlVector__CLightRigSpotLight__` | `CLightRigSpotLight[]` |
| `Suns` | `CUtlVector__CLightRigSunLight__` | `CLightRigSunLight[]` |

### `AutoTagVDataCondition`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `SourceFile` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCVDataResource____` | `string` |

### `CAssetTagInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `TagAliases` | `CUtlVector__CUtlString__` | `string[]` |

### `CAssetTypeConfig`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `AssetTypes` | `CUtlVector__CSimpleAssetTypeInfo___` | `CSimpleAssetTypeInfo?[]` |
| `AssetWarnings` | `CUtlVector__CAssetWarning___` | `CAssetWarning?[]` |
| `SubAssetTypes` | `CUtlVector__CSubassetTypeInfo___` | `CSubAssetTypeInfo?[]` |

### `CAssetWarning`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `Checks` | `CUtlVector__CAssetWarningCheck__` | `CAssetWarningCheck[]` |

### `CAssetWarningCheck`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `ExcludeAddonNames` | `CUtlVector__CUtlString__` | `string[]` |

### `CDetailPropModel`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `ModelName` | `CResourceNameTyped__CWeakHandle__InfoForResourceTypeCModel____` | `string` |

### `CDetailPropType`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `Models` | `CUtlVector__CDetailPropModel__` | `CDetailPropModel[]` |

### `CEngineToolInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `AssetTypes` | `CUtlVector__CUtlString__` | `string[]` |
| `ExcludeFromMods` | `CUtlVector__CUtlString__` | `string[]` |
| `LimitToMods` | `CUtlVector__CUtlString__` | `string[]` |

### `CExternalToolInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `PriorityExts` | `CUtlVector__CUtlString__` | `string[]` |
| `SupportedExts` | `CUtlVector__CUtlString__` | `string[]` |

### `CManifestInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `Resources` | `CUtlVector__CUtlString__` | `string[]` |

### `CModuleManifests`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `Manifests` | `CUtlVector__CManifestInfo__` | `CManifestInfo[]` |

### `CResourceAssetTypeInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `Blocks` | `CUtlVector__ResourceBlockTypeInfo_t__` | `ResourceBlockTypeInfo[]` |
| `CompileDependsOnResourceTypes` | `CUtlVector__CUtlString__` | `string[]` |

### `CSimpleAssetTypeInfo`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `AdditionalExtensions` | `CUtlVector__CUtlString__` | `string[]` |
| `EngineCommands` | `CUtlVector__AssetEngineCommand_t__` | `AssetEngineCommand[]` |
| `ExcludeFromMods` | `CUtlVector__CUtlString__` | `string[]` |
| `HideForRetailMods` | `CUtlVector__CUtlString__` | `string[]` |
| `LimitToMods` | `CUtlVector__CUtlString__` | `string[]` |
| `SuppressSubStrings` | `CUtlVector__CUtlString__` | `string[]` |
| `UnrecognizedOutboundRefsErrorTypeExceptions` | `CUtlVector__CUtlString__` | `string[]` |

### `CToolsConfig`  <sub>Toolutils2</sub>

| Property | Was | Now |
|---|---|---|
| `EngineModulesThatReferenceAssets` | `CUtlVector__CUtlString__` | `string[]` |
| `EngineTools` | `CUtlVector__CEngineToolInfo__` | `CEngineToolInfo[]` |
| `ExternalTools` | `CUtlVector__CExternalToolInfo__` | `CExternalToolInfo[]` |

### `AggregateLODSetUp`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `SwitchDistances` | `CUtlVector__float32__` | `float[]` |

### `AggregateRTProxySceneObject`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `BLASes` | `CUtlVector__RTProxyBLAS_t__` | `RTProxyBLAS[]` |
| `Instances` | `CUtlVector__RTProxyInstanceInfo_t__` | `RTProxyInstanceInfo[]` |

### `AggregateSceneObject`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `AggregateMeshes` | `CUtlVector__AggregateMeshInfo_t__` | `AggregateMeshInfo[]` |
| `FragmentTransforms` | `CUtlVector__matrix3x4_t__` | `Matrix3x4[]` |
| `LodSetups` | `CUtlVector__AggregateLODSetup_t__` | `AggregateLODSetUp[]` |
| `RenderableModel` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `VisClusterMembership` | `CUtlVector__uint16__` | `ushort[]` |

### `BakedLightingInfo`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `BakedShadows` | `CUtlVector__BakedLightingInfo_t_BakedShadowAssignment_t__` | `BakedLightingInfoTBakedShadowAssignment[]` |
| `LightMaps` | `CUtlVector__CStrongHandle__InfoForResourceTypeCTextureBase____` | `CStrongHandle<InfoForResourceTypeCTextureBase>[]` |

### `ClutterSceneObject`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `InstancePositions` | `CUtlVector__Vector__` | `Vector[]` |
| `InstanceScales` | `CUtlVector__float32__` | `float[]` |
| `InstanceTintSrgb` | `CUtlVector__Color__` | `Color[]` |
| `RenderableModel` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |
| `Tiles` | `CUtlVector__ClutterTile_t__` | `ClutterTile[]` |

### `EntityKeyValueData`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `Connections` | `CUtlVector__EntityIOConnectionData_t__` | `EntityIOConnectionData[]` |

### `MaterialOverride`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `Material` | `CStrongHandle__InfoForResourceTypeIMaterial2__` | `CStrongHandle<InfoForResourceTypeIMaterial2>` |

### `NodeData`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `ChildNodeIndices` | `CUtlVector__int32__` | `int[]` |

### `PermEntityLumpData`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `ChildLumps` | `CUtlVector__CStrongHandleCopyable__InfoForResourceTypeCEntityLump____` | `CStrongHandleCopyable<InfoForResourceTypeCEntityLump>[]` |
| `EntityKeyValues` | `CUtlLeanVector__EntityKeyValueData_t__` | `EntityKeyValueData[]` |

### `SceneObject`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `Renderable` | `CStrongHandle__InfoForResourceTypeCRenderMesh__` | `CStrongHandle<InfoForResourceTypeCRenderMesh>` |
| `RenderableModel` | `CStrongHandle__InfoForResourceTypeCModel__` | `CStrongHandle<InfoForResourceTypeCModel>` |

### `World`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `EntityLumps` | `CUtlVector__CStrongHandleCopyable__InfoForResourceTypeCEntityLump____` | `CStrongHandleCopyable<InfoForResourceTypeCEntityLump>[]` |
| `WorldNodes` | `CUtlVector__NodeData_t__` | `NodeData[]` |

### `WorldNode`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `AggregateInstanceStreams` | `CUtlVector__AggregateInstanceStreamOnDiskData_t__` | `AggregateInstanceStreamOnDiskData[]` |
| `AggregateSceneObjects` | `CUtlVector__AggregateSceneObject_t__` | `AggregateSceneObject[]` |
| `ClutterSceneObjects` | `CUtlVector__ClutterSceneObject_t__` | `ClutterSceneObject[]` |
| `ExtraVertexStreamOverrides` | `CUtlVector__ExtraVertexStreamOverride_t__` | `ExtraVertexStreamOverride[]` |
| `ExtraVertexStreams` | `CUtlVector__WorldNodeOnDiskBufferData_t__` | `WorldNodeOnDiskBufferData[]` |
| `LayerNames` | `CUtlVector__CUtlString__` | `string[]` |
| `MaterialOverrides` | `CUtlVector__MaterialOverride_t__` | `MaterialOverride[]` |
| `RtProxies` | `CUtlVector__AggregateRTProxySceneObject_t__` | `AggregateRTProxySceneObject[]` |
| `SceneObjectLayerIndices` | `CUtlVector__uint8__` | `byte[]` |
| `SceneObjects` | `CUtlVector__SceneObject_t__` | `SceneObject[]` |
| `VertexAlbedoStreams` | `CUtlVector__AggregateVertexAlbedoStreamOnDiskData_t__` | `AggregateVertexAlbedoStreamOnDiskData[]` |
| `VertexEmissiveStreams` | `CUtlVector__AggregateVertexEmissiveStreamOnDiskData_t__` | `AggregateVertexEmissiveStreamOnDiskData[]` |
| `VisClusterMembership` | `CUtlVector__uint16__` | `ushort[]` |

### `WorldNodeOnDiskBufferData`  <sub>Worldrenderer</sub>

| Property | Was | Now |
|---|---|---|
| `Data` | `CUtlVector__uint8__` | `byte[]` |
| `InputLayoutFields` | `CUtlVector__RenderInputLayoutField_t__` | `RenderInputLayoutField[]` |
