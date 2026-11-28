#!/usr/bin/env bash
# Configuration setup prepares the machine: the model on the model box, proctor built at the revision the
# configuration's docs value pins, and the runner's image. What the agent reads is that value's files, declared in
# exam.json and laid into the checkout at .bundle/ by proctor, so this hook changes nothing the agent sees: it
# reads only which revision to build. The image is runners/Containerfile's, the package cache warmed for this
# exam's workspaces.
set -euo pipefail
[ "${AUSPEX_AXIS_ASSISTANT:-}" != qcoder ] || "$AUSPEX_EXAM_DIR/../code-change/hooks/ensure-model.sh" qcoder
root=$(cd "$AUSPEX_EXAM_DIR/../.." && pwd)
files=$(jq -c --arg v "$AUSPEX_AXIS_DOCS" '.axes.docs.values[$v].files' "$AUSPEX_EXAM_DIR/exam.json")
url=$(jq -r .git <<<"$files"); rev=$(jq -r .rev <<<"$files")

# proctor as a user would have it, built once per revision on the host (it is our own source, not the model's).
# The value's files hold only the documentation, so the build is from a clone of its own.
src="$root/.proctor/proctor-src/$rev"
[ -d "$src/.git" ] || { rm -rf "$src" && git clone -q "$url" "$src" && git -C "$src" checkout -q "$rev"; }
build="$root/.proctor/proctor-builds/$rev"
[ -f "$build/proctor.dll" ] || dotnet build "$src/Proctor.csproj" -c Release -o "$build" --nologo -v q >/dev/null
echo "proctor $rev built at $build"

[ -n "$AUSPEX_RUNNER" ] || { echo "bare run: no image"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
runners="$AUSPEX_EXAM_DIR/../runners"
"$engine" build -q -t proctor-dotnet --build-arg "UID=$(id -u)" --build-arg "GID=$(id -g)" -f "$runners/Containerfile" "$runners" >/dev/null
echo "image proctor-dotnet $("$engine" image inspect --format '{{.Id}}' proctor-dotnet)"
# Warm the package cache for the .NET repositories inside the workspaces, so neither the agent nor the trial pays for a restore.
keepid=(); [ "$engine" = podman ] && keepid=(--userns=keep-id)
"$engine" run --rm "${keepid[@]}" -v "$root/fixtures:/src:ro" -v proctor-nuget:/home/agent/.nuget/packages proctor-dotnet \
  bash -c 'cp -r /src /tmp/w && find /tmp/w/authoring-* -name "*.csproj" -exec dotnet restore -v q {} \; >/dev/null' && echo "restored the workspaces into proctor-nuget"
