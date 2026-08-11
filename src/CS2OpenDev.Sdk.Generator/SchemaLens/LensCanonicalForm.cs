#region

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

#endregion

namespace CS2SchemaGen.SchemaLens;

// The canonical text form of a LensState, and its hash.
//
// Every migration signs the state it produces via `stateHash`, so the bytes
// being signed need a definition precise enough that two independent
// implementations agree. This is that definition — `lens-canon-1`, documented
// line-by-line in docs/SCHEMA-LENS.md — not a serialization of whatever the
// in-memory types happen to look like. JSON-native value rendering (lowercase
// literals, decimal integers, quoted strings) is part of the spec so that no
// C# enum name or CLR type tag can leak into the preimage and quietly bind the
// hash to this implementation.
//
// The form covers CURATED content only: classes, netName, per-field
// targetProperty / firstSeenBuild / typeHistory, aliases, ignored. It excludes
// everything derived from the current schema — observedFields, schemaType,
// widthBytes — because those change when Valve ships a patch, and a hash that
// revved on every patch would make each migration's stateHash a moving target
// instead of a signature over decisions.
internal static class LensCanonicalForm
{
    internal const string Version = "lens-canon-1";

    internal static string Hash(LensState state)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Render(state)));
        return "sha256:" + Convert.ToHexStringLower(digest);
    }

    // Line grammar (also in docs/SCHEMA-LENS.md, which is the citable spec):
    //
    //   lens-canon-1\n
    //   class/<engineClass>/netName = <json>\n
    //   class/<engineClass>/field/<canonicalPath>/targetProperty = <json>\n
    //   class/<engineClass>/field/<canonicalPath>/firstSeenBuild = <json>\n
    //   class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/build = <json>\n
    //   class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/fromType = <json>\n
    //   class/<engineClass>/field/<canonicalPath>/typeHistory/<i>/toType = <json>\n
    //   class/<engineClass>/alias/<alias> = <json>\n
    //   class/<engineClass>/ignored/<i> = <json>\n
    //
    // Classes, fields and aliases in ordinal key order; ignored entries in
    // ordinal value order, indexed; typeHistory in applied order. Values are
    // JSON — strings quoted, escaping ONLY what JSON itself requires (quote,
    // backslash, control characters), null as `null`, booleans lowercase,
    // integers decimal. Minimal escaping is part of the spec: the default
    // .NET encoder also escapes '<', '&' and friends, which would bind the
    // hash preimage to one library's defensive HTML habits.
    internal static string Render(LensState state)
    {
        StringBuilder sb = new();
        sb.Append(Version).Append('\n');

        foreach ((string className, LensClassState cls) in state.Classes)
        {
            AppendValue(sb, $"class/{className}/netName", cls.NetName);

            foreach ((string canonical, LensFieldEntry field) in cls.Fields)
            {
                string prefix = $"class/{className}/field/{canonical}";
                AppendValue(sb, $"{prefix}/targetProperty", field.TargetProperty);
                AppendValue(sb, $"{prefix}/firstSeenBuild", field.FirstSeenBuild);
                for (int i = 0; i < field.TypeHistory.Count; i++)
                {
                    LensTypeShift shift = field.TypeHistory[i];
                    AppendValue(sb, $"{prefix}/typeHistory/{i}/build", shift.Build);
                    AppendValue(sb, $"{prefix}/typeHistory/{i}/fromType", shift.FromType);
                    AppendValue(sb, $"{prefix}/typeHistory/{i}/toType", shift.ToType);
                }
            }

            foreach ((string alias, string canonical) in cls.Aliases)
            {
                AppendValue(sb, $"class/{className}/alias/{alias}", canonical);
            }

            int index = 0;
            foreach (string ignored in cls.Ignored)
            {
                AppendValue(sb, $"class/{className}/ignored/{index++}", ignored);
            }
        }

        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, string path, string value) =>
        sb.Append(path).Append(" = ").Append(JsonSerializer.Serialize(value, MinimalEscaping)).Append('\n');

    // Not "unsafe" here: the output feeds a hash and a text file, never an
    // HTML sink, and the relaxed encoder is exactly the JSON-required-only
    // escaping the grammar above promises.
    private static readonly JsonSerializerOptions MinimalEscaping = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
