#!/usr/bin/env bash
# Stands in for the agent's edit, since the Mock provider touches nothing: proctor collects the diff afterwards.
set -euo pipefail
printf 'changed\n' > "$PROCTOR_WORK/note.txt"
echo "note.txt edited"
