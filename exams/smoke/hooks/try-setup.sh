#!/usr/bin/env bash
# Runs once before a try's first stage. The fixture is already checked out in $AUSPEX_WORK, with the values' files
# laid over it; a real exam would start its fakes here. What this leaves is the state every stage starts from.
set -euo pipefail
echo "try $AUSPEX_CONFIGURATION/$AUSPEX_TRY_NUMBER setup; work has $(ls "$AUSPEX_WORK" | tr '\n' ' ')"
# A real exam that runs nb in a container would create it here as $AUSPEX_CONTAINER, and skip that when $AUSPEX_RUNNER is empty (a bare run).
echo "runner=$AUSPEX_RUNNER container=$AUSPEX_CONTAINER work_mount=$AUSPEX_WORK_MOUNT"
