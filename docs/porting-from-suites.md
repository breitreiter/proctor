# Porting a suite to an exam

A suite written for proctor 0.3 or earlier (`suites/<id>/suite.json`, its
arms, its `tasks/`) does not load any more: old spellings are not accepted,
and old runs are not read. This is what to change. The reference for every
field is [`configuration.md`](configuration.md); the two worked examples
are `exams/readme-answers` (a model answering, no sandbox) and
`exams/tern-docs` (a coding agent in a container).

## The words

| Then | Now |
|---|---|
| suite | exam |
| arm | variant |
| task | stage |
| sample, cell | attempt: one try, every stage once |
| experiment, `runs/<id>/` | the bank, `bank/<exam>/`: every attempt of the exam, whenever it was made |
| baseline | the live variant |

## The files

| Then | Now |
|---|---|
| `suites/<id>/suite.json` | `exams/<id>/exam.json` |
| `arms: [...]` in `suite.json` | one file per variant, `exams/<id>/variants/<variant>.json`; the file name is the id |
| an arm's `description` | the variant's `note` |
| an arm's `samples` | `attempts` on the exam, the same for every variant |
| `tasks/<task>.json`, one per task | `stages: [...]` in `exam.json`, in the order they run |
| a task's `fixture` | `fixture` on the exam, once |
| a task's `description` | the stage's `about` |
| a task's `labels` | the exam's `labels`; a stage has none |
| `hooks.arm`, `hooks.sample` | `hooks.variant`, `hooks.attempt` (once around the try) |
| `suites/proctor.json`, `suites/nb.json` | `exams/proctor.json`, `exams/nb.json`; `proctor setup` writes them |
| `baseline.json`, `proctor baseline` | `live.json`, `proctor live <exam> <variant>` |
| `runs/` in `.gitignore` | `bank/` |

In `program.nb`, `{{arm}}` is `{{variant}}` and `{{sample}}` is
`{{attempt}}`. In hooks and script checks:

| Then | Now |
|---|---|
| `PROCTOR_SUITE_DIR` | `PROCTOR_EXAM_DIR` |
| `PROCTOR_ARM` | `PROCTOR_VARIANT` |
| `PROCTOR_SAMPLE` | `PROCTOR_ATTEMPT` |
| `PROCTOR_CELL` | `PROCTOR_ATTEMPT_DIR` |
| `PROCTOR_EXPERIMENT` | gone; `PROCTOR_EXAM` names the exam |
| `PROCTOR_TASK`, `PROCTOR_TASK_JSON` | the same names; they hold the stage's id and the stage |

## A suite's tasks become one exam's stages, or several exams

An exam has one fixture, and every stage runs in it.

- **Every task names the same fixture:** one exam. Each task is a stage:
  `id`, `about`, `prompt`, and its `expect` and own `checks` if it had
  them.
- **The tasks name different fixtures:** one exam per fixture. Copy the
  grading, the program and the variants to each. A task alone on its
  fixture is an exam of one stage.
- **A task that fails on purpose** (a scripted outage, say) is an exam of
  its own. A stage that does not run loses its whole try, so it would take
  the stages beside it with it.

Then two questions the suite never asked:

- **Does a later stage need what an earlier one made?** In a suite every
  task had a fresh checkout, and stages keep that: each starts from the
  fixture as it was. Leave it so unless the work is one job done in steps;
  then set `"chained": true` on the exam.
- **How many tries?** `samples: 3` meant three of each task, and the
  report's `n` was the task count. Now the `n` is tries, and a difference
  from the live variant needs 4 tries a side to be confirmed (or 3 against
  5). Set `attempts` to 4 or more for anything you mean to compare. The
  model runs are variants × attempts × stages, as before.

## What else changed

- **A check is asked of each stage.** The exam's and the fixture's checks
  apply to every stage with that stage's `expect`, as a suite's applied
  to every task. `pass` and `validity` are listed the same way. A stage
  failing a `validity` check leaves its try out of the comparison.
- **A stage's `id` is its label on screen.** Two or three words, about 20
  characters; a task's file name usually serves.
- **An oracle** (a task's answer sheet for a simulated user) works on a
  stage only when every variant is bare nb. With an agent variant, keep
  it to an exam whose variants are all bare for now.
- **Changing a stage makes every attempt of the exam stale**, where
  changing a task touched only that task. Changing a check or an expected
  value is still only a regrade.
- **Arms that differed by a hook** (a setup script branching on the arm's
  id) need the difference moved into each variant's `bundle`, where
  proctor can see it.
- **The commands take the exam, never a run's id.** `proctor run <exam>`
  makes the attempts the exam lacks and continues after a crash; there is
  no `resume`. `grade`, `friction` and `report` read every current
  attempt. `report --against` and `--tolerance` are gone: mark a variant
  live and the report compares the others with it.

## Check the port

```bash
proctor list <exam>     # validates, and names what is wrong and where
```

Then prove the checks without a model, as the two examples do in
`reference/shakedown.sh`, before a paid run. Results from `runs/` are not
carried over: make the attempts again.
