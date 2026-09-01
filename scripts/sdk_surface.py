#!/usr/bin/env python3
"""Model the public C# surface `CS2OpenDev.Sdk` emits, so a removal can be caught.

Why this exists
---------------
`proto_surface.py` guards `CS2OpenDev.Protos` because a 188-type removal once
shipped as a patch. Nothing guarded `CS2OpenDev.Sdk`, which is the far larger
surface: ~7,700 named public types against the proto set's few hundred, and the
one every consumer actually binds to.

The 5.0 repair is the demonstration. It changed 1,923 property types across 820
classes and removed 760 stub classes from `Stubs.cs` -- 770 down to 12, two of
them new -- and no check in the pipeline said a word.
Every check that ran was right to pass -- the build, both test suites, the regen
fixed point, `check-migration-readiness.py` -- because none of them is a
semantic-versioning gate. The repair was correct, deliberate and documented in
`docs/MIGRATION-5.0.md`, and that is exactly the problem: the pipeline could not
have told a repair from a regression, and would not have told anyone either way.

The same asymmetry that made 3.0.7 possible applies here, and is worse. The MAJOR
comes from a human editing the root `version.json`; the PATCH comes from git
height. `src/CS2OpenDev.Sdk/` is regenerated wholesale from a submodule pin by a
cron that bumps, regenerates, commits, pushes and publishes unattended -- so an
upstream schema change that deletes a class ships as a patch by default, and the
only thing between that and a release is somebody reading a 4,616-file diff.

Tracked as https://github.com/CS2OpenDev/CS2OpenDev-SDK/issues/32.

What the surface model is
-------------------------
Every `public` (or `protected`) type declared under `src/CS2OpenDev.Sdk/`, keyed
by its namespace-qualified name, and per type the public members it declares,
keyed by name. A consumer binds to a type by name and to its members by name, so
losing either is a break the version number has to say out loud.

Three details are worth stating because they depart from `proto_surface.py`:

- Nested types are first-class entries, not folded into their parent. The
  proto gate folds, because a protobuf consumer names the top-level type. A C#
  consumer names the nested one: `SchemaNames.AABB.MaxBounds` is how the whole
  reverse-lookup table is reached, and 3,054 of this package's named types are
  nested static classes under `SchemaNames` and `SchemaEvents`. Folding would
  turn "`SchemaNames.AIBaseNPCDebugSnapshotData` was removed" into
  "`SchemaNames` lost 13 members", which is the wrong sentence.

- Generic types are keyed by CLR arity -- CHandle`1, in the backtick
  notation reflection uses -- rather than by `CHandle<T>`. Renaming a type
  parameter is not a break, and a model that reported it as one would be
  reporting noise on its first real run.

- Enum members carry their value. This is the direct analogue of the proto
  gate tracking field tags: an enum member silently renumbered is a wrong answer
  at runtime rather than a compile error, which is the worse of the two.

Member types are recorded as written -- `float?`, `string[]`,
`CHandle<CBaseEntity>` -- and never normalised. Nullability and array rank are
part of what a consumer compiles against, so a model that canonicalised them away
would be blind to a real category of break in exchange for tidier keys.

Why removals block and type changes only report
-----------------------------------------------
The 5.0 transition settles this. Its 1,923 property type changes were every one
of them a fix: schema 2.0 made an atomic's name fully templated, no templated
atomic matched any classification branch, and 1,923 properties were emitted as
empty stub classes for three majors. A gate that blocked type changes would have
blocked the repair, and the repair is the thing this repository exists to ship.

A removal is not ambiguous in the same way. A class or member that is gone is
gone, whatever the reason, and the only correct response is a MAJOR. So removals
withhold and type changes are reported loudly and proceed. The reporting is not
decoration -- 1,923 unremarked type changes is the incident, and a number printed
on the run summary is what makes the next one arguable before it ships rather
than after.

`diff` returns both halves separately so the caller decides; this module has no
opinion about exit codes.

Why the baseline is the last released tag
-----------------------------------------
Same reason as `proto_surface.py`: against the previous commit, a removal spread
over two cron cycles is two clean diffs and nobody sees it. Against the last
released tag it is one removal, which is what a consumer upgrading from the last
release will experience.

Where the acknowledgement lives
-------------------------------
The root `version.json`, not `src/CS2OpenDev.Sdk/version.json` -- which does
not exist, and a reader coming from the proto gate will expect it to. The root
file carries `"pathFilters": [":/src/CS2OpenDev.Sdk/"]`, so Nerdbank.GitVersioning
treats it as this package's version source and every other package gets its own
file. Bumping `MAJOR`/`MINOR` there is the human assertion that discharges a
removal, the same shape as the proto gate and the Schema Lens gates: a fact the
build cannot infer, asserted once, checked mechanically thereafter.

What it deliberately does not catch
-----------------------------------
This is a lexical model of the emitted source, not a compiler and not
`Microsoft.DotNet.ApiCompat` against two built assemblies. Known limits:

- Inheritance is not expanded. A type's entry holds only what its own file
  declares. 2,478 of the emitted types declare a base, and the members a derived
  type inherits are never re-declared in its own file. The base class is
  its own entry, so a member vanishing off a base *is* caught -- but it is
  reported against the base, and a member moved from a base to a derived class
  (or back) reads here as one removal plus one unrelated addition when nothing
  broke at all. The declared base list is recorded, so a swapped base is
  reported; what it resolves to is not followed.
- Attribute changes. `[NativeOffset]` moving, `[NativeName]` changing,
  `[EditorBrowsable]` appearing. Out of scope on purpose: those carry the native
  identity, which is independent of the C# projection a consumer compiles
  against, and they churn on every schema bump. `[EditorBrowsable(Never)]` is
  read, but only to describe removals in the report -- never to excuse one.
- Constant values. `SchemaNames.AABB.MaxBounds` is recorded as a `const
  string` named `MaxBounds`; that its value is `"m_vMaxBounds"` is not. A
  changed native identifier is invisible here, and nothing else checks it either
  -- `names.lock.json` pins the lowercase-run vocabulary that produces C# names,
  not the native strings those names map back to.
- Generic constraints (`where T : …`) and type-parameter variance.
- Members the compiler synthesises. A record's `Equals`, `GetHashCode`,
  `Deconstruct`, `<Clone>$` and `EqualityContract` exist on the shipped assembly
  and not in the source, so they are not modelled. Positional record parameters
  would land in the same hole; the emitter declares none today.
- Anything outside `src/CS2OpenDev.Sdk/`. `CS2OpenDev.Sdk.GameEvents`,
  `.Entities` and `.Entities.Abstractions` have their own `version.json` files
  and no gate of their own. The scope here matches the root `version.json`'s
  `pathFilters` exactly, so the surface and the version that acknowledges it
  cover the same files.
- The emitter's formatting. Declarations are found by indentation and one
  member per line, which is safe because this tree is generated and would not be
  safe on hand-written C#. A reformat of the emitter is invisible to the compiler
  and would blind this model -- and a blind model returns a clean diff and a
  green verdict, which is the worst way for a gate to fail.

  `SDK_SELFTEST_SHAPE` in `check-migration-readiness.py` catches a parser that
  stops seeing properties, but only against its own fixture, so it cannot see an
  emitter reformat at all. `SDK_SURFACE_FLOOR` is the one that can: it checks the
  size of the live extraction inside the gate itself, on every run. Until
  2026-09-01 this job belonged to a count pinned against the 4.1 -> 5.0 tags,
  which worked until those tags were deleted.

Member *reordering* is invisible, and that one is correct rather than a
limitation -- it is precisely what a surface model buys over a text diff.
"""

