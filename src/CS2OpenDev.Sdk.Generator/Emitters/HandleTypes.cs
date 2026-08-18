#region

using System.Text;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

// Emits the family of typed handle value structs used by CS2's entity and
// resource systems. The schema reflects each handle field as an atomic with a
// pointer to the referenced declared-class (e.g. `m_hPlayerPawn` carries an
// atomic `CHandle` whose `inner` is `CCSPlayerPawn`), and we project them via
// these structs:
//
//   CHandle<T>             — 32-bit packed entity handle (index + serial number)
//   CEntityHandle          — untyped 32-bit entity handle (no `inner` in schema)
//   CStrongHandle<T>       — 64-bit owning resource handle (materials, models, …)
//   CStrongHandleCopyable<T> — variant that doesn't single-own its resource
//   CStrongHandleVoid      — untyped 64-bit owning handle
//   CWeakHandle<T>         — 64-bit non-owning resource handle
//
// Restored as part of fixing the schema-source switch regression: the old
// schema carried a `handle_kind` field on each atomic which TypeMapper keyed
// off; the current upstream schema dropped that field, so every handle was
// falling through to the unresolved-atomic stub path. Keying off the atomic
// `name` instead — the schema HAS that, and these names are stable per the
// SchemaExplorer dump shape.
//
// Bit-layout helpers (`EntityIndex`, `SerialNumber`) are deliberately conservative:
// expose the raw packed `Value` and an `IsValid` check, and leave bit-decoding
// to the consumer until upstream documents the layout authoritatively. Adding
// EntityIndex/SerialNumber accessors later is a non-breaking addition.
//
// The doc comment below used to state a specific split (15 index / 17 serial).
// No artifact this repo consumes documents the packing (engine_constants.json is
// schema enums only), so stating the number in the XML doc shipped a guess to
// every consumer, which is the one place it could do real damage. See
// docs/HANDLES.md.
internal static class HandleTypes
{
    internal const uint InvalidEntityHandle = 0xFFFFFFFFu;
    internal const ulong InvalidResourceHandle = 0xFFFFFFFFFFFFFFFFul;

    internal static string BuildSource(string ns, SchemaRoot schema)
    {
        StringBuilder sb = new();
        ModuleEmitter.AppendSdkHeader(sb, ns, "Handles", schema);
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        EmitEntityHandle(sb);
        sb.AppendLine();
        EmitGeneric(sb, "CHandle", "uint", InvalidEntityHandle.ToString() + "u",
            "Strongly-typed 32-bit entity handle. `Value` is the raw packed `(serial &lt;&lt; index_bits) | index`; the bit split is not documented authoritatively upstream, so the SDK does not decode it. If you decode it downstream, keep the assumption tested on your side. See docs/HANDLES.md.",
            "T");
        sb.AppendLine();
        EmitStrongHandleVoid(sb);
        sb.AppendLine();
        EmitGeneric(sb, "CStrongHandle", "ulong", InvalidResourceHandle.ToString() + "ul",
            "64-bit owning resource handle. Resolves to an `InfoForResourceType{T}`-shaped record in the schema.",
            "T");
        sb.AppendLine();
        EmitGeneric(sb, "CStrongHandleCopyable", "ulong", InvalidResourceHandle.ToString() + "ul",
            "Variant of `CStrongHandle` that does not single-own its resource.",
            "T");
        sb.AppendLine();
        EmitGeneric(sb, "CWeakHandle", "ulong", InvalidResourceHandle.ToString() + "ul",
            "64-bit non-owning resource handle.",
            "T");
        return sb.ToString();
    }

