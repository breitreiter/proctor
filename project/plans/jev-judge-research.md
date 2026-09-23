---
type: plan
title: Research line — Jev and System One models as proctor's judge
created: 2026-09-20
status: open; reading pass done 2026-09-20 (../learnings/jev-judges.md); measurement plan in jev-trial.md
---

> Vocabulary: written before [suite-task-check.md](suite-task-check.md)
> (2026-09-23). Read eval as suite, case as task, `evals/` as `suites/`,
> `eval.json` as `suite.json` and `cases/` as `tasks/`.

# Research line — Jev and System One models as proctor's judge

Jev is a model that returns typed labels with calibrated probabilities and
cannot generate text. Proctor's judge contract is a fixed label vocabulary plus
quoted evidence, with a deterministic report. Those two shapes are close enough
that the question is not "could this work" but "where exactly does it fail, and
what does it cost us to find out".

This line stays open until we can answer the questions below with our own
numbers rather than other people's READMEs. The first reading pass is filed;
everything after this is measurement.

## The frame

Three constraints already decided elsewhere, which this line must respect
rather than revisit:

- The report is deterministic tables. A model may supply a label from a fixed
  vocabulary and evidence; it may not write prose that reaches a reader.
- The judge must be off-family relative to the arm under test.
- Local inference is free and hosted inference is not, so anything that runs
  on the Strix box wins by default at large N.

Jev satisfies all three on paper. `prior-art/judges.md` is the standard against
which its output format should be read; nothing there needs redoing.

## Questions, in the order they should be answered

### 1. Does it agree with us on our own transcripts?
The only number that decides anything. Take a set of already-graded cells from
`evals/code-change`, hand-label the judgement calls, and measure agreement —
Cohen's kappa, not raw agreement, since our pass rates are skewed. Baselines to
beat: our existing deterministic checks where they overlap, and a cheap LLM
judge (Haiku-class) on the same items. A public benchmark already has Jev
losing to Claude Haiku 4.5 on both accuracy and calibration; we need to know
whether that is the task or the model.

### 2. Does the calibration survive our input shape?
Everything published so far scores Jev on short, self-contained items: support
tickets, emails, snippets. Our `state` is an agent trajectory — tool calls,
file edits, a diff, possibly tens of thousands of tokens against a 32K listed
context. Two sub-questions: does ECE hold at that length, and what do we do
when a transcript does not fit. The known out-of-scope overconfidence (random
letters at p=0.97) says a truncated or empty transcript will be judged
confidently and wrongly unless something screens for it first.

### 3. What threshold, at what coverage?
This is the one that turns into a published number. Independent runs put
p ≥ 0.99 at 100% accuracy over 60.2% of traffic on one set, and p ≥ 0.9 at
1.000 over only 21.5–32.5% on a harder one. Find ours. The output is a
threshold plus the fraction of cells it decides, and the rest become
`needs-judge` — which proctor already models as a first-class outcome that is
never folded into fail. **If this works, "escalation rate" becomes a column in
the report, and the report gets more defensible, not less.**

### 4. Does evidence-by-selection work?
Jev cannot quote, so it cannot satisfy half our judge contract directly. The
proposal in the learnings doc is to enumerate candidate spans from the
transcript windows deterministically and let Jev pick one via `choice` (up to
255 options). A selected span is verbatim by construction. Test whether the
picked span is the one a human would have quoted, and how badly it degrades as
the candidate list grows. If this holds it is a stronger evidence guarantee
than a generative judge can give, and it is the most interesting idea in the
pass.

### 5. How much does rubric wording move the answer?
Reported accuracy with deliberately wrong criteria descriptions: 16.7%, below a
25% random baseline. That makes rubric text the dominant risk in the whole
design — a badly worded question is worse than no judge. Fortunately we own an
eval runner: **the rubric wording is itself an eval**, with the rubric as the
arm and hand-labelled cells as the cases. Also worth testing: `noul` versus
`choice` for the same question, since one report has `noul` roughly twice as
well calibrated.

### 6. Hosted or local, and what does local cost in accuracy?
`OpenJev` (MIT) turns any HF instruct model into a System One decider behind
`POST /v1/systemone`, wire-compatible with the official SDK. That means hosted
Jev and a local Qwen on the Strix box are an endpoint swap. Run the same rubric
both ways on the same cells and measure the gap. A local judge that is a few
points worse but free changes what we can afford at large N; a local judge that
is badly calibrated is worse than none.

### 7. What is the supply risk?
We have just finished measuring the cost of coupling tightly to one tool
(`../learnings/nb-coupling.md`), so do not repeat it. The durable artefact here
is the `/v1/systemone` wire format, which is open, small and already has several
independent implementations. Any integration should be against the wire format
with the endpoint configurable, never against TypeSafe the company. A four-day-
old lab with $40M of seed funding is not a dependency; an HTTP schema with four
implementations is.

## How to run it cheaply

The cheapest viable first experiment needs no proctor changes at all. A Jev
judge can enter as an ordinary `script` check: the script reads
`PROCTOR_TRANSCRIPT` and `PROCTOR_EXPECT`, fans the rubric out in one
`system_one` call, and exits 0/1/2. `alexhawat/judge-jev` already implements
that funnel and its first three exit codes are ours exactly. Nothing in
`Checks.cs`, `Grade.cs`, `Stats.cs` or the renderers needs to move to find out
whether this is any good.

Only if it earns it should a `jev` check join the built-in vocabulary, and the
bar for that is question 3 producing a threshold we are willing to print.

## What would make us drop this

Written down in advance, so the answer is not negotiated after the fact:

- Kappa against our own labels no better than a Haiku-class judge at
  comparable cost.
- No threshold that gives a usable coverage at an accuracy we would publish.
- Calibration that does not survive trajectory-length inputs, with no workable
  screening step.
- Evidence-by-selection failing, leaving us with labels and no evidence — in
  which case Jev can still be a *triage* stage in front of a generative judge,
  but not the judge.
