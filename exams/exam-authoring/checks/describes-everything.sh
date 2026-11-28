#!/usr/bin/env bash
# Does the written exam carry a description and each stage an `about`? A report prints one beside every id;
# proctor falls back to a prompt line for a stage, but a reader deserves a sentence. (An axis's `about` and a
# value's `note` proctor requires itself, so `validates` holds those.)
set -uo pipefail
id=$(jq -r .exam <<<"$AUSPEX_EXPECT")
def="$AUSPEX_WORK/exams/$id/exam.json"
[ -f "$def" ] || { echo "no exam to read"; exit 1; }
stages=$(jq '(.stages // []) | length' "$def" 2>/dev/null)
[ "${stages:-0}" -gt 0 ] || { echo "exam.json has no stages"; exit 1; }
missing=$(jq -r '[(if (.description // "") == "" then "the exam" else empty end),
  ((.stages // [])[] | select((.about // "") == "") | "stage \(.id // "?")")] | join(", ")' "$def")
if [ -z "$missing" ]; then echo "the exam and its $stages stage(s) are described"; exit 0; fi
echo "no description on $missing"; exit 1
