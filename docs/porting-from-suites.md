# Porting an exam to stages and axes

An exam written as tasks and variants (`exams/<id>/tasks/`,
`exams/<id>/variants/`), or a suite of proctor 0.3 or earlier
(`suites/<id>/suite.json`, its arms), does not load any more. This is
what to change. The reference for every field is
[`configuration.md`](configuration.md); the worked examples are
`exams/readme-answers` (a model answering, no sandbox),
`exams/tern-docs` (a coding agent in a container) and
`exams/code-change` (one question in three repositories).

`proctor list <exam>` on an exam of the earlier shape says what it found
and what each part becomes.

## The words

| Then | Before that | Now |
|---|---|---|
| exam | suite | exam |
| task | task | stage |
| variant | arm | a configuration: one value on every axis |
| a variant's fields | an arm's fields | a value of an axis |
| a variant's bundle | an arm's bundle | a value's `files`, or a tree parameter it sets |
| attempt | sample, cell | a try is every stage once; an attempt is one stage of it |
| the bank | experiment, `runs/<id>/` | the bank, `bank/<exam>/<try id>/` |
| the live variant | baseline | the live value of an axis |

## The files

| Then | Now |
|---|---|
| `tasks/<task>.json`, one per task | `stages: [...]` in `exam.json`, in the order they run |
| a task's `fixture` | `fixture` on the exam, once; or an axis of worlds (below) |
| a task's `description` | the stage's `about` |
| a task's `labels` | the exam's `labels`; a stage has none |
| `variants/<id>.json`, one per variant | a value under `axes` in `exam.json`, and a name under `configurations` |
| a variant's `note` | the value's `note`: what it changes for the agent |
| `runner: nb`, `harness`, `provider`, `model` | a value with `provider` and `model`; nb runs bare unless it says `"route": "harbor"` |
| `agent: nb` | `"route": "harbor"` |
| `agent`, `agent_version`, `model` | the same, on a value |
| `bundle: {path}` or `{git, rev, paths}` | `files: {…}` (laid over the checkout), or `set: {NAME: {…}}` for a tree a service or the program reads |
| `attempts` | `tries`: how many tries every named configuration should have |
| `hooks.variant`, `hooks.attempt` | `hooks.configuration`, `hooks.try` |
| `live.json`, `proctor live` | `"live": "<value>"` on the axis |
| `--variant`, `--tasks` | `--configuration <name>`, `--value axis=value`; a stage is never run alone |

In `program.nb`, `{{variant}}` is `{{configuration}}`, and `{{bundle}}`
is gone: files a value carries are under `{{work}}`, and a tree it sets
is `{{param.NAME}}`. In hooks and script checks:

| Then | Now |
|---|---|
| `PROCTOR_VARIANT` | `PROCTOR_CONFIGURATION` (the key, `axis=value,…`), and `PROCTOR_AXIS_<NAME>` per axis |
| `PROCTOR_BUNDLE`, `PROCTOR_BUNDLE_MOUNT` | gone; `PROCTOR_PARAM_<NAME>` for a parameter |
| `PROCTOR_TASK`, `PROCTOR_TASK_JSON` | `PROCTOR_STAGE`, `PROCTOR_STAGE_JSON`: the stage's id and the stage |
| `PROCTOR_ATTEMPT` | `PROCTOR_TRY_NUMBER`: the try's number |

From a suite of 0.3 or earlier, also: `suites/` is `exams/`,
`suite.json` is `exam.json`, `PROCTOR_SUITE_DIR` is `PROCTOR_EXAM_DIR`,
`PROCTOR_ARM` is `PROCTOR_CONFIGURATION`, `PROCTOR_SAMPLE` is
`PROCTOR_TRY_NUMBER`, `PROCTOR_CELL` is `PROCTOR_ATTEMPT_DIR`, and
`PROCTOR_EXPERIMENT` is gone.

## Variants become axes

Ask what the variants differed in. Each thing that was varied is an
axis; each variant is then one value on each, which is a configuration,
and you name the ones you ran.

- **They differed in one thing** (two versions of the docs, on one
  agent): one axis for that thing, and an `assistant` axis with the one
  value that does the work.
- **They were different agents and models:** one `assistant` axis, a
  value each. Agent and model that only ever change together are one
  axis, not two.
- **They were a hand-written grid** (two doc sets on two models): two
  axes, and the four configurations named.
- **They differed by a hook** (a setup script branching on the variant's
  id): move the difference into a value, as files or as a parameter the
  hook reads (`PROCTOR_PARAM_<NAME>`), where proctor can see and hash it.

Mark the value in use as `"live"` on its axis. An axis nobody chooses
gets a `"reference"` instead, or nothing if it has one value.

## Tasks become stages

- **Every task named the same fixture:** each task is a stage. A stage
  starts from the workspace as the try began, as a task had a fresh
  checkout.
- **The tasks named different fixtures:** the fixture is an axis whose
  values each name one (`"fixture": "ledger"`), and each stage says
  which it belongs to (`"when": "repository = ledger"`). A try is then
  one repository's stages, and the configurations to name are each
  assistant in each repository.
- **A task that fails on purpose** (a scripted outage), or one with an
  answer sheet that is to run through Harbor, wants a try of its own: an
  axis whose values each pick one stage (`"when": "script = fail"`), as
  `exams/conformance` and `exams/gate-oracle` do. A stage that does not
  run loses its whole try, so it would take the stages beside it with it.
- **A hook that set up one task** has no place: a `try` hook runs once
  around the try. Make it part of that world's fixture, or a stage's own
  doing.

Then two questions:

- **Does a later stage need what an earlier one made?** Leave stages
  fresh unless the work is one job done in steps; then set
  `"chained": true`.
- **How many tries?** The unit is the try, and a difference from what is
  live needs 4 tries a side to be confirmed (or 3 against 5). Set
  `tries` to 4 or more for anything you mean to compare.

## What else changed

- **Changing a stage makes every try of the exam stale**, where changing
  a task touched only that task. Changing a check or an expected value is
  still only a regrade.
- **A correctness check must reach every stage**, from the exam, the
  fixture or the stage's own block.
- **`run` makes what is named.** It no longer continues an interrupted
  run: a try that was lost is made again, whole.
- **The report is by configuration**, each value against its axis's live
  one. `proctor compare` and `proctor coverage` answer the same as JSON.

## The tries already made

Attempts made under tasks and variants stay in `bank/<exam>/` and are
read by nothing: what was under test is named and hashed another way,
so they are no configuration's tries. `proctor list` says how many are
there, and the viewer lists them from the exam's page, so their
transcripts can still be opened. Make the tries again to have numbers.

## Check the port

```bash
proctor list <exam>     # validates, and names what is wrong and where
proctor build <exam>    # what each value changes, with nothing run
```

Then prove the checks without a model, as the examples do in
`reference/shakedown.sh`, before a paid run.
