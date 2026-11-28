#!/usr/bin/env bash
# Is whoever does the work in the written exam bare nb (a provider and a model, no agent and no route through
# Harbor), and does its program say `tools none`? A chat exam needs no sandbox; a value that runs an agent, or a
# program with tools, is not the exam the user asked for.
set -uo pipefail
. "$PROCTOR_EXAM_DIR/checks/written.sh"
def="$WRITTEN_DIR/exam.json"
jq -e '.axes | type == "object"' "$def" >/dev/null 2>&1 || { echo "exam.json declares no axes"; exit 1; }
agents=$(jq -r '[.axes | to_entries[] | .key as $a | (.value.values // {}) | to_entries[]
  | select(.value.agent != null or .value.route == "harbor") | "\($a)=\(.key)"] | join(", ")' "$def")
[ -z "$agents" ] || { echo "not bare nb: $agents"; exit 1; }
workers=$(jq '[.axes[] | (.values // {})[] | select(.provider != null)] | length' "$def")
[ "$workers" -gt 0 ] || { echo "no value names a provider, so nothing answers"; exit 1; }
program="$WRITTEN_DIR/program.nb"
[ -f "$program" ] || { echo "no program.nb"; exit 1; }
if grep -qE '^[[:space:]]*tools[[:space:]]+none[[:space:]]*$' "$program"; then
  echo "$workers value(s) on bare nb, program has tools none"; exit 0
fi
said=$(grep -E '^[[:space:]]*(tools|mcp)\b' "$program" | head -1 | cut -c1-80)
[ -n "$said" ] || said="no tools directive, so the default tool set"
echo "program.nb does not say tools none ($said)"; exit 1
