#!/usr/bin/env bash
# Does the written suite pass every planted correct run (trial/<task>/good-*)? Reason: the checks
# that failed a correct run, which are the ones asking for more than the task does.
set -uo pipefail
"$PROCTOR_SUITE_DIR/checks/trial.sh"
t="$PROCTOR_CELL/trial/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$PROCTOR_CELL/trial/trial.log" 2>/dev/null)"; exit 3; }
err=$(jq -r '.error // empty' "$t"); [ -z "$err" ] || { echo "$err"; exit 1; }
failed=$(jq -r '.arms | to_entries[] | select(.key | startswith("good-")) | .key as $arm | .value[] | select(.pass != true)
  | "\($arm) on \(.task): \(if .status != "completed" then .status else ([.checks // {} | to_entries[] | select(.value != "pass") | "\(.key) \(.value)"] | join(", ")) end)"' "$t")
if [ -z "$failed" ]; then echo "passes $(jq -r '[.arms | keys[] | select(startswith("good-"))] | join(", ")' "$t")"; exit 0; fi
echo "fails a correct run: $(paste -sd';' <<<"$failed")"; exit 1
