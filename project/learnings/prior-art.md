---
type: learnings
title: Prior art — what has already been solved, and what has not
created: 2026-09-17
---

# Prior art — what has already been solved, and what has not

The research pass in `plans/research-pass.md`, done 2026-09-17 by reading, not
by running anything. Six source groups: LLM eval frameworks, experiment
trackers, the LLM-as-judge literature, statistics for small-N evals, readout
conventions from adjacent fields, and our own two in-house harnesses. The raw
findings, with URLs and worked numbers, are under `prior-art/`; this document
is what they add up to.

Numbers quoted from papers are as the surveying agents extracted them. Two 2026
preprints in the judge section were read from summaries rather than tables.
Treat any figure here as a pointer into `prior-art/`, not as verified.

## The short version

Most of the problem is solved and the solutions agree with each other. The
pieces that are not solved anywhere are small, specific, and exactly the ones
proctor's brief cares about:

- **Nobody ships a paired arm-versus-arm difference with a confidence
  interval**, even though every comparison UI aligns rows by case. This is
  the single most useful thing proctor can add, and the statistics are
  closed-form.
- **Nobody requires a judge to quote evidence from the transcript and then
  verifies the quote.** The literature says to do it; no tool does.
- **Nobody writes reports with a model.** Every framework confines model text
  to a per-sample reason string. The brief's rule is not contrarian; it is the
  universal practice, and the frameworks that come closest to violating it do
  so by accident (judge-generated rubrics).
- **Nobody resumes a stochastic sweep from disk correctly.** The one tool with
  real dedup keys on input hashes, which is exactly wrong for runs that are
  supposed to differ.

Everything else below is borrowed.

## 1. Experiment definition on disk

**Finding.** Two shapes dominate. Inspect declares a `Task` in Python with
dataset, solver, scorer, epochs and limits, and varies arms through task
parameters. promptfoo declares one YAML file whose arms are the cartesian
product of prompts and providers, with cases as rows. Both are file-based and
both are checked in beside the code under test. The sidecar shape the brief
asks for is the norm, not an innovation.

Inspect is the only framework whose data model already fits a whole-agent run:
a sample carries `messages`, `events[]`, `scores`, `error` and the `limit` that
ended it, which maps almost one-to-one onto nb's JSONL and trailer. promptfoo
flattens the transcript to a string before its rubric assertions see it, which
throws away the tool calls.

Weaver's plan rejected both frameworks in July for a stated reason: the hard
part was the per-harness sandbox invocation, which is custom bash either way,
and a framework would add a viewer plus an install step. That reasoning still
holds for the runner. It does not hold for the grader and reporter, which is
where the one-off code kept growing.

**What proctor takes.** A plain-files experiment definition a project checks
in under its own `evals/` directory. Arms as a declared list, not a cartesian
product by default; cases as files; sample count as a number. Inspect's
sample record as the model for what a run looks like once graded.

**What proctor rejects.** Wrapping the thing under test as a "provider" that
returns a string. nb already produces the structured record; proctor reads it.

## 2. Run bookkeeping

**Finding.** Every tracker surveyed converged on the same answer: a random run
id, never one derived from parameters, plus a small per-run manifest of the
fully resolved inputs. Hydra's triple is the cleanest: the resolved config,
the overrides literally typed, and the tool's own runtime record. W&B adds git
commit and dirty diff, command line, host and start time. Sacred adds a status
enum and a failure trace. Inspect's log header records `task_version`, full
task arguments, `revision{commit, dirty}`, package versions, and
`total_samples` against `completed_samples`.

On disk, the tools that stayed readable used one value per file or one JSON
per concern, so `ls`, `cat`, `jq` and `grep -l` are the query language. The
tools that did not (W&B's binary log, DVC's hidden git refs, MLflow's move to
SQLite) are the ones users complain about. MLflow deprecated its file store
because a second backend doubled every feature.

