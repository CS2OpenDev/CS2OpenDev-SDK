# CS2OpenDev.Protos

Generated C# protobuf message types for Counter-Strike 2's demo and engine wire protocol.

The descriptors are recovered per-build from the shipped CS2 game binaries by
[CS2OpenDev-SchemaTracker](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker), not scraped
from a third-party mirror, so every release names the exact Steam build it came from.

```csharp
using CS2OpenSchema.Protos;

var packet = CDemoPacket.Parser.ParseFrom(payload);
var ev     = CMsgSource1LegacyGameEvent.Parser.ParseFrom(userMessage);
```

## What's in it

The demo/engine wire path: demo packets, net messages, entity updates, user messages, usercmds,
game events, temp entities. 18 `.proto` files, ~240,000 lines of generated C#. Namespace
`CS2OpenSchema.Protos`, target `net8.0`, `Google.Protobuf` as the only dependency, MIT licence.

Proto short names and `.proto` filenames are preserved exactly as Valve ships them; only the C#
namespace is added. Tools that match on message short names or descriptor filenames keep working,
including SchemaTracker's own `network_messages.json` and `demo_messages.json`, which join
wire-IDs to message types by short name.

## Where to get it

GitHub Packages only. The release job pushes to
`https://nuget.pkg.github.com/CS2OpenDev/index.json` and attaches the `.nupkg` and `.snupkg` to the
GitHub release. Each release's notes name the feeds that version actually reached, so this is
checkable rather than something you have to take on trust.

Two ways to consume it: a `nuget.config` source pointing at that feed, or the `.nupkg` off the
release page into a local folder source. GitHub Packages requires an authenticated token even for
public packages, which is reason enough for some consumers to prefer the second.

An earlier revision of this page called NuGet.org "a gap rather than a decision — a credential, not
a design question." That is no longer true in either half: it *is* a decision, and it has been
made. This project publishes to GitHub Packages only.

The cost that paragraph correctly identified still stands, now as a deliberate cost rather than an
accident: a package on NuGet.org cannot declare a dependency on one that is not there, so this
choice blocks downstream *publishers*, not just convenience. If you need to publish a NuGet.org
package that depends on these types, the options are to vendor the generated types, to keep your
dependency on them private, or to republish under an ID you own. Discussion in
#5.

## Which CS2 build?

Every assembly carries it, readable without unpacking anything:

```csharp
typeof(CDemoPacket).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .First(a => a.Key == "CS2BuildId").Value;     // "24537688"
```

`CS2BuildId`, `CS2Platform` and `CS2SchemaTrackerCommit` are stamped as `AssemblyMetadata`, and the
package version carries `+cs.build.{id}` as SemVer 2 build metadata. The metadata attributes are
the reliable channel: build metadata is not part of a NuGet package's identity, never appears in
the `.nupkg` filename, and feeds are inconsistent about preserving it.

The same facts are in `protos/PROVENANCE.json` in the repository, alongside the `.proto` sources.

## The curated subset

The full 40-file descriptor set does not compile as one assembly. That is a property of Valve's
descriptors, not a packaging choice on our part. Two independent symbol collisions exist:

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

proto2 enum values are siblings of their type, so they must be globally unique, and none of these
files declares a `package`, so "global" means the whole assembly.

