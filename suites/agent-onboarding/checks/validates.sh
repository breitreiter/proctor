#!/usr/bin/env bash
# Did the agent write one suite, and does proctor's list load it with no problem? The proctor that judges is
# the one on PATH: in the verifier, the image's build at the pinned revision, the same the agent ran.
set -uo pipefail
. "$PROCTOR_SUITE_DIR/checks/written.sh"
err=$(mktemp); trap 'rm -f "$err"' EXIT
out=$(cd "$WRITTEN_ROOT" && proctor list "$WRITTEN_ID" --root "$WRITTEN_ROOT" 2>"$err"); code=$?
problems=$(grep -c . "$err")
where=${WRITTEN_DIR#"$PROCTOR_WORK"/}
if [ "$code" -eq 0 ] && [ "$problems" -eq 0 ]; then echo "$where loads: $(head -1 <<<"$out")"; exit 0; fi
echo "$where: $problems problem(s), exit $code: $(head -1 "$err" | cut -c1-200)"; exit 1
