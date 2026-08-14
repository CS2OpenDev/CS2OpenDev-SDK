using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;
using CS2SchemaGen.SchemaLens;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — pure unit tests for TypeMapper.
//
// TypeMapper.Map(TypeModel) is mostly pure. The one piece of static state is
// _nameMap (set via SetNameMap), consulted only by DeclaredClass/DeclaredEnum
// dispatch. Tests that need the map are marked [NotInParallel("NameMap")].

public class TypeMapperTests
{
    // ── MapBuiltin ───────────────────────────────────────────────────────────

    /// <summary>Maps each C++ builtin scalar (<c>int32</c>, <c>float32</c>, <c>uint8</c>, …) to its canonical CLR primitive name.</summary>
    [Test]
    [Arguments("bool",    "bool")]
    [Arguments("float32", "float")]
    [Arguments("float64", "double")]
    [Arguments("int8",    "sbyte")]
    [Arguments("int16",   "short")]
    [Arguments("int32",   "int")]
    [Arguments("int64",   "long")]
    [Arguments("uint8",   "byte")]
    [Arguments("uint16",  "ushort")]
    [Arguments("uint32",  "uint")]
    [Arguments("uint64",  "ulong")]
    [Arguments("void",    "object")]
    public async Task MapBuiltin_NumericAndScalar(string cpp, string expected)
    {
        string actual = TypeMapper.Map(new BuiltinType(cpp));
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Bare <c>char</c> (outside an array/pointer) still maps to <c>sbyte</c> — the B3 string projection only applies to <c>char[N]</c> and <c>char*</c>.</summary>
    [Test]
    public async Task MapBuiltin_BareChar_IsSbyte()
    {
        // A naked `char` outside of an array/pointer remains sbyte. The B3 fix
        // only changes the behaviour for FixedArray<char> and Ptr<char>.
        string actual = TypeMapper.Map(new BuiltinType("char"));
        await Assert.That(actual).IsEqualTo("sbyte");
    }

    // ── B3: char[N] / char* should map to string / string? ───────────────────

    /// <summary>B3: <c>char[N]</c> projects to <c>string</c> (C-style fixed-size string buffer), not <c>sbyte[]</c>.</summary>
    [Test]
    public async Task Map_FixedArrayOfChar_IsString()
    {
        FixedArrayType type = new(18, new BuiltinType("char"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("string");
    }

    /// <summary>B3: <c>char*</c> projects to <c>string?</c> (C-style nullable string), not <c>sbyte?</c>.</summary>
    [Test]
    public async Task Map_PtrToChar_IsNullableString()
    {
        PtrType type = new(new BuiltinType("char"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("string?");
    }

    /// <summary>A non-char fixed array (e.g. <c>int32[4]</c>) projects to a normal CLR array (<c>int[]</c>).</summary>
    [Test]
    public async Task Map_FixedArrayOfInt32_IsIntArray()
    {
        FixedArrayType type = new(4, new BuiltinType("int32"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int[]");
    }

    /// <summary>A non-char pointer (e.g. <c>int32*</c>) projects to a nullable value-type (<c>int?</c>).</summary>
    [Test]
    public async Task Map_PtrToInt32_IsNullableInt()
    {
        PtrType type = new(new BuiltinType("int32"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int?");
    }

    // ── MapAtomic: known shapes ──────────────────────────────────────────────

    /// <summary>Every Valve string-flavoured atomic (<c>CUtlString</c>, <c>CBufferString</c>, <c>CUtlStringToken</c>, …) projects to <c>string</c>.</summary>
    [Test]
    [Arguments("CUtlString",                "string")]
    [Arguments("CUtlSymbolLarge",           "string")]
    [Arguments("CBufferString",             "string")]
    [Arguments("CUtlStringToken",           "string")]
    [Arguments("CUtlSymbolUTF8",            "string")]
    [Arguments("CGlobalSymbolCaseSensitive","string")]
    public async Task MapAtomic_StringAtoms_AreString(string atomName, string expected)
    {
        AtomicType type = new(atomName, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>Index-style atomics (<c>CEntityIndex</c>, <c>CPlayerSlot</c>, …) project to <c>int</c>.</summary>
    [Test]
    [Arguments("CEntityIndex",     "int")]
    [Arguments("CPlayerSlot",      "int")]
    [Arguments("CSplitScreenSlot", "int")]
    public async Task MapAtomic_IntegerAtoms_AreInt(string atomName, string expected)
    {
        AtomicType type = new(atomName, null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary><c>V_uuid_t</c> projects to <see cref="System.Guid"/>.</summary>
    [Test]
    public async Task MapAtomic_VUuid_IsGuid()
    {
        AtomicType type = new("V_uuid_t", null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("Guid");
    }

    /// <summary><c>CNetworkedQuantizedFloat</c> projects to <c>float</c> (the consumer sees the dequantised value).</summary>
    [Test]
    public async Task MapAtomic_NetworkedQuantizedFloat_IsFloat()
    {
        AtomicType type = new("CNetworkedQuantizedFloat", null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("float");
    }

    /// <summary><c>CUtlBinaryBlock</c> projects to <c>byte[]</c>.</summary>
    [Test]
    public async Task MapAtomic_UtlBinaryBlob_IsByteArray()
    {
        AtomicType type = new("CUtlBinaryBlock", null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("byte[]");
    }

    /// <summary>A collection atomic (e.g. <c>CUtlVector&lt;int32&gt;</c>) wraps the inner type as a CLR array.</summary>
    [Test]
    public async Task MapAtomic_Collection_WrapsInnerAsArray()
    {
        AtomicType type = new(
            Name: "CUtlVector",
            HandleKind: null,
            Nullable: false,
            Inner: new BuiltinType("int32"),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int[]");
    }

    /// <summary>A map atomic with both inner types (e.g. <c>CUtlMap&lt;int,float&gt;</c>) projects to <c>Dictionary&lt;K,V&gt;</c>.</summary>
    [Test]
    public async Task MapAtomic_Map_WrapsAsDictionary()
    {
        AtomicType type = new(
            Name: "CUtlMap",
            HandleKind: null,
            Nullable: false,
            Inner: new BuiltinType("int32"),
            Inner2: new BuiltinType("float32"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("Dictionary<int, float>");
    }

    // ── Step 4 follow-up: classify the two real-schema atoms the plan called out ─

    /// <summary>Step 4 follow-up: <c>CUtlStringMap&lt;T&gt;</c> (no <c>inner2</c> in the schema — key is implied string) projects to <c>Dictionary&lt;string, T&gt;</c>.</summary>
    [Test]
    public async Task MapAtomic_CUtlStringMap_WithInnerOnly_IsStringKeyedDictionary()
    {
        // Real schema entries for CUtlStringMap omit `inner2` because the key is
        // implied to be a string. Should resolve to `Dictionary<string, T>`, not
        // a stub class.
        AtomicType type = new(
            Name: "CUtlStringMap",
            HandleKind: null,
            Nullable: false,
            Inner: new BuiltinType("int32"),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("Dictionary<string, int>");
    }

    /// <summary>Step 4 follow-up: <c>CUtlVectorSIMDPaddedVector</c> (no <c>inner</c>) falls into <c>CollectionAtoms</c> and projects to <c>object[]</c> rather than emitting a stub.</summary>
    [Test]
    public async Task MapAtomic_CUtlVectorSIMDPaddedVector_NoInner_IsObjectArray()
    {
        // The schema name implies CUtlVector<Vector> with SIMD padding but the JSON
        // carries no `inner`. Classify into CollectionAtoms so it falls through to
        // `object[]` rather than emitting a stub class.
        AtomicType type = new(
            Name: "CUtlVectorSIMDPaddedVector",
            HandleKind: null,
            Nullable: false,
            Inner: null,
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("object[]");
    }

    /// <summary>Pins that <see cref="TypeMapper.IsKnownAtomicName"/> mirrors every newly-classified atomic — otherwise <c>ModuleEmitter</c>'s stub pre-pass would emit spurious stubs.</summary>
    [Test]
    [Arguments("CUtlStringMap")]
    [Arguments("CUtlVectorSIMDPaddedVector")]
    public async Task IsKnownAtomicName_StepFourFollowups_ReportsKnown(string name)
    {
        // Stub-emission pre-pass keys off IsKnownAtomicName. If the mirror falls out
        // of sync with the classification sets, the type would resolve correctly via
        // Map() but ModuleEmitter would still emit a spurious stub + CS2_GEN_003.
        await Assert.That(TypeMapper.IsKnownAtomicName(name)).IsTrue();
    }

    /// <summary>The presence of a legacy <c>HandleKind</c> field doesn't change the projection — name-based dispatch wins, so <c>CHandle</c> always projects to <c>CHandle&lt;T&gt;</c> regardless of whether the legacy schema field is set.</summary>
    [Test]
    public async Task MapAtomic_Handle_LegacyHandleKindIgnored()
    {
        AtomicType type = new(
            Name: "CHandle",
            HandleKind: "entity", // legacy field — should not change behavior
            Nullable: false,
            Inner: new DeclaredClassType("CFoo", "client"),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("CHandle<CFoo>");
    }

    /// <summary>Current schema shape: <c>CHandle</c> with no <c>handle_kind</c> field projects to the typed value struct <c>CHandle&lt;T&gt;</c>.</summary>
    [Test]
    public async Task MapAtomic_Handle_NewSchema_ProjectsToTypedStruct()
    {
        AtomicType type = new(
            Name: "CHandle",
            HandleKind: null, // current upstream schema omits this field
            Nullable: false,
            Inner: new DeclaredClassType("CCSPlayerPawn", "server"),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("CHandle<CCSPlayerPawn>");
    }

    /// <summary>Resource-handle atomics (<c>CStrongHandle</c>, <c>CStrongHandleCopyable</c>, <c>CWeakHandle</c>) all wrap their inner as <c>{HandleName}&lt;T&gt;</c>.</summary>
    [Test]
    [Arguments("CStrongHandle")]
    [Arguments("CStrongHandleCopyable")]
    [Arguments("CWeakHandle")]
    public async Task MapAtomic_ResourceHandle_ProjectsToTypedStruct(string handleName)
    {
        AtomicType type = new(
            Name: handleName,
            HandleKind: null,
            Nullable: false,
            Inner: new DeclaredClassType("MyResource", "resourcesystem"),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo($"{handleName}<MyResource>");
    }

    /// <summary>Untyped handles (<c>CEntityHandle</c>, <c>CStrongHandleVoid</c>) project to the non-generic structs of the same name.</summary>
    [Test]
    [Arguments("CEntityHandle")]
    [Arguments("CStrongHandleVoid")]
    public async Task MapAtomic_UntypedHandle_ProjectsToNonGenericStruct(string handleName)
    {
        AtomicType type = new(
            Name: handleName,
            HandleKind: null,
            Nullable: false,
            Inner: null,
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo(handleName);
    }

    /// <summary>Defensive: <c>CHandle</c> without an inner falls back to the untyped <c>CEntityHandle</c> rather than emitting `CHandle&lt;&gt;`.</summary>
    [Test]
    public async Task MapAtomic_TypedHandle_NoInner_FallsBackToUntyped()
    {
        AtomicType type = new(
            Name: "CHandle",
            HandleKind: null,
            Nullable: false,
            Inner: null,
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("CEntityHandle");
    }

    /// <summary><see cref="TypeMapper.IsKnownAtomicName"/> reports handle atomics as known so the stub-emission pass doesn't produce spurious empty `CHandle`/`CStrongHandle`/etc. classes.</summary>
    [Test]
    [Arguments("CHandle")]
    [Arguments("CStrongHandle")]
    [Arguments("CStrongHandleCopyable")]
    [Arguments("CStrongHandleVoid")]
    [Arguments("CWeakHandle")]
    [Arguments("CEntityHandle")]
    public async Task IsKnownAtomicName_HandleFamily_IsKnown(string name)
    {
        await Assert.That(TypeMapper.IsKnownAtomicName(name)).IsTrue();
    }

    /// <summary><c>KeyValues</c> / <c>KeyValues3</c> project to a serialised string. The non-nullable form is returned because the common field shape is a pointer-to-KeyValues — the surrounding <see cref="PtrType"/> applies the <c>?</c>.</summary>
    [Test]
    [Arguments("KeyValues")]
    [Arguments("KeyValues3")]
    public async Task MapAtomic_KeyValues_ProjectsToString(string name)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("string");
    }

    /// <summary>A `ptr → KeyValues` field shape (the most common KeyValues field in the schema) projects to <c>string?</c> via the standard PtrType wrapper.</summary>
    [Test]
    public async Task Map_PtrToKeyValues_ProjectsToNullableString()
    {
        PtrType field = new(new AtomicType("KeyValues", HandleKind: null, Nullable: false, Inner: null, Inner2: null));
        await Assert.That(TypeMapper.Map(field)).IsEqualTo("string?");
    }

    /// <summary><c>CBitVec</c> / <c>CTypedBitVec</c> project to <c>byte[]</c> rather than an opaque stub class.</summary>
    [Test]
    [Arguments("CBitVec")]
    [Arguments("CTypedBitVec")]
    public async Task MapAtomic_BitVec_ProjectsToByteArray(string name)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("byte[]");
    }

    // ── Researched atomic projections (P0 follow-up) ─────────────────────────
    //
    // Each test ties the projection back to the schema atomic name. Sources
    // for the mappings live in TypeMapper's `ValueWrapperAtoms` / etc. comment
    // blocks (hl2sdk cs2 branch, DumpSource2's SchemaAtomicCategory_t enum,
    // CounterStrikeSharp's generated-schema projections).

    /// <summary>Value-wrapper atomics project to their inner type — animation networked variables, script params, etc.</summary>
    [Test]
    [Arguments("CAnimNetVar")]
    [Arguments("CAnimValue")]
    [Arguments("CAnimScriptParam")]
    public async Task MapAtomic_ValueWrapper_ProjectsToInner(string name)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false,
            Inner: new BuiltinType("float32"), Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("float");
    }

    /// <summary>Optional-ref atomics project to nullable inner.</summary>
    [Test]
    public async Task MapAtomic_OptionalRef_ProjectsToNullableInner()
    {
        AtomicType type = new("CAnimGraph2ParamOptionalRef", HandleKind: null, Nullable: false,
            Inner: new BuiltinType("bool"), Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("bool?");
    }

    /// <summary><c>CCompressor&lt;T&gt;</c> projects as an array of T (compressed animation sequence).</summary>
    [Test]
    public async Task MapAtomic_Compressor_ProjectsToInnerArray()
    {
        AtomicType type = new("CCompressor", HandleKind: null, Nullable: false,
            Inner: new BuiltinType("float32"), Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("float[]");
    }

    /// <summary><c>CEntityOutputTemplate&lt;T&gt;</c> projects as nullable T — entity I/O fires events whose payload is T.</summary>
    [Test]
    public async Task MapAtomic_EntityOutputTemplate_ProjectsToNullableInner()
    {
        AtomicType type = new("CEntityOutputTemplate", HandleKind: null, Nullable: false,
            Inner: new BuiltinType("int32"), Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("int?");
    }

    /// <summary>SmartProp editor attributes project per-type based on the atomic-name suffix.</summary>
    [Test]
    [Arguments("CSmartPropAttributeBool", "bool?")]
    [Arguments("CSmartPropAttributeInt", "int?")]
    [Arguments("CSmartPropAttributeFloat", "float?")]
    [Arguments("CSmartPropAttributeVector", "Vector?")]
    [Arguments("CSmartPropAttributeVector2D", "Vector2D?")]
    [Arguments("CSmartPropAttributeAngles", "QAngle?")]
    [Arguments("CSmartPropAttributeColor", "Color?")]
    [Arguments("CSmartPropAttributeMaterialName", "string?")]
    [Arguments("CSmartPropAttributeModelName", "string?")]
    [Arguments("CSmartPropAttributeStateName", "string?")]
    [Arguments("CSmartPropAttributeSurfaceProperty", "string?")]
    [Arguments("CSmartPropAttributeMaterialGroup", "string?")]
    public async Task MapAtomic_SmartPropAttribute_ProjectsPerType(string name, string expected)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo(expected);
    }

    /// <summary>Foreign-pointer atomics project to <c>nint</c> — opaque pointers to FFI resources.</summary>
    [Test]
    [Arguments("HSCRIPT")]
    [Arguments("BASEPTR")]
    [Arguments("USEPTR")]
    [Arguments("ENTITYFUNCPTR")]
    [Arguments("IPLScene")]
    [Arguments("IPLProbeBatch")]
    [Arguments("IPLStaticMesh")]
    [Arguments("IPLCompressedEnergyFields")]
    public async Task MapAtomic_ForeignPointer_ProjectsToNint(string name)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("nint");
    }

    /// <summary>Opaque-blob atomics project to nullable byte arrays — schema doesn't expose binary layout but the type carries some serialised payload.</summary>
    [Test]
    [Arguments("CPiecewiseCurve")]
    [Arguments("CColorGradient")]
    [Arguments("CMotionTransform")]
    public async Task MapAtomic_OpaqueBlob_ProjectsToByteArray(string name)
    {
        AtomicType type = new(name, HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("byte[]?");
    }

    /// <summary><c>FourVectors</c> (3 fltx4 = 12 floats per hl2sdk:public/mathlib/ssemath.h) projects to <c>float[]?</c>.</summary>
    [Test]
    public async Task MapAtomic_FourVectors_ProjectsToFloatArray()
    {
        AtomicType type = new("FourVectors", HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("float[]?");
    }

    /// <summary><c>CKV3MemberNameSet</c> projects to a string array.</summary>
    [Test]
    public async Task MapAtomic_KV3MemberNameSet_ProjectsToStringArray()
    {
        AtomicType type = new("CKV3MemberNameSet", HandleKind: null, Nullable: false, Inner: null, Inner2: null);
        await Assert.That(TypeMapper.Map(type)).IsEqualTo("string[]?");
    }

    /// <summary>All researched-projection atomics are reported as known so the stub-emission pre-pass doesn't produce spurious empty classes for them.</summary>
    [Test]
    [Arguments("CAnimNetVar")]
    [Arguments("CAnimGraph2ParamOptionalRef")]
    [Arguments("CCompressor")]
    [Arguments("CEntityOutputTemplate")]
    [Arguments("CSmartPropAttributeFloat")]
    [Arguments("HSCRIPT")]
    [Arguments("CPiecewiseCurve")]
    [Arguments("FourVectors")]
    [Arguments("CParticleNamedValueRef")]
    [Arguments("CAnimVariant")]
    [Arguments("CPulseValueFullType")]
    public async Task IsKnownAtomicName_ResearchedAtomics_IsKnown(string name)
    {
        await Assert.That(TypeMapper.IsKnownAtomicName(name)).IsTrue();
    }

    /// <summary><c>std::pair&lt;A,B&gt;</c> projects to a C# value tuple <c>(A, B)</c>.</summary>
    [Test]
    public async Task MapAtomic_StdPair_IsValueTuple()
    {
        AtomicType type = new(
            Name: "std::pair",
            HandleKind: null,
            Nullable: false,
            Inner: new BuiltinType("int32"),
            Inner2: new BuiltinType("float32"));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("(int, float)");
    }

    // ── TM-1: nullable atomic should append "?" ──────────────────────────────

    /// <summary>TM-1: an atomic carrying <c>nullable: true</c> in the schema appends <c>?</c> to its projected type.</summary>
    [Test]
    public async Task MapAtomic_Nullable_AppendsQuestionMark()
    {
        AtomicType type = new(
            Name: "CUtlString",
            HandleKind: null,
            Nullable: true,
            Inner: null,
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("string?");
    }

    // ── Q3 / TM-2: unresolved atomics should emit stubs, not placeholders ─

    /// <summary>Q3: an unrecognised atomic registers itself for stub emission and is referenced by its sanitized name — never as an <c>object /* … */</c> placeholder.</summary>
    [Test]
    [NotInParallel("NameMap")]
    public async Task MapAtomic_UnknownAtom_ResolvesToStubClassName()
    {
        AtomicType type = new("CFutureUnknownAtom", null, false, null, null);
        string actual = TypeMapper.Map(type);
        // Q3: the unresolved name is registered for stub emission and the mapper
        // returns the sanitized C++ name as the type reference (matching the stub
        // class name that ModuleEmitter will emit) — not "object /* … */".
        await Assert.That(actual).IsEqualTo("CFutureUnknownAtom");
    }

    // ── TM-3: CResource* should match an explicit set, not a name prefix ─────

    /// <summary>TM-3: known <c>CResource*</c> atomics in the explicit allow-set still project to <c>string</c>.</summary>
    [Test]
    [Arguments("CResourceString",       "string")]
    [Arguments("CResourceName",         "string")]
    public async Task MapAtomic_KnownResourceAtoms_AreString(string atomName, string expected)
    {
        AtomicType type = new(atomName, null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>TM-3 hardening: a hypothetical new <c>CResource*</c> atom not in the explicit set falls through to the unknown-atomic path rather than being silently coerced to <c>string</c>.</summary>
    [Test]
    public async Task MapAtomic_FutureCResourcePrefixedType_IsNotSilentlyString()
    {
        // A hypothetical future atom whose name happens to start with "CResource"
        // should not be silently coerced to string — it should fall through to the
        // unknown-atomic path (which Q3 will then handle).
        AtomicType type = new("CResourceVector", null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsNotEqualTo("string");
    }

    // ── DeclaredClass / DeclaredEnum: uses name map ──────────────────────────

    /// <summary>A <see cref="DeclaredClassType"/> reference consults the static name map first, so field type names match the disambiguated declaration name.</summary>
    [Test]
    [NotInParallel("NameMap")]
    public async Task Map_DeclaredClass_UsesNameMapWhenPresent()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal) { ["CFoo_t"] = "CFoo" };
        TypeMapper.SetNameMap(map);
        try
        {
            string actual = TypeMapper.Map(new DeclaredClassType("CFoo_t", "client"));
            await Assert.That(actual).IsEqualTo("CFoo");
        }
        finally
        {
            TypeMapper.SetNameMap(new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    /// <summary>When the name map is empty, <c>DeclaredClass</c> falls back to <see cref="NameHelpers.ToTypeName"/> to derive the C# name.</summary>
    [Test]
    [NotInParallel("NameMap")]
    public async Task Map_DeclaredClass_FallsBackToToTypeNameWhenNotInMap()
    {
        TypeMapper.SetNameMap(new Dictionary<string, string>(StringComparer.Ordinal));
        string actual = TypeMapper.Map(new DeclaredClassType("CFoo_t", "client"));
        await Assert.That(actual).IsEqualTo("CFoo");
    }

    // ── Synthetic atomics resolved via name (Vector, QAngle, etc.) ───────────

    /// <summary>Synthetic atoms (Vector/QAngle/Matrix3x4/…) resolve by name and adopt the PascalCased synthetic struct name.</summary>
    [Test]
    [Arguments("Vector",         "Vector")]
    [Arguments("QAngle",         "QAngle")]
    [Arguments("Quaternion",     "Quaternion")]
    [Arguments("matrix3x4_t",    "Matrix3x4")]
    [Arguments("AABB_t",         "AABB")]
    [Arguments("Range_t",        "Range")]
    public async Task MapAtomic_SyntheticAtom_UsesPascalCasedName(string cppName, string expected)
    {
        AtomicType type = new(cppName, null, false, null, null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── Nested type composition ──────────────────────────────────────────────

    /// <summary>Composition: <c>*(int[4])</c> projects to <c>int[]?</c> (nullable array).</summary>
    [Test]
    public async Task Map_PtrToFixedArray_ComposesNullableArray()
    {
        // *(int[4]) → int[]?
        PtrType type = new(new FixedArrayType(4, new BuiltinType("int32")));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int[]?");
    }

    /// <summary>Composition: <c>(int*)[4]</c> projects to <c>int?[]</c> (array of nullable).</summary>
    [Test]
    public async Task Map_FixedArrayOfPtr_ComposesArrayOfNullable()
    {
        // (int*)[4] → int?[]
        FixedArrayType type = new(4, new PtrType(new BuiltinType("int32")));
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int?[]");
    }

    /// <summary>Composition: <c>CUtlVector&lt;int32*&gt;</c> projects to <c>int?[]</c> (array of nullable).</summary>
    [Test]
    public async Task MapAtomic_CollectionOfPtr_ComposesArrayOfNullable()
    {
        // CUtlVector<int*> → int?[]
        AtomicType type = new(
            Name: "CUtlVector",
            HandleKind: null,
            Nullable: false,
            Inner: new PtrType(new BuiltinType("int32")),
            Inner2: null);
        string actual = TypeMapper.Map(type);
        await Assert.That(actual).IsEqualTo("int?[]");
    }

    // ── Bitfield / Unknown ───────────────────────────────────────────────────

    /// <summary>A <see cref="BitfieldType"/> projects to <c>uint</c> regardless of width — the bit count is metadata, not a CLR shape.</summary>
    [Test]
    public async Task Map_Bitfield_IsUint()
    {
        string actual = TypeMapper.Map(new BitfieldType(3));
        await Assert.That(actual).IsEqualTo("uint");
    }

    /// <summary>Pins the fallback for an unrecognised <see cref="UnknownType"/> category — <c>object /* category */</c> commentary so the bug is visible in the SDK source.</summary>
    [Test]
    public async Task Map_UnknownType_EmitsObjectPlaceholder()
    {
        // Until Q3 lands, the fallback for an unknown category is "object /* … */".
        // This test pins the current behaviour so we notice when it changes.
        string actual = TypeMapper.Map(new UnknownType("mystery"));
        await Assert.That(actual).IsEqualTo("object /* mystery */");
    }

    // ── FormatEnumValue ──────────────────────────────────────────────────────

    /// <summary>Reinterprets negative enum values as their unsigned equivalent at the given storage width (1/2/4/8 bytes) so they compile against the unsigned underlying type.</summary>
    [Test]
    [Arguments(0L,   null, "0")]
    [Arguments(42L,  null, "42")]
    [Arguments(-1L,  1,    "255")]
    [Arguments(-1L,  2,    "65535")]
    [Arguments(-1L,  4,    "4294967295")]
    [Arguments(-1L,  8,    "18446744073709551615")]
    [Arguments(-128L, 1,   "128")]
    public async Task FormatEnumValue_NegativesReinterpretedUnsignedPerStorageSize(long value, int? storage, string expected)
    {
        string actual = TypeMapper.FormatEnumValue(value, storage);
        await Assert.That(actual).IsEqualTo(expected);
    }

    // ── Effective builtin width through a wrapper class ───────────────────────

    /// <summary>A struct that reduces to one builtin reports that builtin's width, which is what stops every consumer maintaining a hand-curated "secretly wide" table.</summary>
    [Test]
    public async Task WidthBytes_ResolvesThroughAWrapperClass()
    {
        // CInButtonState declares one field, uint64[3]. A type-graph walk stops at
        // the struct and returns null; upstream's effectiveBuiltin says uint64/8.
        DeclaredClassType buttons = new("CInButtonState", "server");

        await Assert.That(LensTypeRenderer.WidthBytes(buttons)).IsNull();
        await Assert.That(LensTypeRenderer.WidthBytes(buttons, n => n == "CInButtonState" ? 8 : null))
            .IsEqualTo(8);
    }

    /// <summary>An ordinary struct reduces to nothing, and null means "unknown" rather than a guess.</summary>
    [Test]
    public async Task WidthBytes_OrdinaryStructStaysUnknown()
    {
        DeclaredClassType plain = new("CSomeAggregate", "server");

        await Assert.That(LensTypeRenderer.WidthBytes(plain, _ => null)).IsNull();
    }

    /// <summary>The resolver reaches through pointers and atomic wrappers, not just the top-level type.</summary>
    [Test]
    public async Task WidthBytes_ResolverReachesThroughWrappers()
    {
        TypeModel wrapped = new PtrType(new DeclaredClassType("CInButtonState", "server"));

        await Assert.That(LensTypeRenderer.WidthBytes(wrapped, n => n == "CInButtonState" ? 8 : null))
            .IsEqualTo(8);
    }

    // ── BareAtomName / CS2_GEN_015 category drift ─────────────────────────────

    /// <summary>Strips template arguments so a schema 2.0 name (<c>CUtlVector&lt; CGlobalSymbol &gt;</c>) reduces to the bare name <c>TypeMapper</c>'s classification sets are keyed on.</summary>
    [Test]
    [Arguments("CUtlVector< CGlobalSymbol >", "CUtlVector")]
    [Arguments("CUtlHashtable< CUtlString, int32 >", "CUtlHashtable")]
    [Arguments("CHandle< CBaseEntity >", "CHandle")]
    [Arguments("CUtlString", "CUtlString")]
    [Arguments("std::pair< CGlobalSymbol, bool >", "std::pair")]
    public async Task BareAtomName_StripsTemplateArguments(string full, string expected)
    {
        await Assert.That(TypeMapper.BareAtomName(full)).IsEqualTo(expected);
    }

    /// <summary>Records a container-category atomic that fell through to a stub, aggregated by bare name with a field count — the CS2_GEN_015 measurement.</summary>
    [Test]
    [NotInParallel("AtomicDrift")]
    public async Task CategoryDrift_AggregatesContainerAtomicsByBareName()
    {
        TypeMapper.BeginEmission();

        // Deliberately invented template names. Every real container template is
        // classified now, which is the point of the repair — so exercising the
        // detector needs a container upstream knows about and TypeMapper does not,
        // which is exactly the future case this diagnostic exists to catch.
        //
        // Two instantiations of the same template: one entry, count 2.
        TypeMapper.Map(new AtomicType("CFutureVector< CGlobalSymbol >", null, false, null, null, "ATOMIC_COLLECTION_OF_T"));
        TypeMapper.Map(new AtomicType("CFutureVector< int32 >", null, false, null, null, "ATOMIC_COLLECTION_OF_T"));
        TypeMapper.Map(new AtomicType("CFutureMap< CUtlString, int32 >", null, false, null, null, "ATOMIC_TT"));

        // Not a container category, and an artifact with no discriminator at all
        // (pre-2.1). Both are ordinary CS2_GEN_003 material and must not appear.
        TypeMapper.Map(new AtomicType("CFutureRef< CTransform >", null, false, null, null, "ATOMIC_T"));
        TypeMapper.Map(new AtomicType("CFutureUnknown< int32 >", null, false, null, null, null));

        IReadOnlyDictionary<string, (string Category, int Count)> drift = TypeMapper.GetAtomicCategoryDrift();

        await Assert.That(drift.Count).IsEqualTo(2);
        await Assert.That(drift["CFutureVector"].Category).IsEqualTo("ATOMIC_COLLECTION_OF_T");
        await Assert.That(drift["CFutureVector"].Count).IsEqualTo(2);
        await Assert.That(drift["CFutureMap"].Category).IsEqualTo("ATOMIC_TT");
        await Assert.That(drift["CFutureMap"].Count).IsEqualTo(1);
    }

    /// <summary>An atomic the mapper already projects properly never counts as drift, however upstream categorises it.</summary>
    [Test]
    [NotInParallel("AtomicDrift")]
    public async Task CategoryDrift_IgnoresAtomicsThatAlreadyResolve()
    {
        TypeMapper.BeginEmission();

        // CUtlVector resolves through CollectionAtoms now that classification keys
        // on the bare template name, so it never reaches the unresolved path where
        // drift is recorded. Before the repair this same call recorded drift, which
        // is the regression this case pins.
        TypeMapper.Map(new AtomicType("CUtlVector< int32 >", null, false, null, null, "ATOMIC_COLLECTION_OF_T"));

        await Assert.That(TypeMapper.GetAtomicCategoryDrift().Count).IsEqualTo(0);
    }
}
