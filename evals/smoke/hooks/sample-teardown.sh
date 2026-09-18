#!/usr/bin/env bash
# Stands in for `git diff`: write a unified diff naming the file the setup created.
set -euo pipefail
printf 'diff --git a/note.txt b/note.txt\n--- a/note.txt\n+++ b/note.txt\n@@ -1 +1 @@\n-original\n+changed\n' > "$PROCTOR_CELL/diff.patch"
echo "diff collected"
