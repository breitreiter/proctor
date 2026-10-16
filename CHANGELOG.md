# Changelog

Proctor's version is `<Version>` in `Proctor.csproj`; `proctor --version`
prints it and every `experiment.json` records it under `versions.proctor`.
Before 1.0 a minor bump means a breaking change to the layout, the file
formats or the vocabulary; a patch bump does not.

## 0.2.6 — 2026-10-16

`grade` grades agent cells (staging step 3). Nothing on disk breaks; an nb
cell is read and graded as before, and no rendering changed.

- An agent cell's run is its ATIF `trajectory.json`, with `core.json` as the
  trailer: `exit_reason`, `max_tool_calls`, `max_tokens`, `max_duration_ms`
  and `denied_calls` read proctor's own count. The windows (answer, answer
  JSON, tool calls, tool results, user turns) are the main agent's
  conversation; a tool result is an error when the agent marked it so.
- A script check on an agent cell is not rerun: its verdict is the cell's
  `reward.json` key, its reason the first line of `verifier/<check>.out`. A
  missing key is `error`, with the script's output or the diff that did not
  apply as the reason.
- `loop_nudged` on an agent cell is `error`: only nb nudges.
- `core.json` gains `duration_ms`, the agent's phase alone, and takes Harbor's
  token counts when the trajectory has none.

## 0.2.5 — 2026-10-16

Agent arms: a real vendor CLI, run by Harbor in the fixture's image, as an
arm beside nb's. Staging step 2 of `project/plans/agent-neutral-runs.md`:
compile, run and ingest, for Claude Code. Nothing on disk breaks; an nb
suite runs exactly as before.

- An arm with `"agent": "claude-code"`, `"agent_version"` (pinned) and a
  bare Anthropic `model` runs through Harbor. The suite pins
  `harbor.version` and sets `limits.wall_ms`; every task's fixture declares
  an `image` (`from`, `env`, `run`). `codex` and `qwen-code` are "not yet".
  `limits.tool_calls`, `tokens` and `cost_usd` are "not yet"; a sample hook
  in a suite with an agent arm is refused (its setup goes in the image).
- `suites/proctor.json` gains `agents.claude-code` (`base_url`, `api_key`,
  `${VAR}` resolved at `run`) and `harbor.uv`. The base URL's host is the one
  name the agent's container may reach. See `proctor.example.json`.
- `run` compiles each agent arm's tasks under `runs/<id>/harbor/tasks/` and
  records `harbor` (version, tasks hash) and `versions.harbor` in
  `experiment.json`; `resume` refuses compiled tasks changed on disk. Each
  cell runs one Harbor job in `<cell>/harbor/`, with telemetry off and the key
  only in Harbor's environment.
- An agent cell holds `trajectory.json` (ATIF), `agent/` (native logs),
  `diff.patch`, `verifier/` and `reward.json` (one key per script check: 1
  pass, 0 fail, 2 needs-judge, absent error), and `core.json`: proctor's own
  count (turns, tool calls, kinds, tokens, exit reason). Its manifest has
  `agent` in place of `runner`, `harness` and `provider`, which are now
  optional. A Harbor exception other than a timeout, a version other than the
  pinned one, or a tool the mapping does not know fails the cell.
- `grade` skips an agent cell and says so: grading from the trajectory and
  the rewards is staging step 3.
- `Subprocess.Run` takes a timeout; the Harbor process is killed past the
  wall limit plus build, verifier and start-up time.

## 0.2.4 — 2026-10-10

Two fixes and `report --against`. Nothing on disk breaks; `stats.json` only
gains fields.

- `report <id> --against <other>` compares each arm with the arm of the same
  id in another experiment, of this suite or another, in place of the
  baseline: "did the new condition help each arm?". The other experiment's
  decided cells are rescored under this suite's lists from their
  `checks.json`, with no judge calls, and the gap rule below applies. An arm
  the other experiment lacks is listed as unpaired. `stats.json`
  `baseline.against` and `baseline.unpaired`; `--tolerance` and
  `--fail-on regression` work as with a baseline.

- A baseline whose pinned cells were never graded on one of the suite's
  current `pass` checks no longer rescores them to 0. Those tasks keep the
  pinned score, `stats.json` lists the check under `baseline.gaps` (check,
  experiment, tasks), and the report and `report`'s console output warn.
  Within an experiment a missing pass check still fails the run.
- A `null` in a suite's `arms`, `grading.pass` or `grading.validity` is a
  problem naming the field, not a crash.

## 0.2.3 — 2026-09-23

