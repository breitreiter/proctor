# Configuring proctor

The reference for what goes in an exam, a fixture and the machine's
config. To install proctor and run a first exam, start from the
[README](../README.md).

## Layout and vocabulary

A repository that uses proctor has `exams/` and `fixtures/` (yours, in
git), `bank/` (every try made, gitignored), `log/` and `exams-seen/`
(what each `proctor run` did and the exam as it stood, gitignored) and
`reports/` (derived, written by `proctor report`).

The words, which the report and every file use the same way:

| word | one sentence |
|---|---|
| **exam** | One question, the stages that ask it and the checks that grade them, with what is under test declared as axes. |
| **stage** | One prompt and one desired outcome, with its own answer key (`expect`) for the checks that apply to it, and any checks of its own. The stages of an exam run in order, in one workspace. |
| **check** | One yes-or-no question asked of a finished stage, declared on the exam, a fixture or a stage. Where it is listed sets its role: `correctness`, `integrity`, `quality`, or in no list an observation. |
| **fixture** | The repository the work is done in and what "done" looks like in it; reused across exams. |
| **axis** | One thing that is varied: the docs an agent is given, the agent and model, the repository. |
| **value** | One choice on an axis, as declared data: files, parameters, or who does the work. Its identity is its content. |
| **configuration** | One value on every axis: what a try ran as. The exam names the ones it wants run. |
| **try** | One configuration, run once through every stage that applies in it, with the evidence. Several per configuration, because the agent is stochastic. |
| **attempt** | One stage of one try. |
| **bank** | Every try of an exam, whenever it was made: `bank/<exam>/<try id>/`. One is current while its configuration's values and the exam's stages are as they were when it was made, and stale after; a stale one stays where it is and is left out. What each `proctor run` did (when, on what machine, at what versions) is an entry under `log/`, a record and not a grouping. |
| **live** | A mark on one value of an axis: the one in use now, and what the other values of that axis are compared with. |
| **prediction** | A statement written before the tries are made: run this configuration, and this will change. |

An exam is one question. Every stage in it is graded by the same lists
of checks. If you find yourself wanting a different correctness list for
one stage, you have two exams. If you want to ask a second question of
the same transcripts, you have a new check, not a new exam. The prompt
is what the user wants, in the user's words; the checks are how proctor
measures whether they got it.

### When an exam gets a copy

An exam's tries are its bank, and every try in it is evidence about one
world: the same fixtures, the same services, the same machine. What is
under test changes; the world is held still. That is what lets a report
compare tries made weeks apart, and lets a new check regrade tries
already made. So before any change, ask whether the tries already made
are still evidence after it.

- **A change to what is under test is a new value, in the same exam.**
  A new agent version, a new tool, a new model id, a reworded prompt: a
  new value beside the old one, and a configuration that names it. The
  old value still runs and the world has not moved, so its tries still
  count, and the two can be tried in the same week and compared.
- **A change to the standard is a regrade, in the same exam.** A new
  check, a tighter `expect`. The transcripts are what they were; the
  exam asks more of them. If what is live fails the new check, it was
  failing all along, and the report now says so.
- **A change to what the agent meets is every try made again.** A new
  stage, a reworded prompt that is not a value, a changed hook: the
  stages are one job with one hash, so the tries made before are stale.
- **A change to the world is a new exam.** An API the stages' code calls
  now rate-limits; the fixture's environment gains a tool; builds run on
  a smaller machine. The tries already made are about a world that is
  gone, and no regrade fixes that. Copy the exam to a new id (`cp -r
  exams/tern-docs exams/tern-docs-2`), try what is live again there as
  the reference, and close the old one: its kept report is the record.
  Proctor sees a change to a fixture (every try on it goes stale) but
  not a machine or a service outside the repository. When the change is
  in a fixture, copy the fixture too, since fixtures are shared and
  editing one in place makes every exam on it stale.
- **A hosted model drifting is noise you accept.** A model id that
  answers a little differently from last month is absorbed like the
  model's own randomness. That is tolerable only because everything else
  is pinned: the agent version, the files, the fixture, the image.

