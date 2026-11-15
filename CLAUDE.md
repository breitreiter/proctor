# proctor — developer notes

Runner, grader and reporter of evals around nb (`../nb`). The intent is in
`project/brief.md`; the layout every verb reads and writes is
`project/plans/on-disk-layout.md`; the data model proctor is moving to is
`project/plans/exam-variant-attempt.md`. Built and dropped plans are in
`project/archive/`, written in the earlier words.

## Build and test

```bash
dotnet build
dotnet test --no-build                 # ~1 min; the runner tests spawn nb per attempt
PROCTOR_APPROVE=1 dotnet test --no-build --filter ReportTests   # approve a changed rendering
dotnet bin/Debug/net10.0/proctor.dll list             # or: dotnet run -- list
```

Tests need nb built: `$NB_PATH`, else `../nb/bin/Debug/net10.0/nb` (build nb
with `dotnet build` in its repo; its Mock provider drives every test here).

## Bugs

File a bug as a GitHub issue (`gh issue create`), never as a file. The
notes on bugs filed before 2026-10-10, all fixed, are in `project/archive/`.
When planning a bug bash, start from the open issues (`gh issue list`).

## Vocabulary

Fixed in `project/plans/exam-variant-attempt.md` (2026-11-15), which is
the data model written out as files and commands. Use these words in
code, files, prose and the report, and no others for these things:

| word | one sentence |
|---|---|
| **exam** | The question being answered, the tasks, and the checks that grade them. It does not hold the variants that take it. |
| **task** | One prompt against a fixture, with its own answer key for the exam's checks. |
| **check** | One yes-or-no grading question, shared across the exam's tasks and asked of a finished attempt. Where it is listed sets its role: `pass`, `validity`, or a guardrail rate. |
| **fixture** | The repository a task runs against and what "done" looks like in it; reused across exams. |
| **variant** | What is under test: an agent, a model and a bundle, as declared data. Its identity is its content. |
| **attempt** | One variant at one task, once, with the evidence. Several per task, because the agent is stochastic. |
| **batch** | The attempts one `proctor run` made: when, on what machine, at what versions. Not something a reader of the report needs. |
| **live** | A mark on one variant: the one in use now, and the reference a change is compared with. |
| **claim** | What should change between two variants, which checks and which way, said before the attempts are made. |

**The words are done; the model is not.** Step 1 of
`project/plans/exam-variant-attempt-removal.md` renamed everything, so the
code, the files and this page say exam, variant, attempt and batch. What
is still pending: step 2, variants out of `exam.json` into
`variants/<id>.json` and `attempts` set on the exam rather than per
variant; step 3, the pool (`run` fills what is missing, `grade`,
`friction` and `report` take the exam, `resume` goes); step 4, `live`
in place of the baseline (`baseline.json`, `proctor baseline`,
`report --against`, "the first variant is the reference"). Until then the
variants are inline in `exam.json`, attempts sit under the batch that
made them, and those baseline and resume pieces still run. Write nothing
new in the earlier words; the grep gate below lists them and says where
they may stay.

## Structure

One project, one executable, no library split. The root holds the entry point
and the project files; the source sits in one directory per pipeline stage,
in the order the verbs run them. Every file is one concern:

