#!/usr/bin/env bash
# Reference solution for clear-for-scripts, applied to the checkout in $1. A patch, not edits, so it
# fails loudly when the fixture moves.
set -euo pipefail
cd "$1"
git apply "$(dirname "$0")/clear-for-scripts.patch"
