#!/usr/bin/env bash
# Prove the fixtures and checks without a model or an agent: for each task, make the repository the image makes
# (nb at the fixture's pin, plant.patch applied, one commit), apply the reference fix, collect the diff, and run
# every script check.
#   usage: shakedown.sh [--unsolved|--half] [task...]
#   (no flag)    the references: every check passes
#   --unsolved   nothing changed: acceptance fails on every planted task and on the feature, and suite-green on the
#                two whose defect nb's own tests see (red-test, two-causes); not-a-bug passes, since its right
#                change is none
#   --half       two-causes with only its first defect fixed, not-a-bug "fixed" in code, and every other
#                reference with a test deleted: acceptance fails on the first two, tests-untouched on the rest
# Clones nb from the fixture's URL, or from NB_REPO (a local clone that holds the pin). Needs dotnet, git and jq.
# About twelve minutes for all six: each task builds nb and runs its 671 tests.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=; case "${1:-}" in --unsolved|--half) mode=$1; shift ;; -*) echo "usage: $0 [--unsolved|--half] [task...]"; exit 2 ;; esac
only=" $* "
tmp=$(mktemp -d)
checks=(planted-present builds acceptance suite-green tests-untouched)
expected() {  # the outcome each mode should give a task's check
  case "$mode:$1:$2" in
    --unsolved:not-a-bug:*) echo pass ;;
    --unsolved:*:acceptance) echo fail ;;
    --unsolved:red-test:suite-green|--unsolved:two-causes:suite-green) echo fail ;;
    --half:two-causes:acceptance|--half:two-causes:suite-green|--half:not-a-bug:acceptance) echo fail ;;
    --half:two-causes:*|--half:not-a-bug:*) echo pass ;;
    --half:*:tests-untouched) echo fail ;;
    *) echo pass ;;
  esac
}
unexpected=0
for task_file in "$here"/tasks/*.json; do
  c=$(basename "$task_file" .json)
  [ "$only" = "  " ] || [[ "$only" == *" $c "* ]] || continue
  f=$(jq -r .fixture "$task_file")
  export PROCTOR_SUITE_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_TASK="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work" PROCTOR_DIFF="$tmp/$c/cell/diff.patch"
  export PROCTOR_TASK_JSON="$(cat "$task_file")" PROCTOR_EXPECT="$(jq -c '.expect // {}' "$task_file")" PROCTOR_TRANSCRIPT=""
  mkdir -p "$PROCTOR_CELL"
  git -c safe.directory='*' clone -q "${NB_REPO:-$(jq -r .source.git "$PROCTOR_FIXTURE/fixture.json")}" "$PROCTOR_WORK" || exit 1
  (
    cd "$PROCTOR_WORK" && git checkout -q "$(jq -r .source.rev "$PROCTOR_FIXTURE/fixture.json")" && rm -rf .git
    [ ! -f "$PROCTOR_FIXTURE/plant.patch" ] || git apply "$PROCTOR_FIXTURE/plant.patch"
    git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm nb
  ) || exit 1
  case "$mode:$c" in
    --unsolved:*) ;;
    --half:two-causes) (cd "$PROCTOR_WORK" && git apply --reverse "$here/reference/two-causes-first-only.patch") ;;
    --half:not-a-bug) echo '// inline includes' >>"$PROCTOR_WORK/nb.Core/Transcript/ProgramParser.cs" ;;
    --half:*) "$here/reference/$c.sh" "$PROCTOR_WORK"; rm "$PROCTOR_WORK/nb.Tests/TodoManagerTests.cs" ;;
    *) "$here/reference/$c.sh" "$PROCTOR_WORK" ;;
  esac
  (cd "$PROCTOR_WORK" && git add -A -N . && git diff --no-color --binary >"$PROCTOR_DIFF")
  for check in "${checks[@]}"; do
    reason=$(cd "$PROCTOR_CELL" && "$here/checks/$check.sh"); code=$?
    got=$([ $code = 0 ] && echo pass || echo fail)
    want=$(expected "$c" "$check")
    mark=$([ "$got" = "$want" ] && echo "" || echo " UNEXPECTED (want $want)")
    [ -z "$mark" ] || unexpected=$((unexpected + 1))
    printf '%-12s %-16s %-8s %s%s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$(cut -c1-150 <<<"$reason")" "$mark"
  done
  rm -rf "$PROCTOR_WORK"
done
rm -rf "$tmp"
if [ $unexpected -gt 0 ]; then echo "$unexpected check(s) did not come out as expected"; exit 1; fi
echo "every check came out as expected${mode:+ for $mode}"
