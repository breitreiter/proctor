#!/usr/bin/env bash
set -euo pipefail
cd "$1"
grep -rl 'Legacy.Widgets' src tests | xargs sed -i 's/Legacy\.Widgets/Acme.Widgets/g'
