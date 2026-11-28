#!/usr/bin/env bash
# Does the written exam pass every planted correct run (trial/<stage>/good-*)? Reason: the checks
# that failed a correct run, which are the ones asking for more than the stage does.
set -uo pipefail
"$AUSPEX_EXAM_DIR/checks/trial.sh"
t="$AUSPEX_ATTEMPT_DIR/trial/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$AUSPEX_ATTEMPT_DIR/trial/trial.log" 2>/dev/null)"; exit 3; }
err=$(jq -r '.error // empty' "$t"); [ -z "$err" ] || { echo "$err"; exit 1; }
good=$(jq '[.plants | keys[] | select(startswith("good-"))] | length' "$t")
[ "$good" -gt 0 ] || { echo "no correct run was made: $(tail -1 "$AUSPEX_ATTEMPT_DIR/trial/run.log" 2>/dev/null | cut -c1-160)"; exit 1; }
failed=$(jq -r '.plants | to_entries[] | select(.key | startswith("good-")) | .key as $plant | .value[] | select(.pass != true)
  | "\($plant) on \(.stage): \(if .status != "completed" then .status else ([.checks // {} | to_entries[] | select(.value != "pass") | "\(.key) \(.value)"] | join(", ")) end)"' "$t")
if [ -z "$failed" ]; then echo "passes $(jq -r '[.plants | keys[] | select(startswith("good-"))] | join(", ")' "$t")"; exit 0; fi
echo "fails a correct run: $(paste -sd';' <<<"$failed")"; exit 1
