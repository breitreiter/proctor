#!/usr/bin/env bash
# The trial: the exam the agent wrote, run by proctor against planted runs of its own tasks and graded, so it is
# judged by what it passes and fails rather than by how it reads. Each written variant file is cloned once per
# plant in <task>/plants.json, its bundle and everything else kept, and the exam asks one attempt; hooks and the nb runner
# are dropped and trial/planted.sh answers for the bot. Model checks are dropped too, since nothing here can
# answer one; a model check named in correctness or integrity is a problem, since then no run could be decided.
# It needs no network: proctor and nb from PATH, the bot on nb's Mock provider.
#   env: TRIAL_TASK, TRIAL_OUT (a writable directory), WRITTEN_ROOT, WRITTEN_ID (checks/written.sh)
#   writes: $TRIAL_OUT/trial.json {problems: [...], attempts: [{plant, variant, task, key, version, expect, status, pass, failed}]},
#   the logs, the records planted.sh made, and the graded attempts (bank/, log/).
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
plants="$here/$TRIAL_TASK/plants.json"
out=$TRIAL_OUT
fail() { jq -n --arg e "$1" '{problems: [$e], attempts: []}' >"$out/trial.json"; echo "$1"; exit 0; }
[ -f "$plants" ] || fail "no planted runs for task $TRIAL_TASK"
mkdir -p "$out/records"

root="$(mktemp -d)/root"
cp -r "$WRITTEN_ROOT" "$root" || fail "could not copy the written exam"
id=$WRITTEN_ID; dir="$root/exams/$id"; def="$dir/exam.json"
rm -rf "$root/attempts" "$root/reports" "$root/.proctor" "$dir/live.json"
jq -e . "$def" >/dev/null 2>&1 || fail "exams/$id/exam.json is not JSON"
variants=("$dir"/variants/*.json)
[ -f "${variants[0]}" ] || fail "exams/$id has no variants/*.json"
jq -s . "${variants[@]}" >"$out/written-variants.json" 2>/dev/null || fail "a file in exams/$id/variants/ is not JSON"
agents=$(jq -r '[.[] | select(.agent != null) | .id] | join(", ")' "$out/written-variants.json")
[ -z "$agents" ] || fail "variant(s) $agents run an agent through Harbor; a chat exam's variants are bare nb"

# Model checks, wherever they are declared: the exam, its tasks, the fixtures.
spec_files=("$def" "$dir"/tasks/*.json "$root"/fixtures/*/fixture.json)
models='def asks: [to_entries[] | select(.value | type == "object" and (has("decide") or has("judge"))) | .key];'
asks_model=$(for f in "${spec_files[@]}"; do [ -f "$f" ] && jq -c "$models"' ((.grading.checks // .checks // {}) | asks)' "$f" 2>/dev/null; done | jq -s 'add // [] | unique')
in_role=$(jq -r --argjson m "$asks_model" '[(.grading.correctness // []) + (.grading.integrity // []) | .[] | select(. as $n | $m | index($n))] | first // empty' "$def")
[ -z "$in_role" ] || fail "'$in_role' asks a model and is named in correctness or integrity, so no run can be decided without one"
for f in "${spec_files[@]}"; do
  [ -f "$f" ] || continue
  jq --argjson m "$asks_model" 'def drop: with_entries(select(.key as $k | $m | index($k) | not));
    if .grading.checks then .grading.checks |= drop elif .checks then .checks |= drop else . end' "$f" >"$f.trial" && mv "$f.trial" "$f"
done

names=$(jq -c ".plants | keys_unsorted" "$plants")
jq -n --argjson v "$names" --slurpfile variants "$out/written-variants.json" \
  '[$v[] as $plant | $variants[0][] | {key: "\($plant)-\(.id)", value: {plant: $plant, variant: .id}}] | from_entries' >"$out/variants.json"
