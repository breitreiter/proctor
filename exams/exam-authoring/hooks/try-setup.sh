#!/usr/bin/env bash
# Try setup: the try's container, with the proctor build of the docs value's revision (configuration-setup.sh)
# at .bundle/proctor in the checkout, beside the documentation proctor laid there, read-only. It sits inside the
# work mount since nb's file tools read nothing outside the working directory. What this leaves in the checkout
# (an empty mount point, or on a bare run a link to the build) is in the try's starting commit or invisible to
# git, so it is in no stage's diff.
set -euo pipefail
root=$(cd "$PROCTOR_EXAM_DIR/../.." && pwd)
rev=$(jq -r --arg v "$PROCTOR_AXIS_DOCS" '.axes.docs.values[$v].files.rev' "$PROCTOR_EXAM_DIR/exam.json")
build="$root/.proctor/proctor-builds/$rev"
[ -f "$build/proctor.dll" ] || { echo "no proctor build at $build: the configuration's setup did not run"; exit 1; }
mkdir -p "$PROCTOR_WORK/.bundle"
if [ -z "$PROCTOR_RUNNER" ]; then
  ln -sfn "$build" "$PROCTOR_WORK/.bundle/proctor"
  echo "bare run: nb stays on the host, no container; the build is linked at .bundle/proctor"; exit 0
fi
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
"$engine" rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true
# The build's mount point is made here: a rootless engine cannot make it in a directory it does not own.
mkdir -p "$PROCTOR_WORK/.bundle/proctor"
args=(-d --name "$PROCTOR_CONTAINER"
      -v "$PROCTOR_WORK:$PROCTOR_WORK_MOUNT"
      -v "$build:$PROCTOR_WORK_MOUNT/.bundle/proctor:ro"
      -v "$PROCTOR_NB_CONFIG:/nb/config.json:ro"
      -v proctor-nuget:/home/agent/.nuget/packages
      -e LLM_GATEWAY -e LLM_GATEWAY_KEY)
# nb's config names the variables it reads (${VAR}) and the hosts it calls: the container gets both, whatever this machine calls them.
for var in $(grep -oE '\$\{[A-Za-z_][A-Za-z0-9_]*\}' "$PROCTOR_NB_CONFIG" | tr -d '${}' | sort -u); do args+=(-e "$var"); done
for host in $( { printf '%s\n' "${LLM_GATEWAY:-}"; grep -oE 'https?://[^/:"$ ]+' "$PROCTOR_NB_CONFIG"; } | sed -E 's#^[a-z]+://##; s#[:/].*##' | sort -u); do
  if ip=$(getent hosts "$host" | cut -d' ' -f1 | head -1) && [ -n "$ip" ]; then args+=(--add-host "$host:$ip"); fi
done
[ "$engine" = podman ] && args+=(--userns=keep-id)
"$engine" run "${args[@]}" proctor-dotnet sleep infinity >/dev/null
echo "$engine container $PROCTOR_CONTAINER: $PROCTOR_WORK at $PROCTOR_WORK_MOUNT, $build at $PROCTOR_WORK_MOUNT/.bundle/proctor"