import re
import subprocess
from pathlib import Path
from typing import NamedTuple

SDK_ROOT = "src/CS2OpenDev.Sdk"

# Only these two are surface. `internal`/`private` are not, and the emitted tree
# declares none of them at member level -- verified, and if that changes the
# member is still correctly ignored.
_ACCESS = frozenset({"public", "protected"})

# Everything that can sit between the accessibility keyword and the thing being
# declared. `internal` is here as well as in _ACCESS for `protected internal`.
_MODIFIERS = frozenset({
    "internal", "static", "sealed", "abstract", "partial", "readonly", "ref",
    "new", "virtual", "override", "required", "unsafe", "extern", "volatile",
    "async", "const", "event", "implicit", "explicit", "fixed", "file",
})

_TYPE_KEYWORDS = frozenset({"class", "struct", "interface", "record", "enum"})

_NAMESPACE = re.compile(r"^namespace\s+([A-Za-z_][\w.]*)\s*[;{]")

# An enum member: `Foo`, `Foo = 3`, `Foo = -1,`. Deliberately strict, so a
# wrapped attribute argument or a stray expression cannot be mistaken for one.
_ENUM_MEMBER = re.compile(r"^([A-Za-z_]\w*)\s*(?:=\s*(-?\w+))?\s*,?$")

# An assignment `=`, as opposed to `==`, `=>`, `<=`, `!=` and friends.
_ASSIGN = re.compile(r"(?<![=!<>+\-*/%&|^])=(?![=>])")

