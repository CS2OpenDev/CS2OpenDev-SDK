#region

using System.Text.Json;

#endregion

namespace CS2SchemaGen.Models;

// Curated game events that the extracted schema does not contain.
//
// The three inputs upstream of this repo — SchemaTracker's dump, the Docs
// annotation overlay, and `gameevents_schema.json` itself — are all derived from
// the shipped binaries, and they miss events. `item_drop`, `halftime` and
// `game_restart` all appear in the `CMsgSource1LegacyGameEventList` descriptor a
// real GOTV demo carries, and all three fire, but none of them is declared
// anywhere the extractor can see. See GitHub issue #3.
//
// A *missing field* is a visible defect: the record exists, the property isn't
// there, and the consumer's code doesn't compile. A *missing record* is invisible
// at every layer that compiles — a dispatcher's rule bound to `item_drop` simply
// never fires, and nothing logs. That asymmetry is why this hook exists at all,
// and why it is not merely "nice to have the events".
//
// `game-event-overrides.json` cannot cover this: it re-points a KV1 *type tag* at
// a consumer type. It has no way to introduce an event.
//
// ── Additive only ────────────────────────────────────────────────────────────
//
// The supplement can add a native name; it can never replace one. If the
// extracted schema already declares the name — under any source — Apply fails the
// build. That rule is the whole design:
//
//   * These entries are meant to be temporary. They are guesses about a shape
//     Valve owns, and the moment the extractor sees the real declaration, the
//     real one must win.
//   * Collision on *name* rather than on (name, source) is deliberate. A
//     supplement stamped `sdk.supplement` and an upstream `item_drop` stamped
//     `mod.gameevents` are different (name, source) pairs, so an identity check
//     would let both through — and the duplicate-name machinery would quietly
//     emit `ItemDropEvent` (upstream, higher priority) *plus* a stale
//     `ItemDropSdkEvent`, shipping an invented shape forever under a slightly
//     different type name. Failing on the name forces the maintainer to delete
//     the entry, which is the only correct response.
internal static class GameEventSupplement
{
    // Conventional filename, resolved next to the schema first and then in the
    // working directory — identical to `game-event-overrides.json`.
    internal const string FileName = "game-event-supplement.json";

    // The `source` stamped on every supplemented event.
    //
    // Deliberately not one of `core.gameevents` / `game.gameevents` /
    // `mod.gameevents`: those name real KV1 files, and a curated record claiming
    // to have come out of one would be a lie told in the one field a consumer
    // uses to judge provenance. It is also not `*.gameevents` at all, so a
    // consumer filtering on that suffix sees this for what it is.
    //
    // Not read from the supplement file. A maintainer typing `mod.gameevents`
    // into their own supplement would defeat the entire point, so the generator
    // stamps it and rejects any attempt to declare something else.
    //
    // Grouping consequence, verified rather than assumed: GameEventsEmitter's
    // SourcePriority table gives an unlisted source priority 0 — below `core` at
    // 1 — so a supplemented event can never outrank an extracted one within a
    // name group. In practice the group is always a single entry, because a
    // shared name is rejected before emission ever runs.
    internal const string SupplementSource = "sdk.supplement";

    internal static GameEventsRoot Empty { get; } = new([]);

    // Locates the supplement, or null when there isn't one. Absent is a silent
    // no-op — same contract as the overrides file.
    internal static string? ResolvePath(string? schemaDirectory, string workingDirectory) =>
        SideCarFile.Resolve(FileName, schemaDirectory, workingDirectory);

    // Parses a supplement document.
    //
    // The event shape is exactly `gameevents_schema.json`'s, so the existing
    // parser does the work: reusing it means a supplemented event's fields and
    // annotations are read by the same code that reads an extracted one, and the
    // two cannot drift into accepting different JSON.
    internal static GameEventsRoot Parse(string json)
    {
        GameEventsRoot parsed = GameEventsModel.Parse(json);

        List<GameEventModel> events = new(parsed.Events.Length);
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (GameEventModel ev in parsed.Events)
        {
            if (string.IsNullOrWhiteSpace(ev.Name))
            {
                throw new InvalidOperationException(
                    "every supplement event needs a non-empty 'name' — it is the native wire name the "
                    + "descriptor table carries, and the only thing a dispatcher can key on.");
            }

            // Rejected rather than silently overwritten. Two entries for one name
            // would both claim the unsuffixed type name, and because GameEventModel
            // is a record with value equality, two identical zero-field entries
            // collapse to a single key in the type-name map — one record would
            // vanish and the emitted registry would carry a duplicate dictionary
            // key that throws at static init.
            if (!seen.Add(ev.Name))
            {
                throw new InvalidOperationException(
                    $"'{ev.Name}' is declared more than once in the supplement. A supplement adds a "
                    + "native name that upstream is missing; declaring it twice has no meaning.");
            }

            // `source` may be stated for readability, but only as the value the
            // generator was going to stamp anyway. Anything else is an attempt to
            // pass a curated record off as extracted.
            if (!string.IsNullOrEmpty(ev.Source) && !ev.Source.Equals(SupplementSource, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{ev.Name}' declares source '{ev.Source}'. A supplemented event cannot claim to come "
                    + $"from a .gameevents file it was not extracted from; omit 'source' or set it to "
                    + $"'{SupplementSource}'.");
            }

            events.Add(ev with { Source = SupplementSource, Supplemented = true });
        }

        return new GameEventsRoot(events.ToArray());
    }

    // Folds the supplement into the extracted schema.
    //
    // Appended rather than interleaved: the emitters sort their own output (by
    // C# type name), so position carries no meaning, and appending keeps the
    // extracted events in the order upstream wrote them.
    internal static GameEventsRoot Apply(GameEventsRoot extracted, GameEventsRoot supplement)
    {
        if (supplement.Events.Length == 0)
        {
            return extracted;
        }

        HashSet<string> extractedNames = new(StringComparer.Ordinal);
        foreach (GameEventModel ev in extracted.Events)
        {
            extractedNames.Add(ev.Name);
        }

        foreach (GameEventModel ev in supplement.Events)
        {
            if (extractedNames.Contains(ev.Name))
            {
                // The good outcome, reported as a failure on purpose. Upstream has
                // caught up, the curated guess is now obsolete, and continuing
                // would ship an invented shape alongside the real one.
                throw new InvalidOperationException(
                    $"'{ev.Name}' is now declared in the extracted schema, so the supplement entry for it "
                    + "is obsolete and must be deleted. The supplement exists only to carry events the "
                    + "extractor cannot see; it must never shadow, mask or duplicate an extracted event.");
            }
        }

        GameEventModel[] merged = new GameEventModel[extracted.Events.Length + supplement.Events.Length];
        extracted.Events.CopyTo(merged, 0);
        supplement.Events.CopyTo(merged, extracted.Events.Length);
        return new GameEventsRoot(merged);
    }
}
