#!/usr/bin/env bash
# Does the fixture's own test suite pass as the run left it? Reason: the counts.
set -uo pipefail
out=$(cd "$PROCTOR_WORK" && dotnet test Ledger.Tests --nologo 2>&1)
summary=$(grep -E '^(Passed|Failed)!' <<<"$out" | tail -1)
if [ -z "$summary" ]; then echo "tests did not run: $(grep -m1 'error ' <<<"$out" | sed 's/.*error /error /' | cut -c1-120)"; exit 1; fi
failed=$(sed -E 's/.*Failed: *([0-9]+).*/\1/' <<<"$summary")
total=$(sed -E 's/.*Total: *([0-9]+).*/\1/' <<<"$summary")
if [ "$failed" -eq 0 ]; then echo "$total of $total tests pass"; exit 0; fi
echo "$failed of $total tests failed: $(grep -m1 -E '^\s+Failed ' <<<"$out" | sed -E 's/^\s+Failed //' | cut -c1-80)"
exit 1
