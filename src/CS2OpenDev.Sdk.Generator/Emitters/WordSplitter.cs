#region

using System.Text;
using System.Text.RegularExpressions;

#endregion

namespace CS2SchemaGen.Emitters;

// Splits run-together lowercase words inside an otherwise-PascalCase identifier,
// so `Userid` becomes `UserId` and `Attackerinair` becomes `AttackerInAir`.
//
// Why this needs a vocabulary at all
// ----------------------------------
// Native CS2 names arrive with no word boundary to key off. `m_flNextAttack`
// carries Hungarian humps and folds to `NextAttack` for free, but the KV1
// game-event names are flat: `userid`, `thrusmoke`, `attackerinair`. There is no
// rule that recovers the boundaries from the characters alone, so the words have
// to be known.
//
// Why NOT a dictionary
// --------------------
// A general English word list is actively harmful here: it turns `assister` into
// Ass|Is|Ter and `hostage` into Host|Age. The vocabulary below is deliberately
// small and domain-specific — the words that appear as parts of CS2 identifiers,
// nothing else — and `Atomic` names the handful of single words a greedy match
// would still chew through.
//
// The safety property
// -------------------
// A run is rewritten ONLY when it segments completely into known tokens. Any run
// that does not is emitted exactly as the generator emitted it before this class
// existed. An unrecognised compound therefore produces the old name, never a
// guess — the failure mode is a missed improvement, not a wrong identifier.
// `UnsegmentedRuns` collects those so they surface as a diagnostic for vocabulary
// review rather than sitting unnoticed.
internal static class WordSplitter
{
    // Tokens that may appear as part of a longer identifier. Sorted longest-first
    // at load so greedy matching prefers `player` over `play`.
    private static readonly string[] Vocabulary =
    [
        // identity / entity
        "account", "attacker", "avenger", "controller", "entity", "ent", "handle",
        "id", "index", "initiator", "instance", "network", "other", "owner",
        "player", "spectator", "steam", "target", "user", "victim", "xuid",

        // combat
        "aim", "armor", "assist", "assisted", "attack", "blind", "bomb", "damage",
        "defuse", "dmg", "flash", "grenade", "headshot", "health", "hit", "hurt",
        "inflictor", "kill", "killed", "penetrated", "punch", "scope", "shot",
        "silencer", "smoke", "thru", "weapon", "wep",

        // Participles. `Atomic` stops these standing alone as a split, but they
        // still have to exist as tokens or a compound built from one cannot
        // segment at all — `issilenced` failed on the missing tail, not on the
        // `is`.
        "painted", "planted", "silenced", "connected", "spotted", "defused",

        // match / round
        "bronze", "frag", "gold", "half", "limit", "loser", "match", "money",
        "mvp", "mvps", "objective", "rank", "reason", "round", "rounds", "score",
        "silver", "site", "skirmish", "slots", "team", "time", "timer", "total",
        "warmup", "win", "winner",

        // world / assets
        "addon", "class", "crate", "decal", "hostage", "item", "kit", "level",
        "loadout", "map", "material", "model", "music", "prop", "sound", "weather",

        // config / meta
        "console", "cvar", "data", "def", "difficulty", "group", "hint", "host",
        "info", "message", "mode", "name", "option", "password", "path", "port",
        "priority", "server", "state", "status", "str", "text", "type", "value",
        "version", "vote", "votes",

        // qualifiers and small words
        "advanced", "air", "by", "can", "count", "delta", "dir", "end", "force", "free",
        "full", "has", "in", "is", "max", "min", "new", "no", "num", "old", "para",
        "potential", "restart", "screen", "split", "sub", "tick", "tracers",
        "upload", "yes", "zoom",

        // physics / geometry
        "angle", "origin", "pos", "radius", "rotation", "scale", "velocity",

        // Second pass, taken from the CS2_GEN_006 near-miss report rather than
        // guessed at. Every entry here is 3+ characters: the two-letter tokens
        // already in this list (`is`, `no`, `in`, `by`) are the ones that cause
        // mis-splits, and adding more of them to chase a few compounds is a bad
        // trade.
        "abs", "actor", "add", "axis", "bar", "base", "body", "box", "boxes",
        "bright", "change", "clip", "cmd", "corner", "doc", "entry", "event",
        "face", "fade", "fake", "field", "file", "fixed", "flex", "freeze",
        "game", "global", "god", "ground", "gun", "life", "light", "list",
        "load", "machine", "mask", "mesh", "mini", "mix", "next", "normal",
        "physics", "play", "point", "port", "preview", "render", "screen",
        "shake", "shatter", "shot", "sound", "space", "speed", "stamp", "start",
        "stop", "tag", "text", "tone", "tree", "update", "view", "voice",
        "world",
    ];