| file | holds |
|---|---|
| `Program.cs` | flag parsing and verb dispatch; `ProctorException` is a user-facing failure |
| `Verbs.cs` | one method per verb: `list`, `run`, `resume`, `grade`, `friction`, `report`, `baseline` |
| `Exam/Layout.cs` | paths and file names, nothing else |
| `Exam/Exam.cs` | `proctor.json` (the host binary and its config), `exam.json` (variants, the `nb` block with the runner and mounts, hooks, grading), tasks, the program template: records, loading, validation (`Problem` = file, field, message); the merged check set and expect block per task |
| `Exam/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Exam/EnvFile.cs` | the user's env file (`~/.config/proctor/env`, `%LOCALAPPDATA%\proctor\env` on Windows, or `PROCTOR_ENV_FILE`): `NAME=value` lines `Program.Main` loads into the process environment before any verb, the real environment winning, so every `${VAR}` resolver and child process sees them |
| `Exam/Judges.cs` | `proctor.json`'s `judges` block: an endpoint per name by wire shape (`systemone`, `chat`), key resolution, which judge a check gets |
| `Exam/Tree.cs` | a directory's files minus excluded names, and their content hash (fixtures and bundles) |
| `Exam/Baseline.cs` | `exams/<exam>/baseline.json`: pinned attempts per task; scores recomputed from the attempts while they exist |
| `Run/Runner.cs` | `batch.json` and attempt manifests, the matrix loop, hooks, nb as a subprocess, an agent attempt through Harbor, resume |
| `Harbor/Compile.cs` | an agent variant's task as a Harbor task (`instruction.md`, `task.toml`, the image, the verifier) and an attempt's job file |
| `Harbor/nb_agent.py` | nb as a Harbor agent: uploads the attempt's program and nb's config, runs nb on stdin; embedded, compiled into `attempts/<batch>/harbor/agents/` (Python, not compiled into proctor) |
| `Harbor/oracle_user.py` | the oracle as Harbor's simulated user, on the host: sends the task over the ACP bridge (`acpx`), judges each finished turn with the host's `nb --judge`, answers from the sheet, writes `oracle-user.json`; embedded and compiled like `nb_agent.py` with its template (`oracle_user.j2`) when an agent variant meets an oracle task |
| `Harbor/qwen_code_agent.py` | Harbor's Qwen Code agent with its per-trial network install replaced by a check that the image holds the pin; embedded and compiled like `nb_agent.py` |
| `Run/Harbor.cs` | one attempt's Harbor job: `uv tool run harbor@<pin>` with a seven-day `--exclude-newer` cooldown on everything it resolves, telemetry off, the key in its environment only; nb's layer image from its publish |
| `Run/Ingest.cs` | a Harbor trial into an attempt, and the counted core (`core.json`) from the ATIF trajectory |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, Harbor, script checks, git; an optional timeout |
| `Grade/Transcript.cs` | nb JSONL, or an agent attempt's ATIF trajectory with its `core.json`, into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
| `Grade/Window.cs` | what a model check sees: named extractions from the transcript, joined with `+`, compiled into labelled fences |
| `Grade/Judge.cs` | `JudgeClient` (the transports), the `decide` check over systemone, the `judge` check over chat, the verdict file as cache, the report's judge provenance |
| `Grade/Grade.cs` | checks over an attempt into `checks.json`; the headline pass |
| `Friction/Shell.cs` | a shell command as the friction rules read it: words, then actions (one per pipeline, named by its first command, heredoc bodies dropped); POSIX path and glob helpers, since the paths are the container's |
| `Friction/Friction.cs` | `Surface` (the exam's `surface` block, compiled) and the friction rules: an attempt's trajectory into `friction.json` (schema `friction/1`: the `attempt` it is about, points, each with a checked quote, and the summary by surface part) |
| `Friction/Reader.cs` | the reader stage of `friction --reader`: an attempt's trajectory rendered as text with a call id per call, the fixed instructions, the reply checked into points with a model source, the reading file kept in the attempt |
| `Setup/providers.json` | `proctor setup`'s provider catalogue, embedded: per kind its form fields (a secret's env variable), the jobs it `serves` (`?` untested) or serves `by_hand`, the site help anchor (never a vendor URL; an anchor once released is permanent), where it lists models. The plan and mockups are `project/plans/setup/` |
| `Setup/Catalogue.cs` | the catalogue as records; `ProviderKind.Fit(job)` |
| `Setup/KeyCheck.cs` | the key check: list a provider's models (no tokens), the ids sorted, or the provider's words with the key redacted and a reading that is the same for every kind; Azure, and an OpenAI-compatible entry with no endpoint yet, have none. `Proctor.Tests/fixtures/models/` are answers captured from each vendor, trimmed |
| `Setup/SetupState.cs` | `exams/setup.json`: the providers setup knows (kind, key variable, last check, the models listed) and the answer per job (`set`, `later`, `by-hand`; the sandbox's place), intent included; the job order |
| `Setup/SetupFiles.cs` | setup's writes in `exams/`: its own nb.json entries (the key as `${VAR}`, no `Model`), proctor.json `agents.<agent>` and `harbor.engine`, `exams/.gitignore`; read as a JSON tree and written back whole, so an entry setup did not write is never touched, and a file it cannot parse is refused, not overwritten |
| `Setup/SetupWizard.cs` | `proctor setup`: the Spectre wizard, job by job (use a provider, add one, skip); the provider flow (pick, form, key check, name); the rerun menu; the summary. Refuses with exit 2 without a terminal. The only file that uses Spectre: `--check` and every other verb print plain lines |
| `Setup/SetupCheck.cs` | `proctor setup --check`: plain lines for an agent, `problem:` lines and exit 1 for what to fix, never a value; `--probe` repeats each key check |
| `Setup/SandboxPlan.cs` | `exams/sandbox-plan.md`: what setup observed (OS, uv, docker, podman and its socket, compose, registries) and what proctor needs, written as a brief for the user's agent, never a list of commands |
| `Report/Results.cs` | `results.jsonl` rows |
| `Report/Stats.cs` | Wilson, Newcombe paired, MDE, summaries into `stats.json` |
| `Report/Report.cs` | `report.html` and `summary.md`, pure functions of stats and results; `Td` is a table cell |
| `exams/smoke/` | proctor's own exam: every task scripts nb's Mock provider |
| `exams/code-change/` | the first real exam: three tasks on three fixtures, acceptance tests, reference solutions; four agent variants through Harbor (nb, Qwen Code, Claude Code, Codex) |
| `exams/gate/` | the deterministic gate: one Mock variant bare and the same variant as a Harbor agent; `GateTests` (with `PROCTOR_GATE`) asserts they agree |
| `exams/gate-oracle/` | the oracle's gate: the same pair on three oracle tasks, one per ending (`ok`, `oracle_miss`, `oracle_budget`); `OracleGateTests` (with `PROCTOR_GATE`) asserts the in-process oracle and the bridged simulated user agree, apart from the judge's tokens |
| `exams/oracle-change/` | the oracle on a real model: `add-retry-flag` with its semantics only in the answer sheet, chosen to be unconventional so a guess fails acceptance; nb on Anthropic Haiku bare and through Harbor, judged by Haiku. Real API calls (about $1 a batch) |
| `exams/conformance/` | Harbor conformance at $0: every vendor agent at its pin against `fixtures/stub`'s scripted model, tasks `ok`, `fail`, `hang`; `ConformanceTests` (with `PROCTOR_CONFORMANCE`, `1` or a `proctor.json` whose `harbor` block to run on) asserts each attempt came back as scripted. The plan and its benchmark are `project/archive/harbor-conformance.md` |
| `exams/tern-docs/` | the bundle showcase: Claude Code on Sonnet implements six shipping functions in `fixtures/tern-shop` against Tern, an invented carrier, with its docs bundle as shipped, restructured or absent, served by the fixture's docs service; hidden acceptance runs against Tern's sandbox API started in the verifier; `reference/shakedown.sh` proves the checks for free (`--unsolved`, `--v1`, `--kilograms`). The plan is `project/archive/bundle-showcase.md` |
| `exams/exam-authoring/` | the demo project: an agent writes exams, graded by running them against planted good and bad attempts (`trial/`); a batch's report is meant to be copied into `report/`, which has none yet. Its variants pin proctor at a GitHub revision as their bundle, which still documents the earlier layout until the pin moves |
| `exams/playground/` | the first exam a new user runs: two scripted Mock tasks on `fixtures/jotcli`, no model and no key; the fixture's `builds` and `tests-pass` run for real on the checkout, so the first report is about a repository |
| `exams/cli-for-agents/` | the first exam with a real model: one task on `jotcli` (`clear-for-scripts`: add `--yes`, refuse without a terminal, exit non-zero when nothing was cleared), nb bare on the local model; `acceptance/<task>.sh` runs the built CLI with no TTY under a ten-second limit; `reference/shakedown.sh` proves the checks solved and `--unsolved` without a model |
| `exams/agent-onboarding/` | a shipped coding agent, given proctor's skill, docs and CLI at a pinned revision, stages a chat-only exam for a small bot project; each written exam is graded by a trial against planted attempts (`trial/<task>/plants.json`) |
| `exams/nb-changes/` | defect tasks and a feature task on nb itself, at pinned revisions (`fixtures/nb-*`); `tests-green` runs nb's own tests; `reference/shakedown.sh` proves the checks on the host |
| `exams/runners/` | the container runner (`container.sh`) and its image (`Containerfile`) that exams' `nb.runner` names |
| `exams/readme-answers/` | the first exam on a hosted model, after `playground`: six questions about jot answered from a README alone (`system @{{bundle}}/README.md`, `tools none`), two variants that differ only in their README bundle, Gemini Flash-Lite on the free tier, every check a regex (`correct` per task in `expect`); `reference/shakedown.sh` proves the checks on Mock (`--wrong` for none) |
| `fixtures/` | the repositories tasks run against, each with its own checks beside (never inside) its `repo/`. `jotcli` is a tiny TypeScript CLI whose `clear` reads its confirmation from `/dev/tty` with no `--yes` and exits 0 when it gives up, planted on purpose (its README says why); one of two non-.NET fixtures, with `tern-shop` (Python, beside two compose services: Tern's sandbox API and a docs site serving the variant's bundle). `empty` holds nothing, for tasks whose whole context is in the prompt |
| `assets/` | the wizard hat icon, adapted from Lorc's "Pointy hat" (game-icons.net, CC BY 3.0); not MIT. Wherever it ships (a favicon in `report.html`, the site), the credit goes with it |
| `Proctor.Tests/` | xunit, flat; `fixtures/` are captured Mock transcripts, and `fixtures/nb-run/` pairs of nb's JSONL and its own trajectory from one nb run (`capture.sh` regenerates them); `snapshots/` are the approved renderings; `exams/` is test data `TestRepo.CopyExam` finds after `exams/` (`harbor-unit`, which `HarborTests` build on with a fake uv), never an exam to run; `python/` is `oracle_user.py`'s unittests, on stub `harbor` modules and captured `acpx` output, which `OracleHarborTests` runs with `python3` |
| `docs/` | user-facing guides beyond the README: `getting-started.md`, clone to a first report in three steps (setup with every job skipped, then `playground`; setup's Gemini, then `readme-answers`; your own exam with your agent); `cli.md`, every verb and flag, explore and guard mode, the report files (moved out of the README); `configuration.md`, the reference for exam, fixture, task, variant and `proctor.json` (moved out of the README; `exam-authoring`'s variant view copies it); `porting-to-agent-neutral.md`, how an exam moves to agent variants (a draft, from porting `code-change`); `podman.md`, what a machine needs for `harbor.engine: podman`, why, and a sandbox for Harbor; `modal.md`, agent variants on Modal's sandboxes from a second machine file (`--config`), what it costs and what cannot run there |
| `skills/proctor-exam/` | the onboarding skill a user copies into their agent's skills directory: plan an exam with the user (inventory, tasks, checks, variants, cost; a `PLAN.md` approved), then build it and prove its checks for free; only what a frontier model would not guess and proctor's gotchas, pointing at `docs/configuration.md` and the example exams for the rest |
| `project/` | the brief, the plans, the learnings and the loose ends (`todo.md`); `archive/` holds built and dropped plans and the old bug notes, in the earlier words |
| `project/gate/` | `old-words.sh`, the grep gate for the old words, and `old-words.allow`, where each exception is listed with a reason |

The directories are for reading, not for namespaces: everything is
`namespace Proctor`. The data directories `exams/`, `fixtures/`, `bench/`,
`attempts/` and `reports/` are lowercase and excluded from compilation in
`Proctor.csproj`.

## Conventions and gotchas

- **`failed` is infrastructure; a bad transcript is `completed`.** A hook that
  exits non-zero or an nb startup error (exit 1, no `result` trailer) marks the
  attempt failed with a reason. A transcript whose exit reason is `max_tool_calls`
  is completed; grading decides what it means.
- **Grading writes beside the evidence.** `checks.json` is the only file the
  grader adds to an attempt. `report` reads it; a completed attempt without one is
  reported as not analysed, and `report` says to run `grade`.
- **Friction is beside grading, not in it.** `proctor friction` writes
  `friction.json` in an attempt and reads only `trajectory.json` and the
  exam's `surface`; no check, verdict or report reads either. The rules
  have their own version (`Friction.RulesVersion`, in every file and
  every point), bumped whenever a rule changes what it flags. They were
  ported from a Python trial in auspex (`../auspex/project/trials/friction.py`,
  the contract in `../auspex/project/friction-contract.md`) and held to
  identical output on 702 trajectories when ported; a rule change is made in one
  place now, here. The rules call no model. `--reader <judge>` adds the
  reader stage (`FrictionReader`): one chat call per attempt through
  `JudgeClient`, so throttling and the test fakes are the judges'. It
  picks from five labels and must quote the call; a point is written
  only when the call exists, the label is in the list and the quote is
  in that call, and its reason is a note, never a count. What was sent
  and answered is `friction-reading.<judge>.<hash>.json` in the attempt,
  the cache and the record; `--rejudge` asks again. `Instructions` is
  the wording the measurements were made with, pinned by hash in
  `FrictionReaderTests`: change it knowingly, and measure again.
- **A check is one of three kinds by where it is named.** In `grading.pass`
  it is capability; in `grading.validity` it decides whether the attempt
  counts (a `fail` excludes the attempt with its reason, `error` never does);
  in neither it is a guardrail rate. A name in both lists is a problem.
- **A check is declared at one of three levels, and an attempt carries the
  union.** The exam's `grading.checks` reach every task; a fixture's
  `checks` reach every task on it; a task's own `checks` reach that task
  (`Exam.ChecksFor`, exam then fixture then task). A name at two levels is
  a problem. A `pass` or `validity` check the exam does not declare must
  reach every task from its fixture or its own block. A task's scripts
  resolve against the exam directory. A check's rate is over the attempts
  that carry it, so a task-only check reports over one task.
- **The fixture is the task's; what is under test is the variant's.** A task
  names a fixture; the fixture's checks join the exam's for that attempt (a
  clash is a problem) and its default `expect` sits under the task's. Only
  `repo/` is ever copied into the work directory, so the checker and the
  expectations are unreachable from inside it by construction. Proctor does
  the checkout (commit, then `diff.patch` after teardown) and restores
  fixture plus diff for a regrade whose checkout is gone. A variant's `bundle`
  (`{path}` under the repository root, or `{git, rev}` cloned under
  `.proctor/bundles/<rev>`) is resolved once per batch and recorded
  by source, path and hash in `batch.json` and every attempt manifest;
  proctor never reads what is inside it. `resume` refuses a changed bundle
  as it refuses a changed exam. On an agent variant the agent's container
  never holds the bundle, so an `agent: nb` variant with one cannot use
  `{{bundle}}`; it reaches the agent only through the fixture's services,
  which compile hands it as `environment/bundle/` (empty without one) for
  a compose service to mount and serve on the agent's `localhost`.
- **A variant with an agent runs through Harbor; a variant without one is nb run
  bare.** `variant.agent` is a vendor agent (`claude-code` on Anthropic models,
  `codex` on OpenAI's, `qwen-code` on Qwen's), with a pinned
  `agent_version` and a bare model of the agent's vendor, the nb fields
  refused; or `nb`, nb itself as a Harbor agent, with `harness`,
  `provider` and `model` as on the bare path and `runner`, `command` and
  `agent_version` refused. (Decision 1's rule, Harbor exactly when the
  program has tools, waits for validation to refuse tools on the bare path;
  until then `agent: nb` is how an nb variant asks for Harbor.) The plan is
  `project/plans/agent-neutral-runs.md`; what Harbor writes is
  `project/learnings/harbor.md`. `run` compiles each (agent variant, task) once
  under `attempts/<batch>/harbor/tasks/<variant>/<task>/` (so `harbor` is not a variant
  id): the prompt as `instruction.md`, the fixture's `repo/` (and only it)
  committed in the image `fixture.image` describes, the variant's agent at its
  pin, and `tests/` with the exam, the fixture minus `repo/`, and a
  `test.sh` running every script check in Harbor's separate, offline
  verifier (one `reward.json` key per check). A vendor agent reaches the
  host of `proctor.json` `agents.<agent>.base_url`, exactly as spelled, its
  key in Harbor's process as `ANTHROPIC_API_KEY` or `OPENAI_API_KEY`; nb
  reaches the host of its provider's `Endpoint` in nb's config, or nothing
  (Mock), its endpoint `${VAR}`s passed by value and its key by name. Each
  attempt is one Harbor job in `<attempt>/harbor/`; `Ingest` lifts the trial into
  the attempt and writes `core.json`, proctor's own count, strictly: an
  unknown tool or event, a version other than the pin, or no trajectory
  fails the attempt. An agent timeout is a completed attempt with `time_budget`;
  a vendor agent that took no turn at all is failed, since its model never
  answered (Codex retries an unreachable endpoint until the timeout).
  `grade` reads an agent attempt like any other: `Transcript.Read` takes
  `trajectory.json` when there is no `transcript.jsonl`, with `core.json`
  as the trailer (so a budget check reads proctor's count, never the
  agent's) and the main agent's conversation as the windows (a subagent's
  calls are counted, not listed). A script check is not rerun on the host:
  its verdict is the attempt's `reward.json` key and its reason the first line
  of `verifier/<check>.out`, a missing key `error`. `loop_nudged` is
  `error` on a vendor agent's attempt, since only nb nudges; a script check
  that reads `PROCTOR_TRANSCRIPT` has none in the verifier.
- **nb in Harbor is the host's nb.** `proctor.json` `harbor.nb` is nb's
  self-contained linux-x64 publish; `run` builds it once into a
  `proctor-nb:<content hash>` image that each nb task's Dockerfile copies
  `/opt/nb` from, and refuses a publish whose `nb --version` is not the
  host nb's, since the host compiles the program (per attempt, `{{work}}` as
  `/app`) and the image runs it. The agent (`Harbor/nb_agent.py`) is on
  Harbor's `PYTHONPATH` from `attempts/<batch>/harbor/agents/`. nb writes its own
  ATIF to `agent/trajectory.json` (`--trajectory`), beside its JSONL
  (`agent/nb.jsonl`, with `nb.stderr.log` and `nb.exit`); ingest takes it
  as a vendor agent's. Bare attempts get one too (`NB_TRAJECTORY`, so a runner
  script passes it on), though grading reads their JSONL. `NbTrajectoryTests`
  holds every `fixtures/nb-run` pair to reading the same both ways. The gate
  (`exams/gate`, `GateTests`, decision 12) is opt-in:
  `PROCTOR_GATE=<publish dir> dotnet test --no-build --filter GateTests`,
  about ten minutes, Docker (or podman, with `PROCTOR_GATE_ENGINE=podman`)
  and uv needed. The filter takes `OracleGateTests` too (`exams/gate-oracle`,
  about four minutes). The publish must have `--acp`, so republish after an nb
  change the bridge sees.
