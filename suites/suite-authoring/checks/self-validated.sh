#!/usr/bin/env bash
# Did the agent run proctor's list at least once? Reason: how many times.
set -uo pipefail
n=$(jq -r 'select(.type == "tool_call" and .name == "bash") | .arguments.command // ""' "$PROCTOR_TRANSCRIPT" | grep -cE 'proctor(\.dll)?[" ]+list' || true)
if [ "$n" -gt 0 ]; then echo "ran list $n time(s)"; exit 0; fi
echo "never ran list"; exit 1
