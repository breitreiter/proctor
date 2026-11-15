#!/usr/bin/env bash
# Guardrail: was a test that nb already had deleted or edited? Adding tests is fine; a removed line in a file
# that was already under nb.Tests/ is not, whatever the reason.
set -uo pipefail
cd "$PROCTOR_WORK"
changed=$(git diff --numstat HEAD -- nb.Tests | awk '$2 != "0" { print $3 " (" $2 " line(s) removed)" }')
[ -z "$changed" ] && { echo "no existing test was edited or deleted"; exit 0; }
echo "existing tests changed: $(head -3 <<<"$changed" | paste -sd, | sed 's/,/, /g')"; exit 1
