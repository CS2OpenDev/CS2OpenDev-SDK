#!/usr/bin/env python3
"""Diff the public type surface of two generated SDK trees.

Written for the 1.x -> 2.0 release, where the namespace layout moves for a
large fraction of the surface and consumers need to know which `using` lines to
change. It takes two directories rather than reading git, so it can be re-run
against any pair -- in particular it has to be re-run once SchemaTracker starts
emitting `projectName` on enum records, because that moves ~380 enums out of
GlobalTypes and every number below changes.

Usage:
    python3 scripts/namespace-diff.py OLD_SDK_DIR NEW_SDK_DIR [--markdown]

Default output is a summary; --markdown emits the moved-type table for
docs/MIGRATION-2.0.md.
"""

import os
import re
import sys
from collections import Counter, defaultdict

# public partial class Foo / public abstract partial class Foo / public enum Foo
DECL = re.compile(
    r"^public (?:abstract )?(?:partial class|readonly struct|struct|enum) (\w+)",
    re.M,
)
NS = re.compile(r"^namespace ([\w.]+)", re.M)


def surface(root: str) -> dict[str, str]:
    """Map every emitted public type name to the namespace declaring it."""
    found: dict[str, str] = {}
    for dirpath, _, filenames in os.walk(root):
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(dirpath, fn)
            try:
                text = open(path, encoding="utf-8").read()
            except OSError:
                continue
            ns = NS.search(text)
            if not ns:
                continue
            for name in DECL.findall(text):
                found[name] = ns.group(1)
    return found


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    markdown = "--markdown" in sys.argv
    if len(args) != 2:
        print(__doc__)
        return 2

    old, new = surface(args[0]), surface(args[1])
    common = old.keys() & new.keys()
    moved = sorted(t for t in common if old[t] != new[t])
    added = sorted(new.keys() - old.keys())
    removed = sorted(old.keys() - new.keys())

    if not markdown:
        print(f"old surface   {len(old)}")
        print(f"new surface   {len(new)}")
        print(f"  common      {len(common)}")
        print(f"  moved       {len(moved)}")
        print(f"  added       {len(added)}")
        print(f"  removed     {len(removed)}")
        print()
        for (a, b), n in Counter((old[t], new[t]) for t in moved).most_common():
            print(f"  {n:5}  {a} -> {b}")
        return 0

    # Grouped by the move itself: a consumer fixes one `using` at a time, so the
    # useful unit is "these N types went from A to B", not an alphabetical list.
    groups: dict[tuple[str, str], list[str]] = defaultdict(list)
    for t in moved:
        groups[(old[t], new[t])].append(t)

    print(f"Types that moved namespace: **{len(moved)}** of {len(common)} carried over.")
    print(f"New types: **{len(added)}**. Removed: **{len(removed)}**.")
    print()
    print("| From | To | Types | Names |")
    print("|---|---|---|---|")
    for (a, b), names in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        shown = ", ".join(f"`{n}`" for n in names[:6])
        if len(names) > 6:
            shown += f", … (+{len(names) - 6})"
        print(f"| `{a}` | `{b}` | {len(names)} | {shown} |")

    if removed:
        print()
        print("### Removed")
        print()
        print(", ".join(f"`{t}`" for t in removed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
