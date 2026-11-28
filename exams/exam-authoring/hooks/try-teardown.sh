#!/usr/bin/env bash
# Try teardown: remove the try's container. Runs whenever setup ran, even when setup failed, so
# it is idempotent. Anything to pull out of the container (a fake's log, a file nb left outside
# the checkout) goes into $PROCTOR_ATTEMPT_DIR/hooks/ before the rm.
set -uo pipefail
[ -n "$PROCTOR_RUNNER" ] || exit 0
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
"$engine" rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true
echo "removed $PROCTOR_CONTAINER"
