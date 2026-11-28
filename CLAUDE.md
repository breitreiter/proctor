# auspex — developer notes

Runner, grader and reporter of evals around nb (`../nb`). The intent is in
`project/brief.md`; the data model is `project/plans/axes.md`, built slice
by slice per `project/plans/axes-build.md` (its progress log says what each
slice did); where the project goes next is `project/plans/remix.md`. Built
and dropped plans are in `project/archive/`, written in the earlier words,
and so are `project/plans/on-disk-layout.md` and
`project/plans/exam-variant-attempt.md`, the layout and the model before
axes.

## Build and test

```bash
dotnet build
dotnet test --no-build                 # both suites, auspex's and the viewer's (View/Auspex.View.Tests); the runner tests spawn nb per stage
dotnet bin/Debug/net10.0/auspex.dll list             # or: dotnet run -- list
```

Tests need nb built: `$NB_PATH`, else `../nb/bin/Debug/net10.0/nb` (build nb
with `dotnet build` in its repo; its Mock provider drives every test here).

## Bugs

File a bug as a GitHub issue (`gh issue create`), never as a file. The
notes on bugs filed before 2026-10-10, all fixed, are in `project/archive/`.
When planning a bug bash, start from the open issues (`gh issue list`).

## Vocabulary

Fixed in `project/plans/axes.md` and `docs/configuration.md`. Use these
words in code, files, prose and the report, and no others for these things:

| word | one sentence |
|---|---|
| **exam** | One question, the stages that ask it and the checks that grade them, with what is under test declared as axes. |
| **stage** | One prompt and one desired outcome, with its own answer key for the checks that apply to it, and any checks of its own. An exam's stages run in order in one workspace; a stage's `when` says where it applies. |
| **check** | One yes-or-no grading question asked of a finished stage, declared on the exam, a fixture or a stage (a stage carries the union). Where it is listed sets its role: `correctness`, `integrity`, `quality`, or in no list an observation. |
| **fixture** | The repository the work is done in and what "done" looks like in it; reused across exams. |
| **axis** | One thing that is varied. |
| **value** | One choice on an axis, as declared data: parameters it sets, files it carries, who does the work, or a whole fixture. Its identity is its content. |
| **configuration** | One value on every axis: what a try ran as. Its key is `axis=value,…` in the exam's axis order. The exam carries the ones it names, is asked for, or has tries of, never the cross product. |
| **try** | One configuration, run once through every stage that applies in it, with the evidence. Several per configuration, because the agent is stochastic. |
| **attempt** | One stage of one try (`bank/<exam>/<try id>/stages/<i>-<stage>/`). |
| **bank** | Every try of an exam, whenever it was made. What one `auspex run` did, when, on what machine and at what versions, is a **log entry** (`log/<id>/`): a record, never a grouping a person reads results by. |
| **live** | A mark on one value of an axis: the one in use now, and what the other values of that axis are compared with. An axis nobody chooses has a `reference` instead. |
| **prediction** | One statement written before the tries are made: run this configuration, and this will change. Its lines are **changes**, each a check or a metric and a direction. It replaced "claim" (the user, 2026-11-25); "candidate" and "treatment" are not words here. |

**There is one shape** (slice 7 of `axes-build.md`, 2026-11-27): an exam is
stages and axes. Tasks as files, variants, bundles, `live.json`, `--variant`,
`--tasks`, `auspex live`, the continuation of an interrupted run, and the
report of variants side by side are gone; `Exam.Load` refuses the earlier
shape and says what each part becomes, and `docs/porting-from-suites.md` is
the map. "Variant" is an old word now, like suite, arm and batch before it.

**A refusal is written for someone coming from `master`** (proctor 0.3:
suites, arms, tasks as files, `hooks.arm` and `hooks.sample`, `grading.pass`,
`{{arm}}`, `{{sample}}`, `{{task}}`, `--arm`, `baseline`, `resume`), who is
everyone outside this branch. A form that only ever existed on this branch
(`variants/`, `hooks.variant`, `--variant`, `auspex live`) gets none.

