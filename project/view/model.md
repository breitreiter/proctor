---
date: 2026-11-15
status: agreed with the user in conversation. Built in proctor on branch exam-variant-attempt (2026-11-15, not pushed) except claims: exam, variant files, attempts pooled across batches, run filling gaps, the live mark. Not built: claims and the change report. auspex's viewer still reads the old layout
---

# The data model, from first principles

## Why

The viewer's first pass (2026-11-15) opened on a list of "experiments",
a word proctor's report never shows a reader. Working back from that,
half of proctor's vocabulary turned out blurry to the person who wrote
it: fixture, check and bundle were clear; suite, arm, task, experiment,
baseline and run against sample were not. The user's call: fix the
foundation, and absorb rewriting proctor, before anything more is
stacked on it.

## What a person is doing

You own something an agent uses: docs, a CLI, a skill, a prompt. You
want to know whether an agent succeeds with it, and whether a change
you make helps. That needs three things.

1. **The exam.** What to ask the agent to do, where, and how to tell
   whether it went well.
2. **A variant.** One configuration put in front of the exam.
3. **An attempt.** One variant trying one task once.

Everything else is a question asked of the pile of attempts.

## The words

| Word | Meaning |
|---|---|
| **Exam** | The question being answered, the tasks, and the checks that grade them. It does not hold the variants. |
| **Task** | One prompt against a fixture, with its own answer key for the exam's checks. |
| **Fixture** | The repository and environment a task runs in. As in proctor now. |
| **Check** | One yes-or-no grading question, shared across the exam's tasks. As in proctor now. |
| **Variant** | An agent, a model and a bundle, with a free-text note on what is different about it. Its identity is its content. It never carries a prediction. |
| **Bundle** | The pinned version of what the variant puts in front of the agent. As in proctor now. |
| **Attempt** | One variant, one task, once, with the evidence. Records exactly which task it attempted: prompt, fixture, answer key. |
| **Claim** | Names two variants and says what should change between them: which checks, which direction. Recorded before the attempts are made. |
| **Treatment** | A variant that comes with a claim. |
| **Live** | A mark on one variant: the one in use now, and the reference a change is compared against. |

- **"Exam", not "suite"** (the user, 2026-11-15). "Suite" in technical
  use just means a collection of things. "Exam" carries a list of
  questions with grading criteria, used as a standard measure of
  competence at related tasks. It is outside the vocabulary of agent
  evals, which is a cost, and it is clear to a less experienced user,
  who is most of the audience.
- **Task stays "task".** The exam metaphor suggests "question", but a
  check is already a yes-or-no question.
- **"Variant", not "candidate".** It bundles more than the test-taker:
  the agent, the model and the version of your own thing.

## Three kinds of "many"

| Many of | What varies | What it tells you |
|---|---|---|
| Checks | The question asked of one outcome | Whether it went well in each way that matters |
| Tasks | The situation the agent is put in | Whether the result holds beyond one prompt |
| Attempts at one task | Nothing; it is a repeat | How much is luck |

One prompt graded by several checks is an exam with one task. Many
prompts graded by the same checks is an exam with many. The same shape
at two sizes.

**The exam owns the questions; each task owns its answer key.** "Do the
hidden tests pass?" is asked of every task, and each task has its own
hidden tests. Proctor does this today with a task's `expect` block
filling in a shared check, which is why it is unclear from the files
whether a check belongs to the suite or the task.

## Why proctor's words blur

- **Suite** is two things in one file: the exam and the list of
  variants taking it. Adding a variant changes the suite's hash, so it
  is a different suite and a new experiment.
- **Arm** is a variant that exists only inside one execution. The same
  arm id in two experiments is two unrelated things unless a report is
  asked to pair them.
- **Experiment** promises a question with a hypothesis. It is one
  invocation of `proctor run`: a batch with a timestamp. The real
  experiment (does the restructure help?) can span batches.
- **Baseline** exists because comparison is locked inside a batch. It
  is a second mechanism for "compare two variants", for when they ran
  at different times.
- **Sample, run, cell** are three words for one attempt: the statistic,
  the report's word, the directory. "Run" is also the verb that creates
  an experiment.

## What becomes of them

| Proctor today | In this model |
|---|---|
| suite | exam, minus its arms |
| arm | variant, living outside the exam |
| sample, run, cell | attempt |
| experiment | a batch: when attempts were made, kept for resuming and provenance, not a noun a reader sees |
| baseline | the live variant |
| `report --against` | comparing two variants; no special case |

## The two modes

How proctor is used in practice (the user, 2026-11-15).

