# issue #30 — wrapper inheritance and the ordinal space: design

> **Status: proposal. Nothing here is implemented, and merging this document does not
> accept it.** It exists so the argument and the verified numbers survive somewhere other
> than an issue thread, and so the decision can be taken — or refused — against something
> concrete. Section 6 states what this repository cannot prove about it, which is the part
> that should be read before section 2.

Decision document for CS2OpenDev/CS2OpenDev-SDK#30. Everything asserted below was
re-verified against the tree as of #29; where the issue's numbers are
repeated, they were checked first.

**Re-checked after #35, #36, #37 and #38 landed.** What moved, and what it does to the
argument:

#35 (Abstractions 1.0.0) changed only `README.md` and `version.json` in the contract
assembly, so every contract claim in §1 and §3 holds verbatim. It does raise the price:
a seam change is now a MAJOR, which is an argument *for* the option chosen here, since
it moves the contract not at all.

#36 flipped `Origin` to `Vector3?` and cut Entities 0.2.0. Property *types* changed on
three classes; the census below is unaffected — still 58 wrappers, 58 `sealed`, same
field counts and ordinals. #37, the 6.0 atomics, touched `src/CS2OpenDev.Sdk/` only and
does not touch the entity wrappers or the contract.

#38, the C# surface gate, narrows §5's stated risk considerably: a schema reparent that
changed emitted base types would now be reported rather than published silently. It does
**not** eliminate it. A base swap is case 1 in that gate's own "cannot catch" list, so
the pinned-hierarchy census in step 4 is still required.

The flatness the issue exists to fix is unchanged: 58 wrappers, 58 of them `sealed`,
deriving `EntityWrapper` directly.

## 1. what is actually true today

The flatness claim is exact. All 58 emitted wrappers are `public sealed class X(...)
: EntityWrapper(reader, world)`: 58 files under
`src/CS2OpenDev.Sdk.Entities/Generated/`, 58 `sealed`, zero deriving anything but
`EntityWrapper`. The emitter hardcodes both tokens at
`src/CS2OpenDev.Sdk.Generator/Emitters/EntityWrapperEmitter.cs:219-220`.

So is the failure mechanism. `BasePlayerWeapon.Clip1` compiles to
`Reader.TryReadInt32(Ord.Clip1, ...)` with `Ord.Clip1 = 2`
(`src/CS2OpenDev.Sdk.Entities/Generated/BasePlayerWeapon.cs:31,59`). `CAK47` curates no
fields, so its binding's `CanonicalPaths` is empty, and the reference reader
bounds-checks the ordinal and reports absent (the bounds check in
`src/CS2OpenDev.Sdk.Entities.Abstractions/DictionaryEntityReader.cs`). If `AK47`
inherited `BasePlayerWeapon` today, every inherited property would silently read absent.
The issue describes this correctly.

The invariant is real, and it was verified in source rather than taken from the issue.
`LensClassState.Fields` is a `SortedDictionary` keyed `StringComparer.Ordinal`
(`src/CS2OpenDev.Sdk.Generator/SchemaLens/LensModels.cs:110`). The planning walk
(`EntityWrapperEmitter.Plan`) assigns ordinals in that iteration order, and
`EmitBinding` in the same file writes `CanonicalPaths` from the same `FieldPlan` list in
the same order. Both are emitted from the one in-memory state in one exporter pass —
"two projections of one walk" (`src/CS2OpenDev.Sdk.Exporter/Program.cs:426-428`). So the
invariant has two parts, and they are not equally load-bearing. Correctness rests on
*agreement by construction* between a wrapper's `Ord` constants and its manifest's
`CanonicalPaths`; that part must survive. The other part, *ordinal-sortedness* of each
class's path array, is a repo convention. It shows up in the emitter's header comment
and in one test, `CanonicalPaths_AreOrdinalSorted`
(`test/CS2OpenDev.Sdk.Entities.Tests/EmittedWrapperTests.cs`). It appears **nowhere in
the contract**: `IEntityFieldReader` requires only that ordinal `i` is the field at
`CanonicalPaths[i]`
(`src/CS2OpenDev.Sdk.Entities.Abstractions/IEntityFieldReader.cs:9-15`),
`EntityClassBinding` requires "dense from zero" (`EntityClassBinding.cs:25-31`), and
`BindingConformance` checks density, duplicates, alias resolution and handle-ordinal
range, not order (`BindingConformance.cs:48-99`). The distinction is the whole design
space of this issue.

