#!/usr/bin/env bash
# The trial's runner, standing in for the bot: proctor hands it the program it compiled from the written suite
# for this cell, and it answers as the planted run would, through nb's Mock provider, so the transcript is nb's
# own. It reads which prompt the program gives the bot from the markers in plants.json (versions), which
# question the task asks from its prompt (questions, first match), and the arm's planted variant from the map
# run.sh wrote; then it records what it planted and what the suite should make of it (expect: pass, fail, or
# none when the task asks something the trial does not know or the program carries no bot).
#   env: TRIAL_PLANTS (plants.json), TRIAL_ARMS (clone arm -> variant, written arm), TRIAL_RECORDS (a directory)
set -euo pipefail
program=$(cat)
plants=$TRIAL_PLANTS
entry=$(jq -c --arg a "$PROCTOR_ARM" '.[$a]' "$TRIAL_ARMS")
variant=$(jq -r .variant <<<"$entry")
prompt=$(jq -r '.prompt // ""' <<<"$PROCTOR_TASK_JSON")

version=none; found=0
while IFS= read -r v; do
  all=1
  while IFS= read -r marker; do grep -qF -- "$marker" <<<"$program" || all=0; done < <(jq -r --arg v "$v" '.versions[$v][]' "$plants")
  if [ $all = 1 ]; then version=$v; found=$((found + 1)); fi
done < <(jq -r '.versions | keys_unsorted[]' "$plants")
[ $found -le 1 ] || version=ambiguous

key=$(jq -r .fallback "$plants")
for i in $(seq 0 $(($(jq '.questions | length' "$plants") - 1))); do
  if grep -qiE -- "$(jq -r ".questions[$i].match" "$plants")" <<<"$prompt"; then key=$(jq -r ".questions[$i].key" "$plants"); break; fi
done

planted=$(jq -c --arg v "$variant" --arg ver "$version" --arg k "$key" '
  .dodge as $dodge
  | (.variants[$v] | . + (.by_version[$ver] // {})) as $x
  | if $ver == "none" or $ver == "ambiguous" then {answer: $dodge, expect: "none"}
    else {answer: ($x.answers[$k] // $x.answers["*"]),
          expect: (if $k == "*" then "none" elif ($x.expect | type) == "object" then $x.expect[$k] else $x.expect end)}
    end' "$plants")
jq -n --argjson e "$entry" --argjson p "$planted" --arg arm "$PROCTOR_ARM" --arg task "$PROCTOR_TASK" \
  --arg sample "$PROCTOR_SAMPLE" --arg key "$key" --arg version "$version" \
  '{arm: $arm, task: $task, sample: $sample, variant: $e.variant, written_arm: $e.arm, key: $key, version: $version, expect: $p.expect}' \
  >"$TRIAL_RECORDS/$PROCTOR_ARM.$PROCTOR_TASK.$PROCTOR_SAMPLE.json"

config=$(mktemp)
jq -n --arg answer "$(jq -r .answer <<<"$planted")" \
  '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: [{Name: "Mock", Response: $answer}]}' >"$config"
printf 'tools none\nrun planted reply\n' | nb --output jsonl --config "$config" -
