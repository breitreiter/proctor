#!/usr/bin/env bash
# Does the written exam carry a sentence on itself, each variant and each task: a description on the exam and a
# task, a note on a variant? A report prints them beside every id; proctor falls back to a prompt line for a
# task, but a reader deserves a sentence.
set -uo pipefail
id=$(jq -r .exam <<<"$PROCTOR_EXPECT")
dir="$PROCTOR_WORK/exams/$id"; def="$dir/exam.json"
[ -f "$def" ] || { echo "no exam to read"; exit 1; }
missing=$( { jq -r 'if (.description // "") == "" then "the exam" else empty end' "$def"
             for v in "$dir"/variants/*.json; do [ -f "$v" ] && jq -r 'select((.note // "") == "") | "variant \(.id // input_filename)"' "$v"; done
             for c in "$dir"/tasks/*.json; do [ -f "$c" ] && jq -r 'select((.description // "") == "") | "task \(.id // input_filename)"' "$c"; done; } | paste -sd, -)
if [ -z "$missing" ]; then echo "every exam, variant and task described"; exit 0; fi
echo "no description or note on $missing"; exit 1
