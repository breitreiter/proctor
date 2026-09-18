#!/usr/bin/env bash
# Arm setup: make sure imp is serving the model this arm needs. Blocks until healthy.
set -euo pipefail
case "$PROCTOR_ARM" in
  floor) profile=qcoder ;;
  *) echo "no model profile for arm $PROCTOR_ARM; nothing to do"; exit 0 ;;
esac
current=$(ssh imp '~/.local/bin/swap-model status' 2>/dev/null | awk '{print $2}' | head -1 || true)
if [ "$current" = "$profile" ]; then echo "imp already serving $profile"; exit 0; fi
echo "imp is serving '${current:-nothing}'; switching to $profile"
ssh imp '~/.local/bin/swap-model stop all' && ssh imp "~/.local/bin/swap-model $profile"
