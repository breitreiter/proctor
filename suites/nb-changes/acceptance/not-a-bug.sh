#!/usr/bin/env bash
# not-a-bug: the report describes documented behaviour, so the right change to nb's code is none. A change to the
# docs (any .md) is allowed: saying it more clearly is a fair answer. Anything else in the diff fails.
set -uo pipefail
touched=$(grep '^diff --git ' "$PROCTOR_DIFF" 2>/dev/null | sed 's|.* b/||' | grep -v '\.md$')
[ -z "$touched" ] && { echo "no code changed"; exit 0; }
echo "changed $(wc -l <<<"$touched") file(s) for behaviour the docs describe: $(head -3 <<<"$touched" | tr '\n' ' ')"; exit 1
