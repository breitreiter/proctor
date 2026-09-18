#!/usr/bin/env bash
# Script-check contract: exit 0/1/2 = pass/fail/needs-judge, first stdout line is the reason.
set -euo pipefail
words=$(jq -r 'select(.type=="assistant_text") | .text' "$PROCTOR_TRANSCRIPT" | tail -1 | wc -w)
if [ "$words" -gt 0 ]; then echo "answer has $words words"; exit 0; fi
echo "answer is empty"; exit 1
