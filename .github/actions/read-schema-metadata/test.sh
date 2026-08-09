#!/usr/bin/env bash
#
# Tests for read-schema-metadata/action.yml.
#
# This action is worth testing for one specific reason: its failure mode was not
# a failed run but a silent wrong one. Given a schema 2.0 header, the previous
# version exited 0 having read the walker-identity string out of `revision` and
# handed back `hl2sdk-cs2/5f891c90…/v1/3d1200e3…` as the value that goes into a
# SemVer 2 build-metadata identifier. Nothing downstream would have caught it —
# `dotnet pack` takes whatever it is given. The `2.0-no-build-id` case below is
# that exact regression.
#
# The logic under test is extracted from the shipped `run:` block rather than
# copied here, so a test cannot pass against a version of the script that is not
# the one in the action.
#
# Run: bash .github/actions/read-schema-metadata/test.sh
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ACTION="$HERE/action.yml"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

python3 - "$ACTION" > "$WORK/parse.sh" <<'PY'
import sys

lines = open(sys.argv[1]).read().splitlines()
start = next(i for i, l in enumerate(lines) if l.strip() == 'run: |') + 1
body = []
for line in lines[start:]:
    # The block ends at the first non-blank line back at a shallower indent.
    if line.strip() and not line.startswith('        '):
        break
    body.append(line[8:] if line.startswith('        ') else line)
print('\n'.join(body))
PY

FAILED=0

# Runs the extracted script against a synthetic cs2_schema.json and checks the
# exit code, and the emitted revision when one is expected.
run_case() {
  local name="$1" json="$2" want_rc="$3" want_rev="${4:-}"
  local dir="$WORK/$name"
  mkdir -p "$dir/upstream/docs/generated/downstream-codegen-schemas"
  printf '%s' "$json" > "$dir/upstream/docs/generated/downstream-codegen-schemas/cs2_schema.json"
  : > "$dir/out"

  ( cd "$dir" && GITHUB_OUTPUT="$dir/out" bash "$WORK/parse.sh" ) > "$dir/log" 2>&1
  local rc=$?
  local rev date
  rev=$(sed -nE 's/^revision=(.*)$/\1/p' "$dir/out")
  date=$(sed -nE 's/^date-iso=(.*)$/\1/p' "$dir/out")

  if [ "$rc" != "$want_rc" ]; then
    echo "FAIL $name: exit $rc, want $want_rc"
    sed 's/^/       /' "$dir/log"
    FAILED=1
    return
  fi
  if [ -n "$want_rev" ] && [ "$rev" != "$want_rev" ]; then
    echo "FAIL $name: revision '$rev', want '$want_rev'"
    FAILED=1
    return
  fi
  echo "ok   $name (exit $rc, revision '$rev', date '$date')"
}

# Schema 1.x: numeric `revision`, "Mon DD YYYY" date. This is what the pinned
# submodule still serves, so it must keep working unchanged.
run_case format-1.x \
  '{"schema_format_version":"1.1","revision":10677034,"version_date":"May 21 2026","classes":[]}' \
  0 10677034

# Schema 2.0: numeric `build_id`, ISO date, and a `revision` that must be
# ignored. Reading `revision` here is the bug this action was rewritten for.
run_case format-2.0 \
  '{"schema_format_version":"2.0","generator":"x","build_id":24537688,"platform":"windows-x86_64","revision":"hl2sdk-cs2/5f891c9026230cce0fc0a3fc4b5fef1c467a1385/v1/3d1200e346019c59","version_date":"2026-08-03","version_time":"2026-08-03T18:18:10Z","classes":[]}' \
  0 24537688

# The regression case: 2.0 shape with `build_id` dropped, i.e. an upstream
# passthrough regression. Must fail closed rather than fall back to `revision`.
run_case 2.0-no-build-id \
  '{"schema_format_version":"2.0","revision":"hl2sdk-cs2/5f891c90/v1/3d1200e3","version_date":"2026-08-03","classes":[]}' \
  1

# No version_date at all. This is the case that failed on the runner and
# passed locally: GNU date reads "" as "now", so the action emitted today's
# date as if it had read it from the header. BSD date rejects "", so macOS
# never saw it. Keep it.
run_case missing-date \
  '{"schema_format_version":"2.0","build_id":24537688,"classes":[]}' \
  1

run_case unparseable-date \
  '{"schema_format_version":"2.0","build_id":24537688,"version_date":"sometime last tuesday","classes":[]}' \
  1

# The other half of the same problem: GNU date accepts relative expressions, so
# a junk value can parse into a real, plausible-looking date instead of failing.
run_case relative-date \
  '{"schema_format_version":"2.0","build_id":24537688,"version_date":"last tuesday","classes":[]}' \
  1

# A date shape neither upstream has ever emitted. GNU date parses this happily;
# it is still not a header we know how to read.
run_case unknown-date-shape \
  '{"schema_format_version":"2.0","build_id":24537688,"version_date":"08/03/2026","classes":[]}' \
  1

# `build_id` beyond the 512-byte head window, which is how the jq fallback gets
# exercised. A header that grows past the heuristic must still resolve.
PAD=$(python3 -c 'print("x" * 600)')
run_case jq-fallback \
  "{\"schema_format_version\":\"2.0\",\"pad\":\"$PAD\",\"build_id\":24537688,\"version_date\":\"2026-08-03\",\"classes\":[]}" \
  0 24537688

echo
if [ "$FAILED" = 0 ]; then
  echo "read-schema-metadata: all cases pass"
else
  echo "read-schema-metadata: FAILURES"
fi
exit "$FAILED"
