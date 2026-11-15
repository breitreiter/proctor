#!/usr/bin/env bash
# Attempt setup: the attempt's container, as code-change's was, except that the bundle mount is the variant's view
# (see variant-setup.sh: the bundle and a proctor build) rather than the bundle itself, read-only. exam.json puts it inside the work mount,
# since nb's file tools read nothing outside the working directory; on the host that leaves an empty
# mount point in the checkout, which git does not see.
# continue-the-batch starts from an interrupted batch, made here by the proctor the agent is shown (interrupt.sh).
set -euo pipefail
root=$(cd "$PROCTOR_EXAM_DIR/../.." && pwd)
view="$root/.proctor/views/$(basename "$PROCTOR_BUNDLE")"
interrupt() { [ "$PROCTOR_TASK" != continue-the-batch ] || "$PROCTOR_EXAM_DIR/hooks/interrupt.sh" "$PROCTOR_WORK" "$@" >"$PROCTOR_ATTEMPT_DIR/interrupted-batch"; }
if [ -z "$PROCTOR_RUNNER" ]; then
  interrupt "$PROCTOR_WORK" dotnet "$view/proctor/proctor.dll" --nb "$PROCTOR_NB"
  echo "bare run: nb stays on the host, no container"; exit 0
fi
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
"$engine" rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true
# The bundle mount sits inside the work mount, so its mount point is made here: a rootless engine cannot make it in a directory it does not own.
case "$PROCTOR_BUNDLE_MOUNT" in "$PROCTOR_WORK_MOUNT"/*) mkdir -p "$PROCTOR_WORK/${PROCTOR_BUNDLE_MOUNT#"$PROCTOR_WORK_MOUNT"/}" ;; esac
args=(-d --name "$PROCTOR_CONTAINER"
      -v "$PROCTOR_WORK:$PROCTOR_WORK_MOUNT"
      -v "$view:$PROCTOR_BUNDLE_MOUNT:ro"
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
echo "$engine container $PROCTOR_CONTAINER: $PROCTOR_WORK at $PROCTOR_WORK_MOUNT, $view at $PROCTOR_BUNDLE_MOUNT"
interrupt "$PROCTOR_WORK_MOUNT" "$engine" exec "$PROCTOR_CONTAINER" dotnet "$PROCTOR_BUNDLE_MOUNT/proctor/proctor.dll"