    // Single words a greedy match would otherwise split. Every entry here is a
    // real word that happens to start with, or contain, a shorter token:
    // `assister` (assist), `hostage` (host), `paradrop` is NOT here because
    // Para|Drop is the reading we want.
    private static readonly HashSet<string> Atomic = new(StringComparer.Ordinal)
    {
        "achievement", "address", "assister", "attacker", "behavior", "blocked",
        "category", "checkpoint", "clients", "dedicated", "delivered", "details",
        "disconnect", "distance", "dominated", "duration", "enabled", "hostage",
        "inertia", "initiator", "material", "message", "objective",
        "password", "penetrated", "priority", "proxies", "revenge", "silenced",
        "statue", "subject", "success", "transition", "version", "winner",

        // Found by reading the generated rename list, which is the only thing
        // that catches a split that is valid but wrong:
        //   identity   -> Id|Entity     (CEntityIdentity became CEntityIdEntity)
        //   instr      -> In|Str        (OpenCrateInstr became OpenCrateInStr)
        //   screenshot -> Screen|Shot   one English word; .NET writes Screenshot
        //   subclass   -> Sub|Class     likewise
        "identity", "identifier", "instr", "screenshot", "subclass",

        // Second batch, same lesson from the second vocabulary pass. Adding
        // `base`, `gun` and `light` as tokens made three single words newly
        // splittable: DataBase, ShotGun, FlashLight. Widening the vocabulary
        // always widens this list too — the two are not independent, which is
        // why each pass ends by re-reading the rename diff.
        "database", "shotgun", "flashlight", "submachinegun", "spotlight",
        "highlight", "daylight", "lightning", "baseline", "gameplay",
    };

    // Tokens whose idiomatic .NET form is not `char.ToUpper(first) + rest`.
    private static readonly Dictionary<string, string> Casing = new(StringComparer.Ordinal)
    {
        ["id"] = "Id",
        ["mvp"] = "Mvp",
        ["mvps"] = "Mvps",
        ["cvar"] = "CVar",
        ["str"] = "Str",
        ["dmg"] = "Dmg",
        ["ent"] = "Ent",
        ["num"] = "Num",
        ["wep"] = "Wep",
        ["pos"] = "Pos",
        ["dir"] = "Dir",
        ["xuid"] = "Xuid",
    };

