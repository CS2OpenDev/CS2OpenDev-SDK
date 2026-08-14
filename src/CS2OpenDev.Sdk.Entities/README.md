# CS2OpenDev.Sdk.Entities

Typed entity wrappers for Counter-Strike 2, generated from the curated Schema Lens state.

They read through [`CS2OpenDev.Sdk.Entities.Abstractions`](../CS2OpenDev.Sdk.Entities.Abstractions/README.md)
and touch nothing else — no schema type, no protobuf message, no parser. That is what lets the same
wrappers run over any demo parser implementing the contract, rather than over one particular
runtime.

**Published to GitHub Packages, not NuGet.org.** Add
`https://nuget.pkg.github.com/CS2OpenDev/index.json` as a package source, or take the `.nupkg` off
a [release](https://github.com/CS2OpenDev/CS2OpenDev-SDK/releases).

## What you get

58 wrapper classes over the curated set, and a registry:

```csharp
// Bind once per class at startup — your runtime builds its own
// ordinal-to-storage map from the manifests.
foreach (EntityClassBinding binding in EntityWrapperRegistry.Bindings)
{
    myRuntime.Bind(binding);
}

// Construct when an entity of a known class appears.
EntityWrapper? w = EntityWrapperRegistry.Create(engineClassName, reader, world);

if (w is CSPlayerPawn pawn)
{
    int health = pawn.Health;                   // absent reads as 0
    int? life  = pawn.LifeState;                // absent reads as null — 0 means LIFE_ALIVE
    Vector3? at = pawn.Origin;                  // absent reads as null — null is the normal case
    ulong buttons = pawn.Buttons;

    uint raw = pawn.ActiveWeaponHandle;         // packed, undecoded
    BasePlayerWeapon? gun = pawn.ActiveWeapon;  // resolved by your runtime
}
```

## Two read policies, and the difference matters

Most properties are **0-default**: a field that was never received reads as zero, which is
harmless when zero is not a meaningful value.

A curated few are **seen-aware** and typed `T?`, because a zero would be read as data. Which fields
get which policy is a per-field judgement recorded in the generator, not something inferred from a
type — and the reason differs per field, so read the property's `<remarks>` rather than assuming.

Two ways in so far:

- **A received zero is a state.** `m_lifeState`'s `0` is `LIFE_ALIVE`, so a 0-default getter would
  make a pawn that never transmitted the field indistinguishable from a live one.
- **The value never arrives at all.** `Origin`'s canonical path names a struct
  (`CNetworkOriginCellCoordQuantizedVector`) whose leaves are what the wire carries, so the parent
  path does not materialise over a GOTV demo and a 0-default presented that absence as the world
  origin. Here `null` is the *normal* case, not an edge case — it does not mean the entity is at
  `(0,0,0)`, and it does not mean your runtime dropped something. A runtime that reconstructs world
  coordinates from the cell leaves and stores the result under this path serves it through this
  property.

## Handles cross undecoded

A handle property gives you the raw packed `uint`. The companion property — `ActiveWeapon` beside
`ActiveWeaponHandle` — asks your runtime to resolve it, and is `null` when the handle names no live
entity of that type.

Only handles whose target is itself a curated class get a companion. `m_hOwnerEntity` points at
`CBaseEntity`, which this package does not wrap, so it exposes the raw handle alone rather than
inventing a type for it.

## Skew detection

`EntityWrapperRegistry.LensHash` and `.SchemaBuild` identify the curated state these wrappers were
generated from. If your runtime also loads the Schema Lens, compare hashes at startup: a mismatch
means the curation moved without the wrappers being regenerated, and that skew otherwise surfaces
as fields quietly reading absent.

## Versioning

`0.x` until a second implementation has run these wrappers over a real demo. The package
self-verifies — every manifest passes `BindingConformance`, and the wrappers are exercised over the
reference reader with no parser present — but "compiles and self-verifies" is not "correct", and
the difference is what `1.0` claims.

This package regenerates with the schema, so it moves when the curated state does. It does not
share a version with the contract next door, which deliberately does not.
