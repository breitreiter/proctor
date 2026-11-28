#!/usr/bin/env bash
# Does the written exam fail every planted wrong run (trial/<stage>/bad-*)? Reason: which wrong runs
# got through, or which check caught each one. An undecided wrong run was not caught.
set -uo pipefail
"$PROCTOR_EXAM_DIR/checks/trial.sh"
t="$PROCTOR_ATTEMPT_DIR/trial/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$PROCTOR_ATTEMPT_DIR/trial/trial.log" 2>/dev/null)"; exit 3; }
err=$(jq -r '.error // empty' "$t"); [ -z "$err" ] || { echo "$err"; exit 1; }
through=$(jq -r '[.plants | to_entries[] | select(.key | startswith("bad-")) | select(any(.value[]; .pass != false)) | .key] | join(", ")' "$t")
total=$(jq '[.plants | keys[] | select(startswith("bad-"))] | length' "$t")
[ "$total" -gt 0 ] || { echo "no wrong run was made: $(tail -1 "$PROCTOR_ATTEMPT_DIR/trial/run.log" 2>/dev/null | cut -c1-160)"; exit 1; }
if [ -n "$through" ]; then echo "lets through $through (of $total wrong runs)"; exit 1; fi
echo "rejects all $total: $(jq -r '[.plants | to_entries[] | select(.key | startswith("bad-"))
  | "\(.key) by \([.value[].checks // {} | to_entries[] | select(.value == "fail") | .key] | unique | join("+"))"] | join("; ")' "$t")"
