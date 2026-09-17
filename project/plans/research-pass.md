---
type: plan
title: Research pass — what has already been solved
created: 2026-09-17
status: done 2026-09-17, see ../learnings/prior-art.md
---

# Research pass — what has already been solved

The problem proctor addresses has been solved many times by capable people.
Before designing anything, survey those solutions and take what works. The
brief sets the filter: on the instrumentation side we borrow patterns freely
and owe nothing to convention; on the reporting side the output has to be
legible to non-experts and defensible to experts, and must not depend on a
model writing prose.

This is a reading pass, not a bake-off. The output is a learnings document,
not a shortlist of dependencies.

## Questions the pass has to answer

1. **Experiment definition.** How do the good frameworks describe an arm, a
   variant, a sample count, and a sweep over a lever, on disk? What do they get
   wrong when the thing under test is a whole agent run rather than a single
   prompt and completion? In particular, which of them support the sidecar
   shape the brief asks for, where a project checks in its evals beside its
   code the way it checks in `test/`, and how do they keep eval concerns from
   leaking into the codebase under test?
2. **Run bookkeeping.** How do they name and store runs so nothing is labelled
   by hand afterwards, and so a run can be joined back to the exact program,
   config, and code that produced it?
3. **Grading without loading everything.** What do they extract from a
   transcript before a judge sees it, and how do they keep a judge honest:
   fixed label vocabularies, quoted evidence, pairwise versus pointwise, known
   biases such as position and verbosity preference?
4. **Small-N statistics.** With five to fifty samples per arm, what interval
   and comparison methods do the careful ones use, and how do they present
   uncertainty so a PM reads it correctly and a statistician does not object?
5. **Report shape.** What does a readout look like that survives both
   audiences? Which tables appear in every good one, and what do they leave to
   an appendix?
6. **What they do with a model in the reporting loop, if anything**, and
   whether any of them constrain it the way the brief demands.

## Where to look

Grouped by the question they mostly answer. Not exhaustive; add as found.

**Eval frameworks for LLM and agent work.** Inspect (UK AISI), OpenAI Evals,
promptfoo, Braintrust, LangSmith evaluations, DeepEval, lm-evaluation-harness,
HELM. Inspect is the one most likely to have thought about agentic transcripts
and log viewers; promptfoo is the one most likely to have thought about
matrix-style variant sweeps from a config file.

**Experiment tracking outside LLM work.** MLflow, Weights & Biases, Sacred,
Hydra multirun, DVC experiments. The interesting part is how they key a run
back to its inputs, not their UIs.

**LLM-as-judge literature.** MT-Bench and the "Judging LLM-as-a-Judge" paper,
G-Eval, Prometheus, and the follow-ups on position bias, verbosity bias, and
self-preference. Also inter-rater agreement measures (Cohen's kappa and
friends) as the honesty check on a judge.

**Statistics for evals.** Anthropic's "Adding Error Bars to Evals" (Evan
Miller, 2024) is the closest thing to a canonical treatment of question one
through four for this domain. Wilson intervals, bootstrap CIs, and paired
comparisons for when arms share cases.

**Report conventions from adjacent fields.** A/B test readouts as practised at
large web companies, benchmark reports from performance tooling such as
Criterion and pytest-benchmark, and clinical trial summary tables. All three
have solved "one table a manager reads, one an expert checks".

**In-house prior art.** Two survive on disk, and they are the concrete record
of what we kept rebuilding, so any borrowed pattern has to beat them.

- nb's `evals/oracle-bench/`: N samples per case, hermetic config beside the
  results, `results.jsonl` plus `summary.md` plus a `runs/` directory.
- `../weaver/evals/` (July 2026, design in `weaver/project/plans/agent-evals.md`):
  a harness-by-model matrix (claude, codex, nb) over ten "rungs", each a
  `prompt.md` plus a deterministic `grade.sh`. Notable: the model's answer is a
  JSON block extracted from the end of its reply, deterministic gates run
  inline, and only the four rungs that need prose judgement go to a separate,
  re-runnable judge pass using an off-distribution model. Results are one row
  per run in `results-<stamp>.jsonl`, with a `.judged.jsonl` sibling folding
  verdicts in. That two-phase split, deterministic first and judge only where
  unavoidable, is already the shape the brief asks for.

## Output

- `learnings/prior-art.md`: findings per question above, with what proctor
  takes and what it rejects, and why. Negative findings included.
- Edits to `brief.md` where a finding overturns something written there.
- A short list of the two or three design decisions the research makes
  obvious, each of which then gets its own plan.

## Not in scope

- Choosing a language or dependency. That follows from the findings.
- Prototyping. Reading only.
- Surveying the harnesses under test (Claude Code, Codex, and so on). That is
  nb's business.
