#!/usr/bin/env bash
# Prove the trial without a model: for each stage, check the workspace out the way proctor does, lay
# the reference exam over it, and run the checks that grade the written exam. Expected: every check
# passes. With --naive, each reference loses the check that makes it discriminate (the scope guard,
# the anchored answer and the read-only guard, the no-legacy script), and rejects-wrong must fail,
# naming the wrong runs that got through. With --unsolved, nothing is written and every check fails.
# Needs proctor built (dotnet build at the repository root) and the proctor-dotnet image
# (exams/runners/Containerfile, which a run's configuration setup builds).
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=${1:-}
case "$mode" in ""|--naive|--unsolved) ;; *) echo "usage: $0 [--naive|--unsolved]"; exit 2 ;; esac
tmp=$(mktemp -d)
def="$here/exam.json"
unexpected=0
for c in $(jq -r '.stages[].id' "$def"); do
  stage=$(jq -c --arg c "$c" '.stages[] | select(.id == $c)' "$def")
  workspace=$(jq -r '.when | sub("^workspace = "; "")' <<<"$stage")
  f=$(jq -r --arg w "$workspace" '.axes.workspace.values[$w].fixture' "$def")
  id=$(jq -r .expect.exam <<<"$stage")
  export PROCTOR_EXAM_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXAM="$(basename "$here")" PROCTOR_STAGE="$c" PROCTOR_TRY_NUMBER=1
  export PROCTOR_CONFIGURATION="workspace=$workspace,docs=ref,assistant=ref" PROCTOR_AXIS_WORKSPACE="$workspace"
  export PROCTOR_ATTEMPT_DIR="$tmp/$c/attempt" PROCTOR_WORK="$tmp/$c/work"
  export PROCTOR_STAGE_JSON="$stage" PROCTOR_EXPECT="$(jq -c .expect <<<"$stage")"
  mkdir -p "$PROCTOR_ATTEMPT_DIR" "$PROCTOR_WORK"
  cp -r "$PROCTOR_FIXTURE/repo/." "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  checks="validates accepts-correct rejects-wrong describes-everything"
  [ "$mode" = --unsolved ] || cp -r "$here/reference/$c/." "$PROCTOR_WORK"
  if [ "$mode" = --naive ]; then
    e="$PROCTOR_WORK/exams/$id/exam.json"
    case "$c" in
      guard-the-tests)   jq '.grading.correctness -= ["tests-untouched"]' "$e" ;;
      check-the-answer)  jq '.grading.checks["right-count"] = {answer_contains: "8"} | .grading.correctness -= ["read-only"]' "$e" ;;
      write-the-fixture) jq '.grading.correctness -= ["no-legacy"]' "$e" ;;
    esac >"$e.naive" && mv "$e.naive" "$e"
  fi
  for check in $checks; do
    reason=$(cd "$PROCTOR_ATTEMPT_DIR" && "$here/checks/$check.sh"); code=$?
    got=$([ $code = 0 ] && echo pass || echo fail)
    case "$mode:$check" in :*) want=pass ;; --naive:rejects-wrong) want=fail ;; --naive:*) want=pass ;; *) want=fail ;; esac
    mark=""; [ "$got" = "$want" ] || { mark=" UNEXPECTED (want $want)"; unexpected=$((unexpected + 1)); }
    printf '%-18s %-21s %-8s %s%s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$(cut -c1-170 <<<"$reason")" "$mark"
  done
done
echo "trials kept in $tmp"
if [ $unexpected -gt 0 ]; then echo "$unexpected check(s) did not come out as expected"; exit 1; fi
echo "every check came out as expected${mode:+ for $mode}"
