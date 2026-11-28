#!/usr/bin/env bash
# The trial: the exam the agent wrote, run by proctor against planted replies to its own stages and graded, so it
# is judged by what it passes and fails rather than by how it reads. The written exam gains one axis, `planted`,
# with a value per plant in <stage>/plants.json; each configuration it names is named once per plant, everything
# else of it kept, and the exam asks one try of each. Hooks and the nb runner are dropped and trial/planted.sh
# answers for the bot. Model checks are dropped too, since nothing here can answer one; a model check named in
# correctness or integrity is a problem, since then no run could be decided.
# It needs no network: proctor and nb from PATH, the bot on nb's Mock provider.
#   env: TRIAL_TASK (the stage of this exam being graded), TRIAL_OUT (a writable directory), WRITTEN_ROOT, WRITTEN_ID (checks/written.sh)
#   writes: $TRIAL_OUT/trial.json {problems: [...], runs: [{plant, configuration, stage, key, version, expect, status, pass, failed}]},
#   the logs, the records planted.sh made, and the graded tries (bank/, log/).
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
plants="$here/$TRIAL_TASK/plants.json"
out=$TRIAL_OUT
fail() { jq -n --arg e "$1" '{problems: [$e], runs: []}' >"$out/trial.json"; echo "$1"; exit 0; }
[ -f "$plants" ] || fail "no planted replies for $TRIAL_TASK"
mkdir -p "$out/records"

root="$(mktemp -d)/root"
cp -r "$WRITTEN_ROOT" "$root" || fail "could not copy the written exam"
id=$WRITTEN_ID; dir="$root/exams/$id"; def="$dir/exam.json"
rm -rf "$root/bank" "$root/log" "$root/exams-seen" "$root/reports" "$root/.proctor" "$dir/predictions" "$dir/retired.json" "$dir/combinations.json"
jq -e . "$def" >/dev/null 2>&1 || fail "exams/$id/exam.json is not JSON"
cp "$def" "$out/written-exam.json"
jq -e '(.axes | type == "object") and (.configurations | type == "object" and length > 0)' "$def" >/dev/null 2>&1 \
  || fail "exams/$id/exam.json names no configurations to run (axes, and configurations with one value on every axis)"
jq -e '.axes | has("planted") | not' "$def" >/dev/null || fail "exams/$id has an axis named planted, which the trial needs for itself"
agents=$(jq -r '[.axes | to_entries[] | .key as $a | (.value.values // {}) | to_entries[] | select(.value.agent != null or .value.route == "harbor") | "\($a)=\(.key)"] | join(", ")' "$def")
[ -z "$agents" ] || fail "$agents run(s) through Harbor; a chat exam's bot is bare nb"

