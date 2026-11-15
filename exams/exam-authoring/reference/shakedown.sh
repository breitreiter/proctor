#!/usr/bin/env bash
# Prove the trial without a model: for each task, check the workspace out the way proctor does, lay
# the reference exam over it, and run the checks that grade the written exam. Expected: every check
# passes. With --naive, each reference loses the check that makes it discriminate (the scope guard,
# the anchored answer and the read-only guard, the no-legacy script), and rejects-wrong must fail,
# naming the wrong runs that got through. With --unsolved, nothing is written and validates fails.
# continue-the-batch is proved by doing it: an interrupted batch, then `run` and `report`; continued and reported
# must pass, fail with --naive (the exam started over) and fail with --unsolved.
# Needs proctor built (dotnet build at the repository root), nb ($NB_PATH) and the proctor-dotnet image.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=${1:-}
nb="${NB_PATH:-$root/../nb/bin/Debug/net10.0/nb}"
tmp=$(mktemp -d)
for task_file in "$here"/tasks/*.json; do
  c=$(basename "$task_file" .json)
  f=$(jq -r .fixture "$task_file")
  id=$(jq -r .expect.exam "$task_file")
  export PROCTOR_EXAM_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_BATCH=shakedown PROCTOR_VARIANT=ref PROCTOR_TASK="$c" PROCTOR_ATTEMPT=1
  export PROCTOR_ATTEMPT_DIR="$tmp/$c/attempt" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_TASK_JSON="$(cat "$task_file")" PROCTOR_EXPECT="$(jq -c .expect "$task_file")"
  mkdir -p "$PROCTOR_ATTEMPT_DIR" "$PROCTOR_WORK"
  cp -r "$PROCTOR_FIXTURE/repo/." "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  checks="validates accepts-correct rejects-wrong describes-everything"
  if [ "$c" = continue-the-batch ]; then
    # The reference is an action, not files: the interrupted batch as attempt setup makes it, then `run` and `report`
    # on the host build. --naive starts the exam over instead of continuing it; --unsolved does nothing.
    p=(dotnet "$root/bin/Debug/net10.0/proctor.dll" --nb "$nb")
    "$here/hooks/interrupt.sh" "$PROCTOR_WORK" "$PROCTOR_WORK" "${p[@]}" >"$PROCTOR_ATTEMPT_DIR/interrupted-batch"
    [ "$mode" != --naive ] || rm -rf "$PROCTOR_WORK/attempts"
    [ "$mode" = --unsolved ] || { "${p[@]}" run "$id" --root "$PROCTOR_WORK" && "${p[@]}" report "$id" --root "$PROCTOR_WORK"; } >"$PROCTOR_ATTEMPT_DIR/solve.log" 2>&1
    checks="validates continued reported describes-everything"
  elif [ "$mode" != --unsolved ]; then cp -r "$here/reference/$c/." "$PROCTOR_WORK"; fi
  if [ "$mode" = --naive ] && [ "$c" != continue-the-batch ]; then
    e="$PROCTOR_WORK/exams/$id/exam.json"
    case "$c" in
      guard-the-tests)   jq '.grading.pass -= ["tests-untouched"]' "$e" ;;
      check-the-answer)  jq '.grading.checks["right-count"] = {answer_contains: "8"} | .grading.pass -= ["read-only"]' "$e" ;;
      write-the-fixture) jq '.grading.pass -= ["no-legacy"]' "$e" ;;
    esac >"$e.naive" && mv "$e.naive" "$e"
  fi
  for check in $checks; do
    reason=$(cd "$PROCTOR_ATTEMPT_DIR" && "$here/checks/$check.sh"); code=$?
    printf '%-18s %-21s %-8s %s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$reason"
  done
done
echo "trials kept in $tmp"
