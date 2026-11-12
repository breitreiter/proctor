#!/usr/bin/env bash
# far-cause: on a run the oracle answered, the trailer's input and output add up to its total, and the total
# still holds the oracle's own call. Run on nb's Mock provider, whose token counts are fixed for a given program.
set -uo pipefail
nb="$PROCTOR_WORK/bin/Debug/net10.0/nb"
[ -x "$nb" ] || { echo "no built nb at bin/Debug/net10.0/nb"; exit 1; }
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
echo '{"Harness": "nb", "ActiveProvider": "Mock", "ChatProviders": [{"Name": "Mock", "Response": "mock-ok"}]}' >"$tmp/nb.json"
printf '## deploy-target\nStaging only.\n' >"$tmp/sheet.md"
usage() { (cd "$tmp" && printf '%s\n' "$@" | "$nb" --output jsonl --config "$tmp/nb.json" - 2>/dev/null) | jq -c 'select(.type=="result") | .usage | [.input, .output, .total]' | tail -1; }
plain=$(usage 'tools none' 'run MOCK:response=Done.')
oracle=$(usage 'tools none' "oracle @$tmp/sheet.md" 'run MOCK:response=Which environment? MOCK:oracle=deploy-target')
[ -n "$plain" ] && [ -n "$oracle" ] || { echo "nb wrote no result trailer on its Mock provider"; exit 1; }
sums() { jq -e '.[0] + .[1] == .[2]' <<<"$1" >/dev/null; }
sums "$plain" || { echo "a run with no oracle: input, output, total $plain do not add up"; exit 1; }
sums "$oracle" || { echo "a run the oracle answered: input, output, total $oracle do not add up"; exit 1; }
# Unplanted nb at the pin reads [40,20,60] here: two model turns and the oracle's own call.
[ "$oracle" = "[40,20,60]" ] || { echo "a run the oracle answered: input, output, total $oracle, where nb at the pin reads [40,20,60]; a call went uncounted"; exit 1; }
echo "input and output add up to total with and without the oracle ($oracle, $plain)"
