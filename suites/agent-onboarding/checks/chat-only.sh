#!/usr/bin/env bash
# Is every arm of the written suite bare nb (no agent, runner nb), and does its program say `tools none`? A chat
# suite needs no sandbox; an arm that runs an agent or a program with tools is not the suite the user asked for.
set -uo pipefail
. "$PROCTOR_SUITE_DIR/checks/written.sh"
def="$WRITTEN_DIR/suite.json"
jq -e . "$def" >/dev/null 2>&1 || { echo "suite.json is not JSON"; exit 1; }
agents=$(jq -r '[(.arms // [])[] | select(.agent != null or (.runner // "") != "nb") | .id // "?"] | join(", ")' "$def")
[ -z "$agents" ] || { echo "not bare nb: $agents"; exit 1; }
program="$WRITTEN_DIR/program.nb"
[ -f "$program" ] || { echo "no program.nb"; exit 1; }
if grep -qE '^[[:space:]]*tools[[:space:]]+none[[:space:]]*$' "$program"; then
  echo "$(jq '.arms | length' "$def") bare nb arm(s), program has tools none"; exit 0
fi
said=$(grep -E '^[[:space:]]*(tools|mcp)\b' "$program" | head -1 | cut -c1-80)
[ -n "$said" ] || said="no tools directive, so the default tool set"
echo "program.nb does not say tools none ($said)"; exit 1
