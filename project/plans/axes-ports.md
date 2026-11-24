---
type: plan
title: Every exam on paper as axes — slice 0 of `axes-build.md`
created: 2026-11-24
status: written 2026-11-24 from the thirteen exams as they stand, for the user. No code. The declaration's form is a proposal; the findings are what the exams showed. Stop point: nothing in slice 1 starts until the user has read "What did not fit"
---

# Every exam on paper as axes

"Variant" below means only what proctor has today.

## The short of it

- **What is under test fits, in all thirteen.** Every variant field
  becomes a value; no hook has to branch on what is under test; nothing
  needed a value that adds a step. Two exams (`exam-authoring`,
  `smoke`) get simpler: their hand-written grids of variants are two
  axes.
- **One real hole, and it is not in axes: one question asked of several
  worlds.** Six exams have work that cannot share one environment. Four
  are the ones stages already left split. Axes offer a home for them
  that stages alone did not (finding 1). It is the decision this
  document is for.
- **One thing the model does not say: when two things are one axis.**
  Agent and model are rarely independent (finding 2). It needs
  guidance, not a feature.
- Six smaller findings, each with a proposal (3 to 8).

Against the signs in `axes-build.md`: sign 1 (exams that do not fit) is
clear for what is under test, and open for worlds. Sign 2 (two axes
needing one path) did not come up once. Sign 6 (wanting the bundle
back) did not come up; the nearest is finding 4.

## The declaration (agreed in conversation, 2026-11-24)

