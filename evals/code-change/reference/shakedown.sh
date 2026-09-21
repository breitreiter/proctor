#!/usr/bin/env bash
# Prove the fixtures and checks without a model: for each case, check the fixture out the way
# proctor does, apply the reference solution (or nothing, with --unsolved), diff, run the checks.
# Expected: every check passes solved; builds passes and tests-pass or acceptance fails unsolved.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
solve=1; [ "${1:-}" = "--unsolved" ] && solve=0
tmp=$(mktemp -d)
for case_file in "$here"/cases/*.json; do
  c=$(basename "$case_file" .json)
  f=$(jq -r .fixture "$case_file")
  export PROCTOR_EVAL_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_CASE="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_CASE_JSON="$(cat "$case_file")" PROCTOR_EXPECT="$(jq -c .expect "$case_file")"
  export PROCTOR_TRANSCRIPT="$PROCTOR_CELL/transcript.jsonl" PROCTOR_DIFF="$PROCTOR_CELL/diff.patch"
  mkdir -p "$PROCTOR_CELL" "$PROCTOR_WORK"
  (cd "$PROCTOR_FIXTURE/repo" && find . -type d \( -name bin -o -name obj -o -name .git \) -prune -o -type f -print | cpio -pdm --quiet "$PROCTOR_WORK")
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  [ $solve = 1 ] && "$here/reference/$c.sh" "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git add -A -N . && git diff --no-color > "$PROCTOR_DIFF")
  for check in "$PROCTOR_FIXTURE/checks/builds.sh" "$PROCTOR_FIXTURE/checks/tests-pass.sh" "$here/checks/acceptance.sh"; do
    reason=$(cd "$PROCTOR_CELL" && "$check"); code=$?
    printf '%-16s %-11s %s  %s\n' "$c" "$(basename "$check" .sh)" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$reason"
  done
  printf '%-16s %-11s %s\n' "$c" diff "$(grep '^diff --git' "$PROCTOR_DIFF" | sed 's/.* b\///' | tr '\n' ' ')"
done
rm -rf "$tmp"
