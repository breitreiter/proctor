#!/usr/bin/env bash
# Prove the checks without a model: copy the exam into a scratch repository where the provider
# "gemini" is nb's Mock, script each stage's answer into its prompt, run and grade. Every stage of every try should
# pass with the right answers (the default) and none with --wrong. Needs jq and nb; costs nothing.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
nb="${NB_PATH:-$root/../nb/bin/Debug/net10.0/nb}"
proctor=(dotnet "$root/bin/Debug/net10.0/auspex.dll")
mode=right; [ "${1:-}" = "--wrong" ] && mode=wrong

declare -A right=([add-a-task]='jot add "buy milk"' [not-found-exit]=1 [store-location]=JOT_FILE [clear-exit-code]=0 [piped-yes]=No [clear-wait]=30)
declare -A wrong=([add-a-task]='jot new buy milk' [not-found-exit]=2 [store-location]=JOT_HOME [clear-exit-code]=1 [piped-yes]=Yes [clear-wait]=10)

tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/exams" "$tmp/fixtures"
cp -r "$here" "$tmp/exams/" && cp -r "$root/fixtures/empty" "$tmp/fixtures/"
printf '{ "nb": { "path": "%s", "config": "nb.json" } }\n' "$nb" > "$tmp/exams/proctor.json"
echo '{ "Harness": "nb", "ActiveProvider": "Mock", "ChatProviders": [ { "Name": "Mock", "Response": "OK" }, { "Name": "gemini", "Provider": "Mock" } ] }' > "$tmp/exams/nb.json"
declare -n answer=$mode
exam="$tmp/exams/readme-answers/exam.json"
for id in "${!answer[@]}"; do
  jq --arg id "$id" --arg a "${answer[$id]}" '(.stages[] | select(.id == $id) | .prompt) = "MOCK:response=ANSWER: \($a)\nWHY: scripted."' "$exam" > "$exam.new" && mv "$exam.new" "$exam"
done
cd "$tmp" && git init -q
"${proctor[@]}" run readme-answers > run.log
"${proctor[@]}" grade readme-answers | grep "^graded"
