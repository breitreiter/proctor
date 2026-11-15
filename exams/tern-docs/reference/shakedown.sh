#!/usr/bin/env bash
# Prove the fixture and checks without a model or an agent: for each task, make the checkout the image makes (the
# fixture's repo/ as one commit), apply a mode, collect the diff, and run every script check.
#   usage: shakedown.sh [--unsolved|--v1|--kilograms] [task...]
#   (no flag)     the reference: every check passes
#   --unsolved    nothing changed: acceptance fails everywhere; the rest pass
#   --v1          the reference with quotes on the retired /v1 in kilograms, as the restructured Python quickstart
#                 shows: acceptance still passes, current-endpoint fails (on every task, since the reference is one whole file)
#   --kilograms   the reference with quotes on /v2 but in kilograms: acceptance fails on the two quote tasks
# Needs python3 with pytest, git, curl and jq. About ten seconds.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
mode=; case "${1:-}" in --unsolved|--v1|--kilograms) mode=$1; shift ;; -*) echo "usage: $0 [--unsolved|--v1|--kilograms] [task...]"; exit 2 ;; esac
only=" $* "
tmp=$(mktemp -d)
# The image's python is `python`; the host's may be python3 only.
mkdir -p "$tmp/bin"; ln -s "$(command -v python3)" "$tmp/bin/python"; export PATH="$tmp/bin:$PATH"
checks=(acceptance tests-pass tests-untouched current-endpoint)
expected() {
  case "$mode:$1:$2" in
    --unsolved:*:acceptance) echo fail ;;
    --v1:*:current-endpoint) echo fail ;;
    --kilograms:quote-*:acceptance) echo fail ;;
    *) echo pass ;;
  esac
}
unexpected=0
for task_file in "$here"/tasks/*.json; do
  c=$(basename "$task_file" .json)
  [ "$only" = "  " ] || [[ "$only" == *" $c "* ]] || continue
  fixture="$root/fixtures/$(jq -r .fixture "$task_file")"
  export PROCTOR_EXAM_DIR="$here" PROCTOR_FIXTURE="$fixture" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_TASK="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work" PROCTOR_DIFF="$tmp/$c/cell/diff.patch"
  export PROCTOR_TASK_JSON="$(cat "$task_file")" PROCTOR_EXPECT="{}" PROCTOR_TRANSCRIPT=""
  mkdir -p "$PROCTOR_CELL"; cp -r "$fixture/repo" "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm fixture) || exit 1
  case "$mode" in
    --unsolved) ;;
    --v1) "$here/reference/solve.sh" "$PROCTOR_WORK" --v1 ;;
    --kilograms) "$here/reference/solve.sh" "$PROCTOR_WORK"; sed -i 's|"weight": _grams(cart)}|"weight": cart.total_weight_kg()}|' "$PROCTOR_WORK/shop/shipping.py" ;;
    *) "$here/reference/solve.sh" "$PROCTOR_WORK" ;;
  esac
  (cd "$PROCTOR_WORK" && git add -A -N . && git diff --no-color --binary >"$PROCTOR_DIFF")
  for check in "${checks[@]}"; do
    script="$here/checks/$check.sh"; [ -f "$script" ] || script="$fixture/checks/$check.sh"
    reason=$(cd "$PROCTOR_CELL" && "$script"); code=$?
    got=$([ $code = 0 ] && echo pass || echo fail)
    want=$(expected "$c" "$check")
    mark=$([ "$got" = "$want" ] && echo "" || echo " UNEXPECTED (want $want)")
    [ -z "$mark" ] || unexpected=$((unexpected + 1))
    printf '%-21s %-17s %-8s %s%s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$(cut -c1-130 <<<"$reason")" "$mark"
  done
done
rm -rf "$tmp"
if [ $unexpected -gt 0 ]; then echo "$unexpected check(s) did not come out as expected"; exit 1; fi
echo "every check came out as expected${mode:+ for $mode}"
