#!/usr/bin/env bash
# Did the agent open an example exam or fixture under .bundle/? Only docs=docs-and-examples has
# any, so this rate is the measure of borrowing: how often an agent with examples looks at them.
set -uo pipefail
# The files sit inside the work mount, so they are also reached by a path relative to it (.bundle/exams/...).
w=${AUSPEX_WORK_MOUNT:-/work}; b="$w/.bundle"; rel=${b#"$w"/}
[ "$rel" != "$b" ] && at="($b|(\./)?$rel)" || at="$b"
read=$(jq -r 'select(.type == "tool_call") | .arguments | del(.content, .old_string, .new_string, .description) | tostring' "$AUSPEX_TRANSCRIPT" \
  | grep -oE "(^|[^A-Za-z0-9_/])$at/(exams|fixtures)/[A-Za-z0-9_./-]*" | sed -E "s#^.*$at/##" | sort -u)
if [ -n "$read" ]; then echo "$(wc -l <<<"$read") example path(s): $(head -4 <<<"$read" | paste -sd, - | sed 's/,/, /g')"; exit 0; fi
echo "no example opened"; exit 1