    private static void EmitEntityHandle(StringBuilder sb)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Untyped 32-bit entity handle. Schema atomic <c>CEntityHandle</c>; carries no");
        sb.AppendLine("///     target type. For typed entity references use <see cref=\"CHandle{T}\"/>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public readonly struct CEntityHandle : System.IEquatable<CEntityHandle>");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>The packed value the engine emits when no entity is referenced ({InvalidEntityHandle:X}).</summary>");
        sb.AppendLine($"    public const uint InvalidValue = 0x{InvalidEntityHandle:X}u;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Sentinel \"no entity\" handle.</summary>");
        sb.AppendLine("    public static CEntityHandle Invalid => new(InvalidValue);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Constructs a handle wrapping the given raw packed value.</summary>");
        sb.AppendLine("    public CEntityHandle(uint value) => Value = value;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The raw packed handle value as it appears on the wire.</summary>");
        sb.AppendLine("    public uint Value { get; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>True when this handle refers to something other than the invalid sentinel.</summary>");
        sb.AppendLine("    public bool IsValid => Value != InvalidValue;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool Equals(CEntityHandle other) => Value == other.Value;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override bool Equals(object? obj) => obj is CEntityHandle h && Equals(h);");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override int GetHashCode() => Value.GetHashCode();");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override string ToString() => IsValid ? $\"CEntityHandle(0x{Value:X8})\" : \"CEntityHandle(invalid)\";");
        sb.AppendLine();
        sb.AppendLine("    public static bool operator ==(CEntityHandle a, CEntityHandle b) => a.Equals(b);");
        sb.AppendLine();
        sb.AppendLine("    public static bool operator !=(CEntityHandle a, CEntityHandle b) => !a.Equals(b);");
        sb.AppendLine("}");
    }

    private static void EmitStrongHandleVoid(StringBuilder sb)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Untyped 64-bit owning resource handle. Schema atomic <c>CStrongHandleVoid</c>;");
        sb.AppendLine("///     carries no target type. For typed resource references use <see cref=\"CStrongHandle{T}\"/>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public readonly struct CStrongHandleVoid : System.IEquatable<CStrongHandleVoid>");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>The packed value the engine emits when no resource is referenced ({InvalidResourceHandle:X}).</summary>");
        sb.AppendLine($"    public const ulong InvalidValue = 0x{InvalidResourceHandle:X}ul;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Sentinel \"no resource\" handle.</summary>");
        sb.AppendLine("    public static CStrongHandleVoid Invalid => new(InvalidValue);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Constructs a handle wrapping the given raw value.</summary>");
        sb.AppendLine("    public CStrongHandleVoid(ulong value) => Value = value;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The raw handle value as it appears on the wire.</summary>");
        sb.AppendLine("    public ulong Value { get; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>True when this handle refers to something other than the invalid sentinel.</summary>");
        sb.AppendLine("    public bool IsValid => Value != InvalidValue;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool Equals(CStrongHandleVoid other) => Value == other.Value;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override bool Equals(object? obj) => obj is CStrongHandleVoid h && Equals(h);");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override int GetHashCode() => Value.GetHashCode();");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override string ToString() => IsValid ? $\"CStrongHandleVoid(0x{Value:X16})\" : \"CStrongHandleVoid(invalid)\";");
        sb.AppendLine();
        sb.AppendLine("    public static bool operator ==(CStrongHandleVoid a, CStrongHandleVoid b) => a.Equals(b);");
        sb.AppendLine();
        sb.AppendLine("    public static bool operator !=(CStrongHandleVoid a, CStrongHandleVoid b) => !a.Equals(b);");
        sb.AppendLine("}");
    }

    // Emits a generic handle struct. `backing` is the raw integer field type
    // (`uint` for entity handles, `ulong` for resource handles); `invalidLiteral`
    // is the C# literal for the "no value" sentinel; `tParam` names the target-
    // type generic parameter (kept variable so the same builder can produce
    // `CHandle<T>` and `CWeakHandle<TResource>` styles if we ever want to
    // diverge naming).
    private static void EmitGeneric(
        StringBuilder sb,
        string structName,
        string backing,
        string invalidLiteral,
        string summary,
        string tParam)
    {
        string toStringFormat = backing == "uint" ? "X8" : "X16";

        sb.Append("/// <summary>")
            .Append("\n///     ").Append(summary)
            .AppendLine("\n/// </summary>");
        sb.AppendLine($"/// <typeparam name=\"{tParam}\">The referenced declared-class or resource type.</typeparam>");
        sb.AppendLine($"public readonly struct {structName}<{tParam}> : System.IEquatable<{structName}<{tParam}>>");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The packed value the engine emits when no target is referenced.</summary>");
        sb.AppendLine($"    public const {backing} InvalidValue = {invalidLiteral};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Sentinel \"no target\" handle.</summary>");
        sb.AppendLine($"    public static {structName}<{tParam}> Invalid => new(InvalidValue);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Constructs a handle wrapping the given raw value.</summary>");
        sb.AppendLine($"    public {structName}({backing} value) => Value = value;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The raw handle value as it appears on the wire.</summary>");
        sb.AppendLine($"    public {backing} Value {{ get; }}");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>True when this handle refers to something other than the invalid sentinel.</summary>");
        sb.AppendLine("    public bool IsValid => Value != InvalidValue;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine($"    public bool Equals({structName}<{tParam}> other) => Value == other.Value;");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine($"    public override bool Equals(object? obj) => obj is {structName}<{tParam}> h && Equals(h);");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public override int GetHashCode() => Value.GetHashCode();");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine($"    public override string ToString() => IsValid ? $\"{structName}<{{typeof({tParam}).Name}}>(0x{{Value:{toStringFormat}}})\" : $\"{structName}<{{typeof({tParam}).Name}}>(invalid)\";");
        sb.AppendLine();
        sb.AppendLine($"    public static bool operator ==({structName}<{tParam}> a, {structName}<{tParam}> b) => a.Equals(b);");
        sb.AppendLine();
        sb.AppendLine($"    public static bool operator !=({structName}<{tParam}> a, {structName}<{tParam}> b) => !a.Equals(b);");
        sb.AppendLine("}");
    }
}
