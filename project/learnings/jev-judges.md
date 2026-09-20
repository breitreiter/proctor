---
type: learnings
title: Jev as proctor's judge — first pass
created: 2026-09-20
---

# Jev as proctor's judge — first pass

A reading pass on 2026-09-20 against the research line in
`../plans/jev-judge-research.md`. Nothing had been run when this was written;
the two routes were probed later the same day, see `../plans/jev-trial.md`.
Every number below came from a vendor page, a third-party repo README or a summary of one; treat
them as pointers to check, not as measurements we made. The ecosystem is four
days old and the noise-to-signal ratio is what you would expect.

## What Jev is

TypeSafe AI (out of stealth 2026-09-15, $40M seed) shipped **Jev**, the first
of what it calls **System One models**: a model that does not generate text at
all. You send it one `state` plus a list of typed questions and it returns one
typed answer per question, each with a calibrated probability. Three question
primitives:

| primitive | answer |
|---|---|
| `noul` | a yes/no with a probability |
| `choice` | one option from a list you define (cardinality up to 255) |
| `score` | a position on an ordinal scale you define |

Claimed shape: 70–500 ms end to end, **$0.042 / MTok in, output free**, 32K
context on Cloudflare's listing, 0% hallucination rate in the trivial sense
that the output is schema-matched and cannot be anything else. Training is
"RLCD" — reinforcement learning for calibrated decisions, optimising Brier-ish
calibration rather than human preference. It cannot emit a string; it gives up
generation entirely.

## Why this is interesting for us specifically

Our judge constraint is already written down and is unusually strict: the
report is deterministic tables, and the model's role is limited to **a fixed
label vocabulary plus quoted evidence** (brief; `prior-art/judges.md` §1). Every
finding in that prior-art pass points the same way — binary beats Likert,
pointwise beats pairwise for objective questions, discard the reasoning and
keep the label.

Jev is a judge that is *physically incapable* of violating that constraint. It
cannot write prose into our report because it cannot write prose. The usual
LLM-judge failure modes we catalogued — verbosity preference, position bias in
pairwise, score clustering on Likert, a judge that argues itself into a verdict
— are either absent or become measurable numbers instead of vibes.

Three more fits, in descending order of how much they matter:

1. **It is off-family.** Our judge-hosting note requires a judge from a
   different family than the arm under test; today that is a paid Cloudflare
   Workers AI model. TypeSafe is nobody's family. A Jev judge is off-family for
   Claude Code, Codex and qwen arms simultaneously.
2. **The three outcomes already line up.** Proctor's check contract is
   0/1/2 = pass/fail/needs-judge, and `Verdict` has `NeedsJudge` as a first
   class result that is never folded into fail. A calibrated probability is
   exactly the input a principled `needs-judge` threshold wants. Today the
   third outcome is a judgement call; with a calibrated judge it becomes a
   coverage number we can print.
3. **Batching is absurdly cheap.** One independent run reports **800 typed
   judgments in a single call: 985 ms, $0.00075**. A whole experiment's rubric,
   across every cell, is fractions of a cent and about a second. That changes
   what is affordable at large N, which is the regime the local-inference note
   says we care about.

## What the independent evaluations actually say

This is the part that matters, and it is more mixed than the launch coverage.
Several third parties ran pre-registered or contamination-free tests within
days. Read together:

**The good.**
- `priorbench/jev` — pre-registered, 50 predictions filed before collection,
  5,721 calls. **95.9% zero-shot accuracy** on a 400-item benchmark against
  77.2% for hand-written keyword matching and 66.0% for supervised TF-IDF +
  logistic regression trained on all the labels. 26 of 50 predictions
  confirmed, 21 falsified — and **18 of the 21 misses were the authors
  predicting failure that did not happen**. That directional bias is worth
  noting: the sceptics were wrong more often than they were right.
- Same source: tasks TypeSafe's own docs call unreliable measured **99.6%
  across 13 designs** for number comparison and date ordering, **100%** for
  negation. The vendor understated it.
