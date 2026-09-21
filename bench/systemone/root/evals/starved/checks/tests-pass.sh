#!/usr/bin/env bash
# Do the fixture's tests pass, plus the case's acceptance tests if it has any? Reason: the counts.
set -uo pipefail
source "$(dirname "$0")/lib.sh"
dir=$(work_dir)
proj=$(test_project "$dir")
acceptance="$PROCTOR_EVAL_DIR/acceptance/$PROCTOR_CASE"
[ -d "$acceptance" ] && cp "$acceptance"/*.cs "$(dirname "$proj")/"
out=$(cd "$dir" && dotnet test "$proj" --nologo 2>&1)
summary=$(grep -E '^(Passed|Failed)!' <<<"$out" | tail -1)
if [ -z "$summary" ]; then echo "tests did not run: $(grep -m1 'error ' <<<"$out" | sed 's/.*error /error /' | cut -c1-120)"; exit 1; fi
failed=$(sed -E 's/.*Failed: *([0-9]+).*/\1/' <<<"$summary")
total=$(sed -E 's/.*Total: *([0-9]+).*/\1/' <<<"$summary")
if [ "$failed" -eq 0 ]; then echo "$total of $total tests pass"; exit 0; fi
echo "$failed of $total tests failed: $(grep -m1 -E '^\s+Failed ' <<<"$out" | sed -E 's/^\s+Failed //' | cut -c1-80)"
exit 1
