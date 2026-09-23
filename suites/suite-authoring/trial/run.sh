#!/usr/bin/env bash
# The trial, inside a container: the suite the agent wrote, run by proctor against planted runs of
# its own task and graded, so it is judged by what it passes and fails rather than by how it reads.
# Its arms are replaced by one arm per planted run in /trial/$TRIAL_TASK (good-* must pass, bad-*
# must fail), its hooks and runner by planted.sh, and its model checks dropped: a guardrail that
# asks a model decides nothing here, and one named in pass or validity makes the trial fail.
#   usage: run.sh <suite id>    mounts: /authored (the work directory, read-only), /trial (read-only),
#   /opt/proctor (the grading build), /out (writable): trial.json, the logs, the graded experiment.
set -uo pipefail
id=$1
fail() { jq -n --arg e "$1" '{error: $e}' >/out/trial.json; echo "$1"; exit 0; }
root=/tmp/root
cp -r /authored "$root" && rm -rf "$root/runs" "$root/reports" "$root/.proctor" "$root/suites/$id/baseline.json" "$root/evals/$id/baseline.json"
e="$root/suites/$id/suite.json"; defs="$root/suites"
# The layout before suite/task, which an agent shown an older bundle still writes; proctor reads both.
[ -f "$e" ] || { e="$root/evals/$id/eval.json"; defs="$root/evals"; }
[ -f "$e" ] || fail "no suites/$id/suite.json"
jq -e . "$e" >/dev/null 2>&1 || fail "suites/$id/suite.json is not JSON"

asks_model=$(jq -c '[.grading.checks // {} | to_entries[] | select(.value | type == "object" and (keys | any(test("(decide|judge)$")))) | .key]' "$e")
in_headline=$(jq -r --argjson m "$asks_model" '[(.grading.pass // []) + (.grading.validity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$e")
[ -z "$in_headline" ] || fail "'$in_headline' asks a model and is named in pass or validity, so no run can be decided without one"
arms=$(for f in "/trial/$TRIAL_TASK"/*.sh; do
         jq -n --arg id "$(basename "$f" .sh)" '{id: $id, runner: "nb", harness: "nb", provider: "Mock", model: "mock", samples: 1}'
       done | jq -s .)
jq --argjson arms "$arms" --argjson m "$asks_model" \
   '.arms = $arms | del(.hooks, .nb) | .grading.checks |= with_entries(select(.key as $k | $m | index($k) | not))' "$e" >"$e.trial"
mv "$e.trial" "$e"
jq -n '{nb: {path: "/opt/nb/nb", config: "trial-nb.json"}}' >"$defs/proctor.json"
jq -n '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: [{Name: "Mock"}]}' >"$defs/trial-nb.json"

cd "$root"
proctor() { dotnet /opt/proctor/proctor.dll "$@"; }
proctor run "$id" --runner /trial/planted.sh >/out/run.log 2>&1 || fail "proctor run refused it: $(grep -v '^ ' /out/run.log | tail -1)"
experiment=$(sed -n 's/^experiment //p' /out/run.log)
proctor grade "$experiment" >/out/grade.log 2>&1 || fail "proctor grade failed: $(tail -1 /out/grade.log)"
proctor report "$experiment" >/out/report.log 2>&1 || fail "proctor report failed: $(tail -1 /out/report.log)"
cp -r "runs/$experiment" /out/experiment
jq -s '{arms: (group_by(.arm) | map({key: .[0].arm, value: map({task, status, pass, checks})}) | from_entries)}' \
  "reports/data/$experiment/results.jsonl" >/out/trial.json
echo "trial $experiment: $(jq -r '.arms | to_entries | map("\(.key)=\(.value | map(.pass) | tostring)") | join(" ")' /out/trial.json)"
