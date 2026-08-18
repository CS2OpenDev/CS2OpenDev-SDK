#!/usr/bin/env python3
"""Draft a Schema Lens migration from SchemaTracker's rename evidence.

When Valve renames a field the Lens tracks, CS2_GEN_010 fires and the build
fails. The diagnostic says what broke; it does not say what to write, because
choosing between `rename`, `removeField` and a `module` pin is the judgement the
Lens exists to record. The evidence for that choice already exists in
SchemaTracker's `schema_evolution/<platform>.json` — nothing joins it to the
failure, so today the answer means hand-searching a 60 MB artifact across 380
transitions while CI is red and the 4-hourly cron keeps pushing.

This script does the search. It finds where the field left its class, ranks the
candidate successors by signal strength, cross-checks the two whole-move
surfaces, and writes a migration file with the op filled in and the evidence in
`notes`.

It proposes; it never decides. SchemaTracker publishes these candidates
deliberately unselected and N:M -- picking one among tied candidates is an
inference, not a fact, and 58% of the corpus sits in the weak
`sizeMatch + typeMatch` tier where ties are normal. A `rename` op is an
empirical claim that can be wrong, so the draft is meant to be read, checked
against the alternatives it prints, and edited or thrown away.

The emitted file carries `"stateHash": "sha256:PLACEHOLDER"`. That is the
documented authoring flow: the exporter computes the real hash, prints it under
CS2_GEN_014 and exits 1, and you paste it in. A placeholder never survives into
a green build.

Usage:
    # what happened to a field that stopped resolving
    python3 scripts/lens-rename-candidates.py CBaseCSGrenadeProjectile m_bHasEverHitPlayer

    # write the draft instead of just printing it
    python3 scripts/lens-rename-candidates.py CCSPlayerPawn m_iHealth --write

    # a class that vanished entirely (module move, or genuinely gone)
    python3 scripts/lens-rename-candidates.py CCSPlayerController --class-only

    # you decided which successor is right; draft that one
    python3 scripts/lens-rename-candidates.py CCSPlayerPawn m_flLandseconds \
        --to m_flLandingTimeSeconds --write

Options:
    --platform {windows-x86_64,linux-x86_64}   default windows-x86_64
    --artifact PATH   override the evolution artifact location
    --write           write schema-lens/NNNN-<stamp>-<slug>.json
    --all             search the whole corpus, not just Lens-covered classes
    --to NAME         you pick the successor; required when the top tier ties

When it refuses to draft:

    * two or more candidates tie at the top signal tier, or
    * the only candidate lacks `offsetExact`

Both print the ranked list and stop. Offset is what makes a candidate
near-unique; without it, or with a tie, any pick is arbitrary rather than
evidenced. Decide by hand and pass `--to`. The refusal is deliberate:
m_flLandseconds has 18 tied candidates, and the one a human would choose
(m_flLandingTimeSeconds) is not the first the artifact happens to list.
"""

import argparse
import datetime as dt
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LENS_DIR = os.path.join(REPO, "schema-lens")
DEFAULT_ARTIFACT = os.path.join(
    REPO, "schema-tracker", "artifacts", "schema_evolution", "{platform}.json"
)

# Ranked strongest first. A candidate carrying all three is near-certain; the
# two-signal `offsetExact + typeMatch` bar is what `pairedEvidence` froze at,
# and everything below it is where ties live. Ordering is the whole point of
# the tool -- an unranked candidate list is what the artifact already gives you.
SIGNAL_TIERS = [
    (("offsetExact", "sizeMatch", "typeMatch"), "near-certain"),
    (("offsetExact", "typeMatch"), "strong (the frozen pairedEvidence bar)"),
    (("offsetExact", "sizeMatch"), "moderate — offset held, type did not"),
    (("offsetExact",), "moderate — offset alone"),
    (("sizeMatch", "typeMatch"), "weak — ties are common in this tier"),
    (("typeMatch",), "very weak — type alone pairs almost anything"),
]


# Below this tier index a candidate lacks `offsetExact`, and offset is the
# signal that makes a candidate near-unique. Without it ties are the norm, not
# the exception -- 1,343 of the corpus's 2,315 candidates sit in
# `sizeMatch + typeMatch` alone. The script will rank those but will not draft
# from them: turning a tied field into a single `rename` op is precisely the
# inference SchemaTracker refused to make when it published these unselected.
DRAFTABLE_TIER = 3


def tier(signals):
    """Rank a candidate. Returns (index, label); lower index is stronger."""
    key = tuple(sorted(signals))
    for i, (want, label) in enumerate(SIGNAL_TIERS):
        if key == tuple(sorted(want)):
            return i, label
    # Unknown combination -- a future signal this script has not been taught.
    # Sort it just below the strongest so it is seen rather than buried, and
    # say so, instead of silently scoring it as noise.
    return 0.5, "unrecognised signal set — check SchemaTracker's current vocabulary"


