"""nb as a Harbor agent, for auspex. Compiled into each run's harbor/agents/ (under its log entry) and imported as nb_agent:NbAgent.

The image already holds the binary (/opt/nb, copied from auspex's nb layer), so setup installs nothing: it uploads the
config the host's nb runs with and the program auspex compiled on the host for this attempt, and asks the binary its
version. The run is nb's own invocation, the program on stdin; nb writes its own ATIF trajectory (--trajectory) where
Harbor keeps an agent's, beside its JSONL, stderr and exit code. A non-zero exit is nb's to report, not a Harbor failure.

nb is also an ACP bridge target (nb --acp): with a user agent driving it, the program configures the session and holds
no run, and each prompt over the bridge is one. nb rewrites the same trajectory at the end of every prompt, so ingest
reads it as on the direct path, not the bridge's raw ACP export.
"""

import os
import shlex
from pathlib import Path

from harbor.agents.base import BaseAgent
from harbor.agents.capabilities import AgentCapabilities
from harbor.agents.protocols.acp import ACPAgentMixin
from harbor.environments.base import BaseEnvironment
from harbor.models.agent.context import AgentContext
from harbor.models.bridge import BridgeKind

NB = "/opt/nb/nb"
CONFIG = "/nb/config.json"
PROGRAM = "/nb/program.jsonl"
PROGRAMS = "/nb/programs"


class NbAgent(BaseAgent, ACPAgentMixin):
    capabilities = AgentCapabilities(bridges=frozenset({BridgeKind.ACP}))

    def __init__(self, *args, program: str, config: str | None = None, workdir: str = "/app",
                 secret: str | None = None, **kwargs):
        super().__init__(*args, **kwargs)
        self._program = program
        self._config = config
        self._workdir = workdir
        self._secret = secret
        self._version: str | None = None
        # A staged exam: `program` is a directory of one program per stage, <place>.jsonl, and Harbor calls run once per
        # step on this one agent, in order, so the nth run is the nth stage's.
        self._programs = sorted(Path(program).glob("*.jsonl"), key=lambda p: int(p.stem)) if Path(program).is_dir() else None
        self._runs = 0

    @staticmethod
    def name() -> str:
        return "nb"

    def version(self) -> str | None:
        return self._version

    async def setup(self, environment: BaseEnvironment) -> None:
        await environment.exec("mkdir -p /nb")
        if self._programs is None:
            await environment.upload_file(Path(self._program), PROGRAM)
        else:
            await environment.exec(f"mkdir -p {PROGRAMS}")
            for program in self._programs:
                await environment.upload_file(program, f"{PROGRAMS}/{program.name}")
        if self._config:
            await environment.upload_file(Path(self._config), CONFIG)
        result = await environment.exec(f"{NB} --version", env={"NO_COLOR": "1"})
        if result.return_code == 0 and result.stdout:
            self._version = result.stdout.strip().split("+")[0]

    def _env(self) -> dict[str, str]:
        # The configured env (the provider's endpoint variables) plus the key, by name, from Harbor's own environment,
        # which is the only place auspex puts it.
        env = {"NO_COLOR": "1", **self.extra_env}
        if self._secret and self._secret in os.environ:
            env[self._secret] = os.environ[self._secret]
        return env

    def acp_command(self) -> list[str]:
        config = ["--config", CONFIG] if self._config else []
        return [NB, "--acp", *config, "--trajectory", f"{self.environment_logs_dir}/trajectory.json", PROGRAM]

    def acp_env(self) -> dict[str, str]:
        return self._env()

    async def run(self, instruction: str, environment: BaseEnvironment, context: AgentContext) -> None:
        env = self._env()
        logs = str(self.environment_logs_dir)
        config = f"--config {CONFIG} " if self._config else ""
        program = PROGRAM
        if self._programs is not None:
            if self._runs >= len(self._programs):
                raise RuntimeError(f"nb was run {self._runs + 1} times and has {len(self._programs)} stage programs")
            program = f"{PROGRAMS}/{self._programs[self._runs].name}"
            self._runs += 1
        await environment.exec(
            f"cd {shlex.quote(self._workdir)} && {NB} --output jsonl --trajectory {logs}/trajectory.json {config}- < {program}"
            f" > {logs}/nb.jsonl 2> {logs}/nb.stderr.log; echo $? > {logs}/nb.exit",
            env=env,
        )
