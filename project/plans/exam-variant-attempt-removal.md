---
type: plan
title: Exam, variant, attempt — what leaves, what is renamed, what stays with a label
created: 2026-11-15
status: work-list, nothing done. Made by laying `exam-variant-attempt.md` against the repository at `59140bb`. Counts are word occurrences from grep, so they size the work and are not a checklist
---

# The removal list

`exam-variant-attempt.md` is the ruler. Anything in the repository that
says suite, arm, experiment, baseline, sample or cell is saying what
proctor used to be. Agreeing to drop a word deletes nothing: while the
code and forty documents use it, they are the specification, and the
next session reads them.

Every item below is one of four things:

- **Delete**: the old model's own surface. Gone, no replacement.
- **Rename**: the thing survives; its name changes.
- **Rewrite**: prose that states the old model as current.
- **Label**: a shape we cannot afford to move yet. It gets one line
  where it lives: `Shape inherited from experiments; tolerated, not
  endorsed. See project/plans/exam-variant-attempt.md.`

## How much there is

| Word | Code | Tests | Docs | Suite and fixture files |
|---|---|---|---|---|
| suite | 494 | 560 | 749 | 301 |
| arm | 814 | 257 | 1,043 | 226 |
| experiment | 237 | 108 | 305 | 38 |
| baseline | 81 | 39 | 206 | 6 |
| sample | 151 | 83 | 356 | 74 |

Heaviest code files: `Run/Runner.cs` 581, `Report/Report.cs` 375,
`Verbs.cs` 293, `Suite/Suite.cs` 246, `Report/Stats.cs` 173,
`Harbor/Compile.cs` 162, `Grade/Grade.cs` 129.

## The name map

One map, used everywhere. No old spelling is read afterwards (D2).

| Old | New |
|---|---|
| `suites/`, `suite.json` | `exams/`, `exam.json` |
| `suites/proctor.json`, `suites/nb.json`, `suites/codex.toml`, `suites/runners/` | the same under `exams/`. The first three are gitignored and hold keys: move the files on disk, never print them |
| `runs/<experiment>/`, `experiment.json` | `attempts/<batch>/`, `batch.json` |
| `skills/proctor-suite/` | `skills/proctor-exam/` |
| `Suite/` (code), `Proctor.Tests/suites/` | `Exam/`, `Proctor.Tests/exams/` |
| `Suite`, `SuiteDef`, `SuiteNb`, `SuiteTests`, `OracleSuiteTests` | `Exam`, `ExamDef`, `ExamNb`, `ExamTests`, `OracleExamTests` |
| `Arm`, `ArmStats`, `CardArm` | `Variant`, `VariantStats`, `CardVariant` |
| `Experiment`, `WorkedExperiment` | `Batch`, `WorkedBatch` |
| `Cell`, `CellContext`, `CellGrade`, `CellFriction`, `CellStatus`, `NbCell`, `OracleCell`, `Runner.Cells` | `Attempt…`; `Runner.Attempts` |
| manifest `experiment`, `arm`, `sample`, `run_id`, `attempts` (retries), `suite_hash` | `batch`, `variant {id, hash}`, `attempt`, `id`, `tries`; `suite_hash` gives way to `task {id, hash}` |
| `experiment.json` `suite`, `suite_hash`, `suite_def` | `batch.json` `exam`, and the exam and variants as they were, under `exam_def` and `variants` |
| arm field `samples` | `attempts` on the exam (D3) |
| `friction.json` `run {experiment, arm, task, sample}`, schema `friction/0` | `attempt {exam, batch, variant, task, attempt}`, schema `friction/1` |
| `PROCTOR_SUITE_DIR`, `PROCTOR_ARM`, `PROCTOR_SAMPLE`, `PROCTOR_EXPERIMENT`, `PROCTOR_CELL` | `PROCTOR_EXAM_DIR`, `PROCTOR_VARIANT`, `PROCTOR_ATTEMPT`, `PROCTOR_BATCH`, `PROCTOR_ATTEMPT_DIR` |
| `{{arm}}`, `{{sample}}` in a program template | `{{variant}}`, `{{attempt}}` |
| `hooks.arm`, `hooks.sample` | `hooks.variant`, `hooks.attempt`; both the exam's (D12) |
| `--arm` | `--variant` |
| `setup --check --live` | `setup --check --probe`, so "live" means one thing |
| a judge check's `samples`, `JudgeSample` | `votes`, `JudgeVote`: a judge's repeated answers, never attempts |
| "run" as a noun for one attempt, in the report and prose ("36 runs", "Every run") | "attempt". `proctor run`, the verb, stays |
| "cell" in prose | "attempt", or "an attempt's directory" |

