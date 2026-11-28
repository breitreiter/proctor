#!/usr/bin/env bash
# Prove the fixtures and checks without a model or an agent: for each stage, make the repository the image makes
# (nb at the fixture's pin, plant.patch applied, one commit), apply the reference fix, collect the diff, and run
# every script check.
#   usage: shakedown.sh [--unsolved|--half] [stage...]
#   (no flag)    the references: every check passes
#   --unsolved   nothing changed: acceptance fails on every planted task and on the feature, and tests-green on the
#                two whose defect nb's own tests see (red-test, two-causes); not-a-bug passes, since its right
#                change is none
#   --half       two-causes with only its first defect fixed, not-a-bug "fixed" in code, and every other
#                reference with a test deleted: acceptance fails on the first two, tests-untouched on the rest
# Clones nb from the fixture's URL, or from NB_REPO (a local clone that holds the pin). Needs dotnet, git and jq.
# About twelve minutes for all six: each task builds nb and runs its 671 tests.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=; case "${1:-}" in --unsolved|--half) mode=$1; shift ;; -*) echo "usage: $0 [--unsolved|--half] [stage...]"; exit 2 ;; esac
only=" $* "
tmp=$(mktemp -d)
checks=(planted-present builds acceptance tests-green tests-untouched)
expected() {  # the outcome each mode should give a task's check
  case "$mode:$1:$2" in
    --unsolved:not-a-bug:*) echo pass ;;
    --unsolved:*:acceptance) echo fail ;;
    --unsolved:red-test:tests-green|--unsolved:two-causes:tests-green) echo fail ;;
    --half:two-causes:acceptance|--half:two-causes:tests-green|--half:not-a-bug:acceptance) echo fail ;;
    --half:two-causes:*|--half:not-a-bug:*) echo pass ;;
    --half:*:tests-untouched) echo fail ;;
    *) echo pass ;;
  esac
}
unexpected=0
# A stage belongs to the fixture its condition names: `when` is `<axis> = <value>`, and that value's `fixture`.
for c in $(jq -r '.stages[].id' "$here/exam.json"); do
  stage=$(jq -c --arg c "$c" '.stages[] | select(.id == $c)' "$here/exam.json")
  [ "$only" = "  " ] || [[ "$only" == *" $c "* ]] || continue
  f=$(jq -r --arg c "$c" '. as $e | .stages[] | select(.id == $c) | .when | capture("^(?<a>[a-z_-]+) = (?<v>.+)$") | $e.axes[.a].values[.v].fixture' "$here/exam.json")
  export AUSPEX_EXAM_DIR="$here" AUSPEX_FIXTURE="$root/fixtures/$f" AUSPEX_EXAM="$(basename "$here")" AUSPEX_CONFIGURATION=ref AUSPEX_STAGE="$c" AUSPEX_TRY_NUMBER=1
  export AUSPEX_ATTEMPT_DIR="$tmp/$c/attempt" AUSPEX_WORK="$tmp/$c/work" AUSPEX_DIFF="$tmp/$c/attempt/diff.patch"
  export AUSPEX_STAGE_JSON="$stage" AUSPEX_EXPECT="$(jq -c '.expect // {}' <<<"$stage")" AUSPEX_TRANSCRIPT=""
  mkdir -p "$AUSPEX_ATTEMPT_DIR"
  git -c safe.directory='*' clone -q "${NB_REPO:-$(jq -r .source.git "$AUSPEX_FIXTURE/fixture.json")}" "$AUSPEX_WORK" || exit 1
  (
    cd "$AUSPEX_WORK" && git checkout -q "$(jq -r .source.rev "$AUSPEX_FIXTURE/fixture.json")" && rm -rf .git
    [ ! -f "$AUSPEX_FIXTURE/plant.patch" ] || git apply "$AUSPEX_FIXTURE/plant.patch"
    git init -q && git add -A && git -c user.name=auspex -c user.email=auspex@localhost commit -qm nb
  ) || exit 1
  case "$mode:$c" in
    --unsolved:*) ;;
    --half:two-causes) (cd "$AUSPEX_WORK" && git apply --reverse "$here/reference/two-causes-first-only.patch") ;;
    --half:not-a-bug) echo '// inline includes' >>"$AUSPEX_WORK/nb.Core/Transcript/ProgramParser.cs" ;;
    --half:*) "$here/reference/$c.sh" "$AUSPEX_WORK"; rm "$AUSPEX_WORK/nb.Tests/TodoManagerTests.cs" ;;
    *) "$here/reference/$c.sh" "$AUSPEX_WORK" ;;
  esac
  (cd "$AUSPEX_WORK" && git add -A -N . && git diff --no-color --binary >"$AUSPEX_DIFF")
  for check in "${checks[@]}"; do
    reason=$(cd "$AUSPEX_ATTEMPT_DIR" && "$here/checks/$check.sh"); code=$?
    got=$([ $code = 0 ] && echo pass || echo fail)
    want=$(expected "$c" "$check")
    mark=$([ "$got" = "$want" ] && echo "" || echo " UNEXPECTED (want $want)")
    [ -z "$mark" ] || unexpected=$((unexpected + 1))
    printf '%-12s %-16s %-8s %s%s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$(cut -c1-150 <<<"$reason")" "$mark"
  done
  rm -rf "$AUSPEX_WORK"
done
rm -rf "$tmp"
if [ $unexpected -gt 0 ]; then echo "$unexpected check(s) did not come out as expected"; exit 1; fi
echo "every check came out as expected${mode:+ for $mode}"
