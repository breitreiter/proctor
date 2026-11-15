#!/usr/bin/env bash
# ask-user-question: the tool as the task's prompt specifies it, on the built nb with its Mock provider. Each
# case is one program; the first that does not hold is the reason.
set -uo pipefail
nb="$PROCTOR_WORK/bin/Debug/net10.0/nb"
[ -x "$nb" ] || { echo "no built nb at bin/Debug/net10.0/nb"; exit 1; }
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
echo '{"Harness": "nb", "ActiveProvider": "Mock", "ChatProviders": [{"Name": "Mock", "Response": "mock-ok"}]}' >"$tmp/nb.json"
printf '## deploy-target\nStaging only.\nNever touch prod during this exercise.\n\n## region\neu-west-1\n' >"$tmp/sheet.md"
none="The user has no answer to that question. Proceed on your own judgement."
spent="The user is no longer answering questions in this run."
ask() { echo "run MOCK:tool=ask_user_question $1"; }

# run <name> <program line>...: the run's events as one JSON array in $events, nb's exit code in $code
run() {
  local name=$1; shift
  (cd "$tmp" && printf '%s\n' "$@" | timeout 60 "$nb" --output jsonl --config "$tmp/nb.json" - 2>/dev/null) >"$tmp/$name.out"; code=$?
  events=$(grep '^{' "$tmp/$name.out" | jq -cs .)
}
# want <case> <what> <jq filter> <expected, as jq prints it>
want() {
  local got; got=$(jq -c "$3" <<<"$events" 2>/dev/null)
  [ "$got" = "$4" ] || { echo "$1: $2 is $(cut -c1-120 <<<"${got:-nothing}"), expected $4"; exit 1; }
}
result='[.[]|select(.type=="tool_result")]'
trailer='[.[]|select(.type=="result")][0]'

run hit "oracle @sheet.md" "$(ask 'Which environment? MOCK:oracle=deploy-target')"
want "an answered question" "the tool result" "$result[0].output" '"Staging only.\nNever touch prod during this exercise."'
want "an answered question" "the result's source" "$result[0].source" '"oracle"'
want "an answered question" "the result's keys" "$result[0].keys" '["deploy-target"]'
want "an answered question" "the exit reason" "$trailer.exit_reason" '"ok"'
want "an answered question" "the trailer's oracle_turns" "$trailer.oracle_turns" '1'
want "an answered question" "the model's turn after the answer" '[.[]|select(.type=="assistant_text")][-1].text' '"mock-ok"'
want "an answered question" "usage (two model turns, the question's judge call, the end-of-turn judge call)" "$trailer.usage|[.input,.output,.total]" '[40,20,60]'

run two "oracle @sheet.md" "$(ask 'Where and in which region? MOCK:oracle=deploy-target,region')"
want "a question two entries answer" "the result's keys" "$result[0].keys" '["deploy-target","region"]'
want "a question two entries answer" "the tool result ends with the second entry" "$result[0].output|endswith(\"eu-west-1\")" 'true'

run miss "oracle @sheet.md" "$(ask 'Which port? MOCK:oracle=MISS')"
want "a question the sheet does not answer" "the tool result" "$result[0].output" "\"$none\""
want "a question the sheet does not answer" "the result's source" "$result[0].source" '"oracle"'
want "a question the sheet does not answer" "the exit reason" "$trailer.exit_reason" '"ok"'
want "a question the sheet does not answer" "the trailer's oracle_turns" "$trailer.oracle_turns" 'null'
want "a question the sheet does not answer" "the model's turn after it" '[.[]|select(.type=="assistant_text")][-1].text' '"mock-ok"'
[ $code = 0 ] || { echo "a question the sheet does not answer: nb exited $code, expected 0"; exit 1; }

run budget "oracle @sheet.md" "budget oracle_turns 1" "$(ask 'Which environment? MOCK:oracle=deploy-target')" "$(ask 'Which region? MOCK:oracle=region')"
want "a second answer past budget oracle_turns 1" "the first result" "$result[0].keys" '["deploy-target"]'
want "a second answer past budget oracle_turns 1" "the second result" "$result[1].output" "\"$spent\""
want "a second answer past budget oracle_turns 1" "the exit reason" "$trailer.exit_reason" '"oracle_budget"'
want "a second answer past budget oracle_turns 1" "the trailer's oracle_turns" "$trailer.oracle_turns" '1'
[ $code = 3 ] || { echo "a second answer past budget oracle_turns 1: nb exited $code, expected 3"; exit 1; }

run shared "oracle @sheet.md" "budget oracle_turns 1" "run MOCK:response=Which environment? MOCK:oracle=deploy-target" "$(ask 'Which region? MOCK:oracle=region')"
want "the budget shared with an end-of-turn answer" "the tool result" "$result[0].output" "\"$spent\""
want "the budget shared with an end-of-turn answer" "the exit reason" "$trailer.exit_reason" '"oracle_budget"'

run bare "$(ask 'Which environment? MOCK:oracle=deploy-target')"
want "a program with no oracle" "the tool result (the tool must not be offered)" "$result[0].output|startswith(\"Error: Tool 'ask_user_question' not found\")" 'true'
want "a program with no oracle" "the result's source" "$result[0].source" 'null'

run removed "oracle @sheet.md" "tools -ask_user_question" "$(ask 'Which environment? MOCK:oracle=deploy-target')"
want "tools -ask_user_question" "the tool result (the tool must not be offered)" "$result[0].output|startswith(\"Error: Tool 'ask_user_question' not found\")" 'true'
[ -n "$events" ] && [ "$events" != "[]" ] || { echo "tools -ask_user_question: nb refused the program; the name must be one the tools directive accepts"; exit 1; }

run endofturn "oracle @sheet.md" "run MOCK:response=Which environment? MOCK:oracle=deploy-target"
want "an end-of-turn ask, as before" "the oracle's user turn" '[.[]|select(.type=="user" and .source=="oracle")][0].keys' '["deploy-target"]'
want "an end-of-turn ask, as before" "the exit reason" "$trailer.exit_reason" '"ok"'

echo "ask_user_question answers from the sheet, goes on after no answer, shares the budget and is offered only with an oracle"