_WHERE = re.compile(r"\swhere\s")


class Member(NamedTuple):
    """One declared member.

    `kind` is property / field / const / method / constructor / enum.
    `type` is the declared type text verbatim (a method's return type; empty for
    a constructor or an enum member). `extra` carries the accessor list for a
    property and the constant value for an enum member.
    """

    kind: str
    type: str
    extra: str


class TypeEntry(NamedTuple):
    """One declared type and the members it declares itself."""

    kind: str            # class | struct | record | interface | enum
    bases: str           # declared base/interface list, verbatim; "" when none
    hidden: bool         # carries [EditorBrowsable(EditorBrowsableState.Never)]
    members: dict[str, Member]


Surface = dict[str, TypeEntry]


def _split_top_level(text: str, sep: str = ",") -> list[str]:
    """Split on `sep`, ignoring separators nested in <>, () or []."""
    parts: list[str] = []
    depth = 0
    current: list[str] = []
    for ch in text:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == sep and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)
    parts.append("".join(current))
    return [p.strip() for p in parts if p.strip()]


def _balanced(text: str, start: int, open_ch: str, close_ch: str) -> str:
    """The body between `text[start]` (an opener) and its matching closer."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == open_ch:
            depth += 1
        elif text[i] == close_ch:
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
    return text[start + 1:]


def _split_name(decl: str) -> tuple[str, str]:
    """`Dictionary<string, int> Foo` -> ("Dictionary<string, int>", "Foo").

    The greedy leading group leaves the last identifier as the name, which is
    what C# declaration order guarantees.
    """
    m = re.fullmatch(r"(.+)\s+([A-Za-z_]\w*)", decl.strip())
    return (m.group(1).strip(), m.group(2)) if m else ("", decl.strip())


def _param_types(text: str) -> list[str]:
    """Parameter types only -- names are not part of what a caller binds to."""
    out = []
    for param in _split_top_level(text):
        param = _ASSIGN.split(param)[0].strip()       # drop `= default`
        tokens = param.split()
        out.append(" ".join(tokens[:-1]) if len(tokens) > 1 else param)
    return out


def _declarator_paren(decl: str) -> int:
    """Index of the parameter-list `(` in a declarator, or -1 if there is none.

    Not `decl.find("(")`, because C# spells tuple types with parentheses and the
    emitter uses them: `public (string, float)[] MorphCtrlWeightArray { get; set; }`
    is a property whose type happens to start with `(`. A parameter list is
    always preceded by the member's name, so the test is that an identifier
    character sits immediately to the left -- and that the `(` is not nested
    inside a generic argument list.

    Operator declarations are handled by the caller before this runs: their
    "name" is punctuation, so the identifier test would reject `operator ==(`,
    and `operator <(` would put the `(` at a phantom generic depth.
    """
    depth = 0
    for i, ch in enumerate(decl):
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth = max(0, depth - 1)
        elif ch == "(" and depth == 0:
            before = decl[:i].rstrip()
            if before and (before[-1].isalnum() or before[-1] == "_"):
                return i
    return -1


def _type_declaration(line: str) -> tuple[str, str, str] | None:
    """(kind, name-with-arity, base list) for a type declaration, else None."""
    tokens = line.split()
    if not tokens or tokens[0] not in _ACCESS:
        return None

    i = 0
    while i < len(tokens) and (tokens[i] in _ACCESS or tokens[i] in _MODIFIERS):
        i += 1
    if i >= len(tokens) or tokens[i] not in _TYPE_KEYWORDS:
        return None

    kind = tokens[i]
    i += 1
    if kind == "record" and i < len(tokens) and tokens[i] in ("class", "struct"):
        i += 1

    rest = _WHERE.split(" ".join(tokens[i:]))[0]

    # Split the base list off at the first `:` outside <>, () or [] -- and not
    # part of a `::` qualifier.
    depth = 0
    cut = len(rest)
    for j, ch in enumerate(rest):
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        elif ch == ":" and depth == 0 and rest[j:j + 2] != "::" and rest[j - 1:j] != ":":
            cut = j
            break

    head = rest[:cut].replace("{", " ").replace("}", " ").strip()
    bases = rest[cut + 1:].split("{")[0].strip() if cut < len(rest) else ""

    if "(" in head:                    # a positional record, if one ever appears
        head = head[:head.index("(")].strip()

    if "<" in head:
        base_name, _, args = head.partition("<")
        arity = len(_split_top_level(args.rstrip(">")))
        head = f"{base_name.strip()}`{arity}"

    return (kind, head, " ".join(bases.split())) if head else None


def _parse_member(line: str, owner: str) -> tuple[str, Member] | None:
    """(member key, Member) for a member declaration, else None.

    Methods, constructors and operators are keyed by name plus parameter types,
    so an overload set survives the round trip.
    """
    tokens = line.split()
    i = 0
    while i < len(tokens) and (tokens[i] in _ACCESS or tokens[i] in _MODIFIERS):
        i += 1
    is_const = "const" in tokens[:i]
    rest = " ".join(tokens[i:])
    if not rest:
        return None

    brace, arrow = rest.find("{"), rest.find("=>")
    # The declarator ends at the accessor block or the expression body, whichever
    # comes first. Looking for the parameter list past that point would find the
    # `(` in `=> new(InvalidValue);`.
    limit = min([c for c in (brace, arrow) if c >= 0] or [len(rest)])
    declarator = rest[:limit]

    operator = declarator.find(" operator ")
    paren = (
        declarator.find("(", operator) if operator >= 0
        else _declarator_paren(declarator)
    )

    if paren >= 0:
        head = rest[:paren].strip()
        signature = f"({','.join(_param_types(_balanced(rest, paren, '(', ')')))})"
        if " operator " in f" {head} ":
            ret, _, op = head.partition("operator")
            return f"operator {op.strip()}{signature}", Member("method", ret.strip(), "")
        ret, name = _split_name(head)
        if not ret and head.split("`")[0] == owner.split("`")[0]:
            return f"{head}{signature}", Member("constructor", "", "")
        return f"{name}{signature}", Member("method", ret, "")

    if brace >= 0 and brace == limit:
        decl = rest[:brace]
        accessors = " ".join(_balanced(rest, brace, "{", "}").split())
        kind = "property"
    elif arrow >= 0 and arrow == limit:
        decl = rest[:arrow]
        accessors = "get;"                          # expression-bodied property
        kind = "property"
    else:
        assign = _ASSIGN.search(rest)
        decl = rest[:assign.start()] if assign else rest.rstrip(";")
        accessors = ""
        kind = "const" if is_const else "field"

    type_text, name = _split_name(decl.rstrip(";").strip())
    return (name, Member(kind, type_text, accessors)) if name else None


def surface_of_text(text: str) -> Surface:
    """The surface a single `.cs` file contributes.

    Scope is tracked by indentation rather than braces. That is not a shortcut
    taken for lack of a better one: the `[NativeMetadata]` attribute carries
    serialised KV3 defaults containing literal `{` and `}` inside a string, so
    brace counting on this tree is actively wrong unless the scanner also
    tokenises string literals. Indentation is reliable here because the source is
    generated; it would not be on hand-written C#.
    """
    result: Surface = {}
    namespace = ""
    stack: list[tuple[int, str, str]] = []   # (indent, qualified name, kind)
    pending: list[str] = []                  # attributes seen since the last decl

    for raw in text.splitlines():
        line = raw.strip()
        if not line or line in ("{", "}") or line.startswith(("//", "#")):
            continue
        if line.startswith("["):
            pending.append(line)
            continue

        m = _NAMESPACE.match(line)
        if m:
            namespace = m.group(1)
            pending.clear()
            continue

        indent = len(raw) - len(raw.lstrip())
        while stack and indent <= stack[-1][0]:
            stack.pop()

        declaration = _type_declaration(line)
        if declaration is not None:
            kind, name, bases = declaration
            parent = stack[-1][1] if stack else namespace
            qualified = f"{parent}.{name}" if parent else name
            hidden = any("EditorBrowsable" in a and "Never" in a for a in pending)
            existing = result.get(qualified)
            if existing is None:
                # `enum : uint` folds the underlying type into the kind: it is
                # part of the declaration a consumer compiles against, and a
                # widening or narrowing of it deserves the same report a base
                # class swap gets.
                if kind == "enum" and bases:
                    kind = f"enum : {bases}"
                    bases = ""
                result[qualified] = TypeEntry(kind, bases, hidden, {})
            elif hidden and not existing.hidden:
                result[qualified] = existing._replace(hidden=True)
            stack.append((indent, qualified, result[qualified].kind))
            pending.clear()
            continue

        pending.clear()
        if not stack:
            continue

        _, owner, owner_kind = stack[-1]
        if owner_kind.startswith("enum"):
            m = _ENUM_MEMBER.match(line)
            if m:
                result[owner].members[m.group(1)] = Member("enum", "", m.group(2) or "")
            continue

        if line.split()[0] not in _ACCESS:
            continue
        parsed = _parse_member(line, owner.rsplit(".", 1)[-1])
        if parsed:
            result[owner].members[parsed[0]] = parsed[1]

    return result


def _merge(into: Surface, addition: Surface) -> None:
    """Fold one file's contribution in. Partial types split across files merge."""
    for name, entry in addition.items():
        existing = into.get(name)
        if existing is None:
            into[name] = entry
            continue
        existing.members.update(entry.members)
        if entry.hidden and not existing.hidden:
            into[name] = existing._replace(hidden=True)


