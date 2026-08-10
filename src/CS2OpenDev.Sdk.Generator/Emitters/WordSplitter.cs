#region

using System.Collections.Concurrent;
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

        // Third pass. Same source as the second — the CS2_GEN_006 report,
        // read rather than guessed. Short entries here are the risky ones and
        // each is paid for by a correspondingly larger Atomic list below.
        "able", "abs", "address", "align", "alpha", "amt", "animating", "arm",
        "armor", "around", "array", "asset", "assets", "auto", "back", "bad",
        "bang", "binding", "black", "blend", "block", "board", "boat", "bone",
        "bounds", "box", "break", "byte", "checked", "child", "china", "commands",
        "composite", "cone", "config", "control", "count", "cross", "crouch",
        "crowbar", "cube", "custom", "datagram", "death", "delay", "density",
        "doll", "down", "edict", "events", "fill", "fire", "forced", "found",
        "frame", "free", "glass", "graph", "graphs", "groups", "gun", "hair",
        "hud", "initial", "input", "insecure", "integer", "io", "iso", "kick",
        "killing", "leaving", "lerp", "light", "line", "list", "local",
        "location", "locations", "lock", "lower", "maps", "mask", "mass", "match",
        "meta", "mid", "move", "msg", "names", "network", "not", "off", "offset",
        "origin", "out", "particle", "pass", "password", "pay", "perfect",
        "picking", "ping", "pitch", "point", "points", "positive", "protocol",
        "ptr", "pure", "ragdoll", "real", "reduce", "relay", "remove",
        "replication", "safe", "save", "scape", "side", "single", "soft", "solid",
        "sort", "sound", "sounds", "space", "span", "spectator", "stackable",
        "stall", "states", "steam", "string", "strings", "sun", "surface",
        "symbol", "table", "thru", "tick", "timed", "trace", "type", "unknown",
        "untrusted", "verb", "vertex", "view", "vol", "voted", "watch", "way",
        "weight", "wind", "wrap",

        // Fourth pass.
        "added", "all", "anim", "balance", "ban", "band", "brush", "buy", "chain",
        "challenge", "client", "command", "competitive", "crop", "crypt",
        "default", "definition", "detail", "different", "driver", "dry", "dyn",
        "error", "faces", "for", "frac", "generated", "graph", "head",
        "hierarchy", "high", "hltv", "jiggle", "joint", "key", "left", "lfo",
        "lib", "linear", "loop", "los", "low", "merge", "mod", "npc", "occlusion",
        "overflow", "params", "parms", "pieces", "players", "pre", "problem",
        "product", "rate", "rgb", "right", "root", "run", "sell", "shutdown",
        "simulate", "spin", "stats", "step", "system", "tables", "temp",
        "texture", "through", "trans", "transmission", "trigger", "unavailable",
        "util", "utl", "uv", "waiting", "wire",

        // Fifth pass.
        "audio", "cache", "cap", "computed", "controls", "convars", "convicted",
        "deactivate", "direct", "dot", "enum", "extra", "feed", "fetch", "fog",
        "forward", "hinge", "hurting", "ladder", "leg", "library", "log", "math",
        "motion", "mult", "nav", "on", "only", "param", "plate", "populate",
        "restricted", "sav", "script", "spawn", "stacks", "token", "transform",
        "turn", "up", "values", "var", "vec",

        // Sixth pass — the tail of the report.
        "container", "extract", "flags", "framed", "frames", "meter", "modified",
        "multi", "rumble", "sample", "set", "vector",

        // Seventh pass — the last three the report named.
        "containers", "over", "plane", "drown", "recover"
    ];

    // Single words a greedy match would otherwise split. Every entry here is a
    // real word that happens to start with, or contain, a shorter token:
    // `assister` (assist), `hostage` (host), `paradrop` is NOT here because
    // Para|Drop is the reading we want.
    private static readonly HashSet<string> Atomic = new(StringComparer.Ordinal)
    {
        // `pre` as a token makes these two splittable, and both are one word.
        "preset", "prefetch",

        "drowning", "recovery",

        "overlaid", "overlap", "overlay", "overlays", "overridden", "override",
        "overrides", "overrode", "overshoot",

        "respawning", "returning", "sampler", "samples", "setting", "settings",
        "settling",

        "bandwidth", "callbacks", "cmotiontransform", "compmatsysvar",
        "cooldowns", "cropped", "ctransform", "despawn", "feedfoward", "hscript",
        "respawn", "vectorto", "vectorws",

        "capability", "capsule", "capsules", "caption", "capture", "captures",
        "extract", "extraction", "extrapolate", "extrapolation", "ladders",
        "logging", "logical", "motions", "multiple", "multiplex", "multiplier",
        "multiply", "navigation", "navigator", "scripted", "scripts", "spawned",
        "spawner", "spawners", "spawning", "turning", "variance", "variant",
        "variation", "variations", "varying", "vectors",

        "atcontrols", "iphysicsjoint", "ltrigger", "modifer", "noninitialized",
        "pprevious", "retrigger", "rtrigger", "uncrouched", "uninitialized",

        "compressed", "disabled", "disallowed", "footsteps", "movables",
        "observables", "orthographic", "separately", "suppressed", "uncompressed",
        "unpressed", "variables", "wearables",

        "accelerate", "accurate", "adrenaline", "aligned", "allocated",
        "allocation", "allocator", "allowed", "animate", "animated", "animates",
        "animation", "animations", "announcements", "applicable", "assignments",
        "attachable", "attachments", "automated", "automatic", "available",
        "backward", "backwards", "balanced", "bidirectional", "bilinear",
        "bindings", "blended", "blender", "blending", "blocker", "blockers",
        "blocking", "bookkeeping", "breakables", "breaker", "breaking",
        "carriable", "celebrate", "centered", "chainer", "chaining", "children",
        "clamping", "commends", "commentary", "comments", "components",
        "composition", "configuration", "configurations", "consumable",
        "contents", "contexts", "controls", "crossover", "crouched", "crouching",
        "currently", "damping", "defuser", "delayed", "detachable", "detailed",
        "disable", "disallow", "disposition", "doorway", "drawable", "drivers",
        "dropping", "dynamic", "dynamically", "dynamics", "elements",
        "executable", "exponential", "exportable", "fallback", "feedback",
        "footstep", "forever", "forgiveness", "formation", "forward", "fraction",
        "fractional", "generate", "grabbable", "graphic", "graphics",
        "handshakes", "heading", "highest", "immovable", "inheritable",
        "initialized", "initializer", "initially", "interfaces", "interpenetrate",
        "invulnerable", "iterate", "jumping", "lerping", "library", "linearity",
        "localized", "looping", "modification", "modifier", "modifiers",
        "modulate", "modulation", "modulator", "modules", "movable", "notched",
        "nothing", "notification", "notified", "observable", "offering",
        "offsets", "outbound", "outdoor", "outflow", "outflows", "outputs",
        "overall", "overhead", "overwatch", "particles", "passing", "penetrate",
        "percentage", "pickable", "precipitation", "precise", "precision",
        "predict", "predictable", "predicted", "prediction", "predictions",
        "preferred", "prefers", "prefilter", "premier", "preserve", "preserving",
        "pressed", "pressure", "prestige", "previous", "previously", "recipients",
        "reliable", "reloading", "removable", "removed", "repeatable",
        "replacements", "repredict", "requirements", "running", "saturate",
        "searchable", "segments", "selectable", "separate", "simulated",
        "sleeping", "snapping", "softness", "solidity", "sorting", "supported",
        "supports", "surrendered", "surrounding", "swapping", "systems",
        "temperature", "template", "templates", "temporal", "tradable",
        "transform", "transforms", "translate", "translation", "transmissive",
        "transmit", "transpose", "triangles", "triggered", "triggers",
        "unbreakable", "unloading", "unpredictable", "unstoppable",
        "unsubscribed", "updater", "upright", "utility", "variable", "volatile",
        "volumes", "volumetric", "walkable", "wearable", "weighted", "weights",
        "without", "wrapped", "writable",

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

        // Third batch, and the pattern is now predictable enough to state: each
        // widening of the vocabulary newly exposes single words built from the
        // tokens just added. `break`+`able` gave BreakAble across six types,
        // `down`+`load` gave DownLoad, `in`+`side` gave InSide. None of these
        // were reachable before the token that enabled them existed.
        //
        // `deathmatch`, `flashbang` and `timeline` are judgement calls rather
        // than dictionary facts — all three are established single terms in this
        // domain, and DeathMatch reads as a mistake to anyone who plays the game.
        "breakable", "countdown", "deathmatch", "download", "flashbang",
        "inline", "inside", "stopwatch", "timeline",

        // Fourth pass: the -able/-ing/-ed family mostly, which the fourth
        // token batch would otherwise have made splittable.
        "animatable", "automation", "backpack", "blockslos", "bytecode",
        "callback", "classify", "configs", "configurable", "controlled",
        "controlling", "cooldown", "cstring", "customization", "customize",
        "defusal", "directionality", "dropdown", "editable", "entities",
        "entries", "falloff", "fenttable", "flipping", "headshots",
        "hnmgraphdefinition", "hparticlesystemdefinition", "hrendertexture",
        "initializers", "interruptable", "iphysicsragdollcontrol", "leaderboard",
        "mappings", "metalness", "namespace", "offseton", "overlapping",
        "passthough", "passthrough", "payouts", "properties", "pushable",
        "queryable", "refundable", "remapping", "respawnable", "sawedoff",
        "solidities", "soundscape", "spawnable", "teleported", "teleporting",
        "triggerable", "unblockable", "unsubscribe", "useable", "vertextint",
        "volumeto",

        // Every run CS2_GEN_006 reported that is a real English word, plus the
        // upstream abbreviations and typos (`paramater`, `lightnint`,
        // `sndopvarlatchdata`) that no vocabulary should try to interpret.
        //
        // Machine-classified against the system dictionary, then reviewed. A
        // word list is dangerous for DECIDING a split and safe for forbidding
        // one, which is the only thing it is used for here. These also enter
        // the match table, so they protect themselves inside longer runs.
        "absolute", "absorption", "accounts", "additional", "additive",
        "additives", "adjacent", "adjustment", "agreement", "alignment",
        "ambient", "animstate", "announcement", "archetype", "arrangement",
        "assignment", "assists", "attachment", "attacked", "attacking",
        "attractor", "background", "barrier", "blendtobackground", "brightness",
        "buckshot", "canceled", "candelas", "candidate", "changed", "changes",
        "classes", "classptr", "commend", "comment", "component", "composite",
        "concurrent", "content", "context", "controllers", "corners", "counter",
        "counterterrorist", "cphysicsbody", "crowbar", "cspincount", "current",
        "damaged", "damager", "decrement", "default", "deferred", "definition",
        "definitions", "deflection", "deformable", "deformation", "deformer",
        "defusing", "deltaentmsg", "different", "directed", "direction",
        "directional", "directions", "directivity", "directory", "disconnected",
        "disconnecting", "disconnection", "displacement", "display", "distances",
        "distancesqr", "docking", "document", "dohitlocationdmg", "ehandle",
        "element", "enforce", "enrollment", "entered", "entitlement",
        "environment", "equipment", "exponent", "fadeinsav", "fadeoutsav",
        "flashed", "flashing", "flxfade", "foreground", "fragment",
        "freqdependent", "freqindependent", "gradient", "grenades", "grouping",
        "gunfire", "handler", "handlers", "handles", "handshake", "hegrenade",
        "highlights", "hitting", "hmaterial", "hostages",
        "hostedserverprimaryrelay", "incgrenade", "increment", "independent",
        "indexed", "instanced", "instances", "instruction", "instructions",
        "instructor", "instrument", "instruments", "interface", "involvement",
        "iphysicsbody", "iphysicsmotioncontroller", "killing", "lfotype",
        "lighten", "lighting", "lightness", "lightnint", "limiter", "listened",
        "listener", "listeners", "listening", "loading", "mapping", "matched",
        "matches", "matching", "matchmaking", "materials", "maximize", "maximum",
        "meshlet", "meshlets", "meshopt", "messages", "metadata", "minimal",
        "minimum", "mismatch", "modelling", "movement", "multiplay",
        "multiplayer", "multisegment", "navmesh", "networked", "networking",
        "noautoreload", "normalize", "normalized", "normals", "numbers",
        "numerator", "observer", "optional", "options", "original", "originating",
        "ornament", "overbright", "overtime", "ownership", "paltpath",
        "parachute", "parallel", "paramater", "parameter", "parameterization",
        "parameterized", "parameters", "passport", "pathfinding", "pathfinds",
        "pathing", "payload", "percent", "persistent", "phyllotaxis", "placement",
        "playback", "players", "playing", "pointer", "portals", "portrait",
        "position", "positioning", "positions", "positive", "possible",
        "postpone", "posture", "prefixed", "prepend", "present", "prevent",
        "profile", "propagate", "propagation", "property", "proportional",
        "punching", "ranking", "recipient", "rematch", "renderable", "renderamt",
        "rendered", "renderer", "renderers", "rendering", "reorient",
        "replacement", "requirement", "retirement", "rotations", "roundness",
        "runtime", "segment", "serverauthdisabled", "servercdkeyauthinvalid",
        "singleplay", "snapshot", "sndopvarlatchdata", "soundctrl", "spectators",
        "speedto", "splitter", "started", "starting", "startle", "startup",
        "stopped", "stopping", "strafing", "straight", "strange", "strategy",
        "streaming", "streams", "strength", "stretch", "stretches", "stretching",
        "strides", "stringlib", "strings", "structure", "structured", "submerged",
        "subscribe", "subscribed", "subscription", "subtitle", "subtract",
        "successes", "successfully", "support", "surface", "surrender",
        "surround", "tangent", "targets", "teammate", "teammates", "teleport",
        "texture", "textures", "threshold", "thruster", "ticking", "totalled",
        "tournament", "transient", "transitioned", "transitioning", "transitions",
        "translucent", "triangle", "typesafe", "unblocked", "updated", "updates",
        "uploaded", "useorreload", "vacbanstate", "vacnetabnormalbehavior",
        "valueto", "vphysics", "weapons", "whitespace", "windage", "wingman",
        "winning"
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
    //
    // Concurrent because the test runner is parallel. The generator emits on one
    // thread and would never need it, but every test that touches a name reaches
    // this class, and plain SortedSet/SortedDictionary under concurrent writes
    // failed three unrelated tests at random — the kind of flake that gets
    // re-run rather than diagnosed. Sorting happens at read instead.
    private static readonly ConcurrentDictionary<string, byte> Unrecognised =
        new(StringComparer.Ordinal);

    internal static IReadOnlyCollection<string> UnsegmentedRuns =>
        Unrecognised.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    // ── The name lock ────────────────────────────────────────────────────────
    //
    // Previously-decided splits, keyed by the lowercase run. A locked run is
    // returned verbatim and the vocabulary is never consulted for it again.
    //
    // This exists because the vocabulary is RETROACTIVE, which is not obvious
    // until it bites. Adding a word to segment some new field also changes every
    // existing name that word can now cut: `base`, `gun` and `light` were added
    // for three new compounds and silently turned `Database`, `Shotgun` and
    // `Flashlight` into `DataBase`, `ShotGun` and `FlashLight`. That happened
    // sixteen times while this vocabulary was being built, caught each time only
    // by re-reading the generated diff.
    //
    // Before the lock, that meant every future vocabulary edit was a potential
    // breaking rename of already-published API, applied by a scheduled job that
    // regenerates and publishes unattended. The lock makes the vocabulary safe
    // to edit: it can only affect runs nobody has shipped yet.
    //
    // Keyed on the RUN rather than the native name deliberately. Native names
    // are not 1:1 with identifiers — `m_flScale` is `Scale` on most classes and
    // `FlScale` where that collides — so a native-keyed lock would have to
    // encode collision resolution too. The run is a pure input to a pure
    // function, so it is the honest key, and prefix stripping and collision
    // handling stay in the emitters where they already work.
    private static Dictionary<string, string> _locked = new(StringComparer.Ordinal);

    // Every run resolved this session, locked or freshly computed. The exporter
    // writes this back so new names join the lock as they appear.
    private static readonly ConcurrentDictionary<string, string> Decisions =
        new(StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, string> ResolvedRuns =>
        new SortedDictionary<string, string>(Decisions, StringComparer.Ordinal);

    // Runs decided by the vocabulary because the lock had no entry — the review
    // list for a release. Everything else is by definition unchanged.
    private static readonly ConcurrentDictionary<string, string> Fresh =
        new(StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, string> UnlockedRuns =>
        new SortedDictionary<string, string>(Fresh, StringComparer.Ordinal);

    internal static void LoadLock(IReadOnlyDictionary<string, string> entries)
    {
        _locked = new Dictionary<string, string>(entries, StringComparer.Ordinal);
        Decisions.Clear();
        Fresh.Clear();
    }

    // Re-splits the lowercase runs of an already-PascalCase identifier.
    //
    // Runs that already carry casing (`NextAttack` → `Next`, `Attack`) are left
    // alone: upstream supplied the boundary and it is authoritative. Only a
    // wholly-lowercase run is a candidate, because only there is the boundary
    // missing.
    internal static string Split(string identifier) =>
        Split(identifier, _locked, record: true);

    // Same fold against a caller-supplied lock, recording nothing.
    //
    // Exists for tests. The lock, the decision log and the fresh-name list are
    // process-wide statics — fine for the generator, which emits once on one
    // thread, but the test runner is parallel, so a test that installed a lock
    // to assert on it was changing the answer for every other test running at
    // that moment. Three unrelated name tests failed that way before this
    // overload existed.
    internal static string SplitWith(
        string identifier, IReadOnlyDictionary<string, string> lockEntries) =>
        Split(identifier, lockEntries, record: false);

    private static string Split(
        string identifier, IReadOnlyDictionary<string, string> lockEntries, bool record)
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
            if (!IsWordShaped(run))
            {
                sb.Append(PascalFirst(run));
                continue;
            }

            string key = run.ToLowerInvariant();
            string resolved;

            if (lockEntries.TryGetValue(key, out string? pinned))
            {
                // Decided in an earlier release. The vocabulary does not get a
                // vote — that is the entire point of the lock.
                resolved = pinned;
            }
            else
            {
                resolved = TrySegment(key, out List<string>? tokens)
                    ? Join(tokens!)
                    : PascalFirst(run);

                if (record)
                {
                    Fresh[key] = resolved;
                }
            }

            // Record even the unchanged ones. A run that the vocabulary leaves
            // alone today is exactly the kind a future token would start
            // cutting — `database` was correct until `base` existed — so the
            // lock has to pin "stays whole" just as firmly as it pins a split.
            if (record)
            {
                Decisions[key] = resolved;
            }

            if (!string.Equals(resolved, PascalFirst(run), StringComparison.Ordinal))
            {
                changed = true;
            }

            sb.Append(resolved);
        }

        if (last < identifier.Length)
        {
            sb.Append(identifier, last, identifier.Length - last);
        }

        return changed ? sb.ToString() : identifier;
    }

    // Finds a complete segmentation of the run, or reports that none exists.
    // Succeeds only when every character is consumed by known tokens AND the
    // result is more than one token — a run that IS a single vocabulary word is
    // not a split, it is the word.
    //
    // This backtracks; the first version did not, and greedy longest-match alone
    // is wrong here rather than merely weaker. `clientside` has a perfectly good
    // reading, but greedy takes `clients` at position 0 because it is longer,
    // hits `ide`, and reports the whole run as unsegmentable — never trying
    // `client`. Same for `weaponsilencer` (`weapons` first) and `notarget`
    // (`not` first). Those looked like missing words and were not; no amount of
    // vocabulary fixes a search that cannot reconsider.
    //
    // Longest-first ordering is kept as the preference, so `player` still beats
    // `play` when both complete. Backtracking only changes what happens when the
    // preferred choice leads to a dead end.
    private static bool TrySegment(string run, out List<string>? tokens)
    {
        tokens = null;

        if (run.Length < 4 || Atomic.Contains(run))
        {
            return false;
        }

        // Memo of start offsets already proven unsegmentable, so a run with many
        // dead ends stays linear rather than exponential.
        bool[] failed = new bool[run.Length + 1];
        List<string> found = [];

        if (!Solve(run, 0, found, failed) || found.Count < 2)
        {
            // Report only a NEAR miss — a run that starts or ends with a known
            // word but could not be finished.
            //
            // Reporting every failure was the obvious first cut and it was
            // useless: ~2,000 entries, almost all ordinary English
            // (`acceleration`, `dictionary`, `parachute`) that is already
            // correct and must never be split. A list nobody can read is the
            // same as no list. A near miss is different — `issilenced` matched
            // `is` and died on the tail, `actorname` ends in `name` — and that
            // is exactly the shape of a genuine vocabulary gap.
            // `found.Count == 1` means the run was consumed entirely by ONE
            // token — it is a vocabulary word standing alone, which is already
            // correct and needs no split. Reporting those put `headshot`,
            // `loadout` and `ragdoll` on a list of things to fix, where the fix
            // was to do nothing.
            if (found.Count == 0 && run.Length > 6 && TouchesVocabulary(run))
            {
                Unrecognised.TryAdd(run, 0);
            }

            return false;
        }

        tokens = found;
        return true;
    }

    private static string Join(List<string> tokens)
    {
        StringBuilder built = new(16);
        foreach (string t in tokens)
        {
            built.Append(Casing.TryGetValue(t, out string? cased) ? cased : PascalFirst(t));
        }

        return built.ToString();
    }

    private static bool Solve(string run, int start, List<string> acc, bool[] failed)
    {
        if (start == run.Length)
        {
            return true;
        }

        if (failed[start])
        {
            return false;
        }

        foreach (string t in ByLength)
        {
            if (t.Length > run.Length - start ||
                string.CompareOrdinal(run, start, t, 0, t.Length) != 0)
            {
                continue;
            }

            acc.Add(t);
            if (Solve(run, start + t.Length, acc, failed))
            {
                return true;
            }

            acc.RemoveAt(acc.Count - 1);
        }

        failed[start] = true;
        return false;
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
