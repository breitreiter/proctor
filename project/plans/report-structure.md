---
type: plan
title: The report, rebuilt for a reader who was not there
created: 2026-09-22
status: built 2026-09-22 as written, undecided attempts out of the pass denominator; reviewed 2026-11-02 (below); the snapshot is the worked example rendered by the code
---

# The report, rebuilt for a reader who was not there

The first real report (api-docs lookups, 2026-09-22) read as a wall of ids
and intervals. Descriptions on checks and tasks helped, but the page still
assumes the reader knows what a variant, a task, an attempt and a check are, opens
with a data dump written as prose, and shows the same pass counts from five
angles without saying which one to trust. This plan is the structure that
replaces it. It is written as the worked smoke batch, rendered in the
proposed order, so the disagreement can be about actual lines.

The rule the structure follows: **define a term before using it, then use it
consistently.** Five terms carry the whole report:

| term | one sentence, used verbatim wherever the term is introduced |
|---|---|
| exam | A collection of related tasks with one business goal. Named once, in the opening table; the description under the title is the exam's. |
| variant | One configuration under test: a harness, a provider and a model, given every task. The first variant is the reference the others are compared with. |
| task | One input and one desired outcome, given to every variant: a prompt against a fixture repository, with the checks that say whether the outcome was reached. |
| attempt | One variant at one task, once. A task is attempted several times per variant, so the score is not one lucky or unlucky attempt. (This plan first chose "run" for the page; the word is "attempt" since 2026-11-15, `exam-variant-attempt.md`, on the page and on disk alike.) |
| check | One yes-or-no test over a finished attempt. The **headline** checks together decide whether an attempt passed. A **validity** check decides whether an attempt counts at all. Any other check is a **guardrail**: reported, not part of the pass. |

Every heading gets one sentence under it saying what the section is for.
Statistics stay, but move behind the plain result and get a plain name:
"95% interval", never "CI".

## Review, 2026-11-02

Read fresh against `20261102-1143-code-change-jy0n` (5 variants, three of them agents,
3 tasks) and `20261030-1514-oracle-change-n8hq` (the oracle, 1 task), rendered with
headless Chrome.

**Where the page has rightly moved past this plan.** The order below is now:
the verdict card, then the about table (no *Result* row: the card is the result), Variants,
Tasks, Results, Failures, Attempt summary (was *What ran*), Check pass rates, Cost and effort, Every attempt,
Reproducibility and method. *Results by task* is gone as a section: each task's attempts,
score and baseline sit in its own block under Tasks, with the reason each failed attempt
failed, which reads better than a matrix far from the task's sentence. Attempt summary gains
*Decided*. The card is the place for direction and confidence ("Worse (unconfirmed)"),
the about table only for what ran.

**Fixed in the same change** (CHANGELOG, Unreleased): a false "lost attempts unequally"
when variants have different attempt counts; a failed attempt's reason naming a guardrail ahead
of the headline check that failed it; `summary.md` rows split by multi-line reasons;
"nb" wherever the page meant any harness; agent cost not shown though `core.json` has
it; Failures calling a judge's `error` a failure; tables running off the page at five
variants.

**Decided.** A check's `error` is out of its rate on both sides, like `needs-judge`, and
counted beside it: an error is the check's weakness, not the variant's. A headline check
that errored still fails the attempt, so the pass rate is unchanged.

**Next revision** (not built):

- *Baseline verdicts for variants the baseline never saw.* `cc-sonnet` and `codex` are
  "Unchanged" against a baseline pinned on 22 September, before either existed. The
  baseline is per task, so the arithmetic holds, but the word reads as "no change
  since then". Say "against the baseline's scores" for a variant with no pinned attempts of
  its own, or leave it out of the card.
- *Repetition at many variants in Results.* Results has one near-identical comparison
  paragraph per variant; at five variants a comparison table should replace them. (The card's
  half of this is done; see below.)
- *Per-task check lists.* Each task block lists every check it carries, 9 of 11 the
  exam's, three times over. This plan listed only what was particular to the task;
  go back to that, with the exam's checks once under Checks.
