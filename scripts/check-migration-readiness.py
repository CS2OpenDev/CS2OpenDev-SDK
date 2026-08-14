#!/usr/bin/env python3
"""Gate the scheduled submodule bump on the SDK actually being able to ship it.

Three conditions, all about "can this bump be released", all stated here rather
than left to whichever downstream step happens to break first:

1. The schema carries what the namespace layout needs (`projectName` on enums).
2. The staged `.proto` surface has not shrunk without `version.json` moving.
3. The emitted C# surface has not lost a type or a member without the same.

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

Why the third condition exists
------------------------------
The proto gate covered the smaller of the two generated surfaces. `CS2OpenDev.Sdk`
is the one consumers actually bind to -- ~7,700 public types against the proto
set's few hundred -- and until now nothing looked at it at all.

The 5.0 repair is the proof. It changed 1,923 property types across 820 classes
and dropped 760 stub classes, and every check in the pipeline stayed green,
correctly, because none of them is a semantic-versioning gate. The change was
right. The point is that a wrong one would have looked identical from here.

The split between blocking and reporting is not symmetric, and 5.0 is why.
Blocking type changes would have blocked the repair -- all 1,923 of them were
fixes to properties that had been emitted as empty stubs for three majors. So a
type change is reported loudly and proceeds; a removal, which has no benign
reading, withholds. `scripts/sdk_surface.py` carries the surface model, the rest
of that reasoning, and an explicit list of what a lexical model cannot see.

The acknowledgement for this one is the **root** `version.json`, not a file under
`src/CS2OpenDev.Sdk/` -- there isn't one. The root file's
`"pathFilters": [":/src/CS2OpenDev.Sdk/"]` is what makes it this package's
version source, and it is the same file a human edits to cut a major.

Tracked as https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/32.

Exit codes: 0 ready to bump, 1 not ready (or an input could not be read).
"""

import json
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sdk_surface  # noqa: E402
from proto_surface import diff, surface_from_disk, surface_from_ref  # noqa: E402

SCHEMA = "upstream/docs/generated/downstream-codegen-schemas/cs2_schema.json"
ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/1"

PROTOS_DIR = Path("protos")
PROTOS_VERSION_JSON = "src/CS2OpenDev.Protos/version.json"
TAG_PREFIX = "CS2OpenDev.Protos/v"
SURFACE_ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/13"

SDK_DIR = Path(sdk_surface.SDK_ROOT)
# The root file, not one under src/CS2OpenDev.Sdk/. See the module docstring.
SDK_VERSION_JSON = "version.json"
SDK_TAG_PREFIX = "CS2OpenDev.Sdk/v"
SDK_SURFACE_ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/32"


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


def _latest_tag(prefix: str) -> str | None:
    """The newest released tag under `prefix`, by version order.

    Version order rather than tag date, because a re-dispatched release can tag an
    older version later. Unparseable tags are skipped rather than fatal -- a
    malformed tag must not be able to disable the gate.

    The `prefix` also has to be exact: `CS2OpenDev.Sdk/v` must not pick up
    `CS2OpenDev.Sdk.GameEvents/v5.1.0`, which the glob would happily match if the
    trailing slash were dropped. The fullmatch below is the real guard.
    """
    try:
        tags = subprocess.run(
            ["git", "tag", "--list", f"{prefix}*"],
            capture_output=True, text=True, check=True,
        ).stdout.split()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None

    parsed = []
    for tag in tags:
        m = re.fullmatch(rf"{re.escape(prefix)}(\d+)\.(\d+)\.(\d+)", tag)
        if m:
            parsed.append((tuple(int(g) for g in m.groups()), tag))
    return max(parsed)[1] if parsed else None


def _declared_version(text: str) -> str:
    return str(json.loads(text).get("version", ""))


