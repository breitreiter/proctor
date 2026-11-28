#!/usr/bin/env bash
# Not a check: every check sources it. Finds the exam the agent wrote in $PROCTOR_WORK, from what git says is
# new or changed since the fixture commit, so it works in Harbor's offline verifier (the diff applied, no
# transcript) and on the host alike. The prompt names no exam id and no layout, so the exam may sit under
# any root: <root>/exams/<id>/exam.json. Proctor's own scratch (.proctor/, bank/, log/, exams-seen/, reports/) is ignored, and
# so is an exam whose id says it is a scratch copy when there is another. Sets WRITTEN_ROOT (absolute),
# WRITTEN_ID and WRITTEN_DIR, or prints why there is nothing to grade and exits 1.
found=$(git -C "$PROCTOR_WORK" status --porcelain --untracked-files=all 2>/dev/null \
  | sed -E 's/^.. //; s/^"(.*)"$/\1/' \
  | grep -E '(^|/)exams/[^/]+/exam\.json$' \
  | grep -vE '(^|/)(\.proctor|bank|log|exams-seen|reports)/' | sort -u)
if [ "$(grep -c . <<<"$found")" -gt 1 ]; then
  kept=$(grep -vE '(^|/)exams/[^/]*(mock|shakedown|smoke|scratch|tmp|trial|test)[^/]*/exam\.json$' <<<"$found")
  [ -n "$kept" ] && found=$kept
fi
case "$(grep -c . <<<"$found")" in
  0) echo "no exam written: no new exams/<id>/exam.json in the work directory"; exit 1 ;;
  1) ;;
  *) echo "$(grep -c . <<<"$found") exams written, expected one: $(paste -sd' ' <<<"$found")"; exit 1 ;;
esac
WRITTEN_ID=$(basename "$(dirname "$found")")
rel=${found%exams/"$WRITTEN_ID"/exam.json}
WRITTEN_ROOT=$(cd "$PROCTOR_WORK/${rel:-.}" && pwd)
WRITTEN_DIR="$WRITTEN_ROOT/exams/$WRITTEN_ID"