- Thresholding works where it counts. `priorbench` reports accuracy flat from
  0.50 to 0.95 and then **100% at p ≥ 0.99, covering 60.2% of traffic**.

**The bad, and it is the part that decides the design.**
- Calibration is real but not clean. `jev-exploration`'s 800-item
  difficulty-gradient set measures **ECE 2.1–2.5× its own noise floor** across
  all four difficulty tiers, with the error concentrated mid-range. At p ≥ 0.9
  the hit rate was 1.000 in every tier but **coverage was only 21.5–32.5%**.
- It is not uniformly better than a cheap LLM judge. On `jev-phishing-bench`
  (2,000 emails) Jev scored **62.6% accuracy at ECE 0.154** against **Claude
  Haiku 4.5's ECE 0.097**. Worse accuracy *and* worse calibration on that task.
- **Overconfident out of scope.** A cake recipe came back at 0.94 confidence
  for "technical issue"; random letters at 0.97. It answers the question you
  asked about whatever you gave it.
- **Wording is the dominant risk.** With deliberately incorrect criteria
  descriptions accuracy collapsed to **16.7%, below the 25% random baseline**.
  Coverage summaries put it plainly: accuracy swings with how you ask.
- Per-type calibration differs: `noul` was reported roughly **twice as well
  calibrated as `choice` confidence** on numeric tasks.
- Latency floor ~430 ms, throughput saturating near 11 req/s at 8 concurrent.

**The honest reading**: Jev is a high-confidence triage device, not a drop-in
replacement judge. Used as "decide the confident fraction, escalate the rest"
it is excellent and the escalation rate is a number we can publish. Used as
"grade everything" it is a 60-95% accurate classifier whose calibration is
task-dependent, and on at least one public task a Haiku-class LLM judge beat
it on both axes.

## The one thing it cannot do

**Quoted evidence.** Half our judge contract is a verbatim quote from the
transcript backing the label, and Jev gives up string generation, so it cannot
produce one. Ever.

The workaround is better than the thing it replaces, which is why it is worth
recording now: **evidence by selection rather than by generation.** Extract
candidate spans deterministically from the transcript windows we already parse,
present them as a `choice` with up to 255 options, and let Jev pick which span
supports the label. A selected span is an index into text we already have, so
it is verbatim *by construction* — it cannot be paraphrased, truncated
misleadingly or fabricated, which is exactly the failure a generated quote has.
The enumeration is deterministic proctor code; the model only points.

That is a genuinely stronger evidence guarantee than any generative judge can
offer, and it is the most promising thing in this pass.

## Running it ourselves, and who hosts it

**Cloudflare: already done, as a partner model.** Jev is in Cloudflare's AI
catalogue as `typesafe/jev`, callable from a Worker as
`env.AI.run('typesafe/jev', { state, questions })` with no separate TypeSafe
key, account id or base URL. It is billed as a third-party model against AI
Gateway prepaid credits — without a balance the binding fails with
"Insufficient AI Gateway credits". So this is the same mechanism our existing
off-family judge already uses, and the answer to "will Cloudflare pick it up"
is that they picked it up within three days, as did Vercel, LangChain and
Langfuse. The weights are not Cloudflare's; this is a proxy with a nice
binding, and if TypeSafe changes or dies the binding goes with it.

**Locally: yes, through the clones, today.** Six open clones appeared within 48
hours and the count has grown since. The two that matter for us:

| project | what it is | why it matters |
|---|---|---|
| `GitHub30/OpenJev` | **MIT.** Not a model — a framework that turns any HF instruct model (Qwen, Llama, Gemma, SmolLM) into a System One decider by scoring closed candidate sets in one forward pass. FastAPI server exposing `POST /v1/systemone`, wire-compatible with TypeSafe's OpenAPI spec so the official `typesafe-sdk` works unchanged. Ships temperature scaling, LoRA fine-tuning against proper scoring rules, and accuracy/NLL/ECE metrics. | This is the escape hatch. It runs on the Strix box against a model we already have loaded, at zero marginal cost, and it is the same wire protocol as the hosted one — so hosted and local are an endpoint swap, not a port. |
| `jev-local` | open-weight `/v1/systemone` server, described as a verified official-SDK drop-in | Second implementation of the same seam. |

