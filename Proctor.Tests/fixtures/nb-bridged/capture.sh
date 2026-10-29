#!/bin/bash
# One bridged oracle run, recorded: Harbor/oracle_user.py driving nb --acp on Mock through acpx,
# as Harbor's ACP bridge does, on the host with no container. The task asks; the sheet's answer
# scripts the agent's last turn, which the judge calls done. Writes oracle-user.json (the
# simulated user's record) and trajectory.json (nb's, from the same run), for HarborTests'
# ingest of a bridged trial. Re-run after a change to either:
#   ACPX=/path/to/acpx NB=/path/to/nb ./capture.sh      (acpx 0.11.2, Harbor 0.24's pin)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
proctor=$here/../../..
nb=${NB:-$proctor/../nb/bin/Debug/net10.0/nb}
config=$here/../nb-run/mock-config.json
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
mkdir "$work/app" "$work/bin" "$work/logs"
printf '#!/bin/sh\nexec %s "$@"\n' "${ACPX:-acpx}" > "$work/bin/acpx" && chmod +x "$work/bin/acpx"
printf 'system You deploy services.\n' > "$work/program.nb"
cat > "$work/sheet.md" <<'SHEET'
# Answers

## deploy-target
MOCK:response=Deployed to staging. MOCK:oracle=DONE

## region
us-east.
SHEET
printf '{"agents":{"target":{"command":"%s","args":["--acp","--config","%s","--trajectory","%s","%s"]}},"defaultAgent":"target","defaultPermissions":"approve-all","ttl":0,"timeout":60}\n' \
    "$nb" "$config" "$work/logs/trajectory.json" "$work/program.nb" > "$work/app/.acpxrc.json"
export PATH=$work/bin:$PATH
(cd "$work/app" && acpx target sessions new > /dev/null 2>&1)
cd "$work/app"
PYTHONPATH=$proctor/Proctor.Tests/python:$proctor/Harbor python3 -B - "$work" "$nb" "$config" <<'PY'
import asyncio, os, subprocess, sys
from pathlib import Path
from types import SimpleNamespace
from oracle_user import OracleUser

work, nb, config = sys.argv[1:]

class Host:
    async def exec(self, command, **kwargs):
        p = subprocess.run(["bash", "-c", command], capture_output=True, text=True)
        return SimpleNamespace(stdout=p.stdout, stderr=p.stderr, return_code=p.returncode)

user = OracleUser(logs_dir=Path(work, "logs"), sheet=f"{work}/sheet.md", nb=nb, config=config, judge="Mock")
task = "MOCK:response=Which deploy target? MOCK:oracle=deploy-target"
asyncio.run(user.run(f"{task}\n<<<PROCTOR-BRIDGE>>>\nThe bridge's instructions.\n", Host(), SimpleNamespace(metadata=None)))
PY
cp "$work/logs/oracle-user.json" "$work/logs/trajectory.json" "$here/"
