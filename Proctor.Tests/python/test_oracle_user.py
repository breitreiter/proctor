"""Harbor/oracle_user.py without Harbor: stub harbor modules beside this file, acpx's output captured from nb --acp on
Mock (acpx-*.jsonl, acpx 0.11.2), and the real nb --judge on Mock. OracleHarborTests runs it with PYTHONPATH set to
this directory and Harbor/, and NB_PATH, NB_CONFIG naming the host nb and its Mock config."""

import asyncio
import json
import os
import shlex
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

import oracle_user
from oracle_user import OracleUser, judged, next_step, read_prompt, task_text

HERE = Path(__file__).parent
SHEET = "# Answers\n\n## deploy-target\nStaging only.\n\n## region\nus-east.\n"


def acpx(text: str, exit_reason: str = "ok", stop_reason: str = "end_turn", prompt_id: int = 3) -> str:
    """One acpx --format json prompt's output, in the shape acpx-ask.jsonl captured: the request, the agent's text, the answer."""
    lines = [
        {"jsonrpc": "2.0", "id": prompt_id, "method": "session/prompt", "params": {"sessionId": "nb-session", "prompt": [{"type": "text", "text": "…"}]}},
        {"jsonrpc": "2.0", "method": "session/update", "params": {"sessionId": "nb-session", "update": {"sessionUpdate": "agent_message_chunk", "content": {"type": "text", "text": text}}}},
        {"jsonrpc": "2.0", "id": prompt_id, "result": {"stopReason": stop_reason, "usage": {"inputTokens": 10, "outputTokens": 5, "totalTokens": 15}, "_meta": {"nb": {"exit_reason": exit_reason}}}},
    ]
    return "".join(json.dumps(line) + "\n" for line in lines)


class FakeEnvironment:
    """The container as the user agent sees it: each exec is one acpx prompt, answered from a script."""

    def __init__(self, outputs):
        self.outputs, self.commands = list(outputs), []

    async def exec(self, command, **kwargs):
        self.commands.append(command)
        stdout = self.outputs.pop(0)
        return SimpleNamespace(stdout=stdout, stderr="", return_code=0)


def run_user(outputs, turns=8, judge=None):
    """OracleUser over a FakeEnvironment, judged by the real nb on Mock. Returns the record it wrote and the environment."""
    with tempfile.TemporaryDirectory() as d:
        sheet = Path(d, "sheet.md")
        sheet.write_text(SHEET)
        user = OracleUser(logs_dir=d, sheet=str(sheet), nb=os.environ["NB_PATH"], config=os.environ["NB_CONFIG"], judge=judge, turns=turns)
        environment = FakeEnvironment(outputs)
        context = SimpleNamespace(metadata=None)
        asyncio.run(user.setup(environment))
        asyncio.run(user.run("Deploy the service.\n<<<PROCTOR-BRIDGE>>>\nHow to talk to the agent.\n", environment, context))
        record = json.loads(Path(d, oracle_user.LOG).read_text())
        return record, environment, context


