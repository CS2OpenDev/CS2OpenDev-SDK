using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 2 — emitter snapshot tests for SyntheticTypes.

public class SyntheticTypesTests
{
    // Shared stamp-less schema — these tests assert on synthetic content shape,
    // not on the schema-revision header (ME-3 is covered elsewhere).
    private static readonly SchemaRoot EmptySchema = new([], []);


    /// <summary>Emits the <c>Vector</c> readonly struct with X/Y/Z init-only float properties.</summary>
    [Test]
    public async Task BuildSource_EmitsVectorStruct()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", definedClassNames: new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        await Assert.That(src).Contains("public readonly struct Vector");
        await Assert.That(src).Contains("public float X { get; init; }");
        await Assert.That(src).Contains("public float Y { get; init; }");
        await Assert.That(src).Contains("public float Z { get; init; }");
    }

    /// <summary>Emits the <c>QAngle</c> struct with Pitch/Yaw/Roll component names (not X/Y/Z).</summary>
    [Test]
    public async Task BuildSource_EmitsQAngleAsPitchYawRoll()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        await Assert.That(src).Contains("public readonly struct QAngle");
        await Assert.That(src).Contains("public float Pitch { get; init; }");
        await Assert.That(src).Contains("public float Yaw");
        await Assert.That(src).Contains("public float Roll");
    }

    /// <summary>Suppresses a synthetic struct when the reflected schema already declares the same type, avoiding duplicate-type compile errors.</summary>
    [Test]
    public async Task BuildSource_SkipsTypesAlreadyDefinedByReflectedSchema()
    {
        // If the schema itself declared a Vector class, the synthetic emitter must
        // not redeclare it — that would produce a duplicate-type compile error.
        HashSet<string> defined = new(StringComparer.Ordinal) { "Vector" };
        string src = SyntheticTypes.BuildSource("CS2Schema", defined, EmptySchema);
        // Anchor on the newline after the type name so this assertion doesn't match
        // VectorAligned, Vector2D, Vector4D, VectorWS lines.
        await Assert.That(src).DoesNotContain("public readonly struct Vector\n");
        // The other Vector* synthetics should still be emitted.
        await Assert.That(src).Contains("public readonly struct VectorAligned");
    }

    /// <summary>Renames C++ <c>_t</c>-suffixed synthetics (e.g. <c>matrix3x4_t</c> → <c>Matrix3x4</c>) and preserves the original name via <c>[NativeName]</c>.</summary>
    [Test]
    public async Task BuildSource_RenamesCppTypedefSuffixes_AndCarriesNativeNameAttribute()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        // matrix3x4_t → Matrix3x4 with [NativeName("matrix3x4_t")]
        await Assert.That(src).Contains("public readonly struct Matrix3x4");
        await Assert.That(src).Contains("[NativeName(\"matrix3x4_t\")]");
        // AABB_t → AABB
        await Assert.That(src).Contains("public readonly struct AABB");
        await Assert.That(src).Contains("[NativeName(\"AABB_t\")]");
    }

    /// <summary>Pins the full set of synthetic struct names emitted (Vector/QAngle/Matrix3x4/AABB/…); regression-guards accidental removal.</summary>
    [Test]
    public async Task BuildSource_EmitsExpectedSyntheticTypeNames()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        string[] expected = [
            "struct Vector", "struct VectorAligned", "struct Vector2D", "struct Vector4D",
            "struct VectorWS", "struct QAngle", "struct Quaternion", "struct QuaternionStorage",
            "struct Color", "struct CTransform", "struct AABB",
            "struct Matrix3x4", "struct Matrix3x4a", "struct Fltx4",
            "struct DegreeEuler", "struct RadianEuler", "struct RotationVector",
            "struct CRotation", "struct CTransformWS", "struct Range",
            "struct CGraphEditorViewAxis", "struct CGraphEditorViewConfig"
        ];
        foreach (string name in expected)
        {
            await Assert.That(src).Contains(name);
        }
    }

    // ── CGraphEditorViewConfig synthetic (recovered from KV3 defaults) ───────

    /// <summary>Emits <c>CGraphEditorViewAxis</c> with the five fields recovered from the <c>MGetKV3ClassDefaults</c> metadata: float Pos / int ScrollPos / float Min / Max / Scale.</summary>
    [Test]
    public async Task BuildSource_EmitsCGraphEditorViewAxisWithFiveFields()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        await Assert.That(src).Contains("public readonly struct CGraphEditorViewAxis");
        await Assert.That(src).Contains("public float Pos { get; init; }");
        await Assert.That(src).Contains("public int ScrollPos { get; init; }");
        await Assert.That(src).Contains("public float Min { get; init; }");
        await Assert.That(src).Contains("public float Max { get; init; }");
        await Assert.That(src).Contains("public float Scale { get; init; }");
    }

    /// <summary><c>CGraphEditorViewConfig</c> has two <c>CGraphEditorViewAxis</c> fields (XAxis / YAxis) — the schema shape that matches CGraphEditorState's 40-byte size and its KV3 defaults JSON.</summary>
    [Test]
    public async Task BuildSource_EmitsCGraphEditorViewConfigWithTwoAxes()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        await Assert.That(src).Contains("public readonly struct CGraphEditorViewConfig");
        await Assert.That(src).Contains("public CGraphEditorViewAxis XAxis");
        await Assert.That(src).Contains("public CGraphEditorViewAxis YAxis");
    }

    /// <summary>Native-name attributes round-trip the schema field identifiers (XAxis / YAxis / pos / scrollpos / min / max / scale) so downstream tooling can recover the original KV3 keys.</summary>
    [Test]
    public async Task BuildSource_GraphEditorView_NativeNamesPreserveSchemaKeys()
    {
        string src = SyntheticTypes.BuildSource("CS2Schema", new HashSet<string>(StringComparer.Ordinal), EmptySchema);
        await Assert.That(src).Contains("[NativeName(\"XAxis\")]");
        await Assert.That(src).Contains("[NativeName(\"YAxis\")]");
        await Assert.That(src).Contains("[NativeName(\"pos\")]");
        await Assert.That(src).Contains("[NativeName(\"scrollpos\")]");
        await Assert.That(src).Contains("[NativeName(\"scale\")]");
    }

    /// <summary>When the reflected schema would declare CGraphEditorViewConfig itself (a future schema dump might pick it up), the synthetic emitter steps aside to avoid a duplicate.</summary>
    [Test]
    public async Task BuildSource_GraphEditorViewConfig_SuppressedWhenSchemaDeclaresIt()
    {
        HashSet<string> defined = new(StringComparer.Ordinal) { "CGraphEditorViewConfig" };
        string src = SyntheticTypes.BuildSource("CS2Schema", defined, EmptySchema);
        await Assert.That(src).DoesNotContain("public readonly struct CGraphEditorViewConfig");
    }
}
