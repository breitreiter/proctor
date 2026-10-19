"""Qwen Code as a Harbor agent, for proctor. Compiled into each experiment's harbor/agents/ and imported as
qwen_code_agent:QwenCodeAgent.

Harbor's own Qwen Code agent installs nvm, Node and the CLI from the network on every trial, whatever the image holds.
Proctor's image already has the pinned CLI, and the agent phase reaches only the model's host, so this one installs
nothing: it checks that the image's `qwen --version` is the pin and fails setup when it is not. Everything else (the
invocation, the session log, its conversion to ATIF) is Harbor's.
"""

from harbor.agents.installed.qwen_code import QwenCode
from harbor.environments.base import BaseEnvironment


class QwenCodeAgent(QwenCode):
    async def install(self, environment: BaseEnvironment) -> None:
        result = await environment.exec(command=self.get_version_command())
        found = self.parse_version(result.stdout or "") if result.return_code == 0 else None
        if found != self._version:
            raise RuntimeError(f"the image has Qwen Code {found or 'not installed'}, the arm pins {self._version}")
