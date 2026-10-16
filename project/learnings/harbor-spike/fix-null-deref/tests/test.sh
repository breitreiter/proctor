#!/bin/bash
set -uo pipefail
out=/logs/verifier; patch=/logs/artifacts/diff.patch
cd /app
declare -A r
if [ -s "$patch" ]; then git apply --whitespace=nowarn "$patch" || { echo "patch did not apply" > $out/reasons.txt; echo '{}' > $out/reward.json; exit 0; }; fi
dotnet build Ledger.Tests --nologo -v q > $out/build.txt 2>&1 && r[builds]=1 || r[builds]=0
dotnet test Ledger.Tests --nologo --no-build > $out/test.txt 2>&1 && r[tests_pass]=1 || r[tests_pass]=0
# files_touched at_most Ledger/**
bad=$(git diff --name-only HEAD | grep -v '^Ledger/' | head -1)
[ -z "$bad" ] && r[diff_in_scope]=1 || r[diff_in_scope]=0
printf '{"builds": %s, "tests_pass": %s, "diff_in_scope": %s}\n' ${r[builds]} ${r[tests_pass]} ${r[diff_in_scope]} > $out/reward.json
cat $out/reward.json