| | Finding a first pass | Changing something live |
|---|---|---|
| Variants | Several, side by side | Two: the live one and the change |
| Reference | None; all are candidates | The live variant |
| Claim | None | Required |
| The question | Which is best, and is it good enough | Did the claim hold, and did anything else move |
| Evidence for the PR | "Basically works, and I did more than ask for a prompt": the exam, what was tried, how the pick did, where it still fails | How the live variant is doing, where it under-performs, the change, the claim's verdict, collateral damage |

- **Two report shapes over one model.** No mode flag: a live variant
  plus a claim against it is a change proposal; neither is exploration.
- **The first mode ends by shipping a pick.** That pick becomes the
  live variant, and the second mode starts.
- **The first mode flatters its winner.** Some of the best variant's
  lead is luck. Fine for "basically works"; the report says so and does
  not present the pick as a measured improvement.
- **A claim made beforehand is what separates a fix from a lucky
  find.** With a dozen checks and several variants something flips by
  chance. Claimed changes get a verdict; everything else that moved is
  listed as unpredicted (`loop.md`, side effects).
- **"Did not break anything" is part of every claim.** The report
  checks the old tasks by default.

## Why the claim is not on the variant

- A claim needs two variants. The first one ever run has nothing to
  claim against.
- One variant is compared more than once: claimed to beat the shipped
  one today, the reference for a third tomorrow.
- A variant is its content. A claim inside it would make rewording the
  claim a new variant and orphan its attempts.

## A change usually changes the exam too

The user's case:

1. An exploratory pass looks good and ships.
2. A user complains it did not work for them.
3. The cause: they are on an older version of a library. The prompt
   says a flag must be set for the task to be complete, and that flag
   does not exist on the older version.
4. So the fix needs a new prompt and a new environment to test it in.

In the model:

1. **The complaint becomes a new task**: the same request on a fixture
   with the older library, with its own answer key (the flag is not
   required here).
2. **The live variant attempts it and fails.** That reproduces the
   complaint and is the evidence of under-performing. If it passes, the
   complaint is not reproduced yet.
3. **The new prompt is a new variant**, with a claim in two parts: the
   new task goes from fail to pass, and the old tasks stay where they
   were.
4. **The new variant attempts every task.** The live variant already
   has attempts on the old ones.

What follows from it:

- **The task is the unit of comparison.** Two variants are compared on
  the tasks both attempted, matched by the task's exact content. Adding
  a task invalidates nothing, so an exam needs no version number.
- **An exam mostly grows.** Each complaint adds a task, as each bug
  adds a regression test, and the old tasks guard the next change.
- **Running means filling gaps.** Given an exam and some variants,
  proctor makes the attempts that do not exist yet. That is what a
  person asks for in place of "an experiment".

This is also where friction lands (`loop.md`): a friction point a
person promotes becomes a check or a task in the exam, the live variant
fails it, and the treatment claims to flip it.

## What it costs in proctor

Sized on 2026-11-15 by reading `Suite/Suite.cs`, `Suite/Layout.cs`,
`Suite/Baseline.cs`, `Run/Runner.cs`, `Verbs.cs`, `Report/Results.cs`,
`Report/Stats.cs` and the outline of `Report/Report.cs`. Proctor is about
10,000 lines of source and as much again in tests. Sizes are relative;
no day counts, because none would be honest.

**What the reading found.**

- **One line is the spine.** `Runner.Cells(suite)` is the suite's arms ×
  tasks × samples. The runner, `Grade`, `Results.Collect`, `Stats`,
  `Judge.Uses` and the Harbor compile all walk it. They enumerate from
  today's suite, not from what is on disk.
- **Regrading with today's checks is already how proctor works.**
  `grade` and `report` load the current suite and apply it to an old
  experiment's cells. `Baseline.Resolve` rescoring pinned cells, and
  warning where an old cell was never graded on a current check, is the
  cross-time comparison this model wants, already written.
- **Comparing two variants is one existing function.** `Stats.Compare`
  pairs any two sets of per-task scores. The report calls it for every
  arm against the first arm ("the reference"), and again against the
  baseline. So "live" is two mechanisms today: the first arm, and
  `baseline.json`.
- **Each cell's manifest already records** its fixture hash, bundle
  hash, suite hash and program hash. What is missing is a hash of the
  task alone (prompt, fixture, answer key, checks).
- **The suite hash covers the arms**, and `resume` refuses any change.
  That gate is what makes adding a variant a new suite.
- **A script check on an agent variant runs inside the container** at
  run time (`ScriptsInVerifier`). Checks over the trajectory or by a
  judge read saved evidence. So "free to regrade, or a re-run" is a real
  line in the code, and it falls on script checks. Whether proctor's
  restore of fixture plus diff covers an agent variant's script checks
  was not confirmed.

