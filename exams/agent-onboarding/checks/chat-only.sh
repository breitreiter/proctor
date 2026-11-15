#!/usr/bin/env bash
# Is every variant of the written exam bare nb (no agent, runner nb), and does its program say `tools none`? A chat
# exam needs no sandbox; a variant that runs an agent or a program with tools is not the exam the user asked for.
set -uo pipefail
. "$PROCTOR_EXAM_DIR/checks/written.sh"
variants=("$WRITTEN_DIR"/variants/*.json)
[ -f "${variants[0]}" ] || { echo "no variants/*.json"; exit 1; }
jq -e . "${variants[@]}" >/dev/null 2>&1 || { echo "a variant file is not JSON"; exit 1; }
agents=$(jq -rs '[.[] | select(.agent != null or (.runner // "") != "nb") | .id // "?"] | join(", ")' "${variants[@]}")
[ -z "$agents" ] || { echo "not bare nb: $agents"; exit 1; }
program="$WRITTEN_DIR/program.nb"
[ -f "$program" ] || { echo "no program.nb"; exit 1; }
if grep -qE '^[[:space:]]*tools[[:space:]]+none[[:space:]]*$' "$program"; then
  echo "${#variants[@]} bare nb variant(s), program has tools none"; exit 0
fi
said=$(grep -E '^[[:space:]]*(tools|mcp)\b' "$program" | head -1 | cut -c1-80)
[ -n "$said" ] || said="no tools directive, so the default tool set"
echo "program.nb does not say tools none ($said)"; exit 1