def surface_from_disk(root: Path) -> Surface:
    """The surface of the working tree's emitted sources."""
    result: Surface = {}
    for path in sorted(root.rglob("*.cs")):
        _merge(result, surface_of_text(path.read_text(encoding="utf-8")))
    return result


def surface_from_ref(ref: str, root: str = SDK_ROOT) -> Surface:
    """The surface as of a git ref. Raises CalledProcessError if the ref is absent.

    One `git cat-file --batch` for the whole tree, rather than the proto gate's
    `git show` per file. That gate reads ~40 files; this one reads 4,616 on each
    side of every comparison, and a process spawn per file turns a two-second
    check into a two-minute one.
    """
    listing = subprocess.run(
        ["git", "ls-tree", "-r", ref, "--", root],
        capture_output=True, text=True, check=True,
    ).stdout.splitlines()

    shas = []
    for line in listing:
        meta, _, path = line.partition("\t")
        fields = meta.split()
        if path.endswith(".cs") and len(fields) >= 3 and fields[1] == "blob":
            shas.append(fields[2])
    if not shas:
        return {}

    blobs = subprocess.run(
        ["git", "cat-file", "--batch"],
        input=("\n".join(shas) + "\n").encode(), capture_output=True, check=True,
    ).stdout

    result: Surface = {}
    pos = 0
    while pos < len(blobs):
        end = blobs.index(b"\n", pos)
        size = int(blobs[pos:end].split()[2])
        body = blobs[end + 1:end + 1 + size]
        pos = end + 1 + size + 1              # +1 for the trailing newline
        _merge(result, surface_of_text(body.decode("utf-8")))
    return result


