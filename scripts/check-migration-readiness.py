#!/usr/bin/env python3
"""Gate the scheduled submodule bump on the SDK actually being able to ship it.

Two conditions, both about "can this bump be released", both stated here rather
than left to whichever downstream step happens to break first:

1. The schema carries what the namespace layout needs (`projectName` on enums).
2. The staged `.proto` surface has not shrunk without `version.json` moving.

They arrived years apart in spirit and days apart in fact, and they share a
lesson: the cron bumps, regenerates, commits, pushes and publishes unattended, so
anything that must not ship has to be a hard failure *before* the regen, not a
review item after it.

Why the first condition exists
------------------------------
Until 2026-08-09 the generator could not parse schema_format_version 2.0, and
that inability was load-bearing in a way nobody designed: `check-upstream.yml`
bumps the submodule, regenerates, then commits and pushes to main, and a
dependent job publishes. The regen step failing on CS2_GEN_004 was the only
thing keeping "commit + push" unreachable. The migration doc said as much --
"nothing has been published ... while the parse guard holds".

Teaching the generator to read 2.0 removed that block without removing the
reason for it. The pin still cannot move: schema 2.0 enum records carry no
`projectName`, so 591 of 610 enums fall back to `module`, which in 2.0 is the
binary, and land together in one namespace. That compiles. It is a breaking
namespace change for every consumer, and the cron would have shipped it as a
stable release within four hours, unattended.

So the readiness condition gets stated explicitly instead of riding on a parse
failure. It is also a better condition than the one it replaces: the format
version was only ever a proxy, and this checks the thing that actually blocks
us. It clears itself the moment upstream publishes an artifact carrying the
field -- no code change needed to unblock, which the old accidental gate would
have required.

Why the second condition exists
-------------------------------
`CS2OpenDev.Protos` 3.0.6 -> 3.0.7 removed 188 top-level types and shipped as a
patch. Everything was green and everything was right to be -- none of those checks
is a semantic-versioning gate. `protos/` is restaged from a submodule by
`normalize-protos.py`, so the package surface can shrink without a line of this
repository changing, and Nerdbank.GitVersioning supplies a patch number either
way. See `scripts/proto_surface.py` for the surface model and its limits, and
https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/13 for the report.

The rule is deliberately narrow: a shrink is not forbidden, it is only forbidden
*silently*. Bumping `MAJOR`/`MINOR` in `src/CS2OpenDev.Protos/version.json`
discharges it, which is the same shape as the Schema Lens gates -- a fact the
build cannot infer, asserted by a human, checked mechanically thereafter.

Exit codes: 0 ready to bump, 1 not ready (or an input could not be read).
"""

import json
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from proto_surface import diff, surface_from_disk, surface_from_ref  # noqa: E402

SCHEMA = "upstream/docs/generated/downstream-codegen-schemas/cs2_schema.json"
ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/1"

PROTOS_DIR = Path("protos")
PROTOS_VERSION_JSON = "src/CS2OpenDev.Protos/version.json"
TAG_PREFIX = "CS2OpenDev.Protos/v"
SURFACE_ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/13"


# Annotations are suppressed while the self-test runs. Its expected-failure case
# would otherwise paint a red ::error:: on a green run, which trains people to
# ignore the annotation this gate depends on being read.
_ANNOTATE = True


def fail(msg: str) -> int:
    # ::error:: renders in the Actions log and on the run summary.
    print(f"::error::{msg}" if _ANNOTATE else f"       {msg}")
    return 1


def note(msg: str) -> None:
    print(f"::notice::{msg}" if _ANNOTATE else f"       {msg}")


