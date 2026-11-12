#!/usr/bin/env bash
# Do nb's own tests pass on the changed repository? A failing run is repeated once and the second run is the
# verdict: nb has a test that depends on timing (RetryingChatClientTests.MinRequestInterval_PacesEveryCall...)
# and fails now and then on a loaded machine.
set -uo pipefail
for attempt in 1 2; do
  out=$(cd "$PROCTOR_WORK" && timeout 600 dotnet test nb.Tests --nologo 2>&1)
  summary=$(grep -E '^(Passed|Failed)!' <<<"$out" | tail -1)
  [ -n "$summary" ] || { echo "nb's tests did not run: $(grep -m1 -E ' error [A-Z]+[0-9]+' <<<"$out" | sed -E 's|^.*/([^/]+\([0-9,]+\)): |\1: |' | cut -c1-140)"; exit 1; }
  failed=$(sed -E 's/.*Failed: *([0-9]+).*/\1/' <<<"$summary"); total=$(sed -E 's/.*Total: *([0-9]+).*/\1/' <<<"$summary")
  [ "$failed" -eq 0 ] && { echo "$total of $total of nb's tests pass$([ $attempt = 2 ] && echo ' (on a second run)')"; exit 0; }
done
echo "$failed of $total of nb's tests failed, twice: $(grep -E '^\s+Failed ' <<<"$out" | sed -E 's/^\s+Failed nb\.Tests\.//; s/ \[.*//' | head -3 | paste -sd, | sed 's/,/, /g')"; exit 1