- **Harbor's engine is the machine's.** `proctor.json` `harbor.engine`
  (`docker`, the default, or `podman`) is Harbor's `environment.type` in
  every attempt's job file and the CLI nb's layer is built with; podman's
  local tag is `localhost/proctor-nb:<hash>`, spelled out so the compiled
  `COPY --from` resolves no short name. `batch.json` records it and
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
- **Modal is an engine off the machine, picked by a second machine file.**
  `harbor.engine: modal` (`HarborConfig.Remote`) runs each attempt as Modal
  sandboxes on the user's login (`~/.modal.toml` or `MODAL_TOKEN_*`,
  inherited by Harbor's process; `HarborRun.Preflight` checks it), Harbor
  with its `modal` extra (`harbor[modal]@<pin>`). `--config <file>` on
  `list`, `run`, `resume` and `grade` reads that file in place of
  `exams/proctor.json` (relative paths still against `exams/`;
  `ProctorConfig.Source` is what messages call it), so an exam shaken down
  locally is promoted by naming another file. On a remote engine nb's layer
  is not built: compile copies the publish into each nb task's context
  (`environment/proctor-nb/`, `COPY proctor-nb /opt/nb`), the tag kept as
  the publish's identity. Each job gives Modal's sandboxes the attempt's
  lifetime and a ten-minute idle limit (`HarborCompile.ModalIdle`), since
  Harbor's default is a day with none, so a killed trial costs minutes and
  `TearDown` has nothing to remove. `run` refuses, before anything is
  made, an agent variant whose model host a sandbox cannot reach
  (`HarborRun.Unreachable`: loopback, private, dotless or `.local`-style
  names) and a fixture with `image.services`: Harbor's Modal environment
  runs a compose task as docker-in-docker with no allowlist and refuses it.
  So conformance and the oracle gate stay local; the gate runs on Modal
  (`PROCTOR_GATE_ENGINE=modal`). The plan and its three spikes are
  `project/archive/modal-runs.md`; the user's guide is `docs/modal.md`.
