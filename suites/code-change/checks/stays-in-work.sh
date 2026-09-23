#!/usr/bin/env bash
# Validity: did the agent stay inside its checkout? Fails on the first tool call whose arguments
# name an absolute path outside the checkout (system paths aside) or climb with `..`. Reaching
# the repository root would find the suite, the tasks and the fixture's checker, so such a sample
# does not count; it is not a capability failure. The arm's bundle is there to be read. The
# checkout is where the model was told it is: $PROCTOR_WORK_MOUNT, which a runner may put
# somewhere other than $PROCTOR_WORK on the host.
set -uo pipefail
work=${PROCTOR_WORK_MOUNT:-$PROCTOR_WORK}
bundle=${PROCTOR_BUNDLE_MOUNT:-/nonexistent/bundle}
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
      "$work"|"$work"/*|"$bundle"|"$bundle"/*|/dev/*|/tmp/*|/usr/*|/bin/*|/etc/*|/proc/*|/opt/*|/nb/*|/var/*|/root/.nuget/*|/home/*/.nuget/*|/home/*/.dotnet/*) ;;
      *) echo "call $n ($name) reaches outside the checkout: $p"; exit 1 ;;
    esac
  done < <(grep -oE '(^|[^A-Za-z0-9_.~*:/-])/[A-Za-z0-9_.@-][A-Za-z0-9_./@-]*' <<<"$args" | sed -E 's#^[^/]*##' | sort -u)
done <<<"$calls"
echo "$n tool calls, all inside the checkout"; exit 0
