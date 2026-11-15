# Configuring proctor

The reference for what goes in an exam, a fixture, a task and the
machine's config. To install proctor and run a first exam, start from the
[README](../README.md).

## Layout and vocabulary

A repository that uses proctor has `exams/` (yours, in git), `attempts/` (raw
output, gitignored) and `reports/` (derived, written by `proctor report`).

The words, which the report and every file use the same way:

| word | one sentence |
|---|---|
| **exam** | A collection of related tasks with one business goal and the checks that grade them, with one pass list for every task so a pass rate across them means one thing. Its variants are files beside it, outside its identity. |
| **task** | One input and one desired outcome: a prompt against a fixture, with its own answer key (`expect`) for the checks that apply to it, and any checks of its own. |
| **check** | One yes-or-no question asked of a finished attempt, declared on the exam, a fixture or a task; an attempt carries all three. Where it is listed sets its role: `pass`, `validity`, or a guardrail rate. |
| **fixture** | The repository a task runs against and what "done" looks like in it; reused across exams. |
| **variant** | What is under test: an agent or nb's harness and provider, a model and a bundle, as declared data, one file per variant. Its identity is its content. |
| **attempt** | One variant at one task, once, with the evidence. Several per task, because the agent is stochastic. |
| **batch** | The attempts one `proctor run` made, when, on what machine and at what versions: what the exam lacked at the time. An exam's attempts are every batch's; one is current while its variant's and its task's hashes are today's, and stale after. |
| **live** | A mark on one variant (`live.json` beside the exam): the one in use now, and the reference the report compares the others with. |

An exam is one business goal probed by many tasks. Every task in it is
graded by the same pass list. If you find yourself wanting a different
pass list for one task, or a check that only makes sense on some tasks
and passes vacuously on the rest, you have two exams. If you want to
ask a second question of the same transcripts, you have a new check,
not a new exam. The prompt is what the user wants, in the user's words;
the task's checks are how proctor measures whether they got it.

## The machine's config and the judges

`exams/proctor.json` says where nb is and which config it runs with; paths are
relative to `exams/`:

```json
{ "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" } }
```

Without it, `nb` is taken from `PATH` and nb resolves its own config.
`--nb <path>` overrides either.

`--config <file>` on `list`, `run` and `grade` reads another
machine file in place of `exams/proctor.json`, with the same fields and
its relative paths still resolved against `exams/`. An exam is the
question and this file is the machine, so the same exam runs somewhere
else by naming a different file: `proctor run code-change --config
exams/proctor.modal.json`. `run` continues an interrupted batch only with
the `--config` it was made with. Files named `exams/proctor.*.json` and `exams/nb.*.json`
are gitignored, apart from the `*.example.json` ones.

The same file names the judges that the model checks call at grade time,
by wire shape rather than vendor. A `systemone` judge answers typed
questions with probabilities in the System One request shape (TypeSafe's
`/v1/systemone`, OpenRouter's `/alpha/decisions`, or a local server that
speaks it), with a `model` sent in the request when the endpoint serves
several; a `chat` judge is an OpenAI-dialect chat completions endpoint with
a model name. Endpoints and keys may be `${VAR}`
references, resolved only when `grade` runs. This repository commits only
`exams/proctor.example.json`, which expects `LLM_GATEWAY`, the base URL of
an OpenAI-dialect gateway that serves the routes it names, and
`LLM_GATEWAY_KEY`, its key; your `exams/proctor.json` is gitignored, so the
routes you point it at stay on your machine. nb resolves the same references
in its own config, which is how `exams/nb.json` reaches the same gateway.

```json
{
  "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" },
  "judges": {
    "jev": { "kind": "systemone", "endpoint": "${LLM_GATEWAY}/openrouter/alpha/decisions", "model": "typesafe/jev-1.14", "api_key": "${LLM_GATEWAY_KEY}", "family": "typesafe" },
    "glm": { "kind": "chat", "endpoint": "${LLM_GATEWAY}/openrouter/v1", "model": "z-ai/glm-5.3", "api_key": "${LLM_GATEWAY_KEY}", "family": "glm" }
  }
}
```

