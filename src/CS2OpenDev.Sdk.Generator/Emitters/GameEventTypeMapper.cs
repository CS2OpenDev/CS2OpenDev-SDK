#region

using CS2SchemaGen.Models;

#endregion

namespace CS2SchemaGen.Emitters;

// Maps a KV1 .gameevents type tag to its C# projection. The original tag is
// preserved via [GameEventFieldType] so demo parsers / dispatchers can resolve
// to the right entity flavour.
//
// The player-reference tags do not all project to `int`. Only the half of a
// player reference that carries a userid does; the half that carries a pawn
// handle projects to `uint`, like the plain `ehandle` tag. Which half a given
// field is comes from GameEventPawnExpansion, not from the tag — see
// GameEventFieldModel.IsPawnHandle and the Map(GameEventFieldModel, …) overload
// below. Projecting `player_pawn` to `int` was wrong on both counts: the value
// is a handle, and it lives under a different wire key.
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
    // A consumer-supplied override wins over the built-in projection. The same
    // override drives the generated factory (see GameEventFactoryEmitter), so a
    // record property and the code that fills it cannot disagree about the type.
    internal static string Map(string typeTag, GameEventOverrides? overrides)
    {
        FieldTypeOverride? o = overrides?.For(typeTag);
        return o is not null ? o.CSharpType : Map(typeTag);
    }

    // Field-aware projection. Pawn handles are `uint` by construction rather
    // than by projection choice — GameEventPawnExpansion only sets the flag on
    // keys the engine emits as an ehandle — so the flag wins over a tag
    // override, which would otherwise retype the synthesised `*_pawn` companion
    // to match its controller half.
    internal static string Map(GameEventFieldModel field, GameEventOverrides? overrides) =>
        field.IsPawnHandle ? "uint" : Map(field.Type, overrides);

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
        // The userid-carrying halves.
        "player_controller" => "int",
        "player_controller_and_pawn" => "int",
        // `player_pawn` has no userid half — every one of its fields is a pawn
        // handle, projected through the field-aware overload above. Reaching
        // here means GameEventPawnExpansion did not run, which would otherwise
        // emit a compiling SDK whose 11 `player_pawn` properties have the wrong
        // type and read a key that is not on the wire. That is precisely the
        // bug this expansion fixes, so fail rather than fall through to the
        // `object?` default and reintroduce it silently.
        "player_pawn" => throw new InvalidOperationException(
            "player_pawn reached the tag-only projection. Every player_pawn field "
            + "is a pawn handle and must be projected via "
            + "Map(GameEventFieldModel, …) after GameEventPawnExpansion.Expand()."),
        // `local` appears once in the schema today (demo_start.dota_combatlog_list,
        // typed as a CSVCMsgList_GameEvents protobuf blob). The proto plumbing is
        // outside the SDK's scope, so model it as opaque bytes for now.
        "local" => "byte[]?",
        // Unknown tag — emit `object?` so the SDK still compiles. Adding a new
        // mapping here is a one-line change once the tag is encountered.
        _ => "object?"
    };
}