Timestamp-only directory names were universally overridden by users.

Neither in-house harness records the nb binary version or the prompt code
version. The oracle bench's `--variant` label is a human promise, not a
provenance record.

**What proctor takes.**

- An experiment directory with a manifest at the root describing the full
  matrix, then `<arm>/<sample>/` cells, each with its own manifest, the nb
  program as run, the JSONL as produced, stderr, and a status file.
- Per-run manifest fields, from Inspect and W&B: random id, arm, sample index,
  resolved config, git commit and dirty flag of the repo under test, nb version,
  proctor version, command line, start and end time, status.
- One plain backend. Any index is a derived file that can be rebuilt.
- Content-address shared inputs (prompts, sheets, tool definitions) so N
  samples do not carry N copies.

**What proctor rejects.** Any server or database as the source of truth.
Stdout scraping as the results channel.

## 3. Grading

**Finding.** This is the best-studied question and the literature is
unusually consistent. The concrete rules, with the source in `prior-art/judges.md`:

- **Binary or small-enum labels only.** Likert and numeric scores cluster with
  low variance and are then averaged as if they were measurements. Every
  practitioner source says the same thing.
- **Grade each run in isolation and compare arms by counting labels.**
  Pairwise "which is better" judging manufactures winners: one 2026 study
  measured a 13.6% verdict flip rate on rerun, and position bias is worst
  precisely when the candidates are close.
- **One judge call per dimension, on a deterministically extracted window.**
  Holistic judges over long traces miss about one error in five; judge
  accuracy falls with trace length; per-check judges recover the misses.
  Weaver's judge already does this: it gets the answer JSON and the tail of the
  transcript, not the whole thing.
- **Never feed the judge a model-written summary of the transcript.** The one
  benchmark that measured it found the summary-fed judge lost 17 points of
  precision against the trace-fed one. This closes the door on the tempting
  shortcut from the other direction.
- **Judge output is `{label, evidence: [verbatim quotes]}`, and the quotes are
  verified by substring match.** A quote that does not appear in the transcript
  downgrades the verdict to UNVERIFIED. The literature recommends this; no tool
  does it.
- **Reason-then-label, then discard the reasoning.** Inspect's grader matches
  the last `GRADE:` line to defeat steering. Constrained decoding on the
  reasoning model costs accuracy; format in a second pass if at all.
- **UNKNOWN is a label. DISPUTED is a label.** Sample the judge three times at
  temperature zero; disagreement becomes DISPUTED rather than a silent
  majority.
- **Hide arm identity and randomise grading order.** Self-preference bias runs
  10 to 25 points for a judge grading its own family.
- **Prefer a judge outside every arm's model family.** Weaver decided this in
  July for the same reason. The counter-argument (same family is fine if
  validated against human labels) is legitimate but needs the validation.
- **Length, tokens, tool-call count and exit reason are trailer columns.** The
  judge is never asked anything length-related; verbosity attacks fooled
  earlier judges about nine times in ten.
- **Validate each judge check as a classifier before trusting it.** One to two
  hundred human-labelled runs per check, thirty to fifty per class; report
  true-positive and true-negative rates and Cohen's kappa beside raw
  agreement. "85% agreement" on MT-Bench is a kappa near 0.48. Freeze and
  version the prompt and model; re-validate on any change.
- **Outcome checks first. Process checks only for nameable binary properties**
  such as a loop, a forbidden tool, an expected tool never called, or a
  premature stop, and most of those are deterministic from the transcript.
  Never ask for holistic trajectory quality or step-level blame.
- **Parse failures and refusals stay in the denominator.** Weaver already
  records `valid_answer` separately from correctness.

The pattern both in-house harnesses converged on independently is the same
one: deterministic gate first, `needs-judge` as a third state, judge returns
booleans only, the pass bar is code. That is confirmed, not overturned.