## Delete

**Verbs and flags.**

- `proctor resume` and `Runner.Resume`'s whole-suite hash gate. `run`
  continues an interrupted batch (D7).
- `proctor baseline`, `Suite/Baseline.cs`, `baseline.json`,
  `BaselineTests.cs`, `suites/code-change/baseline.json`.
- `report --against`, `Verbs.Against`, `GuardInput.PerArm`.
- "The first arm is the reference" (`Report.cs`, `Stats.Compute`'s
  `arms[0]`).
- "Explore mode" and "guard mode" as named things in `docs/cli.md`.

**The last rename's leftovers.** `Layout.LegacySuitesDir`,
`LegacySuiteFile`, `LegacyTasksDir`, `Layout.Existing`, the `("eval",
"suite")` pairs passed to `ReadJson`, `PROCTOR_EVAL_DIR`,
`PROCTOR_CASE`, `PROCTOR_CASE_JSON`, `{{case}}`. They are a ghost of the
model before this one, and D2 says this rename leaves none of its own.

**Data.** Everything under `runs/` and `reports/` (both gitignored): not
migrated, made again (D2). Delete only when the new proctor has made
its replacements.

## Rename, with the structure change it rides on

In the order the work goes. Each is a commit that builds and passes.

1. **The words, no behaviour change.** The whole map above except what
   Delete removes: do not rename what is about to go. Tests follow
   their subjects. The report snapshots (`Proctor.Tests/snapshots/`)
   change and need the user's approval, as any `.received` does.
2. **Variants out of the exam** (stage 1). `arms` leaves `exam.json` for
   `variants/<id>.json`; the exam's hash stops covering them; a task
   hash and a variant hash go into each manifest; a changed variant
   under an old id is refused (D5); a bundle may name `paths` (D12);
   `attempts` on the exam. The 13 suites are converted by a script,
   kept in the commit that uses it and deleted in the next.
3. **The pool** (stage 2). `Runner.Attempts` and its six callers read
   every batch of an exam; `run` makes what is missing and continues an
   interrupted batch; `grade`, `friction`, `report` take the exam, with
   `--batch`, `--variant`, `--tasks`; stale attempts are listed, never
   compared; reports are stamped with what they were built from.
4. **Live.** `live.json`, `proctor live`, and the report comparing each
   variant with the live one. This is what lets the baseline code be
   deleted in the same stage and not carried to stage 3.

Claims and the two report layouts (stage 3) are not on this list; their
design is not settled.

## Rewrite

**First, before any code**: the vocabulary table in `CLAUDE.md`, which
today says to use suite, arm and experiment "and no others". Until it
changes, every session is told the old model is the rule.

**Current documentation, rewritten in place.**

| File | Old-word hits | Note |
|---|---|---|
| `CLAUDE.md` | 196 | the structure table and every convention bullet |
| `README.md` | 22 | |
| `docs/configuration.md` | 137 | the reference; has its own copy of the vocabulary table |
| `docs/cli.md` | 45 | leads with continuing after a crash (D15) |
| `docs/getting-started.md`, `modal.md`, `podman.md`, `porting-to-agent-neutral.md` | 21, 24, 7, 50 | |
| `skills/proctor-suite/SKILL.md` | 56 | shipped to users' agents |
| `suites/*/README.md` | 7 files | `agent-onboarding` 66, `suite-authoring` 37 |
| `Setup/SetupWizard.cs`, `SetupCheck.cs` | printed text | what a new user reads first |
| `project/plans/on-disk-layout.md`, `report-structure.md` | 118, 111 | `CLAUDE.md` cites both as current |
| `project/brief.md` | 73 | |

**What an agent under test is shown.** `suite-authoring`'s view copies
five plans to the agent as documentation: `on-disk-layout`,
`fixtures-arms-baselines`, `containerised-runs`, `judge`,
`report-structure`. Three of those would be archived below. The view
must stop copying archived plans, and what it needs from them moves into
`docs/configuration.md`.

**Prompts.** They are the test that the words work for an agent (D13).

