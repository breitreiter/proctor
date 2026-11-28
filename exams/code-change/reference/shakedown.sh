#!/usr/bin/env bash
# Prove the fixtures and checks without a model: for each stage, check the fixture out the way
# proctor does, apply the reference solution (or nothing, with --unsolved), diff, run the checks.
# Expected: every check passes solved; builds passes and tests-pass or acceptance fails unsolved.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
solve=1; [ "${1:-}" = "--unsolved" ] && solve=0
tmp=$(mktemp -d)
# A stage belongs to the fixture its condition names: `when` is `<axis> = <value>`, and that value's `fixture`.
for c in $(jq -r '.stages[].id' "$here/exam.json"); do
  stage=$(jq -c --arg c "$c" '.stages[] | select(.id == $c)' "$here/exam.json")
  f=$(jq -r --arg c "$c" '. as $e | .stages[] | select(.id == $c) | .when | capture("^(?<a>[a-z_-]+) = (?<v>.+)$") | $e.axes[.a].values[.v].fixture' "$here/exam.json")
  export PROCTOR_EXAM_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXAM="$(basename "$here")" PROCTOR_CONFIGURATION=ref PROCTOR_STAGE="$c" PROCTOR_TRY_NUMBER=1
  export PROCTOR_ATTEMPT_DIR="$tmp/$c/attempt" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_STAGE_JSON="$stage" PROCTOR_EXPECT="$(jq -c .expect <<<"$stage")"
  export PROCTOR_TRANSCRIPT="$PROCTOR_ATTEMPT_DIR/transcript.jsonl" PROCTOR_DIFF="$PROCTOR_ATTEMPT_DIR/diff.patch"
  mkdir -p "$PROCTOR_ATTEMPT_DIR" "$PROCTOR_WORK"
  (cd "$PROCTOR_FIXTURE/repo" && find . -type d \( -name bin -o -name obj -o -name .git \) -prune -o -type f -print | cpio -pdm --quiet "$PROCTOR_WORK")
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  [ $solve = 1 ] && "$here/reference/$c.sh" "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git add -A -N . && git diff --no-color > "$PROCTOR_DIFF")
  for check in "$PROCTOR_FIXTURE/checks/builds.sh" "$PROCTOR_FIXTURE/checks/tests-pass.sh" "$here/checks/acceptance.sh"; do
    reason=$(cd "$PROCTOR_ATTEMPT_DIR" && "$check"); code=$?
    printf '%-16s %-11s %s  %s\n' "$c" "$(basename "$check" .sh)" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$reason"
  done
  printf '%-16s %-11s %s\n' "$c" diff "$(grep '^diff --git' "$PROCTOR_DIFF" | sed 's/.* b\///' | tr '\n' ' ')"
done
rm -rf "$tmp"
