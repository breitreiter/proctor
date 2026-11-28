#!/usr/bin/env bash
# Not a check: accepts-correct.sh and rejects-wrong.sh both call it. Runs the trial (trial/run.sh) once per stage
# and keeps it under trial/: in the attempt on the host, beside the reward in Harbor's verifier (whose working
# directory is its log directory, and which has no attempt). The verifier is offline and is its own container,
# so the scripts the agent wrote run there and nowhere else.
set -euo pipefail
. "$AUSPEX_EXAM_DIR/checks/written.sh"
out="${AUSPEX_ATTEMPT_DIR:-$PWD}/trial"
mkdir -p "$out"
exec 9>"$out/.lock"; flock 9
[ -f "$out/trial.json" ] && exit 0
TRIAL_TASK="$AUSPEX_STAGE" TRIAL_OUT="$out" WRITTEN_ROOT="$WRITTEN_ROOT" WRITTEN_ID="$WRITTEN_ID" \
  bash "$AUSPEX_EXAM_DIR/trial/run.sh" >"$out/trial.log" 2>&1
