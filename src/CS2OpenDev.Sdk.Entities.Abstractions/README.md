# CS2OpenDev.Sdk.Entities.Abstractions

The read contract that generated Counter-Strike 2 entity wrappers are emitted against.

A demo parser implements it. Generated wrapper code consumes it. It carries field identity and
value semantics, and deliberately carries nothing about storage, decode or lifetime — those are
private engineering decisions every parser makes differently, and a seam that encoded one
parser's answers would be that parser wearing an interface.

Zero dependencies outside the BCL. Trimmable and AOT-compatible.

## The shape

| Type | Role |
|---|---|
| `IEntityFieldReader` | Read one entity's current field values, by ordinal |
| `IEntityWorld` | Turn a raw packed handle into a wrapper — one member |
| `EntityWrapper` | Base class holding a reader and a world |
| `EntityClassBinding` | Pure-data manifest: canonical paths, aliases, handle ordinals |
| `QAngle` | Euler angles, since `Vector3`'s `X/Y/Z` names mislead on an angle triple |
| `SchemaFieldVersionAttribute` | Which CS2 builds a field existed in |
| `DictionaryEntityReader` | Reference implementation and conformance kit |
| `BindingConformance` | Structural checks a well-formed binding passes |

## Two things worth knowing before implementing it

**Absent is not zero.** Every `TryRead*` returns `false` when a field has never been received on
the wire, which is different from receiving a default. `m_lifeState`'s `0` means `LIFE_ALIVE`, so
a reader that cannot distinguish the two reports corpses as healthy. If your storage does not
already track per-field presence, this is the member that will tell you.

**Handles cross undecoded.** `TryReadEntityHandle` hands back a raw `uint`. The packing is
`(serial << index_bits) | index` and how many bits the index gets is not documented
authoritatively upstream — two implementations in this ecosystem already disagree. Mask, sentinel
encodings and serial validation are yours. Resolution goes through `IEntityWorld.Resolve<T>`,
which returns `null` for every way a handle can fail to name a live entity of the requested type.

## Implementing it

Bind once per class, read many times. The binding gives you `CanonicalPaths`; build your own
`ordinal → wherever-you-keep-that-field` map from it at bind time, falling back through `Aliases`
when the canonical path is absent from the demo's serializer — that fallback is what lets a
wrapper generated today read a recording made before Valve renamed the field.

Bind against the array, never against hard-coded ordinals. Ordinals are an implementation detail
shared between a generated wrapper and the manifest emitted beside it, and they are not stable
across releases.

## Testing it

`DictionaryEntityReader` is the reference implementation, and running your reader against the
same assertions is how you find out whether you agree with it about what the contract means:

```csharp
var binding = new EntityClassBinding(
    EngineClass: "CCSPlayerPawn",
    NetName: "CSPlayerPawn",
    CanonicalPaths: ["m_ArmorValue", "m_CBodyComponent.m_pSceneNode.m_vecOrigin"],
    Aliases: new Dictionary<string, string>
    {
        ["m_vecOrigin"] = "m_CBodyComponent.m_pSceneNode.m_vecOrigin",
    },
    HandleOrdinals: []);

var reader = new DictionaryEntityReader(binding, new Dictionary<string, object?>
{
    ["m_ArmorValue"] = 100,
});

reader.TryReadInt32(0, out int armor);   // true, 100
reader.TryReadInt32(1, out _);           // false — never received, not zero
reader.TryReadByEnginePath("m_vecOrigin", out _); // alias resolves to the canonical path
```

`BindingConformance.ThrowIfInvalid` checks a manifest's structural invariants — dense ordinals,
aliases that resolve, handle ordinals in range — with nothing constructed. Worth running at
startup over whatever binding set you load.

## Versioning

`0.x` because no runtime has implemented this yet. It goes to `1.0` when a real parser has and
the conformance suite passes against it — `1.0` is the claim that the shape survived contact with
a second implementation, and that claim is not ours to make alone.

This package does **not** share the major version of `CS2OpenDev.Sdk` / `.GameEvents` / `.Protos`.
Those regenerate together from the schema; this is a contract with its own life, and its
`version.json` is scoped to its own directory so a schema regen cannot move it.
