---
date: 2026-11-12, two tests added 2026-11-13
status: a research direction; two tests run at $0 (below), nothing built. Adjacent to the friction work in `regimes.md` (the quiet regime and its ledger)
---

# Surprise: how unexpected was that result?

The question asked at one step: the agent called a tool and got this
result, how much did that surprise it? It searched for x and got y
results, how surprising was that?

Surprise is the moment and friction is the cost. They overlap without
nesting: some surprises resolve in one step, and some friction has no
surprise in it at all, such as a slow grind down a path known to be
tedious. A surprise column could explain where a friction line starts.

## Measure the symptoms, not the state

The user's framing (2026-11-13). Surprise is the root: the agent did X,
expected Y and got Z. There may be no pure way to measure it, or only an
absurdly expensive one, so the practical vehicle is what the agent does
next. An on-ramp's safety is measured by counting accidents, not by
measuring the driver's affect and cognitive load while merging.

The symptoms to look for after a result:

- **Corrective behaviour.** An edit to what was just written, or a turn to
  research (docs, `--help`, a search).
- **An unexpected amount of thinking.** A step whose thinking tokens jump
  against the run's own baseline.
- **Hacks and work-arounds.** Building a way round something instead of
  using it.

On this framing approaches 3 and 4 below are the instrument and 1, 2 and 5
are attempts at the state itself, worth their cost only where they find
what the symptoms miss.

## First test: is a result's oddity a usable sign? (2026-11-13)

`trials/surprise.py`, on the 15 Sonnet `agent-onboarding` runs, at $0. The
question was whether friction starts right after a result that is rare
among sibling runs. It does not.

By step, since the reaction to a result is the next agent step. A step's
result is classed by rule from its error lines; rare means the normalised
line is in at most 3 of the 15 runs. The reaction is the readers' friction
labels (`trials/ledger/labels`) on the next step.

| Result of a step | Steps | Next step is friction (Haiku labels) | (Sonnet labels) |
|---|---|---|---|
| Rare error | 21 | 20 (95%) | 20 (95%) |
| Common error | 43 | 13 (30%) | 17 (40%) |
| Clean | 167 | 93 (56%) | 95 (57%) |
| All | 231 | 126 (55%) | 132 (57%) |

| A friction start follows | Haiku labels | Sonnet labels |
|---|---|---|
| A rare error | 4 | 3 |
| A common error | 6 | 10 |
| A clean result | 39 | 34 |
| Starts | 49 | 47 |

- **Rare errors mark friction continuing, not beginning.** 20 of 21 are
  followed by friction, and only 3 or 4 of those are starts. The rest are
  compile failures and missing files inside a repair already under way.
- **Most starts follow a clean result.** The agent reads a doc that
  returns without error, does not find what it needs, and goes digging.
  That is still a surprise (it expected the answer to be there), but
  nothing in the result's text is odd, so any measure of the result alone
  misses it. The expectation lives in the agent, and only the next action
  shows it was not met.
- **Common errors are stepped over.** They are followed by friction less
  often than a clean result is. `file: command not found` (12 runs), the
  shakedown outrunning the shell timeout (9), `ls` on a missing `suites`
  (6). Cheap fixes to the surface, and the list is a by-product worth
  keeping.
- **"Odd result, no reaction" was one step**, and harmless. The rule sees
  only errors, and the cases worth finding there are not errors (an empty
  result accepted, a vacuous pass), so the cell is unmeasured, not empty.

Cautions. The readers saw each result when they labelled, and "repair"
means fixing after a failure, so the 95% is partly the label's
definition. 21 steps, one suite. The error rule needed two fixes before
the numbers settled: text read from docs that mentions errors was counted
as an error, and a task id made one common message look unique to each
run.

What it says about the approaches: a result's rarity among siblings, and
probably its surprisal under a surrogate (5), measure how odd the result
is, which is not the same as whether the agent's expectation was met.
The symptoms are the better first instrument. Elicited prediction (1) is
the one direct approach that could see the clean-result starts, since it
asks what the agent expected to find.

## Second test: do the symptoms find friction without a reader? (2026-11-13)

