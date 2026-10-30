# proctor — developer notes

Experiment manager, grader and reporter around nb (`../nb`). The intent is in
`project/brief.md`; the layout every verb reads and writes is
`project/plans/on-disk-layout.md`; what this version does and does not do is
`project/plans/first-slice.md`.

## Build and test

```bash
dotnet build
dotnet test --no-build                 # ~1 min; the runner tests spawn nb per cell
PROCTOR_APPROVE=1 dotnet test --no-build --filter ReportTests   # approve a changed rendering
dotnet bin/Debug/net10.0/proctor.dll list             # or: dotnet run -- list
```

Tests need nb built: `$NB_PATH`, else `../nb/bin/Debug/net10.0/nb` (build nb
with `dotnet build` in its repo; its Mock provider drives every test here).

## Bugs

File a bug as a GitHub issue (`gh issue create`), not a file under
`project/bugs/`; that directory holds the bugs filed before 2026-10-10 and
gains no new ones. When planning a bug bash, start from the open issues
(`gh issue list`), then any `status: open` file in `project/bugs/`.

## Vocabulary

Fixed in `project/plans/suite-task-check.md` against Anthropic's
*Demystifying evals for AI agents*. Use these words in code, files, prose
and the report, and no others for these things:

| word | one sentence |
|---|---|
| **suite** | A collection of related tasks with one business goal, graded by one shared checklist so a pass rate across them means one thing. |
| **task** | One input and one desired outcome: a prompt against a fixture, with the checks that say whether the outcome was reached. |
| **sample** | One measurement of a stochastic system: one attempt at one task by one arm. |
| **check** | One yes-or-no item on the checklist, asked of a finished sample. Where it is listed sets its role: `pass`, `validity`, or a guardrail rate. |
| **arm** | What is under test: a harness, a provider, a model and a bundle, run over every task. |
| **fixture** | The repository a task runs against and what "done" looks like in it; reused across suites. |
| **experiment** | One execution of one suite across every arm, task and sample. |
| **baseline** | Pinned cells from an earlier experiment, compared with as a virtual arm. |

A cell is one arm, one task, one sample: the directory a sample's evidence
lives in. The report says "run" for a sample. The spellings before
2026-09-23 (`evals/`, `eval.json`, `cases/`, `eval_hash`, `PROCTOR_EVAL_DIR`,
`PROCTOR_CASE`, `{{case}}`) are accepted by every reader for one release
(`Layout.SuiteFileIn`, `Runner.ReadJson`, `CellContext.Environment`) and
written by none.

## Structure

One project, one executable, no library split. The root holds the entry point
and the project files; the source sits in one directory per pipeline stage,
in the order the verbs run them. Every file is one concern:

