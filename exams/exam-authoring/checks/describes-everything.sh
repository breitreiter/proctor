#!/usr/bin/env bash
# Does the written exam carry a description on itself, each variant and each task? A report prints them
# beside every id; proctor falls back to a prompt line for a task, but a reader deserves a sentence.
set -uo pipefail
id=$(jq -r .exam <<<"$PROCTOR_EXPECT")
dir="$PROCTOR_WORK/exams/$id"; def="$dir/exam.json"
# The layout before exam/task, which an agent shown an older bundle still writes.
[ -f "$def" ] || { dir="$PROCTOR_WORK/evals/$id"; def="$dir/eval.json"; }
[ -f "$def" ] || { echo "no exam to read"; exit 1; }
missing=$( { jq -r 'if (.description // "") == "" then "the exam" else empty end, (.variants // [])[] | select((.description // "") == "") | "variant \(.id)"' "$def"
             for c in "$dir"/tasks/*.json "$dir"/cases/*.json; do [ -f "$c" ] && jq -r 'select((.description // "") == "") | "task \(.id // input_filename)"' "$c"; done; } | paste -sd, -)
if [ -z "$missing" ]; then echo "every exam, variant and task described"; exit 0; fi
echo "no description on $missing"; exit 1
