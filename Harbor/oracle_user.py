"""The oracle as Harbor's simulated user, for proctor. Compiled into each experiment's harbor/agents/ and imported as
oracle_user:OracleUser, with nb as the bridged target (nb_agent:NbAgent, nb --acp).

It runs in the Harbor process on the host, so the answer sheet never enters the container. It makes no model call of
its own: each judgement is the host's `nb --judge`, the same prompt and verdict rules as nb's in-process oracle, and the
loop follows nb's (ProgramEvaluator.ResolveWithOracleAsync), so the two paths agree by construction:

1. Send the task. Judge only a prompt that ended `end_turn` with nb's exit reason `ok`; any other ending stands.
2. done ends the run ok; miss ends it oracle_miss; a hit with the answers spent ends it oracle_budget; any other hit
   sends the sheet's answer as the next prompt, and the loop goes on.
3. A judge that fails ends the run judge_error, which ingest fails as infrastructure.

The record is oracle-user.json in the user agent's logs: the ending, each answer with its sheet keys, each judgement
as nb printed it (its usage included), and each prompt's answer. Ingest reads it (project/plans/oracle-in-proctor.md).
"""

import asyncio
import json
import os
import shlex

from harbor.agents.base import BaseAgent
from harbor.environments.base import BaseEnvironment
from harbor.models.agent.context import AgentContext

# The compiled user prompt template puts this between the task and Harbor's bridge instructions, which are for a model
# playing the user and say nothing this user needs.
SENTINEL = "<<<PROCTOR-BRIDGE>>>"
LOG = "oracle-user.json"


def task_text(instruction: str) -> str:
    """The task from the rendered user prompt: everything before the last sentinel."""
    if SENTINEL not in instruction:
        raise ValueError(f"the user prompt has no {SENTINEL} line: it was not rendered from proctor's template")
    return instruction.rpartition(SENTINEL)[0].rstrip()


def read_prompt(stdout: str) -> dict:
    """
    One `acpx --format json prompt` call's output, the JSON-RPC exchange line by line: the answer to its last
    session/prompt and the last text the agent sent in it. nb sends each assistant message whole, as one
    agent_message_chunk, so the last non-empty chunk is the turn's last message, which is what nb's own loop judges.
    """
    prompt_id, last, answer = None, "", None
    for line in stdout.splitlines():
        try:
            message = json.loads(line)
        except ValueError:
            continue
        if not isinstance(message, dict):
            continue
        if message.get("method") == "session/prompt":
            prompt_id, last, answer = message.get("id"), "", None
        elif prompt_id is None:
            continue
        elif message.get("method") == "session/update":
            update = (message.get("params") or {}).get("update") or {}
            if update.get("sessionUpdate") == "agent_message_chunk":
                text = (update.get("content") or {}).get("text") or ""
                if text:
                    last = text
        elif message.get("id") == prompt_id and ("result" in message or "error" in message):
            answer = message
    if answer is None:
        return {"stop_reason": None, "exit_reason": None, "error": "no answer to session/prompt in acpx's output", "last": last}
    result = answer.get("result") or {}
    nb = (result.get("_meta") or {}).get("nb") or {}
    return {"stop_reason": result.get("stopReason"), "exit_reason": nb.get("exit_reason"), "error": answer.get("error"), "last": last}


def judged(prompt: dict) -> bool:
    """Whether the oracle judges this prompt's turn: only one that ended as a finished, ok turn."""
    return prompt["error"] is None and prompt["stop_reason"] == "end_turn" and prompt["exit_reason"] == "ok"


def next_step(verdict: dict, used: int, turns: int) -> str:
    """What one judgement does: 'answer' sends the sheet's answer; 'ok', 'oracle_miss' or 'oracle_budget' end the run."""
    match verdict.get("verdict"):
        case "done":
            return "ok"
        case "miss":
            return "oracle_miss"
        case "hit":
            return "oracle_budget" if used >= turns else "answer"
    raise ValueError(f"nb --judge gave no verdict proctor knows: {verdict.get('verdict')!r}")


class OracleUser(BaseAgent):
    def __init__(self, *args, sheet: str, nb: str, config: str | None = None, judge: str | None = None,
                 turns: int = 8, **kwargs):
        super().__init__(*args, **kwargs)
        self._sheet = sheet
        self._nb = nb
        self._config = config
        self._judge = judge
        self._turns = int(turns)

    @staticmethod
    def name() -> str:
        return "proctor-oracle"

    def version(self) -> str | None:
        return None

    async def setup(self, environment: BaseEnvironment) -> None:
        for path in (self._sheet, self._nb, self._config):
            if path is not None and not os.path.exists(path):
                raise FileNotFoundError(f"the oracle's {path} is not on the host")

    async def _prompt(self, environment: BaseEnvironment, message: str) -> dict:
        # On stdin, so a message that starts with a dash or spans lines arrives as it is. acpx trims trailing whitespace.
        result = await environment.exec(f"printf '%s' {shlex.quote(message)} | acpx --format json prompt --file -")
        prompt = read_prompt(result.stdout or "")
        if result.return_code != 0 and prompt["error"] is None:
            prompt["error"] = f"acpx exited {result.return_code}: {(result.stderr or '').strip()[-500:]}"
        return prompt

    async def _judge_call(self, message: str) -> dict:
        """The host's nb --judge on one message. Raises on a failed judge; its stderr is in the message."""
        args = [self._nb, "--judge", "--sheet", self._sheet]
        if self._config:
            args += ["--config", self._config]
        if self._judge:
            args += ["--provider", self._judge]
        process = await asyncio.create_subprocess_exec(
            *args, stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
            env={**os.environ, "NO_COLOR": "1"})
        out, err = await process.communicate(message.encode())
        if process.returncode != 0:
            raise RuntimeError(f"nb --judge exited {process.returncode}: {err.decode().strip()[-500:]}")
        return json.loads(out.decode())

    async def run(self, instruction: str, environment: BaseEnvironment, context: AgentContext) -> None:
        record = {"ending": None, "answers": [], "judgements": [], "prompts": []}
        message, used = task_text(instruction), 0
        try:
            while True:
                prompt = await self._prompt(environment, message)
                record["prompts"].append({k: v for k, v in prompt.items() if k != "last"})
                if not judged(prompt):
                    record["ending"] = "agent"   # the run's own ending stands
                    break
                try:
                    verdict = await self._judge_call(prompt["last"])
                    step = next_step(verdict, used, self._turns)
                except (RuntimeError, ValueError) as e:
                    record["ending"], record["error"] = "judge_error", str(e)
                    break
                record["judgements"].append(verdict)
                if step != "answer":
                    record["ending"] = step
                    break
                used += 1
                message = verdict["answer"]
                record["answers"].append({"text": message, "keys": verdict["keys"]})
        finally:
            (self.logs_dir / LOG).write_text(json.dumps(record, indent=1))
        context.metadata = {"oracle_ending": record["ending"], "oracle_turns": used}
