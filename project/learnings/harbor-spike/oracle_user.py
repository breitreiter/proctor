"""A scripted user over Harbor's ACP bridge: the shape of proctor's oracle sheet. No model."""
import json, shlex
from harbor.agents.base import BaseAgent
from harbor.environments.base import BaseEnvironment
from harbor.models.agent.context import AgentContext

SHEET = ["What exact line of code did you change? Reply with that one line only."]

class OracleUser(BaseAgent):
    @staticmethod
    def name() -> str: return "proctor-oracle-user"
    def version(self) -> str | None: return "spike"
    async def setup(self, environment: BaseEnvironment) -> None: pass

    async def run(self, instruction: str, environment: BaseEnvironment, context: AgentContext) -> None:
        task = instruction.split("<<<PROCTOR-BRIDGE>>>")[0].strip()
        turns = []
        for message in [task, *SHEET]:
            r = await environment.exec(f"acpx prompt {shlex.quote(message)}")
            turns.append({"user": message, "rc": r.return_code, "reply": r.stdout, "stderr": r.stderr})
        (self.logs_dir / "oracle-user.json").write_text(json.dumps(turns, indent=1))
        context.metadata = {"oracle_turns": len(turns)}