First written here as six kinds of thing a value could set (`files`,
`args`, `env`, `services`, `program`, the agent's fields). The user's
worry: a set defined now meets edge cases later. So proctor interprets
almost nothing. **A value is a blob of named parameters; the base's own
scripts decide what to do with them.** Those scripts exist already: the
fixture's image steps, its service definitions, the nb program
template, and the hooks. No new scripting language: on Harbor the
environment is a Dockerfile and a compose file, and anything else
would compile to those.

**The base declares its parameters**, in `fixture.json` (or, for the
program template, in `exam.json`):

```json
"params": {
  "PLANTED": { "default": "none", "about": "which defect is planted" },
  "DOCS":    { "default": null, "tree": true, "about": "what the docs site serves" },
  "API_KEY": { "secret": true, "about": "the sandbox account's key" }
}
```

A parameter is text, or with `tree` a directory of files. `secret`
passes it by name and keeps it out of the bank. The image may list the
parameters it reads (`image.params`), so a change to any other does
not rebuild it; with no list, every parameter does.

**The exam declares its axes**, in `exam.json`:

```json
"axes": {
  "planted": {
    "about": "which defect is planted in nb",
    "values": {
      "none":      { "note": "nb as pinned" },
      "far-cause": { "note": "the symptom is far from the cause", "set": { "PLANTED": "far-cause" } }
    }
  },
  "docs": {
    "about": "jot's README",
    "live": "as-shipped",
    "values": {
      "as-shipped":  { "note": "as it ships", "files": { "path": "values/docs/as-shipped" } },
      "for-scripts": { "note": "with a section for scripts", "files": { "path": "values/docs/for-scripts" } }
    }
  },
  "assistant": {
    "values": { "qwen": { "note": "Qwen Code on the local model", "agent": "qwen-code", "agent_version": "0.25.2", "model": "qwen3-coder-next" } }
  },
  "repository": {
    "values": { "fetchcli": { "fixture": "fetchcli" }, "ledger": { "fixture": "ledger" } }
  }
},
"configurations": { "shipped": { "docs": "as-shipped", "assistant": "qwen" } },
"exclude": [ { "auditor": "v2", "go": "1.20" } ]
```

A value holds a `note` and any of:

| Key | What it is | Who acts on it |
|---|---|---|
| `set` | declared parameters, by name | the base's scripts. Proctor hands every parameter over as a build argument, in the attempt's environment, as `{{param.NAME}}` in the program template, and to services |
| `files` | a tree laid over the checkout (`path`, or `git` + `rev` + `paths`; `to` for a subdirectory) | proctor |
| `agent`, `agent_version`, `route`, `model`, `provider`, `harness` | who does the work | proctor |
| `fixture` | a whole base, for an axis of worlds (finding 1, B) | proctor |

What proctor still does itself, and why:

- **The overlay.** A bare run has no container to script, and the
  overlay is committed before the first stage, so a stage's diff and
  `files_touched` show only what the agent changed and a fresh stage
  resets to the base with the overlay.
- **Agent and model.** Proctor installs and routes them. `route` is
  `bare` or `harbor`; today that is `runner: nb` against `agent: nb`.
- **The names.** A parameter the base does not declare is refused at
  load, as is one parameter set by two axes, one path written by two
  axes' overlays, and an overlay that reaches the answer key.

What is given up, knowingly:

- Path conflicts are checked for the overlay only. Where a script puts
  things is the script's business.
- The page shows what a value is (its parameters and files), not what
  the scripts did with it.
- **A script that reads something undeclared is not caught** (the
  network at build time, the clock, a host file). True of today's
  hooks and image steps as well. A changed script is caught, since the
  scripts are in the base's hash. And the manifest records the built
  image's digest, so two attempts with one hash and two digests show
  as a difference in conditions: drift made visible, not prevented.

Also:

- A value with only a note sets nothing and gets the base's defaults.
- A stage may carry `"when": "<axis> = <value>"`.
- A hook runs once per configuration where it ran once per variant,
  and reads `PROCTOR_AXIS_<NAME>` (the value's name) and
  `PROCTOR_PARAM_<NAME>` for declared names only.
- A program template reads `{{axis.<name>}}` and `{{param.<NAME>}}`.
  `{{bundle}}` goes; overlaid files are in `{{work}}`.
- Files a value carries live at `exams/<exam>/values/<axis>/<value>/`
  by convention.

## What did not fit

### 1. One question, several worlds (the decision)

Six exams ask one question in environments that cannot be one fixture:

| Exam | The worlds | Why they cannot share a fixture |
|---|---|---|
| `code-change` | `fetchcli`, `ledger`, `widgets` | three repositories |
| `agent-onboarding` | three chatbot projects | three repositories |
| `exam-authoring` | four workspaces | four repositories; one also needs an interrupted run planted by a hook |
| `nb-changes` | nb at one revision with nothing planted, or one of four defects | the same repository and image, a different planted patch each |
| `gate-oracle` | three oracle scripts | Harbor refuses the simulated user on a task of several steps |
| `conformance` | `ok`, `hang`, and `fail` | `fail` is an outage on purpose, which loses the try for the stages beside it |

Stages said: these are several exams, and something above an exam
rolls them up (`todo.md`). Axes offer a second answer. Three ways:

**A. Several exams and a group above them.** `code-change-fetchcli`,
`code-change-ledger`, and so on, with grading, program and axes copied
or shared through the group; `run` and the report learn to take a
group. New concept, new file, and the copies drift.

**B. The world is an axis; a stage says which world it belongs to.**

```json
"axes": {
  "repository": { "values": { "fetchcli": { "fixture": "fetchcli" }, "ledger": { "fixture": "ledger" }, "widgets": { "fixture": "widgets" } } },
  "assistant":  { "live": "cc-sonnet", "values": { "cc-sonnet": { … }, "codex": { … } } }
},
"stages": [
  { "id": "add-retry-flag", "when": "repository = fetchcli", … },
  { "id": "fix-null-deref", "when": "repository = ledger", … },
  { "id": "rename-module",  "when": "repository = widgets", … }
]
```

One exam, one grading, one set of values. `when` on a stage is already
in `axes.md` (not applicable). A try is still one environment and the
stages that apply in it. The comparison "is Codex better than Claude
Code at this" is a comparison along `assistant`, **pooled across
`repository`**, which `axes.md` already has as a stated assumption with
the split always shown. For `nb-changes` the value is small and
declared (`set: { "PLANTED": "far-cause" }`), and this is exactly the
Go-version case. For the three with whole repositories the value names
a fixture, which is bigger than decision 10 allows a value to be, so
`fixture` would be one more axis that is proctor's own, like `agent`.

**C. A stage carries its own starting state** (its fixture or patch).
This is tasks again, the dead end the user ruled out. Listed only to
say it was considered.

**I recommend B**, with two things said plainly:

- It makes the pooled comparison the main result for these exams. What
  step 4 built compares tries within one configuration; here a try in
  `repository=fetchcli` has one stage, so its own comparison is nearly
  empty and everything rests on the pool. The pooled test must be
  designed (paired by world, as the old report paired by task), and it
  is the first thing slice 3 has to get right.
- It sits close to the line the user drew. The difference from tasks:
  a stage holds no configuration, only a condition; the environment
  belongs to the configuration, and the grid shows every one. If that
  reads as nested exams on the page, B is wrong and A is the fallback.

`gate-oracle` and `conformance` are not helped by B and stay as
planned: `gate-oracle` waits for a one-stage exam to compile as a plain
Harbor task and is then three small exams or, with B, one exam with a
`script` axis; `conformance`'s `fail` is an exam of its own, since a
control whose right answer is "it did not run" cannot be a stage.

### 2. Agent and model are seldom independent

`code-change` has five variants: Claude Code on Haiku and on Sonnet,
Codex on GPT-5.5, Qwen Code and nb on a local model. As two axes that
is a grid of sixteen with eleven impossible, and "Codex against Claude
Code" changes two axes at once, so no claim could name it.

Nothing needs building. One axis, `assistant`, whose values each set
agent, version and model, holds all five, and any two compare. Two
axes are right only where both are really varied: `exam-authoring`
(two doc sets, two models, all four run).

**The guidance, for the skill:** an axis is what you compare along.
Two things that only ever change together are one axis. The old model
is the special case of one axis.

### 3. A hook that prepares the machine

`ensure-model.sh` (four exams) branches on the variant's id to decide
which model the local box should serve. It changes nothing the agent
sees, and under axes it reads the declared `assistant` value's model,
which is better than today: keyed on the model, not a name. Fits.

### 4. A value that needs something built from it

`exam-authoring` shows the agent proctor's docs at a pinned revision
and, beside them, a build of proctor at that same revision. The docs
are the value; the build is derived from it by the variant hook, which
reads `rev` out of the variant's file. Under axes the hook reads
`PROCTOR_AXIS_DOCS_JSON` for the same thing. It fits the rule (the
hook is the base's, and reads only a declared axis), but note what it
is: a step whose input is a value. The value's diff on the page will
show the docs and not the build.

### 5. Read-only goes

Today's bundle is mounted read-only beside the checkout. An overlay is
in the checkout and writable. An agent that edits the docs it was
given is then visible in the diff, which is arguably better, and an
exam that cares adds a `files_touched` guardrail. `exam-authoring`'s
`diff-in-scope` check must exclude or guard the overlaid tree.

### 6. Setup for one stage

Two hooks act for one task: `exam-authoring`'s attempt hook plants an
interrupted run when the task is `continue-the-batch`, and `smoke`'s
teardown writes the file its checks then find. With stages an attempt
hook runs once around the try, and each stage's diff is taken before
the teardown. Under B the first becomes part of that world's fixture
(an image step). `smoke`'s trick has to become a scripted tool call.
No per-stage hook is proposed.

### 7. `exam-authoring` is two ports

It still runs nb in a hand-built container (`nb.runner`, `mounts`),
the path before Harbor. An image argument or a service value needs the
fixture's image, so it moves to Harbor first
(`docs/porting-to-agent-neutral.md`), then to axes.

### 8. Provider rides in the value

A bare nb value names a `provider`, which is an entry in this
machine's `nb.json`, not content. It stays in the value's identity as
today. Under a shared bank two machines must use the same provider
names for their attempts to pool; that is a line for the hosting plan.

## The ports

Each as it would stand. `assistant` values are abbreviated to what
they set.

| Exam | Axes | Live | Stages | Notes |
|---|---|---|---|---|
| `readme-answers` | `docs`: as-shipped, for-scripts (files: `README.md`). `assistant`: flash-lite (nb, bare, gemini) | `docs=as-shipped` | 6 | The program reads `@{{work}}/README.md`. The README at the root, as in `axes.md` |
| `tern-docs` | `docs`: as-shipped, restructure, none (service `docs`, content). `assistant`: cc-sonnet | `docs=as-shipped` | 6 | The base declares the docs service's content directory. `none` sets nothing |
| `playground` | `assistant`: mock | — | 2 | Six lines of declaration in the first exam a newcomer reads |
| `cli-for-agents` | `assistant`: local | — | 1 | |
| `smoke` | `assistant`: mock, mock-b. `extra`: none, smoke (files) | — | 3 | Today's a and b differ in two things at once; as axes it is a free 2 × 2. Finding 6 |
| `gate` | `route`: bare, harbor. `assistant`: mock | — | 5 | Its claim is "nothing changes along `route`" |
| `oracle-change` | `route`: bare, harbor. `assistant`: haiku | — | 1 | Needs the plain-task compile (slice 4) |
| `gate-oracle` | `route`: bare, harbor. `assistant`: mock. With B, `script`: asks-twice, misses, over-budget | — | 1 per script | Finding 1 |
| `conformance` | `assistant`: claude-code, codex, qwen-code, each on the stub | — | 2, and `fail` apart | Finding 1 |
| `code-change` | `assistant`: cc-haiku, cc-sonnet, codex, qwen, floor. With B, `repository`: fetchcli, ledger, widgets | to choose | 1 per repository | Findings 1, 2, 3 |
| `agent-onboarding` | `assistant`: qwen. With B, `project`: toolshed, allotment, bikekitchen | — | 1 per project | Findings 1, 3 |
| `nb-changes` | `assistant`: qwen. With B, `planted`: none, far-cause, red-test, report-only, two-causes (`set`) | — | 2 on none, 1 on each defect | Finding 1. The cleanest case for B: one image argument |
| `exam-authoring` | `docs`: docs, docs-and-examples (files from git). `model`: qwen, haiku. With B, `workspace`: four | `docs=docs`, to confirm | 1 per workspace | Findings 1, 4, 5, 6, 7 |

## Answered (the user, 2026-11-24)

- **Finding 1: B.** The world is an axis and a stage says which world
  it belongs to, "now that we're stretching axes a bit". So slice 1
  includes `when` on a stage and `fixture` as an axis that is proctor's
  own, and slice 3 starts with the comparison pooled across a world
  axis. A stays the fallback if it reads as nested exams on the page.
- **Finding 2's guidance: reasonable, with a caveat.** One `assistant`
  axis does not hold for exams about nb itself, which will vary many
  harness internals independently (the prompt, the tools, loop and
  budget settings). There the agent and model are fixed and each
  internal is an axis of its own. Each internal is then a declared parameter the program
  template reads; no kind of its own is needed.
- **The declaration's form:** settled as "The declaration" above:
  parameters and the base's scripts, not kinds.

## For the user

1. **Finding 1: A, B, or something else?** Everything else in slice 1
   can start either way, but `when` on a stage, `fixture` as an axis
   and the pooled comparison are in or out by this answer.
2. **The declaration's form**, as proposed above.
3. **Finding 2's guidance**: one `assistant` axis unless agent and
   model are varied independently. It means decision 3 ("agent and
   model are axes") reads as "may be", not "always are".