**What proctor takes.** All of the above as the grader contract. The trailer
answers most questions with no model at all; a judge is a per-dimension binary
classifier with quoted evidence, run in a separate re-runnable phase, from a
model outside the arms, and it never decides pass or fail.

**What proctor rejects.** Judge-generated rubrics and logprob-weighted scores.
Any single call over a whole transcript. Any Likert scale.

## 4. Statistics at small N

**Finding.** The methods are settled and closed-form; the surveying agent
computed the interval comparison at n of 5, 20 and 50 in `prior-art/stats.md`.

| question | method | print as |
|---|---|---|
| per-arm pass rate | Wilson 95%, no continuity correction | `62% [45, 77] (13/21)` |
| two arms, shared cases | paired difference from discordant counts, Newcombe paired interval; McNemar mid-p in the appendix | `+24 pts [+6, +41] (A won 6, B won 1, tied 14)` |
| two arms, different cases | difference with Newcombe hybrid score interval, row labelled unpaired | `+24 pts [-3, +46] (unpaired)` |
| counts and durations | median and IQR as headline, mean with t-interval (log scale for time) below n=20, BCa bootstrap with fixed seed above | `median 14 (IQR 9-22), mean 17 [12, 25], n=21` |
| how many samples | Miller's paired formula; also the inverse, MDE for the n you have | one sentence per experiment |

Wald intervals are unusable at these sizes: at 5 of 5 they print a zero-width
interval at 100%. Wilson prints "at least 57%", which is what a reader should
see. Bootstrap coverage is about 82% at n=5 for a nominal 95%, so no bootstrap
below n of 20. If cases have K reruns, reduce to per-case means first, then
interval the cases; more unique cases beats more reruns.

The worked power numbers are sobering and belong in every report: with the
per-case difference spread typical of these evals, n=5 paired cases detects a
63-point difference, n=20 detects 31 points, and 10 points needs about 200
cases. Most of our past experiments could not have detected the effects we
were looking for, and the report never said so.

At two to six arms, treat the experiment as exploratory: show all pairwise
intervals unadjusted, put Holm-adjusted significance in an appendix.

**What proctor takes.** Exactly the table above, plus the MDE sentence on
every report and on every "no clear difference" cell. Closed-form by default,
deterministic by construction.

**What proctor rejects.** Judging a comparison by whether two per-arm
intervals overlap. P-values in headline tables. "No significant difference"
without the MDE beside it.

## 5. Report shape

**Finding.** Four fields solved "one table a manager reads, one an expert
checks" the same way. Go's benchstat prints `± 1%` beside every number and a
`~` for no detectable change. Criterion prints `[lo est hi]` and a fixed
sentence. CONSORT requires a flow diagram accounting for every participant
before any result is shown, and an effect size with its interval. Microsoft's
experimentation platform checks sample-ratio mismatch before anything else and
structures the readout as tested, happened, recommend.

The three conventions that most help mixed audiences:

1. **An interval beside every point estimate and a fixed verdict vocabulary
   instead of p-values.** The manager reads the glyph; the expert reads the
   interval and the footnote. Both come from the same counts.
2. **Account for every run before showing any outcome.** Attempted, completed,
   graded, analysed, per arm, with a reason for every departure, and `k/n`
   printed inside the rate cell so small n is visible in the headline itself.
3. **Separate "did it improve" from "did anything get worse".** Success in the
   headline; failure categories and cost as guardrails with one-sided
   wording.

promptfoo's results matrix (rows are cases, columns are arms, header is the
aggregate, cell is verdict plus reason) is the best single table seen and the
natural drill-down under the headline.

**What proctor takes.** The eight-section skeleton in `prior-art/reports.md`:

1. Templated summary: tested, happened, recommend. Every delta carries its
   interval in the same sentence.
2. Headline table, one row per arm, control first in declared order: runs
   graded of attempted, pass rate with `k/n`, Wilson interval, delta versus
   control, delta interval, verdict glyph. Glyphs only when the delta interval
   excludes zero and exceeds a declared practical threshold.
