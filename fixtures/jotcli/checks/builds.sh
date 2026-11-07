#!/usr/bin/env bash
# Does the repository compile as the run left it? Reason: the error count.
set -uo pipefail
cd "$PROCTOR_WORK" || exit 1
[ -d node_modules ] || npm ci --no-audit --no-fund --silent >/dev/null 2>&1 || { echo "npm ci failed"; exit 1; }
out=$(npx tsc --pretty false 2>&1)
errors=$(grep -c 'error TS' <<<"$out" || true)
if [ "$errors" -eq 0 ]; then echo "tsc: 0 errors"; exit 0; fi
echo "tsc: $errors errors; $(grep -m1 'error TS' <<<"$out" | cut -c1-120)"
exit 1
