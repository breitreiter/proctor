#!/usr/bin/env bash
# Was the interrupted batch finished in place? Every attempt it set out to make is completed, and no other batch of
# the exam was started. Reason: the batch it continued, or what is left, or the batch it started instead.
set -uo pipefail
id=$(jq -r .exam <<<"$PROCTOR_EXPECT")
batch=$(cat "$PROCTOR_ATTEMPT_DIR/interrupted-batch" 2>/dev/null) || { echo "attempt setup made no interrupted batch"; exit 3; }
dir="$PROCTOR_WORK/attempts/$batch"
[ -f "$dir/batch.json" ] || { echo "batch $batch is gone"; exit 1; }
others=$(for f in "$PROCTOR_WORK"/attempts/*/batch.json; do
  [ "$(jq -r .exam "$f")" = "$id" ] && [ "$(dirname "$f")" != "$dir" ] && basename "$(dirname "$f")"; done)
[ -z "$others" ] || { echo "started another batch instead: $(paste -sd' ' <<<"$others")"; exit 1; }
left=$(jq -r '.attempts[] | "\(.variant)/\(.task)/\(.attempt)"' "$dir/batch.json" | while read -r a; do
  s=$(cat "$dir/$a/status" 2>/dev/null || echo pending); [ "$s" = completed ] || echo "$a $s"; done)
[ -z "$left" ] || { echo "not finished: $(paste -sd';' <<<"$left")"; exit 1; }
echo "continued $batch: $(jq '.attempts | length' "$dir/batch.json") attempts completed"
