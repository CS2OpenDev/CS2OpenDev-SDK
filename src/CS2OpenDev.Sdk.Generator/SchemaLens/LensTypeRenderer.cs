#region

using System.Globalization;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.SchemaLens;

// Renders a schema TypeModel into the flat type-name string state.json carries,
// and derives the effective wire width where one is derivable.
//
// This is deliberately not the C# projection TypeMapper produces. state.json is
// read by non-.NET consumers, and what they need is a stable, recognisable
// rendering of what the schema says — `int32`, `CHandle< CCSPlayerPawn >`,
// `char[128]` — not what this SDK chose to map it to. The strings are also the
// vocabulary typeShift ops quote, so the renderer changing its output for an
// unchanged schema type would forge type-history entries; keep it boring.
internal static class LensTypeRenderer
{
    internal static string Render(TypeModel type) => type switch
    {
        BuiltinType b => b.Name,
        // Suffix notation reads inner-out: `int32*`, `char[128]`, `V_uuid_t*[4]`.
        PtrType p => Render(p.Inner) + "*",
        FixedArrayType a => Render(a.Inner) + "[" + a.Count.ToString(CultureInfo.InvariantCulture) + "]",
        // Atomic names arrive carrying their template text ("CHandle< CBaseEntity >")
        // and are passed through untouched — the schema's spelling is the spec.
        AtomicType a => a.Name,
        DeclaredClassType c => c.Name,
        DeclaredEnumType e => e.Name,
        BitfieldType b => "bitfield:" + b.Count.ToString(CultureInfo.InvariantCulture),
        UnknownType u => u.Category ?? "unknown",
        _ => "unknown"
    };

    // The effective wire width in bytes, or null when it is not derivable from
    // the schema alone. Only a builtin leaf has a width the table can vouch
    // for; ptr and atomic wrappers are unwrapped to look for one because their
    // networked payload is the inner value, not the wrapper. Everything else —
    // declared classes, enums, arrays, bitfields — is a shape whose width
    // depends on knowledge this layer does not have, and a guessed width is
    // worse than an honest null.
    internal static int? WidthBytes(TypeModel type) => WidthBytes(type, null);

    /// <param name="type">The field type to measure.</param>
    /// <param name="declaredClassWidth">
    ///     Resolves a declared class name to the width of the builtin it reduces to,
    ///     or null when it reduces to nothing. Supplied by the caller because this
    ///     renderer sees one type at a time and the answer lives on the class record.
    /// </param>
    internal static int? WidthBytes(TypeModel type, Func<string, int?>? declaredClassWidth) => type switch
    {
        BuiltinType b => BuiltinWidth(b.Name),
        PtrType p => WidthBytes(p.Inner, declaredClassWidth),
        AtomicType { Inner: { } inner } => WidthBytes(inner, declaredClassWidth),

        // A struct that is really one builtin in disguise. Before upstream published
        // this fact the walk stopped here and returned null, and every consumer kept
        // a hand-curated list of the exceptions — `m_pMovementServices.m_nButtons`
        // is declared `CInButtonState` and carries uint64 on the wire. Deriving it
        // is what stops that list from being written.
        DeclaredClassType d when declaredClassWidth is not null => declaredClassWidth(d.Name),
        _ => null
    };

    private static int? BuiltinWidth(string name) => name switch
    {
        "int8" or "uint8" or "bool" => 1,
        "int16" or "uint16" => 2,
        "int32" or "uint32" or "float32" => 4,
        "int64" or "uint64" or "float64" => 8,
        _ => null
    };
}
