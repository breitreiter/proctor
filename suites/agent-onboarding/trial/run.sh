#!/usr/bin/env bash
# The trial: the suite the agent wrote, run by proctor against planted runs of its own tasks and graded, so it is
# judged by what it passes and fails rather than by how it reads. Each written arm is cloned once per planted
# variant in <task>/plants.json, with one sample, its bundle and everything else kept; hooks and the nb runner
# are dropped and trial/planted.sh answers for the bot. Model checks are dropped too, since nothing here can
# answer one; a model check named in pass or validity is a problem, since then no run could be decided.
# It needs no network: proctor and nb from PATH, the bot on nb's Mock provider.
#   env: TRIAL_TASK, TRIAL_OUT (a writable directory), WRITTEN_ROOT, WRITTEN_ID (checks/written.sh)
#   writes: $TRIAL_OUT/trial.json {problems: [...], cells: [{variant, arm, task, key, version, expect, status, pass, failed}]},
#   the logs, the records planted.sh made, and the graded experiment.
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
plants="$here/$TRIAL_TASK/plants.json"
out=$TRIAL_OUT
fail() { jq -n --arg e "$1" '{problems: [$e], cells: []}' >"$out/trial.json"; echo "$1"; exit 0; }
[ -f "$plants" ] || fail "no planted runs for task $TRIAL_TASK"
mkdir -p "$out/records"

root="$(mktemp -d)/root"
cp -r "$WRITTEN_ROOT" "$root" || fail "could not copy the written suite"
id=$WRITTEN_ID; dir="$root/suites/$id"; def="$dir/suite.json"
rm -rf "$root/runs" "$root/reports" "$root/.proctor" "$dir/baseline.json"
jq -e . "$def" >/dev/null 2>&1 || fail "suites/$id/suite.json is not JSON"
agents=$(jq -r '[.arms // [] | .[] | select(.agent != null) | .id] | join(", ")' "$def")
[ -z "$agents" ] || fail "arm(s) $agents run an agent through Harbor; a chat suite's arms are bare nb"

# Model checks, wherever they are declared: the suite, its tasks, the fixtures.
spec_files=("$def" "$dir"/tasks/*.json "$root"/fixtures/*/fixture.json)
models='def asks: [to_entries[] | select(.value | type == "object" and (has("decide") or has("judge"))) | .key];'
asks_model=$(for f in "${spec_files[@]}"; do [ -f "$f" ] && jq -c "$models"' ((.grading.checks // .checks // {}) | asks)' "$f" 2>/dev/null; done | jq -s 'add // [] | unique')
headline=$(jq -r --argjson m "$asks_model" '[(.grading.pass // []) + (.grading.validity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$def")
[ -z "$headline" ] || fail "'$headline' asks a model and is named in pass or validity, so no run can be decided without one"
for f in "${spec_files[@]}"; do
  [ -f "$f" ] || continue
  jq --argjson m "$asks_model" 'def drop: with_entries(select(.key as $k | $m | index($k) | not));
    if .grading.checks then .grading.checks |= drop elif .checks then .checks |= drop else . end' "$f" >"$f.trial" && mv "$f.trial" "$f"
done

jq -c '.arms' "$def" >"$out/written-arms.json"
variants=$(jq -c '.variants | keys_unsorted' "$plants")
jq -n --argjson v "$variants" --slurpfile arms "$out/written-arms.json" \
  '[$v[] as $variant | $arms[0][] | {key: "\($variant)-\(.id)", value: {variant: $variant, arm: .id}}] | from_entries' >"$out/arms.json"
jq --argjson v "$variants" '.arms as $arms | .arms = [$v[] as $variant | $arms[] | . + {id: "\($variant)-\(.id)", samples: 1}] | del(.hooks, .nb)' \
  "$def" >"$def.trial" && mv "$def.trial" "$def"

# nb on Mock under every provider name the suite uses, so its program compiles whatever it names.
providers=$( { jq -r '.arms[].provider // empty' "$def"; sed -nE 's/^[[:space:]]*provider[[:space:]]+([^[:space:]{]+)[[:space:]]*$/\1/p' "$dir/program.nb" 2>/dev/null; } | sort -u | grep -vx Mock)
jq -n --arg p "$providers" '{Harness: "nb", ActiveProvider: "Mock", Trust: false,
  ChatProviders: ([{Name: "Mock"}] + [$p | split("\n")[] | select(. != "") | {Name: ., Provider: "Mock"}])}' >"$root/suites/trial-nb.json"
