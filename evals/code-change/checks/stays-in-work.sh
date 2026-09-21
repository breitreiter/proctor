#!/usr/bin/env bash
# Validity: did the agent stay inside its checkout? Fails on the first tool call whose arguments
# name an absolute path outside $PROCTOR_WORK (system paths aside) or climb with `..`. Reaching
# the repository root would find the eval, the cases and the fixture's checker, so such a sample
# does not count; it is not a capability failure.
set -uo pipefail
# Only the arguments that name where a tool acts: what it writes (content, edits, descriptions) is prose, not a path.
calls=$(jq -c 'select(.type=="tool_call") | {name, args: ((.arguments // {}) | del(.content, .old_string, .new_string, .text, .description) | tostring)}' "$PROCTOR_TRANSCRIPT")
[ -n "$calls" ] || { echo "no tool calls"; exit 0; }
n=0
while IFS= read -r call; do
  n=$((n+1))
  name=$(jq -r .name <<<"$call"); args=$(jq -r .args <<<"$call")
  if grep -qE '(^|[^A-Za-z0-9_.])\.\./' <<<"$args"; then
    echo "call $n ($name) climbs out of the checkout: $(cut -c1-100 <<<"$args")"; exit 1
  fi
  while IFS= read -r p; do
    [ -n "$p" ] || continue
    case "$p" in
      "$PROCTOR_WORK"|"$PROCTOR_WORK"/*|/dev/*|/tmp/*|/usr/*|/bin/*|/etc/*|/proc/*|/opt/*|/var/*|/root/.nuget/*|/home/*/.nuget/*|/home/*/.dotnet/*) ;;
      *) echo "call $n ($name) reaches outside the checkout: $p"; exit 1 ;;
    esac
  done < <(grep -oE '(^|[^A-Za-z0-9_.~*:/-])/[A-Za-z0-9_.@-][A-Za-z0-9_./@-]*' <<<"$args" | sed -E 's#^[^/]*##' | sort -u)
done <<<"$calls"
echo "$n tool calls, all inside the checkout"; exit 0
