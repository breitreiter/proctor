---
source: our own measurement, 2026-11-22 and 23 (`friction-contract.md`, "Off-family readers"): Haiku against three cheap off-family readers on 21 labelled public runs, and Haiku against GLM 5.2 on 9 unlabelled Qwen runs
read: the whole run; scores from `project/trials/friction_score.py`, the comparison by hand
take: keep Haiku as the reader for now (the user, 2026-11-23). Friction reading may be less like adversarial judging than we assumed, so the off-family rule that holds for judges may not carry over to it unchanged
---

# An off-family friction reader

## What we expected

Proctor's judges are meant to be off-family from the agents they grade:
a model grading its own family's output is the textbook case for
self-preference, and the evals literature on LLM judges treats it as a
known bias. The friction reader is Haiku, and the agents it reads are
often Claude Code on Haiku or Sonnet. So we expected a same-family
reader to go easy on Claude's runs, and an off-family one to be needed.

## What we found

- **No leniency.** Reading the same `agent-onboarding` runs, Haiku and
  GLM 5.2 agree closely on Qwen's (4.0 and 3.9 steps flagged a run,
  about three quarters in common) and on Claude's Haiku flags *more*
  (2.5 against 1.9). Any family effect made Haiku stricter on its own,
  not softer.
- **The agent difference survives a change of reader.** Both see about
  twice the friction in Qwen's runs as in Claude's.
- **What separated readers was capability, not family.** On short
  agentic runs a cheap off-family model (GLM 5.3 Flash) matched Haiku.
  On long coding runs (`nb-changes`) every off-family reader tried
  mostly answered nothing, ran out of tokens or looped, where Haiku
  found friction both label sets agree with. (Those labels were made by
  Haiku and Sonnet, so they are no referee on the family question.)
- **Haiku is not consistent with itself either.** Two subagent runs
  disagreed on whether a read that returned nothing is `lost`.

Small: 15 and 7 runs on one exam for the family question, six runs for
the coding one, and no labels on the Qwen runs.

## Why the task may differ from judging

A judge is asked for a verdict on quality or correctness, where taste
and a model's own habits can leak in: it prefers answers that read like
its own. Friction reading is closer to annotation of observable events,
and the reader is fenced in:

- **Every point must quote the call.** A quote not found in the call's
  command or result is dropped, so the reader can only point at things
  that happened.
- **The vocabulary is about events,** not quality: a read that led
  nowhere, a fix to the agent's own earlier work, an instruction that
  was wrong. "Was this good?" isn't asked.
- **The count is of steps,** not a score, and the rules (no model at
  all) carry much of it.

If that's right, the reader's bias risk is mostly in what it bothers to
flag (recall), not in which side it takes, and recall is something a
labelled set measures directly.

## What follows

- **Keep Haiku** as the reader for now, with the measured caveat stated
  where its numbers are shown, instead of an assumed one.
- **Keep the off-family rule for judges.** Nothing here says it's wrong
  for verdicts; it says the reader isn't obviously a judge.
- **Measure the reader's family effect, don't assume it,** whenever the
  agents compared span families: read the same runs with a second
  reader and compare amounts and overlap, as here.
- **What would change this:** labels for the Qwen runs (the comparison
  has no ground truth), a second exam, or a prompt that stops off-family
  readers answering nothing on long coding runs.
