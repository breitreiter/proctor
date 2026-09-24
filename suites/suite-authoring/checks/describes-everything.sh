#!/usr/bin/env bash
# Does the written suite carry a description on itself, each arm and each task? A report prints them
# beside every id; proctor falls back to a prompt line for a task, but a reader deserves a sentence.
set -uo pipefail
id=$(jq -r .suite <<<"$PROCTOR_EXPECT")
dir="$PROCTOR_WORK/suites/$id"; def="$dir/suite.json"
# The layout before suite/task, which an agent shown an older bundle still writes.
[ -f "$def" ] || { dir="$PROCTOR_WORK/evals/$id"; def="$dir/eval.json"; }
[ -f "$def" ] || { echo "no suite to read"; exit 1; }
missing=$( { jq -r 'if (.description // "") == "" then "the suite" else empty end, (.arms // [])[] | select((.description // "") == "") | "arm \(.id)"' "$def"
             for c in "$dir"/tasks/*.json "$dir"/cases/*.json; do [ -f "$c" ] && jq -r 'select((.description // "") == "") | "task \(.id // input_filename)"' "$c"; done; } | paste -sd, -)
if [ -z "$missing" ]; then echo "every suite, arm and task described"; exit 0; fi
echo "no description on $missing"; exit 1