3. Accounting table, CONSORT-style, stage by arm, with exclusion reasons, and
   an imbalance warning above the headline when arms lost runs unequally.
4. Failure breakdown: exit reason by arm, guardrail flag only on evidence of
   harm.
5. Cost and effort distributions: median, p90, mean, range per arm; outliers
   listed, never silently dropped.
6. Arm setup table: descriptive only.
7. Appendix: per-run table with links to transcripts, failures first; the
   case-by-arm matrix.
8. Appendix: reproducibility block with versions, hashes, command line, and
   the statistical methods used.

Templated "no difference" wording, fixed: *B passed 86% (42/49) vs A 72%
(36/50): +14 points (95% CI −2 to +30). No detectable difference at n=50 per
arm; a difference smaller than about 18 points would not have been detected.*
Never "equivalent".

The oracle bench's habit of naming the one dangerous number and explaining it
in the same line ("done-on-waiting") is a smaller version of the guardrail
row and worth keeping as a convention: a report may declare one metric as the
thing that must not get worse.

**What proctor rejects.** Sorting the headline by score. Means as the headline
for skewed metrics. Any narrative that is not a filled template.

## 6. A model in the reporting loop

**Finding.** No surveyed framework, tracker, or practitioner source has a model
write a report. The closest any comes is a per-sample `reason` string from a
judge, shown in a cell. DeepEval's G-Eval has a model generate the rubric
steps, which is the nearest thing to a violation and is also the pattern the
judge literature singles out as irreproducible.

The brief's rule therefore costs nothing in lost prior art. The design has
one place where model text appears at all: the judge's evidence quotes, which
are verbatim from the transcript and verified.

## What this overturns in the brief

Nothing is overturned. Two things are sharpened:

- The open question "is the grader a model, a rubric, or deterministic checks"
  is answered: all three, in that order reversed. Deterministic checks on the
  trailer and transcript first; a rubric of named binary criteria applied per
  dimension; a model only as the classifier for criteria that need reading,
  emitting a label and quotes, never a verdict.
- "Logs stay authoritative, verdicts separate" is confirmed by every tracker
  and by weaver's `.judged.jsonl` sibling. The verdict file should also carry
  the judge's prompt hash and model so a regrade is distinguishable from the
  original.

One addition the brief did not anticipate: every report should state what it
could not have detected. Past experiments were mostly underpowered and the
reports did not say so.

## The decisions this makes obvious

Each of these is settled enough by the research to deserve its own plan
rather than more reading.

1. **The on-disk layout of an experiment.** Root manifest, `<arm>/<sample>/`
   cells, per-cell manifest and status, content-addressed shared inputs,
   idempotent resume keyed on the cell. Borrowed from Hydra, Inspect and W&B,
   with the resume nobody built.
2. **The grader contract.** Trailer-derived columns; deterministic checks;
   per-dimension binary judge with verified evidence quotes, UNKNOWN and
   DISPUTED, off-family model, separate re-runnable phase; pass bar in code;
   judge validated as a classifier before use. Borrowed from Inspect's grader
   hardening and the judge literature, with the quote verification nobody
   built.
3. **The report skeleton and its statistics.** The eight sections above,
   Wilson and Newcombe intervals, the MDE sentence, fixed verdict vocabulary,
   templated narrative. Borrowed from benchstat, Criterion, CONSORT and the A/B
   platforms, with the paired difference interval nobody ships.

A fourth decision, distribution, is settled in `ci-distribution.md`, and a
runner shape in `test-framework-patterns.md`.

Two candidates for nb rather than proctor, since they pass the "makes sense
without proctor" test: the trailer could carry a content hash of the program
and the nb version, so provenance does not depend on proctor having captured
them; and a `budget`-style directive for a deterministic random seed on
providers that accept one would make the reproducibility block honest.