| file | holds |
|---|---|
| `Program.cs` | flag parsing and verb dispatch; `ProctorException` is a user-facing failure |
| `Verbs.cs` | one method per verb: `list`, `run`, `resume`, `grade`, `report`, `baseline` |
| `Suite/Layout.cs` | paths and file names, nothing else |
| `Suite/Suite.cs` | `proctor.json` (the host binary and its config), `suite.json` (arms, the `nb` block with the runner and mounts, hooks, grading), tasks, the program template: records, loading, validation (`Problem` = file, field, message); the merged check set and expect block per task |
| `Suite/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Suite/Judges.cs` | `proctor.json`'s `judges` block: an endpoint per name by wire shape (`systemone`, `chat`), key resolution, which judge a check gets |
| `Suite/Tree.cs` | a directory's files minus excluded names, and their content hash (fixtures and bundles) |
| `Suite/Baseline.cs` | `suites/<suite>/baseline.json`: pinned cells per task; scores recomputed from the cells while they exist |
| `Run/Runner.cs` | experiment and cell manifests, the matrix loop, hooks, nb as a subprocess, an agent cell through Harbor, resume |
| `Harbor/Compile.cs` | an agent arm's task as a Harbor task (`instruction.md`, `task.toml`, the image, the verifier) and a cell's job file |
| `Harbor/nb_agent.py` | nb as a Harbor agent: uploads the cell's program and nb's config, runs nb on stdin; embedded, compiled into `runs/<id>/harbor/agents/` (Python, not compiled into proctor) |
| `Harbor/oracle_user.py` | the oracle as Harbor's simulated user, on the host: sends the task over the ACP bridge (`acpx`), judges each finished turn with the host's `nb --judge`, answers from the sheet, writes `oracle-user.json`; embedded and compiled like `nb_agent.py` with its template (`oracle_user.j2`) when an agent arm meets an oracle task |
| `Harbor/qwen_code_agent.py` | Harbor's Qwen Code agent with its per-trial network install replaced by a check that the image holds the pin; embedded and compiled like `nb_agent.py` |
| `Run/Harbor.cs` | one cell's Harbor job: `uv tool run harbor@<pin>` with a seven-day `--exclude-newer` cooldown on everything it resolves, telemetry off, the key in its environment only; nb's layer image from its publish |
| `Run/Ingest.cs` | a Harbor trial into a cell, and the counted core (`core.json`) from the ATIF trajectory |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, Harbor, script checks, git; an optional timeout |
| `Grade/Transcript.cs` | nb JSONL, or an agent cell's ATIF trajectory with its `core.json`, into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
| `Grade/Window.cs` | what a model check sees: named extractions from the transcript, joined with `+`, compiled into labelled fences |
| `Grade/Judge.cs` | `JudgeClient` (the transports), the `decide` check over systemone, the `judge` check over chat, the verdict file as cache, the report's judge provenance |
| `Grade/Grade.cs` | checks over a cell into `checks.json`; the headline pass |
| `Report/Results.cs` | `results.jsonl` rows |
| `Report/Stats.cs` | Wilson, Newcombe paired, MDE, summaries into `stats.json` |
| `Report/Report.cs` | `report.html` and `summary.md`, pure functions of stats and results |
| `suites/smoke/` | proctor's own suite: every task scripts nb's Mock provider |
| `suites/code-change/` | the first real suite: three tasks on three fixtures, acceptance tests, reference solutions; four agent arms through Harbor (nb, Qwen Code, Claude Code, Codex) |
| `suites/gate/` | the deterministic gate: one Mock arm bare and the same arm as a Harbor agent; `GateTests` (with `PROCTOR_GATE`) asserts they agree |
| `suites/gate-oracle/` | the oracle's gate: the same pair on three oracle tasks, one per ending (`ok`, `oracle_miss`, `oracle_budget`); `OracleGateTests` (with `PROCTOR_GATE`) asserts the in-process oracle and the bridged simulated user agree, apart from the judge's tokens |
| `suites/oracle-change/` | the oracle on a real model: `add-retry-flag` with its semantics only in the answer sheet, chosen to be unconventional so a guess fails acceptance; nb on Anthropic Haiku bare and through Harbor, judged by Haiku. Real API calls (about $1 a run) |
| `suites/conformance/` | Harbor conformance at $0: every vendor agent at its pin against `fixtures/stub`'s scripted model, tasks `ok`, `fail`, `hang`; `ConformanceTests` (with `PROCTOR_CONFORMANCE`, `1` or a `proctor.json` whose `harbor` block to run on) asserts each cell came back as scripted. The plan and its benchmark are `project/plans/harbor-conformance.md` |
| `suites/suite-authoring/` | the demo project: an agent writes suites, graded by running them against planted good and bad runs (`trial/`); a run's report is meant to be copied into `report/`, which has none yet |
| `fixtures/` | the repositories tasks run against, each with its own checks beside (never inside) its `repo/` |
| `assets/` | the wizard hat icon, adapted from Lorc's "Pointy hat" (game-icons.net, CC BY 3.0); not MIT. Wherever it ships (a favicon in `report.html`, the site), the credit goes with it |
| `Proctor.Tests/` | xunit, flat; `fixtures/` are captured Mock transcripts, and `fixtures/nb-run/` pairs of nb's JSONL and its own trajectory from one run (`capture.sh` regenerates them); `snapshots/` are the approved renderings; `suites/` is test data `TestRepo.CopySuite` finds after `suites/` (`harbor-unit`, which `HarborTests` build on with a fake uv), never a suite to run; `python/` is `oracle_user.py`'s unittest suite, on stub `harbor` modules and captured `acpx` output, which `OracleHarborTests` runs with `python3` |
| `docs/` | user-facing guides beyond the README: `porting-to-agent-neutral.md`, how a suite moves to agent arms (a draft, from porting `code-change`); `podman.md`, what a machine needs for `harbor.engine: podman`, why, and a sandbox for Harbor |
| `project/` | the brief, the plans, the research notes and the loose ends |

