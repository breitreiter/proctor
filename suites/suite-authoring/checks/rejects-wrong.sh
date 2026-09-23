#!/usr/bin/env bash
# Does the written suite fail every planted wrong run (trial/<task>/bad-*)? Reason: which wrong runs
# got through, or which check caught each one. An undecided wrong run was not caught.
set -uo pipefail
"$PROCTOR_SUITE_DIR/checks/trial.sh"
t="$PROCTOR_CELL/trial/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$PROCTOR_CELL/trial/trial.log" 2>/dev/null)"; exit 3; }
err=$(jq -r '.error // empty' "$t"); [ -z "$err" ] || { echo "$err"; exit 1; }
through=$(jq -r '[.arms | to_entries[] | select(.key | startswith("bad-")) | select(any(.value[]; .pass != false)) | .key] | join(", ")' "$t")
total=$(jq '[.arms | keys[] | select(startswith("bad-"))] | length' "$t")
if [ -n "$through" ]; then echo "lets through $through (of $total wrong runs)"; exit 1; fi
echo "rejects all $total: $(jq -r '[.arms | to_entries[] | select(.key | startswith("bad-"))
  | "\(.key) by \([.value[].checks // {} | to_entries[] | select(.value == "fail") | .key] | unique | join("+"))"] | join("; ")' "$t")"
