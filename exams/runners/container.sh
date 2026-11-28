#!/usr/bin/env bash
# The runner: nb inside the try's container, the checkout at the mount, the program on stdin, the
# transcript on stdout. The container is the try hooks' (an exam's hooks.try setup made it and
# its teardown removes it); this only execs into it. Nothing from the stage's environment
# crosses: the config on its read-only mount and the key set at `run` are all nb needs.
set -euo pipefail
engine=$(command -v podman >/dev/null 2>&1 && echo podman || echo docker)
exec "$engine" exec -i -w "$PROCTOR_WORK_MOUNT" "$PROCTOR_CONTAINER" /opt/nb/nb --output jsonl --config /nb/config.json -
