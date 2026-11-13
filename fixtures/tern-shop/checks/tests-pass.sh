#!/usr/bin/env bash
# Do the shop's own tests pass? Only the test files in the fixture commit: a test the agent added may call Tern's
# sandbox, which the verifier does not run.
set -uo pipefail
cd "$PROCTOR_WORK"
mapfile -t files < <(git ls-tree -r --name-only HEAD -- tests | grep '/test_[^/]*\.py$')
[ ${#files[@]} -gt 0 ] || { echo "no test files in the fixture commit"; exit 3; }
out=$(python -m pytest -q -p no:cacheprovider "${files[@]}" 2>&1); code=$?
summary=$(tail -1 <<<"$out" | sed -E 's/=+//g; s/^ +| +$//g')
[ $code -eq 0 ] && { echo "the shop's tests pass: $summary"; exit 0; }
echo "the shop's tests fail: $summary"; exit 1
