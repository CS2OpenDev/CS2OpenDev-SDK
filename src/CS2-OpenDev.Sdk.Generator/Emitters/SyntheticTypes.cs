#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

internal static class SyntheticTypes
{
    internal static string BuildSource(string ns, HashSet<string> definedClassNames, SchemaRoot schema)
    {
        StringBuilder sb = new();
        ModuleEmitter.AppendSdkHeader(sb, ns, "Synthetics", schema);
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8618");
        sb.AppendLine();
        sb.AppendLine("#region");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();
        sb.AppendLine("#endregion");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        EmitXyz(sb, "Vector", "3D vector.", definedClassNames);
        EmitAligned(sb, "VectorAligned", 16, "16-byte-aligned 3D vector.", definedClassNames, "x", "y", "z");
        EmitXy(sb, "Vector2D", "2D vector.", definedClassNames);
        EmitXyzw(sb, "Vector4D", "4D vector.", definedClassNames);
        EmitXyz(sb, "VectorWS", "World-space 3D vector.", definedClassNames);

        if (!definedClassNames.Contains("QAngle"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Euler angles in degrees.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("/// <remarks>");
            sb.AppendLine("///     x/y/z are exposed as Pitch, Yaw, and Roll respectively.");
            sb.AppendLine("/// </remarks>");
            sb.AppendLine("public readonly struct QAngle");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Pitch — rotation around the X axis, in degrees.</summary>");
            sb.AppendLine("    [NativeName(\"x\")]");
            sb.AppendLine("    public float Pitch { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Yaw — rotation around the Y axis, in degrees.</summary>");
            sb.AppendLine("    [NativeName(\"y\")]");
            sb.AppendLine("    public float Yaw { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Roll — rotation around the Z axis, in degrees.</summary>");
            sb.AppendLine("    [NativeName(\"z\")]");
            sb.AppendLine("    public float Roll { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        EmitXyzw(sb, "Quaternion", "Unit quaternion.", definedClassNames);
        EmitXyzw(sb, "QuaternionStorage", "Quaternion in storage form.", definedClassNames);

        if (!definedClassNames.Contains("Color"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     RGBA color, 8 bits per channel.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public readonly struct Color");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Red channel (0–255).</summary>");
            sb.AppendLine("    [NativeName(\"r\")]");
            sb.AppendLine("    public byte R { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Green channel (0–255).</summary>");
            sb.AppendLine("    [NativeName(\"g\")]");
            sb.AppendLine("    public byte G { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Blue channel (0–255).</summary>");
            sb.AppendLine("    [NativeName(\"b\")]");
            sb.AppendLine("    public byte B { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Alpha channel (0–255).</summary>");
            sb.AppendLine("    [NativeName(\"a\")]");
            sb.AppendLine("    public byte A { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (!definedClassNames.Contains("CTransform"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Position and rotation transform.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public readonly struct CTransform");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Translation component.</summary>");
            sb.AppendLine("    [NativeName(\"position\")]");
            sb.AppendLine("    public VectorAligned Position { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Rotation component.</summary>");
            sb.AppendLine("    [NativeName(\"rotation\")]");
            sb.AppendLine("    public Quaternion Rotation { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (!definedClassNames.Contains("AABB_t"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Axis-aligned bounding box.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("[NativeName(\"AABB_t\")]");
            sb.AppendLine("public readonly struct AABB");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Minimum corner of the bounding box.</summary>");
            sb.AppendLine("    [NativeName(\"mins\")]");
            sb.AppendLine("    public Vector Mins { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Maximum corner of the bounding box.</summary>");
            sb.AppendLine("    [NativeName(\"maxs\")]");
            sb.AppendLine("    public Vector Maxs { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        EmitFloatArray(sb, "matrix3x4_t", "Matrix3x4", "3×4 transform matrix (12 floats, row-major).", 12, 0, definedClassNames);
        EmitFloatArray(sb, "matrix3x4a_t", "Matrix3x4a", "16-byte-aligned 3×4 transform matrix.", 12, 16, definedClassNames);
        EmitFloatArray(sb, "fltx4", "Fltx4", "SIMD 4-float vector (fltx4 / __m128). 16 bytes.", 4, 0, definedClassNames);

        EmitXyz(sb, "DegreeEuler", "Euler angles in degrees.", definedClassNames);
        EmitXyz(sb, "RadianEuler", "Euler angles in radians.", definedClassNames);
        EmitXyz(sb, "RotationVector", "Rotation vector (3 floats).", definedClassNames);
        EmitXyzw(sb, "CRotation", "Rotation in quaternion form.", definedClassNames);

        if (!definedClassNames.Contains("CTransformWS"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     World-space position and rotation transform.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public readonly struct CTransformWS");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Translation component in world space.</summary>");
            sb.AppendLine("    [NativeName(\"position\")]");
            sb.AppendLine("    public Vector Position { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Rotation component in world space.</summary>");
            sb.AppendLine("    [NativeName(\"rotation\")]");
            sb.AppendLine("    public Quaternion Rotation { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (!definedClassNames.Contains("Range_t"))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Scalar range with minimum and maximum bounds.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("[NativeName(\"Range_t\")]");
            sb.AppendLine("public readonly struct Range");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Minimum value of the range.</summary>");
            sb.AppendLine("    [NativeName(\"min\")]");
            sb.AppendLine("    public float Min { get; init; }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Maximum value of the range.</summary>");
            sb.AppendLine("    [NativeName(\"max\")]");
            sb.AppendLine("    public float Max { get; init; }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void EmitAligned(StringBuilder sb, string name, int pack, string desc,
        HashSet<string> skip, params string[] props)
    {
        if (skip.Contains(name))
        {
            return;
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"///     {desc}");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"[StructLayout(LayoutKind.Sequential, Pack = {pack})]");
        sb.AppendLine($"public readonly struct {name}");
        sb.AppendLine("{");
        for (int i = 0; i < props.Length; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            string p = props[i];
            string upper = char.ToUpperInvariant(p[0]) + p.Substring(1);
            sb.AppendLine($"    /// <summary>The {upper} component.</summary>");
            sb.AppendLine($"    [NativeName(\"{p}\")]");
            sb.AppendLine($"    public float {upper} {{ get; init; }}");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // cppName: used for skip-check (C++ name in definedClassNames)
    // csName:  emitted as the C# struct name (idiomatic PascalCase without _t)
    private static void EmitFloatArray(StringBuilder sb, string cppName, string csName, string desc,
        int count, int pack, HashSet<string> skip)
    {
        if (skip.Contains(cppName))
        {
            return;
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"///     {desc}");
        sb.AppendLine($"/// </summary>");
        if (csName != cppName)
        {
            sb.AppendLine($"[NativeName(\"{cppName}\")]");
        }

        if (pack > 0)
        {
            sb.AppendLine($"[StructLayout(LayoutKind.Sequential, Pack = {pack})]");
        }

        sb.AppendLine($"public readonly struct {csName}");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Raw matrix data — {count} elements in row-major order.</summary>");
        sb.AppendLine("    [NativeName(\"values\")]");
        sb.AppendLine("    public float[] Values { get; init; }");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ── Helper emitters ──────────────────────────────────────────────────────────

    private static void EmitXy(StringBuilder sb, string name, string desc, HashSet<string> skip)
    {
        if (skip.Contains(name))
        {
            return;
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"///     {desc}");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public readonly struct {name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The X component.</summary>");
        sb.AppendLine("    [NativeName(\"x\")]");
        sb.AppendLine("    public float X { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The Y component.</summary>");
        sb.AppendLine("    [NativeName(\"y\")]");
        sb.AppendLine("    public float Y { get; init; }");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitXyz(StringBuilder sb, string name, string desc, HashSet<string> skip)
    {
        if (skip.Contains(name))
        {
            return;
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"///     {desc}");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public readonly struct {name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The X component.</summary>");
        sb.AppendLine("    [NativeName(\"x\")]");
        sb.AppendLine("    public float X { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The Y component.</summary>");
        sb.AppendLine("    [NativeName(\"y\")]");
        sb.AppendLine("    public float Y { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The Z component.</summary>");
        sb.AppendLine("    [NativeName(\"z\")]");
        sb.AppendLine("    public float Z { get; init; }");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitXyzw(StringBuilder sb, string name, string desc, HashSet<string> skip)
    {
        if (skip.Contains(name))
        {
            return;
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"///     {desc}");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public readonly struct {name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The X component.</summary>");
        sb.AppendLine("    [NativeName(\"x\")]");
        sb.AppendLine("    public float X { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The Y component.</summary>");
        sb.AppendLine("    [NativeName(\"y\")]");
        sb.AppendLine("    public float Y { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The Z component.</summary>");
        sb.AppendLine("    [NativeName(\"z\")]");
        sb.AppendLine("    public float Z { get; init; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The W component.</summary>");
        sb.AppendLine("    [NativeName(\"w\")]");
        sb.AppendLine("    public float W { get; init; }");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
