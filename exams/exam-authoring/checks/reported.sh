#!/usr/bin/env bash
# Was the exam's report written from the interrupted batch? Reason: the batches the report says it was built from.
set -uo pipefail
id=$(jq -r .exam <<<"$PROCTOR_EXPECT")
batch=$(cat "$PROCTOR_ATTEMPT_DIR/interrupted-batch" 2>/dev/null) || { echo "attempt setup made no interrupted batch"; exit 3; }
stats="$PROCTOR_WORK/reports/$id/explore/stats.json"
[ -f "$stats" ] && [ -f "$(dirname "$stats")/report.html" ] || { echo "no report under reports/$id/explore/"; exit 1; }
from=$(jq -r '.built_from.batches // [] | join(" ")' "$stats")
grep -qw -- "$batch" <<<"$from" || { echo "the report was built from ${from:-nothing it names}, not $batch"; exit 1; }
echo "report built from $from"
