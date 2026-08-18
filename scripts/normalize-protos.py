#!/usr/bin/env python3
"""Stage the demo/engine protobuf subset out of the SchemaTracker submodule.

CS2's shipped `.proto` files carry no `package` statement and no
`option csharp_namespace`, so protoc drops every generated type into the global
C# namespace — a CS0433 collision hazard for any consumer that also references
another protobuf assembly. protoc has no command-line override for this; the
option has to be physically present in the file.

The submodule is upstream-tracked and must not be edited in place, so this
script copies the subset into `protos/` with the option injected. That directory
is committed, and CI re-runs this script and fails on a diff — the same
regenerate-and-compare gate the SDK sources use.

Why a subset rather than all 40 files: the full set does not compile as one
assembly. Two independent symbol collisions exist in Valve's own descriptors
(proto2 enum values are siblings of their type, so they must be globally
unique):

    enums_clientserver.proto  k_EMsgGCSystemMessage  vs base_gcmessages.proto
    steammessages_base.proto  CMsgProtoBufHeader     vs steammessages.proto

So the curation is a real design decision, made once here rather than
rediscovered by every consumer through protoc errors.

Usage
-----
    python3 scripts/normalize-protos.py            # stage into protos/
    python3 scripts/normalize-protos.py --check    # verify protos/ is current
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[1]
TRACKER = REPO / "schema-tracker"
OUT = REPO / "protos"

# The C# namespace every generated type lands in. This is permanent public API
# for the CS2OpenDev.Protos package — changing it is a breaking change for every
# consumer, so it is deliberately not configurable.
CSHARP_NAMESPACE = "CS2OpenSchema.Protos"

# windows-x86_64 is the superset platform: CS2 ships client and dedicated-server
# binaries together per OS, and the Windows depot additionally carries the
# tool-side modules Linux has no binaries for.
PLATFORM = "windows-x86_64"

# Roots of the demo/engine wire path. Everything a `.dem` parser needs to read
# packets, entities, user messages and game events. The transitive import
# closure over these is what gets staged; the GC/Steam matchmaking families are
# reachable only through cstrike15_usermessages and come along for that reason
# alone.
ROOTS = [
    "demo.proto",
    "netmessages.proto",
    "usermessages.proto",
    "gameevents.proto",
    "cs_gameevents.proto",
    "cstrike15_usermessages.proto",
    "te.proto",
    "usercmd.proto",
    "cs_usercmd.proto",
    "clientmessages.proto",
    "networkbasetypes.proto",
]

IMPORT_RE = re.compile(r'^import(?: public| weak)? "([^"]+)";', re.MULTILINE)
SYNTAX_RE = re.compile(r'^(syntax\s*=\s*"proto[23]"\s*;)$', re.MULTILINE)


def tracker_protos() -> pathlib.Path:
    latest = TRACKER / "LATEST.json"
    if not latest.exists():
        sys.exit(
            "schema-tracker submodule is not initialised.\n"
            "  git submodule update --init --depth 1 schema-tracker"
        )
    build_id = json.loads(latest.read_text(encoding="utf-8"))["build_id"]
    path = TRACKER / "artifacts" / str(build_id) / PLATFORM / "protos"
    if not path.is_dir():
        sys.exit(f"no protos for build {build_id} / {PLATFORM} at {path}")
    return path


def closure(src: pathlib.Path) -> list[str]:
    """Transitive import closure over ROOTS, excluding well-known types.

    google/protobuf/* is deliberately excluded: Grpc.Tools puts the well-known
    protos on protoc's include path, so vendoring copies would shadow them and
    risk a version skew against the Google.Protobuf runtime we bind to.
    """
    seen: set[str] = set()
    queue = list(ROOTS)
    while queue:
        name = queue.pop()
        if name in seen or name.startswith("google/"):
            continue
        path = src / name
        if not path.exists():
            sys.exit(f"{name} is imported but not present in {src}")
        seen.add(name)
        queue.extend(IMPORT_RE.findall(path.read_text(encoding="utf-8")))
    return sorted(seen)


def inject(text: str, name: str) -> str:
    """Insert the csharp_namespace option immediately after the syntax line.

    Anything above `syntax` is left alone — SchemaTracker stamps a provenance
    header on the eight wire-message families CS2 embeds in no binary, and that
    header is worth keeping.
    """
    if "csharp_namespace" in text:
        sys.exit(f"{name} already declares csharp_namespace; upstream changed shape")
    match = SYNTAX_RE.search(text)
    if not match:
        sys.exit(f"{name} has no recognisable syntax statement")
    option = f'\noption csharp_namespace = "{CSHARP_NAMESPACE}";\n'
    return text[: match.end()] + option + text[match.end() :]


def verify(staged: pathlib.Path, files: list[str]) -> None:
    """Compile the staged set from a directory containing only that set.

    protoc resolves imports against anything on -I, so a subset that "works"
    inside the full 40-file directory may not be import-closed. Isolating it is
    the only way to prove the package will build for a consumer.
    """
    protoc = shutil.which("protoc")
    if not protoc:
        print("  protoc not found — skipping isolation check", file=sys.stderr)
        return
    with tempfile.TemporaryDirectory() as tmp:
        result = subprocess.run(
            [protoc, f"-I{staged}", f"--descriptor_set_out={tmp}/out.pb", *files],
            capture_output=True,
            text=True,
            check=False,
        )
        errors = [ln for ln in result.stderr.splitlines() if "warning" not in ln.lower()]
        if result.returncode != 0 or errors:
            print("\n".join(errors), file=sys.stderr)
            sys.exit("staged subset does not compile in isolation")
        print(f"  protoc: {len(files)} files compile in isolation")


def stage(src: pathlib.Path, files: list[str], dest: pathlib.Path, provenance: dict) -> None:
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    for name in files:
        (dest / name).write_text(
            inject((src / name).read_text(encoding="utf-8"), name), encoding="utf-8"
        )
    # Committed alongside the protos so the package can stamp build provenance
    # without the submodule being initialised — a consumer building from a
    # source archive, or a `dotnet pack` on a machine that only checked out the
    # superproject, still gets a correctly identified assembly.
    (dest / "PROVENANCE.json").write_text(
        json.dumps(provenance, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="fail if protos/ differs from a fresh stage")
    args = ap.parse_args()

    src = tracker_protos()
    files = closure(src)
    latest = json.loads((TRACKER / "LATEST.json").read_text(encoding="utf-8"))
    build_id = latest["build_id"]
    print(f"build {build_id} / {PLATFORM}: {len(files)} files in the demo/engine closure")

    provenance = {
        "build_id": build_id,
        "platform": PLATFORM,
        "csharp_namespace": CSHARP_NAMESPACE,
        "generated_utc": latest.get("generated_utc"),
        "source_commit": latest.get("source_commit"),
        "source_repo": "https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker",
        "roots": sorted(ROOTS),
        "files": files,
    }

    target = pathlib.Path(tempfile.mkdtemp()) / "protos" if args.check else OUT
    stage(src, files, target, provenance)
    verify(target, files)

    if args.check:
        diff = subprocess.run(
            ["diff", "-r", "-q", str(OUT), str(target)], capture_output=True, text=True, check=False
        )
        if diff.returncode != 0:
            print(diff.stdout + diff.stderr, file=sys.stderr)
            sys.exit("protos/ is stale — re-run scripts/normalize-protos.py and commit")
        print("  protos/ is current")
    else:
        print(f"  staged into {OUT.relative_to(REPO)}/")


if __name__ == "__main__":
    main()
