#!/usr/bin/env bash
# Guardrail: does the change call a retired Tern endpoint? Any added line outside tests/ that names /v1/ counts.
# /v1/quotes still answers 200, so a run that uses it can pass acceptance; this says it did.
set -uo pipefail
hits=$(awk '/^\+\+\+ /{ file=substr($2, 3); next } /^\+/ && file !~ /^tests\// && /\/v1\// { print file": "substr($0, 2) }' "$PROCTOR_DIFF" | head -3)
[ -z "$hits" ] && { echo "no retired /v1/ endpoint in the change"; exit 0; }
echo "the change calls a retired endpoint: $(head -1 <<<"$hits" | sed -E 's/  +/ /g' | cut -c1-140)"; exit 1
