#!/usr/bin/env bash
# Does the task's acceptance script pass against the repository as the run left it?
# The script is acceptance/<task>.sh, run with the checkout as its argument; its first
# stdout line is the reason. A task without one passes trivially.
set -uo pipefail
script="$PROCTOR_SUITE_DIR/acceptance/$PROCTOR_TASK.sh"
[ -x "$script" ] || { echo "no acceptance script for this task"; exit 0; }
exec "$script" "$PROCTOR_WORK"
