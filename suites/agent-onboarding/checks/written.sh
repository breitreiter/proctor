#!/usr/bin/env bash
# Not a check: every check sources it. Finds the suite the agent wrote in $PROCTOR_WORK, from what git says is
# new or changed since the fixture commit, so it works in Harbor's offline verifier (the diff applied, no
# transcript) and on the host alike. The prompt names no suite id and no layout, so the suite may sit under
# any root: <root>/suites/<id>/suite.json. Proctor's own scratch (.proctor/, runs/, reports/) is ignored, and
# so is a suite whose id says it is a scratch copy when there is another. Sets WRITTEN_ROOT (absolute),
# WRITTEN_ID and WRITTEN_DIR, or prints why there is nothing to grade and exits 1.
found=$(git -C "$PROCTOR_WORK" status --porcelain --untracked-files=all 2>/dev/null \
  | sed -E 's/^.. //; s/^"(.*)"$/\1/' \
  | grep -E '(^|/)suites/[^/]+/suite\.json$' \
  | grep -vE '(^|/)(\.proctor|runs|reports)/' | sort -u)
if [ "$(grep -c . <<<"$found")" -gt 1 ]; then
  kept=$(grep -vE '(^|/)suites/[^/]*(mock|shakedown|smoke|scratch|tmp|trial|test)[^/]*/suite\.json$' <<<"$found")
  [ -n "$kept" ] && found=$kept
fi
case "$(grep -c . <<<"$found")" in
  0) echo "no suite written: no new suites/<id>/suite.json in the work directory"; exit 1 ;;
  1) ;;
  *) echo "$(grep -c . <<<"$found") suites written, expected one: $(paste -sd' ' <<<"$found")"; exit 1 ;;
esac
WRITTEN_ID=$(basename "$(dirname "$found")")
rel=${found%suites/"$WRITTEN_ID"/suite.json}
WRITTEN_ROOT=$(cd "$PROCTOR_WORK/${rel:-.}" && pwd)
WRITTEN_DIR="$WRITTEN_ROOT/suites/$WRITTEN_ID"
