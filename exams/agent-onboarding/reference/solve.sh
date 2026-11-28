#!/usr/bin/env bash
# Harbor's solution for one stage: lays the reference written exam beside this script (a copy of
# reference/<stage>/, as solution/files/) over the checkout, as the agent would have written it.
#   usage: solve.sh [work directory, default /app]
set -euo pipefail
cp -r "$(dirname "$0")/files/." "${1:-/app}"
