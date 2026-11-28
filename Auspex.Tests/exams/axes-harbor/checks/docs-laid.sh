#!/usr/bin/env bash
# Passes when the configuration's README is the one in the checkout: a value's files reached the image's repository.
case "$AUSPEX_AXIS_DOCS" in
  rewritten) grep -q 'audit --json' "$AUSPEX_WORK/README.md" && { echo "the rewritten README is in the checkout"; exit 0; } ;;
  *) grep -q 'audit --json' "$AUSPEX_WORK/README.md" || { echo "the shipped README is in the checkout"; exit 0; } ;;
esac
echo "README.md is not the one docs=$AUSPEX_AXIS_DOCS carries"; exit 1
