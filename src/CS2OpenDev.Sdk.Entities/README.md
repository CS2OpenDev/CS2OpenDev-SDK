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

61 wrapper classes over the curated set, mirroring the schema's curated hierarchy, and a
registry:

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
    BasePlayerWeapon? gun = pawn.ActiveWeapon;  // resolved by your runtime to the concrete
                                                // weapon wrapper — every one IS a BasePlayerWeapon
}
```

## The hierarchy, and the ordinal layout that makes it correct

Wrapper classes derive from each other exactly as the schema's curated classes do: `AK47` is a
`CSWeaponBaseGun` is a `BasePlayerWeapon`, `CSPlayerPawn` is a `CSPlayerPawnBase`. Uncurated
intermediates (`CCSWeaponBase`, `CEconEntity`, ...) are skipped — the chain hops to the nearest
*curated* ancestor. Most concrete weapon classes curate no fields of their own; their whole read
surface is inherited, and their binding is their base chain's layout verbatim.

That "verbatim" is the load-bearing part. Each binding's `CanonicalPaths` is laid out as

```
layout(C) = layout(nearestCuratedAncestor(C)) ++ ordinal-sort(ownFields(C))
```

— the base chain's ordinal space as a prefix, own fields after it, the way a C++ base subobject
sits at offset 0 of every derived object. A base property's ordinal constant is therefore valid
through every descendant's binding, which is what lets `BasePlayerWeapon.Clip1` read correctly on
a wrapper constructed over `CAK47`'s manifest.

What this means for a consumer, unchanged in substance but with sharper teeth: **never hard-code
an ordinal.** Ordinals were always unstable across releases; under prefix layout a curation change
to a base class renumbers the own segment of *every* descendant's binding at once. Bind against
the `CanonicalPaths` array, the only supported way, and renumbering cannot affect you — wrapper
and manifest ship from one emitter pass and always agree.

One thing the layout asks of the wire: a concrete class's serializer must carry its ancestors'
fields. It does — DemoViewer.NET measured it on live entities before this shipped (SDK#30: every
gun-chain class carries all composed paths; the shotguns carry the weapon base's fields and none
of the gun's, because shotguns are not guns) — but that is a fact about the wire, not something
this package can enforce. The manifests follow the schema's real parent chain and nothing else,
because the wire does too.

## Two read policies, and the difference matters

Most properties are **0-default**: a field that was never received reads as zero, which is
harmless when zero is not a meaningful value.

A curated few are **seen-aware** and typed `T?`, because a zero would be read as data. Which fields
get which policy is a per-field judgement recorded in the generator, not something inferred from a
type — and the reason differs per field, so read the property's `<remarks>` rather than assuming.

Three ways in so far:

- **A received zero is a state.** `m_lifeState`'s `0` is `LIFE_ALIVE`, so a 0-default getter would
  make a pawn that never transmitted the field indistinguishable from a live one.
- **The value never arrives at all.** `Origin`'s canonical path names a struct
  (`CNetworkOriginCellCoordQuantizedVector`) whose leaves are what the wire carries, so the parent
  path does not materialise over a GOTV demo and a 0-default presented that absence as the world
  origin. Here `null` is the *normal* case, not an edge case — it does not mean the entity is at
  `(0,0,0)`, and it does not mean your runtime dropped something. A runtime that reconstructs world
  coordinates from the cell leaves and stores the result under this path serves it through this
  property.
- **A fabricated zero is a coordinate.** The quantized-origin leaves (`OriginCellX/Y/Z`,
  `OriginVecX/Y/Z` on the same three classes) *do* arrive on the wire, but cell 0 is a legal world
  cell — the consumer-side reconstruction is `(cell − 32) × 512 + offset`, so a 0-default would
  place a never-received entity at −16384 on that axis with full confidence. `null` means the leaf
  has not been received yet; on live entities presence is the normal case. The reconstruction
  arithmetic itself stays on your side of the seam, deliberately.

## Handles cross undecoded

A handle property gives you the raw packed `uint`. The companion property — `ActiveWeapon` beside
`ActiveWeaponHandle` — asks your runtime to resolve it, and is `null` when the handle names no live
entity of that type.

Only handles whose target is itself a curated class get a companion. `m_hOwnerEntity` points at
`CBaseEntity`, which this package does not wrap, so it exposes the raw handle alone rather than
inventing a type for it.

The weapon companions — `ActiveWeapon` and `LastWeapon` — are typed `BasePlayerWeapon?`. Their
handles point at concrete weapons on real demos, and every concrete weapon wrapper now derives
from `BasePlayerWeapon`, so your runtime's dispatch to the concrete class satisfies the typed
fold. This is the type they briefly had and lost: under the old flat emission a resolved
`SmokeGrenade` was *not* a `BasePlayerWeapon`, the fold failed for every real weapon, and
`EntityWrapper?` was the honest type until the hierarchy landed
([#30](https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/30)).

## Skew detection

`EntityWrapperRegistry.LensHash` and `.SchemaBuild` identify the curated state these wrappers were
generated from. `LensHash` is the hash of **this repository's** `schema-lens/state.json` under its
own canonical form.

**Do not compare it against a hash your own runtime computes.** An implementation that maintains
its own Schema Lens hashes a different preimage — different fields, different canonical form — so
the two numbers are not comparable and a mismatch would be guaranteed rather than meaningful.
Assert your hash against your state, and this one against the `state.json` this package was
published beside.

Compatibility across the seam is established by **canonical path, not by hash**: two curated states
can describe the same field under different spellings, and the alias tables are what reconcile
them.

## Versioning

**`1.0`.** It stayed `0.x` until a second implementation had run these wrappers over a real demo.
The package self-verifies — every manifest passes `BindingConformance`, and the wrappers are
exercised over the reference reader with no parser present — but "compiles and self-verifies" is
not "correct", and the difference is what `1.0` claims.

DemoViewer.NET ran stages 2 and 3 against 0.3.0 over their own `EntityTracker` and a real GOTV
demo: **4,434 ordinal comparisons, 0 mismatches**, joined by canonical path through the alias
tables, and **zero adapter changes** — the third consecutive round, this time across a 4.6×
manifest growth. The read this package exists for was measured directly: a marker `AK47` binding
reads `Clip1` through a base-typed `BasePlayerWeapon` reference, using the base's compile-time
ordinal against the derived class's binding, while `typeof(AK47)` declares no `Clip1`.

**What that evidence does not cover.** 16 of the 59 curated classes never went live on the
reference demo — `CWeaponAug`, `CWeaponNegev`, `CWeaponRevolver` and 13 more — so they are
unexercised on real bytes. One demo cannot contain every gun. Each is a fieldless marker whose
binding is its base's paths verbatim, byte-identical to markers under the same base that did sweep
clean, and the prefix law is pinned structurally over all 52 derived wrappers rather than only the
live ones. That is why the risk was judged small; it is not zero, and it is written down here
rather than discovered later. `version.json` carries the full list and the reasoning.

This package regenerates with the schema, so it moves when the curated state does. It does not
share a version with the contract next door, which deliberately does not.
