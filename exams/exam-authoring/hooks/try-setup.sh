#!/usr/bin/env bash
# Try setup: the try's container, with the proctor build of the docs value's revision (configuration-setup.sh)
# at .bundle/proctor in the checkout, beside the documentation proctor laid there, read-only. It sits inside the
# work mount since nb's file tools read nothing outside the working directory. What this leaves in the checkout
# (an empty mount point, or on a bare run a link to the build) is in the try's starting commit or invisible to
# git, so it is in no stage's diff.
set -euo pipefail
root=$(cd "$AUSPEX_EXAM_DIR/../.." && pwd)
rev=$(jq -r --arg v "$AUSPEX_AXIS_DOCS" '.axes.docs.values[$v].files.rev' "$AUSPEX_EXAM_DIR/exam.json")
build="$root/.auspex/auspex-builds/$rev"
[ -f "$build/proctor.dll" ] || { echo "no proctor build at $build: the configuration's setup did not run"; exit 1; }
mkdir -p "$AUSPEX_WORK/.bundle"
if [ -z "$AUSPEX_RUNNER" ]; then
  ln -sfn "$build" "$AUSPEX_WORK/.bundle/proctor"
  echo "bare run: nb stays on the host, no container; the build is linked at .bundle/proctor"; exit 0
fi
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
"$engine" rm -f "$AUSPEX_CONTAINER" >/dev/null 2>&1 || true
# The build's mount point is made here: a rootless engine cannot make it in a directory it does not own.
mkdir -p "$AUSPEX_WORK/.bundle/proctor"
args=(-d --name "$AUSPEX_CONTAINER"
      -v "$AUSPEX_WORK:$AUSPEX_WORK_MOUNT"
      -v "$build:$AUSPEX_WORK_MOUNT/.bundle/proctor:ro"
      -v "$AUSPEX_NB_CONFIG:/nb/config.json:ro"
      -v auspex-nuget:/home/agent/.nuget/packages
      -e LLM_GATEWAY -e LLM_GATEWAY_KEY)
# nb's config names the variables it reads (${VAR}) and the hosts it calls: the container gets both, whatever this machine calls them.
for var in $(grep -oE '\$\{[A-Za-z_][A-Za-z0-9_]*\}' "$AUSPEX_NB_CONFIG" | tr -d '${}' | sort -u); do args+=(-e "$var"); done
for host in $( { printf '%s\n' "${LLM_GATEWAY:-}"; grep -oE 'https?://[^/:"$ ]+' "$AUSPEX_NB_CONFIG"; } | sed -E 's#^[a-z]+://##; s#[:/].*##' | sort -u); do
  if ip=$(getent hosts "$host" | cut -d' ' -f1 | head -1) && [ -n "$ip" ]; then args+=(--add-host "$host:$ip"); fi
done
[ "$engine" = podman ] && args+=(--userns=keep-id)
"$engine" run "${args[@]}" auspex-dotnet sleep infinity >/dev/null
echo "$engine container $AUSPEX_CONTAINER: $AUSPEX_WORK at $AUSPEX_WORK_MOUNT, $build at $AUSPEX_WORK_MOUNT/.bundle/proctor"
