#!/usr/bin/env bash
# Prove the hooks and checks without a model: for each case, reset the fixture, apply
# the reference solution (or nothing, with --unsolved), collect the diff, run the checks.
# Expected: every check passes solved; builds passes and tests-pass fails unsolved.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
solve=1; [ "${1:-}" = "--unsolved" ] && solve=0
tmp=$(mktemp -d)
for case_file in "$here"/cases/*.json; do
  c=$(basename "$case_file" .json)
  export PROCTOR_EVAL_DIR="$here" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_CASE="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_CASE_JSON="$(cat "$case_file")" PROCTOR_EXPECT="$(jq -c .expect "$case_file")"
  export PROCTOR_TRANSCRIPT="$PROCTOR_CELL/transcript.jsonl" PROCTOR_DIFF="$PROCTOR_CELL/diff.patch"
  mkdir -p "$PROCTOR_CELL"
  "$here/hooks/reset-fixture.sh" >/dev/null || { echo "$c: reset failed"; continue; }
  [ $solve = 1 ] && "$here/reference/$c.sh" "$PROCTOR_WORK"
  "$here/hooks/collect-diff.sh" >/dev/null
  for check in builds tests-pass; do
    reason=$(cd "$PROCTOR_CELL" && "$here/checks/$check.sh"); code=$?
    printf '%-16s %-11s exit %d  %s\n' "$c" "$check" "$code" "$reason"
  done
  printf '%-16s %-11s %s\n' "$c" diff "$(grep '^diff --git' "$PROCTOR_DIFF" | sed 's/.* b\///' | tr '\n' ' ')"
done
rm -rf "$tmp"
