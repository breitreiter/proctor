#!/usr/bin/env bash
# Arm setup: make sure the model box is serving the model this arm needs. Blocks until healthy.
# LLM_MODEL_HOST is the ssh host that runs the models behind the gateway, with a `swap-model` on
# its PATH; unset, the gateway is trusted to serve the profile itself.
set -euo pipefail
# Another suite's hook may name the profile as the first argument instead.
case "${1:-$PROCTOR_ARM}" in
  floor|qcoder) profile=qcoder ;;
  *) echo "no model profile for arm $PROCTOR_ARM; nothing to do"; exit 0 ;;
esac
host="${LLM_MODEL_HOST:-}"
[ -n "$host" ] || { echo "LLM_MODEL_HOST not set; trusting the gateway to serve $profile"; exit 0; }
current=$(ssh "$host" 'swap-model status' 2>/dev/null | awk '{print $2}' | head -1 || true)
if [ "$current" = "$profile" ]; then echo "$host already serving $profile"; exit 0; fi
echo "$host is serving '${current:-nothing}'; switching to $profile"
ssh "$host" 'swap-model stop all' && ssh "$host" "swap-model $profile"
