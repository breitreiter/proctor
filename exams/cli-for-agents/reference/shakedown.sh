#!/usr/bin/env bash
# Prove the fixture and the checks without a model: for each stage, check the fixture out the way
# auspex does, apply the reference solution (or nothing, with --unsolved), diff, run the checks.
# Expected: every check passes solved; builds and tests-pass pass and acceptance fails unsolved.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
solve=1; [ "${1:-}" = "--unsolved" ] && solve=0
tmp=$(mktemp -d)
f=$(jq -r .fixture "$here/exam.json")
for c in $(jq -r '.stages[].id' "$here/exam.json"); do
  stage=$(jq -c --arg c "$c" '.stages[] | select(.id == $c)' "$here/exam.json")
  export AUSPEX_EXAM_DIR="$here" AUSPEX_FIXTURE="$root/fixtures/$f" AUSPEX_EXAM="$(basename "$here")" AUSPEX_CONFIGURATION=ref AUSPEX_STAGE="$c" AUSPEX_TRY_NUMBER=1
  export AUSPEX_ATTEMPT_DIR="$tmp/$c/attempt" AUSPEX_WORK="$tmp/$c/work"
  export AUSPEX_STAGE_JSON="$stage" AUSPEX_EXPECT="$(jq -c .expect <<<"$stage")"
  export AUSPEX_TRANSCRIPT="$AUSPEX_ATTEMPT_DIR/transcript.jsonl" AUSPEX_DIFF="$AUSPEX_ATTEMPT_DIR/diff.patch"
  mkdir -p "$AUSPEX_ATTEMPT_DIR" "$AUSPEX_WORK"
  (cd "$AUSPEX_FIXTURE/repo" && find . -type d \( -name node_modules -o -name dist -o -name .git \) -prune -o -type f -print | cpio -pdm --quiet "$AUSPEX_WORK")
  (cd "$AUSPEX_WORK" && git init -q && git add -A && git -c user.name=auspex -c user.email=auspex@localhost commit -qm "fixture $f")
  [ $solve = 1 ] && "$here/reference/$c.sh" "$AUSPEX_WORK"
  (cd "$AUSPEX_WORK" && git add -A -N . && git diff --no-color > "$AUSPEX_DIFF")
  for check in "$AUSPEX_FIXTURE/checks/builds.sh" "$AUSPEX_FIXTURE/checks/tests-pass.sh" "$here/checks/acceptance.sh"; do
    reason=$(cd "$AUSPEX_ATTEMPT_DIR" && "$check"); code=$?
    printf '%-18s %-11s %s  %s\n' "$c" "$(basename "$check" .sh)" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$reason"
  done
  printf '%-18s %-11s %s\n' "$c" diff "$(grep '^diff --git' "$AUSPEX_DIFF" | sed 's/.* b\///' | tr '\n' ' ')"
done
rm -rf "$tmp"