`trials/symptoms.py`, same 15 runs. A step is flagged by rule from its own
commands and metrics, then scored against the readers' friction labels on
that step. 246 steps; the readers call 126 (Haiku labels) and 132 (Sonnet
labels) of them friction, with 49 and 47 starts.

| Symptom | Steps flagged | Flag is friction (Haiku, Sonnet labels) | Friction steps found | Starts found |
|---|---|---|---|---|
| Search (recursive grep, `find -name`) | 62 | 97%, 87% | 48%, 41% | 33%, 23% |
| Locating the install | 61 | 62%, 72% | 30%, 33% | 45%, 47% |
| `--help` | 18 | 100%, 100% | 14%, 14% | 14%, 6% |
| Reading the tool's source | 22 | 100%, 100% | 17%, 17% | 8%, 9% |
| Editing a file the run already wrote | 21 | 71%, 90% | 12%, 14% | 12%, 13% |
| Inline script or scratch file | 10 | 100%, 100% | 8%, 8% | 8%, 6% |
| Thinking tokens jump | 40 | 75%, 72% | 24%, 22% | 14%, 13% |
| Any symptom | 145 | 73%, 77% | 84%, 84% | 73%, 72% |
| The other reader, for scale | 126, 132 | 90%, 94% | 94%, 90% | 98%, 83% |

- **The symptoms find most of it.** Together they catch 84% of friction
  steps and about 73% of starts, with three flags in four right. A second
  reader does about 15 points better on each.
- **Research and work-arounds are near-certain signs** where they fire:
  `--help`, reading source and inline scripts were friction every time.
  Each is rare, so the coverage comes from the union.
- **Thinking is the weakest symptom alone.** Its misses are the plan
  before the first big write, which is the work. But the median step that
  is not friction has 0 thinking tokens, against about 60 at a start and
  100 to 125 inside friction, so "any thinking at all" may be the better
  cut. Untried.
- **14 of the 39 wrong flags are one command**: `type proctor` in each
  run's first look round, which the readers file under checking the setup.
  Dropping it would be tuning to these labels, so it is left in.
- **The 12 starts both readers see and no rule flags** are mostly a first
  read of the example suite at step 8, and `curl` probes of the local
  model. The first is a reader's judgement about a compound call; the
  second is a rule not yet written.

The rules are specific to this surface (which paths are source, which
command is the tool). The symptom kinds should carry to another suite; the
patterns will not.

## Cross-check on other suites: the rules do not carry as they are (2026-11-14)

`trials/symptoms_x.py`. No reader has labelled any run outside
`agent-onboarding`, so nothing here is scored. The five symptoms that need
no knowledge of the surface (search, help, rework, script, thinking) were
run on `code-change`, `nb-changes`, the Qwen `agent-onboarding` arm and one
Terminal-Bench 2.0 job (Claude Code with GLM-4.7, 440 runs).

- **The same action has a different standing on a different task.** On
  `code-change` the Claude Code and Codex runs are 3 or 4 steps, all pass,
  and a third of their steps are flagged, every one as search: finding the
  code to change is the work there. On Terminal-Bench 12% of steps are
  inline scripts and 17% rework, on tasks where writing and rewriting a
  program is the task.
- **Flagged share does not separate failures from passes.** On the 27
  Terminal-Bench tasks with both, failed runs have the higher share in 15
  and the lower in 10, median gap 1 point. Friction is not failure, so
  this rules little out, but it is no support either.
- **Where it fits, it fits.** Qwen on `agent-onboarding`, which passes
  nothing, has rework on 29% of steps against Sonnet's 9%.
- **Thinking tokens** are recorded only for the Claude arms of our own
  runs; not for Qwen, Codex or the Terminal-Bench job.

