#!/usr/bin/env bash
# Stands in for a fixture checkout: put something in the work directory for the run to see.
set -euo pipefail
echo "sample $PROCTOR_ARM/$PROCTOR_CASE/$PROCTOR_SAMPLE setup"
printf 'original\n' > "$PROCTOR_WORK/note.txt"
