# proctor

Runs a suite's arms, grades the transcripts with declared checks, computes
the statistics once, and renders one self-contained HTML report plus a
markdown twin.

An arm is either [nb](https://github.com/breitreiter/nb) run bare on this
machine, or an agent run in a container by
[Harbor](https://github.com/harbor-framework/harbor). Harbor's agents are
Claude Code, Codex, Qwen Code, and nb itself.

**Proctor leans on Harbor and ATIF.** Harbor runs a trial and returns its
evidence; proctor decides what that evidence means across a suite. Every
trial comes back as an ATIF trajectory (Harbor's agent trajectory format,
v1.7), and ATIF is the format the grader reads. An nb cell under Harbor has
its JSONL converted to ATIF. A bare nb cell is still read from its JSONL,
and a test holds every captured transcript to reading the same either way.
A shared format is not yet comparability, so proctor counts turns, tool
calls, denials and how the run ended itself, by its own definitions, from
the ATIF steps. It never takes an agent's word for its own numbers.

The direction is anything with tools through Harbor. The bare path is for
tool-less programs and for shaking a suite down, where it needs no
container and no Python. nb is to write ATIF itself, which would make the
two paths identical by construction. The reasoning, and the costs accepted
for it (a Python dependency and a fast-moving upstream), are in
`project/plans/agent-neutral-runs.md`.

## Install

Build nb first (`dotnet build` in its repo), then:

```bash
dotnet build
alias proctor='dotnet /path/to/proctor/bin/Debug/net10.0/proctor.dll'
```

To run this repository's own suites, copy `suites/proctor.example.json` to
`suites/proctor.json` and `suites/nb.example.json` to `suites/nb.json`, and
point their routes at your gateway (see below). Both copies are gitignored,
since they name your machine.

## Configure

A repository that uses proctor has `suites/` (yours, in git), `runs/` (raw
output, gitignored) and `reports/` (derived, written by `proctor report`).

The words, which the report and every file use the same way:

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

A suite is one business goal probed by many tasks. Every task in it is
graded by the same pass list. If you find yourself wanting a different
pass list for one task, or a check that only makes sense on some tasks
and passes vacuously on the rest, you have two suites. If you want to
ask a second question of the same transcripts, you have a new check,
not a new suite. The prompt is what the user wants, in the user's words;
the task's checks are how proctor measures whether they got it.

`suites/proctor.json` says where nb is and which config it runs with; paths are
relative to `suites/`:

```json
{ "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" } }
```

Without it, `nb` is taken from `PATH` and nb resolves its own config.
`--nb <path>` overrides either.

The same file names the judges that the model checks call at grade time,
by wire shape rather than vendor. A `systemone` judge answers typed
questions with probabilities (`POST /v1/systemone`: Jev on Cloudflare, or
anything that speaks it); a `chat` judge is an OpenAI-dialect chat
completions endpoint with a model name. Endpoints and keys may be `${VAR}`
references, resolved only when `grade` runs. This repository commits only
`suites/proctor.example.json`, which expects `LLM_GATEWAY`, the base URL of
an OpenAI-dialect gateway that serves the routes it names, and
`LLM_GATEWAY_KEY`, its key; your `suites/proctor.json` is gitignored, so the
routes you point it at stay on your machine. nb resolves the same references
in its own config, which is how `suites/nb.json` reaches the same gateway.

```json
{
  "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" },
  "judges": {
    "jev": { "kind": "systemone", "endpoint": "${LLM_GATEWAY}/cf/workers-ai/run/typesafe/jev", "api_key": "${LLM_GATEWAY_KEY}", "family": "typesafe" },
    "glm": { "kind": "chat", "endpoint": "${LLM_GATEWAY}/cf/compat", "model": "workers-ai/@cf/zai-org/glm-5.2", "api_key": "${LLM_GATEWAY_KEY}", "family": "glm" }
  }
}
```

`family` is only for the reminder `grade` prints when an arm's model
carries a judge's family name; a judge should be off-family from every arm
it grades. `max_window` (characters, default 24,000) caps what a judge is
sent; a larger window is an error, never a truncation.

A judge that is rate-limited (429, 503 or 529, or a rate-limit message in
the body) is retried three times, honouring `Retry-After`. If it keeps
refusing, or asks for a wait of more than a minute, it is stopped for the
rest of the pass. Every later check on it is then `error`, with the
endpoint's own words and no further requests. Nothing is lost: the next
`grade` asks every `error` again.

nb's runner, below, is the older way to put a bare nb arm in a container.
For a new suite with tools, prefer an agent arm (`agent: nb` for nb
itself; see *Agent arms*).

A suite that runs nb inside a container names the script that runs it in
its `suite.json`, beside the hooks that make the container;
`--runner <script>` (a path from the current directory) overrides it for one run and
`--runner none` runs bare. The contract is the whole interface:

| proctor gives the runner | the runner must |
|---|---|
| stdin: the resolved program, compiled on the host by `nb --compile` so every `@file` include is inlined | pass it to nb's stdin unchanged |
| cwd: the work directory on the host | start nb with the checkout as its working directory, wherever that is inside |
| the cell environment (`PROCTOR_*`, which includes `PROCTOR_NB`, the host binary, `PROCTOR_NB_CONFIG`, `PROCTOR_WORK_MOUNT`, `PROCTOR_BUNDLE_MOUNT` and `PROCTOR_CONTAINER`) and `NO_COLOR` | run nb where the checkout, the bundle and the config are at the paths those name |
| nothing on argv | run `nb --output jsonl [--config <config>] -` |
| stdout and stderr captured to the cell | put only nb's stdout on stdout |
| | exit with nb's exit code |

`PROCTOR_CONTAINER` is a name derived from the cell that proctor never uses,
so hooks and the runner can agree on one container: the sample setup hook
creates it, the runner execs into it, the sample teardown hook removes it.
Hooks see which is in effect in `PROCTOR_RUNNER`, empty on a bare run, and
skip the container. The manifest records the script and its hash, and
`resume` refuses a changed one. The worked example is
`suites/runners/container.sh` with the `suite-authoring` suite's hooks and the
`Containerfile` beside the runner, which puts nb's own image
(`podman build -t nb .` in the nb repository) on the .NET SDK:

```bash
proctor run suite-authoring                 # each cell in its own container, as suite.json says
proctor run suite-authoring --runner none   # bare: the shakedown, on this machine
```

A bare run is for shaking down a suite: nb and the model's tools run on this
machine as you. A container is for anything you would not run on your own
machine, which is every real suite, since the model runs whatever it decides
to. How to build the image, what to mount, who owns the files the model
writes, and what nb leaves within its reach is nb's runbook,
[`docs/containers.md`](https://github.com/breitreiter/nb/blob/HEAD/docs/containers.md); the example above is that
runbook applied. Each cell keeps `program.nb`, the source as resolved, and
`program.jsonl`, what actually went down stdin.

The suite's `nb` block names the runner, relative to the suite directory like
a hook, and `mounts`, where the runner will show nb the checkout and the
bundle:

```json
{ "nb": { "runner": "../runners/container.sh", "mounts": { "work": "/work", "bundle": "/bundle" } } }
```

With a runner in effect, `{{work}}` and `{{bundle}}` in the program resolve
to those paths, so the model is told where things are inside the container.
Hooks and checks run on the host and keep `PROCTOR_WORK` and
`PROCTOR_BUNDLE` as host paths; `PROCTOR_WORK_MOUNT` and
`PROCTOR_BUNDLE_MOUNT` are the paths the model was told, which fall back to
the host paths when nothing is mounted, so a check such as `stays-in-work`
reads one variable either way. A bare run ignores the mounts, which is what
makes `--runner none` a shakedown of the suite on the host. The plan and the
worked example are in `project/plans/containerised-runs.md`.

One suite is one directory, `suites/<id>/`:

```
suite.json        description, labels, arms, samples, nb (runner, mounts), hooks, grading (checks, pass, validity)
program.nb       the nb program template; {{prompt}}, {{task}}, {{work}}, {{bundle}}, {{provider}}, {{model}}, {{harness}}, {{arm}}, {{sample}}
tasks/*.json     one task per file; the id is the file name; names a fixture and adds the prompt, a description, labels, expect and its own checks
checks/*.sh      script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason); each needs a description
hooks/*.sh       arm and sample setup/teardown
```

A fixture is the repository a task is run against, with what "done" looks
like in it. Fixtures are repo-level, `fixtures/<id>/`, and reused across
suites:

```
fixture.json     id, source ({path} or {git, rev}), stack, labels, default expect, checks (scripts with descriptions)
checks/*.sh      the fixture's own checks (builds, tests pass); merged into every cell run on it
repo/            the checkout source; the only thing copied into the work directory
```

An arm may name a `bundle`, the pinned version of whatever it puts under
test: `{ "path": "bundles/x" }` under the repository root, or
`{ "git": url, "rev": sha }`. Proctor hands its directory to the program
template as `{{bundle}}` and to hooks and checks as `PROCTOR_BUNDLE`, and
records its identity in every manifest; what is inside it is the suite's
business. Two arms with two bundles compare them in one experiment.

Proctor checks the fixture out into the work directory before each sample,
commits it, and collects `diff.patch` afterwards.

Checks live at three levels and a cell carries the union: the suite's
`grading.checks` on every task, a fixture's `checks` on every task run
against it, and a task's own `checks` on that task alone, for the outcome
only it can state. A name declared at two levels is a problem. A check
named in `pass` that the suite does not declare must reach every task from
its fixture or its own block. A check named in `validity` decides whether a
sample counts at all: a sample that fails one is excluded, not failed. A
task's check is reported over the tasks that carry it.

```json
{
  "id": "make-bat-implement-ifoo",
  "fixture": "bat-service",
  "prompt": "Make `Bat` implement `IFoo`. Its existing callers must not change.",
  "expect": { "files_touched": { "paths": ["src/Bat.cs", "tests/**"], "mode": "at_most" } },
  "checks": {
    "callers-untouched": { "not_files_touched": { "paths": ["src/Consumers/**"], "mode": "at_least" }, "description": "no file under src/Consumers was changed" }
  }
}
```

The report is written for a reader, not a grader, so every id it prints
carries a sentence beside it. A suite, an arm and a task take an optional
`description`; a task without one is described by the first line of its
prompt. A check takes `description` as a field beside its spec, and a
script check must have one, because from outside a script says nothing:

```json
{ "acceptance": { "script": "checks/acceptance.sh", "description": "the task's acceptance tests pass against the changed repository" } }
```

A task can name an **answer sheet**, a scripted user that answers when the
agent stops to ask: `"oracle": "oracle/<task>.md"`, a file inside the suite
directory in nb's format (`## <id>`, then the answer, verbatim). After a turn
that ends `ok`, a judge reads the agent's last message against the sheet. On a
hit the selected answers become the next user turn and the run goes on; when
the agent asked for something the sheet lacks, the run ends `oracle_miss`.
`suite.json`'s `oracle` block sets how many answers a run may get (`turns`,
default 8, then `oracle_budget`) and which nb config entry judges (`judge`;
by default the arm's own provider). The checks are `oracle_hit` (a list of
entry ids the oracle must have answered with), `oracle_turns {max}` and
`oracle_misses {max}`. A bare nb arm runs nb's own oracle. An `agent: nb` arm
runs in Harbor with a simulated user on the host, which sends the task and
each answer to nb over Harbor's ACP bridge and judges with the host's nb, so
the sheet never enters the container; its program must end in exactly
`run {{prompt}}`. A vendor agent arm beside an oracle task is refused, because
Harbor leaves a bridged vendor agent no trajectory to grade
(`project/plans/oracle-in-proctor.md`).

A built-in check describes itself from its spec
(`{ "denied_calls": { "max": 0 } }` reads as "no denied tool calls") unless you give it a better sentence.

The report is built for someone who was not there, and for someone who opens
one every few months as much as every day: it defines arm, task, run and
check before using them, and every section opens with a sentence saying what
it is for. Those definitions are set in muted grey so the findings stand
out once you know the drill. A rail down the left lists every section and
task and follows you as you scroll.

It starts with the suite's question and a verdict card: for each arm,
better, worse or unchanged and by how many points, whether the 95% interval
confirms it, the tasks that moved it, and, when it cannot be confirmed, how
many tasks it would take. The card says so if an arm lost more runs than
another. Then Arms, and Tasks: one card per task with its prompt, its
fixture, the checks it carries (its own first, then the suite's), and every
arm's runs, score and verdict on it, followed by each run that did not pass
and why. Results holds the numbers across all tasks (pass rates, the
comparison between arms, the baseline), then Failures: per arm, each check
that failed, in the author's words, with how often and on which tasks.
Everything after that is evidence: what ran and what was left out, check
pass rates, cost, every run, and the method last. A run whose headline check
could not decide (a script's exit 2, a split judge) is *undecided*: out of
the pass rate on both sides, listed for review when there are a few, and
flagged as a defect in the check when there are many.

Labels are yours: a key with a string or a list of strings, on the suite,
the fixture or the task, and proctor never interprets a key. A task carries
its fixture's labels, the suite's laid over them and its own over both, key
by key. They print with `list`, filter it (`--label kind`,
`--label area=coding/*`, repeatable; `*` matches within a slash segment and `**`
across), and sit on every row of `results.jsonl` so a later report can group
on them without re-reading a suite that has since changed:

```json
{ "labels": { "area": "coding/change", "stack": "dotnet" } }
```

Two checks ask a model, and both see a *window*, never the transcript:
named extractions (`answer`, `answer_json`, `prompt`, `tool_calls`,
`tool_results`, `diff`, `user_turns`) joined with `+`. `decide` asks a
systemone judge one typed question and reads the probability: yes/no
without `options`, one label of several with them. `judge` asks a chat
model a criterion, several times at temperature 0, and reads a label with
verbatim evidence; every quote is checked against the window, a sample with
an unverifiable quote is discarded, a split or an `unknown` is `needs-judge`,
and the reasoning stays in the cell's `verdicts/` file and never reaches the
report.

```json
{
  "stance":      { "decide": { "ask": "What does the closing message claim?", "window": "answer",
                               "options": { "complete": "it presents the work as finished", "asked": "it stops to ask", "blocked": "it could not finish" },
                               "expect": "complete", "threshold": 0.95 } },
  "ran-tests":   { "decide": { "ask": "Did the agent run the project's tests before its closing message?", "window": "tool_calls+answer" } },
  "change-fits": { "judge":  { "ask": "Does the diff change only what the prompt asked for?", "window": "prompt+diff", "samples": 3 } }
}
```

`expect` may be `"@expect"`, read from the task's `expect.<check name>`;
so may `ask`, read from `expect.<check name>.ask`, so one `correct` check
can carry a rubric per task. A built-in field set to `"@expect"` reads
`expect.<check name>.<field>` first and `expect.<field>` second.
`with` names a judge from `proctor.json`; without it the only judge of the
needed kind is used. Whether a suite is judged is not declared; it follows
from a check that asks a model, and `list` says so.

### Agent arms

An arm that names an `agent` runs through
[Harbor](https://github.com/harbor-framework/harbor), in a container built
from its fixture's image, instead of running nb on this machine. The agent
is a vendor's own CLI at a pinned version (`claude-code`, `codex`,
`qwen-code`), or `nb` itself:

```json
{
  "arms": [
    { "id": "cc-haiku", "agent": "claude-code", "agent_version": "2.1.301", "model": "claude-haiku-5-5" },
    { "id": "floor", "agent": "nb", "harness": "nb", "provider": "local-qcoder", "model": "qwen3-coder-next" }
  ],
  "harbor": { "version": "0.24.0" },
  "limits": { "wall_ms": 1800000 }
}
```

The machine needs [uv](https://docs.astral.sh/uv/), which runs the pinned
Harbor, and a container engine, set by `harbor.engine` in
`suites/proctor.json`:

- **Docker** (the default), with the Compose and buildx CLI plugins.
- **Podman**, rootless, with its API socket running, docker-compose v2
  behind `podman compose`, and three lines of user config. The setup,
  and why each step is there, is [`docs/podman.md`](docs/podman.md);
  `run` checks what it can before it starts.

The same file says where each vendor agent's model calls go and where nb's
self-contained linux-x64 publish is, for an `agent: nb` arm:

```json
{
  "agents": { "claude-code": { "base_url": "${LLM_GATEWAY}/anthropic", "api_key": "${LLM_GATEWAY_KEY}" } },
  "harbor": { "uv": "uv", "engine": "podman", "nb": "../../nb/artifacts/linux-x64" }
}
```

While the agent works, its container reaches only the host of its model's
base URL; the checks run afterwards with no network at all. An experiment
keeps the engine it started on, and `resume` refuses a different one. What
a fixture's image needs and how a suite moves to agent arms is
[`docs/porting-to-agent-neutral.md`](docs/porting-to-agent-neutral.md);
`suites/code-change/` runs the same three tasks on all four agents.
`agents.codex.config` may name a native Codex `config.toml` (relative to
`suites/`), merged under Harbor's base URL. Use it for what an endpoint
needs of the agent: a llama-server takes only function tools, for example.

Harbor pins its own version but not its dependencies' versions, so proctor
resolves them with uv's `--exclude-newer` seven days back. A poisoned
release that is pulled within a week never reaches you. If the account
that runs agent arms holds credentials worth stealing, `harbor.uv` can
point at a wrapper that runs uv in a sandbox with your home directory
hidden. `docs/podman.md` §8 has one.

To check that a machine can run agent arms, without a model and at $0:

```bash
PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests
```

This runs every vendor agent at its pin against a scripted stub model,
then each `code-change` task's reference solution through Harbor's
verifier. It takes about twenty minutes. `PROCTOR_CONFORMANCE` may
instead name a `proctor.json` whose `harbor` block to use, so podman can
be checked without changing your config.

`suites/smoke/` with `fixtures/note/` is a complete example that runs against
nb's Mock provider. `suites/suite-authoring/` is the demo project: a suite
that asks an agent to write suites, using most of what is described here.
`suites/gate/` and `suites/conformance/` are proctor's own checks of its
Harbor path: nb on Mock, bare and through Harbor, must agree, and each
vendor agent on the stub must come back as scripted.
The check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md` and `project/plans/fixtures-arms-baselines.md`.

## Use

```bash
proctor list [suite]          # validate; print the labels and the cells that would run
proctor list --label kind=bugfix   # only the suites carrying that label
proctor run <suite>           # run every cell into runs/<id>/; prints the id
proctor resume <id>          # rerun cells that did not complete
proctor grade <id>           # checks over every completed cell -> checks.json; model verdicts cached in verdicts/
proctor grade <id> --rejudge # call the judges again instead of reusing the cells' verdict files
proctor grade <id> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <id> --arm a [--tasks x,y]   # only those cells (--arm repeatable); the rest keep their checks.json, so a paid judge pass can be staged
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--tasks x,y]   # pin the arm's analysed cells as suites/<suite>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
proctor report <id> --against <earlier>   # each arm against the same arm in an earlier experiment, in place of the baseline
proctor --version
```

Two ways of working. In explore mode several arms run in one experiment and
the report compares them to each other. In guard mode one arm runs against
a baseline: pinned cells per task, committed with the suite, written by
`proctor baseline` from an experiment you trust. The verdict card then
compares every arm to the baseline with the same paired method, and each
task card shows every arm against that task's pinned score. The verdict is
held, improved or regressed on the point estimate against the tolerance,
and `--fail-on regression` reads only that; it is *confirmed* only when the
whole interval lies beyond the tolerance on the same side (or within it,
for held), so a small task count cannot pass off a guess as a finding.
Without a baseline the card compares each arm with the first, the
reference. `--against <experiment>` stands in for the baseline for one
report: each arm is compared with the arm of the same id in that earlier
experiment, and the page says "earlier run" where it said "baseline".
While the pinned experiment is still under `runs/`, its cells are re-read,
so a regrade flows through; once it is archived the pinned scores stand,
and the report says which.

`report.html` is one file that loads nothing from outside (styles, a few
lines of script for the section rail, and the logo are inline); open it from
`file://` or attach it to a message. The page reads the same without the
script. `summary.md` has the same content as plain markdown: the verdict
card as a list and a heading per task.

Every rate carries a 95% interval and its counts; comparisons between arms are
paired on shared tasks with their own interval and a fixed verdict word; and
the minimum detectable effect is stated so an underpowered result cannot read
as "no difference". The methods are named in the report's last paragraph.

## Credits

The wizard hat icon (`assets/pointy-hat.svg`) is adapted from
["Pointy hat"](https://game-icons.net/1x1/lorc/pointy-hat.html) by Lorc,
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
