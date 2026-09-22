# proctor

Runs an eval's arms through [nb](../nb), grades the transcripts with declared
checks, computes the statistics once, and renders one self-contained HTML
report plus a markdown twin.

## Install

Build nb first (`dotnet build` in its repo), then:

```bash
dotnet build
alias proctor='dotnet /path/to/proctor/bin/Debug/net10.0/proctor.dll'
```

## Configure

A repository that uses proctor has `evals/` (yours, in git), `runs/` (raw
output, gitignored) and `reports/` (derived, written by `proctor report`).

`evals/proctor.json` says where nb is and which config it runs with; paths are
relative to `evals/`:

```json
{ "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb-mock.json" } }
```

Without it, `nb` is taken from `PATH` and nb resolves its own config. `--nb
<path>` overrides either.

The same file names the judges that the model checks call at grade time,
by wire shape rather than vendor. A `systemone` judge answers typed
questions with probabilities (`POST /v1/systemone`: Jev on Cloudflare, or
anything that speaks it); a `chat` judge is an OpenAI-dialect chat
completions endpoint with a model name. Keys are `${VAR}` references,
resolved only when `grade` runs:

```json
{
  "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" },
  "judges": {
    "jev": { "kind": "systemone", "endpoint": "http://imp:8086/x/cf/workers-ai/run/typesafe/jev", "api_key": "${MINROUTER_KEY}", "family": "typesafe" },
    "glm": { "kind": "chat", "endpoint": "http://imp:8086/x/cf/compat/v1", "model": "@cf/zai-org/glm-4.7", "api_key": "${MINROUTER_KEY}", "family": "glm" }
  }
}
```

`family` is only for the reminder `grade` prints when an arm's model
carries a judge's family name; a judge should be off-family from every arm
it grades. `max_window` (characters, default 24,000) caps what a judge is
sent; a larger window is an error, never a truncation.

An eval that runs nb inside a container names the script that runs it in
its `eval.json`, beside the hooks that make the container; `--runner
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
`evals/runners/container.sh` with the `code-change` eval's hooks and the
`Containerfile` beside the runner, which puts nb's own image (`podman build
-t nb .` in the nb repository) on the .NET SDK:

```bash
proctor run code-change                 # each cell in its own container, as eval.json says
proctor run code-change --runner none   # bare: the shakedown, on this machine
```

A bare run is for shaking down an eval: nb and the model's tools run on this
machine as you. A container is for anything you would not run on your own
machine, which is every real eval, since the model runs whatever it decides
to. How to build the image, what to mount, who owns the files the model
writes, and what nb leaves within its reach is nb's runbook,
[`docs/containers.md`](../nb/docs/containers.md); the example above is that
runbook applied. Each cell keeps `program.nb`, the source as resolved, and
`program.jsonl`, what actually went down stdin.

The eval's `nb` block names the runner, relative to the eval directory like
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
makes `--runner none` a shakedown of the eval on the host. The plan and the
worked example are in `project/plans/containerised-runs.md`.

One eval is one directory, `evals/<id>/`:

```
eval.json        description, labels, arms, samples, nb (runner, mounts), hooks, grading (checks, pass, validity)
program.nb       the nb program template; {{prompt}}, {{case}}, {{work}}, {{provider}}, {{model}}, {{harness}}, {{arm}}, {{sample}}
cases/*.json     one case per file; the id is the file name; names a fixture and adds the goal, a description and labels
checks/*.sh      script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason); each needs a description
hooks/*.sh       arm and sample setup/teardown
```

A fixture is the repository a case is run against, with what "done" looks
like in it. Fixtures are repo-level, `fixtures/<id>/`, and reused across
evals:

```
fixture.json     id, source ({path} or {git, rev}), stack, labels, default expect, checks (scripts with descriptions)
checks/*.sh      the fixture's own checks (builds, tests pass); merged into every cell run on it
repo/            the checkout source; the only thing copied into the work directory
```

An arm may name a `bundle`, the pinned version of whatever it puts under
test: `{ "path": "bundles/x" }` under the repository root, or
`{ "git": url, "rev": sha }`. Proctor hands its directory to the program
template as `{{bundle}}` and to hooks and checks as `PROCTOR_BUNDLE`, and
records its identity in every manifest; what is inside it is the eval's
business. Two arms with two bundles compare them in one experiment.

Proctor checks the fixture out into the work directory before each sample,
commits it, and collects `diff.patch` afterwards. A check named in `pass`
that the eval does not declare must come from every case's fixture. A check
named in `validity` decides whether a sample counts at all: a sample that
fails one is excluded, not failed.

The report is written for a reader, not a grader, so every id it prints
carries a sentence beside it. An eval, an arm and a case take an optional
`description`; a case without one is described by the first line of its
prompt. A check takes `description` as a field beside its spec, and a
script check must have one, because from outside a script says nothing:

```json
"acceptance": { "script": "checks/acceptance.sh", "description": "the case's acceptance tests pass against the changed repository" }
```

A built-in check describes itself from its spec (`{ "denied_calls": { "max":
0 } }` reads as "no denied tool calls") unless you give it a better sentence.
The report opens with the eval's description and a section, "Where it fell
down", that names each check that did not hold, in those words, with how
often and in which cases; the case and check tables carry the sentences too.

Labels are yours: a key with a string or a list of strings, on the eval,
the fixture or the case, and proctor never interprets a key. A case carries
its fixture's labels, the eval's laid over them and its own over both, key
by key. They print with `list`, filter it (`--label kind`, `--label
area=coding/*`, repeatable; `*` matches within a slash segment and `**`
across), and sit on every row of `results.jsonl` so a later report can group
on them without re-reading an eval that has since changed:

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

`expect` may be `"@expect"`, read from the case's `expect.<check name>`.
`with` names a judge from `proctor.json`; without it the only judge of the
needed kind is used. Whether an eval is judged is not declared; it follows
from a check that asks a model, and `list` says so.

`evals/smoke/` with `fixtures/note/` is a complete example that runs against
nb's Mock provider. The check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md` and `project/plans/fixtures-arms-baselines.md`.

## Use

```bash
proctor list [eval]          # validate; print the labels and the cells that would run
proctor list --label kind=bugfix   # only the evals carrying that label
proctor run <eval>           # run every cell into runs/<id>/; prints the id
proctor resume <id>          # rerun cells that did not complete
proctor grade <id>           # checks over every completed cell -> checks.json; model verdicts cached in verdicts/
proctor grade <id> --rejudge # call the judges again instead of reusing the cells' verdict files
proctor grade <id> --judge jev=jev-local   # grade checks that name one judge with another
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--cases x,y]   # pin the arm's analysed cells as evals/<eval>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
```

Two ways of working. In explore mode several arms run in one experiment and
the report compares them to each other. In guard mode one arm runs against
a baseline: pinned cells per case, committed with the eval, written by
`proctor baseline` from an experiment you trust. The report then adds a
section comparing every arm to the baseline with the same paired method,
a verdict of held, improved or regressed on the point estimate against the
tolerance, and the interval beside it so a small case count cannot hide.
While the pinned experiment is still under `runs/`, its cells are re-read,
so a regrade flows through; once it is archived the pinned scores stand,
and the report says which.

`report.html` is one file with no external assets; open it from `file://`
or attach it to a message. `summary.md` has the same sections as plain tables.

Every rate carries a 95% interval and its counts; comparisons between arms are
paired on shared cases with their own interval and a fixed verdict word; and
the minimum detectable effect is stated so an underpowered result cannot read
as "no difference". The methods are named in the report's last paragraph.
