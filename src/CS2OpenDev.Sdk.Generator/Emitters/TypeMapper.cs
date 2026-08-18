#region

using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

internal static class TypeMapper
{
    private static readonly HashSet<string> CollectionAtoms = new(StringComparer.Ordinal)
    {
        "CUtlVector",
        "CNetworkUtlVectorBase",
        "C_NetworkUtlVectorBase",
        "CUtlLeanVector",
        "CResourceArray",
        "CRelativeArray",
        "CUtlVectorFixedGrowable",
        "C_UtlVectorEmbeddedNetworkVar",
        "CUtlVectorEmbeddedNetworkVar",
        "CUtlLeanVectorFixedGrowable",
        "CHandleBasedAutoList",
        // Real-schema atom that ships without an `inner` field; falls through to
        // `object[]` rather than a stub. (improvement-plan Step 4 callout.)
        "CUtlVectorSIMDPaddedVector"
    };

    private static readonly HashSet<string> IntegerAtoms = new(StringComparer.Ordinal)
    {
        "CEntityIndex",
        "CPlayerSlot",
        "CSplitScreenSlot",
        "ParticleParamID_t",
        "WorldGroupId_t"
    };

    private static readonly HashSet<string> MapAtoms = new(StringComparer.Ordinal)
    {
        "CUtlHashtable",
        "CUtlOrderedMap",
        "CUtlMap",
        // String-keyed dictionary; real schema entries carry only `inner` (the value
        // type) — the key is implied to be a string. (improvement-plan Step 4 callout.)
        "CUtlStringMap",
        // Same shape, same divergence (issue #33): CUtlDict is CUtlMap with a
        // const-char* key, upstream categorises it ATOMIC_T, and its schema
        // entries carry only `inner`. Listed in the CS2_GEN_015 descriptor's
        // deliberate-divergence roster alongside CUtlStringMap.
        "CUtlDict"
    };

    private static readonly HashSet<string> SmartPtrAtoms = new(StringComparer.Ordinal)
    {
        "CSmartPtr",
        "CSharedPtr",
        "CWeakPtr"
    };

    // ── Atomic name classification sets ─────────────────────────────────────────

    private static readonly HashSet<string> StringAtoms = new(StringComparer.Ordinal)
    {
        "CUtlSymbolLarge",
        "CUtlString",
        "CUtlStringToken",
        "CGlobalSymbol",
        "CBufferString",
        "CUtlSymbol",
        "CUtlStringTokenWithStorage",
        "CGlobalSymbolCaseSensitive",
        "CKV3MemberNameWithStorage",
        "CAttachmentNameSymbolWithStorage",
        "CSoundEventName",
        "CEntityNameString",
        "PulseSymbol_t",
        "CModelAnimNameWithDeltas",
        "CModelMaterialGroupName",
        // CResource* string-like atoms. Previously matched by a `CResource` name-prefix
        // fallback that also caught unrelated atoms; that fallback is gone (TM-3), so
        // every CResource* atom that should project to string must be listed here.
        "CResourceString",
        "CResourceName",
        "CResourceNameTyped",
        "CResourceAssetTypeInfo",
        "CResourcePointer",
        "SndOpEventGuid_t",
        "CUtlSymbolUTF8",
        // Issue #33 residue. CGameSoundEventName is the server-side sibling of
        // CSoundEventName (above) — every schema use is a `m_isz*Sound` name on
        // door/button/train entities. CUtlStringTokenNoRegistration is
        // CUtlStringToken (above) minus the debug-registry side effect; both of
        // its schema uses are the key of a CUtlOrderedMap in
        // CEntityAttributeTable, so this entry is what turns those keys into
        // `Dictionary<string, …>` instead of a stub-keyed dictionary.
        "CGameSoundEventName",
        "CUtlStringTokenNoRegistration"
    };