The directories are for reading, not for namespaces: everything is
`namespace Proctor`. The data directories `suites/`, `fixtures/`, `bench/`,
`runs/` and `reports/` are lowercase and excluded from compilation in
`Proctor.csproj`.

## Conventions and gotchas

- **`failed` is infrastructure; a bad transcript is `completed`.** A hook that
  exits non-zero or an nb startup error (exit 1, no `result` trailer) marks the
  cell failed with a reason. A transcript whose exit reason is `max_tool_calls`
  is completed; grading decides what it means.
- **Grading writes beside the evidence.** `checks.json` is the only file the
  grader adds to a cell. `report` reads it; a completed cell without one is
  reported as not analysed, and `report` says to run `grade`.
- **A check is one of three kinds by where it is named.** In `grading.pass`
  it is capability; in `grading.validity` it decides whether the sample
  counts (a `fail` excludes the sample with its reason, `error` never does);
  in neither it is a guardrail rate. A name in both lists is a problem.
- **A check is declared at one of three levels, and a cell carries the
  union.** The suite's `grading.checks` reach every task; a fixture's
  `checks` reach every task on it; a task's own `checks` reach that task
  (`Suite.ChecksFor`, suite then fixture then task). A name at two levels is
  a problem. A `pass` or `validity` check the suite does not declare must
  reach every task from its fixture or its own block. A task's scripts
  resolve against the suite directory. A check's rate is over the cells
  that carry it, so a task-only check reports over one task.
- **The fixture is the task's; what is under test is the arm's.** A task
  names a fixture; the fixture's checks join the suite's for that cell (a
  clash is a problem) and its default `expect` sits under the task's. Only
  `repo/` is ever copied into the work directory, so the checker and the
  expectations are unreachable from inside it by construction. Proctor does
  the checkout (commit, then `diff.patch` after teardown) and restores
  fixture plus diff for a regrade whose checkout is gone. An arm's `bundle`
  (`{path}` under the repository root, or `{git, rev}` cloned under
  `.proctor/bundles/<rev>`) is resolved once per experiment and recorded
  by source, path and hash in `experiment.json` and every cell manifest;
  proctor never reads what is inside it. `resume` refuses a changed bundle
  as it refuses a changed suite.
