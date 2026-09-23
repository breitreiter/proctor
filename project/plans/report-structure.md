---
type: plan
title: The report, rebuilt for a reader who was not there
created: 2026-09-22
status: built 2026-09-22 as written, undecided runs out of the pass denominator; the snapshot is the worked example rendered by the code
---

# The report, rebuilt for a reader who was not there

The first real report (api-docs lookups, 2026-09-22) read as a wall of ids
and intervals. Descriptions on checks and tasks helped, but the page still
assumes the reader knows what an arm, a task, a cell and a check are, opens
with a data dump written as prose, and shows the same pass counts from five
angles without saying which one to trust. This plan is the structure that
replaces it. It is written as the worked smoke experiment, rendered in the
proposed order, so the disagreement can be about actual lines.

The rule the structure follows: **define a term before using it, then use it
consistently.** Five terms carry the whole report:

| term | one sentence, used verbatim wherever the term is introduced |
|---|---|
| suite | A collection of related tasks with one business goal. Named once, in the opening table; the description under the title is the suite's. |
| arm | One configuration under test: a harness, a provider and a model, run over every task. The first arm is the reference the others are compared with. |
| task | One input and one desired outcome, given to every arm: a prompt against a fixture repository, with the checks that say whether the outcome was reached. |
| run | One attempt at one task by one arm. A task is run several times per arm (its *samples*), so the score is not one lucky or unlucky attempt. (Was "cell" and "sample"; both words go.) |
| check | One yes-or-no test over a finished run. The **headline** checks together decide whether a run passed. A **validity** check decides whether a run counts at all. Any other check is a **guardrail**: reported, not part of the pass. |

Every heading gets one sentence under it saying what the section is for.
Statistics stay, but move behind the plain result and get a plain name:
"95% interval", never "CI".

## Reading order

1. **Title and about** (a table, not a paragraph)
2. **Arms** (defines the term, lists them)
3. **Tasks** (defines the term, lists them with what each asks, its fixture, and the checks it carries beyond the shared ones)
4. **Checks** (defines the term and the three roles, lists them with what each tests and which tasks carry each)
5. **Results** (pass rate per arm, the comparison, the baseline; the verdict in words)
6. **Failures** (per arm, the checks that failed and on which tasks, in the words from 3 and 4)
7. **What ran** (planned, attempted, completed, graded, analysed; what was excluded and why; how nb ended each run, per arm)
8. **Results by task** (the glyph matrix, now that arm, task and run are defined)
9. **Check pass rates** (the checks table with per-arm rates)
10. **Cost and effort**
11. **Every run** (the per-run table, failures first)
12. **Reproducibility and method** (versions, hashes, command; the statistics paragraph, last)

Sections 1 to 6 are the report. A reader who stops after 6 has the answer.
Sections 7 to 12 are the evidence for a sceptical reader, in the order they
would ask for it.

## Worked example: the smoke experiment in this structure

What follows is `summary.md` for the snapshot experiment
(`20260917-1432-smoke-k7px`), rewritten by hand in the proposed structure.
The numbers are the snapshot's. The HTML is the same text with tables.

---

# smoke — 20260917-1432-smoke-k7px

Can a local coder model make a small change to a .NET repository so that it
builds and the tests pass?

| | |
|---|---|
| Suite | `smoke` |
| Run | 2026-09-17 on `bench` |
| Arms | 2: `floor` (the reference), `b` |
| Tasks | 3, each run 3 times per arm |
| Runs | 18 planned, 16 counted |
| Result | `b` passed 89% of its runs, `floor` 56%. With 3 tasks the difference (+33 points) is not statistically detectable. Against the pinned baseline, `floor` regressed and `b` improved at a tolerance of 10 points. |

## Arms

An arm is one configuration under test: a harness, a provider and a model,
run over every task. The first arm is the reference the others are compared
with.

| Arm | What it is | Harness | Provider | Model | Runs per task |
|---|---|---|---|---|---|
| `floor` | the local floor | nb | local-qcoder | qwen-coder | 3 |
| `b` | | nb | local-glm | glm | 3 |