- *"Each attempted 3 times or once per variant".* With mixed attempt counts, say it per variant
  ("3 attempts per task for `floor` and `qwen`, 1 for the rest").

**Reviewed by the user, 2026-11-02, and built.**

- *Why unconfirmed* is the report's, not a variant's: the interval's width rests on the task
  count, which every variant shares, so the foot says it once ("with 3 tasks this report can
  only confirm a change of about 81 points"), from the report's widest pairing. With a
  baseline, each variant's row carries its own standing against the reference ("Against
  **floor**: +11 points, not a detectable difference") where the foot repeated it per
  variant. A variant paired on fewer tasks than the report (every attempt on a task lost or
  undecided) says its own reach on its row; `stats.json` carries it as `mde_points` on
  that comparison.
- *What ran* is *Attempt summary*, opening "This is how far each attempt got before
  terminating."

**Built, 2026-11-08** (the four items under *Next revision*, and the todo's
charts). The card opens with a chart: each variant's pass rate on its interval with
the baseline's own score marked, and its change in points with its interval,
hollow when not confirmed (inline SVG from `stats.json`, a table in
`summary.md`). Under it, a line per variant that moved and one for the rest. A variant
the baseline never ran gets a sentence saying whose attempts the baseline is.
Results compares against the reference in one table. The exam's checks are
listed once in Tasks, each task only its own. Mixed attempt counts are said per
variant. The heatmap and cost-against-pass-rate candidates were not built: the
task blocks carry the per-task view, and nb reports no cost.

**For the site, not the page.** The about table names the host (`sanjuro`) and the
judge rows name the gateway (`library:8086`) because the machine's config spells them
so. A report copied to the public site needs those scrubbed, or written as
`${LLM_GATEWAY}` in the config that graded it.

## Reading order

1. **Title and about** (a table, not a paragraph)
2. **Variants** (defines the term, lists them)
3. **Tasks** (defines the term, lists them with what each asks, its fixture, and the checks it carries beyond the shared ones)
4. **Checks** (defines the term and the three roles, lists them with what each tests and which tasks carry each)
5. **Results** (pass rate per variant, the comparison, the baseline; the verdict in words)
6. **Failures** (per variant, the checks that failed and on which tasks, in the words from 3 and 4)
7. **Attempt summary** (planned, started, completed, graded, analysed; what was excluded and why; how nb ended each attempt, per variant)
8. **Results by task** (the glyph matrix, now that variant, task and attempt are defined)
9. **Check pass rates** (the checks table with per-variant rates)
10. **Cost and effort**
11. **Every attempt** (the per-attempt table, failures first)
12. **Reproducibility and method** (versions, hashes, command; the statistics paragraph, last)

Sections 1 to 6 are the report. A reader who stops after 6 has the answer.
Sections 7 to 12 are the evidence for a sceptical reader, in the order they
would ask for it.

## Worked example: the smoke batch in this structure

What follows is `summary.md` for the snapshot batch
(`20260917-1432-smoke-k7px`), rewritten by hand in the proposed structure.
The numbers are the snapshot's. The HTML is the same text with tables.

---

# smoke — 20260917-1432-smoke-k7px

Can a local coder model make a small change to a .NET repository so that it
builds and the tests pass?

| | |
|---|---|
| Exam | `smoke` |
| Batch | 2026-09-17 on `bench` |
| Variants | 2: `floor` (the reference), `b` |
| Tasks | 3, each attempted 3 times per variant |
| Attempts | 18 planned, 16 counted |
| Result | `b` passed 89% of its attempts, `floor` 56%. With 3 tasks the difference (+33 points) is not statistically detectable. Against the pinned baseline, `floor` regressed and `b` improved at a tolerance of 10 points. |

## Variants

A variant is one configuration under test: a harness, a provider and a model,
given every task. The first variant is the reference the others are compared
with.

| Variant | What it is | Harness | Provider | Model | Runs per task |
|---|---|---|---|---|---|
| `floor` | the local floor | nb | local-qcoder | qwen-coder | 3 |
| `b` | | nb | local-glm | glm | 3 |

## Tasks

A task is one input and one desired outcome, given to every variant: a prompt
against a fixture repository, with the checks that say whether the outcome
was reached. Each task is attempted 3 times per variant, so a score is not one lucky
or unlucky attempt. Every task's attempts carry the 3 checks the exam declares,
listed under Checks; the last column is what a task's attempts are checked for
beyond those, from its fixture or its own file, in that task's own words.

| Task | What it asks | Fixture | How it is measured |
|---|---|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop | `note` | — |
| `plain` | The model answers in one turn with no tools | `note` | — |
| `uses-bash` | The model runs one bash command and answers | `note` | `used-bash` (guardrail): uses bash |

Checks are the task's, not the exam's: an attempt carries the union of what the
exam, its fixture and its own file declare (2026-09-23,
[the exam, task and check note](../archive/suite-task-check.md)). Listing the whole union per
task would repeat the exam's checks on every row, so the table shows what
is particular to the task and the Checks section says which tasks carry
each check. "Particular" means declared by the fixture or the task, not
"carried by fewer than every task": a headline every task declares under
one name, each with its own criterion, is on every row with that task's
sentence, headline first (2026-09-23,
[the bug](../archive/per-task-check-descriptions-never-reach-the-report.md)).
The row then reads as what we asked and how we decided. The fixture column
is omitted when no task names one.

## Checks

A check is one yes-or-no test over a finished attempt. The headline checks
together decide whether an attempt passed. A validity check decides whether an attempt
counts at all: an attempt that fails one is left out of every rate, not counted as
a failure. Any other check is a guardrail: reported, not part of the pass.
A check that cannot decide an attempt leaves that attempt out of its rate on both
sides. A check is declared by the exam, by a fixture or by a task, and its
rate is over the attempts of the tasks it is on.

| Check | What it tests | Role | On |
|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | every task |
| `builds` | the repository builds after the change | headline | every task |
| `no_denials` | no denied tool calls | validity | every task |
| `used-bash` | uses bash | guardrail | `uses-bash` |

"What it tests" is one sentence only when the check says one thing
everywhere: the exam's checks always do, and a fixture's or a task's does
when every declaration describes it the same way. A check whose tasks
describe it differently reads "per task; see Tasks" rather than one task's
sentence standing in for all of them.

## Results

Pass rate is the share of counted attempts in which every headline check held,
averaged task by task so that one task with many attempts does not outweigh
another. The 95% interval says how far the true rate could plausibly sit
from the measured one; with 3 tasks it is wide.

| Variant | Pass rate | 95% interval | Passed / counted attempts |
|---|---|---|---|
| `floor` | 56% | 15% to 90% | 4 / 7 |
| `b` | 89% | 35% to 99% | 8 / 9 |

**`b` against `floor`.** `b` passed 33 points more of its attempts than `floor`.
The 95% interval on that difference runs from −31 to +75 points, so with 3
tasks the difference could be noise: no detectable difference. Task by task,
`b` did better on 2, worse on 0, the same on 1. To detect a 10-point
difference reliably this exam would need about 200 tasks.

**Against the baseline.** The baseline is the score pinned for each task on
2026-09-14; the verdict compares each variant's score with it and calls anything
more than 10 points either way a change.

| Variant | Difference from baseline | 95% interval | Tasks better / worse / same | Verdict |
|---|---|---|---|---|
| `floor` | −22 points | −67 to +39 | 0 / 2 / 1 | regressed |
| `b` | +11 points | −46 to +63 | 1 / 0 / 2 | improved |

| Task | Baseline | `floor` | `b` |
|---|---|---|---|
| `loops` | 100% | 67% | 100% |
| `plain` | 100% | 100% | 100% |
| `uses-bash` | 33% | 0% | 67% |

## Failures

For each variant, the checks that failed in at least one counted attempt, most
frequent first, with the tasks they failed on.

**`floor`** (the local floor) failed 2 checks:

- **the repository builds after the change** (`builds`, headline) failed in
  3 of 7 attempts: twice on `uses-bash`, once on `loops`.
- **nb exits with 'ok'** (`exit_ok`, headline) failed in 1 of 7 attempts: once on
  `loops`, where nb stopped at the tool-call limit.

**`b`** failed 1 check:

- **the repository builds after the change** (`builds`, headline) failed in
  1 of 9 attempts: once on `uses-bash`.

A check described per task leads with its id and carries each task's
sentence beside the task: **`answer`** (headline) failed in 2 of 9 attempts:
once on `lookup-semantic` (the answer gives `#fbedef`), once on
`fanout-no-guidance` (the answer gives 120).

## Attempt summary

Every attempt planned by the matrix, and how far it got. *Started* attempts
began; *completed* attempts ended with a transcript from nb; *graded* attempts
have their checks; *counted* attempts are graded and passed every validity
check. Rates are over counted attempts only.

| Variant | Planned | Started | Completed | Graded | Counted |
|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 7 |
| `b` | 9 | 9 | 9 | 9 | 9 |

> **The variants lost attempts unequally.** `floor` counted 7 of 9, `b` 9 of 9. The
> rates above are over counted attempts; the two attempts `floor` lost are listed
> here.

Runs left out, and why:

- `floor` / `plain` / attempt 3: not counted, failed a validity check.
  `no_denials`: 1 denied call: bash (no-match)
- `floor` / `uses-bash` / attempt 2: never completed. Attempt setup hook failed:
  hooks/reset-fixture.sh exited 1: clone failed

How nb ended each completed attempt. `ok` is a normal finish; anything else is
nb stopping the attempt, which the checks then grade like any other.

| Variant | ok | max_tool_calls |
|---|---|---|
| `floor` | 7 | 1 (`loops`, attempt 3) |
| `b` | 9 | 0 |

## Results by task

One row per task, one column per variant, one mark per attempt: ● passed, ○ failed,
× not counted. Hover a mark for the reason; click it for the attempt.

| Task | What it asks | `floor` | `b` |
|---|---|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop | ● ● ○ | ● ● ● |
| `plain` | The model answers in one turn with no tools | ● ● × | ● ● ● |
| `uses-bash` | The model runs one bash command and answers | ○ × ○ | ● ○ ● |

## Check pass rates

How often each check held, per variant, over the attempts it applies to. Headline
checks are over counted attempts; a validity check is over every graded attempt,
because it says how many were counted.

| Check | What it tests | Role | `floor` | `b` |
|---|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | 89% (6/7) | 100% (9/9) |
| `builds` | the repository builds after the change | headline | 56% (4/7) | 89% (8/9) |
| `no_denials` | no denied tool calls | validity | 89% (7/8) | 100% (9/9) |

## Cost and effort

Per completed attempt. Duration is wall time including hooks; tokens are what
nb reported. Cost is not shown: nb does not report it.

| Variant | Duration (min): median | p90 | mean | range | Tokens: median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Every attempt

Every attempt, failures first. *Reason* is the first check that did not hold and
what it saw, or why the attempt never completed.

| Variant | Task | Attempt | Result | nb ended | Duration | Tokens | Reason |
|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | ○ failed | ok | 17.3 | 63,000 | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | ○ failed | max_tool_calls | 23.5 | 47,200 | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | ○ failed | ok | 13.5 | 41,200 | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | ○ failed | ok | 16.9 | 47,200 | builds: 2 of 41 tests failed |
| `floor` | `plain` | 3 | × not counted | ok | 16.9 | 67,200 | no_denials: 1 denied call: bash (no-match) |
| `floor` | `uses-bash` | 2 | × never completed | | 15.2 | | attempt setup hook failed: hooks/reset-fixture.sh exited 1: clone failed |
| `b` | `loops` | 1 | ● passed | ok | 16.5 | 61,000 | |
| … | | | | | | | |

(The attempt id moves out of the visible columns into the row's anchor and the
hover text; it identifies, it does not inform.)

## Reproducibility and method

| | |
|---|---|
| proctor | 0.1.0 |
| nb | 1.0.0 at /usr/local/bin/nb |
| exam hash | sha256:9c1e0000 |
| repository | 3f2c1e9a |
| command | `proctor run smoke` |
| created | 2026-09-17T14:32:00Z |

Each task is scored as its mean over its counted attempts, so n in every
interval is the number of tasks. Per-variant rates use the Wilson 95% interval.
Differences between variants, and against the baseline, use Newcombe's paired
method (Wilson square-and-add, with phi from the per-task scores). The
detectable difference assumes 80% power and a per-task paired-difference sd
of 0.5. No multiplicity adjustment; 1 comparison shown. The baseline verdict
is the point estimate against the tolerance; the interval is shown so a
small n cannot hide.

---

## Undecided attempts (`needs-judge`)

A check that cannot decide is a defect in the check, not a fact about the
model. One or two per batch is an edge task; dozens means the criterion
is not decidable from the window it was given, and the fix is to rewrite the
check, widen its window, or drop it. Nobody reviews 87 undecided attempts by
hand, so the report never presents them as a list to work through unless
the list is short.

The report handles them in three places:

- **A warning band, when there are many.** If any check is undecided in
  more than a few attempts (say, over 5% of the attempts it applies to, or more than
  five), the band under the about table says so, in the same voice as the
  unequal-loss warning: *"The check `answers-correctly` could not decide 87
  of 126 attempts. Its pass rates below are over the 39 attempts it decided, and the
  check needs rewriting before this report is conclusive."* The about
  table's *Result* line carries the same caveat in a clause.
- **A short list under Failures, when there are few.** Each undecided attempt
  gets its own line with the check, the attempt and the reason the check gave,
  linked to the attempt, so a reviewer with five of them can work top to
  bottom. Over the threshold the list collapses to the count and the band.
- **Out of the pass rate on both sides.** Today an undecided check blocks
  the pass, so the attempt counts as a fail in the headline and the variant is
  punished for the check's weakness. The cleaner accounting treats an
  undecided attempt like a validity failure for that check: out of the
  denominator, with "decided 39 of 126" beside the rate so the shrinkage is
  visible. That is a change in `Grade.Pass` and `Stats`, not rendering, and
  is the one point in this plan that touches a number.

Upstream of the report, the judge plan's bench measures a judge's undecided
rate before it is trusted; a judge above a few percent on the bench should
be refused in `grading.pass` at validation, so the band is rare.

## What changes underneath, if this is agreed

- **Vocabulary.** One word for one variant at one task, once: "attempt",
  on the page and on disk (this plan first said "run" on the page; changed
  2026-11-15 with `exam-variant-attempt.md`). "analysed" becomes "counted".
  "CI" becomes "95% interval". Section names as above.
- **The about table** replaces the two summary paragraphs. The one-sentence
  result in it is the report's verdict, built from the comparison and
  baseline verdicts that already exist in `stats.json`.
- **Variants, Tasks and Checks sections** are new. They need nothing new in
  `stats.json`: variant details come from `batch.json`'s copy of the exam,
  tasks and checks from the `descriptions` block.
- **Failures** renders the existing per-variant `failures` list, with task
  counts written as "twice on `uses-bash`" and the exit reason folded into
  the `exit_ok` line when that is the check.
- **Exit reasons** move into "What ran", per variant with the task named when
  the count is small, so a `max_tool_calls` is visibly one variant's problem.
- **Every attempt** drops the attempt id from the visible columns and merges
  status and pass into one *Result* column.
- **In `Stats.cs`:** a `verdict` sentence per comparison so the about
  table's result line is computed once; per check, the undecided count and
  the decided denominator; and, if the third point above is agreed, the
  pass rate over decided attempts only, with `Grade.Pass` no longer treating
  `needs-judge` as a fail.

## Open questions

- ~~Undecided attempts out of the pass denominator (above)?~~ Agreed and built:
  `Grade.Pass` is null for an undecided attempt, and the pass rate is over
  decided attempts.

- Should the about table's *Result* line exist at all, given the brief's
  rule that narrative is the author's prose and the counts, never a model's?
  It is a template over three verdicts, not generated text, so I think yes.
- "Counted" versus "analysed": "counted" is plainer, but the accounting
  columns are named in `stats.json` and the layout plan. Rename in the
  rendering only, or everywhere?
- The Tasks table could carry the fixture id and the prompt in a hover.
  Worth it, or noise?
