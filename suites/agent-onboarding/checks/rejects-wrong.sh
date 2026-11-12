#!/usr/bin/env bash
# Does the written suite fail every planted run that gives a wrong reply? A wrong reply is rejected only when its
# run completed, counted and failed: one the suite left undecided, excluded, or never ran got through. Reason:
# the trial's problem, the wrong replies that got through, or which checks caught each kind.
set -uo pipefail
. "$PROCTOR_SUITE_DIR/checks/written.sh"
"$PROCTOR_SUITE_DIR/checks/trial.sh"
out="${PROCTOR_CELL:-$PWD}/trial"; t="$out/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$out/trial.log" 2>/dev/null)"; exit 3; }
problem=$(jq -r '.problems[0] // empty' "$t")
[ -z "$problem" ] || { echo "$problem"; exit 1; }
wrong=$(jq '[.cells[] | select(.expect == "fail")] | length' "$t")
[ "$wrong" -gt 0 ] || { echo "no wrong reply to reject: $(jq -r .unrecognised "$PROCTOR_SUITE_DIR/trial/$PROCTOR_TASK/plants.json")"; exit 1; }
through=$(jq -r '[.cells[] | select(.expect == "fail" and (.status != "completed" or .invalid != null or .pass != false))]
  | group_by(.variant) | map("\(.[0].variant) on \(length) task(s) (\([.[].task] | unique | join(", ")))") | .[]' "$t")
if [ -n "$through" ]; then
  echo "lets through $(paste -sd';' <<<"$through" | sed 's/;/; /g' | cut -c1-300) (of $wrong wrong replies)"; exit 1
fi
echo "rejects all $wrong: $(jq -r '[.cells[] | select(.expect == "fail")] | group_by(.variant)
  | map("\(.[0].variant) by \([.[].failed[] | split(" ")[0]] | unique | join("+"))") | join("; ")' "$t")"
