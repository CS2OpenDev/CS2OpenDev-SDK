# CS2OpenDev.Sdk

A strongly-typed C# SDK for the Counter-Strike 2 schema system. Every class, struct, and enum that CS2 reflects through its schema runtime is exposed as a C# type, with the original C++ field names and byte offsets preserved as attributes for native interop work.

The SDK is built from [CS2OpenDev-Docs](https://github.com/CS2OpenDev/CS2OpenDev-Docs), pulled in via the `upstream/` git submodule. Docs enriches the schema extracted per CS2 build by [CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker), which reads the shipped game binaries directly; the SteamDatabase/GameTracking-CS2 and DumpSource2 chain the SDK originally sat on is no longer in the pipeline. The committed source under `src/CS2OpenDev.Sdk/` is the canonical artifact — every file is auto-generated, but the regeneration pipeline lives in this repo so contributors can review what changed when CS2 patches the schema.

---

## Packages

Three packages ship from this repo. They are layered so that taking the schema types never costs you a dependency you didn't ask for.

| Package | What it is | Depends on |
|---|---|---|
| **`CS2OpenDev.Sdk`** | Schema classes, enums and game-event records. | **nothing** |
| **`CS2OpenDev.Protos`** | Generated protobuf message types for the demo/engine wire protocol. | `Google.Protobuf` |
| **`CS2OpenDev.Sdk.GameEvents`** | Decodes `CMsgSource1LegacyGameEvent` into the SDK's typed records. | `CS2OpenDev.Protos`, `CS2OpenDev.Sdk` |

`CS2OpenDev.Sdk`'s **zero dependencies are a deliberate, load-bearing property**, not an accident of the current implementation. A decoder's input type is a protobuf message, so putting one in the SDK would drag `Google.Protobuf` onto every consumer who only wanted to name a schema type. That is the entire reason the decoder is a separate package, and CI fails the build if the SDK's nuspec ever grows a `<dependency>`.

The two new packages carry their own READMEs with the detail: [`CS2OpenDev.Protos`](src/CS2OpenDev.Protos/README.md) (the curated proto subset, the collision domains, the `Google.Protobuf` floor policy) and [`CS2OpenDev.Sdk.GameEvents`](src/CS2OpenDev.Sdk.GameEvents/README.md) (the descriptor-table join, the integer fallback chain, duplicate event names).

> **Upgrading?** Five releases carry migration guides listing every affected name:
>
> - **[4.1 — `CS2OpenDev.Protos`](docs/MIGRATION-4.1-protos.md)** — 188 Game-Coordinator types removed and three `.proto` files dropped, when SchemaTracker v1.3.0 began emitting `cstrike15_gcmessages.proto` as a derived closure. Demo and network wire paths are untouched; only this package's major moves.
> - **[4.1](docs/MIGRATION-4.1.md)** — the player-reference game-event fields decoded the wrong wire key. Adds 59 `*Pawn` companion properties and retypes 11 that had silently decoded as `0` since 1.0. Breaking on paper only: no working code can have depended on a constant zero.
> - **[4.0](docs/MIGRATION-4.0.md)** — ten identifiers the 3.0 pass left run-together (`Isbot` → `IsBot`, `WeaponFauxitemid` → `WeaponFauxItemId`). Also adds three curated event records (`item_drop`, `halftime`, `game_restart`) that fire on the wire but are declared nowhere the extractor can see.
> - **[3.0](docs/MIGRATION-3.0.md)** — generated identifiers move to idiomatic .NET casing (`Userid` → `UserId`, `...ID` → `...Id`). Renames only: nothing moved namespace, nothing was added or removed, no behaviour changed. **Its tables were incomplete — see the correction note and [the 534 omitted enum members](docs/MIGRATION-3.0-enum-members.md).**
> - **[2.0](docs/MIGRATION-2.0.md)** — 297 types moved namespace and 40 were removed, when the schema's namespace key changed from `module` to `projectName`.

### Upstreams

Two submodules, each vending a distinct artifact:

| Submodule | Vends | Used for |
|---|---|---|
| `upstream/` → [CS2OpenDev-Docs](https://github.com/CS2OpenDev/CS2OpenDev-Docs) | enriched schema JSON | `CS2OpenDev.Sdk` |
| `schema-tracker/` → [CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker) | per-build `.proto` files | `CS2OpenDev.Protos` |

Docs publishes the proto surface only as markdown, and SchemaTracker's output is not annotation-enriched, so neither subsumes the other. SchemaTracker is pinned to its `latest` branch (newest build only) rather than the multi-GB full history:

```sh
git submodule update --init --depth 1 schema-tracker
```

---

## Using the SDK

Add the NuGet package to your project:

```xml
<PackageReference Include="CS2OpenDev.Sdk" Version="1.0.0" />
```

Then reference types out of the per-module namespaces. The root namespace is `CS2OpenSchema`; each schema module gets a child namespace (`CS2OpenSchema.Client`, `CS2OpenSchema.Server`, `CS2OpenSchema.Common`, etc.).

```csharp
using CS2OpenSchema.Client;

CCSPlayerPawn pawn = new()
{
    Health = 100,
    ArmorValue = 50,
};
```

### Native-interop attributes

Every property carries metadata that lets you bridge from the managed projection back to the native layout:

```csharp
public partial class CCSPlayerPawn : CCSPlayerPawnBase
{
    [NativeOffset(0x83C)]
    [NativeName("m_iArmor")]
    public int ArmorValue { get; set; }
    // …
}
```

| Attribute | Where | What it carries |
|---|---|---|
| `[NativeName("…")]` | classes, properties, enum members | The original C++ identifier as it appears in `cs2_schema.json`. |
| `[NativeOffset(0x…)]` | properties | Byte offset of the field within its native C++ type. |
| `[NativeSize(N)]` | classes | Informational size in bytes of the native type. **Not** a P/Invoke marshalling contract — the managed layout isn't required to match. |
| `[NativeMetadata("Key", "Value")]` | properties, enum members | Round-trips schema markers (`MPropertyFriendlyName`, `MNotSaved`, `MNetworkVar`, …) so downstream tooling can read them without re-parsing `cs2_schema.json`. |

The reverse lookup — given a generated C# property name, recover the raw C++ field name without reflection — is available via the static `SchemaNames` table:

```csharp
string nativeName = SchemaNames.CCSPlayerPawn.ArmorValue; // "m_iArmor"
```

### Entity and resource handles

Six atomic type names carry entity and resource references, and the schema has no
discriminator field for them — every consumer identifies them by string-matching
the type name. **[docs/HANDLES.md](docs/HANDLES.md)** is the canonical list: what
each of the six means, the typed/untyped split, the C# struct and invalid
sentinel each projects to, the template-argument spellings to strip, and the
prefix-ordering trap (a naive `StartsWith("CStrongHandle")` matches three
different types, one of them untyped).

### Game events

Every `.gameevents` entry in the upstream registry is emitted as a `public sealed record` under `CS2OpenSchema.Events`. Each property's KV1 type tag (`string`, `short`, `ehandle`, `player_controller_and_pawn`, …) is preserved via `[GameEventFieldType("...")]` so demo parsers and dispatchers can recover the original wire shape; the C# property type is the SDK's projection of that tag.

```csharp
using CS2OpenSchema.Events;

PlayerDeathEvent death = new()
{
    Userid = 7,        // KV1 tag: player_controller_and_pawn — raw userid
    Attacker = 12,
    Weapon = "ak47",
    Headshot = true,
    // …every field is `required init`, so the compiler enforces completeness
};

string nativeFieldName = SchemaEvents.PlayerDeathEvent.Weapon;  // "weapon"
string nativeEventName = SchemaEvents.PlayerDeathEvent.EventName; // "player_death"
```

Cross-file duplicates (15 events that appear in more than one source `.gameevents` file with different field shapes) follow source priority `mod > game > core`: the mod variant gets the unsuffixed type, others carry a source-name suffix (e.g. `PlayerDeathEvent` for the CS2 shape, `PlayerDeathCoreEvent` for the Source-2-base shape).

Three of the 276 names are not in the extracted schema at all. `item_drop`, `halftime` and `game_restart` fire in real demos and appear in the `CMsgSource1LegacyGameEventList` descriptor, but nothing upstream declares them; they are carried in a root `game-event-supplement.json`, emitted with `[GameEventSource("sdk.supplement")]`, and documented as curated on the records themselves. The mechanism is additive only — when upstream declares one, generation fails (`CS2_GEN_008`) until the entry is deleted. See `src/CS2OpenDev.Sdk.GameEvents/README.md`.

### Extending generated types

Every class is emitted as `public partial class`. To add your own methods or properties without losing them on regeneration, declare a partial extension **in a different file** that doesn't start with `// <auto-generated/>`:

```csharp
// MyProject/CCSPlayerPawn.Extensions.cs
namespace CS2OpenSchema.Client;

public partial class CCSPlayerPawn
{
    public bool IsLowHealth => Health < 25;
}
```

The regeneration pipeline's stale-file sweep uses the `// <auto-generated/>` marker as the discriminator. Files without it are never touched.

---

## Repo layout

```
src/
  CS2OpenDev.Sdk/             — the generated SDK (4k+ files; what the NuGet ships)
    Client/, Server/, Common/, …  — one file per reflected class/enum, grouped by module
    Events/                       — one record per `.gameevents` entry (292 events — 289 extracted, 3 curated)
    SchemaNames.cs                — reverse-lookup table: C# property → native C++ field
    SchemaEvents.cs               — reverse-lookup table: C# event property → native KV1 name
  CS2OpenDev.Protos/          — protobuf package; compiles ../../protos/ via Grpc.Tools at build
  CS2OpenDev.Sdk.GameEvents/  — decoder, registry, envelope
    Generated/                    — exporter output: 292 factories + the name registry
  CS2OpenDev.Sdk.Generator/   — emitter library (consumes cs2_schema.json + gameevents_schema.json)
  CS2OpenDev.Sdk.Exporter/    — CLI that drives the emitters and writes both output trees to disk
test/
  CS2OpenDev.Sdk.Generator.Tests/     — emitter + model unit tests
  CS2OpenDev.Sdk.GameEvents.Tests/    — decoder tests against real protobuf messages
protos/                        — staged, namespace-injected .proto subset (generated, committed)
  PROVENANCE.json              — CS2 build id / platform / tracker commit the protos came from
scripts/
  normalize-protos.py          — restages protos/ from the schema-tracker submodule
  check-migration-readiness.py — release gate: refuses a schema the SDK cannot attribute
  namespace-diff.py            — which types changed namespace between two SDK trees
  rename-diff.py               — which identifiers were renamed between two SDK trees
names.lock.json                — pinned identifier spellings (generated, committed)
game-event-supplement.json     — curated events absent from the extracted schema (hand-maintained)
upstream/                      — git submodule → CS2OpenDev-Docs (refreshed every 4h upstream)
  docs/generated/downstream-codegen-schemas/
    cs2_schema.json            — entity classes/enums/fields
    gameevents_schema.json     — `.gameevents` registry (KV1)
schema-tracker/                — git submodule → CS2OpenDev-SchemaTracker (`latest` branch)
  artifacts/<build>/windows-x86_64/protos/   — source for protos/
```

The three shipped packages target `net8.0` for broad consumer compatibility. The Generator, Exporter and tests target `net10.0`.

Two directories are **generated but committed**: `src/CS2OpenDev.Sdk/` (plus `src/CS2OpenDev.Sdk.GameEvents/Generated/`) and `protos/`. CI regenerates both and fails on a diff, so a change to either has to arrive with the regeneration that produced it. The C# protoc emits from `protos/` is *not* committed — that would be ~240,000 lines of review surface per CS2 patch, for output the build reproduces exactly.

---

## Regenerating the SDK

Initialise the upstream submodule once after cloning:

```bash
git submodule update --init upstream
```

Refresh it to the latest CS2OpenDev-Docs revision (upstream updates every 4 hours) whenever you want a newer schema:

```bash
git submodule update --remote upstream
```

Then regenerate:

```bash
dotnet run --project src/CS2OpenDev.Sdk.Exporter
```

That single command builds the Generator and Exporter as needed, parses the schema, and writes the per-class layout into `src/CS2OpenDev.Sdk/`. Idempotent — running it twice produces no change.

Optional arguments for non-default paths (e.g. a custom dump or vendored copy):

```bash
dotnet run --project src/CS2OpenDev.Sdk.Exporter -- <schema-path> <output-dir>
```

The Exporter also prunes orphan generated files — classes that disappeared from the schema since the last regen — using the `// <auto-generated/>` first-line marker to discriminate emitter output from any hand-written partial-class extensions.

### The name lock

`names.lock.json` pins every run-together lowercase word the generator has resolved and what it resolved to (`userid` → `UserId`, `database` → `Database`). A locked run is returned verbatim; the word vocabulary in `WordSplitter` is never consulted for it again.

It exists because the vocabulary is **retroactive**. Adding a word so a new field splits correctly also re-splits every existing name that word can now cut — adding `base` turned `Database` into `DataBase`, and `gun` turned `Shotgun` into `ShotGun`. Without the lock, every vocabulary edit is a potential rename of published API, applied by a scheduled job that regenerates and publishes unattended.

- **New names** are decided by the vocabulary and appended to the lock. The regen prints them individually — they are the only identifiers a release could have got wrong, and the review list before shipping.
- **Existing names never change.** Entries are added, never rewritten.
- **A deliberate rename** needs `dotnet run --project src/CS2OpenDev.Sdk.Exporter -- --rebaseline-names`, which re-derives the whole file and renames shipped API. That is a major version bump.

A changed (rather than added) entry in a `names.lock.json` diff means someone re-baselined. Treat it as an API break.

### Versioning

Package versions follow SemVer 2 with build metadata identifying the upstream schema:

```
{Generator-MAJOR}.{Generator-MINOR}.{git-height}+cs.{cs2-build-id}.dump.{yyyy-MM-dd}
```

| Segment | Where it comes from | Who bumps it |
|---|---|---|
| `MAJOR` | `version.json` | Human — reserved for breaking API changes. **Bump every `version.json` still behind the new major** |
| `MINOR` | `version.json` | Human — new emitter features |
| `PATCH` | Git commit height since the last `MAJOR.MINOR` bump | Automatic via Nerdbank.GitVersioning |
| Build metadata | `cs2_schema.json` → `build_id`, `version_date` | Automatic from CI at pack-time |

The build-metadata slot is the Steam **`build_id`** (`24537688`), not the header's `revision` field. In schema 2.0 `revision` is a slash-bearing walker identity (`hl2sdk-cs2/5f891c90…/v1/3d1200e3…`) which is not a legal SemVer 2 build-metadata identifier; `.github/actions/read-schema-metadata` reads `build_id` and fails closed if it is missing rather than falling back to it.

`pathFilters` in `version.json` scopes the patch-bumping commits to `src/CS2OpenDev.Sdk/` only, so every regen produces a monotonically newer version, while contributions to tests / docs / generator code that don't change the SDK content don't churn the version. Build metadata is informational — NuGet shows it in the package details, but ordering is determined by `MAJOR.MINOR.PATCH` alone, which is exactly what we want (every regen advances the SortKey).

**Each package has its own `version.json`**, so each has its own git height and the three patch numbers differ. `CS2OpenDev.Sdk.GameEvents` gained one late — until then it inherited the root file, whose `pathFilters` are `:/src/CS2OpenDev.Sdk/` and do not prefix-match `src/CS2OpenDev.Sdk.GameEvents/`, so a commit touching only that project produced no version change and could not be released at all. Its `versionHeightOffset` exists to clear the versions already published from the inherited clock; the file says why.

**`CS2OpenDev.Protos` has its own `version.json`, and it is a patch clock, not a major one.** Its `pathFilters` cover `protos/`, `src/CS2OpenDev.Protos/` and `scripts/normalize-protos.py`, so a schema regen that leaves the `.proto` files alone does not bump it — that is the point, and it stays. But the `MAJOR` is kept in step with the SDK by hand, so the three packages that ship from this repo read as one product rather than three unrelated ones. A major bump is therefore a multi-file edit; the version numbers will agree on major and minor and differ on patch, which is intended.

The rule above reads as though every break starts in the SDK. Not all do. The `.proto` closure is derived upstream, so Valve — or a SchemaTracker walker change — can delete public types from `CS2OpenDev.Protos` while `cs2_schema.json` is untouched and the SDK's projected API does not move at all. That is what happened at CS2 24701871: SchemaTracker v1.3.0 began emitting `cstrike15_gcmessages.proto` as a derived closure and 188 top-level types left the package, with a two-value metadata edit as the entire SDK diff. Bump the package that broke to the family's current major — the break is what `MAJOR` exists to signal, and a package sitting a major behind cannot signal it. Packages already at that major stay put rather than taking a break they did not have.

### Continuous integration

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | PRs + pushes to main | Build, test, regenerate, fail if the regen output diverges from committed SDK, verify pack succeeds |
| `check-upstream.yml` | Cron (every 4h) + manual | Bump the upstream submodule, regenerate, push to main if anything changed, then invoke the reusable pack-and-publish flow in the same run |
| `release.yml` | Pushes to main that touch `src/CS2OpenDev.Sdk/**` or `version.json` + manual | Invokes the reusable pack-and-publish flow (for human-driven version bumps and the post-merge case) |
| `_pack-and-publish.yml` | `workflow_call` from the two above | Packs the SDK with CS2 build metadata, uploads the `.nupkg` as a workflow artifact, and pushes to NuGet.org (gated on `NUGET_API_KEY`) |

The bot identity used for automated pushes is `CS2OpenDev-bot <bot@CS2OpenDev.invalid>`. `check-upstream.yml` calls the publish workflow as a *dependent job in the same run* (via `workflow_call`) rather than relying on its push to fire `release.yml` — that's because GitHub blocks workflow-triggered pushes authenticated with the default `GITHUB_TOKEN` from triggering downstream workflows, and we'd rather skip the PAT-rotation burden.

To enable NuGet.org publishing, add a `NUGET_API_KEY` secret in Settings → Secrets and variables → Actions. Without it, the package is still built on every run and uploaded as a workflow artifact (downloadable from the run page) — useful for forks and during initial setup.

#### What ends up in the published artifact

NuGet's package filename uses only the SemVer 2 `MAJOR.MINOR.PATCH` segment — so `1.0.42+cs.10673343.dump.2026-05-20` is filed as `CS2OpenDev.Sdk.1.0.42.nupkg`. The build metadata is preserved inside the package's `AssemblyInformationalVersion` (visible via reflection or `dotnet --info`-style inspection), so consumers can recover which CS2 build their installed SDK was generated from without unpacking the nupkg.

### Build commands

| Task | Command |
| --- | --- |
| Restore + build everything | `dotnet build` |
| Build the generated SDK only | `dotnet build src/CS2OpenDev.Sdk` |
| Run the test suite | `dotnet run --project test/CS2OpenDev.Sdk.Generator.Tests/` |
| Run one test | `dotnet run --project test/CS2OpenDev.Sdk.Generator.Tests/ -- --treenode-filter "/*/*/*/TestName"` |
| Regenerate the SDK | `dotnet run --project src/CS2OpenDev.Sdk.Exporter` |

The repo uses [TUnit](https://github.com/thomhurst/TUnit) on the Microsoft Testing Platform; invoke tests with `dotnet run`, not `dotnet test`.

### Diagnostics emitted by the regen pipeline

| ID | Severity | Meaning |
|---|---|---|
| `CS2_GEN_001` | Error | `cs2_schema.json` failed to parse. The Exporter exits with status 1. |
| `CS2_GEN_002` | Warning | Both `output/schemas.json` and a repo-root `schemas.json` exist (legacy local-cache paths); the resolver chose the dump-layout copy. The new upstream submodule path is preferred when present. |
| `CS2_GEN_003` | Info | A schema atomic type fell through every classification branch in `TypeMapper`. It was emitted as an empty stub class; adding it to the right set in `TypeMapper.cs` gives it a real C# projection. |
| `CS2_GEN_004` | Error | `schema_format_version` declares a major this generator does not read. Regen stops rather than failing later on whichever shape change breaks first. |
| `CS2_GEN_005` | Error | `game-event-overrides.json` is malformed or names an event/field that does not exist. Stops rather than silently emitting the built-in projection the override asked to replace. |
| `CS2_GEN_006` | Info | A run-together lowercase word `WordSplitter` could not segment, so it was emitted unsegmented (`Somenewcompound`). Add its parts to the vocabulary if it is a compound, or to `Atomic` if it is one word. |
| `CS2_GEN_007` | Error | `game-event-supplement.json` is malformed, names an event twice, or claims a `source` it did not come from. Stops rather than emitting an SDK that silently omits the events it was asked to add. |
| `CS2_GEN_008` | Error | The supplement declares an event the extracted schema now declares too. Not a mistake — upstream caught up, and the supplement entry must be deleted so the real declaration takes over. |
| `CS2_GEN_009` | Warning | A run pinned in `names.lock.json` to a spelling the current vocabulary no longer agrees with. The lock wins and the emitted name does not move — this reports a divergence, never a change. It exists because a locked run short-circuits before segmentation, which made `CS2_GEN_006` structurally blind to anything already shipped. Clear it by rebaselining (an API break) or by adding the run to `Atomic` if the pinned spelling was right. |

---

## Working on the generator

If you're changing how types are named, mapped, or emitted, the architecture lives in `src/CS2OpenDev.Sdk.Generator/`:

- `Emitters/ModuleEmitter.cs` — the choke point. `EmitAll(IGeneratorSink, SchemaRoot, ns)` orchestrates classification (per-module vs `Common`), conflict propagation, per-class file emission, stub collection, and `SchemaNames` emission.
- `Emitters/ClassEmitter.cs` / `EnumEmitter.cs` / `SyntheticTypes.cs` — produce one type's source. They append to a `StringBuilder` and return it; `ModuleEmitter` hands the result to the sink.
- `Emitters/TypeMapper.cs` — C++ atomic type → C# type mapping. Adding a new atomic classification branch in `MapAtomicCore` must be mirrored in `IsKnownAtomicName` and (if it surfaces an inner type) `AtomicProjectionUsesInner` / `AtomicProjectionUsesInner2`.
- `Emitters/NameHelpers.cs` — single source of truth for identifier/filename normalisation (Hungarian-prefix stripping, suffix handling, sanitisation). Every `To*` entry point ends by handing its result to `WordSplitter`, so a name cannot reach the output having skipped that pass.
- `Emitters/WordSplitter.cs` — splits run-together lowercase words (`userid` → `UserId`) against a curated vocabulary, and folds a lone `ID` run to `Id`. Editing the vocabulary is safe: `names.lock.json` pins already-resolved names, so a new word only affects names nobody has shipped. Read the class comment before adding one — the `Atomic` list next to it exists because a word list that decides splits produces `Ass|Is|Ter`. A vocabulary edit that disagrees with a *locked* name does not change it, but is reported as `CS2_GEN_009` so the divergence is visible rather than silent.
- `IGeneratorSink.cs` — output abstraction. The Exporter's `DiskSink` writes to the source tree; tests use a `CapturingSink` that retains output in memory.

Workflow: edit the relevant emitter, run the test suite, regenerate, review the SDK diff. The Exporter doesn't carry a parallel naming path — there's exactly one source of truth.

---

## License

MIT — see [`LICENSE`](LICENSE).