    // Splits an identifier into its existing casing runs: an acronym (`CT`), a
    // capitalised word (`Attack`), a lowercase run (`userid`), or digits.
    private static readonly Regex Runs = new(
        @"[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z]+|[0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Match table: the vocabulary AND every atomic word, longest first.
    //
    // Atomic has to be in here, not just consulted for whole runs. Guarding only
    // the whole run left `globalentitydatabase` free to segment as
    // global|entity|data|base, because nothing stopped the greedy match from
    // walking straight through `database` — the guard never applied, since the
    // run being segmented was the longer string. Putting atomic words in the
    // table makes them win on length instead: `database` (8) beats `data` (4) at
    // that position, so the word survives wherever it appears rather than only
    // when it stands alone.
    private static readonly string[] ByLength =
        Vocabulary.Concat(Atomic)
                  .Distinct(StringComparer.Ordinal)
                  .OrderByDescending(t => t.Length)
                  .ThenBy(t => t, StringComparer.Ordinal)
                  .ToArray();

    // Lowercase runs that failed to segment, for the CS2_GEN_006 diagnostic.
    // Generator-lifetime state: the exporter drains this once after emitting.
    // Only runs long enough to plausibly be compounds are recorded — a
    // three-letter run that is not in the vocabulary is noise, not a gap.
    private static readonly SortedSet<string> Unrecognised = new(StringComparer.Ordinal);

    internal static IReadOnlyCollection<string> UnsegmentedRuns => Unrecognised;

    internal static void ResetUnsegmentedRuns() => Unrecognised.Clear();

    // Re-splits the lowercase runs of an already-PascalCase identifier.
    //
    // Runs that already carry casing (`NextAttack` → `Next`, `Attack`) are left
    // alone: upstream supplied the boundary and it is authoritative. Only a
    // wholly-lowercase run is a candidate, because only there is the boundary
    // missing.
    internal static string Split(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        // Cheap guard that skips the regex for the overwhelming majority of
        // Hungarian names. Has to admit a bare `ID` too, or a name whose only
        // change is the suffix fold slips past with no lowercase run long
        // enough to look interesting.
        if (!HasSplittableRun(identifier) &&
            !identifier.Contains("ID", StringComparison.Ordinal))
        {
            return identifier;
        }

        StringBuilder sb = new(identifier.Length + 8);
        int last = 0;
        bool changed = false;

        foreach (Match m in Runs.Matches(identifier))
        {
            // Preserve anything between runs verbatim (underscores, digits that
            // the regex split off, and any character the pattern does not claim).
            if (m.Index > last)
            {
                sb.Append(identifier, last, m.Index - last);
            }

            last = m.Index + m.Length;
            string run = m.Value;

            // `ID` -> `Id`. Upstream spells the suffix both ways (`m_nSubclassID`
            // but `accountid`), and splitting the lowercase form produces `Id`,
            // so leaving the shouted one alone would ship `SubclassID` next to
            // `AccountId` on the same object. `Id` is the .NET spelling — it is
            // an abbreviation of "identifier", not an acronym, and the BCL uses
            // it (`Process.Id`, `Activity.Id`).
            //
            // Matching the whole run rather than a trailing substring is what
            // keeps `INVALID` intact: that arrives as one seven-character
            // all-caps run, not as something ending in a separate `ID` run.
            if (run.Length == 2 && run is "ID" && identifier.Length > 2)
            {
                sb.Append("Id");
                changed = true;
                continue;
            }

            // Candidates are word-shaped runs — `userid` or `Userid`, but not
            // `CT`. By the time an identifier reaches here the earlier folds have
            // already capitalised it, so the run to segment is `Userid` and the
            // vocabulary lookup has to happen on its lowercased form. Matching on
            // the raw run instead silently matched nothing at all.
            if (!IsWordShaped(run) || !TrySegment(run.ToLowerInvariant(), out List<string>? tokens))
            {
                sb.Append(PascalFirst(run));
                continue;
            }

            changed = true;
            foreach (string t in tokens!)
            {
                sb.Append(Casing.TryGetValue(t, out string? cased) ? cased : PascalFirst(t));
            }
        }

        if (last < identifier.Length)
        {
            sb.Append(identifier, last, identifier.Length - last);
        }

        return changed ? sb.ToString() : identifier;
    }

    // Greedy longest-match. Succeeds only if the whole run is consumed by known
    // tokens AND the result is more than one token — a run that IS a single
    // vocabulary word is not a split, it is the word.
    private static bool TrySegment(string run, out List<string>? tokens)
    {
        tokens = null;

        if (run.Length < 4 || Atomic.Contains(run))
        {
            return false;
        }

        List<string> found = [];
        int i = 0;

        while (i < run.Length)
        {
            string? match = null;
            foreach (string t in ByLength)
            {
                if (t.Length <= run.Length - i &&
                    string.CompareOrdinal(run, i, t, 0, t.Length) == 0)
                {
                    match = t;
                    break;
                }
            }

            if (match is null)
            {
                // Report only a NEAR miss — a run that starts or ends with a
                // known word but could not be finished.
                //
                // Reporting every failure was the obvious first cut and it was
                // useless: ~2,000 entries, almost all ordinary English
                // (`acceleration`, `dictionary`, `parachute`) that is already
                // correct and must never be split. A list nobody can read is
                // the same as no list. A near miss is different — `issilenced`
                // matched `is` and died on the tail, `actorname` ends in `name`
                // — and that is exactly the shape of a genuine vocabulary gap.
                if (run.Length > 6 && TouchesVocabulary(run))
                {
                    Unrecognised.Add(run);
                }

                return false;
            }

            found.Add(match);
            i += match.Length;
        }

        if (found.Count < 2)
        {
            return false;
        }

        tokens = found;
        return true;
    }

    // True when a vocabulary word of 3+ characters sits at either end of the
    // run. Length 3 is the floor because `is`/`in`/`no`/`by` appear inside far
    // too many ordinary words to carry any signal.
    private static bool TouchesVocabulary(string run)
    {
        foreach (string t in ByLength)
        {
            if (t.Length < 3 || t.Length >= run.Length)
            {
                continue;
            }

            if (string.CompareOrdinal(run, 0, t, 0, t.Length) == 0 ||
                string.CompareOrdinal(run, run.Length - t.Length, t, 0, t.Length) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSplittableRun(string s)
    {
        int run = 0;
        foreach (char c in s)
        {
            if (c is >= 'a' and <= 'z')
            {
                if (++run >= 4)
                {
                    return true;
                }
            }
            else
            {
                run = 0;
            }
        }

        return false;
    }

    // A run worth segmenting: letters only, and uppercase at most in the first
    // position. That admits `userid` and `Userid` while excluding the acronym
    // runs (`CT`, `MVP`) the regex hands over as all-caps — splitting those would
    // destroy casing upstream deliberately supplied.
    private static bool IsWordShaped(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c is >= 'a' and <= 'z' || (i == 0 && c is >= 'A' and <= 'Z');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static string PascalFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