**The on-disk layout does not have to change.** An earlier draft of this
note said attempts stop nesting under an experiment. They need not: an
experiment's directory is the batch, and the pool of attempts is a
query over the manifests of every batch of an exam. The 31 copied
datasets and auspex's reader keep working.

**Three stages, each usable alone.**

| Stage | What changes | Where | Size |
|---|---|---|---|
| 1. Variants out of the exam's identity | The exam's hash stops covering variants; a task gets its own hash, recorded in each manifest; `run` takes which variants to run; `resume` checks the task and variant of each cell, not the whole suite | `Suite.cs` (load, validate, hash), `Runner.Start` and `Resume`, `Verbs.Run`, the manifest | Small |
| 2. The pool, and filling gaps | An index of attempts across an exam's batches, keyed by variant and task content; `run` makes only the attempts that are missing; `grade`, `friction`, `report` read the pool, not one batch's `Cells(suite)` | `Runner.Cells` and its six callers, `Results.Collect`, `Grade`, `Verbs` | Medium. Mechanical once the index exists; every verb's argument changes from an experiment id to an exam |
| 3. Live, claims and the two reports | A live mark replaces `baseline.json` and the first-arm rule; a claim file; the report in two shapes; a rule for which unclaimed movement is beyond noise | `Baseline.cs` goes, `Stats.cs` guard path generalises, `Report.cs` headline sections, `report-structure.md` | Large, and the only stage with open design: the claim's file format, the collateral-damage rule, the first page |

- **Untouched, about 60% of the code:** the checks, the judge, ingest
  and transcripts, the Harbor compile and run, friction, setup.
- **Tests:** `RunnerTests`, `SuiteTests`, `StatsTests`, `ReportTests`
  and `BaselineTests` (about 1,500 lines) are reworked across the
  stages. Eleven test files define arms inline.
- **Cheapest choice available:** keep variants declared in the exam's
  directory, in `suite.json` or a file beside it, and only leave them
  out of the exam's hash. Then the 13 suites and most tests keep
  loading unchanged. Moving variants elsewhere is a later, separate
  call.
- **Words in the report and CLI** (exam, variant, attempt) can change at
  any stage, independent of the structure. Renaming `suites/`,
  `suite.json` and `suite_def` is undecided; proctor has done one such
  rename before (from `evals`), accepting the old spellings for a
  release.

## Future work: artifacts for human grading

Not for now. Recorded so the model does not rule it out.

Proctor is occasionally used to produce artifacts for a professional to
judge: "you work in this field, which of these versions makes the most
sense to you?"

- **It is rare, and high-touch** (the user, 2026-11-15). A professional
  cannot be handed forty comparisons. Someone, the user or a user
  researcher, cleans up the output, picks the comparisons that matter,
  stages them into a curated session, and usually runs it on a call.
- **So proctor's part is generating, not grading.** Several versions
  each producing output for the same prompts is an exam, variants and
  attempts, unchanged. Proctor gives the provenance of each artifact,
  repeats, and gap-filling when a version is added. The curation and
  the session stay with a person.
- **What the model must keep allowing:**
  - an exam whose checks are only the sanity ones (was there any
    output, did it time out). Those always apply, so an exam with no
    checks at all is not a case to support (the user, 2026-11-15);
  - an attempt's output kept whole and easy to pull out, with which
    variant made it;
  - a result that is not a check. The professional's judgement is
    comparative, across variants on one task, where a check is a
    yes-or-no about one attempt. Nothing should assume every finding
    about a variant is a pass rate.
- **Not planned:** a blinded export, reading answers back in, rater
  agreement. If the judgement is ever absolute ("is this acceptable?"
  per artifact), that is a check answered by a person, and proctor's
  undecided checks already have room for it.

## Open

- **A shared check that was wrong.** Fixing it changes what the old
  tasks mean, so old attempts need regrading: free where the check
  reads saved evidence, a re-run where it needs the environment live.
- **Environment as its own axis.** The older library is one new task
  here. Wanting every task on both versions means copying each task per
  environment. Keep the fixture inside the task until that copying
  starts; then probably "one prompt, a list of fixtures".
- **What counts as collateral damage.** Every check and friction
  measure the claim did not name will wobble. The report has to say
  which movements are larger than noise.
- **A claim made after the attempts exist.** People will want it, and
  it weakens the one property that makes a claim worth having. Proposed
  and not agreed: allow it, labelled as late.
- **The viewer's first page.** The user's sketch, for someone who has
  just had an agent "set up evals and run the first pass": this is an
  exam; proctor produced and manages it; a narrative of what the exam
  is meant to find out, to anchor it to the request that started it;
  what has been run so far, at the top because an experienced user
  reaches for it first; and that with one variant there is nothing to
  compare yet. It was drawn before this model, in terms of a list of
  experiments, and needs redrawing in these words.