class Change(NamedTuple):
    type_name: str
    member: str
    before: Member
    after: Member


class SurfaceDiff(NamedTuple):
    """What moved between two surfaces.

    Removals are the blocking half; everything else is reported. Kept as flat
    lists so a caller can count, group and truncate without re-walking the
    surfaces.
    """

    removed_types: list[str]
    added_types: list[str]
    removed_members: list[tuple[str, str]]        # (type, member)
    added_members: list[tuple[str, str]]
    changed_members: list[Change]
    redeclared: list[tuple[str, str, str]]        # (type, before decl, after decl)


def diff(before: Surface, after: Surface) -> SurfaceDiff:
    removed_types = sorted(set(before) - set(after))
    added_types = sorted(set(after) - set(before))

    removed_members: list[tuple[str, str]] = []
    added_members: list[tuple[str, str]] = []
    changed_members: list[Change] = []
    redeclared: list[tuple[str, str, str]] = []

    for name in sorted(set(before) & set(after)):
        old, new = before[name], after[name]
        if (old.kind, old.bases) != (new.kind, new.bases):
            redeclared.append((
                name,
                f"{old.kind}{' : ' + old.bases if old.bases else ''}",
                f"{new.kind}{' : ' + new.bases if new.bases else ''}",
            ))
        removed_members.extend((name, m) for m in sorted(set(old.members) - set(new.members)))
        added_members.extend((name, m) for m in sorted(set(new.members) - set(old.members)))
        for member in sorted(set(old.members) & set(new.members)):
            if old.members[member] != new.members[member]:
                changed_members.append(
                    Change(name, member, old.members[member], new.members[member])
                )

    return SurfaceDiff(
        removed_types, added_types, removed_members,
        added_members, changed_members, redeclared,
    )


def property_type_changes(d: SurfaceDiff) -> list[Change]:
    """Changes where a surviving property's declared type moved.

    The 5.0 shape, and the one the gate reports rather than blocks. An accessor
    list changing under an unchanged type is a change too, and lands outside this
    list on purpose -- it is a different kind of break and deserves its own count.
    """
    return [
        c for c in d.changed_members
        if c.before.kind == "property" == c.after.kind and c.before.type != c.after.type
    ]


def removes_api(d: SurfaceDiff) -> bool:
    """True when something a consumer could have bound to is gone."""
    return bool(d.removed_types or d.removed_members)
