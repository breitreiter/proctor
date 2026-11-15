#!/usr/bin/env bash
# Is Legacy.Widgets gone from src/ and tests/? The tests pass either side of the rename, so they cannot say.
set -uo pipefail
left=$(cd "$PROCTOR_WORK" && grep -rl --exclude-dir=bin --exclude-dir=obj 'Legacy\.Widgets' src tests)
[ -z "$left" ] && { echo "no Legacy.Widgets left"; exit 0; }
echo "Legacy.Widgets still in $(paste -sd, <<<"$left")"; exit 1