def bare(qualified):
    """`server.dll/CAK47` -> `CAK47`. Class keys are module-qualified."""
    return qualified.split("/")[-1]


def load_artifact(path):
    if not os.path.exists(path):
        sys.exit(
            f"No evolution artifact at {path}\n"
            "The schema-tracker submodule may not be initialised. Run:\n"
            "  git submodule update --init --depth 1 schema-tracker"
        )
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def load_lens_classes():
    """Class -> tracked canonical paths, from the committed state. {} if absent."""
    state = os.path.join(LENS_DIR, "state.json")
    if not os.path.exists(state):
        return {}
    with open(state, encoding="utf-8") as fh:
        data = json.load(fh)
    return {name: set(c.get("fields", {})) for name, c in data.get("classes", {}).items()}


def find_field_removals(artifact, class_name, field):
    """Every transition where `field` was REMOVEd from `class_name`.

    Returns the transitions in corpus order. More than one is possible and is
    not an error: a field can be removed, re-added and removed again, and the
    caller wants the most recent.
    """
    out = []
    for tr in artifact["transitions"]:
        for cd in tr.get("classChanged", []):
            if bare(cd["name"]) != class_name:
                continue
            for op in cd.get("fieldOps", []):
                if op.get("kind") == "REMOVE" and op.get("field") == field:
                    out.append((tr, cd, op))
    return out


def candidates_for(cd, field):
    """Ranked pairCandidates whose `from` is the vanished field."""
    hits = [c for c in cd.get("pairCandidates", []) if c.get("from") == field]
    return sorted(hits, key=lambda c: tier(c.get("signals", []))[0])


def field_moves_for(tr, class_name, field):
    """Cross-class moves of this field — the hoist / push-down case."""
    return [
        m
        for m in tr.get("fieldMoveCandidates", [])
        if m.get("field") == field and bare(m.get("fromClass", "")) == class_name
    ]


def class_moves_for(artifact, class_name):
    """Whole-class module moves. The remedy is a `module` pin, not a rename."""
    out = []
    for tr in artifact["transitions"]:
        for c in tr.get("classPairCandidates", []):
            if bare(c.get("from", "")) == class_name:
                out.append((tr, c))
    return out


def next_ordinal():
    """Next `NNNN-` prefix. Filename order is replay order, so it must not collide."""
    highest = -1
    if os.path.isdir(LENS_DIR):
        for name in os.listdir(LENS_DIR):
            m = re.match(r"^(\d{4})-", name)
            if m:
                highest = max(highest, int(m.group(1)))
    return highest + 1


def build_migration(ordinal, build, class_name, field, chosen, notes):
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    slug = re.sub(r"[^a-z0-9]+", "-", field.lower()).strip("-")[:24] or "rename"
    mid = f"{ordinal:04d}-{stamp}-{slug}"
    return mid, {
        "id": mid,
        "build": str(build),
        "appliedAt": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "notes": notes,
        # The exporter computes this, prints it under CS2_GEN_014 and exits 1.
        "stateHash": "sha256:PLACEHOLDER",
        "changes": [
            {"op": "rename", "class": class_name, "from": field, "to": chosen["to"]}
        ],
    }


