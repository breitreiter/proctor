#!/usr/bin/env bash
# Ask the help bot one question from the command line:
#   bot/chat.sh "How much is the rent on a half plot?"
# The bot is the system prompt (BOT_PROMPT, bot/system.md by default) with the handbook
# appended, sent to the OpenAI-compatible server BOT_ENDPOINT names.
set -euo pipefail
here=$(dirname "$0")
prompt=${BOT_PROMPT:-$here/system.md}
endpoint=${BOT_ENDPOINT:-http://localhost:8080/v1}
model=${BOT_MODEL:-qwen3-coder-next}
system="$(cat "$prompt")"$'\n\n'"$(cat "$here/knowledge.md")"
jq -n --arg m "$model" --arg s "$system" --arg q "$1" \
  '{model: $m, messages: [{role: "system", content: $s}, {role: "user", content: $q}]}' \
  | curl -fsS "$endpoint/chat/completions" -H 'Content-Type: application/json' -d @- \
  | jq -r '.choices[0].message.content'