So the package ships a collision-free, import-closed subset: the transitive import closure over
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
`gcsdk_gcmessages` entirely (about 32% of the generated C#) without moving a descriptor byte on
anything a demo parser touches. That prune is [an open upstream
request](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker); it belongs in the tool that
materialises the descriptors, not here, so that it is re-derived on every CS2 build rather than
hand-maintained and silently reverted by the next refresh.

</details>

Broader coverage will not be one bigger package. The Steam and GC families sit in the collision
domains above, so covering them means additional packages split along those domains — never one
assembly.

## The change of source, measured

These descriptors used to come from Valve's published GameTracking-CS2 tree and are now recovered
from the shipped binaries. That is the kind of change a compile cannot check for you: the compiler
proves that names resolve, and a field whose number, declared type or label differs still resolves,
still compiles, and then misparses in silence.

Cs2DemoKit / DemoViewer.NET diffed the two sources field-by-field before trusting the swap, and
reported the result to us (#4). It is their
measurement, not ours. 2,753 fields are present in both sources, and zero of them differ in field
number, declared type or label. Separately, 47 fields dropped and 50 added; the drops are GC /
close-caption cruft and `descriptor.proto` internals, none of which their parser reads.

The two figures are complementary bookkeeping. "Present in both" is keyed on name, so a rename
lands as one drop plus one add rather than as a difference; the counts are where renames and
genuine additions and removals show up. The first number is the load-bearing one because of the
asymmetry: a field that disappeared out from under you is a compile error, a field you never knew
about is inert, and a field that quietly changed number or type is neither. That last case is the
one that was measured, and it is empty.

One scope caveat: the comparison covered the 13 `.proto` files that project compiles, not all 18
shipped here. Our closure adds `clientmessages`, `valveextensions` and the three GC-chain files
(`steammessages`, `engine_gcmessages`, `gcsdk_gcmessages`), and those five were not part of it.
Their full parser suite and analysis suite stayed green across the swap, and their accuracy suite
ran 5/5 with no demo regressing against its recorded baseline.

It measures that one transition, once. The descriptors are re-derived on every CS2 build, so each
build is still its own diff — which is why the `.proto` sources are committed here rather than
fetched at build time.

## `Google.Protobuf` floor policy

The package exposes generated `IMessage` types on its public surface, so the `Google.Protobuf`
version is an ABI commitment rather than an implementation detail: every consumer is bound to a
compatible major, and raising the floor is a breaking change for anyone pinned below it.

The floor is therefore deliberately conservative and moves rarely. It is `Google.Protobuf` >=
3.27.0; any 3.x at or above the floor works, and NuGet resolves the highest compatible version in
your graph. The floor is raised only for a security fix or a generated-code requirement we cannot
work around — never for convenience, and never to track latest. A floor bump ships as a minor
version bump of this package, called out in the release notes. A `Google.Protobuf` major bump
(4.x) would ship as a new major of this package.

`Grpc.Tools` is build-only. It supplies protoc and the MSBuild integration, is marked
`PrivateAssets="all"`, and never appears in a consumer's dependency graph. It tracks latest.

## Versioning

Patch versions ride the protobuf clock; the major stays in step with `CS2OpenDev.Sdk`.

The schema dump and the protobuf descriptors move separately: most CS2 patches change the schema
without touching a single `.proto`. If this package rode the SDK's version stream, every schema
regen would push a new version, a diff review and a CI run for a byte-identical assembly. Its
`version.json` matches only `protos/`, `src/CS2OpenDev.Protos/` and the normalisation script, so
the patch version moves when, and only when, the descriptors do.

The major is the exception, and it is deliberate. It tracks `CS2OpenDev.Sdk` so the three packages
that ship from this repo read as one product; three independent majors on a feed invites pairing
`CS2OpenDev.Protos` 1.x with `CS2OpenDev.Sdk` 1.x, which was never a real correspondence. The cost
is that a major here does not by itself mean the descriptors broke. It also moves when the SDK's
schema projection breaks and this package follows to stay in step. `protos/PROVENANCE.json` plus
the `CS2BuildId` assembly attribute are what actually identify the descriptors.

The converse does not hold, and 4.1 is the case that proves it: the descriptors can break on their
own. The staged set is a closure derived upstream, so types can leave this package while
`cs2_schema.json` — and therefore the SDK's API — does not move at all. 188 Game-Coordinator types
went that way at CS2 24701871 ([migration guide](../../docs/MIGRATION-4.1-protos.md)). A removal
like that takes a major of its own; only additive descriptor changes land in the patch.

## Regenerating

```sh
git submodule update --init --depth 1 schema-tracker
python3 scripts/normalize-protos.py          # stage protos/ from the submodule
python3 scripts/normalize-protos.py --check  # verify protos/ is current (CI runs this)
```

The `.proto` sources are committed rather than fetched at build time, so a change to Valve's wire
format shows up as a reviewable diff on the CS2 build that introduced it. The generated C# is not
committed; protoc produces it at build time from the committed sources.

## Related

Both ship from the same repository and the same feeds as this package: GitHub Packages and the
GitHub release page, not NuGet.org.

- [`CS2OpenDev.Sdk`](https://github.com/CS2OpenDev/CS2OpenDev-SDK): strongly-typed schema classes,
  enums and game-event records. Zero dependencies.
- [`CS2OpenDev.Sdk.GameEvents`](https://github.com/CS2OpenDev/CS2OpenDev-SDK/tree/main/src/CS2OpenDev.Sdk.GameEvents):
  decodes `CMsgSource1LegacyGameEvent` into those typed records. Depends on this package.
