#!/usr/bin/env bash
# Variant setup prepares the machine: the model on the model box, proctor built at the revision the variant's
# bundle pins, and the view the agent is shown at /work/.bundle, which is the bundle and that build. What the
# agent reads is the bundle's paths, declared in the variant's file, so this hook never branches on which
# variant it runs for. The image is runners/Containerfile's, the package cache warmed for this exam's fixtures.
set -euo pipefail
"$PROCTOR_EXAM_DIR/../code-change/hooks/ensure-model.sh" qcoder
root=$(cd "$PROCTOR_EXAM_DIR/../.." && pwd)
variant="$PROCTOR_EXAM_DIR/variants/$PROCTOR_VARIANT.json"
url=$(jq -r .bundle.git "$variant"); rev=$(jq -r .bundle.rev "$variant")

# proctor as a user would have it, built once per revision on the host (it is our own source, not the model's).
# The bundle holds only the documentation, so the build is from a clone of its own.
src="$root/.proctor/proctor-src/$rev"
[ -d "$src/.git" ] || { rm -rf "$src" && git clone -q "$url" "$src" && git -C "$src" checkout -q "$rev"; }
build="$root/.proctor/proctor-builds/$rev"
[ -f "$build/proctor.dll" ] || dotnet build "$src/Proctor.csproj" -c Release -o "$build" --nologo -v q >/dev/null
echo "proctor $rev built at $build"

# The view: the bundle as proctor resolved it, and the build beside it. Named by the bundle's directory, which is
# its identity, so attempt-setup.sh finds the same one.
view="$root/.proctor/views/$(basename "$PROCTOR_BUNDLE")"
rm -rf "$view" && mkdir -p "$view"
cp -r "$PROCTOR_BUNDLE/." "$view/"
cp -r "$build" "$view/proctor"
echo "view for $PROCTOR_VARIANT: $(find "$view" -type f -not -path "$view/proctor/*" | wc -l) files beside the build"

[ -n "$PROCTOR_RUNNER" ] || { echo "bare run: no image"; exit 0; }
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
runners="$PROCTOR_EXAM_DIR/../runners"
"$engine" build -q -t proctor-dotnet --build-arg "UID=$(id -u)" --build-arg "GID=$(id -g)" -f "$runners/Containerfile" "$runners" >/dev/null
echo "image proctor-dotnet $("$engine" image inspect --format '{{.Id}}' proctor-dotnet)"
# Warm the package cache for the .NET repositories inside the workspaces, so neither the agent nor the trial pays for a restore.
keepid=(); [ "$engine" = podman ] && keepid=(--userns=keep-id)
"$engine" run --rm "${keepid[@]}" -v "$root/fixtures:/src:ro" -v proctor-nuget:/home/agent/.nuget/packages proctor-dotnet \
  bash -c 'cp -r /src /tmp/w && find /tmp/w/authoring-* -name "*.csproj" -exec dotnet restore -v q {} \; >/dev/null' && echo "restored the workspaces into proctor-nuget"
