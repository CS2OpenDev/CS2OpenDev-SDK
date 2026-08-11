# Migrating to CS2OpenDev.Sdk 4.0

4.0 renames **ten generated identifiers**. That is the only breaking change — no
type moved namespace, none was added or removed, no signature or projected type
changed, and the schema pin is the same as 3.1's. If your code compiles after the
renames, it behaves exactly as it did.

Regenerate the table below with:

```
python3 scripts/rename-diff.py OLD_SDK_DIR NEW_SDK_DIR --markdown
```

## The renames

| Native name | Was | Now | Where |
|---|---|---|---|
| `isbot` | `Isbot` | `IsBot` | `PlayerTeamEvent`, `PlayerTeamCoreEvent` |
| `noreplay` | `Noreplay` | `NoReplay` | `PlayerDeathEvent` |
| `damagebits` | `Damagebits` | `DamageBits` | `EntityKilledEvent` |
| `totalrewards` | `Totalrewards` | `TotalRewards` | `TournamentRewardEvent` |
| `weapon_fauxitemid` | `WeaponFauxitemid` | `WeaponFauxItemId` | `PlayerDeathEvent`, `OtherDeathEvent` |
| `hcontent` | `Hcontent` | `HContent` | `UgcFileDownloadStartEvent`, `UgcFileDownloadFinishedEvent` |
| `botid` | `Botid` | `BotId` | `BotTakeoverEvent` |
| `HBOX` | `Hbox` | `HBox` | `GraphCanvasChildLayoutAlgorithm` |
| `HBOX_REVERSE` | `HboxReverse` | `HBoxReverse` | `GraphCanvasChildLayoutAlgorithm` |
| `FIELD_HMODEL` | `FieldHmodel` | `FieldHModel` | `FieldType` |

The matching `SchemaEvents` constants move with them (`SchemaEvents.PlayerTeamEvent.IsBot`
and so on), as do the property assignments in `GameEventFactories`. Native names
are unchanged — every `[NativeName]` value, every wire key, every dictionary key
in the registry is exactly what it was. A rename map keyed on native name is
therefore sufficient, and nothing that reads the wire needs to change.

## Why

Six of these were [reported by a downstream consumer](https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/2)
after adopting 3.0.3. The 3.0 casing pass split run-together lowercase names
against a curated vocabulary — `attackerinair` → `AttackerInAir` — but these
compounds were built from words the vocabulary did not have. `faux` is the
clearest case: `WeaponFauxitemid` sat on `PlayerDeathEvent` directly beside
`WeaponItemId`, which did split, so the same record showed the rule working and
not working two properties apart.

4.0 adds the six missing words (`bot`, `replay`, `bits`, `rewards`, `faux`, `h`)
and the four guards that widening costs (`both`, `hash`, `hold`, `hover`, which
`h` would otherwise cut into `BotH`, `HasH`, `HOld` and `HOver`).

The other four were not reported. `botid` came out of the same vocabulary
addition. `HBOX`, `HBOX_REVERSE` and `FIELD_HMODEL` are the same single-letter
handle prefix as `hcontent`, and they were fixed in the same major deliberately:
shipping `HContent` while leaving `Hbox` alone would have meant doing this again.

## Why they were not caught here

Worth stating plainly, because the answer is not "nobody looked".

The generator has a diagnostic, `CS2_GEN_006`, whose entire job is to report
compounds the vocabulary cannot segment. It reported **zero** while all six of
these were shipping, for three independent reasons:

1. It was drained inside the *class* emitter, which runs before the game-event
   emitter. Every game-event name — the flat KV1 vocabulary the splitter was
   written for — landed in the bucket after the last read.
2. Its near-miss filter ignored runs of six characters or fewer, and `isbot` is
   five.
3. `names.lock.json` pins resolved spellings and short-circuits before
   segmentation, so once a name shipped, the vocabulary was never consulted for
   it again and the diagnostic could not see it by construction.

All three are fixed in 4.0. The third is the one that mattered most and is now a
separate diagnostic, `CS2_GEN_009`: it audits *locked* names against the current
vocabulary and reports disagreement without changing any output. On the commit
that added the six words it named all ten of these renames unprompted, which is
what a working report looks like.

`scripts/rename-diff.py` had a matching blind spot — it required `public ` before
the identifier, so it never matched a bare enum member and reported 7 of these 10.
Fixed in 3.1; its coverage went from 11,539 members to 15,920.

That one was not free. **`MIGRATION-3.0.md` was generated with the blind version
and shipped listing 574 of 3.0's 1,108 renames.** The 534 it omitted are all enum
members and are now published as
[`MIGRATION-3.0-enum-members.md`](MIGRATION-3.0-enum-members.md), with a correction
note on the original. If you migrated to 3.x from that document alone, read the
appendix — this 4.0 upgrade is ten renames, but that one was not what it said.

## Upgrading

Ten renames, all mechanical. Find-and-replace on the C# names is safe: every one
of the old spellings is unique to the member it names.

If you keep a rename map keyed on native name, the six event-property entries are
the only ones that can appear in a decode path. The three enum members and
`BotId` are compile-time only — a `switch` on `GraphCanvasChildLayoutAlgorithm`
or `FieldType` will fail to build rather than misbehave.

Nothing else moved. `SchemaNames`, every namespace, every projected type, and the
schema revision are identical to 3.1.