    // Handle atomics project to the typed-handle value structs emitted by
    // HandleTypes.BuildSource. Keying off the atomic name (instead of the
    // schema's old `handle_kind` field, which the current upstream cs2_schema.json
    // no longer carries) makes the dispatch survive the schema-source switch.
    // Entries split by inner-bearing vs untyped because the C# projection differs
    // (`CHandle<T>` for the typed entity handle, `CEntityHandle` for the untyped).
    internal static readonly HashSet<string> TypedHandleAtoms = new(StringComparer.Ordinal)
    {
        "CHandle",
        "CStrongHandle",
        "CStrongHandleCopyable",
        "CWeakHandle"
    };

    internal static readonly HashSet<string> UntypedHandleAtoms = new(StringComparer.Ordinal)
    {
        "CEntityHandle",
        "CStrongHandleVoid"
    };

    // ── Animation / SmartProp / Entity-IO atomics ──────────────────────────
    //
    // Cross-referenced against DumpSource2's `SchemaAtomicCategory_t` enum
    // (PLAIN / T / TT / COLLECTION_OF_T / I) and the hl2sdk cs2 branch where
    // possible. The mappings below pick the C# projection that best preserves
    // the semantic each atomic carries:
    //
    //   ─ "wraps a single value of T" → project as Map(Inner) directly. Used
    //     by animation networked variables and similar storage shells.
    //   ─ "optional reference to T"   → project as Map(Inner) + "?".
    //   ─ "fires events of T"         → project as Map(Inner) + "?" — entity
    //     I/O outputs are conceptually nullable handles to a delegate-like
    //     channel that the consumer reads as "expected value type".
    //   ─ "compressed sequence of T"  → project as Map(Inner) + "[]".
    //
    // None of these carry first-class C# shape today; if upstream ever
    // documents them more concretely we revisit. For now `[NativeName]` round-
    // trips the original atomic name so consumers can decode the wire form
    // themselves if they need to.

    // Atomics that wrap a single value of their `inner` type (storage shells
    // for networked / scripted variables). Schema category: SCHEMA_ATOMIC_T.
    internal static readonly HashSet<string> ValueWrapperAtoms = new(StringComparer.Ordinal)
    {
        "CAnimNetVar",         // animation networked variable (CSS confirms common usage)
        "CAnimValue",          // single-value animation node
        "CAnimScriptParam",    // animation script parameter
        "CSteamAudioMovableBakedData", // wraps the baked-data class
        "CVariantBase"         // wraps the variant allocator class
    };

    // Atomics whose semantic is "optional reference to T": wraps `inner`
    // and may be absent. Schema category: SCHEMA_ATOMIC_T.
    internal static readonly HashSet<string> OptionalRefAtoms = new(StringComparer.Ordinal)
    {
        "CAnimGraph2ParamOptionalRef"
    };

    // SmartProp editor attributes: each is a typed editor value used by the
    // SmartProp editor system. Not networked at runtime; the runtime carries
    // the resolved value, not the attribute wrapper. Per-type projection
    // matches the obvious binding from the atomic name suffix. Schema
    // category: SCHEMA_ATOMIC_PLAIN (no inner).
    internal static readonly Dictionary<string, string> SmartPropAttributeProjections = new(StringComparer.Ordinal)
    {
        ["CSmartPropAttributeBool"]              = "bool?",
        ["CSmartPropAttributeInt"]               = "int?",
        ["CSmartPropAttributeFloat"]             = "float?",
        ["CSmartPropAttributeVector"]            = "Vector?",
        ["CSmartPropAttributeVector2D"]          = "Vector2D?",
        ["CSmartPropAttributeAngles"]            = "QAngle?",
        ["CSmartPropAttributeColor"]             = "Color?",
        ["CSmartPropAttributeMaterialName"]      = "string?",
        ["CSmartPropAttributeMaterialGroup"]     = "string?",
        ["CSmartPropAttributeModelName"]         = "string?",
        ["CSmartPropAttributeStateName"]         = "string?",
        ["CSmartPropAttributeSurfaceProperty"]   = "string?",
        ["CSmartPropAttributeVariableValue"]     = "object?",
        ["CSmartPropVariableComparison"]         = "object?"
    };