## Tasks

A task is one input and one desired outcome, given to every arm: a prompt
against a fixture repository, with the checks that say whether the outcome
was reached. Each task is run 3 times per arm, so a score is not one lucky
or unlucky attempt. Every task's runs carry the 3 checks the suite declares,
listed under Checks; the last column is what a task's runs are checked for
beyond those, from its fixture or its own file, in that task's own words.

| Task | What it asks | Fixture | How it is measured |
|---|---|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop | `note` | — |
| `plain` | The model answers in one turn with no tools | `note` | — |
| `uses-bash` | The model runs one bash command and answers | `note` | `used-bash` (guardrail): uses bash |

Checks are the task's, not the suite's: a run carries the union of what the
suite, its fixture and its own file declare (2026-09-23,
[suite-task-check.md](suite-task-check.md)). Listing the whole union per
task would repeat the suite's checks on every row, so the table shows what
is particular to the task and the Checks section says which tasks carry
each check. "Particular" means declared by the fixture or the task, not
"carried by fewer than every task": a headline every task declares under
one name, each with its own criterion, is on every row with that task's
sentence, headline first (2026-09-23,
[the bug](../bugs/per-task-check-descriptions-never-reach-the-report.md)).
The row then reads as what we asked and how we decided. The fixture column
is omitted when no task names one.

## Checks

A check is one yes-or-no test over a finished run. The headline checks
together decide whether a run passed. A validity check decides whether a run
counts at all: a run that fails one is left out of every rate, not counted as
a failure. Any other check is a guardrail: reported, not part of the pass.
A check that cannot decide a run leaves that run out of its rate on both
sides. A check is declared by the suite, by a fixture or by a task, and its
rate is over the runs of the tasks it is on.

| Check | What it tests | Role | On |
|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | every task |
| `builds` | the repository builds after the change | headline | every task |
| `no_denials` | no denied tool calls | validity | every task |
| `used-bash` | uses bash | guardrail | `uses-bash` |

"What it tests" is one sentence only when the check says one thing
everywhere: the suite's checks always do, and a fixture's or a task's does
when every declaration describes it the same way. A check whose tasks
describe it differently reads "per task; see Tasks" rather than one task's
sentence standing in for all of them.

## Results

Pass rate is the share of counted runs in which every headline check held,
averaged task by task so that one task with many runs does not outweigh
another. The 95% interval says how far the true rate could plausibly sit
from the measured one; with 3 tasks it is wide.

| Arm | Pass rate | 95% interval | Passed / counted runs |
|---|---|---|---|
| `floor` | 56% | 15% to 90% | 4 / 7 |
| `b` | 89% | 35% to 99% | 8 / 9 |

**`b` against `floor`.** `b` passed 33 points more of its runs than `floor`.
The 95% interval on that difference runs from −31 to +75 points, so with 3
tasks the difference could be noise: no detectable difference. Task by task,
`b` did better on 2, worse on 0, the same on 1. To detect a 10-point
difference reliably this suite would need about 200 tasks.

**Against the baseline.** The baseline is the score pinned for each task on
2026-09-14; the verdict compares each arm's score with it and calls anything
more than 10 points either way a change.

| Arm | Difference from baseline | 95% interval | Tasks better / worse / same | Verdict |
|---|---|---|---|---|
| `floor` | −22 points | −67 to +39 | 0 / 2 / 1 | regressed |
| `b` | +11 points | −46 to +63 | 1 / 0 / 2 | improved |

| Task | Baseline | `floor` | `b` |
|---|---|---|---|
| `loops` | 100% | 67% | 100% |
| `plain` | 100% | 100% | 100% |
| `uses-bash` | 33% | 0% | 67% |

## Failures

For each arm, the checks that failed in at least one counted run, most
frequent first, with the tasks they failed on.

**`floor`** (the local floor) failed 2 checks:

- **the repository builds after the change** (`builds`, headline) failed in
  3 of 7 runs: twice on `uses-bash`, once on `loops`.
