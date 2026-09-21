#!/usr/bin/env bash
# Sample teardown: everything the run changed, including new files, as one unified diff.
set -euo pipefail
cd "$PROCTOR_WORK"
git add -A -N .
git diff --no-color > "$PROCTOR_CELL/diff.patch"
echo "diff.patch: $(grep -c '^diff --git' "$PROCTOR_CELL/diff.patch" || true) files"
