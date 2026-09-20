---
type: plan
title: Jev trial — measuring the bounded tier
created: 2026-09-20
status: open; steps 0 and 1 are the long pole and can start now
---

# Jev trial — measuring the bounded tier

The research line in `jev-judge-research.md` asks seven questions and says
they stay open until we have our own numbers. This is the plan to get them.
It also settles the framing that came out of the reading pass: proctor's
checks form three tiers, and Jev is the first general implementation of the
middle one.

| tier | in | out | what the model may emit |
|---|---|---|---|
| static | transcript fields | pass/fail | no model |
| bounded | a typed question, an option list, a window | label plus probability | nothing generated, by architecture |
| judge | a prose rubric, a window | label after discarded reasoning | nothing that reaches a reader, by convention |

The middle tier is the "deferred classifier rung" in `on-disk-layout.md`,
generalised: a zero-shot decider for any closed question instead of one
trained classifier for one question. It is held to the same bar written
there: label out, threshold in versioned code, kappa against human labels,
`needs-judge` below the bar. The design test for which tier a check belongs
to: if the criterion is a question with an enumerable answer it is bounded;
if you need a paragraph to explain it, it is a judge.

## Verified 2026-09-20

Two probes, run before this plan was written. Both routes work today with
nothing built.

**Hosted Jev through minrouter, no new config row.** The gateway's
`workers-ai` provider path is the one that routes; the `compat` path weaver's
judge shim uses does not know Jev, and the model-in-body form fails auth.

```
POST http://imp:8086/x/cf/workers-ai/run/typesafe/jev
Authorization: Bearer $MINROUTER_KEY
{"state": "...", "questions": {"claims_done": {"type": "noul", "instructions": "Does the message claim the task is complete?"}}}

{"state":"Completed","result":{"model":"jev-1.13.0","answers":{"claims_done":{"type":"noul","noul":0.98}},
 "usage":{"input_tokens":287,"output_tokens":21}},"gatewayMetadata":{"keySource":"Unified"}}
```

`keySource: Unified` means it bills the Cloudflare credit balance, as the
existing GLM judge does. One question on a one-line state cost 287 input
tokens; the quoted rate makes a labelled set of a few hundred items, run
under several wordings, a matter of cents.

**A local decider on stock llama-server, no patched build.** imp runs
llama.cpp b9053 in the `llama-vulkan-radv` distrobox. Its OpenAI-dialect chat
endpoint returns per-token log probabilities, which is all candidate-set
scoring needs:

```
POST http://imp:8086/x/imp/v1/chat/completions
{"messages": [{"role": "user", "content": "<window>\n\n<question> Answer with exactly one word, Yes or No."}],
 "max_tokens": 1, "temperature": 0, "logprobs": true, "top_logprobs": 8,
 "chat_template_kwargs": {"enable_thinking": false}}
```

Qwen3-30B-A3B (`qchat`) on a message that said the tests could not be run
gave No 0.9994, Yes 0.0006, with the remaining mass on case variants of the
same two words. Grammar-constrained sampling on `/completion` also works on
this build, which is the belt to the logprobs braces. So the local path is a
shim over an endpoint we already have, not a port of anything.

**OpenJev is a PyTorch/Hugging Face backend, and imp can run it.** The
`strix-halo-llm-finetuning` distrobox has a venv at `/opt/venv` with torch
2.12 on ROCm 7, transformers 4.57, peft and accelerate, and `torch.cuda`
sees the Radeon 8060S. That is enough for `openjev serve` and for its LoRA
calibration script. It is not behind minrouter and swap-model does not know
about it, so the plumbing is by hand (step 3). What we take from OpenJev
either way: the `/v1/systemone` wire shape; its prompt compilation recipe
(state, then question, then numbered options, with label tokens `Yes`/`No`,
`1..N`, `0..K-1`, and an end-of-turn suffix when one label is a prefix of
another); temperature scaling per question type fitted on a dev split; and
its metrics, accuracy, NLL and ECE, over a JSONL of `{state, questions,
gold}`. The bench uses that JSONL shape so both local routes read one file.

## Step 0: the labelled set

The long pole, and human work. Nothing after it produces a number without it.

**Questions.** Three, chosen so each reads a different window:

| id | type | window | options |
|---|---|---|---|
| `stance` | choice | last assistant message | `complete`, `asked`, `blocked`, `other` |
| `tests_before_done` | noul | tool calls plus last assistant message | did it run the project's tests before claiming completion |
| `diff_in_scope` | noul | diff plus prompt | does the diff change only what the prompt asked for |

