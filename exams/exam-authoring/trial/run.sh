#!/usr/bin/env bash
# The trial, inside a container: the exam the agent wrote, run by proctor against planted runs of
# its own task and graded, so it is judged by what it passes and fails rather than by how it reads.
# Its variants/ is replaced by one variant per planted run in /trial/$TRIAL_TASK (good-* must pass, bad-*
# must fail), its hooks and runner by planted.sh, and its model checks dropped: a guardrail that
# asks a model decides nothing here, and one named in pass or validity makes the trial fail.
#   usage: run.sh <exam id>    mounts: /authored (the work directory, read-only), /trial (read-only),
#   /opt/proctor (the grading build), /out (writable): trial.json, the logs, the graded attempts (bank/, log/).
set -uo pipefail
id=$1
fail() { jq -n --arg e "$1" '{error: $e}' >/out/trial.json; echo "$1"; exit 0; }
root=/tmp/root
cp -r /authored "$root" && rm -rf "$root/attempts" "$root/reports" "$root/.proctor" "$root/exams/$id/live.json"
e="$root/exams/$id/exam.json"; defs="$root/exams"
[ -f "$e" ] || fail "no exams/$id/exam.json"
jq -e . "$e" >/dev/null 2>&1 || fail "exams/$id/exam.json is not JSON"

asks_model=$(jq -c '[.grading.checks // {} | to_entries[] | select(.value | type == "object" and (keys | any(test("(decide|judge)$")))) | .key]' "$e")
in_headline=$(jq -r --argjson m "$asks_model" '[(.grading.pass // []) + (.grading.validity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$e")
[ -z "$in_headline" ] || fail "'$in_headline' asks a model and is named in pass or validity, so no run can be decided without one"
rm -rf "$root/exams/$id/variants" && mkdir -p "$root/exams/$id/variants"
for f in "/trial/$TRIAL_TASK"/*.sh; do
  v=$(basename "$f" .sh)
  jq -n --arg id "$v" '{id: $id, runner: "nb", harness: "nb", provider: "Mock", model: "mock"}' >"$root/exams/$id/variants/$v.json"
done
jq --argjson m "$asks_model" \
   '.attempts = 1 | del(.hooks, .nb) | .grading.checks |= with_entries(select(.key as $k | $m | index($k) | not))' "$e" >"$e.trial"
mv "$e.trial" "$e"
jq -n '{nb: {path: "/opt/nb/nb", config: "trial-nb.json"}}' >"$defs/proctor.json"
jq -n '{Harness: "nb", ActiveProvider: "Mock", Trust: false, ChatProviders: [{Name: "Mock"}]}' >"$defs/trial-nb.json"

cd "$root"
proctor() { dotnet /opt/proctor/proctor.dll "$@"; }
proctor run "$id" --runner /trial/planted.sh >/out/run.log 2>&1 || fail "proctor run refused it: $(grep -v '^ ' /out/run.log | tail -1)"
made=$(sed -n 's/^run \([^ :]*\): .*/\1/p' /out/run.log | tail -1)
proctor grade "$id" >/out/grade.log 2>&1 || fail "proctor grade failed: $(tail -1 /out/grade.log)"
proctor report "$id" >/out/report.log 2>&1 || fail "proctor report failed: $(tail -1 /out/report.log)"
cp -r bank log /out/
jq -s '{variants: (group_by(.variant) | map({key: .[0].variant, value: map({task, status, pass, checks})}) | from_entries)}' \
  "reports/$id/explore/results.jsonl" >/out/trial.json
echo "trial $made: $(jq -r '.variants | to_entries | map("\(.key)=\(.value | map(.pass) | tostring)") | join(" ")' /out/trial.json)"