def _version_at(ref: str, path: str) -> str:
    return _declared_version(
        subprocess.run(
            ["git", "show", f"{ref}:{path}"],
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

    baseline = baseline or _latest_tag(TAG_PREFIX)
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
        baseline_version = _version_at(baseline, PROTOS_VERSION_JSON)
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


def proto_selftest() -> int:
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
                _version_at(before_ref, PROTOS_VERSION_JSON),
                _version_at(after_ref, PROTOS_VERSION_JSON),
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


def _sdk_report(before: sdk_surface.Surface, d: sdk_surface.SurfaceDiff) -> None:
    """Say out loud everything that changed but is not being blocked.

    This half is the actual answer to issue #32. Withholding a removal was never
    the hard part; 5.0's 1,923 type changes went by unremarked precisely because
    nothing was in a position to remark on them, and a gate that silently allows
    them reproduces that. So the counts land on the run summary either way.
    """
    retyped = sdk_surface.property_type_changes(d)
    if retyped:
        shapes = Counter((c.before.type, c.after.type) for c in retyped)
        top = "; ".join(f"{n}x `{b}` -> `{a}`" for (b, a), n in shapes.most_common(3))
        note(
            f"SDK surface: {len(retyped)} property type change(s) across "
            f"{len({c.type_name for c in retyped})} type(s), in {len(shapes)} distinct "
            f"shapes — {top}"
            + (f"; and {len(shapes) - 3} further shapes" if len(shapes) > 3 else "")
            + ". Reported, not blocked: the 5.0 repair was 1,923 of these and every "
            "one was a fix. Read them before releasing."
        )

    # By key, not by scanning `retyped` per change: both lists run to ~1,900 on a
    # transition the size of 5.0, and the self-test replays that three times.
    retyped_keys = {(c.type_name, c.member) for c in retyped}
    other = [c for c in d.changed_members if (c.type_name, c.member) not in retyped_keys]
    if other:
        example = other[0]
        note(
            f"SDK surface: {len(other)} member(s) changed in some way other than a "
            f"property's type — accessors, enum values or member kind. First: "
            f"{example.type_name}.{example.member} {example.before} -> {example.after}."
        )

    if d.redeclared:
        first = "; ".join(f"{n}: {b} -> {a}" for n, b, a in d.redeclared[:3])
        note(
            f"SDK surface: {len(d.redeclared)} type(s) changed declaration — a base "
            f"list, an interface or an enum's underlying type. {first}. Inherited "
            f"members are not expanded by this model, so check what moved with them."
        )

    if d.added_types or d.added_members:
        note(
            f"SDK surface: {len(d.added_types)} type(s) and {len(d.added_members)} "
            f"member(s) added. Additions never block."
        )


def _sdk_verdict(baseline, before, after, baseline_version, current_version) -> int:
    """The decision, given two surfaces and two declared versions.

    Split out from its inputs for the same reason the proto gate's is: it lets the
    self-test replay a transition tag-to-tag, and — more usefully here — lets it
    replay a real transition against a *counterfactual* version pair. Every
    released `CS2OpenDev.Sdk` tag either removes nothing or removes under an
    acknowledged bump, so without this seam the gate has no failing case to prove
    itself against.
    """
    d = sdk_surface.diff(before, after)
    _sdk_report(before, d)

    if not sdk_surface.removes_api(d):
        note(
            f"SDK surface: no removals against {baseline} "
            f"({len(after)} public types, {sum(len(e.members) for e in after.values())} "
            f"members)."
        )
        return 0

    hidden = sum(1 for t in d.removed_types if before[t].hidden)
    character = (
        f"{len(d.removed_types)} public type(s) removed"
        + (f", {hidden} of them [EditorBrowsable(Never)] stubs" if hidden else "")
        if d.removed_types else ""
    )
    members = (
        f"{len(d.removed_members)} member(s) removed from surviving types"
        if d.removed_members else ""
    )
    summary = " and ".join(p for p in (character, members) if p)

    if current_version != baseline_version:
        note(
            f"SDK surface: {summary} against {baseline}, declared under "
            f"{SDK_VERSION_JSON} {baseline_version} -> {current_version}. "
            f"Acknowledged — proceeding."
        )
        return 0

    names = ", ".join(sorted(d.removed_types)[:6])
    lost = "; ".join(f"{t}.{m}" for t, m in d.removed_members[:6])
    return fail(
        f"SDK surface lost public API without a version bump. Against {baseline}: "
        + summary
        + (f" [{names}{', …' if len(d.removed_types) > 6 else ''}]" if d.removed_types else "")
        + (f" [{lost}{'; …' if len(d.removed_members) > 6 else ''}]" if d.removed_members else "")
        + f". {SDK_VERSION_JSON} still declares {current_version}, so this would publish "
        f"as a patch off git height — the shape that put a 188-type removal out as "
        f"CS2OpenDev.Protos 3.0.7. Removing public API is a MAJOR: bump the root "
        f"{SDK_VERSION_JSON} and document the removals (docs/MIGRATION-5.0.md is the "
        f"template). If the removals are stubs the emitter no longer needs, that is "
        f"still a MAJOR — 5.0 dropped 760 of them and said so. See {SDK_SURFACE_ISSUE}."
    )


def check_sdk_surface(baseline: str | None = None) -> int:
    """Fail when the emitted C# surface lost API without the root version moving."""
    if not SDK_DIR.is_dir():
        return fail(f"{SDK_DIR}/ not found — is this a full checkout?")

    baseline = baseline or _latest_tag(SDK_TAG_PREFIX)
    if baseline is None:
        # Not a pass in disguise: say so, because a silently skipped gate is what
        # this whole check exists to prevent.
        note(
            "SDK surface: no released CS2OpenDev.Sdk tag to compare against — "
            "gate skipped. In CI this means the checkout has no tags; "
            "actions/checkout needs fetch-depth: 0."
        )
        return 0

    try:
        before = sdk_surface.surface_from_ref(baseline)
        baseline_version = _version_at(baseline, SDK_VERSION_JSON)
    except (subprocess.CalledProcessError, json.JSONDecodeError) as exc:
        return fail(f"SDK surface: could not read the baseline at {baseline}: {exc}")

    return _sdk_verdict(
        baseline,
        before,
        sdk_surface.surface_from_disk(SDK_DIR),
        baseline_version,
        _declared_version(Path(SDK_VERSION_JSON).read_text(encoding="utf-8")),
    )


# The 4.1 -> 5.0 transition, whose right answer is known independently: 1,923
# property type changes across 820 classes is the count `docs/MIGRATION-5.0.md`
# was built from, by a different method (a diff of property declarations out of
# `git diff`). Two cases, plus the counts.
#
# The first case is counterfactual, and has to be. No released CS2OpenDev.Sdk tag
# pair removes API without an acknowledging bump, so the only way to prove the
# gate fires is to replay a real removal against the version that did NOT move.
# That is the incident shape — the removal is real, only the acknowledgement is
# imagined away.
SDK_SELFTEST_CASES = [
    ("CS2OpenDev.Sdk/v4.1.5", "CS2OpenDev.Sdk/v5.0.1", "4.1", "4.1", 1,
     "760 stub classes removed under an unchanged 4.1 — the 5.0 repair, unacknowledged"),
    ("CS2OpenDev.Sdk/v4.1.5", "CS2OpenDev.Sdk/v5.0.1", None, None, 0,
     "the same removals, acknowledged by 4.1 -> 5.0"),
    ("CS2OpenDev.Sdk/v5.0.1", "CS2OpenDev.Sdk/v5.1.0", None, None, 0,
     "a release that removed nothing"),
]

# Asserted separately from the verdicts, and this is the load-bearing half. The
# verdict cases only prove the *rule* still works; they would pass unchanged if
# the extractor silently stopped recognising properties, because an empty diff
# removes nothing. Pinning the counts is what makes an emitter reformat — which
# is invisible to the compiler and would blind the lexical model — show up as a
# red step instead of a permanently green one.
SDK_SELFTEST_COUNTS = (
    "CS2OpenDev.Sdk/v4.1.5", "CS2OpenDev.Sdk/v5.0.1",
    {"property type changes": 1923, "declaring types": 820,
     "removed types": 760, "removed members": 0},
)


def sdk_selftest() -> int:
    global _ANNOTATE
    _ANNOTATE = False
    failures = 0
    cache: dict[str, sdk_surface.Surface] = {}

    def surface(ref: str) -> sdk_surface.Surface:
        if ref not in cache:
            cache[ref] = sdk_surface.surface_from_ref(ref)
        return cache[ref]

    for before_ref, after_ref, before_v, after_v, expected, why in SDK_SELFTEST_CASES:
        try:
            got = _sdk_verdict(
                before_ref,
                surface(before_ref),
                surface(after_ref),
                before_v if before_v is not None else _version_at(before_ref, SDK_VERSION_JSON),
                after_v if after_v is not None else _version_at(after_ref, SDK_VERSION_JSON),
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

    before_ref, after_ref, want_counts = SDK_SELFTEST_COUNTS
    try:
        d = sdk_surface.diff(surface(before_ref), surface(after_ref))
        retyped = sdk_surface.property_type_changes(d)
        got_counts = {
            "property type changes": len(retyped),
            "declaring types": len({c.type_name for c in retyped}),
            "removed types": len(d.removed_types),
            "removed members": len(d.removed_members),
        }
    except subprocess.CalledProcessError as exc:
        _ANNOTATE = True
        print(f"::error::Self-test could not read {before_ref}..{after_ref}: {exc}")
        _ANNOTATE = False
        failures += 1
        got_counts = {}

    for label, want in want_counts.items():
        got = got_counts.get(label)
        if got == want:
            print(f"  ok   {want:5}  {label}  ({before_ref} -> {after_ref})")
        else:
            _ANNOTATE = True
            print(
                f"::error::Self-test: {before_ref} -> {after_ref} counted {got} "
                f"{label}, want {want}. docs/MIGRATION-5.0.md is the independent "
                f"source for these; a mismatch means the extractor stopped seeing "
                f"part of the surface, not that the history changed."
            )
            _ANNOTATE = False
            failures += 1

    _ANNOTATE = True
    total = len(SDK_SELFTEST_CASES) + len(want_counts)
    if failures:
        return fail(f"SDK surface gate self-test: {failures} case(s) wrong.")
    print(f"SDK surface gate self-test: {total}/{total} cases correct.")
    return 0


def selftest() -> int:
    """Exercise both surface gates. Neither needs a worktree or a build."""
    return max(proto_selftest(), sdk_selftest())


def sdk_audit(before_ref: str, after_ref: str | None) -> int:
    """Print the full SDK surface verdict between two refs, ignoring version.json.

    For auditing one transition by hand — "what did 4.1 -> 5.0 actually do to the
    C# surface". Versions are forced equal so the removals get described rather
    than waved through by the bump that acknowledged them. CI never calls this.

    Always exits 0 when the audit itself ran. The verdict is printed, but it is
    an answer to a question somebody asked, not a judgement on the working tree,
    and an exit code that means "this transition removed things" is the kind that
    gets wired into a workflow by someone who read it as "the audit failed".
    """
    before = sdk_surface.surface_from_ref(before_ref)
    after = (
        sdk_surface.surface_from_ref(after_ref) if after_ref
        else sdk_surface.surface_from_disk(SDK_DIR)
    )
    _sdk_verdict(before_ref, before, after, "audit", "audit")
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
    # These all skip the schema check, which is about the current upstream pin
    # and has nothing to say about history.
    if "--selftest" in argv:
        # Scoped so each gate can own a named CI step — a log that says which gate
        # went red is worth more than one that says a script did. Bare --selftest
        # runs both, which is what a local run wants.
        rest = [a for a in argv[argv.index("--selftest") + 1:] if not a.startswith("-")]
        scope = rest[0] if rest else "all"
        if scope == "proto":
            return proto_selftest()
        if scope == "sdk":
            return sdk_selftest()
        if scope != "all":
            return fail(f"--selftest takes `proto`, `sdk` or nothing; got {scope!r}.")
        return selftest()
    if "--baseline" in argv:
        return check_proto_surface(argv[argv.index("--baseline") + 1])
    if "--sdk-only" in argv:
        # For the cron, which regenerates `src/CS2OpenDev.Sdk/` *after* the
        # readiness check and so has to ask again once the new sources exist.
        return check_sdk_surface()
    if "--sdk-baseline" in argv:
        return check_sdk_surface(argv[argv.index("--sdk-baseline") + 1])
    if "--sdk-audit" in argv:
        rest = argv[argv.index("--sdk-audit") + 1:]
        if not rest:
            return fail("--sdk-audit needs a ref, and optionally a second one.")
        return sdk_audit(rest[0], rest[1] if len(rest) > 1 else None)

    # All three run even when an earlier one fails. A bump that trips one
    # condition usually trips another, and reporting only the first turns one
    # unattended run into two — the second discovering what the first could have
    # said.
    return max(check_schema_readiness(), check_proto_surface(), check_sdk_surface())


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