- **An arm with an agent runs through Harbor; an arm without one is nb run
  bare.** `arm.agent` is a vendor agent (`claude-code` on Anthropic models,
  `codex` on OpenAI's, `qwen-code` on Qwen's), with a pinned
  `agent_version` and a bare model of the agent's vendor, the nb fields
  refused; or `nb`, nb itself as a Harbor agent, with `harness`,
  `provider` and `model` as on the bare path and `runner`, `command` and
  `agent_version` refused. (Decision 1's rule, Harbor exactly when the
  program has tools, waits for validation to refuse tools on the bare path;
  until then `agent: nb` is how an nb arm asks for Harbor.) The plan is
  `project/plans/agent-neutral-runs.md`; what Harbor writes is
  `project/learnings/harbor.md`. `run` compiles each (agent arm, task) once
  under `runs/<id>/harbor/tasks/<arm>/<task>/` (so `harbor` is not an arm
  id): the prompt as `instruction.md`, the fixture's `repo/` (and only it)
  committed in the image `fixture.image` describes, the arm's agent at its
  pin, and `tests/` with the suite, the fixture minus `repo/`, and a
  `test.sh` running every script check in Harbor's separate, offline
  verifier (one `reward.json` key per check). A vendor agent reaches the
  host of `proctor.json` `agents.<agent>.base_url`, exactly as spelled, its
  key in Harbor's process as `ANTHROPIC_API_KEY` or `OPENAI_API_KEY`; nb
  reaches the host of its provider's `Endpoint` in nb's config, or nothing
  (Mock), its endpoint `${VAR}`s passed by value and its key by name. Each
  cell is one Harbor job in `<cell>/harbor/`; `Ingest` lifts the trial into
  the cell and writes `core.json`, proctor's own count, strictly: an
  unknown tool or event, a version other than the pin, or no trajectory
  fails the cell. An agent timeout is a completed run with `time_budget`;
  a vendor agent that took no turn at all is failed, since its model never
  answered (Codex retries an unreachable endpoint until the timeout).
  `grade` reads an agent cell like any other: `Transcript.Read` takes
  `trajectory.json` when there is no `transcript.jsonl`, with `core.json`
  as the trailer (so a budget check reads proctor's count, never the
  agent's) and the main agent's conversation as the windows (a subagent's
  calls are counted, not listed). A script check is not rerun on the host:
  its verdict is the cell's `reward.json` key and its reason the first line
  of `verifier/<check>.out`, a missing key `error`. `loop_nudged` is
  `error` on a vendor agent's cell, since only nb nudges; a script check
  that reads `PROCTOR_TRANSCRIPT` has none in the verifier.
- **nb in Harbor is the host's nb.** `proctor.json` `harbor.nb` is nb's
  self-contained linux-x64 publish; `run` builds it once into a
  `proctor-nb:<content hash>` image that each nb task's Dockerfile copies
  `/opt/nb` from, and refuses a publish whose `nb --version` is not the
  host nb's, since the host compiles the program (per cell, `{{work}}` as
  `/app`) and the image runs it. The agent (`Harbor/nb_agent.py`) is on
  Harbor's `PYTHONPATH` from `runs/<id>/harbor/agents/`. nb writes its own
  ATIF to `agent/trajectory.json` (`--trajectory`), beside its JSONL
  (`agent/nb.jsonl`, with `nb.stderr.log` and `nb.exit`); ingest takes it
  as a vendor agent's. Bare cells get one too (`NB_TRAJECTORY`, so a runner
  script passes it on), though grading reads their JSONL. `NbTrajectoryTests`
  holds every `fixtures/nb-run` pair to reading the same both ways. The gate
  (`suites/gate`, `GateTests`, decision 12) is opt-in:
  `PROCTOR_GATE=<publish dir> dotnet test --no-build --filter GateTests`,
  about ten minutes, Docker (or podman, with `PROCTOR_GATE_ENGINE=podman`)
  and uv needed. The filter takes `OracleGateTests` too (`suites/gate-oracle`,
  about four minutes). The publish must have `--acp`, so republish after an nb
  change the bridge sees.
- **Harbor's engine is the machine's.** `proctor.json` `harbor.engine`
  (`docker`, the default, or `podman`) is Harbor's `environment.type` in
  every cell's job file and the CLI nb's layer is built with; podman's
  local tag is `localhost/proctor-nb:<hash>`, spelled out so the compiled
  `COPY --from` resolves no short name. `experiment.json` records it and
  `resume` refuses a changed one, since the compiled tasks name a layer in
  that engine's store. Podman needs `podman.socket` running (Harbor's
  preflight probes `podman compose ls`), docker-compose v2 behind
  `podman compose` (podman-compose has no `ls` and rejects
  `exec --no-TTY`), a search registry, since the egress sidecar is
  `FROM gogost/gost`, and `compose_warning_logs = false`, since Harbor
  reads the compose banner as agent output; `run` and `resume` check the
  last three before anything starts (`HarborRun.CheckPodman`). The
  machine's setup is `docs/podman.md`; what was seen is
  `project/learnings/harbor.md`.
  The bare path's hooks pick their own engine (`command -v podman`).
- **A check of the layer costs nothing.** Proctor checks that it drives
  Harbor and reads back what Harbor wrote, never Harbor itself and never a
  model: the unit tests, the gate (nb on Mock) and conformance (each vendor
  agent on the stub) cover it, with no hosted model, judge or gateway. A
  hosted model runs only in an experiment whose question is that model,
  its spend agreed first. `PROCTOR_CONFORMANCE=1 dotnet test --no-build
  --filter ConformanceTests` runs the stub cells and the oracle pass (each
  `code-change` task's `reference/<task>.sh` as Harbor's `solution/`,
  every script check 1); Docker (or podman) and uv needed, about half an hour, $0.