The counts check out. `schema-lens/state.json`: 58 curated classes, 45 with zero
fields, 13 with fields totalling 144. Emitted: 144 field properties plus 7 handle
companions = 151 public properties; `EntityWrapperRegistry.Create` has 56 cases (58 minus
`CCSWeaponBaseShotgun` and `CBaseCSGrenade`, per `EntityWrapperEmitter.cs:53-57`).

The hierarchy below was computed from the schema, not quoted from the issue: walk each
curated class's `Parents[0]` chain in
`upstream/docs/generated/downstream-codegen-schemas/cs2_schema.json` to the nearest
curated ancestor.

```
CBaseCSGrenadeProjectile (12)        CBasePlayerWeapon (8)
├─ CDecoyProjectile (4)              ├─ CBaseCSGrenade (0)
├─ CFlashbangProjectile (3)          │  └─ 5 grenades (0 each)
├─ CHEGrenadeProjectile (0)          ├─ CC4 (0)   ├─ CKnife (0)
├─ CMolotovProjectile (2)            ├─ CCSWeaponBaseGun (3)
└─ CSmokeGrenadeProjectile (6)       │  └─ 32 guns (0 each)
                                     └─ CCSWeaponBaseShotgun (0)
CCSPlayerPawnBase (6)                   └─ 3 shotguns (0 each)
└─ CCSPlayerPawn (32)
roots with no curated ancestor: CCSGameRules, CCSGameRulesProxy,
CCSPlayerController, CPlantedC4
```

51 of 58 classes have a curated ancestor; 6 classes are curated bases, exactly the set
`hasCuratedDescendant` already computes (`EntityWrapperEmitter.cs`). Uncurated
intermediates (`CCSWeaponBase`, `CEconEntity`, ...) contribute nothing and are skipped, per
the issue's diagram. Also checked: the schema has multiple inheritance on these chains
(`CPlantedC4` and `CEconEntity` carry `IHasAttributes` as a second parent), and the
first-parent-only walk reaches the same curated ancestors as a full-graph walk for all 58
— today. The emitter should keep using `Parents[0]` for the new computation so the two
walks cannot disagree.

Two things the committed tree got wrong that the issue does not mention, both since
fixed. `src/CS2OpenDev.Sdk.Entities/README.md` was written in #27 and not touched by #28
or #29, so as of #29 it still:

- told a runtime that also loads the Schema Lens to "compare hashes at startup", the exact
  advice #28 removed from the registry docs because the two preimages are not comparable;
- showed `BasePlayerWeapon? gun = pawn.ActiveWeapon;`, false since #29 typed both weapon
  companions `EntityWrapper?`.

Both were fixed in #36 alongside the `Origin` flip, which had made a third example
(`Vector3 at = pawn.Origin;`) false as well. Recorded here because the pattern is the
finding, not the three instances: **the README is prose beside generated code with nothing
checking that the two agree**, and it had drifted three times in three days. #38's surface
gate does not close this; it compares emitted C# against emitted C#, and a README is
neither side of that comparison.

If this design is implemented, the companion example becomes true again and that section
of the README needs a fourth edit. Step 6 covers it.

## 2. the decision

**Emit inheritance and take option 1, with the ordinal layout rule changed from
"ordinal-sort of own ∪ inherited" to "the base chain's ordinal space verbatim as a
prefix, then own fields ordinal-sorted after it".** This is single-inheritance object
layout applied to ordinal spaces. It makes every base ordinal constant valid in every
descendant's binding, moves the contract not at all, and adds nothing to the read path.

## 3. the argument

The issue's objection to option 1 ("a base field's ordinal differs per derived class,
so a compile-time constant in the base cannot address it") is true only under the
sorted-merge layout. It is an artifact of the sort rule, not of inheritance. And the sort
rule is not contract (section 1): the contract requires `ordinal i ↔ CanonicalPaths[i]`,
dense, duplicate-free, and nothing about order. Change the layout law to

```
layout(C) = layout(nearestCuratedAncestor(C)) ++ ordinal-sort(ownFields(C))
```

and base ordinals are identical in every descendant by construction, exactly as a C++
base-class subobject sits at offset 0 of every derived object. Concretely:
`CBasePlayerWeapon` keeps its 8 paths and its `Ord` block byte-for-byte;
`CSWeaponBaseGun`'s binding becomes those 8 paths then `m_bNeedsBoltAction`,
`m_iBurstShotsRemaining`, `m_zoomLevel` at ordinals 8-10; `AK47`'s binding is
`CSWeaponBaseGun`'s 11 paths verbatim. `BasePlayerWeapon.Clip1`'s `Ord.Clip1 = 2` reads
`m_iClip1` through all three bindings. The failure mode this issue exists for cannot
occur, and no runtime does anything new: it binds ordinal→storage from the array, as the
only supported way already is (`IEntityFieldReader.cs:12-14`).

