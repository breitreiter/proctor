#!/usr/bin/env bash
# Does the fixture's own test suite pass as the run left it? Reason: the counts.
set -uo pipefail
cd "$PROCTOR_WORK" || exit 1
[ -d node_modules ] || npm ci --no-audit --no-fund --silent >/dev/null 2>&1 || { echo "npm ci failed"; exit 1; }
out=$(npm test --silent 2>&1)
pass=$(grep -m1 -E '^# pass ' <<<"$out" | awk '{print $3}')
fail=$(grep -m1 -E '^# fail ' <<<"$out" | awk '{print $3}')
if [ -z "$pass" ] || [ -z "$fail" ]; then echo "tests did not run: $(grep -m1 -E 'error' <<<"$out" | cut -c1-120)"; exit 1; fi
total=$((pass + fail))
if [ "$fail" -eq 0 ]; then echo "$total of $total tests pass"; exit 0; fi
echo "$fail of $total tests failed: $(grep -m1 -E '^not ok' <<<"$out" | sed -E 's/^not ok [0-9]+ - //' | cut -c1-80)"
exit 1
