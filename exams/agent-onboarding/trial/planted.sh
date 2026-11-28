#!/usr/bin/env bash
# The trial's runner, standing in for the bot: proctor hands it the program it compiled from the written exam
# for this stage, and it answers as the planted reply would, through nb's Mock provider, so the transcript is
# nb's own. It reads which prompt the program gives the bot from the markers in plants.json (versions), which
# question the stage asks from its prompt (questions, first match), and the plant from the axis run.sh added;
# then it records what it planted and what the exam should make of it (expect: pass, fail, or none when the
# stage asks something the trial does not know or the program carries no bot).
#   env: TRIAL_PLANTS (plants.json), TRIAL_CONFIGURATIONS (a written configuration's key -> its name), TRIAL_RECORDS (a directory)
set -euo pipefail
program=$(cat)
plants=$TRIAL_PLANTS
# The proctor that runs this is the one pinned in the fixture's image in a real try and the host's in the shakedown,
# and the two name a stage differently until the pin is moved past the rename: both names are read here.
stage_id=${PROCTOR_STAGE:-${PROCTOR_TASK:-}} stage_json=${PROCTOR_STAGE_JSON:-${PROCTOR_TASK_JSON:-}}
plant=$PROCTOR_AXIS_PLANTED
written_key=${PROCTOR_CONFIGURATION%",planted=$plant"}
written=$(jq -r --arg k "$written_key" '.[$k] // $k' "$TRIAL_CONFIGURATIONS")
prompt=$(jq -r '.prompt // ""' <<<"$stage_json")

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

planted=$(jq -c --arg v "$plant" --arg ver "$version" --arg k "$key" '
  .dodge as $dodge
  | (.plants[$v] | . + (.by_version[$ver] // {})) as $x
  | if $ver == "none" or $ver == "ambiguous" then {answer: $dodge, expect: "none"}
    else {answer: ($x.answers[$k] // $x.answers["*"]),
          expect: (if $k == "*" then "none" elif ($x.expect | type) == "object" then $x.expect[$k] else $x.expect end)}
    end' "$plants")
jq -n --argjson p "$planted" --arg configuration "$PROCTOR_CONFIGURATION" --arg stage "$stage_id" \
  --arg plant "$plant" --arg written "$written" --arg key "$key" --arg version "$version" \
  '{configuration: $configuration, stage: $stage, plant: $plant, written: $written, key: $key, version: $version, expect: $p.expect}' \
  >"$TRIAL_RECORDS/$PROCTOR_CONFIGURATION.$stage_id.json"

config=$(mktemp)
jq -n --arg answer "$(jq -r .answer <<<"$planted")" \
  '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: [{Name: "Mock", Response: $answer}]}' >"$config"
printf 'tools none\nrun planted reply\n' | nb --output jsonl --config "$config" -
