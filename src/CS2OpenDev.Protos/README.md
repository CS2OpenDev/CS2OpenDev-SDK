# CS2OpenDev.Protos

Generated C# protobuf message types for Counter-Strike 2's demo and engine wire protocol.

The descriptors are recovered per-build from the shipped CS2 game binaries by
[CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker) — not scraped
from a third-party mirror — so every release names the exact Steam build it came from.

```csharp
using CS2OpenSchema.Protos;

var packet = CDemoPacket.Parser.ParseFrom(payload);
var ev     = CMsgSource1LegacyGameEvent.Parser.ParseFrom(userMessage);
```

## What's in it

The demo/engine wire path: demo packets, net messages, entity updates, user messages, usercmds,
game events, temp entities. 18 `.proto` files, ~240,000 lines of generated C#.

| | |
|---|---|
| Namespace | `CS2OpenSchema.Protos` |
| Target | `net8.0` |
| Dependencies | `Google.Protobuf` only |
| Licence | MIT |

Proto **short names** and `.proto` **filenames** are preserved exactly as Valve ships them. Only the
C# namespace is added. Tools that match on message short names or descriptor filenames keep
working — including SchemaTracker's own `network_messages.json` and `demo_messages.json`, which join
wire-IDs to message types by short name.

## Which CS2 build?

Every assembly carries it, readable without unpacking anything:

```csharp
typeof(CDemoPacket).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .First(a => a.Key == "CS2BuildId").Value;     // "24537688"
```

`CS2BuildId`, `CS2Platform` and `CS2SchemaTrackerCommit` are stamped as `AssemblyMetadata`, and the
package version carries `+cs.build.{id}` as SemVer 2 build metadata. The metadata attributes are the
reliable channel — build metadata is not part of a NuGet package's identity, never appears in the
`.nupkg` filename, and feeds are inconsistent about preserving it.

The same facts are in `protos/PROVENANCE.json` in the repository, alongside the `.proto` sources.

## The curated subset, and why it is curated

**The full 40-file descriptor set does not compile as one assembly.** This is not a packaging
choice; it is a property of Valve's descriptors. Two independent symbol collisions exist:

```
enums_clientserver.proto:528:3: "k_EMsgGCSystemMessage" is already defined in file
    "base_gcmessages.proto".
    Note that enum values use C++ scoping rules, meaning that enum values are
    siblings of their type, not children of it.
```

| Symbol | Defined in | And in |
|---|---|---|
| `k_EMsgGCSystemMessage` | `base_gcmessages.proto:6` (= 4001) | `enums_clientserver.proto:528` (= 2213) |
| `CMsgProtoBufHeader` | `steammessages.proto:13` | `steammessages_base.proto:76` |

proto2 enum values are siblings of their type, so they must be globally unique — and none of these
files declares a `package`, so "global" means the whole assembly.

So the package ships a **collision-free, import-closed subset**: the transitive import closure over
the demo/engine roots. It is verified by compiling from a directory containing only those files,
because protoc resolves imports against anything on its include path and a subset that appears to
work inside the full directory may not actually be closed.

<details>
<summary>The 18 files</summary>

```
clientmessages          cs_gameevents           cs_usercmd
cstrike15_gcmessages    cstrike15_usermessages  demo
engine_gcmessages       gameevents              gcsdk_gcmessages
netmessages             network_connection      networkbasetypes
source2_steam_stats     steammessages           te
usercmd                 usermessages            valveextensions
```

Four of these are Steam GC / matchmaking families reachable only through
`cstrike15_usermessages.proto` → `cstrike15_gcmessages.proto`. They are not on the demo wire path
and are present only to satisfy that import. Pruning `cstrike15_gcmessages` to the 17-type closure
its 6 referenced types actually need would drop `steammessages`, `engine_gcmessages` and
`gcsdk_gcmessages` entirely — about 32% of the generated C# — without moving a descriptor byte on
anything a demo parser touches. That prune is [an open upstream
request](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker); it belongs in the tool that
materialises the descriptors, not here, so that it is re-derived on every CS2 build rather than
hand-maintained and silently reverted by the next refresh.

</details>

**Broader coverage will not be one bigger package.** The Steam and GC families sit in the collision
domains above, so covering them means additional packages split along those domains — never one
assembly.

## `Google.Protobuf` floor policy

The package exposes generated `IMessage` types on its public surface, so the `Google.Protobuf`
version is an **ABI commitment**, not an implementation detail: every consumer is bound to a
compatible major, and raising the floor is a breaking change for anyone pinned below it.

The floor is therefore deliberately conservative and moves rarely:

- **Floor: `Google.Protobuf` >= 3.27.0.** Any 3.x at or above the floor works; NuGet resolves the
  highest compatible version in your graph.
- The floor is raised **only** for a security fix or a generated-code requirement we cannot work
  around. Never for convenience, and never to track latest.
- A floor bump ships as a **minor version** bump of this package, called out in the release notes.
- A `Google.Protobuf` **major** bump (4.x) would ship as a new major of this package.

`Grpc.Tools` is build-only — it supplies protoc and the MSBuild integration, is marked
`PrivateAssets="all"`, and never appears in a consumer's dependency graph. It tracks latest.

## Versioning

Versioned on the **protobuf clock**, independently of `CS2OpenDev.Sdk`.

The schema dump and the protobuf descriptors move separately: most CS2 patches change the schema
without touching a single `.proto`. If this package rode the SDK's version stream, every schema
regen would push a new version, a diff review and a CI run for a byte-identical assembly. Its
`version.json` matches only `protos/`, `src/CS2OpenDev.Protos/` and the normalisation script, so the
patch version moves when — and only when — the descriptors do.

## Regenerating

```sh
git submodule update --init --depth 1 schema-tracker
python3 scripts/normalize-protos.py          # stage protos/ from the submodule
python3 scripts/normalize-protos.py --check  # verify protos/ is current (CI runs this)
```

The `.proto` sources are committed rather than fetched at build time, so a change to Valve's wire
format shows up as a reviewable diff on the CS2 build that introduced it. The generated C# is not
committed — protoc produces it at build time from the committed sources.

## Related

- [`CS2OpenDev.Sdk`](https://www.nuget.org/packages/CS2OpenDev.Sdk) — strongly-typed schema classes,
  enums and game-event records. Zero dependencies.
- [`CS2OpenDev.Sdk.GameEvents`](https://www.nuget.org/packages/CS2OpenDev.Sdk.GameEvents) — decodes
  `CMsgSource1LegacyGameEvent` into those typed records. Depends on this package.
