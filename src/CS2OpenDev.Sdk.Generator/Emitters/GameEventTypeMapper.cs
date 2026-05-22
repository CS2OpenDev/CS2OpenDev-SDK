namespace CS2SchemaGen.Emitters;

// Maps a KV1 .gameevents type tag to its C# projection. Per the agreement in the
// design discussion, all three player-reference tags
// (`player_controller`, `player_pawn`, `player_controller_and_pawn`) project to
// `int` — the raw userid the engine emits — with the original tag preserved via
// [GameEventFieldType] so demo parsers / dispatchers can resolve to the right
// entity flavour. Layering typed wrappers on top is a non-breaking future change.
//
// Tag inventory (288 events × N fields) drawn from gameevents_schema.json:
//   string                           111
//   short                            124
//   long                              73   (Valve's `long` is 32-bit, not .NET-style int64)
//   float                             80
//   bool                              71
//   byte                              70
//   player_controller_and_pawn        59
//   player_controller                 82
//   player_pawn                       11
//   uint64                            10
//   local                              5   (`CSVCMsgList_GameEvents`-shaped — modelled as byte[]?)
//   int                                4
//   ehandle                            3
internal static class GameEventTypeMapper
{
    internal static string Map(string typeTag) => typeTag switch
    {
        "string" => "string",
        "bool" => "bool",
        "byte" => "byte",
        "short" => "short",
        "int" => "int",
        // Valve's KV1 `long` is a 32-bit integer (Source-engine convention),
        // distinct from .NET `long` (Int64). Project to `int` so consumers see
        // the right range; the `long` tag is preserved on the property via
        // [GameEventFieldType] for wire-shape recovery.
        "long" => "int",
        "float" => "float",
        "uint64" => "ulong",
        "ehandle" => "uint",
        "player_controller" => "int",
        "player_pawn" => "int",
        "player_controller_and_pawn" => "int",
        // `local` appears once in the schema today (demo_start.dota_combatlog_list,
        // typed as a CSVCMsgList_GameEvents protobuf blob). The proto plumbing is
        // outside the SDK's scope, so model it as opaque bytes for now.
        "local" => "byte[]?",
        // Unknown tag — emit `object?` so the SDK still compiles. Adding a new
        // mapping here is a one-line change once the tag is encountered.
        _ => "object?"
    };
}
