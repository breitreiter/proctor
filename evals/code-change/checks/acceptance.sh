#!/usr/bin/env bash
# Do the case's acceptance tests pass, dropped into the fixture's test project? A case without any passes trivially.
set -uo pipefail
acceptance="$PROCTOR_EVAL_DIR/acceptance/$PROCTOR_CASE"
[ -d "$acceptance" ] || { echo "no acceptance tests for this case"; exit 0; }
proj=$(find "$PROCTOR_WORK" -name '*.Tests.csproj' | head -1)
cp "$acceptance"/*.cs "$(dirname "$proj")/"
out=$(cd "$PROCTOR_WORK" && dotnet test "$proj" --nologo 2>&1)
summary=$(grep -E '^(Passed|Failed)!' <<<"$out" | tail -1)
if [ -z "$summary" ]; then echo "tests did not run: $(grep -m1 'error ' <<<"$out" | sed 's/.*error /error /' | cut -c1-120)"; exit 1; fi
failed=$(sed -E 's/.*Failed: *([0-9]+).*/\1/' <<<"$summary")
total=$(sed -E 's/.*Total: *([0-9]+).*/\1/' <<<"$summary")
if [ "$failed" -eq 0 ]; then echo "$total of $total tests pass with acceptance"; exit 0; fi
echo "$failed of $total tests failed with acceptance: $(grep -m1 -E '^\s+Failed ' <<<"$out" | sed -E 's/^\s+Failed //' | cut -c1-80)"
exit 1
