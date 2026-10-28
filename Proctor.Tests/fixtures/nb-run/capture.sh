#!/bin/bash
# Each program here, run once by nb against the Mock provider: its JSONL (stdout) and its
# ATIF trajectory (--trajectory) from the same run, for the tests that read nb's output.
# Re-run after an nb change that alters either: NB=/path/to/nb ./capture.sh
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
nb=${NB:-$here/../../../../nb/bin/Debug/net10.0/nb}
cd "$here/programs"
for program in *.nb *.jsonl; do
    name=${program%.*}
    NO_COLOR=1 "$nb" --config "$here/mock-config.json" --trajectory "$here/$name.trajectory.json" - < "$program" > "$here/$name.jsonl" 2>/dev/null || true
done