    // Atomics projected to `nint` (IntPtr equivalent): C-style function or
    // foreign-resource pointers. Schema treats them as opaque atomics.
    //
    // Primary sources:
    //   HSCRIPT  — hl2sdk:public/vscript/ivscript.h declares
    //              `DECLARE_POINTER_HANDLE(HSCRIPT)` with sentinel `(HSCRIPT)-1`.
    //   IPL*     — Steam Audio (Intel Phonon) SDK; opaque handle types.
    //   BASEPTR / USEPTR / ENTITYFUNCPTR — C-style function pointers used in
    //              entity-system internals. Not portable across builds.
    internal static readonly HashSet<string> ForeignPointerAtoms = new(StringComparer.Ordinal)
    {
        "HSCRIPT",
        "BASEPTR",
        "USEPTR",
        "ENTITYFUNCPTR",
        "IPLScene",
        "IPLProbeBatch",
        "IPLStaticMesh",
        "IPLCompressedEnergyFields"
    };

    // Opaque-but-shaped atomics: schema doesn't expose binary layout, but the
    // type clearly carries some serialised payload. Project as nullable byte
    // arrays so consumers can move them around / hand them to a downstream
    // parser, rather than touching an empty stub class.
    internal static readonly HashSet<string> OpaqueBlobAtoms = new(StringComparer.Ordinal)
    {
        "CPiecewiseCurve",      // animation curve serialised blob
        "CColorGradient",       // color gradient blob
        "CMotionTransform"      // animation transform blob (similar to CTransform shape but not directly exposable)
    };

    // Atomics whose schema doesn't expose meaningful shape; project as `object?`
    // so consumers see "this is opaque" rather than an empty stub class.
    internal static readonly HashSet<string> OpaqueObjectAtoms = new(StringComparer.Ordinal)
    {
        "CAnimVariant",                          // variant value
        "CPulseValueFullType",                   // Pulse scripting type descriptor
        "CAnimGraph2ParamAutoResetOptionalRef"   // animation graph internal (one schema usage, no inner)
    };

    // Atomics projected as `string?`: schema represents them as a name token
    // rather than a structured value. KV3-member-name-set is a list of names,
    // so project that as `string[]?`.
    internal static readonly HashSet<string> NamedStringAtoms = new(StringComparer.Ordinal)
    {
        "CParticleNamedValueRef"
    };

    // Synthetic math types emitted as readonly structs, keyed by C++ name.
    internal static readonly HashSet<string> SyntheticAtoms = new(StringComparer.Ordinal)
    {
        "Vector",
        "VectorAligned",
        "Vector2D",
        "Vector4D",
        "VectorWS",
        "QAngle",
        "Quaternion",
        "QuaternionStorage",
        "Color",
        "CTransform",
        "AABB_t",
        "matrix3x4_t",
        "matrix3x4a_t",
        "fltx4",
        "DegreeEuler",
        "RadianEuler",
        "RotationVector",
        "CRotation",
        "CTransformWS",
        "Range_t",
        // CGraphEditorViewConfig is referenced by CGraphEditorState.m_viewConfig
        // (sounddoc_lib) but the schema never defines it. SyntheticTypes
        // reconstructs the shape from the MGetKV3ClassDefaults metadata on the
        // referencing class — see SyntheticTypes.EmitGraphEditorViewConfig.
        // Listed here so stub-collection treats it as a known type and skips
        // emitting an empty `public partial class CGraphEditorViewConfig {}`.
        "CGraphEditorViewConfig",
        // Rubikon bounding sphere (issue #33). Not reflected in cs2_schema.json —
        // physicslib reflects RnCapsule_t but not RnSphere_t — so the struct is
        // reconstructed in SyntheticTypes.EmitRnSphere from evidence the schema
        // does carry: RnSphereDesc_t is 40 bytes with m_Sphere at offset 24
        // (parent RnShapeDesc_t is 24), pinning RnSphere_t at 16 bytes, and the
        // declared sibling RnCapsule_t is exactly the same shape one segment up
        // (m_vCenter[2] + m_flRadius, 28 bytes). Center + radius is the only
        // 16-byte reading.
        "RnSphere_t"
    };

