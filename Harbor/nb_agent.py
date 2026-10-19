"""nb as a Harbor agent, for proctor. Compiled into each experiment's harbor/agents/ and imported as nb_agent:NbAgent.

The image already holds the binary (/opt/nb, copied from proctor's nb layer), so setup installs nothing: it uploads the
config the host's nb runs with and the program proctor compiled on the host for this cell, and asks the binary its
version. The run is nb's own invocation, the program on stdin; its JSONL, stderr and exit code are the native log,
which proctor converts to ATIF at ingest. A non-zero exit is nb's to report in the transcript, not a Harbor failure.
"""

import os
import shlex
from pathlib import Path

from harbor.agents.base import BaseAgent
from harbor.environments.base import BaseEnvironment
from harbor.models.agent.context import AgentContext

NB = "/opt/nb/nb"
CONFIG = "/nb/config.json"
PROGRAM = "/nb/program.jsonl"


class NbAgent(BaseAgent):
    def __init__(self, *args, program: str, config: str | None = None, workdir: str = "/app",
                 secrets: list[str] | None = None, **kwargs):
        super().__init__(*args, **kwargs)
        self._program = program
        self._config = config
        self._workdir = workdir
        self._secrets = secrets or []
        self._version: str | None = None

    @staticmethod
    def name() -> str:
        return "nb"

    def version(self) -> str | None:
        return self._version

    async def setup(self, environment: BaseEnvironment) -> None:
        await environment.exec("mkdir -p /nb")
        await environment.upload_file(Path(self._program), PROGRAM)
        if self._config:
            await environment.upload_file(Path(self._config), CONFIG)
        result = await environment.exec(f"{NB} --version", env={"NO_COLOR": "1"})
        if result.return_code == 0 and result.stdout:
            self._version = result.stdout.strip().split("+")[0]

    async def run(self, instruction: str, environment: BaseEnvironment, context: AgentContext) -> None:
        # The configured env (the provider's endpoint variables) plus each key by name from Harbor's own environment,
        # which is the only place proctor puts it.
        env = {"NO_COLOR": "1", **self.extra_env}
        env.update({name: os.environ[name] for name in self._secrets if name in os.environ})
        logs = str(self.environment_logs_dir)
        config = f"--config {CONFIG} " if self._config else ""
        await environment.exec(
            f"cd {shlex.quote(self._workdir)} && {NB} --output jsonl {config}- < {PROGRAM}"
            f" > {logs}/nb.jsonl 2> {logs}/nb.stderr.log; echo $? > {logs}/nb.exit",
            env=env,
        )
