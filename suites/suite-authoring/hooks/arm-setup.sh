#!/usr/bin/env bash
# Arm setup: the model on the model box, proctor built from the arm's bundle, and the arm's view of
# that bundle: what the agent is shown at /bundle. Both arms pin the same revision; the view is
# what differs, so the comparison between arms is the examples and nothing else. The image and the
# package cache are code-change's, extended by nothing but the fixtures here.
set -euo pipefail
"$PROCTOR_SUITE_DIR/../code-change/hooks/ensure-model.sh" qcoder
root=$(cd "$PROCTOR_SUITE_DIR/../.." && pwd)
rev=$(git -C "$PROCTOR_BUNDLE" rev-parse HEAD)

# proctor as a user would have it, built once per revision on the host (it is our own source, not the model's).
build="$root/.proctor/proctor-builds/$rev"
[ -f "$build/proctor.dll" ] || dotnet build "$PROCTOR_BUNDLE/Proctor.csproj" -c Release -o "$build" --nologo -v q >/dev/null
echo "proctor $rev built at $build"

# The view: documentation for every arm, the examples for the arm named for them. This suite's
# trial/ and reference/ are its answer key and never shown; nor are the workspace fixtures the tasks run in.
view="$root/.proctor/views/$rev/$PROCTOR_ARM"
rm -rf "$view" && mkdir -p "$view/project/plans"
cp "$PROCTOR_BUNDLE/README.md" "$view/"
cp "$PROCTOR_BUNDLE"/project/plans/{on-disk-layout,fixtures-arms-baselines,containerised-runs,judge,report-structure}.md "$view/project/plans/"
case "$PROCTOR_ARM" in
  docs) ;;
  docs-and-examples)
    (cd "$PROCTOR_BUNDLE" && find suites evals fixtures -type f 2>/dev/null \
       -not -path 'suites/suite-authoring/trial/*' -not -path 'suites/suite-authoring/reference/*' -not -path 'evals/eval-authoring/trial/*' -not -path 'evals/eval-authoring/reference/*' -not -path 'fixtures/authoring-*' \
       -not -path '*/bin/*' -not -path '*/obj/*' -not -name baseline.json -print0 \
     | xargs -0 cp --parents -t "$view") ;;
  *) echo "no view for arm $PROCTOR_ARM"; exit 1 ;;
esac
cp -r "$build" "$view/proctor"
echo "view for $PROCTOR_ARM: $(find "$view" -type f -not -path "$view/proctor/*" | wc -l) files beside the build"

[ -n "$PROCTOR_RUNNER" ] || { echo "bare run: no image"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
runners="$PROCTOR_SUITE_DIR/../runners"
"$engine" build -q -t proctor-dotnet --build-arg "UID=$(id -u)" --build-arg "GID=$(id -g)" -f "$runners/Containerfile" "$runners" >/dev/null
echo "image proctor-dotnet $("$engine" image inspect --format '{{.Id}}' proctor-dotnet)"
# Warm the package cache for the .NET repositories inside the workspaces, so neither the agent nor the trial pays for a restore.
keepid=(); [ "$engine" = podman ] && keepid=(--userns=keep-id)
"$engine" run --rm "${keepid[@]}" -v "$root/fixtures:/src:ro" -v proctor-nuget:/home/agent/.nuget/packages proctor-dotnet \
  bash -c 'cp -r /src /tmp/w && find /tmp/w/authoring-* -name "*.csproj" -exec dotnet restore -v q {} \; >/dev/null' && echo "restored the workspaces into proctor-nuget"
