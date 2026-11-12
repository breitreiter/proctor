#!/usr/bin/env bash
# Does nb's solution build, tests included?
set -uo pipefail
out=$(cd "$PROCTOR_WORK" && dotnet build nb.sln -nologo -v q 2>&1) && { echo "nb.sln builds"; exit 0; }
echo "build failed: $(grep -m1 -E ' error [A-Z]+[0-9]+' <<<"$out" | sed -E 's|^.*/([^/]+\([0-9,]+\)): |\1: |' | cut -c1-160)"; exit 1