def _latest_protos_tag() -> str | None:
    """The newest released `CS2OpenDev.Protos` tag, by version order.

    Version order rather than tag date, because a re-dispatched release can tag an
    older version later. Unparseable tags are skipped rather than fatal -- a
    malformed tag must not be able to disable the gate.
    """
    try:
        tags = subprocess.run(
            ["git", "tag", "--list", f"{TAG_PREFIX}*"],
            capture_output=True, text=True, check=True,
        ).stdout.split()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None

    parsed = []
    for tag in tags:
        m = re.fullmatch(rf"{re.escape(TAG_PREFIX)}(\d+)\.(\d+)\.(\d+)", tag)
        if m:
            parsed.append((tuple(int(g) for g in m.groups()), tag))
    return max(parsed)[1] if parsed else None


def _declared_version(text: str) -> str:
    return str(json.loads(text).get("version", ""))


def _version_at(ref: str) -> str:
    return _declared_version(
        subprocess.run(
            ["git", "show", f"{ref}:{PROTOS_VERSION_JSON}"],
            capture_output=True, text=True, check=True,
        ).stdout
    )


def _verdict(baseline, before, after, baseline_version, current_version) -> int:
    """The decision, given two surfaces and two declared versions.

    Split out from its inputs so the same rule serves the live check (disk against
    a tag) and the self-test (tag against tag).
    """
    removed, shrunk, added = diff(before, after)
    if not removed and not shrunk:
        note(
            f"Proto surface: no removals against {baseline} "
            f"({len(after)} top-level types, {len(added)} added)."
        )
        return 0

    if current_version != baseline_version:
        note(
            f"Proto surface: {len(removed)} type(s) removed and {len(shrunk)} shrunk "
            f"against {baseline}, declared under version.json {baseline_version} -> "
            f"{current_version}. Acknowledged — proceeding."
        )
        return 0

    detail = ", ".join(removed[:8]) + (f", … (+{len(removed) - 8} more)" if len(removed) > 8 else "")
    shrunk_detail = "; ".join(f"{n} lost {v}" for n, v in list(shrunk.items())[:4])
    return fail(
        f"Proto surface shrank without a version bump. Against {baseline}: "
        f"{len(removed)} top-level type(s) removed"
        + (f" [{detail}]" if removed else "")
        + (f"; {len(shrunk)} type(s) lost field/enum numbers [{shrunk_detail}]" if shrunk else "")
        + f". {PROTOS_VERSION_JSON} still declares {current_version}, so this would "
        f"publish as a patch — which is what happened at 3.0.7. Removing public API "
        f"is a MAJOR: bump version.json to the family's current major and document "
        f"the removals (docs/MIGRATION-4.1-protos.md is the template). See "
        f"{SURFACE_ISSUE}."
    )


def check_proto_surface(baseline: str | None = None) -> int:
    """Fail when the staged `.proto` surface shrank without `version.json` moving.

    `baseline` overrides the auto-detected tag. CI never passes it; it exists for
    auditing one transition by hand ("what did 3.0.6 -> 3.0.7 actually remove").
    """
    if not PROTOS_DIR.is_dir():
        return fail(f"{PROTOS_DIR}/ not found — has normalize-protos.py run?")

    baseline = baseline or _latest_protos_tag()
    if baseline is None:
        # First release, or a checkout without tags. Not a pass in disguise: say so,
        # because a silently skipped gate is what this whole check exists to prevent.
        note(
            "Proto surface: no released CS2OpenDev.Protos tag to compare against — "
            "gate skipped. In CI this means the checkout has no tags; "
            "actions/checkout needs fetch-depth: 0."
        )
        return 0

    try:
        before = surface_from_ref(baseline)
        baseline_version = _version_at(baseline)
    except (subprocess.CalledProcessError, json.JSONDecodeError) as exc:
        return fail(f"Proto surface: could not read the baseline at {baseline}: {exc}")

    return _verdict(
        baseline,
        before,
        surface_from_disk(PROTOS_DIR),
        baseline_version,
        _declared_version(Path(PROTOS_VERSION_JSON).read_text(encoding="utf-8")),
    )


