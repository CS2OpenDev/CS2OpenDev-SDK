# Schema formats and where they come from

The SDK is generated from `cs2_schema.json` in the `upstream` submodule
(CS2OpenDev-Docs). Docs vends the enriched downstream schemas (class schema, game
events, convars, commands, well-known constants, field history) with the community
annotation overlay applied on top. The walker behind those artifacts is
CS2OpenDev-SchemaTracker, which this repo also pins, but only for protos:
SchemaTracker has the real `.proto` files and a prebuilt `protos.descriptorset`,
which Docs publishes only as markdown. We do not read schema JSON from SchemaTracker
directly. That would be a second pin to keep in sync with the first, for data Docs
already enriches.

## Format 2.0

`schema_format_version` went 1.1 to 2.0 in August 2026, when Docs dropped the old
GameTracking-CS2/SchemaExplorer chain and started passing SchemaTracker's
`entity_schema.json` shape through nearly unchanged. The generator reads 2.x only.
Anything else is rejected up front with CS2_GEN_004, before any field parse runs;
without that check a shape mismatch surfaces as whatever happens to break first,
which for the 2.0 cutover was an unhelpful "requires an element of type 'Number',
but the target element has type 'String'" out of the field-offset parse.

What moved between the formats:

| v1.1 | v2.0 | Note |
|---|---|---|
| `category: "builtin"` | `category: "BUILTIN"` | all seven discriminators uppercased |
| `size: 12`, `offset: 0`, `count: 10` | `"56"`, `"0"`, `"10"` | numerics are JSON strings throughout |
| `count` present on `fixed_array` only | present on every type node (`"0"` when N/A) | |
| `module` = project | `module` = binary; `projectName` = project | the namespace key moved |
| type node had no `name` for composites | `name: "CPoseHandle[10]"` on fixed arrays | ignore; `inner` + `count` still authoritative |
| `parents` absent when empty | always present | |
| `metadata` absent when empty | always present | |
| — | `cppName`, `flags`, `flags2`, `staticFields`, `singleInheritanceDepth`, `multipleInheritanceDepth` | new; `flags` bit 1 is `abstract` |
| — | `typeModule` on fields | new |
| enum: `alignment` only | enum: `alignment`, `flags` (int bitfield), `size` (int) | |

`metadata[].value_parsed` survived the change, so the KV3-defaults recovery path is
unaffected.

The header changed too. `revision` is now a walker-identity string (it names the
reader, not the game build, and contains `/`, so it cannot be a SemVer 2
build-metadata identifier). The provenance key is `build_id`, the Steam build
number, added in Docs#19. `read-schema-metadata` reads `build_id` first, accepts a
numeric `revision` as fallback for old artifacts, and fails closed on anything else.

Two problems only showed up against the real artifact; both are handled in the
generator now, but they explain code that otherwise looks paranoid:

- 2.0 module names are binaries, and one of them is `!GlobalTypes`. It passed
  through `ToNamespacePart` verbatim and emitted
  `namespace CS2OpenSchema.!GlobalTypes;` across 591 files. A syntax error, not a
  bad name. Identifier-illegal characters are dropped now.
- 2.0 has a `TagStatus` class whose first field is `m_TagStatus`, which projects to
  a property named after its own class; C# forbids that (CS0542). Colliding members
  take a `Value` suffix, and `[NativeName]` still carries the wire name.

## Fields that are not coming back

`EnumModel.IsFlags` cannot be populated from 2.0, and this is settled, not pending.
[SchemaTracker#2](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/2)
confirmed `SchemaEnumFlags_t` declares exactly three bits: `IS_REGISTERED` (set on
every enum in the artifact, by definition), `MODULE_LOCAL_TYPE_SCOPE`, and
`GLOBAL_TYPE_SCOPE`. None marks a flag enum. Our best statistical candidate, bit 16,
was noise: 17 hits against 8 false positives and 14 false negatives. Don't
rediscover it.

`flags2` is fully opaque (`SchemaClassFlags2_t` is an empty enum). Atomic records
still carry no `handle_kind`, so the per-atomic-name lookup table in `HandleTypes`
remains the mechanism for handle projection.

## Why enum attribution was never synthesized locally

For two days in August 2026, 2.0 enum records carried no `projectName`, which would
have collapsed 591 of 610 enums into one namespace. The tempting local fix was to
infer each enum's project from the classes that reference it. We measured it first:
194 of 610 enums are referenced by no class at all, and winner-takes-most on the
rest scores 69.9% against the 1.x oracle, so roughly 120 enums would have been
silently misfiled. A frozen lookup table built from the pinned 1.1 artifact is the
same problem with better provenance: 530 of 610 covered, and it rots the moment
upstream adds an enum. Neither beats waiting, and upstream shipped the field
([SchemaTracker#1](https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/1))
on 2026-08-09.

The general rule stands: attribution that isn't in the artifact is not recoverable
here, and we don't guess it.

## The release gate

`scripts/check-migration-readiness.py` fails while any enum record lacks its
namespace key. It runs in two places because there are two ways to reach a publish:
`check-upstream.yml` between the submodule bump and the regen (the scheduled path),
and `_pack-and-publish.yml` before pack (the release path, which `release.yml` and
the cron both publish through). `CS2OpenDev.Protos` is exempt; it builds from
`protos/` on its own clock and enum attribution does not touch it.

The gate checks the actual blocker rather than a proxy like the format version, and
it clears itself when the upstream data is complete. No code change needed to
unblock.