class Parsing(unittest.TestCase):
    def test_the_task_is_everything_before_the_last_sentinel(self):
        self.assertEqual("Fix it.\n\nCarefully.", task_text("Fix it.\n\nCarefully.\n\n<<<PROCTOR-BRIDGE>>>\nacpx prompt …\n"))
        self.assertEqual("a <<<PROCTOR-BRIDGE>>> b", task_text("a <<<PROCTOR-BRIDGE>>> b\n<<<PROCTOR-BRIDGE>>>\nbridge"))
        with self.assertRaises(ValueError):
            task_text("no sentinel")

    def test_a_tool_turn_reads_the_answer_and_the_text_after_the_tool(self):
        prompt = read_prompt((HERE / "acpx-tool.jsonl").read_text())
        self.assertEqual({"stop_reason": "end_turn", "exit_reason": "ok", "error": None, "last": "OK"}, prompt)
        self.assertTrue(judged(prompt))

    def test_a_second_prompt_has_no_initialize(self):
        prompt = read_prompt((HERE / "acpx-ask.jsonl").read_text())
        self.assertEqual("Which deploy target? MOCK:oracle=deploy-target", prompt["last"])
        self.assertTrue(judged(prompt))

    def test_only_a_finished_ok_turn_is_judged(self):
        self.assertFalse(judged(read_prompt(acpx("Out of budget.", exit_reason="budget_exhausted"))))
        self.assertFalse(judged(read_prompt(acpx("", stop_reason="cancelled", exit_reason="cancelled"))))
        error = read_prompt('{"jsonrpc":"2.0","id":2,"method":"session/prompt","params":{}}\n{"jsonrpc":"2.0","id":2,"error":{"code":-32603,"message":"boom"}}\n')
        self.assertEqual("boom", error["error"]["message"])
        self.assertFalse(judged(error))
        nothing = read_prompt("[acpx] agent needs reconnect\n")
        self.assertIn("no answer", nothing["error"])
        self.assertFalse(judged(nothing))

    def test_the_last_non_empty_message_is_the_one_judged(self):
        two = acpx("first") + acpx("second", prompt_id=4)
        self.assertEqual("second", read_prompt(two)["last"])
        empty = acpx("said this").replace('"text": "said this"', '"text": ""', 1)
        self.assertEqual("", read_prompt(empty)["last"])

    def test_next_step_follows_nbs_loop(self):
        self.assertEqual("ok", next_step({"verdict": "done"}, 0, 8))
        self.assertEqual("oracle_miss", next_step({"verdict": "miss"}, 0, 8))
        self.assertEqual("answer", next_step({"verdict": "hit"}, 7, 8))
        self.assertEqual("oracle_budget", next_step({"verdict": "hit"}, 8, 8))
        with self.assertRaises(ValueError):
            next_step({"verdict": "maybe"}, 0, 8)


class Loop(unittest.TestCase):
    def test_a_hit_sends_the_sheets_answer_and_done_ends_ok(self):
        record, environment, context = run_user([
            acpx("Which deploy target? MOCK:oracle=deploy-target"),
            acpx("Deploying to staging. MOCK:oracle=DONE"),
        ])
        self.assertEqual("ok", record["ending"])
        self.assertEqual(1, len(record["answers"]))
        answer = record["answers"][0]
        self.assertEqual(["deploy-target"], answer["keys"])
        self.assertIn("Staging only.", answer["text"])
        self.assertEqual(["hit", "done"], [j["verdict"] for j in record["judgements"]])
        self.assertIn("usage", record["judgements"][0])
        self.assertEqual(2, len(record["prompts"]))
        # The task went first, then the answer, each on stdin.
        self.assertEqual(f"printf '%s' {shlex.quote('Deploy the service.')} | acpx --format json prompt --file -", environment.commands[0])
        self.assertIn(shlex.quote(answer["text"]), environment.commands[1])
        self.assertEqual({"oracle_ending": "ok", "oracle_turns": 1}, context.metadata)

    def test_a_miss_ends_oracle_miss(self):
        record, _, _ = run_user([acpx("What is the password? MOCK:oracle=MISS")])
        self.assertEqual("oracle_miss", record["ending"])
        self.assertEqual([], record["answers"])

    def test_a_hit_with_the_answers_spent_ends_oracle_budget(self):
        record, environment, _ = run_user([
            acpx("Which target? MOCK:oracle=deploy-target"),
            acpx("Which region? MOCK:oracle=region"),
        ], turns=1, judge="MockB")
        self.assertEqual("oracle_budget", record["ending"])
        self.assertEqual(1, len(record["answers"]))
        self.assertEqual(2, len(environment.commands))

    def test_a_turn_that_did_not_end_ok_is_not_judged(self):
        record, _, context = run_user([acpx("Out of budget. MOCK:oracle=deploy-target", exit_reason="budget_exhausted")])
        self.assertEqual("agent", record["ending"])
        self.assertEqual([], record["judgements"])
        self.assertEqual("budget_exhausted", record["prompts"][0]["exit_reason"])
        self.assertEqual({"oracle_ending": "agent", "oracle_turns": 0}, context.metadata)

    def test_a_judge_that_fails_ends_judge_error(self):
        record, _, _ = run_user([acpx("Which target? MOCK:oracle=deploy-target")], judge="NoSuchEntry")
        self.assertEqual("judge_error", record["ending"])
        self.assertIn("nb --judge exited 1", record["error"])


if __name__ == "__main__":
    unittest.main()
