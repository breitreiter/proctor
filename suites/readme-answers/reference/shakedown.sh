#!/usr/bin/env bash
# Prove the checks without a model: copy the suite into a scratch repository where the provider
# "gemini" is nb's Mock, script each task's answer into its prompt, run and grade. Every run should
# pass with the right answers (the default) and none with --wrong. Needs jq and nb; costs nothing.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
nb="${NB_PATH:-$root/../nb/bin/Debug/net10.0/nb}"
proctor=(dotnet "$root/bin/Debug/net10.0/proctor.dll")
mode=right; [ "${1:-}" = "--wrong" ] && mode=wrong

declare -A right=([add-a-task]='jot add "buy milk"' [not-found-exit]=1 [store-location]=JOT_FILE [clear-exit-code]=0 [piped-yes]=No [clear-wait]=30)
declare -A wrong=([add-a-task]='jot new buy milk' [not-found-exit]=2 [store-location]=JOT_HOME [clear-exit-code]=1 [piped-yes]=Yes [clear-wait]=10)

tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/suites" "$tmp/fixtures"
cp -r "$here" "$tmp/suites/" && cp -r "$root/fixtures/empty" "$tmp/fixtures/"
printf '{ "nb": { "path": "%s", "config": "nb.json" } }\n' "$nb" > "$tmp/suites/proctor.json"
echo '{ "Harness": "nb", "ActiveProvider": "Mock", "ChatProviders": [ { "Name": "Mock", "Response": "OK" }, { "Name": "gemini", "Provider": "Mock" } ] }' > "$tmp/suites/nb.json"
for f in "$tmp"/suites/readme-answers/tasks/*.json; do
  id=$(basename "$f" .json)
  if [ $mode = right ]; then answer=${right[$id]}; else answer=${wrong[$id]}; fi
  jq --arg a "$answer" '.prompt = "MOCK:response=ANSWER: \($a)\nWHY: scripted."' "$f" > "$f.new" && mv "$f.new" "$f"
done
cd "$tmp" && git init -q
"${proctor[@]}" run readme-answers > run.log
experiment=$(sed -n 's/^experiment //p' run.log)
"${proctor[@]}" grade "$experiment" | tail -1
