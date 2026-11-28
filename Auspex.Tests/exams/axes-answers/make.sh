#!/usr/bin/env bash
# Makes this exam's bank in a scratch root, for trying auspex's queries by hand (plans/axes-build.md, slice 3c).
# No model: nb's Mock provider and scripted checks. About two minutes.
#   usage: Auspex.Tests/exams/axes-answers/make.sh <empty directory>     (from the repository root, auspex and nb built)
set -euo pipefail
root=${1:?usage: make.sh <empty directory>}
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
nb=${NB_PATH:-$repo/../nb/bin/Debug/net10.0/nb}
mkdir -p "$root/exams" "$root/fixtures"
cp -r "$here" "$root/exams/axes-answers"
rm "$root/exams/axes-answers/make.sh"
cp -r "$repo/fixtures/note" "$root/fixtures/note"
cp "$repo/exams/nb.example.json" "$root/exams/nb.json"
printf '{ "nb": { "path": "%s", "config": "nb.json" } }\n' "$(realpath "$nb")" > "$root/exams/auspex.json"
auspex() { dotnet "$repo/bin/Debug/net10.0/auspex.dll" --root "$root" "$@"; }
auspex build axes-answers > "$root/build.txt"
# Every named configuration but long-b, which is left as a hole.
for name in shipped shipped-b rewritten rewritten-b terse-two long rewritten-long; do auspex run axes-answers --configuration "$name" | tail -1; done
auspex grade axes-answers | tail -3
auspex friction axes-answers > /dev/null
auspex report axes-answers
echo "made: $root"
