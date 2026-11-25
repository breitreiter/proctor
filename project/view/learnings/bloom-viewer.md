---
source: https://github.com/isha-gpt/bloom-viewer (given as a github.qingwei-s.workers.dev proxy link; read on github.com) and https://github.com/safety-research/bloom (README, step4_judgment.py, prompts/step4_judgment.py, transcript_utils.py)
read: 2026-11-13, through a subagent's fetch; the viewer's README and file tree only (no UI code, no screenshots exist), Bloom's judgment code as raw source
take: an inspiration, not something to reproduce uncritically; lessons both ways
---

# bloom-viewer, and Bloom's judge

**Bloom** generates evaluation suites that probe a model for one
behaviour: understanding, ideation, rollout, then a judgment stage that
"scores behavior presence and additional qualities." The repo is frozen;
it now lives at Meridian Labs as Petri Bloom
(https://meridianlabs-ai.github.io/petri_bloom, not read). See
`inspect-petri.md`.

**bloom-viewer** is a SvelteKit app forked from
`@kaifronsdal/transcript-viewer` and customised for Bloom's output: "a
web-based viewer for AI conversation transcripts with support for
rollbacks, branching conversations, and detailed analysis." 15 commits.

## What it shows

- A tree of suite → config → transcript, with the meta-judge's report
  loaded when a config is expanded.
- Scores (1–10) as translucent colour bands: white, yellow, orange, red.
- **Numbered quote references in the judge's justification that jump to
  the quoted message and highlight it.**
- Filters by score, tag and custom expression, in a virtualised table
  (TanStack). The README claims 20,000+ transcripts across 34 configs,
  with `_index.json` files built per config directory on first start
  (30–60 s).
- Branching and rollbacks.

No comparison or diff view is mentioned.

## How Bloom's judge works (from the code)

- Per transcript: one call for a summary, scores, a justification and 3–5
  highlights, each an exact quote. Then `num_samples` calls that ask for
  scores only (default 1), averaged and rounded to an integer. Then a
  call that writes the final justification against the averaged scores.
  The highlights come from the first call, before the averaging.
- **Quote check:** `if quoted_text in text` over the transcript's
  messages. A miss sets `message_id` to `"unknown"` silently, with no
  error and no count.
- The prompt asks for message ids "shown in square brackets
  [xxxxxxxx] if available". The formatter writes `USER (EVALUATOR): …`
  and `ASSISTANT: …` with no ids. In this reading of the code, the model
  has no ids to cite.
- `elicitation_rate`: the share of transcripts whose judge score is
  above 6, a hardcoded threshold.
- A meta-judge (off by default) reads all the judgments' scores and
  summaries, not the transcripts, and scores the whole suite 1–10.
- The viewer README's schema (`metadata.id`) doesn't match what Bloom
  writes (`transcript_id`).

## Worth taking

- **A checked quote, made clickable.** Numbered references that jump to
  the step and highlight the span. This is the reading side of our
  citation rule and the right way to present it: a claim and its evidence
  one click apart.
- **A substring check is the floor, and they have it.** Ours is the same
  rule with all whitespace removed (proctor's judge).
- **Index files for scale.** A per-directory index built once makes
  20k transcripts browsable. Our datasets are far smaller; the pattern
  holds if they grow.

## Not to take, and the lessons in them

- **A failed check that nobody sees.** `"unknown"` with no count lets an
  invented quote pass as a highlight with no anchor. A citation that
  doesn't resolve must be shown, counted and refused.
- **Asking for ids the model can't see.** The quote can only point back if
  the id is in what the model reads. This is cheap to get wrong and
  invisible unless something checks it, so a test should check it.
- **A threshold on a judge's score as the headline rate.** "Above 6 out of
  10" is a count from a model with a cut chosen by hand.
- **A model reading other models' summaries.** The meta-judge has no
  access to the evidence. Anything at suite level has to come from
  queries.
- **Justification written after the averaging, highlights before.** The
  prose and its evidence come from different calls.