Others seen, unverified: `Laya` (ModernBERT-large, 421M, PPO, ~35 ms), `von`
(395M, <15 ms), `NanoJev` (0.6B), `Kev-0.5B` (MacBook-class), `Luce` (on
Qwen3-4B), `Jevlike` (a ~40KB embedding-only thing), `Bespoke Nimble` (LoRA on
Qwen3.5-9B, ~100 ms on an H100, 66%→90% after a data-curation pass). None of
these performance claims has been independently checked, including by the
outlets reporting them.

**The strategic point**: the interesting artefact is not the model, it is the
`/v1/systemone` wire format, which is open, already has multiple independent
implementations, and is trivially small. Coupling to *that* is cheap in a way
coupling to TypeSafe is not — a lesson we just finished writing down about nb.

## Prior art we would not be inventing

`alexhawat/judge-jev` already builds the thing we are describing: a judge kit
for assistant replies and agent trajectories, fanning every rubric question out
in **one** `system_one` call, with a screen → profile → locate → score → route
funnel and declarative routing rules in a rubric YAML. Confidence floors per
stakes level automatically downgrade borderline passes and fails to "review",
and a missing answer escalates rather than being skipped.

Its exit codes are **0 pass, 1 fail, 2 review, 3 escalate, 4 skip**. Ours are
0 pass, 1 fail, 2 needs-judge, anything else an error. The first three agree
exactly; 3 and 4 would land as `error` under our contract, which is arguably
the right outcome anyway. **A Jev judge could enter proctor as an ordinary
`script` check with a shim of a few lines** — no new check vocabulary, no
grader changes, nothing in `Stats.cs` or the renderers. That is the cheapest
possible first experiment and it is available now.

## What this pass did not answer

Left to the research line: agreement with our own human labels on our own
transcripts; whether the calibration survives our `state` being a whole agent
trajectory rather than a support ticket; the 32K context limit against real
transcript sizes; what the local clones cost us in accuracy versus hosted Jev;
and whether the evidence-by-selection idea actually works.

## Sources

Vendor: [Introducing System One Models & Jev](https://typesafe.ai/blog/introducing-system-one-models-and-jev),
[Jev on Cloudflare AI docs](https://developers.cloudflare.com/ai/models/typesafe/jev/),
[Cloudflare model catalogue](https://developers.cloudflare.com/ai/models/).
Independent evaluation: [priorbench/jev](https://github.com/priorbench/jev),
[scienthoon/jev-ood-calibration](https://github.com/scienthoon/jev-ood-calibration),
[SamuelSacco/jev-exploration](https://github.com/SamuelSacco/jev-exploration/issues/10),
[14-TR/jev-empirical](https://github.com/14-TR/jev-empirical).
Ecosystem: [yibie/awesome-jev](https://github.com/yibie/awesome-jev),
[GitHub30/OpenJev](https://github.com/GitHub30/OpenJev),
[capitaharlock/jev-clone](https://github.com/capitaharlock/jev-clone),
[alexhawat/judge-jev](https://github.com/alexhawat/judge-jev),
[six clones in two days](https://www.explainx.ai/blog/six-jev-clones-two-days-2026).
Coverage: [LangChain](https://www.langchain.com/blog/building-a-harness-with-jev),
[DataCamp](https://www.datacamp.com/blog/system-one-models-jev),
[Forbes](https://www.forbes.com/sites/josipamajic/2026/09/19/jev-cuts-ai-decision-costs-100x-and-vercel-cloudflare-rushed-to-add-it/),
[XenoSpectrum](https://xenospectrum.com/en/jev-typesafe-bert-classifier-decomposition/).
