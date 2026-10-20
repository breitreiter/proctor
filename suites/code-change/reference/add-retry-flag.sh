#!/usr/bin/env bash
# Reference solution for add-retry-flag, applied to the checkout in $1. A patch, not edits, so it fails loudly when the
# fixture moves and needs nothing the fixture's image lacks (git is there; python is not).
set -euo pipefail
cd "$1"
git apply "$(dirname "$0")/add-retry-flag.patch"
