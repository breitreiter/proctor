#!/usr/bin/env bash
# Do the written suite, each arm and each task carry a description? The report prints one beside every id;
# proctor falls back to a prompt line for a task, but a reader deserves a sentence.
set -uo pipefail
. "$PROCTOR_SUITE_DIR/checks/written.sh"
def="$WRITTEN_DIR/suite.json"
missing=$( { jq -r '(if (.description // "") == "" then "the suite" else empty end), ((.arms // [])[] | select((.description // "") == "") | "arm \(.id)")' "$def"
             for t in "$WRITTEN_DIR"/tasks/*.json; do [ -f "$t" ] && jq -r --arg f "$(basename "$t" .json)" 'select((.description // "") == "") | "task \(.id // $f)"' "$t"; done; } | paste -sd, - | sed 's/,/, /g')
if [ -z "$missing" ]; then echo "the suite, its arms and its tasks are all described"; exit 0; fi
echo "no description on $missing"; exit 1