- **nb exits with 'ok'** (`exit_ok`, headline) failed in 1 of 7 runs: once on
  `loops`, where nb stopped at the tool-call limit.

**`b`** failed 1 check:

- **the repository builds after the change** (`builds`, headline) failed in
  1 of 9 runs: once on `uses-bash`.

A check described per task leads with its id and carries each task's
sentence beside the task: **`answer`** (headline) failed in 2 of 9 runs:
once on `lookup-semantic` (the answer gives `#fbedef`), once on
`fanout-no-guidance` (the answer gives 120).

## What ran

Every run planned by the matrix, and how far it got. *Attempted* runs
started; *completed* runs ended with a transcript from nb; *graded* runs
have their checks; *counted* runs are graded and passed every validity
check. Rates are over counted runs only.

| Arm | Planned | Attempted | Completed | Graded | Counted |
|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 7 |
| `b` | 9 | 9 | 9 | 9 | 9 |

> **The arms lost runs unequally.** `floor` counted 7 of 9, `b` 9 of 9. The
> rates above are over counted runs; the two runs `floor` lost are listed
> here.

Runs left out, and why:

- `floor` / `plain` / run 3: not counted, failed a validity check.
  `no_denials`: 1 denied call: bash (no-match)
- `floor` / `uses-bash` / run 2: never completed. Sample setup hook failed:
  hooks/reset-fixture.sh exited 1: clone failed

How nb ended each completed run. `ok` is a normal finish; anything else is
nb stopping the run, which the checks then grade like any other.

| Arm | ok | max_tool_calls |
|---|---|---|
| `floor` | 7 | 1 (`loops`, run 3) |
| `b` | 9 | 0 |

## Results by task

One row per task, one column per arm, one mark per run: ● passed, ○ failed,
× not counted. Hover a mark for the reason; click it for the run.

| Task | What it asks | `floor` | `b` |
|---|---|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop | ● ● ○ | ● ● ● |
| `plain` | The model answers in one turn with no tools | ● ● × | ● ● ● |
| `uses-bash` | The model runs one bash command and answers | ○ × ○ | ● ○ ● |

## Check pass rates

How often each check held, per arm, over the runs it applies to. Headline
checks are over counted runs; a validity check is over every graded run,
because it says how many were counted.

| Check | What it tests | Role | `floor` | `b` |
|---|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | 89% (6/7) | 100% (9/9) |
| `builds` | the repository builds after the change | headline | 56% (4/7) | 89% (8/9) |
| `no_denials` | no denied tool calls | validity | 89% (7/8) | 100% (9/9) |

## Cost and effort

Per completed run. Duration is wall time including hooks; tokens are what
nb reported. Cost is not shown: nb does not report it.

| Arm | Duration (min): median | p90 | mean | range | Tokens: median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Every run

Every run, failures first. *Reason* is the first check that did not hold and
what it saw, or why the run never completed.

| Arm | Task | Run | Result | nb ended | Duration | Tokens | Reason |
|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | ○ failed | ok | 17.3 | 63,000 | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | ○ failed | max_tool_calls | 23.5 | 47,200 | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | ○ failed | ok | 13.5 | 41,200 | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | ○ failed | ok | 16.9 | 47,200 | builds: 2 of 41 tests failed |
| `floor` | `plain` | 3 | × not counted | ok | 16.9 | 67,200 | no_denials: 1 denied call: bash (no-match) |
| `floor` | `uses-bash` | 2 | × never completed | | 15.2 | | sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed |
| `b` | `loops` | 1 | ● passed | ok | 16.5 | 61,000 | |
| … | | | | | | | |

(The run id moves out of the visible columns into the row's anchor and the
hover text; it identifies, it does not inform.)

## Reproducibility and method

| | |
|---|---|
| proctor | 0.1.0 |
| nb | 1.0.0 at /usr/local/bin/nb |
| suite hash | sha256:9c1e0000 |
| repository | 3f2c1e9a |
| command | `proctor run smoke` |
| created | 2026-09-17T14:32:00Z |