What survives, what changes:

- Agreement by construction survives: one layout computation per class, `Ord` constants
  and `CanonicalPaths` both projected from it, same walk, same pass.
- The read path is unchanged. Compile-time constant into an array-backed binding; no
  string lookup, no virtual call, no indirection, no per-instance state.
- The contract is untouched (section 4).
- Renumbering blast radius grows. Today a rename renumbers one class's space; under
  prefix layout a curation change to `CBasePlayerWeapon` shifts the own-segment of all 45
  descendant bindings. Acceptable for the same reason renumbering is acceptable at all:
  ordinals are declared unstable across releases, wrapper and manifest move together, and
  runtimes bind against the array. It is a generated-diff-size cost, not a correctness
  cost.
- Manifest size grows too. Total `CanonicalPaths` entries go 144 → 666 (sum of
  own + inherited across 58 classes), which is 522 more path strings plus the inherited
  alias rows in `EntityWrapperRegistry.cs`. Tens of KB of generated data. The 45 markers'
  bindings stop being empty, which is the point: a marker's binding *is* its base's
  layout.
- One new curation law, gated. The same canonical path (or the same `targetProperty`)
  curated at two levels of one curated chain is now an error; today it is legal because
  every class owns its space. Checked against current state: no such collision exists.
  The emitter must fail it at generation with an error-severity diagnostic (the exporter
  exits non-zero on those — the post-3.0.7 guard), rather than letting
  `BindingConformance`'s duplicate check catch it at some consumer's startup.

### why not option 2 — base properties resolve by path

The contract's only by-name member is `TryReadByEnginePath(string, out object?)`:
boxed, engine-spelling, and documented "Not for hot paths" (`IEntityFieldReader.cs:143`).
Routing 144 typed properties through it costs a dictionary hash per read, boxes every
value type, and loses the typed accessors the dispatch table exists to pick
(`EntityWrapperEmitter.cs:172-201`). Doing it properly means adding typed by-path members
to `IEntityFieldReader`, and that interface member addition breaks every implementer of a
frozen contract with an external implementation already validated against it
(`Abstractions/version.json`, the 0.3 note). That is the expensive contract change the
issue warns about, spent to buy a slower read path. It also quietly inverts the design
premise: the ordinal space exists precisely so reads are constant-time array math; a
contract whose base properties hash strings has conceded that premise.

### why not option 3 — derived classes re-declare inherited properties

The issue's phrasing ("`new`-shadowing visible in the API") undersells the defect.
Shadowing here is not merely ugly; it reads the wrong data. Through a base-typed
reference (exactly what the restored `BasePlayerWeapon? ActiveWeapon` companion hands
out), a `new`-shadowed property executes the *base* body with the *base* ordinal against
the *derived* binding, and under sorted-merge layout silently reads the wrong field or
absent. Trading "silently null" for "silently wrong value" is a regression. The correct
variant is virtual-plus-override, which works but costs: a virtual dispatch on every
read; 522 override bodies (666 − 144, a 4.6× property surface, re-emitted on every
regen); and it *still* requires derived bindings to carry the inherited paths, because
the derived ordinals must address them. So option 3 ends up as option 1's manifest growth
plus the emitted bloat plus a per-read vcall, and there is no axis on which it beats
prefix layout.

### why not option 4 — per-instance ordinal offset / indirection table

