#!/usr/bin/env bash
# Does the written exam fail every planted run that gives a wrong reply? A wrong reply is rejected only when its
# run completed, counted and failed: one the exam left undecided, excluded, or never ran got through. Reason:
# the trial's problem, the wrong replies that got through, or which checks caught each kind.
set -uo pipefail
. "$AUSPEX_EXAM_DIR/checks/written.sh"
"$AUSPEX_EXAM_DIR/checks/trial.sh"
out="${AUSPEX_ATTEMPT_DIR:-$PWD}/trial"; t="$out/trial.json"
[ -f "$t" ] || { echo "the trial did not run: $(tail -1 "$out/trial.log" 2>/dev/null)"; exit 3; }
problem=$(jq -r '.problems[0] // empty' "$t")
[ -z "$problem" ] || { echo "$problem"; exit 1; }
wrong=$(jq '[.runs[] | select(.expect == "fail")] | length' "$t")
[ "$wrong" -gt 0 ] || { echo "no wrong reply to reject: $(jq -r .unrecognised "$AUSPEX_EXAM_DIR/trial/$AUSPEX_STAGE/plants.json")"; exit 1; }
through=$(jq -r '[.runs[] | select(.expect == "fail" and (.status != "completed" or .invalid != null or .pass != false))]
  | group_by(.plant) | map("\(.[0].plant) on \(length) stage(s) (\([.[].stage] | unique | join(", ")))") | .[]' "$t")
if [ -n "$through" ]; then
  echo "lets through $(paste -sd';' <<<"$through" | sed 's/;/; /g' | cut -c1-300) (of $wrong wrong replies)"; exit 1
fi
echo "rejects all $wrong: $(jq -r '[.runs[] | select(.expect == "fail")] | group_by(.plant)
  | map("\(.[0].plant) by \([.[].failed[] | split(" ")[0]] | unique | join("+"))") | join("; ")' "$t")"