jq -n --arg nb "$(command -v nb)" '{nb: {path: $nb, config: "trial-nb.json"}}' >"$root/suites/proctor.json"

export TRIAL_PLANTS="$plants" TRIAL_ARMS="$out/arms.json" TRIAL_RECORDS="$out/records"
cd "$root"
proctor run "$id" --root "$root" --runner "$here/planted.sh" >"$out/run.log" 2>&1 || fail "proctor run refused it: $(grep -v '^ ' "$out/run.log" | tail -1)"
experiment=$(sed -n 's/^experiment //p' "$out/run.log" | head -1)
[ -n "$experiment" ] || fail "proctor run printed no experiment: $(tail -1 "$out/run.log")"
proctor grade "$experiment" --root "$root" >"$out/grade.log" 2>&1 || fail "proctor grade failed: $(tail -1 "$out/grade.log")"
proctor report "$experiment" --root "$root" >"$out/report.log" 2>&1 || fail "proctor report failed: $(tail -1 "$out/report.log")"
cp -r "runs/$experiment" "$out/experiment"
cp "reports/data/$experiment/results.jsonl" "$out/results.jsonl"

jq -n --slurpfile plants "$plants" --slurpfile arms "$out/written-arms.json" \
  --slurpfile rec <(cat "$out"/records/*.json 2>/dev/null) --slurpfile res "$out/results.jsonl" '
  $plants[0] as $p | $arms[0] as $written | $rec as $r
  | ($p.versions | keys) as $required
  | ([$r[].version] | unique) as $seen
  | ($r | group_by(.written_arm) | map({key: .[0].written_arm, value: ([.[].version] | unique)}) | from_entries) as $by_arm
  | ($written | map({key: .id, value: .}) | from_entries) as $def
  | [ ($required[] | select(. as $v | $seen | index($v) | not) | $p.missing[.]),
      ([$r[] | select(.version == "ambiguous") | .written_arm] | unique | select(length > 0)
        | "arm(s) \(join(", ")) give the bot more than one version of its system prompt in one program"),
      ($by_arm | to_entries[] | select(.value | length > 1) | "arm \(.key) gives the bot a different prompt on different tasks (\(.value | join(", ")))"),
      ( if $p.compare then
          ([$by_arm | to_entries[] | select(.value == ["live"]) | .key]) as $live
          | ([$by_arm | to_entries[] | select(.value == ["draft"]) | .key]) as $draft
          | $live[] as $l | $draft[] as $d
          | ($def[$l] | del(.id, .description, .samples, .bundle, .labels)) as $a
          | ($def[$d] | del(.id, .description, .samples, .bundle, .labels)) as $b
          | (if $a != $b then "arms \($l) and \($d) differ in \([($a + $b) | keys[] | select($a[.] != $b[.])] | join(", ")) as well as the prompt, so a difference between them is not the rewrite alone" else empty end),
            (if $def[$l].bundle == $def[$d].bundle then "arms \($l) and \($d) have the same bundle, so their bundle is not what gives them different prompts" else empty end)
        else empty end ),
      ([$res[] | select(.status != "completed") | . as $row | select([$r[] | select(.arm == $row.arm and .task == $row.task)] | length == 0)]
        | select(length > 0) | "\(length) run(s) failed before the bot answered, so the suite never asked it: \(.[0].status_reason // .[0].reason // .[0].status)")
    ] as $problems
  | { problems: $problems,
      cells: [ $r[] | . as $c | ([$res[] | select(.arm == $c.arm and .task == $c.task)] | first) as $row
        | {variant, arm: .written_arm, task, key, version, expect,
           status: ($row.status // "missing"), pass: $row.pass, invalid: $row.invalid,
           failed: [($row.checks // {}) | to_entries[] | select(.value != "pass") | "\(.key) \(.value)"]} ] }' >"$out/trial.json" \
  || fail "the trial could not read its own results: $(tail -1 "$out/results.jsonl" 2>/dev/null | cut -c1-120)"
echo "trial $experiment: $(jq -r '"\(.cells | length) runs, \(.problems | length) problem(s)"' "$out/trial.json")"