- **The stub model is the vendor agents' Mock.** `fixtures/stub/services/stub.py`
  speaks Anthropic Messages, OpenAI Responses and Chat Completions and
  reads its script from the prompt: `STUB:run=<cmd>` (one call to the
  agent's shell tool, then `tools: <every tool the request offered>`),
  `STUB:fail` (500s), `STUB:hang` (the call, then silence). It runs as a
  compose service from the fixture's `image.services` directory, which
  compile copies into the task's `environment/`; Harbor puts such a
  service in the main container's network namespace, so the agent reaches
  it at `localhost:8765` and the host listens on nothing. Its tool list is
  how `Ingest.Kinds` keeps up with an agent's pin: a tool it offers that
  `Kinds` lacks is a conformance miss before any model ever calls it.
- **Codex and Qwen Code are installed in the image, never at trial time.**
  Both come from npm on Node 22 by nvm under `~/.nvm`, where Harbor's agents
  look. Harbor's Codex agent skips its install when `codex --version` is the
  pin; Harbor's Qwen Code agent reinstalls from the network on every trial,
  so proctor brings its own (`Harbor/qwen_code_agent.py`, imported like
  nb's), which fails setup when the image's version is not the pin. Harbor
  runs both with approvals and sandbox bypassed (it has no other mode), so
  `denied` is 0 by construction, as under Claude Code's
  `bypassPermissions`. No vendor agent sees the program template: its
  instruction is the task's prompt. `proctor.json` `agents.codex.config` is a native
  Codex `config.toml` (relative to `suites/`) that Harbor merges its base
  URL over: what an endpoint needs of the agent, such as a llama-server,
  which takes only function tools (`web_search = "disabled"`,
  `[features] multi_agent = false`).
- **The baseline is a virtual arm in the statistics, not in the experiment.**
  `report` resolves `baseline.json` to per-task scores (re-read from the
  pinned cells' `checks.json` under the current `pass` and `validity`
  lists when the experiment is still under `runs/`, else the pinned score)
  and compares every arm to them with the same Newcombe pairing. The verdict
  is `held`, `improved` or `regressed` on the point estimate against
  `--tolerance`; `--fail-on regression` turns it into an exit code. The
  baseline is not in the accounting or the matrix. `report --against <exp>`
  replaces it for one report with per-arm scores from another experiment
  (`GuardInput.PerArm`), arms paired by id, built as pins by
  `Baseline.PinOf` and resolved by the same `Resolve`; the page then says
  "earlier run" where it said "baseline". A pinned cell graded without a
  current `pass` check is not rescored: its task stands as pinned and the
  check is a `baseline.gaps` warning.
- **Every id in the report carries a sentence.** `description` is the one
  key in a check spec that is not a check (`Checks.Description`); a script
  check must declare one, a built-in derives one from its fields
  (`Checks.Describe`). Suite, arm and task descriptions are optional; a task
  falls back to the first prompt line (`Suite.Describe`). They travel in
  `stats.json` as `descriptions`, with a per-arm `failures` list (each
  non-validity check that failed or errored in a counted cell, most often
  first, with its tasks), which the Failures section renders.
  `descriptions.checks` holds a check only when it says one thing
  everywhere (`Suite.CheckDescriptions`); a check each task declares in its
  own words is absent there, and its sentence is per task in
  `task_details.<task>.checks` (name, level, description). The Tasks table
  shows a task's fixture- and task-level checks with those sentences, the
  Checks table says "per task; see Tasks", and Failures puts the task's
  sentence beside the task. "Shared" in the report means declared by the
  suite, never "carried by every task".
- **The report is one list of blocks rendered twice** (`Report.Blocks`, then
  `RenderHtml` and `RenderMarkdown`), so the two renderings cannot drift in
  wording; a cell may carry HTML for a link or hover. Inline forms in block
  text: `` `id` `` (body font, no wrap), ``` ``code`` ``` (monospace: only
  commands and paths; `Report.Paths` finds paths in free-text reasons),
  `**bold**`, `*italic*`, `[text](#anchor)` (a link on the page, its text in
  `summary.md`). A `Para` with class `explain` is definitional text, muted;
  findings are never muted. Colour is semantic only: green passed/better,
  red failed/worse, amber not counted or a warning, purple a link.
- **The verdict card is direction plus confidence, both from `stats.json`.**
  Better/Worse/Unchanged is the baseline `verdict` (point estimate against
  `--tolerance`); "(unconfirmed)" is `confirmed: false`. Without a baseline
  the card compares each arm with the reference at no tolerance, confirmed
  when the Newcombe interval excludes zero. The card never carries model
  output. The structure and the
  reader-facing vocabulary (arm, task, run, check; counted, decided; "95%
  interval") are `project/plans/report-structure.md`. On disk a run is still
  a cell and a sample; only the page says run.
- **An undecided run is a third headline outcome.** `Grade.Pass` is `bool?`:
  false when a pass check failed, errored or is missing; null when nothing
  failed but a check is `needs-judge`. An undecided run is counted (it is
  valid) but not decided: out of the pass rate on both sides, out of that
  check's rate, listed under Failures when there are at most five, and a
  warning band when a check is undecided in more than five runs or 5% of
  the runs it applies to (`stats.json` `undecided_checks`). A check that
  cannot decide is the check's weakness, not the arm's.
- **Every number is computed once, in `Stats.cs`.** The renderers format; they
  never compute. If a number looks wrong, fix it in `stats.json` first.
- **Each task is scored as its mean over its counted, decided samples**, so `n` in
  every interval is the task count. Per-arm rates: Wilson. Paired differences:
  Newcombe method 10 with his continuity-corrected phi from the 2x2 task
  table, built from the task means when there are several samples. This
  reduces to the textbook binary methods at one sample and never gives a
  zero-width interval.
- **Version and changelog.** `<Version>` in `Proctor.csproj` is what
  `--version` prints and `versions.proctor` records. Before 1.0 a minor bump
  is a breaking change to the layout, a file format or the vocabulary, and
  `CHANGELOG.md` says what broke and what still reads the old form; bump
  both in the same change.
- **`versions.nb` is `nb --version` from the host binary**, the `+commit`
  suffix stripped; `unknown` when it fails. It is read once per experiment.
- **nb's trailer carries `duration_ms` since nb 0.9** (nb f7df67f); the
  `max_duration_ms` check reads it and falls back to the cell's wall time
  from the manifest, hooks included, which is what the report shows. It
  carries no cost, so the report has no cost column rather than an estimate.
- **nb emits no `assistant_json` event**; the `answer_json` window is the last
  ` ```json ` fence in the last assistant message.
- **Program templates.** Placeholders are `{{prompt}}`, `{{task}}`, `{{work}}`
  (the fixture checkout), `{{bundle}}` (the arm's resolved bundle directory,
  empty without one), `{{provider}}`, `{{model}}`, `{{harness}}`, `{{arm}}`,
  `{{sample}}`. `{{work}}` and `{{bundle}}` are the paths the *model* will
  see: the suite's `nb.mounts` replaces them when a runner is in effect (`CellContext`
  `WorkMount`/`BundleMount`), and a bare run ignores the mounts so
  `--runner none` stays a host shakedown. A prompt's newlines become nb continuation lines (` \`), so a
  multi-line prompt stays one directive. A prompt line that itself ends in a
  backslash cannot be expressed.
- **The judge is proctor's own client, never nb.** `decide` posts to a
  `systemone` endpoint (Jev on Cloudflare through a local gateway today; the
  wire format is the seam, not the vendor); `judge` goes through
  Microsoft.Extensions.AI's `IChatClient` over an OpenAI-dialect endpoint,
  so a provider change is a config line. Both are `Verdict`s like any
  check. The question is the suite's (the check spec), the endpoint is the
  machine's (`proctor.json` `judges`), and the key and endpoint are
  resolved at grade time only (`${VAR}` in both), so `list` and `run`
  need no key. Verdict files and the report keep the endpoint as written,
  so neither names the machine that graded. `Suite.Load` resolves each
  model check's judge when handed the config's judges; the test helper
  passes none, and `grade` resolves again itself.
- **A model check sees a window, never the transcript.** `Window.Known`
  is the whole vocabulary; an empty part or a total over the judge's
  `max_window` is `error` before any call, because a decider answers an
  empty or cut state confidently. Arm id, model name and sample number are
  in no window and no prompt.
- **`verdicts/<check>.<judge>.<hash>.json` is the cache and the record.**
  The hash covers the compiled request, the model, the threshold and the
  expectation; `grade` reuses a file that exists, unless its verdict is
  `error` (an endpoint that did not answer is retried), and `--rejudge`
  reuses nothing.
  The file keeps the request (key redacted), every response, each sample
  as read with why it was discarded, the reasoning, usage and the verdict.
  Deterministic checks are always recomputed. `--judge a=b` is a
  comparison pass: only the checks whose declared judge is `a` are
  evaluated, by `b`, into `b`'s files marked `applied: false`, and the log
  prints both verdicts per cell; `checks.json` is never written. The
  report's judge rows count applied files only, so it names the judge
  whose verdicts the cells hold. Changing `with` in the suite and grading
  plain is how `b` becomes the applied judge, and a plain grade that reuses
  `b`'s file marks it applied.
- **A throttled judge is stopped for the pass, not retried per sample.**
  Every judge request goes through `JudgeClient.Send`: a refusal that
  `Throttle.Of` recognises (429, 503 or 529, or a rate-limit phrase in the
  body, as nb's `RateLimitRetry` reads one) is retried three times at 2, 4
  and 8 s or its `Retry-After`; a `Retry-After` over a minute, or a fourth
  refusal, stops that judge, and every later check on it is `error` with
  the endpoint's own words and no request. Nothing is lost, since the next
  `grade` asks an `error` again. The OpenAI SDK's retries are off
  (`ClientRetryPolicy(0)`), or each attempt is four requests underneath.
- **A judge's evidence is verified, its reasoning discarded.** Every quote
  must be a substring of the window with all whitespace removed from both,
  against the window as written or with its JSON string escapes undone
  (a judge quotes JSON compacted and unescaped); a sample with
  an unverifiable quote, no quote on a yes/no, or no JSON block is
  discarded but stays in the file. Fewer than two usable samples (one when
  `samples` is 1) is `error`; unanimous decides; a split or an `unknown`
  is `needs-judge`. The reason string is `yes 3/3 — "first quote"` (on a
  fail, `no 3/3, expected yes — "…"`; a `decide` likewise says
  `, expected <answer>` whenever its label is not it), so the
  matrix tooltip shows evidence without opening the cell.
- **Calibration is the bench's, not proctor's.** A threshold or a rubric
  earns a place in `grading.pass` by the numbers in `project/plans/jev-trial.md`;
  proctor records which judge, model and prompt hash graded a cell (the
  report's reproducibility section) and carries no kappa.
- **Check values from the task.** A built-in field whose value is
  `"@expect"` reads `expect.<check name>.<field>` from the task, then
  `expect.<field>`; a task with neither yields `error`, never "does not
  apply", since a task that wants to opt out declares its own checks. A
  model check's `expect: "@expect"` reads `expect.<check name>`, which is
  the expectation itself or `{ask, expect}`; `ask: "@expect"` reads the
  `ask` from there, so a shared `correct` check carries a per-task rubric,
  and the verdict hash makes it a per-task verdict file.
- **Hooks and script checks** run with `PROCTOR_SUITE_DIR`, `PROCTOR_FIXTURE`,
  `PROCTOR_BUNDLE`, `PROCTOR_EXPERIMENT`, `PROCTOR_ARM`, `PROCTOR_TASK`, `PROCTOR_SAMPLE`,
  `PROCTOR_CELL`, `PROCTOR_WORK`, `PROCTOR_TASK_JSON`, `PROCTOR_EXPECT` (the
  merged block), `PROCTOR_TRANSCRIPT`, `PROCTOR_DIFF`, `PROCTOR_RUNNER` (the
  runner script in effect, empty on a bare run), `PROCTOR_NB` and
  `PROCTOR_NB_CONFIG` (the host binary and its resolved config, which a
  hook mounts for the runner), `PROCTOR_CONTAINER` (a name derived from the
  cell that proctor never uses), and `PROCTOR_WORK_MOUNT` and
  `PROCTOR_BUNDLE_MOUNT` (the paths the model was told; the host paths
  unless `nb.mounts` moved them). Hooks run in the suite
  directory; script checks run in the cell, a suite's resolved against the
  suite directory and a fixture's against the fixture directory. Scripts exit
  0/1/2 for pass/fail/needs-judge; anything else is `error`, never folded
  into fail. The sample order is: fixture checkout, setup hook, nb, teardown
  hook, diff. Teardown runs whenever setup ran, even when setup failed, so
  it must be idempotent. `PROCTOR_WORK` is never deleted (`.proctor/` is
  gitignored).
- **nb gets the program on stdin, compiled on the host, bare or through a
  runner.** `RunCell` writes the resolved source as `program.nb`, then runs
  `nb --compile` on the host binary in the suite directory (so `@file`
  includes resolve against the suite) and writes the JSONL as
  `program.jsonl`; that is what goes down stdin, so the container never
  holds a path to a sheet. `program_hash` stays the source's. A program nb
  refuses fails the cell before the checkout or any hook. `RunNb` is one
  code path: without a runner it starts `nb --output jsonl [--config] -`
  itself with only `NO_COLOR` added to the environment; with one it starts
  the script with nothing on argv and the cell environment plus `NO_COLOR`,
  and the script starts nb the same way. The bare path deliberately does not get the cell environment:
  `PROCTOR_EXPECT` in the model's reach would leak the answer. A runner that
  forwards its environment into the container wholesale would do the same.
- **The machine's config is never committed.** `suites/proctor.json` and
  `suites/nb.json` are gitignored; `*.example.json` beside them are the
  committed, host-neutral forms (`${LLM_GATEWAY}`, `${LLM_GATEWAY_KEY}`), and
  `run` says to copy them when the live file is missing. The tests read
  `nb.example.json` for its Mock providers, so keep those in it.
- **The runner is the suite's, the binary is the machine's.** `suite.json`'s
  `nb` block (`runner`, relative to the suite directory like a hook, and
  `mounts`) says how the suite runs nb, because the runner only works with
  the hooks that make its container; `suites/proctor.json` says only where
  the host binary and its config are. `--runner` overrides per run. A
  `runner` or `mounts` left in `proctor.json` is reported as a problem, not
  ignored.
- **Labels are the consumer's; proctor has no tags.** `labels` on a suite,
  a fixture or a task is a key with one or more string values that proctor
  stores, prints, filters on (`list --label k[=glob]`) and records on every
  `results.jsonl` row, never interprets. `Suite.LabelsFor` merges fixture,
  then suite, then task, key by key. A `tags` field is reported as removed.
  Whether a suite is judged is `Suite.Judged`, derived from a check naming a
  `judge` (not yet a known check), not declared.
- **`resume` refuses a changed suite.** The suite hash in `experiment.json` must
  match; a changed suite is a new experiment.
- **Validation says "not yet" rather than silently skipping**: `command` arms,
  run- and task-level hooks, `answer_json_schema`, `max_cost`. A task's
  `oracle` runs on nb arms only: bare, as nb's in-process oracle (its
  directives prepended by `Runner.OraclePrefix`); on Harbor, as
  `Harbor/oracle_user.py`, the simulated user, whose record ingest lines up
  with nb's trajectory (`Ingest.Oracle`). A vendor arm beside an oracle task
  is refused: Harbor leaves it no ATIF.
- **Snapshot tests** fail on any rendering change and leave a `.received` file
  (gitignored) beside the approved one. Review it, then approve with
  `PROCTOR_APPROVE=1`. Render to check: `google-chrome --headless=new
  --screenshot=out.png --window-size=1100,2400 file://.../report.html`.
- **The runner tests are slow** (nb takes ~2 s to start per cell); filter them
  out while iterating on anything else: `--filter 'FullyQualifiedName!~RunnerTests'`.
