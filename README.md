# proctor

Runs a suite's arms through [nb](../nb), grades the transcripts with declared
checks, computes the statistics once, and renders one self-contained HTML
report plus a markdown twin.

## Install

Build nb first (`dotnet build` in its repo), then:

```bash
dotnet build
alias proctor='dotnet /path/to/proctor/bin/Debug/net10.0/proctor.dll'
```

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
{ "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb-mock.json" } }
```

Without it, `nb` is taken from `PATH` and nb resolves its own config. `--nb
<path>` overrides either.

The same file names the judges that the model checks call at grade time,
by wire shape rather than vendor. A `systemone` judge answers typed
questions with probabilities (`POST /v1/systemone`: Jev on Cloudflare, or
anything that speaks it); a `chat` judge is an OpenAI-dialect chat
completions endpoint with a model name. Endpoints and keys may be `${VAR}`
references, resolved only when `grade` runs, so the committed file names no
machine: the one checked in here expects `LLM_GATEWAY`, the base URL of an
OpenAI-dialect gateway that serves the routes it names, and
`LLM_GATEWAY_KEY`, its key. nb resolves the same references in its own
config, which is how `suites/nb.json` reaches the same gateway.

```json
{
  "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" },
  "judges": {
    "jev": { "kind": "systemone", "endpoint": "${LLM_GATEWAY}/cf/workers-ai/run/typesafe/jev", "api_key": "${LLM_GATEWAY_KEY}", "family": "typesafe" },
    "glm": { "kind": "chat", "endpoint": "${LLM_GATEWAY}/cf/compat/v1", "model": "@cf/zai-org/glm-4.7", "api_key": "${LLM_GATEWAY_KEY}", "family": "glm" }
  }
}
```

`family` is only for the reminder `grade` prints when an arm's model
carries a judge's family name; a judge should be off-family from every arm
it grades. `max_window` (characters, default 24,000) caps what a judge is
sent; a larger window is an error, never a truncation.

A suite that runs nb inside a container names the script that runs it in
its `suite.json`, beside the hooks that make the container; `--runner
<script>` (a path from the current directory) overrides it for one run and
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
`suites/runners/container.sh` with the `code-change` suite's hooks and the
`Containerfile` beside the runner, which puts nb's own image (`podman build
-t nb .` in the nb repository) on the .NET SDK:

```bash
proctor run code-change                 # each cell in its own container, as suite.json says
proctor run code-change --runner none   # bare: the shakedown, on this machine
```

A bare run is for shaking down a suite: nb and the model's tools run on this
machine as you. A container is for anything you would not run on your own
machine, which is every real suite, since the model runs whatever it decides
to. How to build the image, what to mount, who owns the files the model
writes, and what nb leaves within its reach is nb's runbook,
[`docs/containers.md`](../nb/docs/containers.md); the example above is that
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
program.nb       the nb program template; {{prompt}}, {{task}}, {{work}}, {{provider}}, {{model}}, {{harness}}, {{arm}}, {{sample}}
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
"acceptance": { "script": "checks/acceptance.sh", "description": "the task's acceptance tests pass against the changed repository" }
```

A built-in check describes itself from its spec (`{ "denied_calls": { "max":
0 } }` reads as "no denied tool calls") unless you give it a better sentence.

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
by key. They print with `list`, filter it (`--label kind`, `--label
area=coding/*`, repeatable; `*` matches within a slash segment and `**`
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
"stance":      { "decide": { "ask": "What does the closing message claim?", "window": "answer",
                             "options": { "complete": "it presents the work as finished", "asked": "it stops to ask", "blocked": "it could not finish" },
                             "expect": "complete", "threshold": 0.95 } },
"ran-tests":   { "decide": { "ask": "Did the agent run the project's tests before its closing message?", "window": "tool_calls+answer" } },
"change-fits": { "judge":  { "ask": "Does the diff change only what the prompt asked for?", "window": "prompt+diff", "samples": 3 } }
```

`expect` may be `"@expect"`, read from the task's `expect.<check name>`;
so may `ask`, read from `expect.<check name>.ask`, so one `correct` check
can carry a rubric per task. A built-in field set to `"@expect"` reads
`expect.<check name>.<field>` first and `expect.<field>` second.
`with` names a judge from `proctor.json`; without it the only judge of the
needed kind is used. Whether a suite is judged is not declared; it follows
from a check that asks a model, and `list` says so.

`suites/smoke/` with `fixtures/note/` is a complete example that runs against
nb's Mock provider. `suites/suite-authoring/` is the demo project: a suite
that asks an agent to write suites, using most of what is described here, with
the report of its latest run checked in under `report/`. The check vocabulary and the shape of every file are in
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
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--tasks x,y]   # pin the arm's analysed cells as suites/<suite>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
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
Without a baseline the card compares each arm with the first.
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
