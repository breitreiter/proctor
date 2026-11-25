---
source: https://arxiv.org/abs/2602.07150 ("On Randomness in Agentic Evals", v3 2026-03-25)
read: 2026-11-11, HTML in full
take: complex runs start the same and diverge quickly; open whether identical final edits are a fixed point that different paths reach, or a coincidence of a few trajectories
---

# On randomness in agentic evals

Bjarni Haukur Bjarnason, André Silva, Martin Monperrus (KTH).

## What it measures

SWE-Bench-Verified (500 tasks); Qwen3-32B, DeepSWE-preview, Devstral-2; two
scaffolds (their nano-agent, R2E-Gym); suggested temperature and 0; 10 runs
per configuration, about 60,000 trajectories.

- **Single-run pass@1 spreads 2.2 to 6.0 points** across ten runs of one
  configuration. Temperature 0 does not remove it ("this variance persists
  even with theoretically deterministic sampling"), put down to the
  inference engine.
- **Divergence is early.** Defined as the first token at which two runs
  differ. Median at token 5 to 56; "the first divergence always happen in
  the 1% of the total trajectory." Temperature 0 delays it, never prevents
  it. Case study: two temperature-0 runs identical for 93 tokens, then
  "search for the Paginator class" against "check the Django source code";
  one ends in a broken patch, one passes.
- **pass@1 to pass@5 gains up to 24.9 points**; pass^5 (all five pass) far
  below pass@1.
- **Power:** at σ = 1.5 points, a 2-point gain needs about 9 runs per agent,
  a 1-point gain 36.

## On the fixed-point question

**The paper does not compare final patches at all.** It makes no claim that
runs converge to identical edits, within one model or across agents;
divergence is measured only as a token prefix, with no alignment after it
and no semantic measure. "Compounding" divergence is argued, not measured.
So "several runs end with identical edits" is not from here: it came from
another paper or from our own runs. Worth tracing.

What it leaves open is the interesting part:

- If runs diverge by token ~10 and still end on the same diff, the diff is
  a basin: many paths, one end. Then **end state is the stable unit to
  compare runs by, and the path is variation around it**, which agrees with
  the proctor finding that `pass` collapses toward the final state.
- If identical diffs appear only on small, over-determined tasks (one line,
  one obvious place), it is a property of the task, not of agents.
  Proctor's small fixtures sit at the ceiling for the same reason.
- Testable at $0 on runs proctor already has: per task, group final diffs
  by exact match (whitespace normalised), within an arm and across arms;
  alongside, the first step at which two runs' event tables differ in tool
  or target. A basin if many distinct paths share a diff; a coincidence if
  identical diffs come with near-identical paths.

## Worth taking

- **The divergence point as a measure** carries over to the event table:
  the first step where two runs of a task differ in tool and target is
  cheap and deterministic, a coarser cousin of their token index.
- **pass^k beside pass@k** for "right every time"; proctor reports neither.
- **Their power figures** agree with proctor's position that samples buy
  power on a fixed suite and tasks buy generalisation.

## Limits

One benchmark, short tasks, no compaction, tool calls capped, token
divergence taken as a stand-in for behavioural divergence.
