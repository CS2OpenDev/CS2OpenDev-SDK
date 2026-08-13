#!/usr/bin/env python3
"""Model the public surface of the staged `.proto` set, so a shrink can be caught.

Why this exists
---------------
On 2026-08-13 `CS2OpenDev.Protos` 3.0.6 -> 3.0.7 removed 188 top-level types and
shipped as a patch. Nothing was broken at the time: the build, the test suites,
`check-migration-readiness.py` and the regen fixed point were all green, and all
of them were right to be. None of them is a semantic-versioning gate.

The removal did not come from Valve. SchemaTracker v1.3.0 began emitting
`cstrike15_gcmessages.proto` as a *derived closure* -- the top-level types the
rest of the artifact set transitively references -- which kept 17 of 162 and made
three imports unnecessary. That is an upstream extraction-scope change, and it is
exactly the shape of event this repo cannot see coming: `protos/` is restaged from
a submodule by `normalize-protos.py`, so the package surface can shrink without a
single line of this repository changing.

The major comes from a human editing `version.json`; the patch comes from git
height. So an upstream removal of public API ships as a patch by default, and the
only thing standing between that and a release is somebody noticing. This module
is the half that was missing.

Tracked as https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/13.

What the surface model is
-------------------------
For each `.proto` file, every **top-level** `message` or `enum`, mapped to the set
of numbers declared anywhere inside its body (field tags and enum values alike).
Both halves matter, and for the same reason: a consumer binds to a generated type
by name and to its members by tag, so losing either is a break the version number
has to say out loud.

Nested types are folded into their top-level parent rather than tracked
separately. Their numbers are still counted, so dropping a nested message's field
is caught -- it is attributed to the enclosing type, which is the one a consumer
names.

What it deliberately does not catch
-----------------------------------
This is a lexical model, not a protobuf parser, and the limits are worth stating
rather than discovering:

- **Renames at a stable tag.** `optional string foo = 3` becoming `bar = 3` is a
  source break for a C# consumer and reads here as no change at all. Catching it
  needs per-tag names, which needs real scope tracking.
- **Type changes at a stable tag.** `int32 x = 3` -> `int64 x = 3` likewise.
- **Cardinality changes.** `optional` -> `repeated` at the same tag.

Each is a narrower break than a disappearance, and each would need a parser to see
honestly. The failure this exists to stop is the wholesale one, and a check that
catches it reliably beats a check that half-catches everything. Widening later is
additive: the surface dict is the only thing callers depend on.
"""

import re
import subprocess
from pathlib import Path

# A top-level declaration starts at column 0 -- these files are generated, so the
# indentation is reliable in a way hand-written protos would not be.
_TOP_LEVEL = re.compile(r"^(message|enum)\s+(\w+)", re.MULTILINE)

# Field tags (`= 7;`) and enum values (`= 7;`, `= 7 [deprecated = true];`) share a
# shape. Trailing `[...]` options are matched so the terminator can be `;` or `[`.
_NUMBER = re.compile(r"=\s*(\d+)\s*[;\[]")

# Lines that carry an `=` but declare nothing: `option`, `reserved`, and the
# `[default = 0]` inside an option block are all noise for this purpose.
_NOISE = re.compile(r"^\s*(option|reserved|syntax|import)\b")


def _bodies(text: str) -> dict[str, str]:
    """Split a .proto into {top-level type name: its body text}.

    Bodies run from a top-level declaration to the next one, so a nested type's
    numbers land in its enclosing top-level type. That is intentional -- see the
    module docstring.
    """
    marks = [(m.start(), m.group(2)) for m in _TOP_LEVEL.finditer(text)]
    out: dict[str, str] = {}
    for i, (start, name) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(text)
        out[name] = text[start:end]
    return out


def surface_of_text(text: str) -> dict[str, set[int]]:
    """The surface a single .proto file contributes."""
    result: dict[str, set[int]] = {}
    for name, body in _bodies(text).items():
        numbers: set[int] = set()
        for line in body.splitlines():
            if _NOISE.match(line):
                continue
            numbers.update(int(n) for n in _NUMBER.findall(line))
        # Same type name in two files would collide; proto's own namespacing makes
        # that a compile error upstream, so a merge here is safe and keeps the key
        # space flat (a consumer names the type, not the file).
        result.setdefault(name, set()).update(numbers)
    return result


def surface_from_disk(protos_dir: Path) -> dict[str, set[int]]:
    """The surface of the working tree's staged `protos/`."""
    result: dict[str, set[int]] = {}
    for path in sorted(protos_dir.glob("*.proto")):
        for name, numbers in surface_of_text(path.read_text(encoding="utf-8")).items():
            result.setdefault(name, set()).update(numbers)
    return result


def surface_from_ref(ref: str, protos_dir: str = "protos") -> dict[str, set[int]]:
    """The surface as of a git ref. Raises CalledProcessError if the ref is absent."""
    listing = subprocess.run(
        ["git", "ls-tree", "-r", "--name-only", ref, "--", protos_dir],
        capture_output=True, text=True, check=True,
    ).stdout.split()

    result: dict[str, set[int]] = {}
    for path in listing:
        if not path.endswith(".proto"):
            continue
        text = subprocess.run(
            ["git", "show", f"{ref}:{path}"],
            capture_output=True, text=True, check=True,
        ).stdout
        for name, numbers in surface_of_text(text).items():
            result.setdefault(name, set()).update(numbers)
    return result


def diff(before: dict[str, set[int]], after: dict[str, set[int]]):
    """Return (removed_types, shrunk_types, added_types).

    `shrunk_types` maps a surviving type to the numbers it lost.
    """
    removed = sorted(set(before) - set(after))
    added = sorted(set(after) - set(before))
    shrunk = {}
    for name in sorted(set(before) & set(after)):
        lost = before[name] - after[name]
        if lost:
            shrunk[name] = sorted(lost)
    return removed, shrunk, added
