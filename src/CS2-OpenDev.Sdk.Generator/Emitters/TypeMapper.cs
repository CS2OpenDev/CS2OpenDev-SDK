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
        "CUtlStringMap"
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
        "CUtlSymbolUTF8"
    };

    // Synthetic math types emitted as readonly structs — keyed by C++ name.
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
        "Range_t"
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

    // Whether an atomic's C# projection surfaces its `Inner` type. Categories that
    // collapse to a primitive (string, int, Guid, byte[], synthetic structs, …) do
    // NOT surface Inner — `CResourceNameTyped<MDLName_t>` projects to plain `string`,
    // so `MDLName_t` never appears in the emitted source and shouldn't trigger a
    // `using` directive for its module. This predicate must mirror the branches in
    // `MapAtomicCore` — keep them in sync.
    internal static bool AtomicProjectionUsesInner(string atomName, string? handleKind) =>
        handleKind != null
        || SmartPtrAtoms.Contains(atomName)
        || CollectionAtoms.Contains(atomName)
        || MapAtoms.Contains(atomName)
        || atomName == "std::pair";

    // Whether an atomic's C# projection surfaces `Inner2` (only map and pair do).
    internal static bool AtomicProjectionUsesInner2(string atomName) =>
        MapAtoms.Contains(atomName)
        || atomName == "std::pair";

    internal static void BeginEmission()
    {
        _unresolvedAtomics = new HashSet<string>(StringComparer.Ordinal);
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

    // Whether an atomic name resolves to a built-in C# projection (string, int,
    // Dictionary, etc.). Used by ModuleEmitter to decide which atomic references
    // need stub-class emission. Atomics with HandleKind != null are also "known"
    // — caller must check that separately, since this overload is name-only.
    internal static bool IsKnownAtomicName(string name) =>
        StringAtoms.Contains(name)
        || IntegerAtoms.Contains(name)
        || CollectionAtoms.Contains(name)
        || MapAtoms.Contains(name)
        || SmartPtrAtoms.Contains(name)
        || SyntheticAtoms.Contains(name)
        || name == "CUtlBinaryBlock"
        || name == "std::pair"
        || name == "V_uuid_t"
        || name == "CNetworkedQuantizedFloat";

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
        // SmartPtr) already append `?` themselves — don't double up.
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
        if (at.HandleKind != null)
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "object?";
        }

        if (SmartPtrAtoms.Contains(at.Name))
        {
            return at.Inner != null ? Map(at.Inner) + "?" : "object?";
        }

        if (CollectionAtoms.Contains(at.Name))
        {
            return at.Inner != null ? Map(at.Inner) + "[]" : "object[]";
        }

        if (MapAtoms.Contains(at.Name))
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

        if (at is { Name: "std::pair", Inner: not null, Inner2: not null })
        {
            return "(" + Map(at.Inner) + ", " + Map(at.Inner2) + ")";
        }

        if (at.Name == "CUtlBinaryBlock")
        {
            return "byte[]";
        }

        if (StringAtoms.Contains(at.Name))
        {
            return "string";
        }

        if (IntegerAtoms.Contains(at.Name))
        {
            return "int";
        }

        if (at.Name == "V_uuid_t")
        {
            return "Guid";
        }

        if (at.Name == "CNetworkedQuantizedFloat")
        {
            return "float";
        }

        // Synthetic math/geometry types — use ToTypeName for the C# name
        if (SyntheticAtoms.Contains(at.Name))
        {
            return NameHelpers.Esc(NameHelpers.ToTypeName(at.Name));
        }

        // Q3: unresolved atomic. Register it for stub emission + CS2_GEN_003 diagnostic
        // and return the sanitized C++ name as the type reference. The stub class
        // emitted by ModuleEmitter will use the same SanitizeName, so the reference
        // resolves at compile time.
        _unresolvedAtomics?.Add(at.Name);
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