Reasons that read right at a skim. Nothing on disk breaks. A model check's
reason comes from its verdict file, which a plain `grade` reuses, so an
existing experiment gets the new `decide` and `judge` wording only with
`grade --rejudge`; the other changes apply on the next `grade` or `report`.

- A `decide` reason says `, expected <answer>` whenever the label is not the
  expected one (`yes p=0.98, expected no`), below the threshold too; a
  `judge` check that fails unanimously says `no 3/3, expected yes — "…"`.
- A check with several fields leads its reason with the parts that decided
  the verdict, then the rest.
- `summary.md` writes code in single backticks; a path inside quotes in a
  free-text reason is no longer wrapped as code.

## 0.2.2 — 2026-09-23

The report, redesigned: a verdict card up top, one card per task, and a
table of contents. Nothing on disk breaks; `stats.json` only gains fields.

- `stats.json`: `baseline.arms.<arm>.confirmed` (the whole 95% interval lies
  beyond the tolerance on the verdict's side, or within it for `held`);
  `baseline.arms.<arm>.tasks.<task>` gains `diff_points` and `verdict` (the
  same tolerance, per task); `comparisons[].tasks` is the per-task difference
  in points; `arms.<arm>.tasks.<task>` is `{score, passed, decided}`.
  `verdict` is unchanged and is still what `--fail-on regression` reads.
- `report.html`: a sticky rail of sections and tasks (hidden below 900px)
  with an inline scrollspy; the verdict card per arm (Better, Worse or
  Unchanged, confirmed or not, the tasks behind it, why it cannot be
  confirmed, and which arm to read with care); a card per task with its
  checks (its own first) and every arm's runs, score and baseline verdict,
  and each run that did not pass. The Checks section and Results by task
  are folded into the task cards. Explanations are muted; monospace is for
  commands and paths only. The run id moved to Reproducibility and method.
  The wizard hat is inlined from `assets/pointy-hat.svg`, credited in the
  footer (CC BY 3.0).
- `summary.md` carries the same blocks: the card as a list, a `###` per task.

## 0.2.1 — 2026-09-23

A task's own description of a shared check reaches the report
(`project/bugs/per-task-check-descriptions-never-reach-the-report.md`).

- `stats.json` `task_details.<task>.checks` is a list of
  `{name, level, description}` instead of names; `level` is `suite`,
  `fixture` or `task`. `descriptions.checks` carries a check only when every
  declaration describes it the same way. The field is a day old and read by
  nothing outside proctor, so this ships as a patch.
- The report's Tasks table column is "How it is measured": the task's
  fixture- and task-level checks, headline first, each with the task's own
  sentence. A headline every task declares under one name is on every row.
  The Checks table reads "per task; see Tasks" for a check the tasks describe
  differently, and Failures puts each task's sentence beside the task.

## 0.2.0 — 2026-09-23

The vocabulary and the layout, renamed to suite / task / sample / check
(`project/plans/suite-task-check.md`). Breaking:

- `evals/` is `suites/`, `eval.json` is `suite.json`, `cases/` is `tasks/`;
  `evals/eval-authoring` is `suites/suite-authoring`.
- `experiment.json`: `eval`, `eval_hash`, `eval_def`, `cases` are `suite`,
  `suite_hash`, `suite_def`, `tasks`. `manifest.json`: `case` and `eval_hash`
  are `task` and `suite_hash`. `baseline.json`: `eval_hash` and `cases` are
  `suite_hash` and `tasks`. `results.jsonl` rows say `task`; `stats.json`
  says `suite`, `tasks`, `n_tasks` and gains `task_details`.
- Hooks and script checks get `PROCTOR_SUITE_DIR`, `PROCTOR_TASK` and
  `PROCTOR_TASK_JSON`; the program template placeholder is `{{task}}`;
  `baseline --cases` is `--tasks`.
- Every reader accepts the 0.1 spellings for this release only: the
  directory and file names, the JSON keys under `runs/` and in a baseline,
  the old environment names beside the new, and `{{case}}`. Writers use
  the new ones. 0.3 drops the old ones.

Added:

- A task file may declare `checks`, its own beyond the suite's and its
  fixture's; a cell carries the union, and a name at two levels is a problem.
- `@expect` on a built-in field reads `expect.<check>.<field>` before
  `expect.<field>`; a model check's `ask` may be `"@expect"`, read from
  `expect.<check>.ask`.
- The report's Tasks table shows each task's fixture and its own checks;
  the Checks table says which tasks carry each check.

## 0.1.0 — 2026-09-22

The first slice: `list`, `run`, `resume`, `grade`, `report`, `baseline`;
fixtures, arm bundles and baselines; containerised runs; the `decide` and
`judge` model checks; the report rebuilt for a reader who was not there.
