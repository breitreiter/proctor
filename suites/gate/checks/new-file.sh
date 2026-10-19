#!/usr/bin/env bash
# Passes when the run left new.txt in the checkout with the Mock's content; reads the work tree, never the transcript.
f="$PROCTOR_WORK/new.txt"
[ -f "$f" ] || { echo "no new.txt in the checkout"; exit 1; }
grep -q "scripted by MockProvider" "$f" && { echo "new.txt has the Mock's content"; exit 0; }
echo "new.txt does not have the Mock's content"; exit 1
