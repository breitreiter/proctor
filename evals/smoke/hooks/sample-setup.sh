#!/usr/bin/env bash
# The fixture is already checked out in $PROCTOR_WORK; a real eval would start its fakes here.
set -euo pipefail
echo "sample $PROCTOR_ARM/$PROCTOR_CASE/$PROCTOR_SAMPLE setup; work has $(ls "$PROCTOR_WORK" | tr '\n' ' ')"
# A real eval that runs nb in a container would create it here as $PROCTOR_CONTAINER, and skip that when $PROCTOR_RUNNER is empty (a bare run).
echo "runner=$PROCTOR_RUNNER container=$PROCTOR_CONTAINER work_mount=$PROCTOR_WORK_MOUNT"