- **A check of the layer costs nothing.** Proctor checks that it drives
  Harbor and reads back what Harbor wrote, never Harbor itself and never a
  model: the unit tests, the gate (nb on Mock) and conformance (each vendor
  agent on the stub) cover it, with no hosted model, judge or gateway. A
  hosted model runs only in an exam whose question is that model,
  its spend agreed first. `PROCTOR_CONFORMANCE=1 dotnet test --no-build
  --filter ConformanceTests` runs the stub attempts and the oracle pass (each
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
  Codex `config.toml` (relative to `exams/`) that Harbor merges its base
  URL over: what an endpoint needs of the agent, such as a llama-server,
  which takes only function tools (`web_search = "disabled"`,
  `[features] multi_agent = false`).
- **The baseline is a virtual variant in the statistics, not in the batch.**
  `report` resolves `baseline.json` to per-task scores (re-read from the
  pinned attempts' `checks.json` under the current `pass` and `validity`
  lists when the batch is still under `attempts/`, else the pinned score)
  and compares every variant to them with the same Newcombe pairing. The verdict
  is `held`, `improved` or `regressed` on the point estimate against
  `--tolerance`; `--fail-on regression` turns it into an exit code. The
  baseline is not in the accounting or the matrix. `report --against <batch>`
  replaces it for one report with per-variant scores from another batch
  (`GuardInput.PerVariant`), variants paired by id, built as pins by
  `Baseline.PinOf` and resolved by the same `Resolve`; the page then says
  "earlier run" where it said "baseline" (the page's own words for the other batch, which go with `--against`). A pinned attempt graded without a
  current `pass` check is not rescored: its task stands as pinned and the
  check is a `baseline.gaps` warning.
- **Every id in the report carries a sentence.** `description` is the one
  key in a check spec that is not a check (`Checks.Description`); a script
  check must declare one, a built-in derives one from its fields
  (`Checks.Describe`). Exam, variant and task descriptions are optional; a task
  falls back to the first prompt line (`Exam.Describe`). They travel in
  `stats.json` as `descriptions`, with a per-variant `failures` list (each
  non-validity check that failed in a counted attempt, most often first, with
  its tasks, out of the attempts it decided) and an `errored` list beside it
  (each that errored, out of the attempts that carry it), which the Failures
  section renders apart: "could not grade", never "failed".
  `descriptions.checks` holds a check only when it says one thing
  everywhere (`Exam.CheckDescriptions`); a check each task declares in its
  own words is absent there, and its sentence is per task in
  `task_details.<task>.checks` (name, level, description). The Tasks table
  shows a task's fixture- and task-level checks with those sentences, the
  Checks table says "per task; see Tasks", and Failures puts the task's
  sentence beside the task. "Shared" in the report means declared by the
  exam, never "carried by every task".
- **The report is one list of blocks rendered twice** (`Report.Blocks`, then
  `RenderHtml` and `RenderMarkdown`), so the two renderings cannot drift in
  wording; a table cell (`Td`) may carry HTML for a link or hover. Inline forms in block
  text: `` `id` `` (body font, no wrap), ``` ``code`` ``` (monospace: only
  commands and paths; `Report.Paths` finds paths in free-text reasons),
  `**bold**`, `*italic*`, `[text](#anchor)` (a link on the page, its text in
  `summary.md`). A `Para` with class `explain` is definitional text, muted;
  findings are never muted. Colour is semantic only: green passed/better,
  red failed/worse, amber not counted or a warning, purple a link.
- **The verdict card is direction plus confidence, both from `stats.json`.**
  Better/Worse/Unchanged is the baseline `verdict` (point estimate against
  `--tolerance`); "(unconfirmed)" is `confirmed: false`. Without a baseline
  the card compares each variant with the reference at no tolerance, confirmed
  when the Newcombe interval excludes zero. The card never carries model
  output. The structure and the
  reader-facing vocabulary (variant, task, attempt, check; counted, decided;
  "95% interval") are `project/plans/report-structure.md`. The page and the
  files say attempt alike.
- **An undecided attempt is a third headline outcome.** `Grade.Pass` is `bool?`:
  false when a pass check failed, errored or is missing; null when nothing
  failed but a check is `needs-judge`. An undecided attempt is counted (it is
  valid) but not decided: out of the pass rate on both sides, out of that
  check's rate, listed under Failures when there are at most five, and a
  warning band when a check is undecided in more than five attempts or 5% of
  the attempts it applies to (`stats.json` `undecided_checks`). A check that
  cannot decide is the check's weakness, not the variant's. So is one that
  errored: an `error` is out of that check's rate too, counted beside it
  ("· 5 error"), though a headline check that errored still fails the attempt. A
  check that errored in more than one attempt, and in more than 5 attempts or 5% of
  those it applies to, is one warning at the top with its count per variant
  (`stats.json` `errored_checks`) and leaves the per-variant lists under Failures.
- **Every number is computed once, in `Stats.cs`.** The renderers format; they
  never compute. If a number looks wrong, fix it in `stats.json` first.
- **Each task is scored as its mean over its counted, decided attempts**, so `n` in
  every interval is the task count (`stats.json` keeps the attempt counts as
  `k_attempts` and `n_attempts`; the funnel is planned, `started`,
  completed, graded, counted, decided). Per-variant rates: Wilson. Paired differences:
  Newcombe method 10 with his continuity-corrected phi from the 2x2 task
  table, built from the task means when there are several attempts. This
  reduces to the textbook binary methods at one attempt and never gives a
  zero-width interval.
- **Version and changelog.** `<Version>` in `Proctor.csproj` is what
  `--version` prints and `versions.proctor` records. Before 1.0 a minor bump
  is a breaking change to the layout, a file format or the vocabulary, and
  `CHANGELOG.md` says what broke and what still reads the old form; bump
  both in the same change.
- **`versions.nb` is `nb --version` from the host binary**, the `+commit`
  suffix stripped; `unknown` when it fails. It is read once per batch.
- **nb's trailer carries `duration_ms` since nb 0.9** (nb f7df67f); the
  `max_duration_ms` check reads it and falls back to the attempt's wall time
  from the manifest, hooks included, which is what the report shows. It
  carries no cost, so the report has no cost column rather than an estimate.
- **nb emits no `assistant_json` event**; the `answer_json` window is the last
  ` ```json ` fence in the last assistant message.
- **Program templates.** Placeholders are `{{prompt}}`, `{{task}}`, `{{work}}`
  (the fixture checkout), `{{bundle}}` (the variant's resolved bundle directory,
  empty without one), `{{provider}}`, `{{model}}`, `{{harness}}`, `{{variant}}`,
  `{{attempt}}`. `{{work}}` and `{{bundle}}` are the paths the *model* will
  see: the exam's `nb.mounts` replaces them when a runner is in effect (`AttemptContext`
  `WorkMount`/`BundleMount`), and a bare run ignores the mounts so
  `--runner none` stays a host shakedown. A prompt's newlines become nb continuation lines (` \`), so a
  multi-line prompt stays one directive. A prompt line that itself ends in a
  backslash cannot be expressed.
- **The judge is proctor's own client, never nb.** `decide` posts to a
  `systemone` endpoint (the System One request, its `model` sent when the
  judge names one; Jev on OpenRouter's `/alpha/decisions` through a local
  gateway today; the wire format is the seam, not the vendor). Its answer
  is read unwrapped, `{model, answers, usage}`, as
  `Proctor.Tests/fixtures/openrouter-decisions.json` captured it, and its
  POST is hand-rolled on purpose: Microsoft.Extensions.AI has no decision
  abstraction and no .NET System One client is worth the dependency.
  `judge` goes through
  Microsoft.Extensions.AI's `IChatClient` over an OpenAI-dialect endpoint,
  so a provider change is a config line. Both are `Verdict`s like any
  check. The question is the exam's (the check spec), the endpoint is the
  machine's (`proctor.json` `judges`), and the key and endpoint are
  resolved at grade time only (`${VAR}` in both), so `list` and `run`
  need no key. Verdict files and the report keep the endpoint as written,
  so neither names the machine that graded. `Exam.Load` resolves each
  model check's judge when handed the config's judges; the test helper
  passes none, and `grade` resolves again itself.
- **A model check sees a window, never the transcript.** `Window.Known`
  is the whole vocabulary; an empty part or a total over the judge's
  `max_window` is `error` before any call, because a decider answers an
  empty or cut state confidently. Variant id, model name and attempt number are
  in no window and no prompt.
- **`verdicts/<check>.<judge>.<hash>.json` is the cache and the record.**
  The hash covers the compiled request, the model, the threshold and the
  expectation; `grade` reuses a file that exists, unless its verdict is
  `error` (an endpoint that did not answer is retried), and `--rejudge`
  reuses nothing.
  The file keeps the request (key redacted), every response, each vote
  as read with why it was discarded, the reasoning, usage and the verdict.
  Deterministic checks are always recomputed. `--judge a=b` is a
  comparison pass: only the checks whose declared judge is `a` are
  evaluated, by `b`, into `b`'s files marked `applied: false`, and the log
  prints both verdicts per attempt; `checks.json` is never written. The
  report's judge rows count applied files only, so it names the judge
  whose verdicts the attempts hold. Changing `with` in the exam and grading
  plain is how `b` becomes the applied judge, and a plain grade that reuses
  `b`'s file marks it applied.
- **A throttled judge is stopped for the pass, not retried per vote.**
  Every judge request goes through `JudgeClient.Send`: a refusal that
  `Throttle.Of` recognises (429, 503 or 529, or a rate-limit phrase in the
  body, as nb's `RateLimitRetry` reads one) is retried three times at 2, 4
  and 8 s or its `Retry-After`; a `Retry-After` over a minute, or a fourth
  refusal, stops that judge, and every later check on it is `error` with
  the endpoint's own words and no request. Nothing is lost, since the next
  `grade` asks an `error` again. The OpenAI SDK's retries are off
  (`ClientRetryPolicy(0)`), or each request is four underneath.
- **A judge's evidence is verified, its reasoning discarded.** Every quote
  must be a substring of the window with all whitespace removed from both,
  against the window as written or with its JSON string escapes undone
  (a judge quotes JSON compacted and unescaped); a vote with
  an unverifiable quote, no quote on a yes/no, or no JSON block is
  discarded but stays in the file. Fewer than two usable votes (one when
  `votes` is 1) is `error`; unanimous decides; a split or an `unknown`
  is `needs-judge`. The reason string is `yes 3/3 — "first quote"` (on a
  fail, `no 3/3, expected yes — "…"`; a `decide` likewise says
  `, expected <answer>` whenever its label is not it), so the
  matrix tooltip shows evidence without opening the attempt.
- **Calibration is the bench's, not proctor's.** A threshold or a rubric
  earns a place in `grading.pass` by the numbers in `project/plans/jev-trial.md`;
  proctor records which judge, model and prompt hash graded an attempt (the
  report's reproducibility section) and carries no kappa.
- **Check values from the task.** A built-in field whose value is
  `"@expect"` reads `expect.<check name>.<field>` from the task, then
  `expect.<field>`; a task with neither yields `error`, never "does not
  apply", since a task that wants to opt out declares its own checks. A
  model check's `expect: "@expect"` reads `expect.<check name>`, which is
  the expectation itself or `{ask, expect}`; `ask: "@expect"` reads the
  `ask` from there, so a shared `correct` check carries a per-task rubric,
  and the verdict hash makes it a per-task verdict file.
- **Hooks and script checks** run with `PROCTOR_EXAM_DIR`, `PROCTOR_FIXTURE`,
  `PROCTOR_BUNDLE`, `PROCTOR_BATCH`, `PROCTOR_VARIANT`, `PROCTOR_TASK`, `PROCTOR_ATTEMPT`,
  `PROCTOR_ATTEMPT_DIR`, `PROCTOR_WORK`, `PROCTOR_TASK_JSON`, `PROCTOR_EXPECT` (the
  merged block), `PROCTOR_TRANSCRIPT`, `PROCTOR_DIFF`, `PROCTOR_RUNNER` (the
  runner script in effect, empty on a bare run), `PROCTOR_NB` and
  `PROCTOR_NB_CONFIG` (the host binary and its resolved config, which a
  hook mounts for the runner), `PROCTOR_CONTAINER` (a name derived from the
  attempt that proctor never uses), and `PROCTOR_WORK_MOUNT` and
  `PROCTOR_BUNDLE_MOUNT` (the paths the model was told; the host paths
  unless `nb.mounts` moved them). Hooks run in the exam
  directory; script checks run in the attempt, an exam's resolved against the
  exam directory and a fixture's against the fixture directory. Scripts exit
  0/1/2 for pass/fail/needs-judge; anything else is `error`, never folded
  into fail. The attempt order is: fixture checkout, setup hook, nb, teardown
  hook, diff. Teardown runs whenever setup ran, even when setup failed, so
  it must be idempotent. `PROCTOR_WORK` is never deleted (`.proctor/` is
  gitignored).
- **nb gets the program on stdin, compiled on the host, bare or through a
  runner.** `RunAttempt` writes the resolved source as `program.nb`, then runs
  `nb --compile` on the host binary in the exam directory (so `@file`
  includes resolve against the exam) and writes the JSONL as
  `program.jsonl`; that is what goes down stdin, so the container never
  holds a path to a sheet. `program_hash` stays the source's. A program nb
  refuses fails the attempt before the checkout or any hook. `RunNb` is one
  code path: without a runner it starts `nb --output jsonl [--config] -`
  itself with only `NO_COLOR` added to the environment; with one it starts
  the script with nothing on argv and the attempt environment plus `NO_COLOR`,
  and the script starts nb the same way. The bare path deliberately does not get the attempt environment:
  `PROCTOR_EXPECT` in the model's reach would leak the answer. A runner that
  forwards its environment into the container wholesale would do the same.
- **The machine's config is never committed.** `exams/proctor.json` and
  `exams/nb.json` are gitignored; `*.example.json` beside them are the
  committed, host-neutral forms (`${LLM_GATEWAY}`, `${LLM_GATEWAY_KEY}`), and
  `run` says to copy them when the machine's own file is missing. The tests read
  `nb.example.json` for its Mock providers, so keep those in it.
- **The runner is the exam's, the binary is the machine's.** `exam.json`'s
  `nb` block (`runner`, relative to the exam directory like a hook, and
  `mounts`) says how the exam runs nb, because the runner only works with
  the hooks that make its container; `exams/proctor.json` says only where
  the host binary and its config are. `--runner` overrides per batch. A
  `runner` or `mounts` left in `proctor.json` is reported as a problem, not
  ignored.
- **Labels are the consumer's; proctor has no tags.** `labels` on an exam,
  a fixture or a task is a key with one or more string values that proctor
  stores, prints, filters on (`list --label k[=glob]`) and records on every
  `results.jsonl` row, never interprets. `Exam.LabelsFor` merges fixture,
  then exam, then task, key by key. A `tags` field is reported as removed.
  Whether an exam is judged is `Exam.Judged`, derived from a check naming a
  `judge` (not yet a known check), not declared.
- **`resume` refuses a changed exam.** The exam hash in `batch.json` must
  match; a changed exam is a new batch.
- **Validation says "not yet" rather than silently skipping**: `command` variants,
  run- and task-level hooks, `answer_json_schema`, `max_cost`. A task's
  `oracle` runs on nb variants only: bare, as nb's in-process oracle (its
  directives prepended by `Runner.OraclePrefix`); on Harbor, as
  `Harbor/oracle_user.py`, the simulated user, whose record ingest lines up
  with nb's trajectory (`Ingest.Oracle`). A vendor variant beside an oracle task
  is refused: Harbor leaves it no ATIF.
- **Snapshot tests** fail on any rendering change and leave a `.received` file
  (gitignored) beside the approved one. Review it, then approve with
  `PROCTOR_APPROVE=1`. Render to check: `google-chrome --headless=new
  --screenshot=out.png --window-size=1100,2400 file://.../report.html`.
- **Setup writes no model and no judge.** A model is the exam's choice, so
  nb.json entries carry none and a judge or classifier job records only its
  provider; the agent writes the `judges` entry from the models
  `setup --check` lists. Keys go to the env file at a secret prompt and
  never through an agent; Spectre is for interactive verbs only.
- **The old words are gated.** `project/gate/old-words.sh` searches every
  tracked file for the earlier model's words, listed at its top (any case,
  plurals, inside identifiers too) and exits non-zero on a hit
  that `project/gate/old-words.allow` does not list. An exception is an
  edit to the allow-list with a reason; prefer rewording.
- **The runner tests are slow** (nb takes ~2 s to start per attempt); filter them
  out while iterating on anything else: `--filter 'FullyQualifiedName!~RunnerTests'`.
