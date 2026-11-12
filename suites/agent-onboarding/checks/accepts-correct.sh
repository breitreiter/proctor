#!/usr/bin/env bash
# Does the written suite pass every planted run that gives the right reply? A run counts as passed only when it
# passed and was counted (a validity check that excludes a right reply did not accept it). Reason: the trial's
# problem, or the right replies that failed and the checks that failed them, which are the ones asking for more
# than the task does.
set -uo pipefail
. "$PROCTOR_SUITE_DIR/checks/written.sh"
"$PROCTOR_SUITE_DIR/checks/trial.sh"
out="${PROCTOR_CELL:-$PWD}/trial"; t="$out/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$out/trial.log" 2>/dev/null)"; exit 3; }
problem=$(jq -r '.problems[0] // empty' "$t")
[ -z "$problem" ] || { echo "$problem"; exit 1; }
right=$(jq '[.cells[] | select(.expect == "pass")] | length' "$t")
[ "$right" -gt 0 ] || { echo "no right reply to pass: $(jq -r .unrecognised "$PROCTOR_SUITE_DIR/trial/$PROCTOR_TASK/plants.json")"; exit 1; }
failed=$(jq -r '[.cells[] | select(.expect == "pass" and (.pass != true or .invalid != null))
  | "\(.variant) on \(.task): \(if .status != "completed" then .status elif .invalid != null then "excluded (\(.invalid))" else (.failed | join(", ")) end)"] | unique | .[]' "$t")
if [ -z "$failed" ]; then
  echo "passes all $right right replies ($(jq -r '[.cells[] | select(.expect == "pass") | .variant] | unique | join(", ")' "$t"))"; exit 0
fi
echo "fails $(grep -c . <<<"$failed") of $right right replies: $(head -3 <<<"$failed" | paste -sd';' - | sed 's/;/; /g' | cut -c1-300)"; exit 1