# Model checks, wherever they are declared: the exam, its stages, the fixtures.
spec_files=("$def" "$root"/fixtures/*/fixture.json)
models='def asks: [to_entries[] | select(.value | type == "object" and (has("decide") or has("judge"))) | .key];'
asks_model=$(for f in "${spec_files[@]}"; do [ -f "$f" ] && jq -c "$models"' [((.grading.checks // .checks // {}) | asks), ((.stages // [])[] | (.checks // {}) | asks)] | add' "$f" 2>/dev/null; done | jq -s 'add // [] | unique')
in_role=$(jq -r --argjson m "$asks_model" '[(.grading.correctness // []) + (.grading.integrity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$def")
[ -z "$in_role" ] || fail "'$in_role' asks a model and is named in correctness or integrity, so no run can be decided without one"
for f in "${spec_files[@]}"; do
  [ -f "$f" ] || continue
  jq --argjson m "$asks_model" 'def drop: with_entries(select(.key as $k | $m | index($k) | not));
    (if .grading.checks then .grading.checks |= drop elif .checks then .checks |= drop else . end)
    | (if .grading.quality then .grading.quality |= map(select(. as $n | $m | index($n) | not)) else . end)
    | (if .stages then .stages |= map(if .checks then .checks |= drop else . end) else . end)' "$f" >"$f.trial" && mv "$f.trial" "$f"
done

# Each written configuration by its key, as proctor will spell it (axis=value in the exam's axis order), and
# the exam with the planted axis and each configuration named once per plant.
names=$(jq -c ".plants | keys_unsorted" "$plants")
jq '. as $e | [.configurations | to_entries[] | . as $c | {key: ([$e.axes | keys_unsorted[] | "\(.)=\($c.value[.])"] | join(",")), value: $c.key}] | from_entries' \
  "$def" >"$out/configurations.json" || fail "a configuration in exams/$id/exam.json could not be read"
jq --argjson plants "$names" '
  .attempts = 1 | del(.hooks, .nb)
  | .axes.planted = {about: "the planted reply the bot gives, which the trial adds", reference: $plants[0],
      values: ($plants | map({key: ., value: {note: "the trial answers as the plant \(.)"}}) | from_entries)}
  | .configurations = ([.configurations | to_entries[] as $c | $plants[] | {key: "\(.)-\($c.key)", value: ($c.value + {planted: .})}] | from_entries)' \
  "$def" >"$def.trial" && mv "$def.trial" "$def"

# nb on Mock under every provider name the exam uses, so its program compiles whatever it names.
providers=$( { jq -r '.axes[] | (.values // {})[] | .provider // empty' "$def"; sed -nE 's/^[[:space:]]*provider[[:space:]]+([^[:space:]{]+)[[:space:]]*$/\1/p' "$dir/program.nb" 2>/dev/null; } | sort -u | grep -vx Mock)
jq -n --arg p "$providers" '{Harness: "nb", ActiveProvider: "Mock", Trust: false,
  ChatProviders: ([{Name: "Mock"}] + [$p | split("\n")[] | select(. != "") | {Name: ., Provider: "Mock"}])}' >"$root/exams/trial-nb.json"
jq -n --arg nb "$(command -v nb)" '{nb: {path: $nb, config: "trial-nb.json"}}' >"$root/exams/proctor.json"

export TRIAL_PLANTS="$plants" TRIAL_CONFIGURATIONS="$out/configurations.json" TRIAL_RECORDS="$out/records"
cd "$root"
proctor run "$id" --root "$root" --runner "$here/planted.sh" >"$out/run.log" 2>&1 || fail "proctor run refused it: $(grep -v '^ ' "$out/run.log" | tail -1)"
made=$(sed -n 's/^run \([^ :]*\): .*/\1/p' "$out/run.log" | head -1)
[ -n "$made" ] || fail "proctor run made nothing: $(tail -1 "$out/run.log")"
proctor grade "$id" --root "$root" >"$out/grade.log" 2>&1 || fail "proctor grade failed: $(tail -1 "$out/grade.log")"
proctor report "$id" --root "$root" >"$out/report.log" 2>&1 || fail "proctor report failed: $(tail -1 "$out/report.log")"
cp -r bank log "$out/"
cp "reports/$id/explore/results.jsonl" "$out/results.jsonl"

# In results.jsonl a configuration's key is the field `variant` and a stage is `task`, as proctor first wrote them.
jq -n --slurpfile plants "$plants" --slurpfile exam "$out/written-exam.json" \
  --slurpfile rec <(cat "$out"/records/*.json 2>/dev/null) --slurpfile res "$out/results.jsonl" '
  $plants[0] as $p | $exam[0] as $e | $rec as $r
  | ($p.versions | keys) as $required
  | ([$r[].version] | unique) as $seen
  | ($r | group_by(.written) | map({key: .[0].written, value: ([.[].version] | unique)}) | from_entries) as $by_written
  | [ ($required[] | select(. as $v | $seen | index($v) | not) | $p.missing[.]),
      ([$r[] | select(.version == "ambiguous") | .written] | unique | select(length > 0)
        | "configuration(s) \(join(", ")) give the bot more than one version of its system prompt in one program"),
      ($by_written | to_entries[] | select(.value | length > 1) | "configuration \(.key) gives the bot a different prompt on different stages (\(.value | join(", ")))"),
      ( if $p.compare then
          ([$by_written | to_entries[] | select(.value == ["shipped"]) | .key]) as $shipped
          | ([$by_written | to_entries[] | select(.value == ["draft"]) | .key]) as $draft
          | $shipped[] as $l | $draft[] as $d
          | $e.configurations[$l] as $a | $e.configurations[$d] as $b
          | [$e.axes | keys_unsorted[] | select($a[.] != $b[.])] as $differ
          | if ($differ | length) != 1 then
              "configurations \($l) and \($d) differ on \($differ | length) axes (\($differ | join(", "))), so a difference between them is not the rewrite alone"
            else
              $differ[0] as $axis
              | ($e.axes[$axis].values[$a[$axis]] | del(.note, .why)) as $va
              | ($e.axes[$axis].values[$b[$axis]] | del(.note, .why)) as $vb
              | ($va | del(.files, .set)) as $ra | ($vb | del(.files, .set)) as $rb
              | (if $ra != $rb then "the values \($axis)=\($a[$axis]) and \($axis)=\($b[$axis]) differ in \([($ra + $rb) | keys[] | select($ra[.] != $rb[.])] | join(", ")) as well as the prompt, so a difference between them is not the rewrite alone" else empty end),
                (if $va == $vb then "the values \($axis)=\($a[$axis]) and \($axis)=\($b[$axis]) set and carry the same things, so the value is not what gives them different prompts" else empty end)
            end
        else empty end ),
      ([$res[] | select(.status != "completed") | . as $row | select([$r[] | select(.configuration == $row.variant and .stage == $row.task)] | length == 0)]
        | select(length > 0) | "\(length) run(s) failed before the bot answered, so the exam never asked it: \(.[0].status_reason // .[0].reason // .[0].status)")
    ] as $problems
  | { problems: $problems,
      runs: [ $r[] | . as $c | ([$res[] | select(.variant == $c.configuration and .task == $c.stage)] | first) as $row
        | {plant, configuration: .written, stage, key, version, expect,
           status: ($row.status // "missing"), pass: $row.pass, invalid: $row.invalid,
           failed: [($row.checks // {}) | to_entries[] | select(.value != "pass") | "\(.key) \(.value)"]} ] }' >"$out/trial.json" \
  || fail "the trial could not read its own results: $(tail -1 "$out/results.jsonl" 2>/dev/null | cut -c1-120)"
echo "trial $made: $(jq -r '"\(.runs | length) runs, \(.problems | length) problem(s)"' "$out/trial.json")"