**The names kept for the rename are gone** (2026-11-28, layer 1 of stage 2 in
`project/plans/rename-to-auspex.md`). In `manifest.json`, `entry.json`,
`results.jsonl`, `stats.json` and `friction.json` (`friction/3`) a
configuration's key is `configuration` and a stage is `stage`; a manifest's
`values` is the value on each axis and `place` the stage's place in its try;
a log entry's `stage_hashes` is each stage it ran with the job's hash.
`AUSPEX_STAGE`, `AUSPEX_STAGE_JSON` and `{{stage}}` hold a stage. In code a
stage is a `StageDef` (`Exam.Stages`) and one stage of one try a
`BankAttempt`. Nothing reads the earlier field names: no compatibility layer
is kept for tries made on this branch (the user, 2026-11-28), since everyone
else is on `master`. A try's number among its configuration's is `try_number` in those files,
`AUSPEX_TRY_NUMBER` and `{{try_number}}` (`try` in a manifest was already
the try's id), and how many tries an exam wants of each named configuration
is `tries` in `exam.json`; `stats.json` still calls a try's number `n`. Who does the work in a
configuration is an `Assistant` (`Axes.AssistantOf`), whose `Id` is the
configuration's key.

**The project was proctor until 2026-11-28** (stage 2 of the same plan, whose
"Done" section says what each layer did). The code is `namespace Auspex` and
builds `auspex.dll` (the viewer, which alone held the name before, is
`Auspex.View` and `auspex-view.dll`); every variable is `AUSPEX_*`; the
machine's files are `exams/auspex.json`, `.auspex/` and `~/.config/auspex/env`;
containers and images are `auspex-…`; the skills are `skills/auspex-exam` and
`skills/auspex-answers`; messages and docs say auspex. No old name is read:
a `PROCTOR_` variable, `exams/proctor.json` or `~/.config/proctor/env` left
in place is refused with its new name. Still proctor: the repository, this
directory, the site's base URL (`Catalogue.SiteBase`), `docs/index.html` and
`docs/providers.html`, and the two exams that show an agent the tool,
`agent-onboarding` and `exam-authoring`, with their fixtures: they pin this
repository at a revision from before the rename, so their prompts, scripts
and the pinned build keep the old name until they are pinned again, and a
real try of either waits for that (their shakedowns pass). Records keep it
too: `project/archive/`, `project/learnings/`, `project/view/`, most of
`project/plans/`, past CHANGELOG entries, `bench/`, captured fixtures and the
published reports. "proctor 0.3" is what `master` was before the merge.

**Attempts made by an earlier version** (log entries with no stages, no try
ids, or the earlier field names, `LogEntry.Earlier`) stay on disk and are read by nothing: `Bank.Entries`
skips them, `list` counts them (`Bank.Earlier`), and the viewer lists them
at `/{exam}/earlier` so their transcripts can still be opened. `tern-docs`
has 72 paid tries in the bank of this machine: a change under
`exams/tern-docs/` or `fixtures/tern-shop/` beyond a definition file or a
README outdates or stales them.

**Not built** of `project/plans/bank.md`: back-fill and the freeze.

## Structure

One project, one executable, no library split. The root holds the entry point
and the project files; the source sits in one directory per pipeline stage,
in the order the verbs run them. Every file is one concern:

| file | holds |
|---|---|
| `Program.cs` | flag parsing and verb dispatch; `AuspexException` is a user-facing failure; what proctor 0.3 had and this has not (`--arm`, `--tasks`, `--live`, `baseline`, `resume`) refused with what replaces each |
| `Verbs.cs` | one method per verb: `list`, `run`, `build`, `grade`, `friction`, `report`, `compare`, `coverage`, `predict`, `retire`; which configurations a `--value` or `--configuration` selects (`Verbs.ForRun`); the earlier report moved aside |
| `Exam/Layout.cs` | paths and file names, nothing else |
| `Exam/Exam.cs` | `auspex.json` (the host binary and its config, `HarborConfig`), `exam.json` (attempts, stages, the `nb` block with the runner and mounts, hooks, grading), the program template: records, loading, validation (`Problem` = file, field, message); the earlier shape refused with what each part becomes; the merged check set and expect block per stage; the stages that apply in a configuration (`StagesOf`); the exam's hash and the job's (`JobHash`: what the agent meets) |
| `Exam/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Exam/EnvFile.cs` | the user's env file (`~/.config/auspex/env`, `%LOCALAPPDATA%\auspex\env` on Windows, or `AUSPEX_ENV_FILE`): `NAME=value` lines `Program.Main` loads into the process environment before any verb, the real environment winning, so every `${VAR}` resolver and child process sees them |
| `Exam/Judges.cs` | `auspex.json`'s `judges` block: an endpoint per name by wire shape (`systemone`, `chat`), key resolution, which judge a check gets |
| `Exam/Tree.cs` | a directory's files minus excluded names, and their content hash (fixtures and a value's files); `Tree.Litter`, the tool caches (`__pycache__`, `.pytest_cache`, …) no walk, hash or compile copy ever takes, since git ignores them and they differ by machine |
| `Exam/Axes.cs` | an exam's `axes` in `exam.json` (`project/plans/axes.md`): axes, values and each value's hash, the live value or reference of each axis, the configurations the exam carries (named, asked for, or with tries in the bank; never the cross product), `exclude`, parameters (`Axes.Params`) and the files a configuration lays over the checkout (`Axes.Overlays`), who does the work in a configuration (`Axes.AssistantOf`) |
| `Exam/Combinations.cs` | `exams/<exam>/combinations.json`, written by `build` and nothing else: which several-valued configurations are their values put together, more (a script reads several axes), or cannot be told; what holds today (`Combinations.Stand`). Whether anyone read what a value changes is recorded nowhere, on purpose |
| `Exam/Predictions.cs` | `exams/<exam>/predictions/<name>.json`: one configuration the exam names, what it is compared with (`against`, or what is live), and the changes expected, each a check or a metric (`held`, `flagged_steps`, `tool_calls`, `tokens`, `duration`) with `more` or `less`; validation; when it was made (`written`, its author's, required: a date or a time in UTC); the pin (`Predictions.Stamp`: each stage's standard when auspex first saw it, set once). Committed with the exam and in no hash |
| `Exam/Retired.cs` | `exams/<exam>/retired.json`: the named configurations retired, each with its reason and date (`auspex retire`, `--bring-back`). Committed with the exam and in no hash. Nothing is deleted: `run` makes no new tries of one (`Verbs.ForRun`) and the main lists leave it out; the live configuration cannot be retired; `retire --json` answers as `retire/1` (`RetireAnswer`) |
| `Run/Runner.cs` | `run`: where each configuration stands (`Standing`), then the tries it lacks made, each whole: every applicable stage in one workspace, restored between stages unless chained; a lost try made again under the next number, never continued; the log entry (`log/<id>/entry.json`), `exams-seen/` and each stage's manifest, hooks, nb as a subprocess, a try through Harbor; a changed value under a name that made tries refused (`RefuseChangedValues`) |
| `Run/GitFiles.cs` | a value's files or a tree parameter from a git revision: cloned once under `.auspex/files/<rev>`, a `paths` subset copied to `<rev>-<hash>` (`GitFiles.Fetch`); a path the revision lacks stops the run |
| `Run/Build.cs` | `build`: the starting workspace of the reference twice, of each value alone and of each several-valued configuration, with the setup hooks and nothing run; a value's change (`ValueChange`) and the combination check, whose changes to one file are merged line by line |
| `Run/Bank.cs` | an exam's bank: the log entries of this shape (`Bank.Entries`; the earlier ones only counted, `Bank.Earlier`), each stage of each try (`Bank.Of`), current or stale by the configuration's and the job's hashes it was made under (`Bank.Stale`), the tries a configuration lacks (`Bank.Missing`), the selection `--value` and `--configuration` narrow (`Bank.Select`) |
| `Harbor/Compile.cs` | a configuration's try as a Harbor task whose steps are its stages (`instruction.md`, `task.toml`, the image, the verifier per step), or as a plain task when it is one stage with an answer sheet (`Plain`), and its job file; whether a compiled script check is still the check as it is now (`Outdated`) |
| `Harbor/nb_agent.py` | nb as a Harbor agent: uploads the stage's program and nb's config, runs nb on stdin; embedded, compiled into `log/<id>/harbor/agents/` (Python, not compiled into auspex) |
| `Harbor/oracle_user.py` | the oracle as Harbor's simulated user, on the host: sends the task over the ACP bridge (`acpx`), judges each finished turn with the host's `nb --judge`, answers from the sheet, writes `oracle-user.json`; embedded and compiled like `nb_agent.py` with its template (`oracle_user.j2`) when nb through Harbor meets a stage with an answer sheet |
| `Harbor/qwen_code_agent.py` | Harbor's Qwen Code agent with its per-trial network install replaced by a check that the image holds the pin; embedded and compiled like `nb_agent.py` |
| `Run/Harbor.cs` | one try's Harbor job: `uv tool run harbor@<pin>` with a seven-day `--exclude-newer` cooldown on everything it resolves, telemetry off, the key in its environment only; nb's layer image from its publish |
| `Run/Relay.cs` | the relay's host end (`HostRelay`): for each port of this machine a run's agents reach, a unix socket in the user's runtime directory passed to `127.0.0.1:<port>`; what the log entry records of it (`ResolvedRelay`) |
| `Harbor/relay.py` | the relay's container end, compiled into a task as the service `auspex-relay`: serves each port at the agent's `localhost` and passes every connection to the mounted socket; embedded like `nb_agent.py` |
| `Run/Ingest.cs` | a Harbor trial into a stage's attempt, and the counted core (`core.json`) from the ATIF trajectory |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, Harbor, script checks, git; an optional timeout |
| `Grade/Transcript.cs` | nb JSONL, or a Harbor attempt's ATIF trajectory with its `core.json`, into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
| `Grade/Window.cs` | what a model check sees: named extractions from the transcript, joined with `+`, compiled into labelled fences |
| `Grade/Judge.cs` | `JudgeClient` (the transports), the `decide` check over systemone, the `judge` check over chat, the verdict file as cache, the report's judge provenance |
| `Grade/Standard.cs` | grades over time: the standard a stage is graded under (its checks, their conditions and scripts, the correctness and integrity lists, the expected values), each grade kept under it in the attempt's `grades/`, whether a check applies in a configuration (`when`), which tries are not yet graded, and what moved between an earlier grade and this one (`Standard.Moves`: narrowed, added, removed) |
| `Grade/Grade.cs` | checks over a stage's attempt into `checks.json`; whether it passed |
| `Friction/Shell.cs` | a shell command as the friction rules read it: words, then actions (one per pipeline, named by its first command, heredoc bodies dropped); POSIX path and glob helpers, since the paths are the container's |
| `Friction/Friction.cs` | `Surface` (the exam's `surface` block, compiled) and the friction rules: an attempt's trajectory into `friction.json` (schema `friction/3`: the `attempt` it is about, points, each with a checked quote, and the summary by surface part) |
| `Friction/Reader.cs` | the reader stage of `friction --reader`: an attempt's trajectory rendered as text with a call id per call, the fixed instructions, the reply checked into points with a model source, the reading file kept in the attempt |
| `Setup/providers.json` | `auspex setup`'s provider catalogue, embedded: per kind its form fields (a secret's env variable), the jobs it `serves` (`?` untested) or serves `by_hand`, the site help anchor (never a vendor URL; an anchor once released is permanent), where it lists models. The plan and mockups are `project/plans/setup/` |
| `Setup/Catalogue.cs` | the catalogue as records; `ProviderKind.Fit(job)` |
| `Setup/KeyCheck.cs` | the key check: list a provider's models (no tokens), the ids sorted, or the provider's words with the key redacted and a reading that is the same for every kind; Azure, and an OpenAI-compatible entry with no endpoint yet, have none. `Auspex.Tests/fixtures/models/` are answers captured from each vendor, trimmed |
| `Setup/SetupState.cs` | `exams/setup.json`: the providers setup knows (kind, key variable, last check, the models listed) and the answer per job (`set`, `later`, `by-hand`; the sandbox's place), intent included; the job order |
| `Setup/SetupFiles.cs` | setup's writes in `exams/`: its own nb.json entries (the key as `${VAR}`, no `Model`), auspex.json `agents.<agent>` and `harbor.engine`, `exams/.gitignore`; read as a JSON tree and written back whole, so an entry setup did not write is never touched, and a file it cannot parse is refused, not overwritten |
| `Setup/SetupWizard.cs` | `auspex setup`: the Spectre wizard, job by job (use a provider, add one, skip); the provider flow (pick, form, key check, name); the rerun menu; the summary. Refuses with exit 2 without a terminal. The only file that uses Spectre: `--check` and every other verb print plain lines |
| `Setup/SetupCheck.cs` | `auspex setup --check`: plain lines for an agent, `problem:` lines and exit 1 for what to fix, never a value; `--probe` repeats each key check |
| `Setup/SandboxPlan.cs` | `exams/sandbox-plan.md`: what setup observed (OS, uv, docker, podman and its socket, compose, registries) and what auspex needs, written as a brief for the user's agent, never a list of commands |
| `Report/Results.cs` | `results.jsonl` rows, one per stage of every current try read |
| `Report/Stamp.cs` | the report's stamp (`built_from`: the runs' log entries, hashes, current and stale counts, mixed conditions) |
| `Report/Tries.cs` | a configuration's tries as the unit: per try the correctness checks held and the flagged steps, per stage and per check the tallies, what recurs in friction (`friction-rollup/1`); Mann–Whitney exact (`MannWhitneyP`) and the smallest p two counts of tries allow (`MinP`) |
| `Report/Configurations.cs` | the report (`stats.json` as `configurations/1`, `summary.md`): the configurations the exam carries, the judges whose verdicts the tries hold, and `Compare`, the one place a value is compared with another, every other axis held (Mann–Whitney in each setting, van Elteren across them, "better here, worse there" when they disagree) |
| `Report/PredictionOutcomes.cs` | how each change a prediction expects came out against the tries, as a sentence, a miss as plainly as a hit; `post hoc` when tries were made before it was written, and whether the standard moved since |
| `Report/Answers.cs` | `compare` and `coverage`: what auspex answers an agent, as JSON (`answer/1`), from `Configurations.Compare` and the bank; an answer's cautions, its tries' addresses and its `as_of`. Auspex does no decision support (`project/plans/axes.md`, decision 17): these recommend nothing |
| `exams/smoke/` | auspex's own exam: every stage scripts nb's Mock provider; two axes (`assistant`, and `extra`, whose value lays files over the checkout), configuration and try hooks |
| `exams/code-change/` | the first real exam: three changes in three repositories (the `repository` axis, each value a fixture, each stage belonging to one), acceptance tests, reference solutions; five assistants through Harbor (nb, Qwen Code, Claude Code on Haiku and on Sonnet, Codex), each named in each repository |
| `exams/gate/` | the deterministic gate: nb on Mock bare and through Harbor (the `route` axis), five stages; `GateTests` (with `AUSPEX_GATE`) asserts they agree |
| `exams/gate-oracle/` | the oracle's gate: three stages, one per ending (`ok`, `oracle_miss`, `oracle_budget`), each applying on one value of the `ending` axis, so a try is one stage with an answer sheet; the `route` axis runs it bare (nb's in-process oracle) and through Harbor (the simulated user, as a plain task). `OracleGateTests` (with `AUSPEX_GATE`) asserts the two routes agree, apart from the judge's tokens: the same counted core, the same checks, and the same user turns by text, source and sheet keys |
| `exams/oracle-change/` | the oracle on a real model: `add-retry-flag` with its semantics only in the answer sheet, chosen to be unconventional so a guess fails acceptance; nb on Anthropic Haiku bare and through Harbor (the `route` axis), judged by Haiku. Real API calls (about $1 for a full set of tries) |
| `exams/conformance/` | Harbor conformance at $0: every vendor agent at its pin against `fixtures/stub`'s scripted model; the `script` axis (`ok`, `fail`, `hang`) picks the one stage of a try, since a stage that fails on purpose loses its try; `ConformanceTests` (with `AUSPEX_CONFORMANCE`, `1` or an `auspex.json` whose `harbor` block to run on) asserts each try came back as scripted. The plan and its benchmark are `project/archive/harbor-conformance.md` |
| `exams/tern-docs/` | the docs showcase, and the first exam on axes (2026-11-25): Claude Code on Sonnet implements six shipping functions in `fixtures/tern-shop` against Tern, an invented carrier. Its `docs` axis sets the fixture's `DOCS` tree parameter (as shipped, restructured, or none), which the fixture's docs service serves; `assistant` is the agent and model. Hidden acceptance runs against Tern's sandbox API started in the verifier; `reference/shakedown.sh` proves the checks for free (`--unsolved`, `--v1`, `--kilograms`). The plan is `project/archive/bundle-showcase.md` |
| `exams/exam-authoring/` | the demo project: an agent writes exams, graded by running them against planted good and bad runs (`trial/`, in a container, the written exam given one more axis, `planted`); its report is meant to be copied into `report/`, which has none yet. Three axes: `workspace` (three fixtures, each stage belonging to one), `docs` (auspex's docs at a pinned GitHub revision as the value's files, laid at `.bundle/`; with and without the example exams) and `assistant`. nb bare in a container the try hooks make; the configuration hook builds auspex at the pinned revision. `reference/shakedown.sh` proves the trial for free (`--naive`, `--unsolved`) |
| `exams/playground/` | the first exam a new user runs: two scripted Mock stages on `fixtures/jotcli`, no model and no key; the fixture's `builds` and `tests-pass` run for real on the checkout, so the first report is about a repository |
| `exams/cli-for-agents/` | the first exam with a real model: one stage on `jotcli` (`clear-for-scripts`: add `--yes`, refuse without a terminal, exit non-zero when nothing was cleared), nb bare on the local model; `acceptance/<stage>.sh` runs the built CLI with no TTY under a ten-second limit; `reference/shakedown.sh` proves the checks solved and `--unsolved` without a model |
| `exams/agent-onboarding/` | a shipped coding agent, given auspex's skill, docs and CLI at a pinned revision (in the fixtures' image), writes a chat-only exam for a small bot project; the three projects are the `project` axis, each a fixture, each stage belonging to one. Each written exam is graded by a trial against planted replies (`trial/<stage>/plants.json`, the written exam given one more axis, `planted`); `reference/shakedown.sh` proves the checks for free in four modes |
| `exams/nb-changes/` | defect stages and a feature stage on nb itself, at pinned revisions: the `planted` axis, each value a fixture (`fixtures/nb-*`), each stage belonging to one; `tests-green` runs nb's own tests; `reference/shakedown.sh` proves the checks on the host |
| `exams/runners/` | the container runner (`container.sh`) and its image (`Containerfile`) that exams' `nb.runner` names |
| `exams/readme-answers/` | the first exam on a hosted model, after `playground`: six questions about jot answered from a README alone (`system @{{param.README}}/README.md`, `tools none`), two values of the `docs` axis that differ only in the README tree they set, Gemini Flash-Lite on the free tier, every check a regex (`correct` per stage in `expect`); `reference/shakedown.sh` proves the checks on Mock (`--wrong` for none) |
| `fixtures/` | the repositories stages run in, each with its own checks beside (never inside) its `repo/`. `jotcli` is a tiny TypeScript CLI whose `clear` reads its confirmation from `/dev/tty` with no `--yes` and exits 0 when it gives up, planted on purpose (its README says why); one of two non-.NET fixtures, with `tern-shop` (Python, beside two compose services: Tern's sandbox API and a docs site serving the `DOCS` tree parameter). `empty` holds nothing, for stages whose whole context is in the prompt. `toolchain` is a scripted control whose image takes a declared parameter as a build argument and writes it to `/etc/toolchain`, standing in for a real toolchain axis |
| `assets/` | the wizard hat icon, adapted from Lorc's "Pointy hat" (game-icons.net, CC BY 3.0); not MIT. Wherever it ships (the site), the credit goes with it |
| `Auspex.Tests/` | xunit, flat; `fixtures/` are captured Mock transcripts, and `fixtures/nb-run/` pairs of nb's JSONL and its own trajectory from one nb run (`capture.sh` regenerates them); `exams/` is test data `TestRepo.CopyExam` finds after `exams/` (`harbor-unit`, which `HarborTests` build on with a fake uv), never an exam to run; `TestRepo` has the helpers (`A` and `B`, two configurations' keys; `Run`, `TryDir`, `StageDir`, `EditStage`); `exams/axes-harbor` is the scripted control for axes through Harbor (nb on Mock in `fixtures/toolchain`'s image, a toolchain as a build argument, a README laid into the repository, a stage with a condition), compiled by `AxesHarborTests` and run in Docker by hand; `exams/axes-answers` is a scripted exam with known answers for `compare` and `coverage` (its checks pass at a chance set by the configuration; `make.sh <dir>` makes its bank in a scratch root, no model), which `AnswersTests` runs part of; `python/` is `oracle_user.py`'s unittests, on stub `harbor` modules and captured `acpx` output, which `OracleHarborTests` runs with `python3` |
| `docs/` | user-facing guides beyond the README: `getting-started.md`, clone to a first report in three steps (setup with every job skipped, then `playground`; setup's Gemini, then `readme-answers`; your own exam with your agent); `cli.md`, every verb and flag, what a try is and how one ends, what is live, the report files; `configuration.md`, the reference for exam, stage, axis, value, fixture, check and `auspex.json`; `porting-from-suites.md`, the map from tasks and variants (and from suites) to stages and axes; `porting-to-agent-neutral.md`, what an exam, its fixture and the machine need for a try through Harbor; `network.md`, what an agent's container can reach on any engine, what an allowlist is and is not, and how to see it (the page every engine guide points at); `service-fixtures.md`, how to write a fixture with services and what the agent can and cannot see of one; `docker.md`, what Docker needs, rootless and its bridge's range, what a trial leaves, and macOS marked not run; `podman.md`, what a machine needs for `harbor.engine: podman`, why, and a sandbox for Harbor |
| `skills/auspex-answers/` | the skill for an agent answering a person from `compare` and `coverage`: quote auspex's numbers, keep each inside its scope, link the evidence, and say which part is the agent's own recommendation |
| `skills/auspex-exam/` | the onboarding skill a user copies into their agent's skills directory: plan an exam with the user (inventory, stages, checks, axes and the configurations to run, cost; a `PLAN.md` approved), then build it and prove its checks for free; only what a frontier model would not guess and auspex's gotchas, pointing at `docs/configuration.md` and the example exams for the rest |
| `View/` | the viewer (`auspex-view`; it alone was called auspex until the rename), moved in on 2026-11-24 (stage 1 of `project/plans/rename-to-auspex.md`): its own project (`View/Auspex.View.csproj`, ASP.NET, pages rendered on the server) and tests (`View/Auspex.View.Tests/`) in the same solution, not compiled into auspex. It reads the bank's files itself and calls auspex's code for what only auspex may work out (`View/Measured.cs`: an exam's configurations, `Answers.Compare`, `Answers.Coverage`), so auspex's internals are visible to it. It makes no model call. Its own notes are `View/CLAUDE.md`; its design notes are `project/view/` |
| `project/` | the brief, the plans, the learnings and the loose ends (`todo.md`); `archive/` holds built and dropped plans and the old bug notes, in the earlier words |

The directories are for reading, not for namespaces: everything is
`namespace Auspex` (the viewer is `Auspex.View`). The data directories `exams/`, `fixtures/`, `bench/`,
`bank/`, `log/`, `exams-seen/` and `reports/` are lowercase and excluded from compilation in
`Auspex.csproj`.

## Conventions and gotchas

- **`failed` is infrastructure; a bad transcript is `completed`.** A hook that
  exits non-zero or an nb startup error (exit 1, no `result` trailer) marks the
  stage's attempt failed with a reason, and a failed stage loses its try: nothing
  more of it runs, none of it counts, and the next `run` makes another, whole.
  A transcript whose exit reason is `max_tool_calls` is completed; grading
  decides what it means. On a chained exam a stopped stage ends the try and
  the stages after it are `skipped`; the try still counts.
- **Grading writes beside the evidence.** `checks.json`, a model check's
  `verdicts/`, and `grades/` are the only files the grader adds to a stage's
  attempt. `report` reads `checks.json`.
- **A grade names its standard, and an earlier grade is kept.** Every `grade` writes `checks.json` (the verdicts as they
  are now) and `grades/<standard>.json` in the attempt: the same verdicts with the standard's hash (`Standard.Hash`),
  the exam's hash and the time. Graded again under the same checks, that file is replaced; under changed checks,
  another is added beside it. A try with no grade under the checks as they are now is **not yet
  graded**: it is left out of the report's numbers (`ConfigurationRow.NotGraded`, state `not_graded`), counted in
  `coverage`, and a `compare` that would read it is refused, so both sides of a comparison are always under one
  standard. `grade --value` or `--configuration` grades part of an exam and leaves the rest so. A check may carry
  `"when": "<axis> = <value>"` (with `!=` and `and`, as a stage's): where it does not hold, the verdict is
  `not-applicable`, no script runs and no judge is asked, the check decides nothing there
  (`Standard.PassIn`), and it counts toward nothing. `Standard.Moves` compares the current grade with the earlier ones
  kept: a check narrowed away from where it had been decided, one added after the first grade, one removed. Each is
  a sentence in `grade`'s output, in the report ("The standard moved") and in every comparison that reads the tries
  concerned, because a number can move with nothing re-run. Before a judged grade asks anything, `Grade.Cost` counts
  what it would ask of each judge and what is already on file (`JudgeClient.Counting`).
- **Friction is beside grading, not in it.** `auspex friction` writes
  `friction.json` in an attempt and reads only `trajectory.json` and the
  exam's `surface`; no check, verdict or report reads either. The rules
  have their own version (`Friction.RulesVersion`, in every file and
  every point), bumped whenever a rule changes what it flags. They were
  ported from a Python trial in the viewer's old repository (`project/trials/friction.py` there;
  the contract is `project/view/friction-contract.md` here) and held to
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
  `grading.correctness` it says whether the stage was completed as intended: together those decide right or wrong, and
  none may ask a model (a `judge` or `decide` check there is refused). In `grading.integrity` it decides whether the
  try counts (a `fail` in any stage excludes the try with its reason, `error` never does). In `grading.quality` it is
  something preferred and not required: reported, deciding nothing. In no list it is an observation, counted with no
  preferred answer. A name in two lists is a problem, and `grading.pass` and `grading.validity`, the first two lists'
  earlier names, are refused with the new one. Nothing computes with quality apart from observation yet: the role is
  in `stats.json` (`stages[].checks`, name to role) for the viewer.
- **A check is declared at one of three levels, and a stage carries the
  union.** The exam's `grading.checks` reach every stage; a fixture's
  `checks` reach every stage run on it; a stage's own `checks` reach that stage
  (`Exam.ChecksFor`, exam then fixture then stage). A name at two levels is
  a problem. A `correctness` or `integrity` check the exam does not declare must
  reach every stage from its fixture or its own block. A stage's scripts
  resolve against the exam directory. A check's rate is over the times it
  was asked, so a stage-only check reports over one stage.
- **The fixture is the world; what is under test is a value's.** An exam
  names one fixture, or has an axis whose values each name one, each stage
  then belonging to one by its `when`. The fixture's checks join the exam's
  (a clash is a problem) and its default `expect` sits under the stage's. Only
  `repo/` is ever copied into the work directory, so the checker and the
  expectations are unreachable from inside it by construction. Auspex does
  the checkout once per try, lays the configuration's files over it
  (`Axes.Overlays`), runs the try's setup hook and commits; before each stage
  it restores that commit unless the exam is chained, and after each it
  collects `diff.patch`. A value's `files` and a tree parameter are a
  directory in the exam or a git revision (`GitFiles`), and are part of the
  value's hash by content or by `git:<rev>[:<paths>]`; auspex never reads
  what is inside. A parameter must be declared (`params` on the fixture, or
  on the exam for what the program reads) before a value may set it.
- **A configuration through Harbor is compiled into its image, never its environment** (`axes-build.md`, slice 4). A
  value says who does the work, and `route: harbor` (or a vendor's agent) sends the try through Harbor; nb with no
  route runs bare. `HarborCompile.Job` then reads the configuration: its values' files are copied into
  `environment/repo/` before the image commits it; each text parameter the image reads (`image.params`, or all of them)
  is an `ARG` in the compiled Dockerfile, placed before the image's own steps; each tree parameter is a directory
  `environment/params/<NAME>/` (empty when nothing sets it) for a compose service to mount; the job's steps are the stages
  that apply; and each step's `tests/env.sh` carries `AUSPEX_AXIS_*` and `AUSPEX_PARAM_*`. A parameter is never put
  in the agent's environment, since it can say what is planted, and a secret parameter reaches neither the image nor
  the verifier yet. `auspex build` on such a configuration compares what the image build is handed (the compiled
  `environment/`) and builds no image, so what a Dockerfile step does with an argument is held to account by a check in
  the verifier (`fixtures/toolchain`'s `toolchain-built`), not by `build`. An image that cannot be built loses the
  try, and its reason is the build's own error (`Ingest.Said`).
- **A vendor's agent, or `route: harbor`, runs through Harbor; nb without a
  route is run bare.** A value's `agent` is a vendor agent (`claude-code` on
  Anthropic models, `codex` on OpenAI's, `qwen-code` on Qwen's), with a pinned
  `agent_version` and a bare model of the agent's vendor, `provider` refused;
  nb names `harness`, `provider` and `model`, and `"route": "harbor"` makes it
  a Harbor agent. The plan is
  `project/plans/agent-neutral-runs.md`; what Harbor writes is
  `project/learnings/harbor.md`. `run` compiles each configuration once
  under `log/<id>/harbor/tasks/<configuration>/<exam>/`, one Harbor task whose
  steps are the stages that apply: each prompt as an `instruction.md`, the fixture's `repo/` (and only it, with the values' files)
  committed in the image `fixture.image` describes, the agent at its
  pin, and per step `tests/` with the exam, the fixture minus `repo/`, and a
  `test.sh` running every script check in Harbor's separate, offline
  verifier (one `reward.json` key per check). The compiled `task.toml`
  gives `[environment]` the same network policy as `[agent]` (the model's
  host allowed, or `no-network`), never `public`: Harbor applies a
  phase's policy only during the phase, so under a public environment a
  process the agent left running had the internet once its phase ended
  and between stages (`HarborCompile.Network`). A bridged task (an
  answer sheet) is the exception and stays `public`: Harbor installs
  acpx from npm in the container on every trial. A vendor agent reaches the
  host of `auspex.json` `agents.<agent>.base_url`, exactly as spelled, its
  key in Harbor's process as `ANTHROPIC_API_KEY` or `OPENAI_API_KEY`; nb
  reaches the host of its provider's `Endpoint` in nb's config, or nothing
  (Mock), its endpoint `${VAR}`s passed by value and its key by name. Each
  try is one Harbor job in the try's `harbor/`; `Ingest` lifts each step into
  its stage's attempt and writes `core.json`, auspex's own count, strictly: an
  unknown tool or event, a version other than the pin, or no trajectory
  fails it. An agent timeout is a completed attempt with `time_budget`;
  a vendor agent that took no turn at all is failed, since its model never
  answered (Codex retries an unreachable endpoint until the timeout).
  `grade` reads a Harbor attempt like any other: `Transcript.Read` takes
  `trajectory.json` when there is no `transcript.jsonl`, with `core.json`
  as the trailer (so a budget check reads auspex's count, never the
  agent's) and the main agent's conversation as the windows (a subagent's
  calls are counted, not listed). A script check is not rerun on the host:
  its verdict is the attempt's `reward.json` key and its reason the first line
  of `verifier/<check>.out`, a missing key `error`. So it is fixed when the
  try is made: `HarborCompile.Outdated` compares the run's compiled
  `tests/` (the check's `test.sh` line, every file of the exam and fixture
  copies but `exam.json`, `combinations.json`, `retired.json`, `predictions/*.json`,
  `fixture.json`, the exam's and the fixture's own `README.md`, and another check's script, and `AUSPEX_EXPECT` in
  `env.sh`) with what compile would write now, and a difference, a check
  the run never compiled, or no compiled copy makes the verdict
  `outdated` (`Verdict.Outdated`), whose reason ends "make the attempt
  again". An outdated correctness check makes the stage undecided (`Grade.Pass`
  null, `ResultRow.Outdated`), out of every number like `needs-judge`; `grade`
  prints `Grade.OutdatedSummary`, which names the tries' directories to move
  out of the bank so that `run` makes them again. `loop_nudged` is
  `error` on a vendor agent's attempt, since only nb nudges; a script check
  that reads `AUSPEX_TRANSCRIPT` has none in the verifier.
- **nb in Harbor is the host's nb.** `auspex.json` `harbor.nb` is nb's
  self-contained linux-x64 publish; `run` builds it once into a
  `auspex-nb:<content hash>` image that each nb task's Dockerfile copies
  `/opt/nb` from, and refuses a publish whose `nb --version` is not the
  host nb's, since the host compiles the program (per stage, `{{work}}` as
  `/app`) and the image runs it. The agent (`Harbor/nb_agent.py`) is on
  Harbor's `PYTHONPATH` from `log/<id>/harbor/agents/`. nb writes its own
  ATIF to `agent/trajectory.json` (`--trajectory`), beside its JSONL
  (`agent/nb.jsonl`, with `nb.stderr.log` and `nb.exit`); ingest takes it
  as a vendor agent's. Bare stages get one too (`NB_TRAJECTORY`, so a runner
  script passes it on), though grading reads their JSONL. `NbTrajectoryTests`
  holds every `fixtures/nb-run` pair to reading the same both ways. The gate
  (`exams/gate`, `GateTests`, decision 12 of `agent-neutral-runs.md`) is opt-in:
  `AUSPEX_GATE=<publish dir> dotnet test --no-build --filter GateTests`,
  about five minutes on podman (`AUSPEX_GATE_ENGINE=podman`; Docker without it)
  and uv needed; never rebuild while it runs. The filter takes `OracleGateTests` too (`exams/gate-oracle`,
  about four minutes). The publish must have `--acp`, so republish after an nb
  change the bridge sees.
- **Harbor's engine is the machine's.** `auspex.json` `harbor.engine`
  (`docker`, the default, or `podman`) is Harbor's `environment.type` in
  every try's job file and the CLI nb's layer is built with; podman's
  local tag is `localhost/auspex-nb:<hash>`, spelled out so the compiled
  `COPY --from` resolves no short name. The log entry records it. Podman needs `podman.socket` running (Harbor's
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
  provider's endpoint) whose host is loopback means the machine that runs auspex (`HostRelay.Loopback`), unless
  the fixture's own services answer on that port (`image.serves`, as `fixtures/stub` declares 8765). A fixture
  names further ports in `image.host_ports`. `Runner.Relay` works out a run's ports and refuses when one does not
  answer; `HarborCompile.Task` writes `auspex-relay.py` and the `auspex-relay` service into the task's compose
  file (creating one for a fixture with no services, so `NET_RAW` is dropped there too) and compiles the policy
  with no host for a relayed model (`HarborCompile.Reached`), so the agent's phase is `no-network`. `Make` holds
  the host end while tries are made, at the directory the log entry recorded (`harbor.relay`). The relay passes bytes and holds no key. Never open the
  host's loopback to containers in its place. Not on a remote engine. A fixture's `image.allowed_hosts` join the
  model's host in the allowlist (`HarborCompile.Hosts`), and `run` warns, never refuses, when one of them is inside
  the engine's default container range (`HarborRun.SubnetWarning`). The plan is
  `project/plans/container-network-finish.md`; what was measured is `project/learnings/harbor.md`.
- **The engine is on this machine; a second machine file picks another.** `--config <file>` on `list`, `run` and
  `grade` reads that file in place of `exams/auspex.json` (relative paths still against `exams/`;
  `AuspexConfig.Source` is what messages call it). Modal, an engine off the machine, was taken out of the core on 2026-11-27 (`project/plans/remix.md`):
  `harbor.engine: modal` is refused with where it went (`HarborConfig.GoneWhere`), the code is at the git tag
  `modal-in-core`, and its plan, guide and example file are in `project/archive/` (`modal-runs.md` lists what it
  touched). The path for a remote engine went with it, having no other user.
- **A check of the layer costs nothing.** Auspex checks that it drives
  Harbor and reads back what Harbor wrote, never Harbor itself and never a
  model: the unit tests, the gate (nb on Mock) and conformance (each vendor
  agent on the stub) cover it, with no hosted model, judge or gateway. A
  hosted model runs only in an exam whose question is that model,
  its spend agreed first. `AUSPEX_CONFORMANCE=1 dotnet test --no-build
  --filter ConformanceTests` runs the stub tries and the oracle pass (each
  `code-change` stage's `reference/<stage>.sh` as Harbor's `solution/`,
  every script check 1); podman (or Docker) and uv needed, about twenty minutes, $0.
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
  so auspex brings its own (`Harbor/qwen_code_agent.py`, imported like
  nb's), which fails setup when the image's version is not the pin. Harbor
  runs both with approvals and sandbox bypassed (it has no other mode), so
  `denied` is 0 by construction, as under Claude Code's
  `bypassPermissions`. No vendor agent sees the program template: its
  instruction is the stage's prompt. `auspex.json` `agents.codex.config` is a native
  Codex `config.toml` (relative to `exams/`) that Harbor merges its base
  URL over: what an endpoint needs of the agent, such as a llama-server,
  which takes only function tools (`web_search = "disabled"`,
  `[features] multi_agent = false`).
- **What is live is on the axis, and the comparison is along one axis.**
  `"live": "<value>"` on an axis is part of `exam.json`; there is no mark
  file and no verb. `Configurations.Compare` is the one place a value is
  compared with another: against its axis's live value (or `--against`),
  every other axis held, try against try by Mann–Whitney on the correctness
  checks held and on flagged steps, in each setting of the other axes and
  across them by van Elteren, "better here, worse there" when settings
  disagree. The report, `compare` and the viewer read that; nothing else
  compares. Fewer than 4 tries a side (or 3 against 5) can never confirm
  (`Tries.MinP`), and `run` says so.
- **Every id in the report carries a sentence.** `description` is the one
  key in a check spec that is not a check (`Checks.Description`); a script
  check must declare one, a built-in derives one from its fields
  (`Checks.Describe`). An exam's `description`, a stage's `about`, an axis's
  `about` and a value's `note` and `why` travel in `stats.json`; a stage
  falls back to the first prompt line (`Exam.Describe`). `grade` writes each
  check's description, `why` and script path into every verdict.
- **An undecided stage is a third outcome.** `Grade.Pass` is `bool?`:
  false when a correctness check failed, errored or is missing; null when nothing
  failed but a check is `needs-judge` or `outdated`. A check that
  cannot decide is the check's weakness, not the configuration's: the try's
  stage reads `undecided` in the report (`Tries.cs`), never wrong.
- **Every number is computed once, in `Report/Configurations.cs` and `Report/Tries.cs`.**
  `summary.md` and the viewer format; they never compute. If a number looks
  wrong, fix it in `stats.json` first. The unit is the try: each gives the
  correctness checks it held, of all that apply in its configuration, and
  its flagged steps. There is no HTML page: the viewer (`View/`) is.
- **Version and changelog.** `<Version>` in `Auspex.csproj` is what
  `--version` prints and `versions.auspex` records. Before 1.0 a minor bump
  is a breaking change to the layout, a file format or the vocabulary, and
  `CHANGELOG.md` says what broke and what still reads the old form; bump
  both in the same change.
- **`versions.nb` is `nb --version` from the host binary**, the `+commit`
  suffix stripped; `unknown` when it fails. It is read once per run.
- **nb's trailer carries `duration_ms` since nb 0.9** (nb f7df67f); the
  `max_duration_ms` check reads it and falls back to the attempt's wall time
  from the manifest. It carries no cost, so an nb attempt has none.
- **nb emits no `assistant_json` event**; the `answer_json` window is the last
  ` ```json ` fence in the last assistant message.
- **Program templates.** Placeholders are `{{prompt}}`, `{{stage}}` (the
  stage's id), `{{work}}` (the fixture checkout), `{{provider}}`, `{{model}}`,
  `{{harness}}`, `{{configuration}}`, `{{try_number}}`, `{{axis.<name>}}` and
  `{{param.NAME}}` (a tree parameter as its directory). `{{work}}` is the path the *model* will
  see: the exam's `nb.mounts` replaces it when a runner is in effect, and a bare run ignores the mounts so
  `--runner none` stays a host shakedown. A prompt's newlines become nb continuation lines (` \`), so a
  multi-line prompt stays one directive. A prompt line that itself ends in a
  backslash cannot be expressed.
- **The judge is auspex's own client, never nb.** `decide` posts to a
  `systemone` endpoint (the System One request, its `model` sent when the
  judge names one; Jev on OpenRouter's `/alpha/decisions` through a local
  gateway today; the wire format is the seam, not the vendor). Its answer
  is read unwrapped, `{model, answers, usage}`, as
  `Auspex.Tests/fixtures/openrouter-decisions.json` captured it, and its
  POST is hand-rolled on purpose: Microsoft.Extensions.AI has no decision
  abstraction and no .NET System One client is worth the dependency.
  `judge` goes through
  Microsoft.Extensions.AI's `IChatClient` over an OpenAI-dialect endpoint,
  so a provider change is a config line. Both are `Verdict`s like any
  check. The question is the exam's (the check spec), the endpoint is the
  machine's (`auspex.json` `judges`), and the key and endpoint are
  resolved at grade time only (`${VAR}` in both), so `list` and `run`
  need no key. Verdict files and the report keep the endpoint as written,
  so neither names the machine that graded. `Exam.Load` resolves each
  model check's judge when handed the config's judges; the test helper
  passes none, and `grade` resolves again itself.
- **A model check sees a window, never the transcript.** `Window.Known`
  is the whole vocabulary; an empty part or a total over the judge's
  `max_window` is `error` before any call, because a decider answers an
  empty or cut state confidently. The configuration, the model's name and the try's number are
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
- **Calibration is the bench's, not auspex's.** A threshold or a rubric
  earns a place in `grading.correctness` by the numbers in `project/plans/jev-trial.md`;
  auspex records which judge, model and prompt hash graded an attempt (the
  report's reproducibility section) and carries no kappa.
- **Check values from the stage.** A built-in field whose value is
  `"@expect"` reads `expect.<check name>.<field>` from the stage, then
  `expect.<field>`; a stage with neither yields `error`, never "does not
  apply", since a stage that wants to opt out declares its own checks. A
  model check's `expect: "@expect"` reads `expect.<check name>`, which is
  the expectation itself or `{ask, expect}`; `ask: "@expect"` reads the
  `ask` from there, so a shared `correct` check carries a per-stage rubric,
  and the verdict hash makes it a per-stage verdict file.
- **Hooks and script checks** run with `AUSPEX_EXAM_DIR`, `AUSPEX_FIXTURE`,
  `AUSPEX_EXAM`, `AUSPEX_CONFIGURATION` (the key), `AUSPEX_AXIS_<NAME>` and
  `AUSPEX_PARAM_<NAME>`, `AUSPEX_STAGE` (the stage's id), `AUSPEX_TRY_NUMBER` (the try's number),
  `AUSPEX_ATTEMPT_DIR`, `AUSPEX_WORK`, `AUSPEX_STAGE_JSON`, `AUSPEX_EXPECT` (the
  merged block), `AUSPEX_TRANSCRIPT`, `AUSPEX_DIFF`, `AUSPEX_RUNNER` (the
  runner script in effect, empty on a bare run), `AUSPEX_NB` and
  `AUSPEX_NB_CONFIG` (the host binary and its resolved config, which a
  hook mounts for the runner), `AUSPEX_CONTAINER` (`auspex-<exam>-<try id>`,
  which auspex never uses), and `AUSPEX_WORK_MOUNT` (the path the model was told; the host path
  unless `nb.mounts` moved it). `hooks.configuration` runs once around a
  configuration's tries in a run (and in `build`), `hooks.try` once around
  each try (`try.setup.log`, `try.teardown.log`); `hooks.arm` and `hooks.sample`, proctor 0.3's names for them, are refused with the new ones, and a `try` hook is refused
  where a value runs through Harbor. Hooks run in the exam
  directory; script checks run in the stage's attempt, an exam's resolved against the
  exam directory and a fixture's against the fixture directory. Scripts exit
  0/1/2 for pass/fail/needs-judge; anything else is `error`, never folded
  into fail. A try's order is: fixture checkout, the values' files, setup hook,
  commit, then per stage restore (unless chained), nb, diff; then the teardown
  hook. Teardown runs whenever setup ran, even when setup failed, so
  it must be idempotent. `AUSPEX_WORK` is never deleted (`.auspex/` is
  gitignored).
- **nb gets the program on stdin, compiled on the host, bare or through a
  runner.** Each stage's resolved source is written as `program.nb`; auspex then runs
  `nb --compile` on the host binary in the exam directory (so `@file`
  includes resolve against the exam) and writes the JSONL as
  `program.jsonl`; that is what goes down stdin, so the container never
  holds a path to a sheet. `program_hash` stays the source's. A program nb
  refuses loses the try. `RunNb` is one
  code path: without a runner it starts `nb --output jsonl [--config] -`
  itself with only `NO_COLOR` added to the environment; with one it starts
  the script with nothing on argv and the stage's environment plus `NO_COLOR`,
  and the script starts nb the same way. The bare path deliberately does not get that environment:
  `AUSPEX_EXPECT` in the model's reach would leak the answer. A runner that
  forwards its environment into the container wholesale would do the same.
- **The machine's config is never committed.** `exams/auspex.json` and
  `exams/nb.json` are gitignored; `*.example.json` beside them are the
  committed, host-neutral forms (`${LLM_GATEWAY}`, `${LLM_GATEWAY_KEY}`), and
  `run` says to copy them when the machine's own file is missing. The tests read
  `nb.example.json` for its Mock providers, so keep those in it.
- **The runner is the exam's, the binary is the machine's.** `exam.json`'s
  `nb` block (`runner`, relative to the exam directory like a hook, and
  `mounts`) says how the exam runs nb, because the runner only works with
  the hooks that make its container; `exams/auspex.json` says only where
  the host binary and its config are. `--runner` overrides for one run. A
  `runner` or `mounts` left in `auspex.json` is reported as a problem, not
  ignored.
- **Labels are the consumer's; auspex has no tags.** `labels` on an exam
  or a fixture is a key with one or more string values that auspex
  stores, prints, filters on (`list --label k[=glob]`) and records on every
  `results.jsonl` row, never interprets; the fixture's sit under the exam's,
  key by key. A stage has none (refused: "the exam's, not a stage's"), so a
  per-stage kind is lost with tasks; `exams/nb-changes/README.md` keeps its
  in a table. A `tags` field is reported as removed.
  Whether an exam is judged is `Exam.Judged`, derived from a check naming
  `judge` or `decide` (`Checks.AsksAModel`), not declared.
- **A value is declared data, and its hash is its identity.** A value's hash
  covers what it sets and carries, its files by content, and not its `note`
  or `why` (`Axes.ValueHash`); a configuration's is its values' together. The
  job's hash covers what the agent meets: every stage's id and prompt in
  order, an oracle's answer sheet, the program template, the hooks and the
  fixture (how it is built and its source by content), not the checks, their
  scripts or the expected values, which are the standard and so a regrade
  (`Exam.JobHash`). Each stage's manifest records the exam, the configuration's
  key and hash (`configuration`), the stage (`stage`), the try and `log`
  (the entry of the run that made it); the log entry keeps `exam_hash` (the
  definition itself is under `exams-seen/`, once per hash), each
  configuration it made tries of with its values as they stood, its stages,
  and the tries it set out to make, each with its id. `run` refuses a value
  whose name made tries under another content, saying which configuration's
  (`Runner.RefuseChangedValues`). Hooks are the exam's; one that branches on
  `AUSPEX_AXIS_*` to change what the agent sees hides it from the hash.
- **An exam's tries are its bank; `run` makes what is lacking.** The bank is
  every try every run of the exam set out to make (`Bank.Of`, which reads the
  log entries' lists, so a directory no entry lists is not seen). A try is
  current when the configuration's and the job's hashes it was made under are
  today's, and stale otherwise (`Bank.Stale`, which says which moved). Per
  named configuration, missing is the exam's `tries` less its current
  tries that were not lost (`Bank.Missing`); a new try is numbered after
  every try of that configuration, and filed under an id of its own,
  `bank/<exam>/<try id>/`. Nothing is continued: a try whose run died, or
  that lost a stage, is made again whole by the next `run`, and its
  directory stays. `grade`, `friction` and `report` read the current tries
  and list the stale ones; a new or changed check is a regrade of every
  current try, never a rerun, except a script check through Harbor, whose
  verdicts go `outdated` (above) until the tries are made again.
- **Reports are stamped and kept.** `report <exam>` writes
  `reports/<exam>/explore/`; one already there is moved into a directory
  named by its build time beside it. `stats.json` `built_from` is the
  stamp (`Stamp.Of`), which also says when one configuration's tries came from runs under different conditions.
- **Validation says "not yet" rather than silently skipping**: `answer_json_schema`, `max_cost`, a secret
  parameter through Harbor. A stage's
  `oracle` runs on nb only: bare, as nb's in-process oracle (its
  directives prepended by `Runner.OraclePrefix`); on Harbor, as
  `Harbor/oracle_user.py`, the simulated user, whose record ingest lines up
  with nb's trajectory (`Ingest.Oracle`). A vendor's agent beside a stage with a sheet
  is refused: Harbor leaves it no ATIF. A try with an answer sheet goes through Harbor only when it is
  one stage (`HarborCompile.Plain`): it is then compiled as a plain Harbor task, with no steps, and run by the
  single-task path into the try's layout (`Runner.RunAgentTask`), since the simulated user answers a task and never
  a step. A try of several stages with a sheet among them is refused through Harbor, and says to give it one stage
  (a stage's `when`) or to run it bare. An oracle's turn is a user step whose `extra.source` is
  `oracle`, with the sheet entries it answered from in `extra.keys`, the same bare and bridged; a question the oracle
  did not answer leaves no turn, only the ending (`oracle_miss`, `oracle_budget`), which through Harbor is in
  `core.json` and not in nb's own trajectory. The viewer shows the turn as the oracle's and says the ending.
- **Setup writes no model and no judge.** A model is the exam's choice, so
  nb.json entries carry none and a judge or classifier job records only its
  provider; the agent writes the `judges` entry from the models
  `setup --check` lists. Keys go to the env file at a secret prompt and
  never through an agent; Spectre is for interactive verbs only.
- **The earlier words are no longer gated** (the user, 2026-11-28). `project/gate/old-words.sh` searched every
  tracked file for them until then, and kept the two models from being mixed while both were in the code. After
  slice 7 a review found none left as a concept, and the gate was bending ordinary sentences, so it was removed
  (it is in git history). The vocabulary table above is the rule: for the things it names, use its words. Sample,
  batch, suite and the like are plain English wherever they mean something else, and a comparison's `cell` (one
  setting of the other axes, `answer/1`) is its own word.
- **Built to be built on** (`project/plans/remix.md`, 2026-11-27). Other people's code stands beside auspex: it reads
  the files and calls the commands. Four rules keep what is built ready for that, before anything is promised: a new
  file a person or a program may read carries a schema name and number (`answer/1`, `configurations/1`); a new verb can give
  its answer as JSON and says what its exit codes mean; nothing new under `exams/` or `fixtures/` means something on
  this machine only (that belongs in `auspex.json`); and no new branch on `harbor.engine` outside `Run/Harbor.cs`,
  `Harbor/Compile.cs` and `Exam/Exam.cs`'s `HarborConfig`. The engine that is supported is rootless podman on Arch
  and Ubuntu; Docker is not maintained, macOS is not run.
- **The runner tests are slow** (nb takes ~2 s to start per stage); filter them
  out while iterating on anything else: `--filter 'FullyQualifiedName!~RunnerTests'`.
