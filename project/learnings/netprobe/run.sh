#!/bin/bash
# usage: run.sh <name> '<agent network lines>'   One trial of the probe under that agent policy, on Harbor's oracle
# agent, no model. NET=<n> picks the trial's subnet (10.213.<n>.0/24), so several can run at once; ENVPOL='<lines>'
# sets the environment's policy too (public otherwise); ENGINE=podman; HARBOR=<version> (0.24.0 otherwise); JOIN and FROM below. What answered is in
# jobs/<name>/*/artifacts/logs/artifacts/probe.txt, and late.txt beside it is the process the agent left running.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd); name=$1; policy=$2
rm -rf "$here/t-$name" "$here/jobs/$name"; cp -r "$here/task" "$here/t-$name"
mkdir -p "$here/t-$name/environment/certs"
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -keyout "$here/t-$name/environment/certs/key.pem" -out "$here/t-$name/environment/certs/cert.pem" \
  -subj "/CN=probe" -addext "subjectAltName=DNS:allowed,DNS:other" 2>/dev/null; chmod 644 "$here/t-$name/environment/certs/"*.pem
python3 - "$here/t-$name/task.toml" "$policy" <<'PY'
import sys; p, pol = sys.argv[1:]; import os; s = open(p).read().replace("@AGENT@", pol)
if os.environ.get("ENVPOL"): s = s.replace('build_timeout_sec = 900.0\nnetwork_mode = "public"', "build_timeout_sec = 900.0\n" + os.environ["ENVPOL"])
open(p, "w").write(s)
PY
sed -i "s/@NET@/${NET:-7}/g" "$here/t-$name/environment/docker-compose.yaml"
# JOIN=<service> has `beside` say outright whose network namespace it joins (Harbor's sidecar, by its name in that Harbor).
if [ -n "${JOIN:-}" ]; then sed -i "s/^  beside:/  beside:\n    network_mode: \"service:$JOIN\"\n    depends_on: { $JOIN: { condition: service_started } }/" "$here/t-$name/environment/docker-compose.yaml"; fi
# DROP=<capability> drops it from the agent's container (compose's `main`, which Harbor merges its own over).
if [ -n "${DROP:-}" ]; then sed -i "s/^services:/services:\n  main: { cap_drop: [$DROP] }/" "$here/t-$name/environment/docker-compose.yaml"; fi
cat > "$here/job-$name.json" <<JSON
{"job_name": "$name", "jobs_dir": "$here/jobs", "n_attempts": 1, "n_concurrent_trials": 1, "quiet": true,
 "retry": {"max_retries": 0}, "environment": {"type": "${ENGINE:-docker}"},
 "agents": [{"name": "oracle"}], "tasks": [{"path": "$here/t-$name"}]}
JSON
# FROM=<what uv installs from> runs a Harbor that is not a release, such as a pull request's commit (git+https://...@<sha>).
if [ -n "${FROM:-}" ]; then harbor=(--from "$FROM" harbor); else harbor=("harbor@${HARBOR:-0.24.0}"); fi
cd "$here"; HARBOR_TELEMETRY=off NO_COLOR=1 uv tool run --exclude-newer "7 days" "${harbor[@]}" run --config "job-$name.json" 2>&1 | tail -15