# The incident this gate exists for, replayed from the tags it happened on. Both
# directions matter: catching the removal proves the gate fires, and passing the
# acknowledged version bump proves it does not simply refuse every shrink.
SELFTEST_CASES = [
    ("CS2OpenDev.Protos/v3.0.6", "CS2OpenDev.Protos/v3.0.7", 1,
     "188 types removed under an unchanged 3.0 — the original incident"),
    ("CS2OpenDev.Protos/v3.0.6", "CS2OpenDev.Protos/v4.1.1", 0,
     "the same removals, acknowledged by 3.0 -> 4.1"),
    ("CS2OpenDev.Protos/v3.0.7", "CS2OpenDev.Protos/v4.1.1", 0,
     "renumbering only, no surface change"),
]


def selftest() -> int:
    """Replay known transitions tag-to-tag and assert the verdicts.

    Needs no worktree: both sides come out of git. Cheap enough to run on every
    PR, which is the point — a gate nobody exercises is a gate nobody trusts.
    """
    global _ANNOTATE
    _ANNOTATE = False
    failures = 0
    for before_ref, after_ref, expected, why in SELFTEST_CASES:
        try:
            got = _verdict(
                before_ref,
                surface_from_ref(before_ref),
                surface_from_ref(after_ref),
                _version_at(before_ref),
                _version_at(after_ref),
            )
        except subprocess.CalledProcessError as exc:
            _ANNOTATE = True
            print(f"::error::Self-test could not read {before_ref}..{after_ref}: {exc}")
            _ANNOTATE = False
            failures += 1
            continue
        verdict = "fail" if got else "pass"
        want = "fail" if expected else "pass"
        if got == expected:
            print(f"  ok   {verdict:4}  {before_ref} -> {after_ref}  ({why})")
        else:
            _ANNOTATE = True
            print(f"::error::Self-test: {before_ref} -> {after_ref} gave {verdict}, want {want} ({why})")
            _ANNOTATE = False
            failures += 1

    _ANNOTATE = True
    if failures:
        return fail(f"Proto surface gate self-test: {failures} case(s) wrong.")
    print(f"Proto surface gate self-test: {len(SELFTEST_CASES)}/{len(SELFTEST_CASES)} cases correct.")
    return 0


def check_schema_readiness() -> int:
    try:
        with open(SCHEMA, encoding="utf-8") as fh:
            schema = json.load(fh)
    except FileNotFoundError:
        return fail(f"{SCHEMA} not found — is the upstream submodule initialised?")
    except json.JSONDecodeError as exc:
        return fail(f"{SCHEMA} is not valid JSON: {exc}")

    declared = str(schema.get("schema_format_version", ""))

    enums = schema.get("enums", [])
    if not enums:
        return fail(f"{SCHEMA} declares no enums — refusing to treat that as ready.")

    # The namespace key. `module` is the binary; `projectName` is the project,
    # and it is what the SDK's namespace layout is built from. Classes carry it,
    # enums do not yet.
    key = "projectName"
    attributed = sum(1 for e in enums if e.get(key))
    total = len(enums)
    if attributed == total:
        print(f"Ready: all {total} enum records carry `{key}` (schema {declared}).")
        return 0

    return fail(
        f"Not ready to bump: {attributed} of {total} enum records carry `{key}` "
        f"in schema {declared}. Without it the missing records fall back to "
        f"`module` — the binary — and collapse into a single namespace, which is "
        f"a breaking change for every consumer. Blocked on {ISSUE}. "
        f"See docs/upstream/schematracker-migration.md."
    )


def main(argv: list[str]) -> int:
    # Both of these skip the schema check, which is about the current upstream pin
    # and has nothing to say about history.
    if "--selftest" in argv:
        return selftest()
    if "--baseline" in argv:
        return check_proto_surface(argv[argv.index("--baseline") + 1])

    # Both run even when the first fails. A bump that trips one condition usually
    # trips the other, and reporting only the first turns one unattended run into
    # two — the second discovering what the first could have said.
    return max(check_schema_readiness(), check_proto_surface())


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
