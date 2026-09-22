#!/usr/bin/env bash
# Sample setup: the cell's container, named for the cell, alive until sample-teardown.sh removes it.
# The checkout is mounted where the program told the model it is, nb's config read-only at the path
# runners/container.sh names, the key alone from the environment. Anything else the cell needs
# before nb starts (a seed file copied in, a fake started, a pod joined) goes at the end.
set -euo pipefail
[ -n "$PROCTOR_RUNNER" ] || { echo "bare run: nb stays on the host, no container"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
image="proctor-$(jq -r .stack "$PROCTOR_FIXTURE/fixture.json")"      # one image per fixture stack
"$engine" rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true            # a resumed cell starts clean
args=(-d --name "$PROCTOR_CONTAINER"
      -v "$PROCTOR_WORK:$PROCTOR_WORK_MOUNT"
      -v "$PROCTOR_NB_CONFIG:/nb/config.json:ro"
      -v proctor-nuget:/home/agent/.nuget/packages
      --add-host "imp:$(getent hosts imp | cut -d' ' -f1)"             # the model's box, as /etc/hosts knows it
      -e MINROUTER_KEY)
[ -n "$PROCTOR_BUNDLE" ] && args+=(-v "$PROCTOR_BUNDLE:$PROCTOR_BUNDLE_MOUNT:ro")
[ "$engine" = podman ] && args+=(--userns=keep-id)                     # rootless: the caller's uid inside, so the checkout stays theirs
"$engine" run "${args[@]}" "$image" sleep infinity >/dev/null
echo "$engine container $PROCTOR_CONTAINER from $image: $PROCTOR_WORK at $PROCTOR_WORK_MOUNT"
