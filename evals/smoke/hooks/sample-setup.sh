#!/usr/bin/env bash
# The fixture is already checked out in $PROCTOR_WORK; a real eval would start its fakes here.
set -euo pipefail
echo "sample $PROCTOR_ARM/$PROCTOR_CASE/$PROCTOR_SAMPLE setup; work has $(ls "$PROCTOR_WORK" | tr '\n' ' ')"
