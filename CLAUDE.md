# proctor — developer notes

Runner, grader and reporter of evals around nb (`../nb`). The intent is in
`project/brief.md`; the layout every verb reads and writes is
`project/plans/on-disk-layout.md`; the data model proctor is moving to is
`project/plans/exam-variant-attempt.md`. Built and dropped plans are in
`project/archive/`, written in the earlier words.

## Build and test

```bash
dotnet build
dotnet test --no-build                 # both suites, proctor's and the viewer's (View/Auspex.Tests); the runner tests spawn nb per attempt
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
| **exam** | The question being answered, the tasks, and the checks that grade them. The variants that take it are files beside it (`variants/`), outside its identity (hash). |
| **task** | One prompt against a fixture, with its own answer key for the checks that apply to it, and any checks of its own. |
| **check** | One yes-or-no grading question asked of a finished attempt, declared on the exam, a fixture or a task (an attempt carries the union). Where it is listed sets its role: `correctness`, `integrity`, `quality`, or in no list an observation. |
| **fixture** | The repository a task runs against and what "done" looks like in it; reused across exams. |
| **variant** | What is under test: an agent, a model and a bundle, as declared data. Its identity is its content. |
| **attempt** | One variant at one task, once, with the evidence. Several per task, because the agent is stochastic. |
| **bank** | Every attempt of an exam, whenever it was made (`bank/<exam>/<variant>/<task>/<n>/`). What one `proctor run` did, when, on what machine and at what versions, is a **log entry** (`log/<id>/`): a record, and what `run` reads to continue after a crash, never a grouping a person reads results by. |
| **live** | A mark on one variant: the one in use now, and the reference a change is compared with. |
| **prediction** | One statement written before the tries are made: run this configuration, and this will change. Its lines are **changes**, each a check or a metric and a direction. It replaced "claim" (the user, 2026-11-25); "candidate" and "treatment" are not words here. |

**Built: the words and steps 1 to 4** of
`project/plans/exam-variant-attempt-removal.md`: everything says exam,
variant and attempt, each variant is its own file
(`exams/<exam>/variants/<id>.json`) with its own hash, each task has a
hash, `attempts` is set on the exam, an exam's attempts are its bank
(`run` makes what is missing and continues an
interrupted run; `grade`, `friction` and `report` take the exam; there
is no resume verb), and one variant may be marked live
(`exams/<exam>/live.json`, `proctor live`), which the report compares the
others with. **Predictions are built on exams with axes only**
(`axes-build.md`, slice 6): `exams/<exam>/predictions/<name>.json`,
its date its author's (`written`, required), its standard pinned by
`proctor predict` or by `run`, held against the tries in
`list`, the report and `compare`. An exam of variants has none, and
the change report of stage 3 is not built. **The bank**
(`project/plans/bank.md`, steps 1 and 2 built 2026-11-17 and 18):
there is no batch. Attempts live in the bank by coordinates and a run
leaves a log entry; `run` opens with each variant's attempts of those wanted and `grade`
ends with each check's graded-of. Steps 3 to 5 of that plan are not built:
back-fill, an exam as one job in stages (tasks are still tasks), the
freeze. **Still in the old shape:** the report (`summary.md`) is built as the variants side
by side (`Report/Report.cs`).
Write nothing new in the earlier words; the grep gate below lists them and
says where they may stay.

## Structure

One project, one executable, no library split. The root holds the entry point
and the project files; the source sits in one directory per pipeline stage,
in the order the verbs run them. Every file is one concern:

| file | holds |
|---|---|
| `Program.cs` | flag parsing and verb dispatch; `ProctorException` is a user-facing failure |
| `Verbs.cs` | one method per verb: `list`, `run`, `build`, `grade`, `friction`, `report`, `compare`, `coverage`, `predict`, `live`; the conditions the report stands for (`View`) and the earlier report moved aside |
| `Exam/Layout.cs` | paths and file names, nothing else |
| `Exam/Exam.cs` | `proctor.json` (the host binary and its config), `exam.json` (attempts, the `nb` block with the runner and mounts, hooks, grading), `variants/*.json`, tasks, the program template: records, loading, validation (`Problem` = file, field, message); the merged check set and expect block per task; the exam's, each variant's and each task's hash |
| `Exam/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Exam/EnvFile.cs` | the user's env file (`~/.config/proctor/env`, `%LOCALAPPDATA%\proctor\env` on Windows, or `PROCTOR_ENV_FILE`): `NAME=value` lines `Program.Main` loads into the process environment before any verb, the real environment winning, so every `${VAR}` resolver and child process sees them |
| `Exam/Judges.cs` | `proctor.json`'s `judges` block: an endpoint per name by wire shape (`systemone`, `chat`), key resolution, which judge a check gets |
| `Exam/Tree.cs` | a directory's files minus excluded names, and their content hash (fixtures and bundles); `Tree.Litter`, the tool caches (`__pycache__`, `.pytest_cache`, …) no walk, hash or compile copy ever takes, since git ignores them and they differ by machine |
| `Exam/Axes.cs` | an exam's `axes` in `exam.json`, in place of `variants/` (`project/plans/axes.md`, built slice by slice per `axes-build.md`): axes, values, the reference of each axis, the configurations the exam carries (named, asked for, or with tries in the bank; never the cross product), the refusals, who does the work in a configuration |
| `Exam/Combinations.cs` | `exams/<exam>/combinations.json`, written by `build` and nothing else: which several-valued configurations are their values put together, more (a script reads several axes), or cannot be told; what holds today (`Combinations.Stand`). Whether anyone read what a value changes is recorded nowhere, on purpose |
| `Exam/Predictions.cs` | `exams/<exam>/predictions/<name>.json` on an exam with axes: one configuration the exam names, what it is compared with (`against`, or what is live), and the changes expected, each a check or a metric (`held`, `flagged_steps`, `tool_calls`, `tokens`, `duration`) with `more` or `less`; validation; when it was made (`written`, its author's, required: a date or a time in UTC); the pin (`Predictions.Stamp`: each stage's standard when proctor first saw it, set once). Committed with the exam and in no hash |
| `Exam/Live.cs` | `exams/<exam>/live.json`: the live variant by id and hash, and since when; whether the mark still holds (`Live.Broken`) and the warning `list`, `run` and `report` print when it does not |
| `Run/Runner.cs` | `run`: an interrupted run continued when its conditions hold (`WhyNotContinue`), then what is missing made by a new run; the log entry (`log/<id>/entry.json`), `exams-seen/` and attempt manifests, hooks, nb as a subprocess, an agent attempt through Harbor; bundles resolved (a git bundle's `paths` copied out of its clone); a changed variant under an id that made attempts refused |
| `Run/Build.cs` | `build`: the starting workspace of the reference twice, of each value alone and of each several-valued configuration, with the setup hooks and nothing run; a value's change (`ValueChange`) and the combination check, whose changes to one file are merged line by line |
| `Run/Bank.cs` | an exam's bank: current or stale by the variant and task hashes each attempt was made under (`Bank.Stale`), what is missing (`Bank.Missing`), which runs were interrupted (`Bank.Interrupted`), the selection `--variant` and `--tasks` narrow (`Bank.Select`) |
| `Harbor/Compile.cs` | an agent variant's task as a Harbor task (`instruction.md`, `task.toml`, the image, the verifier) and an attempt's job file |
| `Harbor/nb_agent.py` | nb as a Harbor agent: uploads the attempt's program and nb's config, runs nb on stdin; embedded, compiled into `log/<id>/harbor/agents/` (Python, not compiled into proctor) |
| `Harbor/oracle_user.py` | the oracle as Harbor's simulated user, on the host: sends the task over the ACP bridge (`acpx`), judges each finished turn with the host's `nb --judge`, answers from the sheet, writes `oracle-user.json`; embedded and compiled like `nb_agent.py` with its template (`oracle_user.j2`) when an agent variant meets an oracle task |
| `Harbor/qwen_code_agent.py` | Harbor's Qwen Code agent with its per-trial network install replaced by a check that the image holds the pin; embedded and compiled like `nb_agent.py` |
| `Run/Harbor.cs` | one attempt's Harbor job: `uv tool run harbor@<pin>` with a seven-day `--exclude-newer` cooldown on everything it resolves, telemetry off, the key in its environment only; nb's layer image from its publish |
| `Run/Relay.cs` | the relay's host end (`HostRelay`): for each port of this machine a run's agents reach, a unix socket in the user's runtime directory passed to `127.0.0.1:<port>`; what the log entry records of it (`ResolvedRelay`) |
| `Harbor/relay.py` | the relay's container end, compiled into a task as the service `proctor-relay`: serves each port at the agent's `localhost` and passes every connection to the mounted socket; embedded like `nb_agent.py` |
| `Run/Ingest.cs` | a Harbor trial into an attempt, and the counted core (`core.json`) from the ATIF trajectory |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, Harbor, script checks, git; an optional timeout |
| `Grade/Transcript.cs` | nb JSONL, or an agent attempt's ATIF trajectory with its `core.json`, into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
| `Grade/Window.cs` | what a model check sees: named extractions from the transcript, joined with `+`, compiled into labelled fences |
| `Grade/Judge.cs` | `JudgeClient` (the transports), the `decide` check over systemone, the `judge` check over chat, the verdict file as cache, the report's judge provenance |
| `Grade/Standard.cs` | grades over time (`axes-build.md`, slice 5): the standard a stage is graded under (its checks, their conditions and scripts, the pass and validity lists, the expected values), each grade kept under it in the attempt's `grades/`, whether a check applies in a configuration (`when`), which tries are not yet graded, and what moved between an earlier grade and this one (`Standard.Moves`: narrowed, added, removed) |
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
| `Report/Results.cs` | `results.jsonl` rows, one per current attempt read |
| `Report/Stats.cs` | Wilson, Newcombe paired, MDE, summaries into `stats.json`; the report's stamp (`built_from`: the runs' log entries, hashes, current and stale counts, mixed conditions) |
| `Report/Configurations.cs` | the report of an exam with axes (`stats.json` as `configurations/1`, `summary.md`): the configurations the exam carries, and `Compare`, the one place a value is compared with another, every other axis held (Mann–Whitney in each setting, van Elteren across them, "better here, worse there" when they disagree) |
| `Report/PredictionOutcomes.cs` | how each change a prediction expects came out against the tries, as a sentence, a miss as plainly as a hit; `post hoc` when tries were made before it was written, and whether the standard moved since |
| `Report/Answers.cs` | `compare` and `coverage`: what proctor answers an agent, as JSON (`answer/1`), from `Configurations.Compare` and the bank; an answer's cautions, its tries' addresses and its `as_of`. Proctor does no decision support (`project/plans/axes.md`, decision 17): these recommend nothing |
| `Report/Report.cs` | `summary.md`, a pure function of stats and results; `Td` is a table cell. There is no HTML page: auspex is the viewer (2026-11-22) |
| `exams/smoke/` | proctor's own exam: every task scripts nb's Mock provider |
| `exams/code-change/` | the first real exam: three tasks on three fixtures, acceptance tests, reference solutions; five agent variants through Harbor (nb, Qwen Code, Claude Code on Haiku and on Sonnet, Codex) |
| `exams/gate/` | the deterministic gate: one Mock variant bare and the same variant as a Harbor agent; `GateTests` (with `PROCTOR_GATE`) asserts they agree |
| `exams/gate-oracle/` | the oracle's gate, on stages and axes since 2026-11-25: three stages, one per ending (`ok`, `oracle_miss`, `oracle_budget`), each applying on one value of the `ending` axis, so a try is one stage with an answer sheet; the `route` axis runs it bare (nb's in-process oracle) and through Harbor (the simulated user, as a plain task). `OracleGateTests` (with `PROCTOR_GATE`) asserts the two routes agree, apart from the judge's tokens: the same counted core, the same checks, and the same user turns by text, source and sheet keys |
| `exams/oracle-change/` | the oracle on a real model: `add-retry-flag` with its semantics only in the answer sheet, chosen to be unconventional so a guess fails acceptance; nb on Anthropic Haiku bare and through Harbor, judged by Haiku. Real API calls (about $1 for a full set of attempts) |
| `exams/conformance/` | Harbor conformance at $0: every vendor agent at its pin against `fixtures/stub`'s scripted model, tasks `ok`, `fail`, `hang`; `ConformanceTests` (with `PROCTOR_CONFORMANCE`, `1` or a `proctor.json` whose `harbor` block to run on) asserts each attempt came back as scripted. The plan and its benchmark are `project/archive/harbor-conformance.md` |
| `exams/tern-docs/` | the docs showcase, and the first exam in the repository on axes (ported 2026-11-25): Claude Code on Sonnet implements six shipping functions in `fixtures/tern-shop` against Tern, an invented carrier. Its `docs` axis sets the fixture's `DOCS` tree parameter (as shipped, restructured, or none), which the fixture's docs service serves; `assistant` is the agent and model. Hidden acceptance runs against Tern's sandbox API started in the verifier; `reference/shakedown.sh` proves the checks for free (`--unsolved`, `--v1`, `--kilograms`). The plan is `project/archive/bundle-showcase.md` |
| `exams/exam-authoring/` | the demo project: an agent writes exams, graded by running them against planted good and bad attempts (`trial/`), and finishes a run a crash interrupted (the task `continue-the-batch`, made by `hooks/interrupt.sh`, both written for the layout before the bank and not yet moved to it; D15 of `exam-variant-attempt.md`); its report is meant to be copied into `report/`, which has none yet. Its variants pin proctor at a GitHub revision as their bundle, each naming the `paths` it shows (the docs; the docs and the examples) |
| `exams/playground/` | the first exam a new user runs: two scripted Mock tasks on `fixtures/jotcli`, no model and no key; the fixture's `builds` and `tests-pass` run for real on the checkout, so the first report is about a repository |
| `exams/cli-for-agents/` | the first exam with a real model: one task on `jotcli` (`clear-for-scripts`: add `--yes`, refuse without a terminal, exit non-zero when nothing was cleared), nb bare on the local model; `acceptance/<task>.sh` runs the built CLI with no TTY under a ten-second limit; `reference/shakedown.sh` proves the checks solved and `--unsolved` without a model |
| `exams/agent-onboarding/` | a shipped coding agent, given proctor's skill, docs and CLI at a pinned revision, stages a chat-only exam for a small bot project; each written exam is graded by a trial against planted attempts (`trial/<task>/plants.json`) |
| `exams/nb-changes/` | defect tasks and a feature task on nb itself, at pinned revisions (`fixtures/nb-*`); `tests-green` runs nb's own tests; `reference/shakedown.sh` proves the checks on the host |
| `exams/runners/` | the container runner (`container.sh`) and its image (`Containerfile`) that exams' `nb.runner` names |
| `exams/readme-answers/` | the first exam on a hosted model, after `playground`: six questions about jot answered from a README alone (`system @{{bundle}}/README.md`, `tools none`), two variants that differ only in their README bundle, Gemini Flash-Lite on the free tier, every check a regex (`correct` per task in `expect`); `reference/shakedown.sh` proves the checks on Mock (`--wrong` for none) |
| `fixtures/` | the repositories tasks run against, each with its own checks beside (never inside) its `repo/`. `jotcli` is a tiny TypeScript CLI whose `clear` reads its confirmation from `/dev/tty` with no `--yes` and exits 0 when it gives up, planted on purpose (its README says why); one of two non-.NET fixtures, with `tern-shop` (Python, beside two compose services: Tern's sandbox API and a docs site serving the variant's bundle). `empty` holds nothing, for tasks whose whole context is in the prompt. `toolchain` is a scripted control whose image takes a declared parameter as a build argument and writes it to `/etc/toolchain`, standing in for a real toolchain axis |
| `assets/` | the wizard hat icon, adapted from Lorc's "Pointy hat" (game-icons.net, CC BY 3.0); not MIT. Wherever it ships (the site), the credit goes with it |
| `Proctor.Tests/` | xunit, flat; `fixtures/` are captured Mock transcripts, and `fixtures/nb-run/` pairs of nb's JSONL and its own trajectory from one nb run (`capture.sh` regenerates them); `snapshots/` are the approved renderings; `exams/` is test data `TestRepo.CopyExam` finds after `exams/` (`harbor-unit`, which `HarborTests` build on with a fake uv), never an exam to run; `exams/axes-harbor` is the scripted control for axes through Harbor (nb on Mock in `fixtures/toolchain`'s image, a toolchain as a build argument, a README laid into the repository, a stage with a condition), compiled by `AxesHarborTests` and run in Docker by hand; `exams/axes-answers` is a scripted exam with known answers for `compare` and `coverage` (its checks pass at a chance set by the configuration; `make.sh <dir>` makes its bank in a scratch root, no model), which `AnswersTests` runs part of; `python/` is `oracle_user.py`'s unittests, on stub `harbor` modules and captured `acpx` output, which `OracleHarborTests` runs with `python3` |
| `docs/` | user-facing guides beyond the README: `getting-started.md`, clone to a first report in three steps (setup with every job skipped, then `playground`; setup's Gemini, then `readme-answers`; your own exam with your agent); `cli.md`, every verb and flag, the live variant, the report files (moved out of the README); `configuration.md`, the reference for exam, fixture, task, variant and `proctor.json` (moved out of the README; `exam-authoring`'s variants name it in their bundle's paths); `porting-to-agent-neutral.md`, how an exam moves to agent variants (a draft, from porting `code-change`); `network.md`, what an agent's container can reach on any engine, what an allowlist is and is not, and how to see it (the page every engine guide points at); `service-fixtures.md`, how to write a fixture with services and what the agent can and cannot see of one; `docker.md`, what Docker needs, rootless and its bridge's range, what a trial leaves, and macOS marked not run; `podman.md`, what a machine needs for `harbor.engine: podman`, why, and a sandbox for Harbor; `modal.md`, agent variants on Modal's sandboxes from a second machine file (`--config`), what it costs and what cannot run there |
| `skills/proctor-answers/` | the skill for an agent answering a person from `compare` and `coverage`: quote proctor's numbers, keep each inside its scope, link the evidence, and say which part is the agent's own recommendation |
| `skills/proctor-exam/` | the onboarding skill a user copies into their agent's skills directory: plan an exam with the user (inventory, tasks, checks, variants, cost; a `PLAN.md` approved), then build it and prove its checks for free; only what a frontier model would not guess and proctor's gotchas, pointing at `docs/configuration.md` and the example exams for the rest |
| `View/` | the viewer, auspex, moved in on 2026-11-24 (stage 1 of `project/plans/rename-to-auspex.md`): its own project (`View/Auspex.csproj`, ASP.NET, pages rendered on the server) and tests (`View/Auspex.Tests/`) in the same solution, not compiled into proctor. It reads the bank's files itself and calls proctor's code for what only proctor may work out (`View/Measured.cs`: an exam's configurations, `Answers.Compare`, `Answers.Coverage`), so proctor's internals are visible to it. It makes no model call. Its own notes are `View/CLAUDE.md`; its design notes are `project/view/` |
| `project/` | the brief, the plans, the learnings and the loose ends (`todo.md`); `archive/` holds built and dropped plans and the old bug notes, in the earlier words |
| `project/gate/` | `old-words.sh`, the grep gate for the old words, and `old-words.allow`, where each exception is listed with a reason |

The directories are for reading, not for namespaces: everything is
`namespace Proctor`. The data directories `exams/`, `fixtures/`, `bench/`,
`bank/`, `log/`, `exams-seen/` and `reports/` are lowercase and excluded from compilation in
`Proctor.csproj`.

## Conventions and gotchas

- **`failed` is infrastructure; a bad transcript is `completed`.** A hook that
  exits non-zero or an nb startup error (exit 1, no `result` trailer) marks the
  attempt failed with a reason. A transcript whose exit reason is `max_tool_calls`
  is completed; grading decides what it means.
- **Grading writes beside the evidence.** `checks.json`, a model check's
  `verdicts/`, and `grades/` are the only files the grader adds to an attempt. `report`
  reads `checks.json`; a completed attempt without one is reported as not
  analysed, and `report` says to run `grade`.
- **A grade names its standard, and an earlier grade is kept.** Every `grade` writes `checks.json` (the verdicts as they
  are now) and `grades/<standard>.json` in the attempt: the same verdicts with the standard's hash (`Standard.Hash`),
  the exam's hash and the time. Graded again under the same checks, that file is replaced; under changed checks,
  another is added beside it. On an exam with axes a try with no grade under the checks as they are now is **not yet
  graded**: it is left out of the report's numbers (`ConfigurationRow.NotGraded`, state `not_graded`), counted in
  `coverage`, and a `compare` that would read it is refused, so both sides of a comparison are always under one
  standard. `grade --value` or `--configuration` grades part of an exam and leaves the rest so. A check may carry
  `"when": "<axis> = <value>"` (with `!=` and `and`, as a stage's): where it does not hold, the verdict is
  `not-applicable`, no script runs and no judge is asked, the check is no part of the headline there
  (`Standard.PassIn`), and it counts toward nothing. `Standard.Moves` compares the current grade with the earlier ones
  kept: a check narrowed away from where it had been decided, one added after the first grade, one removed. Each is
  a sentence in `grade`'s output, in the report ("The standard moved") and in every comparison that reads the tries
  concerned, because a number can move with nothing re-run. Before a judged grade asks anything, `Grade.Cost` counts
  what it would ask of each judge and what is already on file (`JudgeClient.Counting`).
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
- **A check has one of four roles, by the list that names it** (the user, 2026-11-26; `Roles`, `Exam.RoleOf`). In
  `grading.correctness` it says whether the task was completed as intended: together those decide right or wrong, and
  none may ask a model (a `judge` or `decide` check there is refused). In `grading.integrity` it decides whether the
  attempt counts (a `fail` excludes the attempt with its reason, `error` never does). In `grading.quality` it is
  something preferred and not required: reported, deciding nothing. In no list it is an observation, counted with no
  preferred answer. A name in two lists is a problem, and `grading.pass` and `grading.validity`, the first two lists'
  earlier names, are refused with the new one. Nothing computes with quality apart from observation yet: the role is
  in `stats.json` (`stages[].checks`, name to role) for the viewer.
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
  `.proctor/bundles/<rev>`, with `paths` copied out of the clone into
  `.proctor/bundles/<rev>-<hash>`) is resolved once per run and recorded
  by source, path and identity in the log entry and every attempt manifest;
  proctor never reads what is inside it. A bundle's identity (a directory's
  tree hash, or `git:<rev>[:<paths>]`) is part of its variant's hash. On an agent variant the agent's container
  never holds the bundle, so an `agent: nb` variant with one cannot use
  `{{bundle}}`; it reaches the agent only through the fixture's services,
  which compile hands it as `environment/bundle/` (empty without one) for
  a compose service to mount and serve on the agent's `localhost`.
- **A configuration through Harbor is compiled into its image, never its environment** (`axes-build.md`, slice 4). On an exam
  with axes a value says who does the work, and `route: harbor` (or a vendor's agent) sends the try through Harbor;
  `agent: nb` alone still runs bare. `HarborCompile.Job` then reads the configuration: its values' files are copied into
  `environment/repo/` before the image commits it; each text parameter the image reads (`image.params`, or all of them)
  is an `ARG` in the compiled Dockerfile, placed before the image's own steps; each tree parameter is a directory
  `environment/params/<NAME>/` (empty when nothing sets it) for a compose service to mount; the job's steps are the stages
  that apply; and each step's `tests/env.sh` carries `PROCTOR_AXIS_*` and `PROCTOR_PARAM_*`. A parameter is never put
  in the agent's environment, since it can say what is planted, and a secret parameter reaches neither the image nor
  the verifier yet. `proctor build` on such a configuration compares what the image build is handed (the compiled
  `environment/`) and builds no image, so what a Dockerfile step does with an argument is held to account by a check in
  the verifier (`fixtures/toolchain`'s `toolchain-built`), not by `build`. An image that cannot be built loses the
  try, and its reason is the build's own error (`Ingest.Said`).
- **A variant with an agent runs through Harbor; a variant without one is nb run
  bare.** `variant.agent` is a vendor agent (`claude-code` on Anthropic models,
  `codex` on OpenAI's, `qwen-code` on Qwen's), with a pinned
  `agent_version` and a bare model of the agent's vendor, the nb fields
  refused; or `nb`, nb itself as a Harbor agent, with `harness`,
  `provider` and `model` as on the bare path and `runner`, `command` and
  `agent_version` refused. (Decision 1 of
  `project/plans/agent-neutral-runs.md`, Harbor exactly when the program has tools, waits for validation to refuse tools on the bare path;
  until then `agent: nb` is how an nb variant asks for Harbor.) The plan is
  `project/plans/agent-neutral-runs.md`; what Harbor writes is
  `project/learnings/harbor.md`. `run` compiles each (agent variant, task) once
  under `log/<id>/harbor/tasks/<variant>/<task>/`: the prompt as `instruction.md`, the fixture's `repo/` (and only it)
  committed in the image `fixture.image` describes, the variant's agent at its
  pin, and `tests/` with the exam, the fixture minus `repo/`, and a
  `test.sh` running every script check in Harbor's separate, offline
  verifier (one `reward.json` key per check). The compiled `task.toml`
  gives `[environment]` the same network policy as `[agent]` (the model's
  host allowed, or `no-network`), never `public`: Harbor applies a
  phase's policy only during the phase, so under a public environment a
  process the agent left running had the internet once its phase ended
  and between stages (`HarborCompile.Network`). A bridged task (an
  answer sheet) is the exception and stays `public`: Harbor installs
  acpx from npm in the container on every trial. A vendor agent reaches the
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
  of `verifier/<check>.out`, a missing key `error`. So it is fixed when the
  attempt is made: `HarborCompile.Outdated` compares the run's compiled
  `tests/` (the check's `test.sh` line, every file of the exam and fixture
  copies but `exam.json`, `live.json`, `combinations.json`, `tasks/*.json`, `variants/*.json`, `predictions/*.json`,
  `fixture.json` and another check's script, and `PROCTOR_EXPECT` in
  `env.sh`) with what compile would write now, and a difference, a check
  the run never compiled, or no compiled copy makes the verdict
  `outdated` (`Verdict.Outdated`), whose reason ends "make the attempt
  again". An outdated headline check makes the attempt undecided (`Grade.Pass`
  null, `ResultRow.Outdated`), out of every rate like `needs-judge`; `grade`
  prints `Grade.OutdatedSummary`, and `stats.json` `outdated_checks` is a
  warning at the top of the report, always, saying how to make the attempts again (move their directories out of the bank; `run` does not continue the old run for them, since `WhyNotContinue` refuses compiled checks that are not the checks as they are now). `loop_nudged` is
  `error` on a vendor agent's attempt, since only nb nudges; a script check
  that reads `PROCTOR_TRANSCRIPT` has none in the verifier.
- **nb in Harbor is the host's nb.** `proctor.json` `harbor.nb` is nb's
  self-contained linux-x64 publish; `run` builds it once into a
  `proctor-nb:<content hash>` image that each nb task's Dockerfile copies
  `/opt/nb` from, and refuses a publish whose `nb --version` is not the
  host nb's, since the host compiles the program (per attempt, `{{work}}` as
  `/app`) and the image runs it. The agent (`Harbor/nb_agent.py`) is on
  Harbor's `PYTHONPATH` from `log/<id>/harbor/agents/`. nb writes its own
  ATIF to `agent/trajectory.json` (`--trajectory`), beside its JSONL
  (`agent/nb.jsonl`, with `nb.stderr.log` and `nb.exit`); ingest takes it
  as a vendor agent's. Bare attempts get one too (`NB_TRAJECTORY`, so a runner
  script passes it on), though grading reads their JSONL. `NbTrajectoryTests`
  holds every `fixtures/nb-run` pair to reading the same both ways. The gate
  (`exams/gate`, `GateTests`, decision 12 of `agent-neutral-runs.md`) is opt-in:
  `PROCTOR_GATE=<publish dir> dotnet test --no-build --filter GateTests`,
  about ten minutes, Docker (or podman, with `PROCTOR_GATE_ENGINE=podman`)
  and uv needed. The filter takes `OracleGateTests` too (`exams/gate-oracle`,
  about four minutes). The publish must have `--acp`, so republish after an nb
  change the bridge sees.
- **Harbor's engine is the machine's.** `proctor.json` `harbor.engine`
  (`docker`, the default, or `podman`) is Harbor's `environment.type` in
  every attempt's job file and the CLI nb's layer is built with; podman's
  local tag is `localhost/proctor-nb:<hash>`, spelled out so the compiled
  `COPY --from` resolves no short name. The log entry records it and `run`
  does not continue an interrupted run on another engine (it makes what is missing as
  a new run), since the compiled tasks name a layer in that engine's
  store. Podman needs `podman.socket` running (Harbor's
  preflight probes `podman compose ls`), docker-compose v2 behind
  `podman compose` (podman-compose has no `ls` and rejects
  `exec --no-TTY`), a search registry, since the egress sidecar is
  `FROM gogost/gost`, and `compose_warning_logs = false`, since Harbor
  reads the compose banner as agent output; `run` checks the
  last three before anything starts (`HarborRun.CheckPodman`). The
  machine's setup is `docs/podman.md`; what was seen is
  `project/learnings/harbor.md`.
  The bare path's hooks pick their own engine (`command -v podman`).
- **A port on this machine reaches the agent through the relay, and only that port.** A base URL (or an nb
  provider's endpoint) whose host is loopback means the machine that runs proctor (`HostRelay.Loopback`), unless
  the fixture's own services answer on that port (`image.serves`, as `fixtures/stub` declares 8765). A fixture
  names further ports in `image.host_ports`. `Runner.Relay` works out a run's ports and refuses when one does not
  answer; `HarborCompile.Task` writes `proctor-relay.py` and the `proctor-relay` service into the task's compose
  file (creating one for a fixture with no services, so `NET_RAW` is dropped there too) and compiles the policy
  with no host for a relayed model (`HarborCompile.Reached`), so the agent's phase is `no-network`. `Make` holds
  the host end while attempts are made, at the directory the log entry recorded (`harbor.relay`), which a
  continued run needs since its compiled tasks mount it. The relay passes bytes and holds no key. Never open the
  host's loopback to containers in its place. Not on a remote engine. A fixture's `image.allowed_hosts` join the
  model's host in the allowlist (`HarborCompile.Hosts`), and `run` warns, never refuses, when one of them is inside
  the engine's default container range (`HarborRun.SubnetWarning`). The plan is
  `project/plans/container-network-finish.md`; what was measured is `project/learnings/harbor.md`.
- **Modal is an engine off the machine, picked by a second machine file.**
  `harbor.engine: modal` (`HarborConfig.Remote`) runs each attempt as Modal
  sandboxes on the user's login (`~/.modal.toml` or `MODAL_TOKEN_*`,
  inherited by Harbor's process; `HarborRun.Preflight` checks it), Harbor
  with its `modal` extra (`harbor[modal]@<pin>`). `--config <file>` on
  `list`, `run` and `grade` reads that file in place of
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
  names) and a fixture with `image.services`: since 0.26.0 Harbor's Modal
  environment runs a compose task, but builds its image inside the sandbox, so the environment's phase must be public
  and a process the agent left running has the internet after its phase (`project/learnings/harbor.md`, 2026-11-27).
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
- **The live variant is the reference, and only a live mark that holds makes
  one.** `exams/<exam>/live.json` is `{variant: {id, hash}, since}`,
  written by `proctor live <exam> <variant>` and committed with the exam;
  it is in no hash. It holds while that variant has a file with that hash
  (`Live.Broken`); otherwise `list`, `run`, `report` and `proctor live
  <exam>` print one warning and nothing is live. There is no verb to clear
  it: deleting the file is how. `report` hands the mark to `Stats.Compute`,
  which compares every other variant with the live one by the Newcombe
  pairing (`comparisons`, verdict `better`, `worse` or
  `no-detectable-difference`) when the mark holds and the live variant has
  attempts among the rows; `stats.json` `live` carries the mark and, as
  `not_reference`, why it is not the reference when it is not. With nothing
  live there are no comparisons: the card shows the variants side by side.
  The card and the Results table read the same `comparisons`; nothing else
  compares.
- **Every id in the report carries a sentence.** `description` is the one
  key in a check spec that is not a check (`Checks.Description`); a script
  check must declare one, a built-in derives one from its fields
  (`Checks.Describe`). Exam and task descriptions and a variant's `note` are
  optional; a task falls back to the first prompt line (`Exam.Describe`). They travel in
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
- **The report is one list of blocks rendered as markdown** (`Report.Blocks`,
  then `RenderMarkdown`). The HTML page went on 2026-11-22: auspex is the
  viewer, and a second renderer would have been refactored for stages for
  nothing. Inline forms in block text: `` `id` `` (body font, no wrap), ``` ``code`` ``` (monospace: only
  commands and paths; `Report.Paths` finds paths in free-text reasons),
  `**bold**`, `*italic*`, `[text](#anchor)` (its text only in
  `summary.md`).
- **The verdict card is direction plus confidence, both from `stats.json`.**
  With a variant live, each other variant is better, worse or the same
  than it by the sign of the point estimate, confirmed when the Newcombe
  interval excludes zero; with nothing live, the card is the variants side
  by side with no direction. The card never carries model output. The structure and the
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
  suffix stripped; `unknown` when it fails. It is read once per run.
- **nb's trailer carries `duration_ms` since nb 0.9** (nb f7df67f); the
  `max_duration_ms` check reads it and falls back to the attempt's wall time
  from the manifest, hooks included, which is what the report shows. It
  carries no cost, so an nb attempt has none; the report shows a cost column
  only when an agent reports its own (Claude Code, Codex as Harbor priced it).
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
  earns a place in `grading.correctness` by the numbers in `project/plans/jev-trial.md`;
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
  `PROCTOR_BUNDLE`, `PROCTOR_EXAM`, `PROCTOR_VARIANT`, `PROCTOR_TASK`, `PROCTOR_ATTEMPT`,
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
  the host binary and its config are. `--runner` overrides for one run. A
  `runner` or `mounts` left in `proctor.json` is reported as a problem, not
  ignored.
- **Labels are the consumer's; proctor has no tags.** `labels` on an exam,
  a fixture or a task is a key with one or more string values that proctor
  stores, prints, filters on (`list --label k[=glob]`) and records on every
  `results.jsonl` row, never interprets. `Exam.LabelsFor` merges fixture,
  then exam, then task, key by key. A `tags` field is reported as removed.
  Whether an exam is judged is `Exam.Judged`, derived from a check naming
  `judge` or `decide` (`Checks.AsksAModel`), not declared.
- **A variant is declared data in its own file, and its hash is its
  identity.** `exams/<exam>/variants/<id>.json` holds agent or runner,
  harness, provider, model, bundle and a free-text `note`; `exam.json`
  holds no variants (one that lists them is refused, saying where they
  go) and sets `attempts`, how many of each task every variant should
  have. A variant's hash covers what it declares but its id and note,
  its bundle by identity (`Exam.VariantHash`); a task's covers what the agent meets:
  the prompt, an oracle's answer sheet, the program template, the hooks
  and the fixture (how it is built and its source by content), not the
  checks, their scripts or the expected values, which are the standard
  and so a regrade (`Exam.TaskHash`); the exam's covers every definition file but the
  variants'. Each manifest records `exam`, `variant {id, hash}` and
  `task {id, hash}` and `log` (the entry of the run that made it); the log entry keeps `exam_hash`
  (the definition itself is under `exams-seen/`, once per hash), each
  variant it made attempts of as `{id, hash, def}`, each such task as
  `{id, hash}`, and the attempts it set out to make (`attempts`, each
  `{variant, task, attempt}`). `run`
  refuses a variant whose id made attempts under another hash (D5 of
  `exam-variant-attempt.md`), with what changed field by field from the log entry's copy of the file
  (`Runner.RefuseChangedVariants`). Hooks are the exam's; one that branches on
  `PROCTOR_VARIANT` to change what the agent sees hides it from the hash.
- **An exam's attempts are its bank; `run` fills its gaps.** The bank is every
  attempt every run of the exam set out to make (`Bank.Of`, which reads the
  log entries' lists, so an attempt directory no entry lists is not seen). An attempt is current when the variant and
  task hashes it was made under (its manifest's, or its log entry's for one
  never started) are today's, and stale otherwise (`Bank.Stale`, which says
  which hash moved). Missing is, per variant and task, the exam's
  `attempts` less the current, completed ones (`Bank.Missing`); new
  attempts are numbered after every attempt of that variant and task in
  the bank, so `variant/task/n` names one attempt and is its directory. A run is
  interrupted when it has current attempts `pending`, `running` or
  `failed` that are still missing (`Bank.Interrupted`); `run` continues
  each, oldest first, when its runner, Harbor version, compiled Harbor
  tasks, engine and nb image still hold and the script checks it compiled are current (`Runner.WhyNotContinue`), and
  otherwise says why and leaves it; then makes whatever is still missing
  as a new run, compiling and resolving only what that run makes.
  `grade`, `friction` and `report` read the current attempts and list the
  stale ones; a new or changed check is a regrade of every current attempt,
  never a rerun, except a script check on an agent variant, whose verdicts
  go `outdated` (above) until the attempts are made again.
- **Reports are stamped and kept.** `report <exam>` writes
  `reports/<exam>/explore/`; one already there is moved into a directory
  named by its build time beside it. `stats.json` `built_from` is the
  stamp. The page takes its conditions from the log entry of the latest run the attempts came from, with
  the exam's definition and variants as they are now (`Verbs.View`).
- **Validation says "not yet" rather than silently skipping**: `command` variants,
  run- and task-level hooks, `answer_json_schema`, `max_cost`. A task's
  `oracle` runs on nb variants only: bare, as nb's in-process oracle (its
  directives prepended by `Runner.OraclePrefix`); on Harbor, as
  `Harbor/oracle_user.py`, the simulated user, whose record ingest lines up
  with nb's trajectory (`Ingest.Oracle`). A vendor variant beside an oracle task
  is refused: Harbor leaves it no ATIF. On a staged exam a try with an answer sheet goes through Harbor only when it is
  one stage (`HarborCompile.Plain`): it is then compiled as a plain Harbor task, with no steps, and run by the
  single-attempt path into the try's layout (`Runner.RunAgentTask`), since the simulated user answers a task and never
  a step. A try of several stages with a sheet among them is refused through Harbor, and says to give it one stage
  (on an exam with axes, a stage's `when`) or to run it bare. An oracle's turn is a user step whose `extra.source` is
  `oracle`, with the sheet entries it answered from in `extra.keys`, the same bare and bridged; a question the oracle
  did not answer leaves no turn, only the ending (`oracle_miss`, `oracle_budget`), which through Harbor is in
  `core.json` and not in nb's own trajectory. The viewer shows the turn as the oracle's and says the ending.
- **Snapshot tests** fail on any rendering change and leave a `.received` file
  (gitignored) beside the approved one. Review it, then approve with
  `PROCTOR_APPROVE=1`.
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
