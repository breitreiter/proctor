#!/usr/bin/env bash
# Did the agent open an example suite or fixture from the bundle? Only the docs-and-examples arm has
# any, so this rate is the measure of borrowing: how often an agent with examples looks at them.
set -uo pipefail
b=${PROCTOR_BUNDLE_MOUNT:-/bundle}
read=$(jq -r 'select(.type == "tool_call") | .arguments | del(.content, .old_string, .new_string, .description) | tostring' "$PROCTOR_TRANSCRIPT" \
  | grep -oE "$b/(suites|evals|fixtures)/[A-Za-z0-9_./-]*" | sed "s#^$b/##" | sort -u)
if [ -n "$read" ]; then echo "$(wc -l <<<"$read") example path(s): $(head -4 <<<"$read" | paste -sd, - | sed 's/,/, /g')"; exit 0; fi
echo "no example opened"; exit 1
