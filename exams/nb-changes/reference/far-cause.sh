#!/usr/bin/env bash
# The reference fix: take the plant back out.
set -euo pipefail
cd "$1" && git apply --reverse "${AUSPEX_FIXTURE:-/tests/fixture}/plant.patch"