So a symptom rule needs the suite to say what the work is (searching the
repository is expected here, searching the tool's install is not), which
is the user's earlier point about a ledger line needing its context. And
the cross-check proper needs labels on a second suite.

## Approaches, from a discussion with web Claude

In rough order of directness:

1. **Elicited prediction, replayed offline.** Hand the prefix up to a
   tool call to a fresh instance and ask what it expects back: success or
   failure, shape, key content. Score the prediction against the actual
   result. The closest thing to a true surprise measure, and the original
   run is untouched.
2. **Resampling the next action.** From the same prefix, sample the next
   step N times and measure how spread out the actions are, grouped by
   meaning rather than string ("semantic entropy"). Strictly this is
   uncertainty, not surprise; the signal is the jump in spread right after
   a tool result. Where runs of one task diverge across samples is a
   coarse version of the same thing, already in the data.
3. **Markers in the reasoning text.** Thinking often narrates a violated
   expectation: "that's odd", "wait", "actually". A cheap classifier gives
   step ids. Thinking is often summarised rather than raw, and it is not a
   faithful record.
4. **Behaviour afterwards.** An orientation response: a sudden turn from
   acting to exploring (`--help`, `ls`, rereading docs), restating the
   plan, or backtracking right after a result. Readable from the event
   table with sequence operators.
5. **Surprisal under a surrogate model.** Run an open-weights model over
   the trace and take the perplexity of each tool result given its
   context. It is the wrong model's surprise, but a weird error message is
   probably weird to most models, and it is the only approach that gives a
   real number per token.

## Is an open model a good enough proxy?

Possibly its own project. A local Qwen or GLM on `imp` may not be a
strong proxy for Opus. The comparison to run is surprisal over the same
traces from big open models (K2, full-size GLM, through OpenRouter)
against the local ones. The hope is that an open model is close enough to
catch most of the weird behaviour, and the question is how many surprising
steps the local model misses that the big one finds.

Surprisal needs log-probabilities of the tool result as *input* tokens
(prompt logprobs, or `echo` with `logprobs`). What was found on
2026-11-12, from docs and OpenRouter's public endpoint list, with
nothing tested:

- **OpenRouter: no.** Its parameters page lists only output-side
  `logprobs` and `top_logprobs`. No endpoint in `/models/<id>/endpoints`
  advertises `echo` or `prompt_logprobs`. OpenRouter forwards a few named
  provider-specific parameters and says nothing about others, so whether
  `echo` reaches the provider is unknown.
- **Together, direct: yes, documented.** On `/completions`: "`echo`: If
  true, the response contains the prompt. Can be used with `logprobs` to
  return prompt logprobs."
- **Fireworks, direct: yes, documented.** `echo_last`: "Echo back the
  last N tokens of the prompt ... useful for obtaining logprobs of the
  prompt suffix." This fits the use case: send the prefix plus the tool
  result and echo only the result.
- **DeepInfra:** not read.
- Both GLM-5.3 and Together/Fireworks are in OpenRouter's endpoint list.
  K2.6 is not on either there, and their direct catalogs were not
  checked.

The direct route means a raw-text `/completions` call, with the trace
rendered through the model's chat template locally. That needs a
tokenizer, not torch. Local serving is also unverified: vLLM documents
`prompt_logprobs`, and whether imp's llama.cpp server returns per-token
prompt logprobs has to be checked.

## Against the rules

- **No count from a model.** 1 and 3 put a model's judgement in the
  number. 1 needs its scoring pinned to something rerunnable (a fixed
  rubric of yes-or-no fields, say), and 3's classifier output is a label
  to check, not a measure. 4 and 5 are queries and fit as they stand.
- **Plumbing costs $0.** 5 on `imp` is free. 5 on a hosted big model (Together or Fireworks direct, since
  OpenRouter doesn't expose prompt logprobs) is hosted spend, allowed because the proxy question is about those models, but
  agreed first. Build and test the pipeline on `imp`. 1 and 2 against the original
  agent's model are hosted spend and need agreeing first; against a local
  model they are surrogate measures like 5.
- **Embeddings do not group anything.** 2's grouping by meaning cannot use
  embedding clusters; it needs normalised actions (the normalisers in
  `direction.md`) or exact matching on them.

## Open

- How many of the copied runs carry raw thinking, and in what form.
- Whether 4 and 5 agree on which steps are surprising. Less pressing
  after the first test, which suggests 5 would miss most starts.
- Thinking tokens: recorded per step in these runs
  (`metrics.extra.output_tokens_details.thinking_tokens`). Whether any
  thinking at all beats a jump against the run's median.
- Telling a work-around from the work by rule. The ledger's `scaffold`
  line is a reader's call today.
- Whether the symptom rules can be declared per suite (the tool's name,
  its source paths, its docs) rather than written per suite.
