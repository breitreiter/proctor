#!/usr/bin/env bash
# Not a check: accepts-correct.sh and rejects-wrong.sh both call it. Runs the trial (trial/run.sh)
# for this cell in a container without a network, once; the result stays in the cell under trial/
# and a regrade reuses it. The scripts the agent wrote run in there, never on the host. The trial
# grades with this repository's own build of proctor, the same for every arm.
set -euo pipefail
out="$PROCTOR_CELL/trial"
exec 9>"$PROCTOR_CELL/trial.lock"; flock 9
[ -f "$out/trial.json" ] && exit 0
rm -rf "$out" && mkdir -p "$out"
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
keepid=(); [ "$engine" = podman ] && keepid=(--userns=keep-id)
grader=$(cd "$PROCTOR_SUITE_DIR/../../bin/Debug/net10.0" && pwd)
"$engine" run --rm --network none "${keepid[@]}" -e TRIAL_TASK="$PROCTOR_TASK" \
  -v "$PROCTOR_WORK:/authored:ro" -v "$PROCTOR_SUITE_DIR/trial:/trial:ro" -v "$grader:/opt/proctor:ro" \
  -v proctor-nuget:/home/agent/.nuget/packages -v "$out:/out" \
  proctor-dotnet bash /trial/run.sh "$(jq -r .suite <<<"$PROCTOR_EXPECT")" >"$out/trial.log" 2>&1
