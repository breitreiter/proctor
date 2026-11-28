#!/usr/bin/env bash
# Do the written exam carry a description and each stage an `about`? The report prints one beside every id;
# proctor falls back to a prompt line for a stage, but a reader deserves a sentence. (An axis's `about` and a
# value's `note` proctor requires itself, so `validates` holds those.)
set -uo pipefail
. "$AUSPEX_EXAM_DIR/checks/written.sh"
def="$WRITTEN_DIR/exam.json"
missing=$(jq -r '[(if (.description // "") == "" then "the exam" else empty end),
  ((.stages // [])[] | select((.about // "") == "") | "stage \(.id // "?")")] | join(", ")' "$def" 2>/dev/null)
stages=$(jq '(.stages // []) | length' "$def" 2>/dev/null)
[ "${stages:-0}" -gt 0 ] || { echo "exam.json has no stages"; exit 1; }
if [ -z "$missing" ]; then echo "the exam and its $stages stages are all described"; exit 0; fi
echo "no description on $missing"; exit 1
