#!/usr/bin/env bash
# Passes when the agent ran the stub's one command: made.txt in the checkout, saying "stub".
f="$PROCTOR_WORK/made.txt"
[ -f "$f" ] || { echo "no made.txt in the checkout"; exit 1; }
[ "$(cat "$f")" = "stub" ] && { echo "made.txt says stub"; exit 0; }
echo "made.txt does not say stub"; exit 1
