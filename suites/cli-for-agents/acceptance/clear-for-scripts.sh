#!/usr/bin/env bash
# Acceptance for clear-for-scripts, against the checkout in $1. Exit 0/1 = pass/fail; the first stdout
# line is the reason. Every invocation runs with stdin closed and no controlling terminal (setsid), under
# a ten-second limit, so the fixture's thirty-second wait fails on time rather than on behaviour.
set -uo pipefail
work="$1"
cd "$work" || { echo "no checkout at $work"; exit 1; }
[ -d node_modules ] || npm ci --no-audit --no-fund --silent >/dev/null 2>&1 || { echo "npm ci failed"; exit 1; }
npx tsc --pretty false >/dev/null 2>&1 || { echo "does not compile"; exit 1; }

store=$(mktemp)
seed() { printf '[{"id":1,"text":"one","done":false},{"id":2,"text":"two","done":true}]\n' > "$store"; }
jot() { JOT_FILE="$store" JOT_CONFIRM_TIMEOUT_MS=30000 timeout 10 setsid node dist/src/cli.js "$@" </dev/null; }
count() { node -e 'const t=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")||"[]");console.log(t.length)' "$store"; }

# 1. --yes clears without asking, promptly, and exits 0.
seed
jot clear --yes >/dev/null 2>&1; code=$?
if [ "$code" -eq 124 ]; then echo "clear --yes waited for a terminal"; exit 1; fi
if [ "$code" -ne 0 ]; then echo "clear --yes exited $code"; exit 1; fi
if [ "$(count)" -ne 0 ]; then echo "clear --yes left $(count) tasks"; exit 1; fi

# 2. -y is the same.
seed
jot clear -y >/dev/null 2>&1; code=$?
if [ "$code" -ne 0 ] || [ "$(count)" -ne 0 ]; then echo "clear -y exited $code with $(count) tasks left"; exit 1; fi

# 3. Without the flag and without a terminal: no wait, nothing cleared, non-zero exit.
seed
jot clear >/dev/null 2>&1; code=$?
if [ "$code" -eq 124 ]; then echo "clear without --yes still waits for a terminal"; exit 1; fi
if [ "$code" -eq 0 ]; then echo "clear without --yes exited 0 without clearing"; exit 1; fi
if [ "$(count)" -ne 2 ]; then echo "clear without --yes changed the store"; exit 1; fi

# 4. The rest of the tool is untouched.
seed
out=$(jot list 2>&1)
[ "$out" = $'#1 [ ] one\n#2 [x] two' ] || { echo "list output changed: $(head -1 <<<"$out")"; exit 1; }

echo "clear --yes clears, clear alone refuses with exit $code, nothing waits"
exit 0
