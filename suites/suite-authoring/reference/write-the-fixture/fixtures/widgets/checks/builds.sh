#!/usr/bin/env bash
# Does the repository build as the run left it? Reason: the error count.
set -uo pipefail
out=$(cd "$PROCTOR_WORK" && dotnet build tests/Widgets.Tests --nologo -v q 2>&1)
errors=$(grep -c 'error ' <<<"$out" || true)
if [ "$errors" -eq 0 ] && grep -q 'Build succeeded' <<<"$out"; then echo "dotnet build: 0 errors"; exit 0; fi
echo "dotnet build: $errors errors; $(grep -m1 'error ' <<<"$out" | sed 's/.*error /error /' | cut -c1-120)"
exit 1
