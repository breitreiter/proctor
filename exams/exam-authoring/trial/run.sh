#!/usr/bin/env bash
# The trial, inside a container: the exam the agent wrote, run by proctor against planted runs of
# its own stage and graded, so it is judged by what it passes and fails rather than by how it reads.
# The written exam gains one axis, `planted`, with a value per planted run in /trial/$TRIAL_TASK (good-* must
# pass, bad-* must fail); each configuration it names is named once per plant, and every provider it names
# is nb's Mock here. Its hooks and runner are replaced by planted.sh, and its model checks dropped: one that
# asks a model decides nothing here, and one named in correctness or integrity makes the trial fail.
#   usage: run.sh <exam id>    mounts: /authored (the work directory, read-only), /trial (read-only),
#   /opt/proctor (the grading build), /out (writable): trial.json {plants: {<plant>: [{stage, status, pass, checks}]}},
#   the logs, the graded tries (bank/, log/).
set -uo pipefail
id=$1
fail() { jq -n --arg e "$1" '{error: $e}' >/out/trial.json; echo "$1"; exit 0; }
root=/tmp/root
cp -r /authored "$root" && rm -rf "$root/bank" "$root/log" "$root/exams-seen" "$root/reports" "$root/.proctor" "$root/.bundle" \
  "$root/exams/$id/predictions" "$root/exams/$id/retired.json" "$root/exams/$id/combinations.json"
e="$root/exams/$id/exam.json"; defs="$root/exams"
[ -f "$e" ] || fail "no exams/$id/exam.json"
jq -e . "$e" >/dev/null 2>&1 || fail "exams/$id/exam.json is not JSON"
jq -e '(.axes | type == "object") and (.configurations | type == "object" and length > 0)' "$e" >/dev/null 2>&1 \
  || fail "exams/$id/exam.json names no configurations to run (axes, and configurations with one value on every axis)"
jq -e '.axes | has("planted") | not' "$e" >/dev/null || fail "exams/$id has an axis named planted, which the trial needs for itself"

models='def asks: [to_entries[] | select(.value | type == "object" and (keys | any(test("(decide|judge)$")))) | .key];'
asks_model=$(jq -c "$models"' [((.grading.checks // {}) | asks), ((.stages // [])[] | (.checks // {}) | asks)] | add | unique' "$e")
in_role=$(jq -r --argjson m "$asks_model" '[(.grading.correctness // []) + (.grading.integrity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$e")
[ -z "$in_role" ] || fail "'$in_role' asks a model and is named in correctness or integrity, so no run can be decided without one"
plants=$(for f in "/trial/$TRIAL_TASK"/*.sh; do basename "$f" .sh; done | jq -R . | jq -sc .)
jq --argjson m "$asks_model" --argjson plants "$plants" '
   def drop: with_entries(select(.key as $k | $m | index($k) | not));
   .attempts = 1 | del(.hooks, .nb) | .grading.checks |= drop
   | (if .grading.quality then .grading.quality |= map(select(. as $n | $m | index($n) | not)) else . end)
   | .stages |= map(if .checks then .checks |= drop else . end)
   | .axes.planted = {about: "the planted run, which the trial adds", reference: $plants[0],
       values: ($plants | map({key: ., value: {note: "the trial does what the planted run \(.) did"}}) | from_entries)}
   | .configurations = ([.configurations | to_entries[] as $c | $plants[] | {key: "\(.)-\($c.key)", value: ($c.value + {planted: .})}] | from_entries)' \
   "$e" >"$e.trial" || fail "exams/$id/exam.json could not be read as an exam of stages and axes"
mv "$e.trial" "$e"
providers=$(jq -c '[.axes[] | (.values // {})[] | .provider // empty] | unique | map(select(. != "Mock"))' "$e")
jq -n '{nb: {path: "/opt/nb/nb", config: "trial-nb.json"}}' >"$defs/proctor.json"
jq -n --argjson p "$providers" '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: ([{Name: "Mock"}] + [$p[] | {Name: ., Provider: "Mock"}])}' >"$defs/trial-nb.json"

cd "$root"
proctor() { dotnet /opt/proctor/proctor.dll "$@"; }
proctor run "$id" --runner /trial/planted.sh >/out/run.log 2>&1 || fail "proctor run refused it: $(grep -v '^ ' /out/run.log | tail -1)"
made=$(sed -n 's/^run \([^ :]*\): .*/\1/p' /out/run.log | tail -1)
proctor grade "$id" >/out/grade.log 2>&1 || fail "proctor grade failed: $(tail -1 /out/grade.log)"
proctor report "$id" >/out/report.log 2>&1 || fail "proctor report failed: $(tail -1 /out/report.log)"
cp -r bank log /out/
# In results.jsonl the proctor pinned in the image says `variant` and `task` where the host's says `configuration` and `stage`; both are read until the pin moves past the rename.
jq -s '{plants: (map(. + {plant: ((.configuration // .variant) | capture("(^|,)planted=(?<p>[^,]+)").p)}) | group_by(.plant)
        | map({key: .[0].plant, value: map({stage: (.stage // .task), status, pass, checks})}) | from_entries)}' \
  "reports/$id/explore/results.jsonl" >/out/trial.json
echo "trial $made: $(jq -r '.plants | to_entries | map("\(.key)=\(.value | map(.pass) | tostring)") | join(" ")' /out/trial.json)"
