#!/usr/bin/env bash
# Prove the trial without a model: for each task, check the workspace out the way proctor does, lay
# the reference suite over it, and run the checks that grade the written suite. Expected: every check
# passes. With --naive, each reference loses the check that makes it discriminate (the scope guard,
# the anchored answer and the read-only guard, the no-legacy script), and rejects-wrong must fail,
# naming the wrong runs that got through. With --unsolved, nothing is written and validates fails.
# Needs proctor built (dotnet build at the repository root) and the proctor-dotnet image.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=${1:-}
tmp=$(mktemp -d)
for task_file in "$here"/tasks/*.json; do
  c=$(basename "$task_file" .json)
  f=$(jq -r .fixture "$task_file")
  id=$(jq -r .expect.suite "$task_file")
  export PROCTOR_SUITE_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_TASK="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_TASK_JSON="$(cat "$task_file")" PROCTOR_EXPECT="$(jq -c .expect "$task_file")"
  mkdir -p "$PROCTOR_CELL" "$PROCTOR_WORK"
  cp -r "$PROCTOR_FIXTURE/repo/." "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  if [ "$mode" != --unsolved ]; then cp -r "$here/reference/$c/." "$PROCTOR_WORK"; fi
  if [ "$mode" = --naive ]; then
    e="$PROCTOR_WORK/suites/$id/suite.json"
    case "$c" in
      guard-the-tests)   jq '.grading.pass -= ["tests-untouched"]' "$e" ;;
      check-the-answer)  jq '.grading.checks["right-count"] = {answer_contains: "8"} | .grading.pass -= ["read-only"]' "$e" ;;
      write-the-fixture) jq '.grading.pass -= ["no-legacy"]' "$e" ;;
    esac >"$e.naive" && mv "$e.naive" "$e"
  fi
  for check in validates accepts-correct rejects-wrong describes-everything; do
    reason=$(cd "$PROCTOR_CELL" && "$here/checks/$check.sh"); code=$?
    printf '%-18s %-21s %-8s %s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$reason"
  done
done
echo "trials kept in $tmp"
