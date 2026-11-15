#!/usr/bin/env bash
# The fixture is already checked out in $PROCTOR_WORK; a real exam would start its fakes here.
set -euo pipefail
echo "attempt $PROCTOR_VARIANT/$PROCTOR_TASK/$PROCTOR_ATTEMPT setup; work has $(ls "$PROCTOR_WORK" | tr '\n' ' ')"
# A real exam that runs nb in a container would create it here as $PROCTOR_CONTAINER, and skip that when $PROCTOR_RUNNER is empty (a bare run).
echo "runner=$PROCTOR_RUNNER container=$PROCTOR_CONTAINER work_mount=$PROCTOR_WORK_MOUNT"