def report_candidates(cands):
    for i, c in enumerate(cands):
        rank, label = tier(c.get("signals", []))
        marker = "->" if i == 0 else "  "
        print(f"  {marker} {c['to']}")
        print(f"       signals: {', '.join(sorted(c.get('signals', [])))}  [{label}]")


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("class_name")
    ap.add_argument("field", nargs="?")
    ap.add_argument("--platform", default="windows-x86_64",
                    choices=["windows-x86_64", "linux-x86_64"])
    ap.add_argument("--artifact")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--all", action="store_true",
                    help="search outside the Lens-covered class set")
    ap.add_argument("--class-only", action="store_true",
                    help="report whole-class moves only; no field argument needed")
    ap.add_argument("--to", metavar="NAME",
                    help="you decide the successor; draft from that instead of "
                         "the script's ranking. Required when the top tier ties.")
    args = ap.parse_args()

    if not args.field and not args.class_only:
        ap.error("give a field, or pass --class-only")

    path = args.artifact or DEFAULT_ARTIFACT.format(platform=args.platform)
    artifact = load_artifact(path)
    print(f"artifact: {os.path.relpath(path, REPO)}  "
          f"(schemaVersion {artifact.get('schemaVersion')}, "
          f"{len(artifact['transitions'])} transitions)")

    lens = load_lens_classes()
    if lens and args.class_name not in lens and not args.all:
        print(f"\n{args.class_name} is not a Lens-covered class.")
        print("The Lens gates only fire for covered classes; pass --all to search anyway.")
        return 1
    print(f"class:    {args.class_name}"
          + (f"  ({len(lens[args.class_name])} tracked fields)" if args.class_name in lens else ""))

    # Whole-class moves first: if the class itself moved module, no field-level
    # rename is the right remedy and the caller should stop here.
    moves = class_moves_for(artifact, args.class_name)
    if moves:
        print(f"\nWHOLE-CLASS MOVE — {len(moves)} candidate(s):")
        for tr, c in moves:
            print(f"  build {tr['fromBuild']} -> {tr['toBuild']}: "
                  f"{c['from']} => {c['to']}")
            print(f"       signals: {', '.join(sorted(c.get('signals', [])))}")
        print("  Remedy is a `module` pin on the class, not a field rename.")

    if args.class_only:
        if not moves:
            print("\nNo whole-class move candidates.")
        return 0

    removals = find_field_removals(artifact, args.class_name, args.field)
    if not removals:
        print(f"\nNo REMOVE op for {args.class_name}.{args.field} in this artifact.")
        print("The field may still exist (check the gate message), the class may be")
        print("keyed under a different module, or the change predates the corpus.")
        return 1

    # Most recent removal is the one a just-fired gate is about.
    tr, cd, _op = removals[-1]
    if len(removals) > 1:
        print(f"\nNote: {len(removals)} removals of this field in history; "
              "using the most recent.")
    print(f"\nremoved in: build {tr['fromBuild']} -> {tr['toBuild']}"
          f"  ({tr.get('toManifestCreatedUtc', 'no date')})")

    cands = candidates_for(cd, args.field)
    moves_f = field_moves_for(tr, args.class_name, args.field)

    if moves_f:
        print(f"\nMOVED TO ANOTHER CLASS — {len(moves_f)} candidate(s):")
        for m in moves_f:
            print(f"  {m['fromClass']} -> {m['toClass']}")
            print(f"       signals: {', '.join(sorted(m.get('signals', [])))}")
        print("  If this is the real story the remedy is `moveSubService`, not `rename`.")

    if not cands:
        print("\nNo in-class rename candidates.")
        print("An absence here is itself a signal: a field that genuinely died leaves")
        print("no successor, and `removeField` is then the honest migration.")
        return 0

    print(f"\nIN-CLASS RENAME CANDIDATES — {len(cands)}, ranked:")
    report_candidates(cands)

    top_rank = tier(cands[0].get("signals", []))[0]
    top = [c for c in cands if tier(c.get("signals", []))[0] == top_rank]

    if args.to:
        match = [c for c in cands if c["to"] == args.to]
        if not match:
            print(f"\n{args.to} is not among the candidates listed above.")
            print("Pass a name from the list, or check the spelling.")
            return 1
        best = match[0]
        print(f"\nUsing --to {args.to} (your choice, not the script's).")
    elif len(top) > 1:
        # Do not pick. Within a tier the artifact imposes no order, so cands[0]
        # is whatever the JSON happened to list first -- for CCSPlayerPawn's
        # m_flLandseconds that is m_fLastGivenBombTime, while the name a human
        # would choose (m_flLandingTimeSeconds) sits several entries down.
        # Drafting from that position would launder arbitrary order into an
        # assertion about Valve's history.
        print(f"\nNO DRAFT — {len(top)} candidates tie at the top signal tier.")
        print("  The artifact imposes no order within a tier and neither does this")
        print("  script, so picking one here would be arbitrary, not evidenced.")
        print(f"  Decide, then re-run with:  --to <name> --write")
        return 0
    elif top_rank > DRAFTABLE_TIER:
        print(f"\nNO DRAFT — the only candidate lacks `offsetExact`.")
        print("  Offset is what makes a candidate near-unique; without it this is a")
        print("  guess the evidence does not support. Confirm by hand, then re-run")
        print("  with:  --to <name> --write")
        return 0
    else:
        best = cands[0]

    rank, label = tier(best.get("signals", []))
    competing = [c for c in top if c is not best]

    notes = (
        f"CS2_GEN_010 on {args.class_name}.{args.field}. "
        f"Candidate signals: {', '.join(sorted(best.get('signals', [])))} [{label}]. "
        f"{len(cands)} candidate(s) at build {tr['toBuild']}, "
        f"{len(competing)} tied at the top tier. "
        f"Drafted by scripts/lens-rename-candidates.py from schema_evolution "
        f"{artifact.get('schemaVersion')} ({args.platform}); reviewed by hand."
    )
    mid, migration = build_migration(
        next_ordinal(), tr["toBuild"], args.class_name, args.field, best, notes
    )

    print("\n--- DRAFT MIGRATION " + ("(writing)" if args.write else "(not written; --write to save)"))
    print(json.dumps(migration, indent=2, ensure_ascii=False))

    if args.write:
        out = os.path.join(LENS_DIR, mid + ".json")
        if os.path.exists(out):
            sys.exit(f"refusing to overwrite {out}")
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(migration, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
        print(f"\nwrote {os.path.relpath(out, REPO)}")
        print("Next: review the op, then run the exporter — it prints the real")
        print("stateHash under CS2_GEN_014 and exits 1 until you paste it in.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
