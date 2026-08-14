#!/usr/bin/env python3
"""Assert every shipped package is wired into all three places a release needs.

Why this exists
---------------
Adding a package means editing three files, and getting two of them right looks
exactly like getting all three right.

`_pack-and-publish.yml` carries the matrix that builds and publishes. `ci.yml`
packs each package on PRs so a packaging break is caught before merge. And
`release.yml` decides, from the paths a push touched, whether to run the matrix
at all -- those paths are literal prefixes, and `src/CS2OpenDev.Sdk/**` does not
match `src/CS2OpenDev.Sdk.GameEvents/**`. The file says so in its own comment.

It happened anyway. `CS2OpenDev.Sdk.Entities.Abstractions` was added to the
matrix and to the CI pack step, merged to main, and no release fired -- because
nothing had changed under a path `release.yml` was watching. The failure is
silent by construction: a package missing from the trigger list does not error,
it just never ships, and the matrix that would have built it never runs to say
so.

So the invariant gets stated instead of remembered. Cheap, and it fails on the PR
that introduces the gap rather than on the merge that silently does nothing.

Exit codes: 0 wired correctly, 1 something is missing (or a workflow could not be read).
"""

import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - CI installs it; local runs may not have it
    print("::warning::PyYAML not installed — release wiring not checked.")
    sys.exit(0)

REPO = Path(__file__).resolve().parent.parent
PACK_PUBLISH = REPO / ".github/workflows/_pack-and-publish.yml"
RELEASE = REPO / ".github/workflows/release.yml"
CI = REPO / ".github/workflows/ci.yml"


def fail(msg: str) -> int:
    print(f"::error::{msg}")
    return 1


def main() -> int:
    try:
        pack = yaml.safe_load(PACK_PUBLISH.read_text(encoding="utf-8"))
        release = yaml.safe_load(RELEASE.read_text(encoding="utf-8"))
        ci_text = CI.read_text(encoding="utf-8")
    except (OSError, yaml.YAMLError) as exc:
        return fail(f"Release wiring: could not read a workflow: {exc}")

    # PyYAML parses the bare `on:` key as the boolean True, which is a YAML 1.1
    # quirk rather than anything about these files. Accept both spellings so a
    # future quoted `"on":` does not silently skip the check.
    def trigger_block(doc):
        return doc.get("on") or doc.get(True) or {}

    job = next(iter(pack.get("jobs", {}).values()), {})
    packages = [
        entry["id"]
        for entry in job.get("strategy", {}).get("matrix", {}).get("include", [])
    ]
    if not packages:
        return fail("Release wiring: no packages found in the pack-and-publish matrix.")

    # The matrix cannot be the only source of truth for what ships, or a package
    # absent from all three places is invisible to this check -- which is the
    # failure it exists to prevent, one level up. It happened while adding
    # CS2OpenDev.Sdk.Entities: the gate passed on four packages and said nothing
    # about the fifth, because the fifth was in none of the lists it reads.
    #
    # So the ground truth is the filesystem: a project under src/ carrying a
    # PackageId is a package, whatever the workflows think.
    shipped = []
    for proj in sorted((REPO / "src").glob("*/*.csproj")):
        if "<PackageId>" in proj.read_text(encoding="utf-8"):
            shipped.append(proj.parent.name)

    missing_from_matrix = [p for p in shipped if p not in packages]
    if missing_from_matrix:
        for pkg in missing_from_matrix:
            print(
                f"::error::Release wiring: src/{pkg}/ declares a PackageId but is not in the "
                f"pack-and-publish matrix — it would never be built or published."
            )
        return 1

    paths = trigger_block(release).get("push", {}).get("paths", [])
    if not paths:
        return fail("Release wiring: release.yml declares no push trigger paths.")

    problems: list[str] = []

    for pkg in packages:
        # The matrix entry's own `path`, expressed as the glob release.yml needs.
        expected = f"src/{pkg}/**"
        if expected not in paths:
            problems.append(
                f"{pkg} is in the publish matrix but '{expected}' is not in release.yml's "
                f"trigger paths — merging a change to it would ship nothing."
            )

        if f"dotnet pack src/{pkg}" not in ci_text:
            problems.append(
                f"{pkg} is in the publish matrix but ci.yml never packs it — a packaging "
                f"break would reach main unnoticed."
            )

    if problems:
        for problem in problems:
            print(f"::error::Release wiring: {problem}")
        return 1

    print(
        f"Release wiring: all {len(packages)} package(s) found under src/ are in the publish "
        f"matrix, release.yml's trigger paths, and ci.yml's pack step."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