**Chasing every change costs more than it is worth for most exams.** Each
copied exam and each new agent version is a full set of tries, at hosted
prices when the model is hosted, and agents ship new versions every week
or two. Decide per exam how far behind it may fall. An exam whose
question is the agent ("does Claude Code handle our SDK?") has to follow
the versions people run. An exam whose question is your own work (two
versions of your docs, your tool descriptions) can keep one pinned agent
for months, because the difference between your values is what it
reports, and that does not go stale when a newer agent ships. Every
report names the versions and the runs it was built from, so a reader
can judge whether that is close enough.

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
exams/proctor.other.json`. Files named `exams/proctor.*.json` and `exams/nb.*.json`
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

`family` is only for the reminder `grade` prints when the model doing the
work carries a judge's family name; a judge should be off-family from every
model it grades. `max_window` (characters, default 24,000) caps what a judge is
sent; a larger window is an error, never a truncation.

A judge that is rate-limited (429, 503 or 529, or a rate-limit message in
the body) is retried three times, honouring `Retry-After`. If it keeps
refusing, or asks for a wait of more than a minute, it is stopped for the
rest of the pass. Every later check on it is then `error`, with the
endpoint's own words and no further requests. Nothing is lost: the next
`grade` asks every `error` again.

## An exam

One exam is one directory, `exams/<id>/`:

```
exam.json           the question, the fixture, the stages, the axes, the configurations to run, the grading
program.nb          the nb program template (for a value that runs nb)
values/<axis>/<value>/   the files a value carries, by convention
checks/*.sh         script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason)
hooks/*.sh          setup and teardown around each configuration's tries and around each try
oracle/*.md         answer sheets, for a stage a scripted user answers
predictions/*.json  what is expected of a configuration, written before its tries
retired.json        the named configurations retired (`proctor retire`)
combinations.json   written by `proctor build`
```

`exams/readme-answers` (nb, bare, no tools) and `exams/tern-docs` (a
coding agent in a container) are the two to read first. Coming from an
exam of the earlier shape: [`porting-from-suites.md`](porting-from-suites.md)
has the map.

```json
{
  "id": "readme-answers",
  "description": "Does a README tell a reader what they need to use jot from a script?",
  "attempts": 4,
  "fixture": "empty",
  "params": { "README": { "tree": true, "about": "the directory holding the README the model is given" } },
  "axes": {
    "docs": {
      "about": "jot's README",
      "live": "as-shipped",
      "values": {
        "as-shipped":  { "note": "jot's README as it ships", "set": { "README": { "path": "values/docs/as-shipped" } } },
        "for-scripts": { "note": "the same README with a section on using jot from a script",
                         "why": "a script that runs `jot clear` cannot learn from the README as shipped what will happen",
                         "set": { "README": { "path": "values/docs/for-scripts" } } }
      }
    },
    "assistant": {
      "about": "the model that answers, with no tools",
      "values": { "gemini": { "note": "Gemini Flash-Lite, through nb", "provider": "gemini", "model": "gemini-flash-lite-latest" } }
    }
  },
  "configurations": {
    "as-shipped":  { "docs": "as-shipped",  "assistant": "gemini" },
    "for-scripts": { "docs": "for-scripts", "assistant": "gemini" }
  },
  "stages": [
    { "id": "clear-wait", "about": "How long clear waits with no terminal",
      "prompt": "With no terminal attached, how many seconds does `jot clear` wait?",
      "expect": { "answer_regex": "(?im)^ANSWER:\\D*30\\b" } }
  ],
  "grading": {
    "checks": { "answered": { "exit_reason": "ok" }, "correct": { "answer_regex": "@expect" } },
    "correctness": ["correct"],
    "integrity": ["answered"]
  }
}
```

`attempts` is how many tries every named configuration should have (1
if absent).

### Stages

- The stages run in the order listed, in one workspace, and the agent
  is started afresh for each. **Each stage starts from the workspace as
  the try began**: tracked files are restored and new files removed
  before it (ignored files, such as restored packages, are left). So
  stages cannot disturb each other. Services are not reset.
- `"chained": true` is for an exam whose later stages need what earlier
  ones made. Then the workspace is kept from stage to stage, and a stage
  that was stopped ends the try: the stages after it are `skipped`, and
  the try still counts.
- A stage has an `id`, a `prompt`, and optionally `about`, `expect`,
  `checks`, `oracle` and `when`. It has no `fixture` or `labels`: those
  are the exam's.
- The `id` is the stage's label on screen. Keep it to two or three
  words, about 20 characters, naming what the stage asks (`clear-wait`,
  not `stage-12`); `about` is the one-sentence version. `proctor list`
  names the ids that will not read as a label.
- `"when": "<axis> = <value>"` says where a stage applies (`!=`, and
  several terms joined by `and`). A try is the stages that apply in its
  configuration. A stage with no condition applies everywhere.
- A stage that did not run (a provider error, a rate limit, a cancelled
  or crashed nb) loses the try: nothing more of it is launched, none of
  it counts, and `proctor run` makes another, whole.
- A stage that was stopped by a limit or a denial is a result, and the
  next stage runs, unless the exam is chained.
- The stages are one job with one hash: changing, adding, moving or
  renaming a stage, or changing its prompt, makes every banked try of
  the exam stale. Changing a check, an expected value or an `about` does
  not.

### Axes, values and configurations

An axis is one thing you vary, and a value one choice on it. A value has
a `note`, a sentence saying what it changes for the agent, and may have
a `why`, a sentence saying what it is there for (a customer's report, an
idea of your own). Neither is part of its identity. Beyond that a value
holds any of:

| key | what it is | who acts on it |
|---|---|---|
| `set` | parameters the base declares, by name | the base's own scripts: the fixture's image and services, the program template, the hooks |
| `files` | a tree laid over the checkout before the first stage: `{"path": "values/docs/x"}` in the exam, or `{"git": url, "rev": sha, "paths": [...]}`; `to` puts it in a subdirectory | proctor |
| `agent`, `agent_version`, `route`, `model`, `provider`, `harness` | who does the work | proctor |
| `fixture` | a whole fixture, for an axis of worlds (below) | proctor |

A value with only a note sets nothing: the base as it is.

**Parameters.** A value may set only what the base declares, in
`fixture.json` or, for what the program template reads, in `exam.json`:

```json
"params": {
  "PLANTED": { "default": "none", "about": "which defect is planted" },
  "DOCS":    { "tree": true, "about": "what the docs site serves" },
  "API_KEY": { "secret": true, "about": "the sandbox account's key" }
}
```

A parameter is text, or with `tree` a directory of files (`{"path"}` or
`{"git", "rev", "paths"}`, as `files`). `secret` names an environment
variable, whose value reaches hooks and the program on the bare path and
is kept out of the bank; it does not reach an image or the verifier yet.
Where each reaches:

- the program template, as `{{param.NAME}}` (a tree as its directory);
  an axis's value by name is `{{axis.<name>}}`;
- hooks and script checks, as `PROCTOR_PARAM_<NAME>`, with each axis's
  value as `PROCTOR_AXIS_<NAME>`;
- through Harbor, the image as a build argument (`ARG NAME`) for its own
  steps to act on, never the agent's environment, since a parameter can
  say what is planted. `image.params` lists the ones the image reads;
  with no list, every text parameter is an argument. A tree is a
  directory beside the fixture's services, `./params/<NAME>/` (empty when
  nothing sets it), for a compose service to mount and serve.

**Files from git** are cloned once under `.proctor/files/`, and with
`paths` only those files and directories are handed over. A path the
revision does not have stops the run.

**Who does the work** is a value too, usually on one axis named
`assistant`: agent and model seldom vary independently, and two things
that only ever change together are one axis.

```json
"assistant": {
  "about": "the agent and the model that make the change",
  "values": {
    "cc-sonnet": { "note": "Claude Code on Sonnet", "agent": "claude-code", "agent_version": "2.1.301", "model": "claude-sonnet-5-5" },
    "floor":     { "note": "nb on the local model, through Harbor", "route": "harbor", "provider": "local-qcoder", "model": "qwen3-coder-next" },
    "mock":      { "note": "nb on its Mock provider, bare", "provider": "Mock", "model": "mock" }
  }
}
```

A vendor's agent (`claude-code`, `codex`, `qwen-code`) runs through
Harbor at a pinned `agent_version`, on a model of its own vendor. nb
runs bare on this machine unless a value says `"route": "harbor"`; it
names a `provider` (an entry in nb's config) and a `model`. See
[Coding agents](#coding-agents).

**A value's name stands for its content.** Its identity is a hash of
what it sets and carries, its files by content. Edit a value that has
made tries and `proctor run` refuses:

```
exams/tern-docs/exam.json: axes.docs.values.restructure: this is not the docs=restructure that made attempts (docs=restructure,assistant=sonnet).
  A changed value is a new value: give it a new name, and keep the old one as it was.
```

**Live and reference.** `"live": "<value>"` on an axis marks the value
in use now. Every other value of that axis is compared with it, with
every other axis held, and the configuration at the live value of every
axis that has one is the live configuration. An axis nobody chooses (the
repository, a user's own model) has no live value; give it
`"reference": "<value>"` if another axis's value is ever to be looked at
alone, which needs something to hold this one at. An axis with one value
needs neither.

**Configurations.** Nobody spans the cross product. The exam carries the
configurations it names under `configurations` (one value on every
axis), and `proctor run` makes those. `--value axis=value` asks for that
value with every other axis at its live value or reference, named or
not; a configuration with tries in the bank is carried from then on.
`exclude` lists values that cannot go together
(`[{"auditor": "v2", "go": "1.20"}]`).

**An axis of worlds.** When one question is asked in several
repositories, the repository is an axis whose values each name a
`fixture`, and each stage says which it belongs to:

```json
"axes": {
  "repository": { "about": "the repository the change is made in", "reference": "fetchcli",
    "values": { "fetchcli": { "note": "a small fetcher", "fixture": "fetchcli" }, "ledger": { "note": "a statement parser", "fixture": "ledger" } } }
},
"stages": [
  { "id": "add-retry-flag", "when": "repository = fetchcli", "prompt": "…" },
  { "id": "fix-null-deref", "when": "repository = ledger", "prompt": "…" }
]
```

The exam then names no `fixture` of its own. A comparison along another
axis is made in each repository and read across them, each try ranked
only against tries in the same one. `exams/code-change` is written this
way.

**What a value changes** can be seen without running anything:
`proctor build <exam>` builds the starting workspace of the reference
and of each value alone and says what differs, to be read against the
value's note, and checks each configuration of several values against
its values put together. A script that reads two axes shows up there
("more than its values put together"). `--value axis=value` shows one
value's change with its text.

### Predictions

A prediction is a file, `exams/<exam>/predictions/<name>.json`, written
before the tries it is about:

```json
{
  "configuration": "restructure",
  "written": "2026-11-25T16:24Z",
  "changes": [
    { "check": "current-endpoint", "direction": "less", "because": "an agent that copies the quickstart whole ends up on /v1" },
    { "metric": "flagged_steps", "direction": "more", "because": "the units are only in the reference now" }
  ]
}
```

`configuration` is one the exam names; `against` names another to
compare with (absent, what is live). A change is a `check` or a `metric`
(`held`, `flagged_steps`, `tool_calls`, `tokens`, `duration`) with
`more` or `less`. `written` is yours to state and required. `proctor
predict` (or `run`, before it makes the configuration's tries) pins the
checks the prediction was made under. The report says how each change
came out, a miss as plainly as a hit, and says so when tries had already
been made (`post hoc`) or the checks changed since.

### Hooks

Hooks are the exam's and prepare the machine: load the model, build a
tool, start a container. Each is a setup and a teardown script, run on
the host in the exam directory:

```json
"hooks": {
  "configuration": { "setup": "hooks/ensure-model.sh" },
  "try": { "setup": "hooks/try-setup.sh", "teardown": "hooks/try-teardown.sh" }
}
```

`configuration` runs once around each configuration's tries in a
`proctor run`. `try` runs once around each try: its setup after the
checkout and the values' files and before the first stage, its teardown
after the last. What the setup leaves is committed as the try's starting
state, so a stage's diff is the agent's alone. Teardown runs whenever
setup ran, even when setup failed, so it must cope with a half-made or
missing container (`rm -f … || true`). A `try` hook is refused where a
value runs through Harbor: there the agent is in a container the host
cannot prepare, so that setup belongs in the fixture's image.

A hook that branches on an axis to change what the agent sees is hiding
part of a value from proctor, which cannot hash it; put that difference
in the value.

### The program template

`program.nb` is the nb program a value that runs nb is given, with
`{{prompt}}`, `{{stage}}` (the stage's id), `{{work}}` (the
checkout), `{{provider}}`, `{{model}}`, `{{harness}}`,
`{{configuration}}`, `{{attempt}}`, `{{axis.<name>}}` and
`{{param.NAME}}`. A vendor's agent takes the stage's prompt as its
instruction and never sees the template.

Put `model {{model}}` in the template, before `provider {{provider}}`:

```
model {{model}}
provider {{provider}}
harness {{harness}}
```

Then the value's `model` is the model nb asks for, and what the report
records is what ran. Without the line, nb uses the `Model` in the
provider's nb.json entry and the value's `model` is only a label.
`proctor setup` writes entries with no `Model`, so they need the line.
The order matters, because nb builds a provider's client as soon as it
reads `provider`. `proctor setup --check` lists the ids each key can
reach.

## Fixtures

A fixture is the repository the work is done in, with what "done" looks
like in it. Fixtures are repo-level, `fixtures/<id>/`, and reused across
exams:

```
fixture.json     id, source ({path} or {git, rev}), stack, labels, params, default expect, checks, image
checks/*.sh      the fixture's own checks (builds, tests pass); joined to every stage run on it
repo/            the checkout source; the only thing copied into the work directory
```

Proctor checks the fixture out before each try, lays the values' files
over it, runs the try's setup hook, and commits. In order, per stage: nb
compiles the program on the host (a program it refuses loses the try),
the workspace is restored (unless chained), the agent runs, and the diff
is collected as the stage ends. What is copied is `repo/` less `.git`,
any directory named in `source.exclude`, and the caches a tool leaves
where a test was run (`__pycache__`, `.pytest_cache`, `.mypy_cache`,
`.ruff_cache`); the fixture's hash leaves all of these out too.

A fixture's `expect` is a default that a stage's `expect` overlays one
top-level key at a time: a stage's `files_touched` replaces the
fixture's whole `files_touched`. The merged block is what `"@expect"`
reads and what scripts see as `PROCTOR_EXPECT`.

### Services

A fixture's `image.services` names a directory beside `repo/` holding a
`docker-compose.yaml`; through Harbor its services run beside the
agent's container and share its network, so the agent reaches them on
`localhost`, and the model's host stays the only other name it can
reach. A tree parameter is beside them as `./params/<NAME>/`, so a
service can mount and serve it: a docs site, say, that the agent reads
over HTTP and cannot grep. `fixtures/tern-shop` serves `DOCS` this way,
and `exams/tern-docs` compares two versions of those docs with none. How
to write one, and what the agent can and cannot see of it, is
[`service-fixtures.md`](service-fixtures.md).

`image.allowed_hosts` names hosts the agent may reach beside its model's
(`"allowed_hosts": ["api.example.com"]`), for a service that cannot
sensibly be simulated. Each is a name or an address as the agent will
spell it, with no scheme, port or path: Harbor's allowlist is by host,
it matches a name exactly, and every web port of an allowed host is
open. The list is part of what the agent meets, so adding a host makes
the earlier tries stale.

Two more fields of `image` are about ports at the agent's `localhost`:

- `serves` lists the ports the fixture's own services answer on
  (`"serves": [8765]`). It matters when a model endpoint is one of them,
  as with `fixtures/stub`: a base URL at `localhost` on a served port is
  the fixture's service, and on any other port it is the machine's own
  (below).
- `host_ports` lists ports of the machine that runs proctor which the
  agent is to reach at its own `localhost` (`"host_ports": [5173]`): a
  dev server you started by hand beside the run, say. Each is relayed
  like a model on the machine's loopback, and only those ports. A try
  made this way depends on something the exam does not hold, so it is
  for an exam's early days.

A model whose base URL is `localhost` or a `127.` address, on a port no
fixture service serves, is the machine's: `run` relays that port to the
agent's `localhost` and gives the agent's phase no network. The log
entry records the ports (`harbor.relay`). `docs/podman.md`, section 5,
says how it is carried.

## Checks

Checks live at three levels and a stage carries the union: the exam's
`grading.checks` on every stage, a fixture's `checks` on every stage run
on it, and a stage's own `checks` on that stage alone, for the outcome
only it can state. A name declared at two levels is a problem. A check
named in a role's list that the exam does not declare must reach every
stage from its fixture or its own block.

A check on the transcript reads that stage's transcript. A check on the
workspace (`files_touched`, a script) sees the workspace as that stage
left it, and `files_touched` reads what that stage itself changed. Each
stage has its own `checks.json` and `diff.patch`. A stage that was not
reached is not graded.

A check has one of four roles, set by the list under `grading` that
names it, and a check is in one list at most:

| role | list | what it does |
|---|---|---|
| correctness | `correctness` | Was the stage completed as intended. Together these decide whether it is right. Deterministic checks only: one that asks a model (`judge`, `decide`) is refused here. |
| integrity | `integrity` | Can the result be trusted. A try with a stage that fails one is listed and not counted; an `error` or `needs-judge` there excludes nothing. |
| quality | `quality` | Something you prefer and do not require. Reported, and decides nothing. A judged check belongs here. |
| observation | none | Something you want counted, with no preferred answer. |

```json
{ "id": "make-bat-implement-ifoo",
  "prompt": "Make `Bat` implement `IFoo`. Its existing callers must not change.",
  "expect": { "files_touched": { "paths": ["src/Bat.cs", "tests/**"], "mode": "at_most" } },
  "checks": {
    "callers-untouched": { "not_files_touched": { "paths": ["src/Consumers/**"], "mode": "at_least" }, "description": "no file under src/Consumers was changed" }
  } }
```

A check may carry `"when": "<axis> = <value>"`, as a stage does: where
it does not hold the check does not apply, no script runs, no judge is
asked, and it counts toward nothing.

**A grade names its standard.** `grade` writes `checks.json` (the
verdicts as they are now) and keeps each grade under the checks it was
made with (`grades/`). A try with no grade under the checks as they are
now is not yet graded: it is in no number until `grade` reads it, and a
comparison that would read it is refused, so both sides are always under
one standard. When a check is narrowed, added or removed after tries
were graded, `grade` and the report say so ("the standard moved"),
because a number can move with nothing re-run.

Every id a report prints carries a sentence beside it. An exam takes a
`description`, a stage an `about` (without one, the first line of its
prompt), a value a `note`. A check takes `description` as a field beside
its spec, and a script check must have one, because from outside a
script says nothing:

```json
{ "acceptance": { "script": "checks/acceptance.sh", "description": "the stage's acceptance tests pass against the changed repository" } }
```

A built-in check describes itself from its spec (`{ "denied_calls": {
"max": 0 } }` reads as "no denied tool calls") unless you give it a
better sentence.

A check also takes `why`: one sentence on why it is worth checking, as
against `description`, which says what it does. ".env is in .gitignore"
is a description; "secrets in .env stay out of git" is a why. A viewer
shows it beside a failure, so the reader learns what the author was
protecting, not only what tripped:

```json
{ "no-secrets": { "script": "checks/no-secrets.sh", "description": "no key or secret value is in a written file", "why": "an exam that leaks a key would publish it with the try" } }
```

`proctor list` names the checks that have none. Four common checks get
proctor's own reason when they have none: an exit reason of `ok`, no
denied calls, no loop nudge, and a budget of calls, tokens or time.
`grade` writes each check's description, why and (for a script) its path
into every verdict in `checks.json`, as they were when it graded. A
built-in check also writes `evidence`: where in the stage it found what
it looks for, as trajectory steps (`{"step": "7"}`) or diff paths
(`{"file": "src/x.cs"}`). `tools_used` points at the calls, `answer_*` at
the answer, `result_matches` at the matching results, `denied_calls` and
`tool_errors` at the offending steps, `files_touched` at the files its
mode is about. A check on the run as a whole (exit reason, budgets, the
nudge, the oracle) has none, and so does one that found nothing.

### What a script is given

Hooks and script checks get the stage in the environment:
`PROCTOR_EXAM_DIR`, `PROCTOR_FIXTURE` (the fixture's directory, so a
script can read its `fixture.json`), `PROCTOR_EXAM` (its id),
`PROCTOR_CONFIGURATION` (the configuration's key, `axis=value,…`),
`PROCTOR_AXIS_<NAME>` and `PROCTOR_PARAM_<NAME>`, `PROCTOR_STAGE` (the
stage's id), `PROCTOR_ATTEMPT` (the try's number), `PROCTOR_ATTEMPT_DIR`,
`PROCTOR_WORK`, `PROCTOR_STAGE_JSON`, `PROCTOR_EXPECT`,
`PROCTOR_TRANSCRIPT`, `PROCTOR_DIFF`, `PROCTOR_RUNNER`, `PROCTOR_NB`,
`PROCTOR_NB_CONFIG`, `PROCTOR_CONTAINER` and `PROCTOR_WORK_MOUNT`. A
configuration hook, which runs once and not per try, gets `PROCTOR_EXAM`,
`PROCTOR_CONFIGURATION`, `PROCTOR_AXIS_<NAME>`, `PROCTOR_EXAM_DIR` and
`PROCTOR_RUNNER`. A script exits 0, 1 or 2 for pass, fail or
needs-judge, and its first line of output is the reason; any other exit
is `error`, never a fail.

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
| `read-internals` | the agent read the surface's own source or notes, or went looking for where it is installed | yes |
| `used-substitute` | the agent used a declared substitute for the surface | yes |
| `fell-back` | the agent used a substitute within two steps of a call to the surface that failed | yes |
| `searched-docs` | the agent searched the surface's docs | yes |
| `ran-help` | `--help`, `-h` or `man` | no |
| `inline-script` | a script run inline, or a file written to scratch | yes |
| `machine-refused` | the machine refused: command not found, permission, timeout, network | no |
| `harness-refused` | the agent's own tool refused: output too large, a rejected call, an edit that did not apply | no |

Each label reads on its own, met bare as a code in a table; the file
carries each label's sentence (`detector.labels`, `[{name, describes}]`),
so a viewer words nothing itself. Every point carries a quote from the
call it cites, checked the way a judge's quote is checked; a point with no
quote that is in the call is not written. Each evidence item says which
`rule` fired and `because`: one sentence the rule writes from what it
already holds ("used localhost:8081, a declared substitute, one step after
the call to localhost:8080 at s8.1 failed (HTTP 404)"). The summary's
`flagged_steps` counts the steps inside any point, each once, whatever the
label: friction's one measure. The rules use no model, so the same trajectory and declaration
give the same file.

Rules see what a call was and what came back, not what it was for.
`proctor friction <exam> --reader <judge>` adds a reader: a chat judge from
`proctor.json` reads each attempt once and names five more kinds of point,
`fixed-own-work` (fixing its own work after something it ran failed),
`wasted-read` (a search or read that led to nothing used later), `reread`,
`bad-instruction` (following an instruction that could not work as given)
and `workaround`. The reader is asked in its prompt's original words
(`repair`, `lost`, `dead-end`), which keep every kept reading valid, and
its answers are written under these names. The reader must quote the
call, and a point is written only when the call exists, the label is one
of the five and the quote is in that call; its one-line reason is the
evidence's `because`, and is kept as a note too. Its points carry
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

A stage can name an **answer sheet**, a scripted user that answers when
the agent stops to ask: `"oracle": "oracle/<stage>.md"`, a file inside
the exam directory in nb's format (`## <id>`, then the answer,
verbatim). After a turn that ends `ok`, a judge reads the agent's last
message against the sheet. On a hit the selected answers become the next
user turn and the stage goes on; when the agent asked for something the
sheet lacks, the stage ends `oracle_miss`. `exam.json`'s `oracle` block
sets how many answers a stage may get (`turns`, default 8, then
`oracle_budget`) and which nb config entry judges (`judge`; by default
the provider doing the work). The checks are `oracle_hit` (a list of
entry ids the oracle must have answered with), `oracle_turns {max}` and
`oracle_misses {max}`.

Bare, nb runs its own oracle. Through Harbor (`"route": "harbor"`) a
simulated user on the host sends the prompt and each answer to nb over
Harbor's ACP bridge and judges with the host's nb, so the sheet never
enters the container; the program must then end in exactly
`run {{prompt}}`, and the try must be that one stage, since the
simulated user answers a task and never one step of several: give the
stage a `when` that makes it a try of its own (`exams/gate-oracle`). A
vendor's agent beside a stage with a sheet is refused, because Harbor
leaves a bridged vendor agent no trajectory to grade.

## Labels

Labels are yours: a key with a string or a list of strings, on the exam
or a fixture, and proctor never interprets a key. An exam carries its
fixtures' labels with its own laid over them, key by key. They print
with `list`, filter it (`--label kind`, `--label area=coding/*`,
repeatable; `*` matches within a slash segment and `**` across), and sit
on every row of `results.jsonl` so a later report can group on them
without re-reading an exam that has since changed:

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
and the reasoning stays in the stage's `verdicts/` file and never reaches the
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
`diff` on a stage that changed nothing or `answer_json` with no JSON
fence, is `error` before any call. `tool_calls` cuts each call's arguments
at 400 characters and shows no output; `tool_results` keeps each result's
first 40 lines; `max_window` caps the parts together. No window or prompt
carries the configuration, the model or the try's number. A verdict is kept in
`verdicts/` and reused by the next `grade`; its hash covers the request,
the judge's model, the threshold and the expectation, so changing any of
them asks again, and an `error` is always asked again.

`expect` may be `"@expect"`, read from the stage's `expect.<check name>`;
so may `ask`, read from `expect.<check name>.ask`, so one `correct` check
can carry a rubric per stage. A built-in field set to `"@expect"` reads
`expect.<check name>.<field>` first and `expect.<field>` second.
`with` names a judge from `proctor.json`; without it the only judge of the
needed kind is used, and several of that kind with no `with` is a problem
`list` and `run` report. Whether an exam is judged is not declared; it follows
from a check that asks a model, and `list` says so.

## Coding agents

A value that names an `agent`, or says `"route": "harbor"`, runs through
[Harbor](https://github.com/harbor-framework/harbor), in a container
built from the fixture's image, instead of running nb on this machine.
The agent is a vendor's own CLI at a pinned version (`claude-code`,
`codex`, `qwen-code`), or nb itself:

```json
"cc-haiku": { "note": "Claude Code on Haiku", "agent": "claude-code", "agent_version": "2.1.301", "model": "claude-haiku-5-5" },
"floor":    { "note": "nb on the local model, through Harbor", "route": "harbor", "provider": "local-qcoder", "model": "qwen3-coder-next" }
```

`exam.json` pins Harbor and the time each stage may take:

```json
{ "harbor": { "version": "0.26.0" }, "limits": { "wall_ms": 1800000 } }
```

and the fixture describes its image: `"image": {"from": …, "run": […],
"env": {…}}`, in which proctor commits `repo/` (with the values' files
laid over it) and installs the agent at its pin, so a trial installs
nothing and needs no network to start.

The machine needs [uv](https://docs.astral.sh/uv/), which runs the pinned
Harbor, and a container engine, set by `harbor.engine` in
`exams/proctor.json`:

- **Docker** (the default), with the Compose and buildx CLI plugins.
  Ubuntu's `docker.io` package brings neither: install `docker-compose-v2`
  and `docker-buildx` beside it. Without buildx every Harbor attempt fails
  building its egress sidecar with `unknown flag: --file`, the bare
  attempts untouched. Rootless Docker works (the gates and conformance
  pass on it): set
  `DOCKER_HOST=unix:///run/user/<uid>/docker.sock` in proctor's env file,
  since Harbor runs under uv and a client there may not read CLI contexts.
  The rest is [`docker.md`](docker.md).
- **Podman**, rootless, with its API socket running, docker-compose v2
  behind `podman compose`, and three lines of user config. The setup,
  and why each step is there, is [`podman.md`](podman.md);
  `run` checks what it can before it starts.

Rootless podman on Linux (Arch and Ubuntu) is the engine that is
supported: it is where proctor's own gates are run. Docker is not
maintained. An engine off the machine (Modal) was in proctor until
2026-11-27 and is not now: `harbor.engine: modal` is refused, and its
code is at the git tag `modal-in-core`.

What an agent's container may reach on the network, and how to see it,
is [`network.md`](network.md).

The same file says where each vendor agent's model calls go and where nb's
self-contained linux-x64 publish is, for nb through Harbor:

```json
{
  "agents": { "claude-code": { "base_url": "${LLM_GATEWAY}/anthropic", "api_key": "${LLM_GATEWAY_KEY}" } },
  "harbor": { "uv": "uv", "engine": "podman", "nb": "../../nb/artifacts/linux-x64" }
}
```

From the moment it starts until it is removed, the agent's container
reaches only the host of its model's base URL, so a process the agent
leaves running reaches no more than the agent did; the checks run
afterwards with no network at all. One exception: a stage with an
`oracle` keeps the open network outside the agent's own turn, because
Harbor installs the bridge its simulated user talks through when the
trial starts.

A try through Harbor is one Harbor task whose steps are the stages:
the environment is built once, the agent starts a fresh conversation
per stage, and `limits.wall_ms` is each stage's, not the try's. On a
chained exam Harbor still runs the stages after a stopped one; proctor
does not read them.

Through Harbor a script check runs in Harbor's verifier, a separate
container, when the try is made, from a copy of the exam directory and
the fixture compiled when its run started (kept under
`log/<id>/harbor/`); `grade` reads the verdict it wrote and never runs
the script again. So its verdict is fixed when the try is made. Change
the script, any file beside it in the exam or the fixture, or the
stage's expected values, or add a script check, and `grade` marks those
verdicts `outdated`: out of the check's numbers and, for a correctness
check, the stage undecided, never counted as a fail. They stay outdated
until the try is made again (move its directory out of `bank/<exam>/`,
which `grade` names, and `proctor run` the exam). An exam's or a
fixture's own `README.md`, `exam.json`, `fixture.json`, the predictions
and another check's script outdate nothing. Built-in and judged checks
on the same tries are graded afresh as anywhere.

`agents.codex.config` may name a native Codex `config.toml` (relative to
`exams/`), merged under Harbor's base URL. Use it for what an endpoint
needs of the agent: a llama-server takes only function tools, for example.
What a fixture's image needs is
[`porting-to-agent-neutral.md`](porting-to-agent-neutral.md);
`exams/code-change/` runs the same three changes on four agents.

Harbor pins its own version but not its dependencies' versions, so proctor
resolves them with uv's `--exclude-newer` seven days back. A poisoned
release that is pulled within a week never reaches you. If the account
that runs agents holds credentials worth stealing, `harbor.uv` can
point at a wrapper that runs uv in a sandbox with your home directory
hidden. [`podman.md`](podman.md) §8 has one.

To check that a machine can run them, without a model and at $0:

```bash
PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests
```

This runs every vendor agent at its pin against a scripted stub model,
then each `code-change` stage's reference solution through Harbor's
verifier. It takes about twenty minutes. `PROCTOR_CONFORMANCE` may
instead name a `proctor.json` whose `harbor` block to use.

## Running bare nb in a container

nb's runner is the older way to put nb in a container, for an exam whose
hooks make the container themselves. For a new exam with tools, prefer
`"route": "harbor"`.

An exam that runs nb inside a container names the script that runs it in
its `exam.json`, beside the hooks that make the container;
`--runner <script>` (a path from the current directory) overrides it for
one `proctor run` and `--runner none` runs bare. The contract is the
whole interface:

| proctor gives the runner | the runner must |
|---|---|
| stdin: the resolved program, compiled on the host by `nb --compile` so every `@file` include is inlined | pass it to nb's stdin unchanged |
| cwd: the work directory on the host | start nb with the checkout as its working directory, wherever that is inside |
| the stage's environment (`PROCTOR_*`, which includes `PROCTOR_NB`, the host binary, `PROCTOR_NB_CONFIG`, `PROCTOR_WORK_MOUNT` and `PROCTOR_CONTAINER`) and `NO_COLOR` | run nb where the checkout and the config are at the paths those name |
| nothing on argv | run `nb --output jsonl [--config <config>] -` |
| stdout and stderr captured to the stage's directory | put only nb's stdout on stdout |
| | exit with nb's exit code |

`PROCTOR_CONTAINER` is a name for the try, `proctor-<exam>-<try id>`,
that proctor never uses, so hooks and the runner can agree on one
container: the try's setup hook creates it, the runner execs into it for
each stage, the try's teardown hook removes it. Hooks see which is in
effect in `PROCTOR_RUNNER`, empty on a bare run, and skip the container.
The manifest records the script and its hash.

```bash
proctor run <exam>                 # each try in its own container, as exam.json says
proctor run <exam> --runner none   # bare: the shakedown, on this machine
```

A bare run is for shaking down an exam: nb and the model's tools run on
this machine as you. A container is for anything you would not run on
your own machine, which is every real exam, since the model runs
whatever it decides to. How to build the image, what to mount, who owns
the files the model writes, and what nb leaves within its reach is nb's
runbook,
[`docs/containers.md`](https://github.com/breitreiter/nb/blob/HEAD/docs/containers.md).
Each stage keeps `program.nb`, the source as resolved, and
`program.jsonl`, what actually went down stdin.

The exam's `nb` block names the runner, relative to the exam directory
like a hook, and `mounts`, where the runner will show nb the checkout:

```json
{ "nb": { "runner": "../runners/container.sh", "mounts": { "work": "/work" } } }
```

With a runner in effect, `{{work}}` in the program resolves to that
path, so the model is told where things are inside the container. Hooks
and checks run on the host and keep `PROCTOR_WORK` as the host path;
`PROCTOR_WORK_MOUNT` is the path the model was told, which falls back to
the host path when nothing is mounted. A bare run ignores the mounts,
which is what makes `--runner none` a shakedown of the exam on the host.
A runner script is given the whole environment, `PROCTOR_EXPECT` (the
answer key) included, so it must never forward its environment into the
container wholesale: pass the variables nb needs by name
(`-e LLM_GATEWAY`). Bare nb gets only `NO_COLOR`. A mount is an absolute
path inside the container, and `runner` or `mounts` left in
`proctor.json` is a problem, not ignored. Script checks run on the host
after the stage, so a check that needs a toolchain only the image has
starts a container of that image itself.

## Examples

`exams/playground/` is the first to run: two scripted stages on nb's
Mock provider, no model and no key. `exams/smoke/` with `fixtures/note/`
uses most of what is described here, also on Mock. `exams/readme-answers/`
and `exams/tern-docs/` are the two real ones to read: a tree parameter
the program reads, and one a service serves. `exams/code-change/` has an
axis of worlds. `exams/gate/` and `exams/conformance/` are proctor's own
checks of its Harbor path: nb on Mock, bare and through Harbor, must
agree, and each vendor agent on the stub must come back as scripted. The
check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md`.
