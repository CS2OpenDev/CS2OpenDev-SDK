#!/usr/bin/env python3
"""Diff the identifier names of two generated SDK trees.

Companion to namespace-diff.py, which answers "which namespace did this type
move to". This answers the other half — "what is this type or member called
now" — which namespace-diff cannot see, because a rename inside a namespace
looks to it like one type vanishing and an unrelated one appearing.

Written for the 2.0 -> 3.0 release, where every generated identifier is folded
to idiomatic .NET casing (`Userid` -> `UserId`, `Thrusmoke` -> `ThruSmoke`).

Members are paired by their `[NativeName]` attribute rather than by position or
similarity: the native name is the one thing the rename does not touch, so it
is a reliable key. Types have no such anchor and are reported as a gone/new
pair list instead of a mapping -- pairing them by edit distance would invent
correspondences that may not hold.

Usage:
    python3 scripts/rename-diff.py OLD_SDK_DIR NEW_SDK_DIR [--markdown]
"""

import os
import re
import sys

# [NativeName("m_flFoo")] ... public float Foo { get; set; }
# Tolerates attributes between the two, which is the normal shape.
MEMBER = re.compile(
    r'\[NativeName\("([^"]+)"\)\][^\n]*\n(?:\s*\[[^\n]*\]\n)*\s*'
    r"public [\w<>?\[\], ]*?(\w+)\s*(?:\{ get;|=)",
    re.M,
)
TYPE = re.compile(
    r"^public (?:sealed |abstract |partial |static )*"
    r"(?:partial )?(?:class|record|enum|struct) (\w+)",
    re.M,
)


def scan(root: str) -> tuple[dict[str, str], set[str]]:
    members: dict[str, str] = {}
    types: set[str] = set()
    for dirpath, _, filenames in os.walk(root):
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            try:
                text = open(os.path.join(dirpath, fn), encoding="utf-8").read()
            except OSError:
                continue
            for native, cs in MEMBER.findall(text):
                members[native] = cs
            types.update(TYPE.findall(text))
    return members, types


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    markdown = "--markdown" in sys.argv
    if len(args) != 2:
        print(__doc__)
        return 2

    om, ot = scan(args[0])
    nm, nt = scan(args[1])

    renamed = sorted(
        (native, om[native], nm[native])
        for native in om.keys() & nm.keys()
        if om[native] != nm[native]
    )
    gone, added = sorted(ot - nt), sorted(nt - ot)

    if not markdown:
        print(f"members matched by native name  {len(om.keys() & nm.keys())}")
        print(f"  renamed                       {len(renamed)}")
        print(f"types old / new                 {len(ot)} / {len(nt)}")
        print(f"  names gone                    {len(gone)}")
        print(f"  names new                     {len(added)}")
        return 0

    print(f"**{len(renamed)}** members renamed, of "
          f"{len(om.keys() & nm.keys())} matched by native name.")
    print(f"**{len(gone)}** type names replaced by **{len(added)}** new ones.")
    print()
    print("## Renamed members")
    print()
    print("| Native name | Was | Now |")
    print("|---|---|---|")
    for native, a, b in renamed:
        print(f"| `{native}` | `{a}` | `{b}` |")

    print()
    print("## Renamed types")
    print()
    print("| Was | Now |")
    print("|---|---|")
    # Same count on both sides in a pure-rename release, and sorting both by the
    # casing-insensitive form lines the pairs up. Falls back to listing them
    # separately if that assumption ever breaks.
    if len(gone) == len(added):
        pairs = zip(sorted(gone, key=str.lower), sorted(added, key=str.lower))
        for a, b in pairs:
            print(f"| `{a}` | `{b}` |")
    else:
        for a in gone:
            print(f"| `{a}` | — |")
        for b in added:
            print(f"| — | `{b}` |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
