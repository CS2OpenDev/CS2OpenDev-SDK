#!/usr/bin/env bash
# Platform selection cases for normalize-protos.py, run against a fake tracker.
# The script resolves the repo from its own path, so each case copies it into a
# scratch tree with its own schema-tracker/ and protos/.
set -euo pipefail

SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/normalize-protos.py"
ROOTS="demo netmessages usermessages gameevents cs_gameevents cstrike15_usermessages te usercmd cs_usercmd clientmessages networkbasetypes"
fails=0

# make_repo <dir> <platforms-json> <platform dirs...>
make_repo() {
  local dir=$1 listed=$2; shift 2
  mkdir -p "$dir/scripts" "$dir/schema-tracker"
  cp "$SCRIPT" "$dir/scripts/"
  printf '{"build_id": 1, "platforms": %s, "source_commit": "x", "generated_utc": "t"}\n' \
    "$listed" > "$dir/schema-tracker/LATEST.json"
  for p in "$@"; do
    local protos="$dir/schema-tracker/artifacts/1/$p/protos"
    mkdir -p "$protos"
    for r in $ROOTS; do printf 'syntax = "proto2";\n' > "$protos/$r.proto"; done
  done
}

# expect <name> <want-exit> <want-platform-or-message> <dir>
expect() {
  local name=$1 want_rc=$2 want=$3 dir=$4 rc=0 out
  out=$(python3 "$dir/scripts/normalize-protos.py" 2>&1) || rc=$?
  local got=""
  if [ "$rc" -eq 0 ]; then
    got=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['platform'])" \
      "$dir/protos/PROVENANCE.json")
  fi
  if [ "$rc" -ne "$want_rc" ] || { [ "$rc" -eq 0 ] && [ "$got" != "$want" ]; } \
     || { [ "$rc" -ne 0 ] && ! grep -qF -- "$want" <<<"$out"; }; then
    echo "FAIL $name: rc=$rc platform=$got"; echo "$out" | sed 's/^/    /'
    fails=$((fails + 1))
  else
    echo "ok   $name"
  fi
}

tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT

make_repo "$tmp/both" '["linux-x86_64", "windows-x86_64"]' linux-x86_64 windows-x86_64
expect "both platforms stage from windows" 0 windows-x86_64 "$tmp/both"

make_repo "$tmp/linux" '["linux-x86_64"]' linux-x86_64
expect "linux-only build falls back" 0 linux-x86_64 "$tmp/linux"
warn=$(python3 "$tmp/linux/scripts/normalize-protos.py" 2>&1)
grep -qF '::warning::' <<<"$warn" \
  || { echo "FAIL linux-only build does not warn"; fails=$((fails + 1)); }

make_repo "$tmp/unlisted" '["windows-x86_64"]' linux-x86_64
expect "unlisted platform is not used" 1 "no protos for build 1" "$tmp/unlisted"

make_repo "$tmp/none" '[]'
expect "no platform at all fails" 1 "no protos for build 1" "$tmp/none"

make_repo "$tmp/divergent" '["linux-x86_64"]' linux-x86_64
d="$tmp/divergent/schema-tracker/artifacts/1/linux-x86_64/protos"
printf 'syntax = "proto2";\nimport "enums_clientserver.proto";\n' > "$d/demo.proto"
printf 'syntax = "proto2";\n' > "$d/enums_clientserver.proto"
expect "fallback refuses a platform-divergent closure" 1 "differ between platforms" "$tmp/divergent"

[ "$fails" -eq 0 ] || { echo "$fails case(s) failed"; exit 1; }
echo "all cases passed"