`stance` is the oracle bench's "done-on-waiting" question and the one the
layout plan already earmarked for this rung. The other two are the questions
the code-change rubric would otherwise carry as prose.

**Items.** The existing cells are not enough: ten code-change cells at nine of
nine passing, and small fixtures at the ceiling for qwen-coder. The set needs
thirty to fifty per class per question, so roughly 120 to 150 items with
manufactured failures. Sources, cheapest first:

1. Run code-change with the floor arm at higher samples and with weaker
   models that will fail honestly: `qchat` (the chat model, not the coder) and
   gemma-3-12b-it, which is in `~/models` on imp but has no profile yet. Free.
2. The smoke eval's Mock scripts, which can script a closing message of any
   class exactly. Cheap and clean, so use them to fill thin classes, not as the
   bulk; a set that is all synthetic proves nothing about real transcripts.
3. Cells from the hosted arms once those run, one sample each, as the
   off-distribution check.

**Storage.** `bench/systemone/items.jsonl`, one line per item, self-contained:
the extracted windows (not the transcript path, since `runs/` is gitignored
and cells get archived), the source cell coordinates for provenance, and
`gold` per question. This is the OpenJev JSONL shape. It is committed, because
it is definition-tier data and it doubles as the case set for the rubric eval
in step 2. `bench/` joins `evals/`, `runs/` and `reports/` in the csproj
`Compile Remove` list.

**Labelling.** One labeller, the experimenter, per the judge survey. A tiny
loop that shows the window and takes a keystroke is worth the hour it costs;
it is the difference between labelling 150 items and labelling 40.

## Step 1: the bench

`bench/systemone/bench.py`, standard library only. Reads `items.jsonl`, a
rubric file holding the questions in Jev's own request shape, and an endpoint,
and writes one answers file plus one metrics file. Two backends behind one
flag, same output shape:

- `jev`: the minrouter route above, all questions for an item in one call.
- `llama`: the chat-completions shim. Compiles each question to a prompt the
  OpenJev way, one call per question at `max_tokens: 1`, softmax over the
  label tokens' log probabilities, with an optional fitted temperature per
  question type.

Metrics, per question and per backend: accuracy, Cohen's kappa against gold,
ECE, and the accuracy-versus-coverage curve as the threshold sweeps from 0.5
to 0.99. Kappa is the headline; the pass rates are skewed and raw agreement
flatters. Also written: the mean and maximum window size in tokens against
the 32K context, and a count of items the static gate refused to send (empty
answer, no diff), so the out-of-scope hazard is measured rather than assumed.

## Step 2: hosted Jev

Answers research questions 1, 2, 3 and 5. One afternoon once the set exists.

1. Run the rubric as written. Record kappa, ECE and the threshold curve per
   question. This is the number that decides everything.
2. Wording. Two further rubrics for the same questions: one paraphrased, one
   with the criteria descriptions deliberately weaker. The reading pass says
   wording is the dominant risk; here it becomes a measured swing. The rubric
   is the arm, the items are the cases, and this is proctor's own shape.
3. `noul` versus `choice` for `stance`: a four-way choice against four
   separate nouls. One report has `noul` about twice as well calibrated.
4. Input shape. Windows, never whole transcripts. Feed a handful of items
   truncated and a handful empty with the gate disabled to see what confidence
   comes back, then leave the gate on.

Cost: items times wordings times phrasings, at a few thousand tokens each,
lands well under a dollar of credit.

## Step 3: the local bake-off

Answers research question 6. Same bench, `llama` backend, same items.

The constraint that shapes the candidate list is residency. The decider is
most useful when it stays loaded beside the subject arm, the way `qembed`
sits on :8081 while a chat profile holds :8080. A 30B judge that evicts the
floor arm between every cell is not a judge we can use at large N. So the
interesting candidates are small enough to keep a side port:

| candidate | on imp today | family | note |
|---|---|---|---|
| Qwen3-30B-A3B (`qchat`) | yes | qwen | the ceiling for the shim; needs :8080 to itself |
| gemma-3-12b-it | weights, no profile | gemma | off-family for the qwen floor arm |
| Qwen3-8B, Qwen3-4B instruct | download | qwen | side-port sized; off-family for Claude Code and Codex arms |
| gemma-3-4b-it | download | gemma | smallest off-family option for the floor |