Keys belong in your env file, `~/.config/proctor/env` (`$XDG_CONFIG_HOME/proctor/env`
if that is set; `%LOCALAPPDATA%\proctor\env` on Windows; `PROCTOR_ENV_FILE` names
another), one `NAME=value` per line, `#` comments allowed. Every verb loads it at
startup, so a `${VAR}` in `proctor.json` or nb's config resolves without an
export, in nb, the judges, Harbor and your hooks alike; a variable already in the
environment wins. Keep it readable only by you (`chmod 600`; proctor warns
otherwise). It lives outside every repository on purpose: a key in it can be
neither committed nor copied into a work directory.

```
GEMINI_API_KEY=...
LLM_GATEWAY=http://gateway.example:8086/x
LLM_GATEWAY_KEY=...
```

`family` is only for the reminder `grade` prints when a variant's model
carries a judge's family name; a judge should be off-family from every variant
it grades. `max_window` (characters, default 24,000) caps what a judge is
sent; a larger window is an error, never a truncation.

A judge that is rate-limited (429, 503 or 529, or a rate-limit message in
the body) is retried three times, honouring `Retry-After`. If it keeps
refusing, or asks for a wait of more than a minute, it is stopped for the
rest of the pass. Every later check on it is then `error`, with the
endpoint's own words and no further requests. Nothing is lost: the next
`grade` asks every `error` again.

## Running bare nb in a container

