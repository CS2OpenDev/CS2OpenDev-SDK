# Migrating to CS2OpenDev.Protos 0.10

`CS2OpenDev.Protos` goes 0.9.6 → 0.10.0. **One field is renamed and changes cardinality**, in
`CEconItemPreviewDataBlock`. Everything else in this restage is additive.

Measured against CS2 build 25966348 (from 25218825).

## `CEconItemPreviewDataBlock.customname` becomes `customnames`

In `cstrike15_gcmessages.proto`, field 11 of `CEconItemPreviewDataBlock`:

| | Was | Now |
|---|---|---|
| Name | `customname` | `customnames` |
| Cardinality | `optional` | `repeated` |
| Field number | 11 | 11 |

The field number is unchanged, which is why no gate caught this. `scripts/proto_surface.py`
fails on a lost top-level type or a lost field number, and neither happened: field 11 is still
there, under a new name and a new cardinality. A rename at a stable tag and a cardinality change
at a stable tag are both outside what that gate can see, which `docs/` has said since the 3.0.7
repair.

In the generated C# this is two breaks at once:

```csharp
// 0.9.6
string Customname { get; set; }     // plus HasCustomname / ClearCustomname
// 0.10.0
RepeatedField<string> Customnames { get; }   // read-only, no Has/Clear
```

So a consumer reading an item's name tag has to change the member it reads, stop assigning to it,
and decide what more than one name means. On the wire a `repeated string` at tag 11 is
length-delimited exactly as the `optional string` was, so a single-element list is what a
previously-valid message decodes to. Reading `Customnames.FirstOrDefault()` reproduces the old
value for every message that had one, and `Count == 0` is the old "not set".

Valve did not document why the field became repeated, and this package does not guess.

**Pinning `0.9.6` hides this rather than avoiding it.** `optional` and `repeated` are wire
compatible in both directions at tag 11, so a pinned consumer keeps decoding without error, and
protobuf resolves repeated occurrences of a non-repeated field by taking the last one. If a
message ever carries two names, `0.9.6` reports only the second and reports no problem. Verified
with `protoc` 33.2: `customnames: "first"` plus `customnames: "second"` decodes under the old
schema as `customname: "second"`. Pin only if you accept that silent truncation.

## Everything else

Additive, and listed because a restage is easier to trust when the whole diff is accounted for:

- `CMsgGCCStrike15_v2_MatchmakingGC2ServerReserve` gains `repeated string clan_tags = 23`.
- `CEconItemPreviewDataBlock` also gains `optional uint32 pet_food_expiration_date = 24` and
  `optional bytes blobdata = 25`.
- `source2_steam_stats.proto` gains 20 fields: the `netframes_size_uncompressed_*`,
  `netframes_msgs_*`, `msgproc_usec_*`, `msgproc_usec_avg_*` and `queuedmsgs_*` percentile
  families, all `optional uint32`.

The kept closure moved from 17 of 162 top-level types to 17 of 157: upstream's own file shrank by
five types, none of them in the demo/engine subset this package stages. The same three imports
are still removed (`steammessages.proto`, `engine_gcmessages.proto`, `gcsdk_gcmessages.proto`),
and the 15-file subset still compiles in isolation.

That is the complete accounting: 1 field renamed and widened, 23 fields added, 0 top-level types
added or removed.
