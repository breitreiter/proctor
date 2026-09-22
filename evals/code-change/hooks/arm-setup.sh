#!/usr/bin/env bash
# Arm setup: the model on imp; then, when the cells run in containers, the image they run in and
# a warm package cache, so no cell pays for the first restore and no cell's timing includes it.
# The image is identified in this log, since proctor records nothing about the container itself.
set -euo pipefail
"$PROCTOR_EVAL_DIR/hooks/ensure-model.sh"
[ -n "$PROCTOR_RUNNER" ] || { echo "bare run: no image"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
runners="$PROCTOR_EVAL_DIR/../runners"
"$engine" build -q -t proctor-dotnet --build-arg "UID=$(id -u)" --build-arg "GID=$(id -g)" -f "$runners/Containerfile" "$runners" >/dev/null
echo "image proctor-dotnet $("$engine" image inspect --format '{{.Id}}' proctor-dotnet) on nb $("$engine" image inspect --format '{{.Id}}' nb:latest)"
keepid=(); [ "$engine" = podman ] && keepid=(--userns=keep-id)
for fixture in $(jq -r .fixture "$PROCTOR_EVAL_DIR"/cases/*.json | sort -u); do
  "$engine" run --rm "${keepid[@]}" -v "$PROCTOR_EVAL_DIR/../../fixtures/$fixture/repo:/src:ro" -v proctor-nuget:/home/agent/.nuget/packages proctor-dotnet \
    bash -c 'cp -r /src /tmp/w && find /tmp/w -name "*.csproj" -exec dotnet restore -v q {} \; >/dev/null' && echo "restored $fixture into proctor-nuget"
done