    // ── Deliberately stubbed atomics (issue #33) ─────────────────────────────
    //
    // Pulse VM internals that deliberately stay empty stub classes. This set is
    // the decision record: an entry here means someone looked at the type and
    // chose the stub over a projection — do not "fix" one without new evidence.
    // Keyed on the bare template name, so the decision covers every current and
    // future instantiation of the template.
    //
    //   CPulseObservableExpression — not a value wrapper, though it looks like
    //     one. The field is 120 bytes in every schema use (offset arithmetic on
    //     CPulseCell_BooleanSwitchState and CPulseCell_TestYieldWithObservables):
    //     it carries the observable expression itself — bindings and evaluation
    //     state — not a T. Projecting `T?` via ValueWrapperAtoms would hand a
    //     consumer a "float" that is actually an expression object.
    //   HPulseCell / HPulseCellBase / HYieldedCursor — Pulse VM handles into
    //     cell-instance and yielded-cursor tables. `nint` (the
    //     ForeignPointerAtoms route) asserts pointer width, and for HYieldedCursor
    //     that is provably false: CPulseCell_TestWaitWithCursorState::CursorState_t
    //     places it at offset 8 and the next field at 20 — 12 bytes, no pointer.
    //     The two 8-byte handles have no source naming their layout (HSCRIPT got
    //     into ForeignPointerAtoms on hl2sdk's DECLARE_POINTER_HANDLE; nothing
    //     comparable exists for Pulse). Every referencing field sits on a Pulse
    //     cell — two of the four classes are literally Test cells — which no
    //     demo consumer reads.
    //
    // Effect: MapAtomicCore still falls through to the stub path for these (the
    // referencing properties need the class to exist), but the fall-through no
    // longer registers a CS2_GEN_003, so the report only lists what is still
    // undecided.
    //
    // Two things this deliberately does not change:
    //   ─ IsKnownAtomicName must keep returning false for these. "Known" is what
    //     suppresses stub emission, and these still need their stubs. Adding an
    //     entry to both sets produces properties referencing a class that is
    //     never emitted.
    //   ─ The CS2_GEN_015 category-drift check still runs for them. If a future
    //     schema recategorises one as a container, that is new evidence and the
    //     Error-severity tripwire should force the re-litigation.
    //
    // What this cannot catch: if upstream ever reflects one of these as a real
    // class, the field stops being an ATOMIC and this set silently stops
    // matching — which is the outcome we would want anyway.
    internal static readonly HashSet<string> DeliberatelyStubbedAtoms = new(StringComparer.Ordinal)
    {
        "CPulseObservableExpression",
        "HPulseCell",
        "HPulseCellBase",
        "HYieldedCursor"
    };

    // ── Name map ─────────────────────────────────────────────────────────────────
    //
    // Set by ModuleEmitter before any emission begins. Maps C++ type names to their
    // computed C# names so that DeclaredClass/Enum references use the same name as
    // their declaration (including [Flags]-aware Flag→Flags renaming, suffix stripping
    // and underscore normalisation).

    private static IReadOnlyDictionary<string, string>? _nameMap;

    // Set of atomic-type names that fell through every classification branch in
    // MapAtomicCore during the current emission. Populated lazily as Map runs, so
    // unit tests calling Map directly (no BeginEmission) don't pay the allocation.
    // ModuleEmitter reads this after emission to (a) emit them as Stubs.cs entries
    // and (b) report a CS2_GEN_003 diagnostic per unknown name.
    private static HashSet<string>? _unresolvedAtomics;