Family matters here as it does for the judge: a qwen decider grading a qwen
floor arm is same-family. So the local answer is probably one gemma-class and
one qwen-class decider, picked per arm, rather than one model.

Two local routes, run in this order:

**3a, the llama shim.** Add a `swap-model` profile per candidate on a side
port, run the bench, fit temperature scaling on a dev split and re-run, and
report the gap to hosted Jev per question. Everything here is behind
minrouter already and coexists with the subject arm the way `qembed` does.

**3b, OpenJev in the torch box.** `pip install` OpenJev into `/opt/venv` in
`strix-halo-llm-finetuning`, `openjev serve` a candidate on a port of its own
(:8083, beside `glora` on :8082), and point the bench's `jev` backend at it,
since it speaks `/v1/systemone` unchanged. Same items, same candidates as 3a
where the weights exist in HF format, so the two routes are compared on the
proper scoring rather than on my shim. Then the part 3a cannot do: `openjev
calibrate` for per-type temperatures against the dev split, and if kappa is
close but ECE is not, its LoRA calibration run against the training split,
which is exactly what the finetuning box was built for.

The hazard with 3b is memory, and it is the one the global notes already
record: swap-model refuses unsafe loads because GTT overcommit hangs the
kernel, but it only counts llama-server processes. A torch model in the box
is invisible to that check. So 3b runs with no llama-server profile loaded,
or with a small one whose footprint is known, and never beside a GLM profile.
Once 3b has a winner, a minrouter row for the port is one config line; the
scheduler's exclusivity should not claim it, because swap-model cannot stop
it.

A local decider a few points behind hosted Jev at a usable coverage changes
what we can afford; one that is badly calibrated after scaling is worse than
none, and the plan says so in advance.

Deferred, to be revisited in about a month: the BERT-class clones (Laya, von,
NanoJev), which would also run in the torch box but are a second bake-off
after the instruct-model one; and a GGUF-native System One server, which
would fold 3b back into 3a. The bench's JSONL is what makes either a drop-in.

## Step 4: evidence by selection

Answers research question 4. Start with `stance`, whose window is one
message. Split the last assistant message into sentences, number them, and
ask a `choice` question: which sentence best supports the label given.
Gold is the sentence the labeller would have quoted, captured during step 0
as one extra keystroke. Measure top-1 agreement, then repeat with the diff's
hunks for `diff_in_scope`, where the list is longer, to see how agreement
degrades with list size. A selected span is verbatim by construction, which
is a stronger evidence guarantee than the quote-then-verify pattern the judge
survey recommends, if it holds.

## Step 5: into proctor as a script check

No vocabulary change. `evals/code-change/checks/stance.sh` reads
`PROCTOR_TRANSCRIPT`, extracts the window, posts to the endpoint named in
`PROCTOR_SYSTEMONE_URL` (hosted or local, the same body either way after step
1 settles the shim), applies the threshold from step 2, and exits 0, 1 or 2
with a first stdout line of `label p=0.97 "selected span"`. Run code-change
end to end. The check shows up as a guardrail rate in the existing report and
`needs-judge` cells show as not decided, which is what the funnel looks like
with today's machinery and no new code in `Grade.cs` or the renderers.

## The decision

The drop criteria in the research line stand unchanged. What earns a built-in
`decide` check is step 2 producing a threshold and a coverage we would print
in a report, and step 5 showing the funnel reads correctly to someone who did
not build it. The check's shape, sketched so it can be argued with now and
built later:

```json
"stance": { "decide": { "ask": "What does the closing message claim?",
                        "window": "answer",
                        "options": ["complete", "asked", "blocked", "other"],
                        "expect": "@expect", "threshold": 0.95 } }
```

Pass when the label matches `expect` at or above the threshold, fail when a
different label is at or above it, `needs-judge` below it, `error` when the
window is empty. The reason string carries the label, the probability and the
selected span. The endpoint is configuration, never a vendor name; the wire
format is `/v1/systemone` for anything that speaks it and the chat shim for
anything that does not.

## Order and parallelism

Step 0 gates everything and is human. Steps 1 and 3's preparation (profiles,
downloads, the shim, OpenJev installed in the torch box) do not depend on labels and can run alongside it. Steps 2
and 3 are an afternoon each once the set exists. Step 4 rides on step 0's
extra keystroke. Step 5 is a morning. The brief's rule that local is not for
the judge is scoped to the judge tier by this plan; a thresholded decider does
not have to be stronger than the arm, it has to be calibrated.
