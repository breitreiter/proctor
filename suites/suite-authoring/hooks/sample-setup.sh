#!/usr/bin/env bash
# Sample setup: the cell's container, as code-change's, except that /bundle is the arm's view (see
# arm-setup.sh) rather than the clone itself, read-only.
set -euo pipefail
[ -n "$PROCTOR_RUNNER" ] || { echo "bare run: nb stays on the host, no container"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
root=$(cd "$PROCTOR_SUITE_DIR/../.." && pwd)
view="$root/.proctor/views/$(git -C "$PROCTOR_BUNDLE" rev-parse HEAD)/$PROCTOR_ARM"
"$engine" rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true
args=(-d --name "$PROCTOR_CONTAINER"
      -v "$PROCTOR_WORK:$PROCTOR_WORK_MOUNT"
      -v "$view:$PROCTOR_BUNDLE_MOUNT:ro"
      -v "$PROCTOR_NB_CONFIG:/nb/config.json:ro"
      -v proctor-nuget:/home/agent/.nuget/packages
      -e LLM_GATEWAY -e LLM_GATEWAY_KEY)
gateway=$(printf '%s' "${LLM_GATEWAY:-}" | sed -E 's#^[a-z]+://##; s#[:/].*##')
if [ -n "$gateway" ] && ip=$(getent hosts "$gateway" | cut -d' ' -f1 | head -1) && [ -n "$ip" ]; then args+=(--add-host "$gateway:$ip"); fi
[ "$engine" = podman ] && args+=(--userns=keep-id)
"$engine" run "${args[@]}" proctor-dotnet sleep infinity >/dev/null
echo "$engine container $PROCTOR_CONTAINER: $PROCTOR_WORK at $PROCTOR_WORK_MOUNT, $view at $PROCTOR_BUNDLE_MOUNT"
