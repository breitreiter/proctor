---
source: https://meridianlabs-ai.github.io/inspect_petri/ (home, concepts, results, running, dimensions, Python reference)
read: 2026-11-13, docs only, through a subagent's fetch with short quotes; seeds, skills, auditor tools, Petri Bloom and the per-dimension rubric files not read
take: an inspiration, not something to reproduce uncritically; lessons both ways
---

# Inspect Petri

Meridian Labs and the UK AISI Red Team, built on Inspect AI, after
Anthropic's Petri. "An auditing agent that enables automated monitoring
and interaction with language models to detect potential alignment
issues, reward hacking, and other concerning behaviors."

## What it is

Red-teaming, not reading runs that already happened. Three model roles:
an **auditor** that stages the conversation, fabricates tool results ("no
real code runs") and can roll back; a **target** under test; and a
**judge** that scores the finished transcript. Logs are ordinary Inspect
`.eval` logs and hold the transcript, the auditor's calls, timeline
branches from rollbacks, and the scores.

Scoring: one judge pass over the whole transcript, 1–10 on each of 38
built-in dimensions, mostly higher-is-worse. A dimension is a markdown
rubric with "What IS / What is NOT this behavior" sections and examples
anchoring each tier. Output per sample: a summary, a justification for
each dimension, and highlights, all citing message ids like `[M12]`. The
judge sees ids in the flattened timeline, with branch points marked.

Dimensions near our ground: `stuck_in_loops`, `broken_tool_use`,
`input_hallucination`, `unfaithful_thinking`, `auditor_failure`,
`scenario_realism`.

Viewer (`inspect view`): a sample list that can be sorted and filtered by
any dimension, with a heatmap; a sample pane with transcript, scores and
metadata side by side; a target view and an auditor view; and branches
shown as the auditor tried, abandoned and retried them. No view that
compares runs or diffs them was described.

Across runs: `--epochs` reduced by mean (or median, mode, max), with
`mean()` and `stderr()` as metrics.

## Worth taking

- **Ids in what the model reads.** Every message carries `[M3]` in the
  judge's input, so a claim can point back. The same as our step ids, and
  the precondition for any citation check.
- **Gate on the run's validity before comparing.** They tell the reader
  to check `auditor_failure`, `stuck_in_loops` and `scenario_realism`
  before reading target scores. This is our invalid-run mark
  (`../regimes.md`), arrived at independently.
- **Deltas, not absolutes; the rubric frozen for a sweep.** "Focus on
  dimension deltas rather than absolute scores." Changing the dimensions
  mid-sweep "invalidates comparison." The same reasoning applies to kept
  queries: a query changed partway through a series is a new series.
- **Rescore old logs with another judge.** Same transcripts, different
  reader. It makes judge variance something that can be measured, even
  though they don't report measuring it.
- **Validating labels against human labels** (balanced accuracy,
  precision, recall, F1 via Inspect Scout). If auspex ever keeps a model
  label, this is how it earns a place.
- **The rubric shape.** A rubric with "is / is not" sections and anchored
  tiers is a fair way to write down a ledger line's meaning, even when a
  rule assigns it rather than a model.

## Not to take

- **A judge's 1–10 as the measurement.** That is a count from a model.
  Averaging across epochs makes it smoother, not rerunnable.
- **Citations that nothing checks.** The docs say each claim is "tied to
  a concrete message" and tell the reader to "verify manually." No check
  that a cited id exists or says what is claimed was found.
- **A mean over epochs hides the spread.** Mean and stderr are the
  defaults; where repeated runs diverge, which is the interesting part,
  doesn't surface.
- **Simulated environments.** Fabricated tool results make sense for
  auditing a model. They tell us nothing about friction with real tools.

## For surprise (`../surprise.md`)

Rollback is the elicited-prediction setup turned around: the auditor
reruns from a checkpoint with a different input. Replaying a prefix to a
fresh instance needs the same kind of state handling.