nb's runner, below, is the older way to put a bare nb variant in a container.
For a new exam with tools, prefer an agent variant (`agent: nb` for nb
itself; see [Agent variants](#agent-variants)).

An exam that runs nb inside a container names the script that runs it in
its `exam.json`, beside the hooks that make the container;
`--runner <script>` (a path from the current directory) overrides it for one `proctor run` and
`--runner none` runs bare. The contract is the whole interface:

| proctor gives the runner | the runner must |
|---|---|
| stdin: the resolved program, compiled on the host by `nb --compile` so every `@file` include is inlined | pass it to nb's stdin unchanged |
| cwd: the work directory on the host | start nb with the checkout as its working directory, wherever that is inside |
| the attempt environment (`PROCTOR_*`, which includes `PROCTOR_NB`, the host binary, `PROCTOR_NB_CONFIG`, `PROCTOR_WORK_MOUNT`, `PROCTOR_BUNDLE_MOUNT` and `PROCTOR_CONTAINER`) and `NO_COLOR` | run nb where the checkout, the bundle and the config are at the paths those name |
| nothing on argv | run `nb --output jsonl [--config <config>] -` |
| stdout and stderr captured to the attempt | put only nb's stdout on stdout |
| | exit with nb's exit code |

`PROCTOR_CONTAINER` is a name derived from the attempt that proctor never uses,
so hooks and the runner can agree on one container: the attempt setup hook
creates it, the runner execs into it, the attempt teardown hook removes it.
Hooks see which is in effect in `PROCTOR_RUNNER`, empty on a bare run, and
skip the container. The manifest records the script and its hash, and
`run` does not continue a batch whose runner changed: it makes what is
missing in a new batch. The worked example is
`exams/runners/container.sh` with the `exam-authoring` exam's hooks and the
`Containerfile` beside the runner, which puts nb's own image
(`podman build -t nb .` in the nb repository) on the .NET SDK. The
commands below show the two modes:

```bash
proctor run exam-authoring                 # each attempt in its own container, as exam.json says
proctor run exam-authoring --runner none   # bare: the shakedown, on this machine
```

A bare run is for shaking down an exam: nb and the model's tools run on this
machine as you. A container is for anything you would not run on your own
machine, which is every real exam, since the model runs whatever it decides
to. How to build the image, what to mount, who owns the files the model
writes, and what nb leaves within its reach is nb's runbook,
[`docs/containers.md`](https://github.com/breitreiter/nb/blob/HEAD/docs/containers.md); the example above is that
runbook applied. Each attempt keeps `program.nb`, the source as resolved, and
`program.jsonl`, what actually went down stdin.

The exam's `nb` block names the runner, relative to the exam directory like
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
makes `--runner none` a shakedown of the exam on the host. A runner script is given the whole attempt environment, `PROCTOR_EXPECT`
(the answer key) included, so it must never forward its environment into
the container wholesale: pass the variables nb needs by name
(`-e LLM_GATEWAY`), as `exams/exam-authoring/hooks/attempt-setup.sh` does.
Bare nb gets only `NO_COLOR`. A mount is an absolute path inside the
container, and `runner` or `mounts` left in `proctor.json` is a problem, not
ignored. Script checks run on the host after the attempt, so a check that
needs a toolchain only the image has starts a container of that image
itself. `PROCTOR_CONTAINER` is `proctor-<batch>-<variant>-<task>-<attempt>`.

## Exams, fixtures and tasks

One exam is one directory, `exams/<id>/`:

```
exam.json        description, labels, attempts, nb (runner, mounts), hooks, grading (checks, pass, validity)
variants/*.json  one variant per file; the id is the file name; what is under test, as declared data (below)
program.nb       the nb program template; {{prompt}}, {{task}}, {{work}}, {{bundle}}, {{provider}}, {{model}}, {{harness}}, {{variant}}, {{attempt}}
tasks/*.json     one task per file; the id is the file name; names a fixture and adds the prompt, a description, labels, expect and its own checks
checks/*.sh      script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason); each needs a description
hooks/*.sh       variant and attempt setup/teardown
live.json        which variant is live, written by `proctor live` (absent until one is picked); docs/cli.md
```

`attempts` in `exam.json` is how many attempts of each task every variant
should have (1 if absent). A variant is one file, `variants/<id>.json`,
and holds only what is under test:

```json
{
  "id": "as-shipped",
  "note": "Claude Code on Sonnet with Tern's docs as shipped",
  "agent": "claude-code",
  "agent_version": "2.1.301",
  "model": "claude-sonnet-5-5",
  "bundle": { "path": "exams/tern-docs/bundles/as-shipped" }
}
```

A bare nb variant has `runner: nb`, `harness` and `provider` in place of
`agent` and `agent_version`; [Agent variants](#agent-variants) has the rest.
`note` is free text for the report's reader, saying what is different about
the variant; it is never a prediction. `exam.json` holds no variants, and
one that still lists them is refused, as is a variant with `attempts` (the
exam's) or `description` (its `note`).

A variant's identity is a hash of everything it declares but its id and
note, its bundle by content. Each attempt's manifest records it, so
editing a variant's file or its bundle makes a different variant under the
same name, which `proctor run` refuses once that id has made attempts:

```
exams/tern-docs/variants/as-shipped.json: this is not the as-shipped that made 12 attempts
  (bundle a2ed391d then, 77c0e5aa now). A changed variant is a new variant: give it a new id.
```

Give the changed one a new id (a new file) and keep the old file while its
attempts are wanted.

Hooks are the exam's, not a variant's, and prepare the machine: load the
model, build a tool, start a container. A variant hook runs once before and
after each variant's attempts and is told which variant in
`PROCTOR_VARIANT`. A hook that branches on it to change what the agent
sees is hiding part of the variant from proctor, which cannot hash it; put
that difference in the bundle, whose `paths` can show two variants
different parts of one revision.

Put `model {{model}}` in the template, before `provider {{provider}}`:

```
model {{model}}
provider {{provider}}
harness {{harness}}
```

Then the variant's `model` is the model nb asks for, and what the report records
is what ran. Without the line, nb uses the `Model` in the provider's nb.json
entry and the variant's `model` is only a label. `proctor setup` writes entries
with no `Model`, so they need the line. On those entries nb's Gemini provider
otherwise falls back to an old default, and its LocalLlm provider (OpenRouter
and other OpenAI-compatible servers) refuses to start. The order matters,
because nb builds a provider's client as soon as it reads `provider`. With
the line, the variant's `model` must be an id the provider serves (a gateway's
route name, if that is what it takes); `proctor setup --check` lists the ids
each key can reach.

A fixture is the repository a task is run against, with what "done" looks
like in it. Fixtures are repo-level, `fixtures/<id>/`, and reused across
exams:

```
fixture.json     id, source ({path} or {git, rev}), stack, labels, default expect, checks (scripts with descriptions)
checks/*.sh      the fixture's own checks (builds, tests pass); merged into every attempt run on it
repo/            the checkout source; the only thing copied into the work directory
```

A variant may name a `bundle`, the pinned version of whatever it puts under
test: `{ "path": "bundles/x" }` under the repository root, or
`{ "git": url, "rev": sha }`, optionally with `"paths"`, the files and
directories of that revision to hand over and nothing else:

```json
"bundle": { "git": "https://github.com/breitreiter/proctor.git", "rev": "1e71808b…",
            "paths": ["README.md", "docs/configuration.md", "exams/smoke"] }
```

A git bundle is cloned under `.proctor/bundles/`, and its paths copied out of
the clone into a directory of their own; a path the revision does not have
stops the batch. Proctor hands the bundle's directory to the program
template as `{{bundle}}` and to hooks and checks as `PROCTOR_BUNDLE`, and
records its identity in every manifest: a directory's content hash, or the
revision with its paths. What is inside it is the exam's business. Two
variants with two bundles compare them in one batch. A changed bundle is a
changed variant.

On an agent variant the bundle stays on the host. Proctor hashes it, records
it in every attempt and hands it to the variant hooks as `PROCTOR_BUNDLE`,
exactly as on a bare variant. It is never copied into the agent's container. An `agent: nb` variant with a
bundle cannot name `{{bundle}}` in the program template.

The way an agent variant's bundle reaches the agent is through the fixture's
services. A fixture's `image.services` names a directory beside `repo/`
holding a `docker-compose.yaml`; its services run beside the agent's
container and share its network, so the agent reaches them on
`localhost`, and the model's host stays the only other name it can
reach. Proctor copies the variant's bundle beside those services as
`bundle/` (an empty directory for a variant with none), so a service can
mount `./bundle` and serve it: a docs site, say, that the agent reads over
HTTP and cannot grep. `fixtures/tern-shop` serves a docs bundle this way,
and `exams/tern-docs` compares two versions of those docs with none.
Services run on Docker and podman, not on Modal.

Proctor checks the fixture out into the work directory before each attempt,
commits it, and collects `diff.patch` afterwards. In order: nb compiles the
program on the host (a program it refuses fails the attempt before
anything else), the fixture's `repo/` is copied (less `.git` and any
directory named in its `source.exclude`, which the fixture hash leaves out
too) and committed, the attempt setup hook runs, nb runs, the attempt
teardown hook runs, and the diff is collected. The commit comes before the
setup hook, so whatever the hook writes into the work directory is in
`diff.patch` and in `files_touched`. Teardown and the diff run whenever
setup ran, even when it failed, so a teardown hook must cope with a
half-made or missing container (`rm -f … || true`).

A fixture's `expect` is a default that the task's `expect` overlays one
top-level key at a time: a task's `files_touched` replaces the fixture's
whole `files_touched`. The merged block is what `"@expect"` reads and what
scripts see as `PROCTOR_EXPECT`.

Hooks and script checks get the attempt in the environment:
`PROCTOR_EXAM_DIR`, `PROCTOR_FIXTURE` (the fixture's directory, so a script
can read its `fixture.json`), `PROCTOR_BUNDLE`, `PROCTOR_BATCH`,
`PROCTOR_VARIANT`, `PROCTOR_TASK`, `PROCTOR_ATTEMPT`, `PROCTOR_ATTEMPT_DIR`,
`PROCTOR_WORK`, `PROCTOR_TASK_JSON`, `PROCTOR_EXPECT`, `PROCTOR_TRANSCRIPT`,
`PROCTOR_DIFF`, `PROCTOR_RUNNER`, `PROCTOR_NB`, `PROCTOR_NB_CONFIG`,
`PROCTOR_CONTAINER`, `PROCTOR_WORK_MOUNT` and `PROCTOR_BUNDLE_MOUNT`. A
variant hook, which runs once per variant rather than per attempt, gets
`PROCTOR_BATCH`, `PROCTOR_VARIANT`, `PROCTOR_EXAM_DIR`, `PROCTOR_BUNDLE`
and `PROCTOR_RUNNER`.

Checks live at three levels and an attempt carries the union: the exam's
`grading.checks` on every task, a fixture's `checks` on every task run
against it, and a task's own `checks` on that task alone, for the outcome
only it can state. A name declared at two levels is a problem. A check
named in `pass` that the exam does not declare must reach every task from
its fixture or its own block. A check named in `validity` decides whether an
attempt counts at all: an attempt that fails one is excluded, not failed;
an `error` or `needs-judge` there excludes nothing, and a check is in `pass`
or `validity`, not both. A task's check is reported over the tasks that
carry it.

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
carries a sentence beside it. An exam and a task take an optional
`description`, a variant a `note`; a task without one is described by the first line of its
prompt. A check takes `description` as a field beside its spec, and a
script check must have one, because from outside a script says nothing:

```json
{ "acceptance": { "script": "checks/acceptance.sh", "description": "the task's acceptance tests pass against the changed repository" } }
```

## The surface under test

An exam usually tests one thing inside a fixture that holds many: a CLI, an
MCP server, a docs site. An attempt can pass without ever using it, by going
round. `surface` in `exam.json` says what the thing is, so
`proctor friction <exam>` can sort each attempt's actions by it. It is optional,
and no check, report or other verb reads it.

```json
"surface": {
  "description": "Tern's documentation site, served at localhost:8080",
  "tools": ["mcp__tern__*"],
  "commands": ["tern"],
  "hosts": ["localhost:8080"],
  "docs": ["/opt/tern/docs/*", "/opt/tern/README.md", "tool:Skill"],
  "internals": ["/opt/tern/*"],
  "fails": ["<title>Not found</title>"],
  "substitutes": { "commands": ["git log", "gh"], "tools": [], "hosts": ["localhost:8081"] }
}
```

| field | what it names |
|---|---|
| `tools` | tool names that are the surface; a glob |
| `commands` | shell commands that are the surface, matched against the first word of an action |
| `hosts` | a surface reached by URL: a host and port named in any argument of an action, or in a tool call's `url` |
| `docs` | paths that describe the surface (globs), or `tool:<name>` for a tool that reads them |
| `internals` | the surface's own source and private notes; reading them is going round the published surface |
| `fails` | regular expressions (.NET) that mark the result of a call to the surface as a failure, beside a 4xx or 5xx status line |
| `substitutes` | the other ways to meet the same need: `commands` (a prefix of an action, so `git log` and not `git status`), `tools`, `hosts` |

Paths are the container's. In a glob `*` crosses directories, so
`/opt/tern/docs/*` is everything under it. A compound shell call is split
into actions, one per pipeline, named by the pipeline's first command; a
heredoc's body is not read; `cd` is followed; a script the agent wrote that
calls a surface command counts as the surface when it is run.

`friction.json` holds a `summary` and `points`. With a surface declared the
summary says whether the attempt reached it, which it reached first, whether a
call to it worked, how many of its calls failed, whether a substitute was
used, and the actions and cost on, about and around the surface, on
substitutes, and on everything else. A point is a stretch of adjacent steps
with one label:

| label | a step where | needs a surface |
|---|---|---|
| `off-surface` | the agent read the surface's internals, or hunted for where it is installed | yes |
| `substitute` | a declared substitute was used | yes |
| `abandoned` | a substitute was used within two steps of a surface call that failed | yes |
| `surface-search` | the agent searched the surface's docs | yes |
| `help` | `--help`, `-h` or `man` | no |
| `workaround` | a script run inline, or a file written to `/tmp` | yes |
| `environment` | the machine refused: command not found, permission, timeout, network | no |
| `tool-error` | the agent's own tool refused: output too large, a rejected call, an edit that did not apply | no |

Every point carries a quote from the call it cites, checked the way a
judge's quote is checked; a point with no quote that is in the call is not
written. The rules use no model, so the same trajectory and declaration
give the same file.

Rules see what a call was and what came back, not what it was for.
`proctor friction <exam> --reader <judge>` adds a reader: a chat judge from
`proctor.json` reads each attempt once and names five more kinds of point,
`repair` (fixing its own work after something it ran failed), `lost` (a
search or read that led to nothing used later), `reread`, `dead-end`
(following an instruction that could not work as given) and `workaround`.
The reader must quote the call, and a point is written only when the
call exists, the label is one of the five and the quote is in that
call; its one-line reason is kept as a note. Its points carry
`"source": {"kind": "model", ...}`, so they can always be told from the
rules'. It is one model call per attempt, of the attempt's length (a few
thousand to a few tens of thousands of tokens); what was sent and
answered is kept in the attempt as `friction-reading.<judge>.<hash>.json`
and reused, so repeating the command costs nothing, and `--rejudge` asks
again. The command prints the tokens it used. A small, cheap model is
enough; local models tried so far were not.

A point is a place to look, not a verdict: what it
should become, when it matters, is a check. Two built-ins say most of
them: `tool_args` with `match: regex` for what an attempt did, and
`result_matches` (a regular expression over every tool result) for what
it was shown, usually negated:

```json
"no-dead-end": { "not_result_matches": "ask the user to run\\s+`tern setup`", "description": "the docs never send the agent to an instruction it cannot follow" }
```

## Oracles

A task can name an **answer sheet**, a scripted user that answers when the
agent stops to ask: `"oracle": "oracle/<task>.md"`, a file inside the exam
directory in nb's format (`## <id>`, then the answer, verbatim). After a turn
that ends `ok`, a judge reads the agent's last message against the sheet. On a
hit the selected answers become the next user turn and the attempt goes on; when
the agent asked for something the sheet lacks, the attempt ends `oracle_miss`.
`exam.json`'s `oracle` block sets how many answers an attempt may get (`turns`,
default 8, then `oracle_budget`) and which nb config entry judges (`judge`;
by default the variant's own provider). The checks are `oracle_hit` (a list of
entry ids the oracle must have answered with), `oracle_turns {max}` and
`oracle_misses {max}`. A bare nb variant runs nb's own oracle. An `agent: nb` variant
runs in Harbor with a simulated user on the host, which sends the task and
each answer to nb over Harbor's ACP bridge and judges with the host's nb, so
the sheet never enters the container; its program must end in exactly
`run {{prompt}}`. A vendor agent variant beside an oracle task is refused, because
Harbor leaves a bridged vendor agent no trajectory to grade
(`project/plans/oracle-in-proctor.md`).

## Descriptions and the report

A built-in check describes itself from its spec
(`{ "denied_calls": { "max": 0 } }` reads as "no denied tool calls") unless you give it a better sentence.

The report is built for someone who was not there, and for someone who opens
one every few months as much as every day: it defines variant, task, attempt and
check before using them, and every section opens with a sentence saying what
it is for. Those definitions are set in muted grey so the findings stand
out once you know the drill. A rail down the left lists every section and
task and follows you as you scroll.

It starts with the exam's question and a verdict card. With a variant
live, it gives each other variant against the live one: better, worse or
the same and by how many points, whether the 95% interval confirms it, the
tasks that moved it, and, when it cannot be confirmed, how many tasks it
would take. With nothing live, it shows the variants side by side and
compares none. The card says so if a variant lost more attempts than
another. Then Variants, and Tasks: one card per task with its prompt, its
fixture, the checks it carries (its own first, then the exam's), and every
variant's attempts and score on it, followed by each attempt that did not pass
and why. Results holds the numbers across all tasks (pass rates, the
comparison of each variant with the live one, when one is live), then Failures: per variant, each check
that failed, in the author's words, with how often and on which tasks.
Everything after that is evidence: what ran and what was left out, check
pass rates, cost, every attempt, and the method last. An attempt whose headline check
could not decide (a script's exit 2, a split judge) is *undecided*: out of
the pass rate on both sides, listed for review when there are a few, and
flagged as a defect in the check when there are many. An attempt whose
headline script check is *outdated* (an agent variant's, changed since the
attempt was made; see "Agent variants") is undecided the same way, shown as
outdated, and named in a warning at the top with the batches to make again.

## Labels

Labels are yours: a key with a string or a list of strings, on the exam,
the fixture or the task, and proctor never interprets a key. A task carries
its fixture's labels, the exam's laid over them and its own over both, key
by key. They print with `list`, filter it (`--label kind`,
`--label area=coding/*`, repeatable; `*` matches within a slash segment and `**`
across), and sit on every row of `results.jsonl` so a later report can group
on them without re-reading an exam that has since changed:

```json
{ "labels": { "area": "coding/change", "stack": "dotnet" } }
```

## Model checks

Two checks ask a model, and both see a *window*, never the transcript:
named extractions (`answer`, `answer_json`, `prompt`, `tool_calls`,
`tool_results`, `diff`, `user_turns`) joined with `+`. `decide` asks a
systemone judge one typed question and reads the probability: yes/no
without `options`, one label of several with them. `judge` asks a chat
model a criterion, several times at temperature 0, and reads a label with
verbatim evidence; every quote is checked against the window, a vote with
an unverifiable quote is discarded, a split or an `unknown` is `needs-judge`,
and the reasoning stays in the attempt's `verdicts/` file and never reaches the
report.

```json
{
  "stance":      { "decide": { "ask": "What does the closing message claim?", "window": "answer",
                               "options": { "complete": "it presents the work as finished", "asked": "it stops to ask", "blocked": "it could not finish" },
                               "expect": "complete", "threshold": 0.95 } },
  "ran-tests":   { "decide": { "ask": "Did the agent run the project's tests before its closing message?", "window": "tool_calls+answer" } },
  "change-fits": { "judge":  { "ask": "Does the diff change only what the prompt asked for?", "window": "prompt+diff", "votes": 3 } }
}
```

`decide` passes when the most likely label is `expect` at a probability of
at least `threshold` (default 0.9), fails when another label is, and is
`needs-judge` below it. On a yes/no question `expect` is true or false,
default true; with `options` (a list of at least two labels, or labels with
a sentence each) `expect` is required. `judge` takes `votes` (default 3) and
expects yes unless told no; fewer than two usable votes (one when `votes` is
1) is `error`, not `needs-judge`. A window part that is empty, such as
`diff` on an attempt that changed nothing or `answer_json` with no JSON
fence, is `error` before any call. `tool_calls` cuts each call's arguments
at 400 characters and shows no output; `tool_results` keeps each result's
first 40 lines; `max_window` caps the parts together. No window or prompt
carries the variant, the model or the attempt number. A verdict is kept in
`verdicts/` and reused by the next `grade`; its hash covers the request,
the judge's model, the threshold and the expectation, so changing any of
them asks again, and an `error` is always asked again.

`expect` may be `"@expect"`, read from the task's `expect.<check name>`;
so may `ask`, read from `expect.<check name>.ask`, so one `correct` check
can carry a rubric per task. A built-in field set to `"@expect"` reads
`expect.<check name>.<field>` first and `expect.<field>` second.
`with` names a judge from `proctor.json`; without it the only judge of the
needed kind is used, and several of that kind with no `with` is a problem
`list` and `run` report. Whether an exam is judged is not declared; it follows
from a check that asks a model, and `list` says so.

## Agent variants

A variant that names an `agent` runs through
[Harbor](https://github.com/harbor-framework/harbor), in a container built
from its fixture's image, instead of running nb on this machine. The agent
is a vendor's own CLI at a pinned version (`claude-code`, `codex`,
`qwen-code`), or `nb` itself:

```json
{ "id": "cc-haiku", "agent": "claude-code", "agent_version": "2.1.301", "model": "claude-haiku-5-5" }
```

```json
{ "id": "floor", "agent": "nb", "harness": "nb", "provider": "local-qcoder", "model": "qwen3-coder-next" }
```

are `variants/cc-haiku.json` and `variants/floor.json`, and `exam.json` pins
Harbor and the time each attempt may take:

```json
{ "harbor": { "version": "0.24.0" }, "limits": { "wall_ms": 1800000 } }
```

The machine needs [uv](https://docs.astral.sh/uv/), which runs the pinned
Harbor, and a container engine, set by `harbor.engine` in
`exams/proctor.json`:

- **Docker** (the default), with the Compose and buildx CLI plugins.
- **Podman**, rootless, with its API socket running, docker-compose v2
  behind `podman compose`, and three lines of user config. The setup,
  and why each step is there, is [`podman.md`](podman.md);
  `run` checks what it can before it starts.
- **Modal** (experimental), no engine on this machine at all: each attempt's containers run
  as sandboxes on your Modal account. The agent's model must be on the
  internet, and a fixture with `image.services` cannot run there. Setting
  it up, and what it costs, is [`modal.md`](modal.md).

The same file says where each vendor agent's model calls go and where nb's
self-contained linux-x64 publish is, for an `agent: nb` variant:

```json
{
  "agents": { "claude-code": { "base_url": "${LLM_GATEWAY}/anthropic", "api_key": "${LLM_GATEWAY_KEY}" } },
  "harbor": { "uv": "uv", "engine": "podman", "nb": "../../nb/artifacts/linux-x64" }
}
```

While the agent works, its container reaches only the host of its model's
base URL; the checks run afterwards with no network at all.

On an agent variant a script check runs in Harbor's verifier, a separate
container, when the attempt is made, from a copy of the exam directory and
the fixture compiled into the batch; `grade` reads the verdict it wrote and
never runs the script again. So its verdict is fixed when the attempt is
made. Change the script, any file beside it in the exam or the fixture, or
the task's expected values, or add a script check, and `grade` marks those
attempts' verdicts for it `outdated`: out of the check's rate and, for a
headline check, out of the pass rate, never counted as a fail. They stay
outdated until the attempts are made again (move the batch out of
`attempts/` and `proctor run` the exam). Built-in and judged checks on the
same attempts are graded afresh as on any variant. A batch
keeps the engine it started on, and `run` continues it on no other. What
a fixture's image needs and how an exam moves to agent variants is
[`porting-to-agent-neutral.md`](porting-to-agent-neutral.md);
`exams/code-change/` runs the same three tasks on all four agents.
`agents.codex.config` may name a native Codex `config.toml` (relative to
`exams/`), merged under Harbor's base URL. Use it for what an endpoint
needs of the agent: a llama-server takes only function tools, for example.

Harbor pins its own version but not its dependencies' versions, so proctor
resolves them with uv's `--exclude-newer` seven days back. A poisoned
release that is pulled within a week never reaches you. If the account
that runs agent variants holds credentials worth stealing, `harbor.uv` can
point at a wrapper that runs uv in a sandbox with your home directory
hidden. [`podman.md`](podman.md) §8 has one.

To check that a machine can run agent variants, without a model and at $0:

```bash
PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests
```

This runs every vendor agent at its pin against a scripted stub model,
then each `code-change` task's reference solution through Harbor's
verifier. It takes about twenty minutes. `PROCTOR_CONFORMANCE` may
instead name a `proctor.json` whose `harbor` block to use, so podman can
be checked without changing your config.

## Examples

`exams/smoke/` with `fixtures/note/` is a complete example that runs against
nb's Mock provider. `exams/exam-authoring/` is the demo project: an exam
that asks an agent to write exams, using most of what is described here.
`exams/gate/` and `exams/conformance/` are proctor's own checks of its
Harbor path: nb on Mock, bare and through Harbor, must agree, and each
vendor agent on the stub must come back as scripted.
The check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md`.
