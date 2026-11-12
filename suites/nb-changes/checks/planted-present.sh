#!/usr/bin/env bash
# Validity: did the agent start from the repository this task is about? The fixture's image applies plant.patch
# to nb at the pin and commits the result as the only commit. A sample whose commit lacks the plant measured
# something else and does not count. A fixture with no plant.patch (not-a-bug) has nothing to check.
set -uo pipefail
plant="$PROCTOR_FIXTURE/plant.patch"
[ -f "$plant" ] || { echo "nothing is planted for this task"; exit 0; }
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
git -C "$PROCTOR_WORK" archive HEAD | tar -x -C "$tmp" || { echo "the work directory has no fixture commit"; exit 1; }
files=$(grep -c '^diff --git ' "$plant")
(cd "$tmp" && git apply --reverse --check "$plant" 2>/dev/null) && { echo "the plant is in the fixture commit ($files file(s))"; exit 0; }
echo "the fixture commit does not hold plant.patch"; exit 1
