#!/usr/bin/env bash
# Prove the checks without a model or an agent: for each task, check the fixture out the way proctor does, lay
# the reference suite over it as the agent would have written it, collect the diff, and run every check that
# grades the written suite (the trial included, on nb's Mock provider). surface-present is left out: it reads
# the image, and the Harbor reference pass proves it (README.md).
#   (no flag)    the references: every check passes
#   --naive      each reference loses what made it discriminate (exact figures, the figure check, the anchored
#                refusal): rejects-wrong fails, naming what got through, and every other check still passes
#   --sloppy     each reference gets a key in its nb.json, loses its descriptions and its `tools none`:
#                chat-only, no-secrets and describes-everything fail, the trial still passes
#   --unsolved   nothing written: every check fails, saying there is no suite
# Needs proctor built (dotnet build at the repository root), nb (NB_PATH, else ../nb/bin/Debug/net10.0/nb) and jq.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
root="$(cd "$here/../.." && pwd)"
nb="${NB_PATH:-$root/../nb/bin/Debug/net10.0/nb}"
mode=${1:-}
case "$mode" in ""|--naive|--sloppy|--unsolved) ;; *) echo "usage: $0 [--naive|--sloppy|--unsolved]"; exit 2 ;; esac
tmp=$(mktemp -d)

# proctor and nb on PATH, as the image has them.
mkdir -p "$tmp/bin"
printf '#!/bin/sh\nexec dotnet %s "$@"\n' "$root/bin/Debug/net10.0/proctor.dll" >"$tmp/bin/proctor"
ln -s "$nb" "$tmp/bin/nb"
chmod +x "$tmp/bin/proctor"
export PATH="$tmp/bin:$PATH"

checks=(validates accepts-correct rejects-wrong chat-only no-secrets describes-everything)
expected() {  # the outcome each mode should give a check
  case "$mode:$1" in
    :*) echo pass ;;
    --naive:rejects-wrong) echo fail ;;
    --naive:*) echo pass ;;
    --sloppy:chat-only|--sloppy:no-secrets|--sloppy:describes-everything) echo fail ;;
    --sloppy:*) echo pass ;;
    --unsolved:no-secrets) echo pass ;;
    --unsolved:*) echo fail ;;
  esac
}

unexpected=0
for task_file in "$here"/tasks/*.json; do
  c=$(basename "$task_file" .json)
  f=$(jq -r .fixture "$task_file")
  export PROCTOR_SUITE_DIR="$here" PROCTOR_FIXTURE="$root/fixtures/$f" PROCTOR_EXPERIMENT=shakedown PROCTOR_ARM=ref PROCTOR_TASK="$c" PROCTOR_SAMPLE=1
  export PROCTOR_CELL="$tmp/$c/cell" PROCTOR_WORK="$tmp/$c/work" PROCTOR_DIFF="$tmp/$c/cell/diff.patch"
  export PROCTOR_TASK_JSON="$(cat "$task_file")" PROCTOR_EXPECT="$(jq -c '.expect // {}' "$task_file")" PROCTOR_TRANSCRIPT=""
  mkdir -p "$PROCTOR_CELL" "$PROCTOR_WORK"
  cp -r "$PROCTOR_FIXTURE/repo/." "$PROCTOR_WORK"
  (cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $f")
  if [ "$mode" != --unsolved ]; then cp -r "$here/reference/$c/." "$PROCTOR_WORK"; fi
  s=$(find "$PROCTOR_WORK/suites" -mindepth 2 -maxdepth 2 -name suite.json 2>/dev/null | head -1 | xargs -r dirname)
  edit() { jq "$2" "$1" >"$1.new" && mv "$1.new" "$1"; }
  if [ "$mode" = --naive ]; then
    case "$c" in
      exact-answers)  # the bare number, so 19 days passes for 9 and $12 a month for $12 a year
        declare -A bare=([loan-period]=9 [late-fee]=75 [membership]=12 [reserve-ahead]=3 [deposit-refund]=5)
        for t in "$s"/tasks/*.json; do edit "$t" ".expect.answer_regex = \"${bare[$(basename "$t" .json)]}\""; done ;;
      revised-prompt)  # compares the arms but checks only that the bot answered
        edit "$s/suite.json" '.grading.pass = ["answered"] | .grading.validity = []' ;;
      stays-on-topic)  # looks for the sentence anywhere, so declining and then playing along passes
        for t in "$s"/tasks/*.json; do edit "$t" 'if (.expect.answer_regex | startswith("^")) then .expect.answer_regex = "Sorry, I can only help with questions about the Bike Kitchen" else . end'; done ;;
    esac
  fi
  if [ "$mode" = --sloppy ]; then
    edit "$PROCTOR_WORK/suites/nb.json" '.ChatProviders[0].ApiKey = "sk-proj-shakedownFAKEkey000000000000"'
    edit "$s/suite.json" 'del(.description) | .arms |= map(del(.description))'
    sed -i '/^tools none$/d' "$s/program.nb"
  fi
  (cd "$PROCTOR_WORK" && git add -A -N . && git diff --no-color --binary >"$PROCTOR_DIFF")
  for check in "${checks[@]}"; do
    reason=$(cd "$PROCTOR_CELL" && "$here/checks/$check.sh"); code=$?
    got=$([ $code = 0 ] && echo pass || echo fail)
    want=$(expected "$check")
    mark=$([ "$got" = "$want" ] && echo "" || { echo " UNEXPECTED (want $want)"; })
    [ -z "$mark" ] || unexpected=$((unexpected + 1))
    printf '%-15s %-21s %-8s %s%s\n' "$c" "$check" "$([ $code = 0 ] && echo pass || echo "fail($code)")" "$(cut -c1-150 <<<"$reason")" "$mark"
  done
done
echo "trials kept in $tmp"
if [ $unexpected -gt 0 ]; then echo "$unexpected check(s) did not come out as expected"; exit 1; fi
echo "every check came out as expected${mode:+ for $mode}"
