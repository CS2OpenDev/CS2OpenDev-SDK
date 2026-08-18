#region

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.SchemaLens;

// Renders `schema-lens/state.json` — the combined artifact consumers read.
//
// The file is two layers in one document, and keeping the boundary sharp is
// the point of its design:
//
//   * curated — classes, netName, targetProperty, firstSeenBuild, typeHistory,
//     aliases, ignored. Comes only from migrations; covered by `lensHash`.
//   * derived — module, schemaType, widthBytes, observedFields, schemaBuild.
//     Comes from the schema this run was gated against; changes whenever Valve
//     ships, and therefore deliberately outside the hash.
//
// Output is deterministic to the byte — ordinal-sorted map keys, fixed member
// order, two-space indent, LF, one trailing newline — because CI diffs it: a
// regen on an unchanged repo must produce an unchanged file, and any real
// change must be attributable to the schema or a migration, never to the
// serializer's mood.
internal static class LensStateWriter
{
    internal static string Render(
        LensState state,
        string lensHash,
        SchemaRoot schema,
        IReadOnlyDictionary<string, LensResolvedClass> resolution)
    {
        // Built once per write rather than per field: 3,769 classes, of which 165
        // reduce to a builtin today. The renderer resolves declared classes through
        // this, which is what lets `CInButtonState` report the uint64 it actually
        // carries instead of the null a type-graph walk stops at.
        Dictionary<string, int?> effectiveWidths = new(StringComparer.Ordinal);
        foreach (ClassModel c in schema.Classes)
        {
            effectiveWidths[c.Name] = c.EffectiveBuiltin?.ElementWidth;
        }

        Func<string, int?> declaredClassWidth = name =>
            effectiveWidths.TryGetValue(name, out int? w) ? w : null;

        using MemoryStream buffer = new();
        // Relaxed escaping so `CHandle< CCSPlayerPawn >` reads as itself in a
        // file whose whole audience is people and non-.NET tooling — the
        // default encoder's < is HTML defence this file will never need.
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
               {
                   Indented = true,
                   NewLine = "\n",
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            writer.WriteStartObject();
            writer.WriteString("lensHash", lensHash);

            if (schema.Revision is { } revision)
            {
                writer.WriteString("schemaBuild", revision.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNull("schemaBuild");
            }

            writer.WritePropertyName("classes");
            writer.WriteStartObject();
            foreach ((string className, LensClassState cls) in state.Classes)
            {
                LensResolvedClass resolved = resolution[className];
                writer.WritePropertyName(className);
                writer.WriteStartObject();
                writer.WriteString("netName", cls.NetName);
                writer.WriteString("module", resolved.Module);

                writer.WritePropertyName("fields");
                writer.WriteStartObject();
                foreach ((string canonical, LensFieldEntry field) in cls.Fields)
                {
                    TypeModel leaf = resolved.FieldTypes[canonical];
                    writer.WritePropertyName(canonical);
                    writer.WriteStartObject();
                    writer.WriteString("targetProperty", field.TargetProperty);
                    writer.WriteString("schemaType", LensTypeRenderer.Render(leaf));

                    if (LensTypeRenderer.WidthBytes(leaf, declaredClassWidth) is { } width)
                    {
                        writer.WriteNumber("widthBytes", width);
                    }
                    else
                    {
                        writer.WriteNull("widthBytes");
                    }

                    writer.WriteString("firstSeenBuild", field.FirstSeenBuild);

                    // Omitted when empty rather than written as [] — the key's
                    // presence is itself the signal that the field has lived
                    // through a type change.
                    if (field.TypeHistory.Count > 0)
                    {
                        writer.WritePropertyName("typeHistory");
                        writer.WriteStartArray();
                        foreach (LensTypeShift shift in field.TypeHistory)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("build", shift.Build);
                            writer.WriteString("fromType", shift.FromType);
                            writer.WriteString("toType", shift.ToType);
                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();

                writer.WritePropertyName("aliases");
                writer.WriteStartObject();
                foreach ((string alias, string canonical) in cls.Aliases)
                {
                    writer.WriteString(alias, canonical);
                }

                writer.WriteEndObject();

                writer.WritePropertyName("ignored");
                writer.WriteStartArray();
                foreach (string ignored in cls.Ignored)
                {
                    writer.WriteStringValue(ignored);
                }

                writer.WriteEndArray();

                writer.WritePropertyName("observedFields");
                writer.WriteStartArray();
                foreach (string observed in resolved.ObservedFields)
                {
                    writer.WriteStringValue(observed);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
