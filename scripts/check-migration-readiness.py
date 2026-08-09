#!/usr/bin/env python3
"""Gate the scheduled submodule bump on the SDK actually being able to ship it.

Why this exists
---------------
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

Exit codes: 0 ready to bump, 1 not ready (or the schema could not be read).
"""

import json
import sys

SCHEMA = "upstream/docs/generated/downstream-codegen-schemas/cs2_schema.json"
ISSUE = "https://github.com/CS2OpenDev/CS2OpenDev-SchemaTracker/issues/1"


def fail(msg: str) -> int:
    # ::error:: renders in the Actions log and on the run summary.
    print(f"::error::{msg}")
    return 1


def main() -> int:
    try:
        with open(SCHEMA, encoding="utf-8") as fh:
            schema = json.load(fh)
    except FileNotFoundError:
        return fail(f"{SCHEMA} not found — is the upstream submodule initialised?")
    except json.JSONDecodeError as exc:
        return fail(f"{SCHEMA} is not valid JSON: {exc}")

    declared = str(schema.get("schema_format_version", ""))
    major = declared.split(".")[0]

    enums = schema.get("enums", [])
    if not enums:
        return fail(f"{SCHEMA} declares no enums — refusing to treat that as ready.")

    # The namespace key. 1.x put the project in `module`; 2.0 moved it to
    # `projectName` and repurposed `module` for the binary. Classes carry the
    # new key, enums do not yet.
    if major == "1":
        attributed = sum(1 for e in enums if e.get("module"))
        key = "module"
    else:
        attributed = sum(1 for e in enums if e.get("projectName"))
        key = "projectName"

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


if __name__ == "__main__":
    sys.exit(main())
