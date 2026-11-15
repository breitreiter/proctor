#!/usr/bin/env bash
# The interrupted batch continue-the-batch starts from: the workspace's stock-count run in full by the proctor given,
# then part of it undone as a crash would leave it: the last attempt never started, the one before it still running.
# Prints the batch id. attempt-setup.sh calls it for that task (proctor inside the attempt's container), and
# reference/shakedown.sh calls it on the host.
#   usage: interrupt.sh <work dir> <the work dir as proctor sees it> <the command that runs proctor...>
set -euo pipefail
work=$1 root=$2; shift 2
"$@" run stock-count --root "$root" >"$work/.interrupt.log" 2>&1 || { tail -3 "$work/.interrupt.log" >&2; exit 1; }
rm -f "$work/.interrupt.log"
batch=$(basename "$(ls -d "$work"/attempts/*/ | tail -1)")
dir="$work/attempts/$batch"
mapfile -t attempts < <(jq -r '.attempts[] | "\(.variant)/\(.task)/\(.attempt)"' "$dir/batch.json")
rm -rf "${dir:?}/${attempts[-1]}"
echo running >"$dir/${attempts[-2]}/status"
echo "$batch"