Each task is scored as its mean over its counted runs, so n in every
interval is the number of tasks. Per-arm rates use the Wilson 95% interval.
Differences between arms, and against the baseline, use Newcombe's paired
method (Wilson square-and-add, with phi from the per-task scores). The
detectable difference assumes 80% power and a per-task paired-difference sd
of 0.5. No multiplicity adjustment; 1 comparison shown. The baseline verdict
is the point estimate against the tolerance; the interval is shown so a
small n cannot hide.

---

## Undecided runs (`needs-judge`)

A check that cannot decide is a defect in the check, not a fact about the
model. One or two per experiment is an edge task; dozens means the criterion
is not decidable from the window it was given, and the fix is to rewrite the
check, widen its window, or drop it. Nobody reviews 87 undecided runs by
hand, so the report never presents them as a list to work through unless
the list is short.

The report handles them in three places:

- **A warning band, when there are many.** If any check is undecided in
  more than a few runs (say, over 5% of the runs it applies to, or more than
  five), the band under the about table says so, in the same voice as the
  unequal-loss warning: *"The check `answers-correctly` could not decide 87
  of 126 runs. Its pass rates below are over the 39 runs it decided, and the
  check needs rewriting before this experiment is conclusive."* The about
  table's *Result* line carries the same caveat in a clause.
- **A short list under Failures, when there are few.** Each undecided run
  gets its own line with the check, the run and the reason the check gave,
  linked to the run, so a reviewer with five of them can work top to
  bottom. Over the threshold the list collapses to the count and the band.
- **Out of the pass rate on both sides.** Today an undecided check blocks
  the pass, so the run counts as a fail in the headline and the arm is
  punished for the check's weakness. The cleaner accounting treats an
  undecided run like a validity failure for that check: out of the
  denominator, with "decided 39 of 126" beside the rate so the shrinkage is
  visible. That is a change in `Grade.Pass` and `Stats`, not rendering, and
  is the one point in this plan that touches a number.

Upstream of the report, the judge plan's bench measures a judge's undecided
rate before it is trusted; a judge above a few percent on the bench should
be refused in `grading.pass` at validation, so the band is rare.

## What changes underneath, if this is agreed

- **Vocabulary.** "cell" and "sample" leave the report; "run" replaces both.
  "analysed" becomes "counted". "CI" becomes "95% interval". Section names
  as above. The on-disk layout and the CLI keep their words (a directory is
  still `cells/`); only the reader-facing text changes.
- **The about table** replaces the two summary paragraphs. The one-sentence
  result in it is the report's verdict, built from the comparison and
  baseline verdicts that already exist in `stats.json`.
- **Arms, Tasks and Checks sections** are new. They need nothing new in
  `stats.json`: arm details come from `experiment.json`'s copy of the suite,
  tasks and checks from the `descriptions` block.
- **Failures** renders the existing per-arm `failures` list, with task
  counts written as "twice on `uses-bash`" and the exit reason folded into
  the `exit_ok` line when that is the check.
- **Exit reasons** move into "What ran", per arm with the task named when
  the count is small, so a `max_tool_calls` is visibly one arm's problem.
- **Every run** drops the run id from the visible columns and merges
  status and pass into one *Result* column.
- **In `Stats.cs`:** a `verdict` sentence per comparison so the about
  table's result line is computed once; per check, the undecided count and
  the decided denominator; and, if the third point above is agreed, the
  pass rate over decided runs only, with `Grade.Pass` no longer treating
  `needs-judge` as a fail.

## Open questions

- ~~Undecided runs out of the pass denominator (above)?~~ Agreed and built:
  `Grade.Pass` is null for an undecided run, and the pass rate is over
  decided runs.

- Should the about table's *Result* line exist at all, given the brief's
  rule that narrative is the author's prose and the counts, never a model's?
  It is a template over three verdicts, not generated text, so I think yes.
- "Counted" versus "analysed": "counted" is plainer, but the accounting
  columns are named in `stats.json` and the layout plan. Rename in the
  rendering only, or everywhere?
- The Tasks table could carry the fixture id and the prompt in a hover.
  Worth it, or noise?