    // Atomics that fell through to a stub while upstream's own `atomicCategory`
    // says they are a container shape. Keyed by the bare template name with the
    // arguments stripped, because that is the unit a fix would act on: one entry
    // in CollectionAtoms covers every instantiation of it. Carries the category
    // and an occurrence count so the diagnostic can say how much is riding on it.
    //
    // Deliberately not keyed by `at.Name` like _unresolvedAtomics is. That set is
    // per stub emitted, which is the right unit for CS2_GEN_003 (one stub, one
    // name); this is per decision a maintainer would make, which is the bare name.
    private static Dictionary<string, (string Category, int Count)>? _categoryDrift;

    // The template name with its arguments removed: `CUtlVector< CGlobalSymbol >`
    // -> `CUtlVector`. Schema 2.0 made atomic `name` fully templated; the sets in
    // this file are still keyed bare, which is the whole subject of the diagnostic.
    internal static string BareAtomName(string name)
    {
        int lt = name.IndexOf('<');
        return lt < 0 ? name : name[..lt].TrimEnd();
    }

    // Whether an atomic's C# projection surfaces its `Inner` type. Categories that
    // collapse to a primitive (string, int, Guid, byte[], synthetic structs, …) do
    // not surface Inner — `CResourceNameTyped<MDLName_t>` projects to plain `string`,
    // so `MDLName_t` never appears in the emitted source and shouldn't trigger a
    // `using` directive for its module. This predicate must mirror the branches in
    // `MapAtomicCore` — keep them in sync.
    internal static bool AtomicProjectionUsesInner(string atomName, string? handleKind)
    {
        atomName = BareAtomName(atomName);
        return handleKind != null
            || TypedHandleAtoms.Contains(atomName)
            || SmartPtrAtoms.Contains(atomName)
            || CollectionAtoms.Contains(atomName)
            || MapAtoms.Contains(atomName)
            || ValueWrapperAtoms.Contains(atomName)
            || OptionalRefAtoms.Contains(atomName)
            || atomName is "CCompressor" or "CEntityOutputTemplate" or "std::pair";
    }

    // Whether an atomic's C# projection surfaces `Inner2` (only map and pair do).
    internal static bool AtomicProjectionUsesInner2(string atomName)
    {
        atomName = BareAtomName(atomName);
        return MapAtoms.Contains(atomName) || atomName == "std::pair";
    }

    internal static void BeginEmission()
    {
        _unresolvedAtomics = new HashSet<string>(StringComparer.Ordinal);
        _categoryDrift = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
    }

    internal static string FormatEnumValue(long value, int? storageSize)
    {
        if (value >= 0)
        {
            return value.ToString();
        }

        return storageSize switch
        {
            1 => ((byte)(sbyte)(int)value).ToString(),
            2 => ((ushort)(short)(int)value).ToString(),
            4 => ((uint)(int)value).ToString(),
            8 => ((ulong)value).ToString(),
            _ => value.ToString()
        };
    }

    internal static IReadOnlyCollection<string> GetUnresolvedAtomics() =>
        _unresolvedAtomics ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    // Bare template names that upstream calls a container and this repo stubs.
    internal static IReadOnlyDictionary<string, (string Category, int Count)> GetAtomicCategoryDrift() =>
        _categoryDrift
        ?? (IReadOnlyDictionary<string, (string, int)>)new Dictionary<string, (string, int)>();

    // Whether an atomic name resolves to a built-in C# projection (string, int,
    // Dictionary, etc.). Used by ModuleEmitter to decide which atomic references
    // need stub-class emission.
    internal static bool IsKnownAtomicName(string name)
    {
        name = BareAtomName(name);
        return StringAtoms.Contains(name)
            || IntegerAtoms.Contains(name)
            || CollectionAtoms.Contains(name)
            || MapAtoms.Contains(name)
            || SmartPtrAtoms.Contains(name)
            || SyntheticAtoms.Contains(name)
            || TypedHandleAtoms.Contains(name)
            || UntypedHandleAtoms.Contains(name)
            || ValueWrapperAtoms.Contains(name)
            || OptionalRefAtoms.Contains(name)
            || ForeignPointerAtoms.Contains(name)
            || OpaqueBlobAtoms.Contains(name)
            || OpaqueObjectAtoms.Contains(name)
            || NamedStringAtoms.Contains(name)
            || SmartPropAttributeProjections.ContainsKey(name)
            || name is "CUtlBinaryBlock" or "std::pair" or "V_uuid_t"
                or "CNetworkedQuantizedFloat" or "KeyValues" or "KeyValues3"
                or "CBitVec" or "CTypedBitVec" or "CCompressor" or "FourVectors"
                or "CEntityOutputTemplate" or "CKV3MemberNameSet" or "SphereBase_t";
    }

