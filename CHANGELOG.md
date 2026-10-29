# Changelog

Proctor's version is `<Version>` in `Proctor.csproj`; `proctor --version`
prints it and every `experiment.json` records it under `versions.proctor`.
Before 1.0 a minor bump means a breaking change to the layout, the file
formats or the vocabulary; a patch bump does not.

## Unreleased

- `NbAgent` is an ACP bridge target (nb's `plans/atif-and-acp.md` stage 5):
  it implements Harbor's `ACPAgentMixin`, declares `bridges={acp}`, and
  launches `nb --acp` with `--trajectory` in its logs dir, so nb rewrites
  `agent/trajectory.json` after every prompt and ingest reads it as on the
  direct path. Run once through Harbor 0.24 with the spike's scripted user
  on Mock (podman): two turns, one session. Nothing routes an arm through
  the bridge yet; that is the oracle's step 7.

- nb writes its own ATIF (`--trajectory`, nb's `plans/atif-and-acp.md` stage
  3), and `Run/NbAtif.cs` is gone. An nb cell in Harbor takes
  `agent/trajectory.json` as a vendor agent's, now with per-step model,
  tokens and cost. Bare cells gain `trajectory.json` beside
  `transcript.jsonl` (set by `NB_TRAJECTORY`, which a runner script passes
  on untouched); grading still reads the JSONL there. Needs nb at or after
  this change: an older nb ignores the variable, and its Harbor cells fail
  as "nb exited N without a trajectory".

## 0.3.5 — 2026-10-27

A bug bash: issues #2 to #5. Nothing that read before stops reading, hence
a patch.

- `grade` takes `--arm` (repeatable) and `--tasks`, grading only those
  cells and leaving the others' `checks.json` as they were, so a paid judge
  pass can be staged one arm at a time (#2). `baseline` refuses a second
  `--arm`; `report`'s note on ungraded cells counts them per arm.
- A judge's quote is verified with all whitespace removed, against the
  window as written and with its JSON string escapes undone, so a correct
  quote of indented or escaped JSON is no longer discarded (#3).
- A judge that stays throttled is stopped for the pass (#4): a 429, 503 or
  529, or a rate-limit phrase in the body, is retried at 2, 4 and 8 s or
  its `Retry-After`, then every later check on that judge is `error` with
  the endpoint's own words and no request. The OpenAI SDK no longer
  retries underneath. `grade` prints the stop, and errored checks per
  judge after its summary.
- Under `harbor.engine: podman`, `run` and `resume` refuse to start unless
  `podman compose` is docker-compose v2 and short image names resolve,
  saying what to install or add (#5). The README and the porting guide
  name the prerequisites, and how to reach a gateway on the host's
  loopback.
- Harbor's dependencies resolve under a seven-day cooldown
  (`uv tool run --exclude-newer "7 days"`), so a poisoned release pulled
  within the week never reaches a run; a Harbor pin under a week old
  fails to resolve until it ages.

## 0.3.4 — 2026-10-20

Agent arms on podman. Nothing that read before stops reading, hence a patch.

- `proctor.json` `harbor.engine` is the container engine Harbor drives,
  `docker` (the default) or `podman`: Harbor's job file states it as
  `environment.type`, and nb's layer is built in that engine's store
  (`localhost/proctor-nb:<hash>` under podman). Podman needs its API socket
  (`systemctl --user enable --now podman.socket`) and a compose frontend.
- `experiment.json` records the engine under `harbor.engine` (absent reads
  as `docker`), and `resume` refuses a changed one.

## 0.3.3 — 2026-10-20

Harbor conformance at no cost (`project/plans/harbor-conformance.md`).
Nothing that read before stops reading, hence a patch.

- `fixture.json` `image.services` names a directory beside `repo/` with a
  `docker-compose.yaml` of extra services, copied into each compiled task's
  `environment/`. Harbor runs them beside the agent, in its network
  namespace.
- `fixtures/stub`, a scripted model endpoint (Anthropic Messages, OpenAI
  Responses and Chat Completions; `STUB:run=`, `STUB:fail`, `STUB:hang`),
  and `suites/conformance`, every vendor agent against it.
  `ConformanceTests` (`PROCTOR_CONFORMANCE=1`) runs those cells and an oracle
  pass of `code-change`'s reference solutions through Harbor.
- Ingest maps the 16 tools the three agents offer at their pins that it
  lacked. Two kinds are new: `schedule` (Claude Code's `Cron*`,
  `ScheduleWakeup`) and `meta` (Qwen Code's `tool_search`, `tool_call`).
- `suites/agent-smoke` is retired: it is `Proctor.Tests/suites/harbor-unit`,
  test data that never runs.
- `code-change`'s `add-retry-flag` reference solution works again: it is a
  patch, and it no longer needs python.

## 0.3.2 — 2026-10-20

`code-change`'s first run on all four agents, and what it found. Nothing
that read before stops reading, hence a patch.

- Ingest knows nb's wire names: `todo_write` and `todo_read` (`todo` is the
  directive, not a tool) and `apply_patch`. Every nb cell that planned had
  failed `adapter_unmapped`.
- Ingest knows Codex's goal tools (`get_goal`, `create_goal`,
  `update_goal`, as `plan`) and `request_user_input`, a new kind, `ask`.
- `suites/proctor.json` `agents.codex` takes an optional `config`: a native
  Codex `config.toml`, relative to `suites/`, passed to Harbor's Codex agent,
  which merges the base URL over it. A llama-server Responses endpoint takes
  only function tools, so Codex on one needs `web_search = "disabled"` and
  `[features] multi_agent = false`. `config` on another agent is "not yet".

## 0.3.1 — 2026-10-18

Codex and Qwen Code as agents, and `code-change` ported to agent arms
(staging step 5 of `project/plans/agent-neutral-runs.md`). Nothing that
read before stops reading, hence a patch.

- `arm.agent` takes `codex` (OpenAI models: `gpt-…`, `o<n>…`, `codex-…`) and
  `qwen-code` (Qwen models: `qwen…`), each with a pinned `agent_version` and
  the model bare. `suites/proctor.json` `agents` takes `codex` and
  `qwen-code` beside `claude-code`; both are OpenAI-dialect routes, and the
  key travels as `OPENAI_API_KEY` in Harbor's process only.
- Both CLIs are installed in the task's image from npm, on Node 22 by nvm,
  at the pin. Codex is Harbor's own agent, which finds the pin and installs
  nothing. Qwen Code's Harbor agent reinstalls from the network on every
  trial, so proctor brings `Harbor/qwen_code_agent.py`, which checks the
  image's `qwen --version` against the pin instead and fails setup when it
  differs.
- Ingest maps each agent's tools to proctor's kinds (`Run/Ingest.cs`); an
  unknown tool is `adapter_unmapped`, as on Claude Code. Qwen Code's
  `save_memory` is a new kind, `memory`. Harbor runs both with approvals and
  sandbox bypassed, so `denied` is 0 by construction.
- `suites/code-change` runs four agent arms through Harbor: `floor` (nb on
  qwen3-coder-next, now `agent: nb`), `qwen` (Qwen Code on the same model),
  `cc-haiku` and `codex-mini` (one sample each). Its runner, mounts and
  sample hooks are gone; `fetchcli` and `widgets` gain an `image`. The
  `stays-in-work` validity check is dropped from it: it reads the
  transcript, which the verifier has none of, and in the agent's container
  there is nothing outside the checkout for it to guard. It moves, with
  the sample teardown hook, into `suite-authoring`, which still runs on the
  runner path and shared both. Its `baseline.json` is kept for `floor`.
- A vendor agent's run with no turn at all is failed at ingest ("its model
  never answered"), not a completed run: Codex retries an unreachable
  endpoint until the agent timeout, which would otherwise read as a
  `time_budget` run that did nothing.
- nb's Harbor agent took the key's variable as `secrets` (a list) while the
  job passed `secret`; it takes `secret` now. No run was affected: the only
  nb arm in Harbor so far was on Mock, which has no key.
- `docs/porting-to-agent-neutral.md`: the porting guide's draft, from the
  `code-change` move.

## 0.3.0 — 2026-10-18

nb as a Harbor agent, and the deterministic gate (staging step 4 of
`project/plans/agent-neutral-runs.md`). Two things in `suite.json` change
meaning, hence the minor bump:

- **`"agent": "nb"` now runs nb through Harbor.** It used to be the same as
  no agent (nb bare). An arm without `agent` is nb bare, exactly as before;
  no suite in this repository named `agent: nb`. The arm keeps `harness`,
  `provider` and `model`; `runner`, `command` and `agent_version` are
  refused on it, and `bundle` is "not yet".
- **`harbor` is no longer an arm id**: it is the experiment's directory for
  the compiled Harbor tasks, and an arm by that name put its cells there.

What lands:

- An nb arm's image copies nb's own layer: `suites/proctor.json` gains
  `harbor.nb`, nb's self-contained linux-x64 publish, which `run` builds once
  into a local `proctor-nb:<content hash>` image. Its `nb --version` must be
  the host nb's, which compiles the program; `resume` refuses a changed
  publish.
- The program is compiled on the host per cell, as on the bare path, with
  `{{work}}` as `/app`. nb's Harbor agent is proctor's
  (`Harbor/nb_agent.py`, compiled into `runs/<id>/harbor/agents/` and part
  of the compiled tree's hash); it uploads the program and nb's config and
  runs nb on stdin in `/app`.
- The agent reaches the host of its provider's `Endpoint` in nb's config,
  or nothing at all for a provider without one (Mock). The endpoint's
  `${VAR}`s go to the agent by value; the key's variable goes by name, its
  value only in Harbor's process environment.
- Ingest converts nb's JSONL (`agent/nb.jsonl`) to ATIF (`Run/NbAtif.cs`):
  one agent step per nb turn, and nb's own fields (a turn's source, a
  call's approval, a result's structured result, the trailer) under
  `extra`, which the ATIF reader reads back. An nb arm in Harbor fails as
  on the bare path: no result trailer before the time limit is a failed
  cell. `core.json` counts nb's denials.
- `loop_nudged` is graded on nb arms in Harbor; it stays `error` on a
  vendor agent's cell.
- `suites/gate/`: one Mock arm bare and the same arm through Harbor, five
  tasks. `GateTests` runs it when `PROCTOR_GATE` names the publish and
  asserts the two give the same nb transcript, counted core and
  `checks.json`, apart from timings and the checkout's path (a host
  directory bare, `/app` in the image).

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
