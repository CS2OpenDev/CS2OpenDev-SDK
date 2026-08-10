# CS2OpenDev.Sdk.GameEvents

Decodes Counter-Strike 2's legacy game-event wire messages into the strongly-typed event records
shipped by [`CS2OpenDev.Sdk`](https://www.nuget.org/packages/CS2OpenDev.Sdk).

Every CS2 demo parser ends up writing the same code: key-by-name dispatch, the `val_long` /
`val_short` / `val_byte` fallback chain, entity-handle and controller-slot handling. This is that
code, generated from the same schema the records come from.

```csharp
using CS2OpenDev.Sdk.GameEvents;
using CS2OpenSchema.Events;

var decoder = new GameEventDecoder();
decoder.LoadDescriptors(eventList);        // once, when the demo stream yields it

if (decoder.TryDecode(msg, out object? ev) && ev is PlayerDeathEvent death)
{
    Console.WriteLine($"{death.Attacker} killed {death.Userid} with {death.Weapon}");
}
```

## Why you need the descriptor table

The wire format splits an event across two messages:

- **`CMsgSource1LegacyGameEventList`** arrives once and declares, per event id, the event's name and
  its key names **in order**.
- **`CMsgSource1LegacyGameEvent`** carries an event id and a **positional** value list. No names.

Neither is decodable alone, and the table never repeats. So `GameEventDecoder` is stateful for the
lifetime of a demo: feed it the list when you see it, then feed it events. If you decode without
loading descriptors, `TryDecode` returns `false` rather than guessing.

## The integer fallback chain

`key_t` has four integer widths plus a bool, and **the server is not obliged to use the one the
schema declares** — it writes the narrowest slot the value fits. A decoder that reads only the
declared slot silently returns 0 for values that are plainly on the wire. This is the single most
common way a hand-rolled decoder goes wrong.

`GameEventReader` collapses whichever slot was actually used, widest-first, so a value that needed
the wider slot is never truncated. Narrow properties saturate rather than wrap — an out-of-range
value becomes `short.MaxValue`, not a plausible-looking negative.

## Duplicate event names

Native event names are **not unique**. Across 289 declarations there are 273 distinct names: 15
carry more than one, because the same event is declared in several `.gameevents` files with
different field sets. `player_death` has two declarations (core: 2 fields, mod: 22);
`round_end` has three.

```csharp
// Resolves the declaration CS2 actually fires — mod overrides game overrides core.
GameEventRegistry.TryGetFactory("player_death", out var factory);

// Every declaration, if you specifically want an older shape.
foreach (var d in GameEventRegistry.GetAllFactories("player_death"))
{
    Console.WriteLine($"{d.Source} -> {d.RecordType.Name}");
}
```

A registry keyed on name alone cannot round-trip. If your dispatcher assumes one record per name,
that assumption holds for 258 of 273 and silently truncates on the rest.

## Transport context

Records model exactly what the schema declares and nothing else. When an event happened is a
property of the fire, not of the event, and it comes from the demo container rather than the event
message — so it travels in an envelope:

```csharp
if (decoder.TryDecode<PlayerDeathEvent>(msg, gameTick, frameNumber, out var envelope))
{
    // envelope.Payload, .EventId, .ServerTick, .GameTick, .FrameNumber
}
```

The generated records are `partial` and property-based, so if you would rather carry this on the
record itself, add it in a sibling file. The envelope is the recommended shape, not the only one.

## Performance

Factories are generated, not reflective. The records carry `[NativeName]` and
`[GameEventFieldType]` for consumers who want to introspect, but the decode path never reads them —
a demo can fire hundreds of thousands of events, and an attribute lookup per field is not something
to pay on that path. `GameEventReader` is a struct over two references, so decoding allocates only
the record itself.

Key lookup is a linear scan over the descriptor's key names. Events carry a handful of keys (the
widest in the schema has 18), and building a per-event dictionary costs more than the scan.

## Customising the projection

By default all three player-reference tags (`player_controller`, `player_pawn`,
`player_controller_and_pawn`) project to `int` — the raw userid the engine emits — with the original
tag preserved on the property. If you build the SDK from source and would rather the records carried
your own type, drop a `game-event-overrides.json` next to the schema:

```json
{
  "usings": ["MyGame.Model"],
  "fieldTypes": {
    "player_controller_and_pawn": {
      "csharpType": "PlayerRef",
      "readAs": "Int32",
      "wrap": "new PlayerRef({0})"
    }
  }
}
```

The same entry drives the record property **and** the factory that fills it, so the two cannot
disagree. `readAs` must name a `GameEventReader` accessor; a typo fails at generation time with a
message naming the valid set, rather than emitting code that will not compile.

## Dependencies

| Package | Why |
|---|---|
| [`CS2OpenDev.Protos`](https://www.nuget.org/packages/CS2OpenDev.Protos) | the decoder's input type is a protobuf message |
| [`CS2OpenDev.Sdk`](https://www.nuget.org/packages/CS2OpenDev.Sdk) | the event records |

This package exists so that split can hold: `CS2OpenDev.Sdk` ships **zero** package dependencies,
and a consumer who only wants schema types is never made to take `Google.Protobuf`. CI asserts that
property on every build.

MIT.