    // Returns the canonical C# name for a C++ class/enum, honouring the collision
    // disambiguator the name-map step applied. Falls back to NameHelpers.ToTypeName
    // if the map is empty (test paths that bypass ModuleEmitter).
    internal static string LookupCsName(string cppName, bool isEnum = false, bool isFlags = false) =>
        _nameMap != null && _nameMap.TryGetValue(cppName, out string? mapped)
            ? mapped
            : NameHelpers.ToTypeName(cppName, isEnum, isFlags);

    // ── Dispatch ─────────────────────────────────────────────────────────────────

    internal static string Map(TypeModel type)
    {
        return type switch
        {
            // B3: C++ `char[N]` is a fixed-size string buffer, not a byte array.
            // `char*` is a C-style nullable string. Both project to .NET string.
            // Bare `char` outside of an array/pointer still maps to sbyte via MapBuiltin.
            FixedArrayType { Inner: BuiltinType { Name: "char" } } => "string",
            PtrType { Inner: BuiltinType { Name: "char" } } => "string?",
            BuiltinType bt => MapBuiltin(bt.Name),
            PtrType pt => Map(pt.Inner) + "?",
            FixedArrayType fa => Map(fa.Inner) + "[]",
            DeclaredClassType dc => ResolveName(dc.Name, false),
            DeclaredEnumType de => ResolveName(de.Name, true),
            BitfieldType => "uint",
            AtomicType at => MapAtomic(at),
            UnknownType ut => "object /* " + (ut.Category ?? "?") + " */",
            _ => "object"
        };
    }

    internal static void SetNameMap(IReadOnlyDictionary<string, string> map) => _nameMap = map;

    private static string MapAtomic(AtomicType at)
    {
        string baseResult = MapAtomicCore(at);

        // TM-1: honour the schema's nullable marker. Some branches (HandleKind,
        // SmartPtr) already append `?` themselves; don't double up.
        if (at.Nullable && !baseResult.EndsWith('?'))
        {
            return baseResult + "?";
        }

        return baseResult;
    }

    // If you add a classification branch below, mirror it in IsKnownAtomicName above
    // so ModuleEmitter's stub-collection pre-pass doesn't mis-classify the new type
    // as unknown and emit a spurious stub class + CS2_GEN_003 diagnostic.
    private static string MapAtomicCore(AtomicType at)
    {
        // Classification keys off the bare template name. Schema 2.0 made an
        // atomic's `name` fully templated -- `CUtlVector< CGlobalSymbol >`, not
        // `CUtlVector` -- while every set in this file is keyed bare, so for three
        // schema majors no templated atomic matched anything and all 1,931 of them
        // fell through to the stub path below. `at.Name` is still the right identity
        // for that path: a stub's type name has to be the full instantiation.
        string name = BareAtomName(at.Name);

        // Handle atomics: typed (CHandle, CStrongHandle, …) project to the
        // generic value structs emitted by HandleTypes; untyped (CEntityHandle,
        // CStrongHandleVoid) project to the corresponding non-generic structs.
        // We key off the atomic name because the new upstream schema doesn't
        // carry the old `handle_kind` field at all (the legacy schemas.json did,
        // and the old branch below preserved that compat path for fixtures).
        if (TypedHandleAtoms.Contains(name))
        {
            if (at.Inner is null)
            {
                // Defensive: every schema example we've seen carries `inner` for
                // typed handles. If a future schema variant omits it, fall back
                // to the untyped sibling rather than emitting `CHandle<>` which
                // wouldn't compile.
                return name switch
                {
                    "CHandle" => "CEntityHandle",
                    _ => "CStrongHandleVoid"
                };
            }

            return name + "<" + Map(at.Inner) + ">";
        }

        if (UntypedHandleAtoms.Contains(name))
        {
            return name;
        }

        // Note: an older schema variant carried a `handle_kind` field on each
        // atomic. The current upstream cs2_schema.json doesn't, so we no longer
        // have a dedicated dispatch for it — `TypedHandleAtoms` covers every
        // case that branch used to handle.

        // Storage shells around a single value of `inner` (animation/script
        // wrappers). Projects to the inner type directly. See the comment block
        // around ValueWrapperAtoms for the source-of-truth references.
        if (ValueWrapperAtoms.Contains(name))
        {
            return at.Inner != null ? Map(at.Inner) : "object?";
        }

        // "Optional ref to T": wraps inner; projects as nullable inner.
        if (OptionalRefAtoms.Contains(name))
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "object?";
        }

