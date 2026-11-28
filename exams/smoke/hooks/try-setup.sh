#!/usr/bin/env bash
# Runs once before a try's first stage. The fixture is already checked out in $PROCTOR_WORK, with the values' files
# laid over it; a real exam would start its fakes here. What this leaves is the state every stage starts from.
set -euo pipefail
echo "try $PROCTOR_VARIANT/$PROCTOR_ATTEMPT setup; work has $(ls "$PROCTOR_WORK" | tr '\n' ' ')"
# A real exam that runs nb in a container would create it here as $PROCTOR_CONTAINER, and skip that when $PROCTOR_RUNNER is empty (a bare run).
echo "runner=$PROCTOR_RUNNER container=$PROCTOR_CONTAINER work_mount=$PROCTOR_WORK_MOUNT"