The base body becomes `Reader.TryReadInt32(_map[Ord.Clip1], ...)`, and the map must
reach the wrapper at construction. So either `EntityWrapper`'s constructor changes,
which is a contract move (`Abstractions` ships `EntityWrapper`, `EntityWrapper.cs:25`,
and a new constructor parameter is source-breaking for every subclasser, including
DemoViewer.NET's conformance fixtures), or each generated class carries generated map
data privately, adding an array load to every read and per-instance state to a wrapper
that is currently two references. The payoff is nil either way: under single
inheritance, prefix layout **is** the offset table with every offset zero. Option 4 is
what you build when prefix layout is impossible — curated multiple inheritance, or an
immovable sorted-order requirement. Neither holds: the second parents on curated chains
are mixin interfaces the emitter already ignores, and sortedness is a repo test, not a
promise.

### the residual choice inside option 1

Sorted-merge layout (the issue's literal option 1) has no way to keep base constants
valid, so within option 1 the prefix layout is not one alternative among several; it is
the only layout under which compile-time base ordinals are correct in derived bindings.
The deliberate cost is giving up "every binding's path array is globally sorted" in
exchange for "a base's array is a verbatim prefix of each descendant's" — a stronger,
more useful invariant, and one a test can state exactly (step 5).

## 4. what it does to the contract

**`CS2OpenDev.Sdk.Entities.Abstractions` does not move.** No interface, record, or class
changes; no doc-comment change is required, because the contract never stated an ordering
(re-read `IEntityFieldReader.cs:9-15`, `EntityClassBinding.cs:25-31`,
`BindingConformance.cs:30-101`: density, duplicates, alias resolution, handle range, and
order is never mentioned). Deliberately do **not** add the prefix rule to the contract
docs: it is an emission policy of this generator, and freezing it into the seam would
turn the next layout change into a contract event. `version.json` stays at 0.3; the 1.0
re-validation criterion is unaffected. DemoViewer.NET's adapters, which bind any
`EntityClassBinding` by walking the array (their F4: zero adapter changes), consume the
new bindings without modification. That is the claim their stage 2/3 rerun must confirm,
not one this repo can assert.

**The regenerated `CS2OpenDev.Sdk.Entities` package changes visibly**, and versions as
breaking (0.1 → 0.2 in its own `version.json`; in 0.x the minor is the breaking slot, per
the convention written down next door):

- 6 classes lose `sealed` and 51 gain a base type, which is additive;
- `ActiveWeapon`/`LastWeapon` retype `EntityWrapper?` → `BasePlayerWeapon?`, the #29
  reversal the issue names as done-looks-like. Source-compatible narrowing, binary-
  breaking, hence the minor bump;
- the 45 markers gain inherited properties and non-empty bindings. Two DemoViewer.NET
  census tests will fail on facts of 0.1.1 they pinned: "the 13 property-carrying
  wrapper types are exactly the 13 bindings with a non-empty ordinal space", and any
  ported sortedness assertion. Those pin the previous emission, not the contract; flag
  both in the handoff so the failures are expected rather than alarming.

A new unattended-regen exposure, and its pin. Base-type edges now derive from the
live schema, so a Valve reparent would change the emitted C# hierarchy in a 4-hourly
unattended regen, and nothing checks the generated C# API surface (the still-open half
of the 3.0.7 lesson). Mitigation in step 5: commit the 58 base-type edges as a pinned
test expectation, `names.lock` discipline — a reparent fails CI until a human edits the
pin. It is the first pin on any C# API axis; narrow and cheap.

## 5. implementation plan

1. **`src/CS2OpenDev.Sdk.Generator/Emitters/EntityWrapperEmitter.cs`: layout and
   hierarchy.** Beside `hasCuratedDescendant` (lines 72-94), compute
   `nearestCuratedAncestor` from the same `firstParent` map. Replace the per-class walk in
   `Plan` with a memoized `layout(class)` implementing the prefix rule; `BindingPlan`
   gains base net-name, inherited paths, inherited aliases (filtered against the combined
   path set, same identity-alias rule as lines 149-157), and inherited handle ordinals;
   own `FieldPlan` ordinals offset by prefix length. Add emit-time gates, error severity:
   canonical path or `targetProperty` curated twice along one chain; alias-key conflict
   across levels. Rewrite the header comment (lines 21-26) to state the new layout law
   and what it deliberately gives up.
   *Test:* the exporter run in step 2 and the suite in step 4; the emitter has no direct
   unit harness (`GeneratorPipelineTests` drives `ModuleEmitter` only) and the emitted
   package is where this repo tests wrapper behavior.
2. **`EmitWrapper` / `CompanionType` / `EmitBinding`, same file.** Base type in the class
   declaration (`: {BaseNetName}(reader, world)` or `EntityWrapper`); `sealed` only when
   the class has no curated descendant. Delete the `hasCuratedDescendant →
   "EntityWrapper"` branch (lines 343-345) so a curated target with curated descendants
   gets its own net name again. `EmitBinding` writes inherited + own paths, merged
   aliases, inherited + shifted handle ordinals. `NoFactoryRegistration` and the factory
   switch are untouched — its own comment (lines 48-52) was written anticipating exactly
   this: bases curated "so the type hierarchy is complete".
   *Test:* regen fixed point (run the exporter twice, tree identical).
3. **Regenerate `src/CS2OpenDev.Sdk.Entities/Generated/`.** Spot-read: `AK47.cs` is
   `public sealed class AK47 ... : CSWeaponBaseGun(...)` with an empty body;
   `BasePlayerWeapon.cs` is unsealed with its `Ord` block unchanged;
   `CSWeaponBaseGun.cs` has `Ord` 8/9/10; `CSPlayerPawn.cs` derives `CSPlayerPawnBase`,
   its own ordinals shifted by 6, companions typed `BasePlayerWeapon?`; the registry
   carries 58 bindings, ~666 paths, and the `m_vecOrigin` alias on all five projectile
   descendants.
   *Test:* `dotnet build --configuration Release -nologo` compiles the package;
   shadowing or ordinal collisions surface as compile errors here.
4. **`test/CS2OpenDev.Sdk.Entities.Tests/EmittedWrapperTests.cs`.** Replace
   `CanonicalPaths_AreOrdinalSorted` (lines 43-52) with the prefix law: for every binding
   with a curated base, the base binding's `CanonicalPaths` is a verbatim prefix and the
   own suffix is ordinal-sorted; roots stay fully sorted. Add the inheritance read:
   `Create("CAK47", ...)` over the AK47 binding with `m_iClip1 = 30`, then
   `((BasePlayerWeapon)w).Clip1 == 30` — a base body over a derived binding, the exact
   read this issue exists to make correct. Flip
   `WeaponCompanions_AreTypedForWhatARuntimeActuallyReturns` (lines 217-242): a resolved
   `SmokeGrenade` now satisfies the `BasePlayerWeapon?` companion, and the cast at line
   181 disappears. Also add: derived projectile bindings carry the inherited origin alias
   and still conform; marker bindings equal their base's paths; and the pinned hierarchy
   census, all 58 base-type edges as literal expected data, so an unattended schema
   reparent fails CI.
   *Test:* `dotnet run --configuration Release --project
   test/CS2OpenDev.Sdk.Entities.Tests` (TUnit exe; never `dotnet test`).
5. **`src/CS2OpenDev.Sdk.Entities/version.json`: 0.1 → 0.2.** Breaking slot for the
   companion retype; `versionHeightOffset: -1` is already in place, so this cuts a real
   0.2.0.
   *Test:* `nbgv get-version` in the package directory resolves 0.2.0.
6. **`src/CS2OpenDev.Sdk.Entities/README.md`.** Fix the skew-detection paragraph (lines
   61-66) to match the #28 registry docs: assert your hash against your state, never
   across the seam. Document the hierarchy and the prefix rule, including what a consumer
   may not do (hard-code ordinals; unchanged advice, sharper teeth). The line-37 example
   becomes true again; keep it.
   *Test:* prose review against the registry doc-comment; no automated check exists for
   README/emitter agreement, and this change is the second time this README drifted,
   which is worth saying in the PR.
7. **Hand off to DemoViewer.NET on #30/#25.** Name the two census assertions that will
   fail and why, and request the stage 2/3 rerun. Their battery is the acceptance test;
   this repo's steps end at publishing the package.

## 6. what this cannot be verified by in-repo

This repository has no demo parser and, per the #6 decline that created the contract,
never will. Everything above is provable here only over `DictionaryEntityReader`, and
that reader has now missed two real bugs (the hash-comparability claim, the handle
width-fold) for the same structural reason: its fixtures write exactly the values our own
tests expect, so an assumption consistent with itself survives every test this repo can
write. Prefix layout is such an assumption until a foreign runtime runs it.

Specifically not verifiable here:

- that a registry-faithful runtime's storage materializes **inherited** fields for
  concrete weapon entities, i.e. that binding `AK47`'s manifest against a real flattened
  serializer resolves `m_iClip1` for a live `CAK47`. The schema says the serializer
  carries the base's fields; no byte in this repo confirms it;
- that `ActiveWeapon` is non-null on a real demo, the issue's own done-looks-like;
- that DemoViewer.NET's candidate-walk binder takes the 666-path binding set with
  inherited aliases without a policy surprise, at zero stage-3 mismatches.

The prefix-law test in step 4 checks the two projections against each other, and they
agree by construction; it deliberately cannot catch a layout that is self-consistently
wrong about the wire. The acceptance test is DemoViewer.NET's stage-3 real-demo A/B,
joined by canonical path through the alias tables, exactly as #25 divided the labor.