        // Compressed animation streams: wraps inner as a compressed sequence.
        if (name == "CCompressor")
        {
            return at.Inner != null ? Map(at.Inner) + "[]" : "byte[]";
        }

        // Entity I/O typed output: fires events whose payload is the inner type.
        if (name == "CEntityOutputTemplate")
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "object?";
        }

        // SmartProp editor attributes: per-type C# projection picked from the
        // atomic-name suffix. Editor-only at runtime, but we surface the
        // expected value type so consumers can inspect SmartProp metadata.
        if (SmartPropAttributeProjections.TryGetValue(name, out string? smartPropProjection))
        {
            return smartPropProjection;
        }

        // Foreign-pointer atomics (VScript HSCRIPT, Steam Audio IPL handles,
        // raw C function pointers). Project as `nint` so the bit width and
        // pointer-ness are honest.
        if (ForeignPointerAtoms.Contains(name))
        {
            return "nint";
        }

        // Opaque serialised blobs. Schema doesn't expose binary shape.
        if (OpaqueBlobAtoms.Contains(name))
        {
            return "byte[]?";
        }

        // Genuinely-opaque atomics that don't even carry a clear blob shape.
        if (OpaqueObjectAtoms.Contains(name))
        {
            return "object?";
        }

        // Named-reference atomics: schema represents them as a name token.
        if (NamedStringAtoms.Contains(name))
        {
            return "string?";
        }

        // FourVectors: 3 fltx4 fields per hl2sdk:public/mathlib/ssemath.h. Each
        // fltx4 is a 4-float SIMD pack, so the total payload is 12 floats. We
        // project as `float[]?` because no consumer-facing struct exists today
        // — adding a dedicated synthetic struct is a future enhancement.
        if (name == "FourVectors")
        {
            return "float[]?";
        }

        // KV3 member-name set: list of name tokens.
        if (name == "CKV3MemberNameSet")
        {
            return "string[]?";
        }

        // Bounding sphere base — schema's `inner` carries the float radius type.
        if (name == "SphereBase_t")
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "float?";
        }

        if (SmartPtrAtoms.Contains(name))
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "object?";
        }

        if (CollectionAtoms.Contains(name))
        {
            return at.Inner != null ? Map(at.Inner) + "[]" : "object[]";
        }

        if (MapAtoms.Contains(name))
        {
            if (at is { Inner: not null, Inner2: not null })
            {
                string key = StripNullable(Map(at.Inner));
                return "Dictionary<" + key + ", " + Map(at.Inner2) + ">";
            }

            if (at.Inner != null)
            {
                return "Dictionary<string, " + Map(at.Inner) + ">";
            }

            return "object /* map */";
        }

        if (name == "std::pair" && at is { Inner: not null, Inner2: not null })
        {
            return "(" + Map(at.Inner) + ", " + Map(at.Inner2) + ")";
        }

        if (name == "CUtlBinaryBlock")
        {
            return "byte[]";
        }

        // KV3 serialised payload — projected as the raw serialised string so
        // consumers can hand it to their own KV3 parser rather than reach for
        // a stub class that doesn't carry any structure. Returns the non-nullable
        // form; the `?` is added by the PtrType wrapper in `Map` for the common
        // pointer-to-KeyValues field shape (`m_pKeyValues`), or by the TM-1
        // Nullable post-pass for atomic fields that carry `nullable: true`.
        if (name is "KeyValues3" or "KeyValues")
        {
            return "string";
        }

        // CBitVec / CTypedBitVec: fixed-width bitvectors. Project as byte[]
        // (the only shape the schema carries about them; CTypedBitVec's `inner`
        // is the count, not a meaningful element type).
        if (name is "CBitVec" or "CTypedBitVec")
        {
            return "byte[]";
        }

        if (StringAtoms.Contains(name))
        {
            return "string";
        }

        if (IntegerAtoms.Contains(name))
        {
            return "int";
        }

        if (name == "V_uuid_t")
        {
            return "Guid";
        }

        if (name == "CNetworkedQuantizedFloat")
        {
            return "float";
        }

        // Synthetic math/geometry types: use ToTypeName for the C# name
        if (SyntheticAtoms.Contains(name))
        {
            return NameHelpers.Esc(NameHelpers.ToTypeName(name));
        }

        // Q3: unresolved atomic. Register it for stub emission + CS2_GEN_003 diagnostic
        // and return the sanitized C++ name as the type reference. The stub class
        // emitted by ModuleEmitter will use the same SanitizeName, so the reference
        // resolves at compile time.
        //
        // Deliberately-stubbed atomics take the same path minus the diagnostic:
        // the stub is the decision, so reporting it as a gap on every regen would
        // re-train people to skim CS2_GEN_003 — the habit that let the templated-
        // name break survive three majors.
        if (!DeliberatelyStubbedAtoms.Contains(name))
        {
            _unresolvedAtomics?.Add(at.Name);
        }

        // CS2_GEN_015: upstream classified this as a container and we stubbed it.
        // Only the container categories are reported — ATOMIC_PLAIN and ATOMIC_T
        // stubs are ordinary unresolved atomics and CS2_GEN_003 already has them.
        if (at.AtomicCategory is "ATOMIC_COLLECTION_OF_T" or "ATOMIC_TT"
            && _categoryDrift is not null)
        {
            string bare = BareAtomName(at.Name);
            _categoryDrift[bare] = _categoryDrift.TryGetValue(bare, out (string Category, int Count) prev)
                ? (prev.Category, prev.Count + 1)
                : (at.AtomicCategory, 1);
        }
        return NameHelpers.SanitizeName(at.Name);
    }

    private static string MapBuiltin(string name)
    {
        return name switch
        {
            "bool" => "bool",
            "float32" => "float",
            "float64" => "double",
            "int8" => "sbyte",
            "int16" => "short",
            "int32" => "int",
            "int64" => "long",
            "uint8" => "byte",
            "uint16" => "ushort",
            "uint32" => "uint",
            "uint64" => "ulong",
            "char" => "sbyte",
            "void" => "object",
            _ => "object /* builtin:" + name + " */"
        };
    }

    // Look up the C# name from the name map first (which has flag-aware, suffix-stripped
    // names), falling back to ToTypeName for types not in the schema (stubs, synthetics).
    private static string ResolveName(string cppName, bool isEnum)
    {
        if (_nameMap != null && _nameMap.TryGetValue(cppName, out string? mapped))
        {
            return NameHelpers.Esc(mapped);
        }

        return NameHelpers.Esc(NameHelpers.ToTypeName(cppName, isEnum));
    }

    private static string StripNullable(string typeName)
    {
        if (typeName.Length > 1 && typeName[typeName.Length - 1] == '?')
        {
            return typeName.Substring(0, typeName.Length - 1);
        }

        return typeName;
    }
}
