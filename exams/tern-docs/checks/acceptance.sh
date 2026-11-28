#!/usr/bin/env bash
# Does the task's hidden acceptance pass? Starts Tern's sandbox API from the fixture on this container's loopback,
# with its test controls on and a request log, then runs acceptance/<task>.py with pytest against the changed shop.
# Port 8081 is what the shop's settings default to; if something already holds it, a spare port goes in TERN_API_URL.
set -uo pipefail
tests="$AUSPEX_EXAM_DIR/acceptance/$AUSPEX_STAGE.py"
[ -f "$tests" ] || { echo "no acceptance for stage $AUSPEX_STAGE"; exit 3; }
port=8081
python - <<<"import socket; s=socket.socket(); s.bind(('127.0.0.1', $port))" 2>/dev/null || port=18081
log=$(mktemp); out=$(mktemp)
TERN_PORT=$port TERN_TEST_CONTROL=1 TERN_LOG="$log" python -u "$AUSPEX_FIXTURE/services/tern_api.py" >/dev/null 2>&1 &
api=$!
trap 'kill $api 2>/dev/null; rm -f "$log" "$out"' EXIT
for _ in $(seq 50); do curl -fs -o /dev/null -X POST "http://127.0.0.1:$port/_test/reset" && break; sleep 0.1; done
cd "$AUSPEX_WORK"
TERN_API_URL="http://127.0.0.1:$port" TERN_CONTROL_URL="http://127.0.0.1:$port" PYTHONPATH="$AUSPEX_WORK" \
  timeout 300 python -m pytest -q -p no:cacheprovider --rootdir "$(dirname "$tests")" -c /dev/null "$tests" >"$out" 2>&1
code=$?
summary=$(grep -E '^[0-9]+ (passed|failed)|^(FAILED|ERROR) |[0-9]+ (passed|failed|error)' "$out" | tail -1 | sed -E 's/=+//g; s/^ +| +$//g')
if [ $code -eq 0 ]; then echo "acceptance passes: $summary"; exit 0; fi
first=$(grep -m1 -E '^E +' "$out" | sed -E 's/^E +//' | cut -c1-160)
echo "acceptance fails: $summary${first:+; $first}"; exit 1