rm -f "$dir"/variants/*.json
for plant in $(jq -r '.[]' <<<"$names"); do
  jq -c --arg plant "$plant" '.[] | .id = "\($plant)-\(.id)"' "$out/written-variants.json" \
    | while read -r v; do jq . <<<"$v" >"$dir/variants/$(jq -r .id <<<"$v").json"; done
done
jq '.attempts = 1 | del(.hooks, .nb)' "$def" >"$def.trial" && mv "$def.trial" "$def"

# nb on Mock under every provider name the exam uses, so its program compiles whatever it names.
providers=$( { jq -r '.[].provider // empty' "$out/written-variants.json"; sed -nE 's/^[[:space:]]*provider[[:space:]]+([^[:space:]{]+)[[:space:]]*$/\1/p' "$dir/program.nb" 2>/dev/null; } | sort -u | grep -vx Mock)
jq -n --arg p "$providers" '{Harness: "nb", ActiveProvider: "Mock", Trust: false,
  ChatProviders: ([{Name: "Mock"}] + [$p | split("\n")[] | select(. != "") | {Name: ., Provider: "Mock"}])}' >"$root/exams/trial-nb.json"
jq -n --arg nb "$(command -v nb)" '{nb: {path: $nb, config: "trial-nb.json"}}' >"$root/exams/proctor.json"

export TRIAL_PLANTS="$plants" TRIAL_VARIANTS="$out/variants.json" TRIAL_RECORDS="$out/records"
cd "$root"
proctor run "$id" --root "$root" --runner "$here/planted.sh" >"$out/run.log" 2>&1 || fail "proctor run refused it: $(grep -v '^ ' "$out/run.log" | tail -1)"
made=$(sed -n 's/^run \([^ :]*\): .*/\1/p' "$out/run.log" | head -1)
[ -n "$made" ] || fail "proctor run made nothing: $(tail -1 "$out/run.log")"
proctor grade "$id" --root "$root" >"$out/grade.log" 2>&1 || fail "proctor grade failed: $(tail -1 "$out/grade.log")"
proctor report "$id" --root "$root" >"$out/report.log" 2>&1 || fail "proctor report failed: $(tail -1 "$out/report.log")"
cp -r bank log "$out/"
cp "reports/$id/explore/results.jsonl" "$out/results.jsonl"

jq -n --slurpfile plants "$plants" --slurpfile variants "$out/written-variants.json" \
  --slurpfile rec <(cat "$out"/records/*.json 2>/dev/null) --slurpfile res "$out/results.jsonl" '
  $plants[0] as $p | $variants[0] as $written | $rec as $r
  | ($p.versions | keys) as $required
  | ([$r[].version] | unique) as $seen
  | ($r | group_by(.written_variant) | map({key: .[0].written_variant, value: ([.[].version] | unique)}) | from_entries) as $by_variant
  | ($written | map({key: .id, value: .}) | from_entries) as $def
  | [ ($required[] | select(. as $v | $seen | index($v) | not) | $p.missing[.]),
      ([$r[] | select(.version == "ambiguous") | .written_variant] | unique | select(length > 0)
        | "variant(s) \(join(", ")) give the bot more than one version of its system prompt in one program"),
      ($by_variant | to_entries[] | select(.value | length > 1) | "variant \(.key) gives the bot a different prompt on different tasks (\(.value | join(", ")))"),
      ( if $p.compare then
          ([$by_variant | to_entries[] | select(.value == ["shipped"]) | .key]) as $shipped
          | ([$by_variant | to_entries[] | select(.value == ["draft"]) | .key]) as $draft
          | $shipped[] as $l | $draft[] as $d
          | ($def[$l] | del(.id, .note, .bundle)) as $a
          | ($def[$d] | del(.id, .note, .bundle)) as $b
          | (if $a != $b then "variants \($l) and \($d) differ in \([($a + $b) | keys[] | select($a[.] != $b[.])] | join(", ")) as well as the prompt, so a difference between them is not the rewrite alone" else empty end),
            (if $def[$l].bundle == $def[$d].bundle then "variants \($l) and \($d) have the same bundle, so their bundle is not what gives them different prompts" else empty end)
        else empty end ),
      ([$res[] | select(.status != "completed") | . as $row | select([$r[] | select(.variant == $row.variant and .task == $row.task)] | length == 0)]
        | select(length > 0) | "\(length) run(s) failed before the bot answered, so the exam never asked it: \(.[0].status_reason // .[0].reason // .[0].status)")
    ] as $problems
  | { problems: $problems,
      attempts: [ $r[] | . as $c | ([$res[] | select(.variant == $c.variant and .task == $c.task)] | first) as $row
        | {plant, variant: .written_variant, task, key, version, expect,
           status: ($row.status // "missing"), pass: $row.pass, invalid: $row.invalid,
           failed: [($row.checks // {}) | to_entries[] | select(.value != "pass") | "\(.key) \(.value)"]} ] }' >"$out/trial.json" \
  || fail "the trial could not read its own results: $(tail -1 "$out/results.jsonl" 2>/dev/null | cut -c1-120)"
echo "trial $made: $(jq -r '"\(.attempts | length) runs, \(.problems | length) problem(s)"' "$out/trial.json")"