- `suite-authoring`, three tasks: "Write a suite … one arm … three
  samples" becomes exam, variant, "each task is attempted three times".
  The suite becomes `exam-authoring`. It gains a task that starts from
  an interrupted batch (D15).
- `agent-onboarding`, three tasks: each asks, in a user's voice, for "a
  proctor suite".

**`project/todo.md`** (1,076 lines, 143 hits). Loose ends, many still
live. Each section is either reworded or moved to the archive; this one
needs a person's judgement per section and is not mechanical.

**`CHANGELOG.md`** (194 hits). History stays as written. The Unreleased
entry carries the name map, and one line above the first old release
says entries below it use the earlier words.

## Archive, with a header

Moved to `project/archive/`, each opening with: `Written in proctor's
earlier vocabulary (suite, arm, experiment, baseline, sample). Superseded
by project/plans/exam-variant-attempt.md; kept for the reasoning.`

- **Plans whose own status says built, done or dropped** (11 of 23):
  `suite-task-check` (the old vocabulary's founding note),
  `fixtures-arms-baselines`, `first-slice`, `bundle-showcase`,
  `containerised-runs`, `debugging-suite`, `harbor-conformance`,
  `host-agent-arms`, `judge`, `modal-runs`, `research-pass`.
- **Plans still open stay in `plans/` and are reworded**:
  `agent-neutral-runs` (current, items open), `jev-judge-research` and
  `jev-trial` (open), `onboarding` (draft, four decisions open), `lab`
  with `lab-report-worked-example` (proposed), `oracle-in-proctor` (no
  status line; read it first). Whether each is still wanted is the
  user's.
- **`project/bugs/`**, seven notes, all marked fixed, two about the baseline.
- **`project/learnings/two-modes-and-baselines.md`**. The other
  learnings are about other tools and stay.

## Label

Shapes that stay for now. Each gets the one-line label where it lives.

| Where | The inherited shape |
|---|---|
| `Layout` (the `attempts/<batch>/<variant>/<task>/<n>/` path) | attempts nest under the batch that made them; the pool is a query across batches |
| `Harbor/Compile.cs` | Harbor tasks are compiled once per batch, per variant and task |
| `Report/Report.cs`, top of file | the page is built as one batch's variants side by side, until the two report layouts exist |

## Outside this repository

- **`.claude/worktrees/workbench-plan`**: a second checkout of proctor
  at `93b8927`, inside the directory. Every grep hits it. Remove the
  worktree or exclude it; the user's call, it may hold uncommitted work.
- **auspex**: `Cells.cs` and `Pages.cs` read today's layout and
  `friction/0`; its `CLAUDE.md` repeats proctor's vocabulary; its notes
  use the old words. `datasets/` stays in the old format, frozen, and
  its README says so.
- **nb** (`../nb`): has a `ProctorContractTests.cs`. Not read for this
  list; check whether it names any field in the map.
- **The public repository**: `suite-authoring` pins proctor at a GitHub
  revision as its bundle. The pin moves to a pushed branch with the new
  docs. Pushing is the user's.

## Done when

1. **The build and every test pass**, the opt-in gates included.
2. **A grep finds no old word outside the allowed places.** Over tracked
   files, whole words, any case: `suite`, `arm`, `experiment`,
   `baseline`, `sample`, `cell`, `resume`, and their plurals. Allowed:
   `project/archive/`, the two `exam-variant-attempt` notes, old
   `CHANGELOG.md` entries, and lines carrying the label. The allowed
   list is a file in the repository, so an exception is a visible edit.
3. **`exam-authoring` runs on the local model** with the reworded
   prompts and the pass rate is no worse than `suite-authoring`'s last.
4. **A fresh session, given only the repository, describes the data
   model** as exam, task, check, variant, attempt, batch. If it says
   suite, arm or experiment, the work is not finished, whatever the
   grep says.

## What can be handed off

- **To another agent, with this file and the worked example as the
  brief:** rename step 1; the Delete list; the Rewrite table except
  `todo.md`; the archive move; the grep gate. Then steps 2 to 4 one at
  a time, each reviewed before the next starts.
- **Not handed off:** `project/todo.md`; the report snapshots' approval;
  the six open plans; anything in stage 3; the push.

## Settled since

- `setup --check --live` becomes `--probe`; a judge's `samples` becomes
  `votes` (the user, 2026-11-15). Both are in the name map.
- `research_notes/` stays untracked.

## Open

- Which of the six open plans are still wanted.
