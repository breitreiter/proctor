#!/usr/bin/env bash
# Does proctor's list load the written exam with no problem? Only reads JSON, so it runs on the host.
set -uo pipefail
id=$(jq -r .exam <<<"$PROCTOR_EXPECT")
proctor=$(cd "$PROCTOR_EXAM_DIR/../../bin/Debug/net10.0" && pwd)/proctor.dll
[ -f "$PROCTOR_WORK/exams/$id/exam.json" ] || [ -f "$PROCTOR_WORK/evals/$id/eval.json" ] || { echo "no exams/$id/exam.json"; exit 1; }
err=$(mktemp); trap 'rm -f "$err"' EXIT
out=$(dotnet "$proctor" list "$id" --root "$PROCTOR_WORK" 2>"$err"); code=$?
problems=$(grep -c . "$err")
if [ "$code" -eq 0 ] && [ "$problems" -eq 0 ]; then echo "$(head -1 <<<"$out")"; exit 0; fi
echo "$problems problem(s): $(head -1 "$err" | cut -c1-160)"; exit 1
