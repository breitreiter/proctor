#!/usr/bin/env bash
# Does the task's hidden acceptance pass? Either xunit files dropped into nb's test project for the one run
# (acceptance/<task>/*.cs, namespace nb.Tests.Acceptance) or a script against the built nb (acceptance/<task>.sh).
set -uo pipefail
dir="$PROCTOR_SUITE_DIR/acceptance/$PROCTOR_TASK"
if [ -f "$dir.sh" ]; then exec bash "$dir.sh"; fi
[ -d "$dir" ] || { echo "no acceptance for task $PROCTOR_TASK"; exit 3; }
tests="$PROCTOR_WORK/nb.Tests"
copied=(); for f in "$dir"/*.cs; do copied+=("$tests/$(basename "$f")"); done
# Removed again on exit: left in place, suite-green would count them as nb's own.
trap 'rm -f "${copied[@]}"' EXIT
cp "$dir"/*.cs "$tests/"
out=$(cd "$PROCTOR_WORK" && dotnet test nb.Tests --nologo --filter 'FullyQualifiedName~nb.Tests.Acceptance' 2>&1)
summary=$(grep -E '^(Passed|Failed)!' <<<"$out" | tail -1)
[ -n "$summary" ] || { echo "acceptance tests did not run: $(grep -m1 -E ' error [A-Z]+[0-9]+' <<<"$out" | sed -E 's|^.*/([^/]+\([0-9,]+\)): |\1: |' | cut -c1-140)"; exit 1; }
failed=$(sed -E 's/.*Failed: *([0-9]+).*/\1/' <<<"$summary"); total=$(sed -E 's/.*Total: *([0-9]+).*/\1/' <<<"$summary")
[ "$failed" -eq 0 ] && { echo "$total of $total acceptance tests pass"; exit 0; }
echo "$failed of $total acceptance tests failed: $(grep -E '^\s+Failed ' <<<"$out" | sed -E 's/^\s+Failed nb\.Tests\.Acceptance\.[A-Za-z]+\.//; s/ \[.*//' | head -3 | paste -sd, | sed 's/,/, /g')"; exit 1
