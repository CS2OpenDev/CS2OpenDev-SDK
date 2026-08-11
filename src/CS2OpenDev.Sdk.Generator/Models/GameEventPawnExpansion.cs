namespace CS2SchemaGen.Models;

// Expands the two pawn-bearing player-reference type tags into the wire keys the
// engine actually emits.
//
// The schema declares three player-reference field types. Only one of them maps
// to a single wire key of the same name:
//
//   player_controller           82 fields  ->  <name>                 (userid)
//   player_controller_and_pawn  59 fields  ->  <name> + <name>_pawn   (userid, ehandle)
//   player_pawn                 11 fields  ->  <name>_pawn            (ehandle)
//
// The engine expands the declared *type* when it builds the
// CMsgSource1LegacyGameEventList descriptor, so nothing in the extracted schema
// names the `_pawn` keys — they are a property of the type, one representation
// level up from the field list. See CS2OpenDev-SchemaTracker#6, which measured
// the resulting 62 wire keys against GOTV demos and derived 63 from the declared
// types at build 24662694 (the delta is build skew).
//
// Before this expansion the generator read the declared name for all three tags.
// That silently dropped every pawn handle on `player_controller_and_pawn`, and
// read a key that is not on the wire at all for `player_pawn` — where
// GameEventReader's absent-key contract (yield 0) turned the miss into a
// plausible-looking zero rather than a failure.
//
// Runs after the supplement merge so curated events get the same treatment as
// extracted ones: the rule is a property of the type tag, not of where the
// record came from.
internal static class GameEventPawnExpansion
{
    private const string ControllerAndPawn = "player_controller_and_pawn";
    private const string Pawn = "player_pawn";

    // Suffix the engine appends when it splits a declared type into wire keys.
    private const string PawnSuffix = "_pawn";

    internal static GameEventsRoot Expand(GameEventsRoot root)
    {
        GameEventModel[] expanded = new GameEventModel[root.Events.Length];
        for (int i = 0; i < root.Events.Length; i++)
        {
            expanded[i] = Expand(root.Events[i]);
        }

        return new GameEventsRoot(expanded);
    }

    private static GameEventModel Expand(GameEventModel ev)
    {
        // Most events carry no pawn-bearing field; leave those records untouched
        // rather than reallocating an identical array.
        bool touched = false;
        foreach (GameEventFieldModel f in ev.Fields)
        {
            if (f.Type is Pawn or ControllerAndPawn)
            {
                touched = true;
                break;
            }
        }

        if (!touched)
        {
            return ev;
        }

        // Names already present on the record. A companion is only synthesised
        // for a key nothing else provides — so if upstream ever starts
        // declaring the `_pawn` fields directly, the real declaration wins and
        // this expansion quietly stops adding its own, the same way
        // game-event-supplement.json yields to a real extraction. It also makes
        // the transform idempotent, which the field-level flags alone do not:
        // the controller half keeps its `player_controller_and_pawn` tag, so a
        // second pass would otherwise append a duplicate companion.
        HashSet<string> declared = new(ev.Fields.Length, StringComparer.Ordinal);
        foreach (GameEventFieldModel f in ev.Fields)
        {
            declared.Add(f.Name);
        }

        List<GameEventFieldModel> fields = new(ev.Fields.Length + 1);
        foreach (GameEventFieldModel f in ev.Fields)
        {
            // Already carries its expanded identity — pass through untouched
            // rather than re-suffixing it into `userid_pawn_pawn`.
            if (f.IsPawnHandle)
            {
                fields.Add(f);
                continue;
            }

            switch (f.Type)
            {
                // Sole wire key is `<name>_pawn`. The declared name is kept as
                // the field's Name — so the shipped property keeps its
                // identifier and [NativeName] still records what the schema
                // says — and only the lookup key moves.
                case Pawn:
                    fields.Add(f with
                    {
                        WireKeyOverride = f.Name + PawnSuffix,
                        IsPawnHandle = true
                    });
                    break;

                // Two wire keys. The controller half is exactly what was already
                // emitted, so existing consumers are untouched; the companion is
                // additive.
                case ControllerAndPawn:
                    fields.Add(f);
                    if (!declared.Contains(f.Name + PawnSuffix))
                    {
                        fields.Add(f with
                        {
                            Name = f.Name + PawnSuffix,
                            Comment = CompanionComment(f),
                            // The companion is synthesised, so it carries no
                            // upstream annotations of its own — inheriting the
                            // controller's would attach prose about a userid to
                            // an entity handle.
                            Annotations = null,
                            IsPawnHandle = true
                        });
                    }

                    break;

                default:
                    fields.Add(f);
                    break;
            }
        }

        return ev with { Fields = fields.ToArray() };
    }

    // The declared tag stays on both halves (see GameEventsEmitter's
    // [GameEventFieldType]), so the comment is what tells a reader which half
    // this property is.
    private static string CompanionComment(GameEventFieldModel controller) =>
        $"Entity handle of the pawn for `{controller.Name}`. Companion wire key "
        + $"`{controller.Name}{PawnSuffix}`, emitted by the engine alongside "
        + $"`{controller.Name}` for the {ControllerAndPawn} type.";
}
