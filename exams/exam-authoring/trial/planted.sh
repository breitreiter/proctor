#!/usr/bin/env bash
# The trial's runner, standing in for the agent under test: it applies the planted run to the checkout
# (/trial/$TRIAL_TASK/<plant>.sh, the plant being the value of the axis run.sh added), then has nb's Mock
# provider make the one tool call every planted run makes (<stage>/tool) and close with the plant's message
# (<plant>.answer), so the transcript is nb's own. The program proctor compiled from the written exam is read
# and not used.
set -euo pipefail
cat >/dev/null
dir="/trial/$TRIAL_TASK"
plant=$AUSPEX_AXIS_PLANTED
bash "$dir/$plant.sh" >&2
config=$(mktemp)
jq -n --rawfile answer "$dir/$plant.answer" \
  '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: [{Name: "Mock", Response: ($answer | rtrimstr("\n"))}]}' >"$config"
printf 'approval default deny\napproval bash *\nrun MOCK:tool=bash %s\n' "$(cat "$dir/tool")" \
  | /opt/nb/nb --output jsonl --config "$config" -
