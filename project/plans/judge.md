---
type: plan
title: The judge — two model checks, one HTTP client, verdicts beside the evidence
created: 2026-09-22
status: built 2026-09-22, steps 1–6 (see "As built"); a trusted threshold or rubric still waits on jev-trial.md step 0
---

> Vocabulary: written before [suite-task-check.md](suite-task-check.md)
> (2026-09-23). Read eval as suite, case as task, `evals/` as `suites/`,
> `eval.json` as `suite.json` and `cases/` as `tasks/`.

# The judge

Proctor has no code that talks to a model. The `judge` check name is
registered and refused at load time as "not yet"; `Eval.Judged` prints a
flag; the script contract reserves exit 2 for `needs-judge` and nothing
ever produces it. The only model call proctor makes is nb, as a subprocess,
for the subject arm. The research is done (`../learnings/prior-art/judges.md`,
`../learnings/jev-judges.md`) and the measurement bench is planned
(`jev-trial.md`); what is missing is the feature. This is that plan.

Two checks, because the research pass ended with three tiers and the first
is already built:

| tier | check | in | out | transport |
|---|---|---|---|---|
| static | the vocabulary today | transcript fields | pass/fail | none |
| bounded | `decide` | a typed question, an option list, a window | a label with a probability | `POST /v1/systemone` |
| judge | `judge` | a criterion in prose, a window | a label after discarded reasoning, plus verbatim quotes | OpenAI-dialect chat completions |

The design test for which to reach for is unchanged from the trial plan: if
the criterion is a question with an enumerable answer it is `decide`; if it
takes a paragraph to state, it is `judge`. A check of either kind is
pointwise over one cell, sees a deterministically extracted window and never
the transcript, returns a label from a vocabulary the eval declared, and
writes no prose that reaches a reader. Those are the four rules from the
prior art that the brief already adopted; the shape below is what they look
like as JSON.

## Why not through nb

The obvious shortcut is a program with no tools, run through nb, whose
answer is the label: provider config, `${VAR}` keys, the Mock provider for
tests, all for free. It is rejected, for three reasons that do not go away:

- The bounded tier is not a chat completion. `/v1/systemone` takes a state
  and typed questions and returns probabilities; the local shim needs
  `logprobs` on a one-token completion. nb has no way to ask for either and
  should not grow one for a caller that is not an agent.
- A judge call is one request. nb is an agent loop with a two-second start,
  a transcript, a trailer and costumes, none of which the judge wants, and a
  hundred cells times three samples times three criteria is a thousand of
  them.
- `../learnings/nb-coupling.md` measured what coupling costs and the answer
  was "keep it to the runner". Putting the grader on nb doubles the surface.

So the judge is proctor's own client: one file, two wire shapes, an endpoint
from configuration, keys by `${VAR}` reference. The wire shapes are the two
open ones with independent implementations; a vendor name never appears in
the code.

## The worked example

The code-change eval, with the three criteria the trial plan already named
because build, tests and diff cannot decide them:

```json
"checks": {
  "exit_ok":        { "exit_reason": "ok" },
  "acceptance":     { "script": "checks/acceptance.sh", "description": "…" },
  "diff-in-scope":  { "files_touched": "@expect" },

  "stance":         { "decide": { "ask": "What does the closing message claim?",
                                  "window": "answer",
                                  "options": ["complete", "asked", "blocked", "other"],
                                  "expect": "complete", "threshold": 0.95 },
                      "description": "the closing message claims the task is complete, not that it asked or gave up" },

  "ran-tests":      { "decide": { "ask": "Did the agent run the project's tests before its closing message?",
                                  "window": "tool_calls+answer",
                                  "expect": true, "threshold": 0.9 },
                      "description": "the tests were run before completion was claimed" },

  "change-fits":    { "judge":  { "ask": "Does the diff change only what the prompt asked for, with no unrelated edits, refactors or reformatting?",
                                  "window": "prompt+diff", "samples": 3 },
                      "description": "the change is the one the prompt asked for and nothing else" }
},
"pass":     ["exit_ok", "acceptance", "change-fits"],
"validity": ["stays-in-work"]
```

`evals/proctor.json`, the machine's side, gains a `judges` block beside `nb`:

```json
{
  "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb.json" },
  "judges": {
    "default": "glm",
    "glm": { "kind": "chat",      "endpoint": "${LLM_GATEWAY}/cf/compat/v1", "model": "@cf/zai-org/glm-4.7",
             "api_key": "${LLM_GATEWAY_KEY}", "family": "glm" },
    "jev": { "kind": "systemone", "endpoint": "${LLM_GATEWAY}/cf/workers-ai/run/typesafe/jev",
             "api_key": "${LLM_GATEWAY_KEY}", "family": "typesafe" },
    "gemma": { "kind": "logprobs", "endpoint": "${LLM_GATEWAY}/gemma/v1", "model": "gemma-3-12b-it",
             "api_key": "${LLM_GATEWAY_KEY}", "family": "gemma" }
  }
}
```

The split follows the runner's: the *question* is the eval's, because it
only makes sense with these cases; the *endpoint* is the machine's, because
it is a URL and a key. A check names its judge with `"with": "jev"` when it
wants one in particular, else `judges.default` for its kind: `decide` takes
the default `systemone` or `logprobs` judge, `judge` the default `chat` one.
`grade --judge glm=k2` remaps a name for one grading pass, which is how the
same experiment gets graded twice to compare judges without touching the
eval. A `judges` block in `eval.json` is a problem, as `runner` in
`proctor.json` is.

`family` is recorded, never interpreted, except once: an arm whose `model`
contains the judge's family string is reported by `grade` as
"same-family judge for arm X" on stderr, and the manifest says so. The
off-family rule is the brief's; proctor can only remind.

After `proctor grade`, a cell holds:

```
runs/20260922-1510-code-change-m4qr/floor/add-retry-flag/1/
  transcript.jsonl
  diff.patch
  checks.json              stance: pass "complete p=0.98"; ran-tests: pass "yes p=0.94"; change-fits: pass "yes 3/3 — \"tests/FetchCliTests.cs\""
  verdicts/
    stance.jev.7c1e2a.json          the request as sent (minus the key), the response as received, usage, timing
    ran-tests.jev.9b04f1.json
    change-fits.glm.3aa8d2.json     three samples, each with its reasoning kept here and nowhere else
```

The hash is of the compiled prompt: the question, the options, the window
text, the judge's model. Change any of them and the file name changes; a
`grade` that finds the file already there does not call out again. That is
what makes `grade` safe to re-run after a report tweak, and it is the only
cache. `grade --rejudge` ignores it.

The report changes by one row and one number. The provenance table names
each judge used (name, kind, model, prompt-template hash); the per-check
guardrail rate already prints `n needs judge` beside its rate, and a
`decide` check's escalation rate is that number. Nothing else in `Stats.cs`
or the renderers moves: a judge verdict is a `Verdict` like any other, and
`needs-judge` already means not decided.

## The diff to the model

Six changes. Three files are new; the rest are edits at the seams the
vocabulary already has.

### 1. `Eval/Judges.cs`: the `judges` block

Records, loading, validation. `kind` is one of `chat`, `systemone`,
`logprobs`. `endpoint` is required; `model` is required for `chat` and
`logprobs` and refused for `systemone`, whose model is the endpoint's;
`api_key` is optional and `${VAR}` references resolve at grade time, never at
load, so `list` and `run` work without the key in the environment and only
`grade` needs it. `default` names an entry. `family` is a free string.
`ValidateSpec` for `with` checks the name exists and the kind fits the check.

### 2. `Grade/Window.cs`: what the judge sees

A window is a named, deterministic extraction from the `Transcript`, joined
with `+`:

| name | text |
|---|---|
| `answer` | the last assistant message |
| `answer_json` | the last JSON fence in it, pretty-printed |
| `prompt` | the case's prompt, resolved |
| `tool_calls` | one line per call: ordinal, name, arguments compacted to one line, `denied` or `error` when so |
| `tool_results` | the results, each capped at a fixed number of lines, for the criterion that needs an output |
| `diff` | `diff.patch` |
| `user_turns` | the nudges and any scripted user turns |

Each part is fenced and labelled in the compiled prompt so the judge can
quote from it. The whole transcript is not a window and cannot be spelled.
An empty window is `error: window 'answer' is empty`, never a call, because
the research line's question 2 already knows what a decider says about an
empty state. A window over the judge's `max_window` (default 24K
characters, per entry in `proctor.json`) is `error: window too large`,
never truncated, for the same reason. The arm's id, the model's name and the
sample number are not in any window and not in any prompt.

### 3. `Grade/Judge.cs`: the client and the two checks

One `HttpClient`, three request shapes, one method per kind that returns
`(label, probability?, quotes, raw)`:

- `systemone`: `{state, questions: {q: {type, instructions, options?}}}`;
  `noul` for a yes/no `decide`, `choice` for one with options. The
  probability is the response's.
- `logprobs`: the shim from the trial plan. The question is compiled the
  OpenJev way (state, question, numbered options, "answer with exactly one
  of"), sent as a one-token chat completion at temperature 0 with
  `top_logprobs`, and the probability is the softmax over the option
  tokens' log probabilities. A temperature fitted by the bench can sit on
  the entry as `scale`.
- `chat`: the generative judge. System prompt fixed in code and versioned
  by its hash; user turn is the windows then the criterion; the model is
  asked to reason inside `<thinking>` and then emit one fenced JSON
  `{"label": "yes"|"no"|"unknown", "evidence": ["…"]}`. Temperature 0,
  `samples` calls (default 3). The reasoning is kept in the verdict file
  and nowhere else.

The `decide` verdict: `pass` when the winning label is `expect` at or above
`threshold`; `fail` when a different label is at or above it; `needs-judge`
below it; the reason is `label p=0.97`. `expect` may be `"@expect"` like any
check value.

The `judge` verdict: every quote is checked as a verbatim substring of the
window it was drawn from; a sample with an unverifiable quote is discarded
as `unverified`. `pass` when every surviving sample says the expected label
(`yes` unless `expect` says otherwise), `fail` when every one says the
other, `needs-judge` when they split or any says `unknown`, `error` when
fewer than two survive or the endpoint fails. The reason is
`yes 3/3 — "first quote"`, so the matrix tooltip shows the evidence without
opening the cell. A parse failure is a discarded sample, counted in the
verdict file, and a check whose samples all failed to parse is `error`, in
the denominator, as the survey insists.

Both checks are `Known`; the `NotYet` row for `judge` goes. `Eval.Judged`
becomes "any check naming `judge` or `decide`".

### 4. `verdicts/` and the cache

`Layout.VerdictsDir`, one file per check and judge and prompt hash. The
file holds the resolved request (key redacted), each response as received,
the parsed labels and quotes per sample, token usage and wall time, and the
verdict `checks.json` got. `Grade.Cell` looks for the file before calling;
`--rejudge` on the `grade` verb skips the lookup. Deterministic checks are
always recomputed; they are free.

### 5. Provenance

`experiment.json` is untouched, because the judge is chosen at grade time,
not run time. `checks.json` stays `{name: {result, reason}}`. The report's
provenance table reads the verdict files' headers for the judges used, and
`results.jsonl` gains nothing: a row already carries every check's verdict.
The manifest is untouched too; the same-family reminder is stderr from
`grade` and a line in the verdict file.

### 6. Tests

No network. `Proctor.Tests` gets a fake endpoint on a loopback
`HttpListener` that answers each of the three shapes from canned responses,
and the tests drive the two checks through it: a `decide` above, below and
across the threshold; a `judge` unanimous, split, with an `unknown`, with a
quote that is not in the window, with a response that is not JSON, with an
endpoint that returns 500. Window extraction is tested from the captured
Mock transcripts in `Proctor.Tests/fixtures`. The cache is tested by
counting requests across two `grade` calls. The smoke eval gains one
`decide` and one `judge` check pointed at the fake, so the runner tests
cover the whole path once.

## What this does not do, and what brings it in

- **Calibration is the bench's, not proctor's.** A `decide` threshold and a
  `judge` prompt earn a place in `grading.pass` by the numbers in
  `jev-trial.md` steps 0 to 2 (kappa, ECE, coverage against the labelled
  set), and those numbers live in `bench/systemone/`, where the labelled set
  is. Proctor records which judge and which prompt hash graded a cell so the
  reader can look them up; it does not carry kappa. Trigger for carrying it:
  a second eval whose judge is different from code-change's, at which point
  a per-eval calibration note is the cheapest honest thing.
- **The bench gains a `chat` backend** to score the generative judge on the
  same items, which is how the trial plan's "rubric wording is itself an
  eval" is run. That is a bench change, filed under `jev-trial.md` step 2.
- **Evidence by selection** (trial plan step 4) is a `decide` whose options
  are numbered spans from the window. When it holds up, it is a `quote`
  field on `decide`, not a new check.
- **A judge over `tool_results`** is in the window table because the
  criterion "did the tests actually pass" needs the output; the cap on lines
  is a guess until a real criterion sets it.
- **Pairwise judging** stays out. Arms are compared by counting labels, as
  the prior art rules say, and nothing here needs a second arm in a prompt.
- **A script check that wants a judge** keeps calling the endpoint itself,
  as the trial plan's step 5 does. When two evals have written the same
  shim, proctor exports the resolved judges as `PROCTOR_JUDGES` JSON to
  scripts.

## Order

1. `Judges.cs` and validation; `proctor.json` example updated; `list` still
   works with no key set.
2. `Window.cs` with tests from the captured transcripts.
3. `Judge.cs`: the fake endpoint first, then `decide` over `systemone`, then
   `logprobs`, then `judge` over `chat`. Each with its tests before the next.
4. `verdicts/`, the cache, `--rejudge`.
5. Provenance in the report; snapshot approved.
6. The worked example: the three checks on code-change, graded once against
   hosted Jev and GLM through the gateway, once against a local decider on
   imp, and the two verdict sets diffed. That diff is the first number the
   trial plan's step 2 wants and it comes out of proctor rather than the
   bench.

Steps 1 to 5 are a day and do not wait on labels. Step 6 without the
labelled set proves the plumbing, not the judge, and the plan says so in
the report's provenance until the bench says otherwise.

## As built (2026-09-22)

Steps 1 to 5, one session, suite green at 212 tests. What differs from the
plan above:

- **The chat judge rides Microsoft.Extensions.AI.** `judge` talks to an
  `IChatClient`, built by default from the OpenAI adapter against the
  entry's endpoint and model, so a provider change is a config line and
  proctor never chases a vendor's dialect. Tests hand in a scripted
  `IChatClient`; no HTTP listener. `decide` posts to systemone with a plain
  `HttpClient`, and tests hand in a scripted handler.
- **No `logprobs` kind.** Jev on Cloudflare is pennies per run; the local
  shim waits until the local-Jev situation in `jev-trial.md` step 3 settles,
  and enters as a third `kind` if it does.
- **No `default` key.** A `judges` map of entries only; a check without
  `with` gets the only judge of the kind it needs, and two of a kind is a
  load-time problem that says to name one, as `--arm` does for `baseline`.
- **Judge resolution is a load-time problem when the config is in hand.**
  `list` and `run` load the eval with the config's judges and report a
  missing or wrong-kind judge under `grading.checks.<name>.<decide|judge>.with`;
  `grade` resolves again and throws the same sentence.
- **The hash covers threshold and expectation too**, not only the compiled
  prompt and the model, so changing what a check expects re-calls rather
  than re-reading a verdict scored under another expectation.
- **`expect: "@expect"` on a model check reads `expect.<check name>`**, not
  `expect.decide`, since every decide shares the field name.
- **The `tool_results` window cuts each result at 40 lines**, the guess the
  plan said it would be, and the `tool_calls` window cuts arguments at 400
  characters.
- **Same-family is a `note:` line from `grade`**, computed from the arm's
  `model` and `provider` strings against the judge's `family`, nothing in the
  manifest.
- **The report's judge rows read the verdict files.** `Judge.Uses` collects
  judge, kind, model (the response's for systemone) and endpoint per check;
  `Report.Html` and `Markdown` take the list as a fourth argument, and the
  snapshot is unchanged because the worked experiment has none.

### Step 6: the worked example, run

The three checks are on code-change as guardrails, not in `pass`, and
`20260922-1308-code-change-x5jx` (nine cells, all passing on the
deterministic checks) was graded three times: Jev for the two decides and
GLM 5.2 for the judge, then `--judge glm=k2` for Kimi K2.6, then plain
again, which restored GLM's verdicts from the files without a call.

| check | judge | verdicts | note |
|---|---|---|---|
| `stance` | jev | 9 pass, all `complete p=1.00` | one call per cell, well under a second |
| `ran-tests` | jev | 9 pass, `yes p=0.98`–`0.99` | |
| `change-fits` | glm | 9 pass; 7 at 3/3, 2 at 2/3 | 4 of 27 samples discarded: GLM quoted `Legacy_Widgets` from a diff that says `Legacy.Widgets` |
| `change-fits` | k2 | 9 pass; 7 at 3/3, 2 at 2/3 | 2 of 27 discarded, same reason; 20–130 s per cell, 1–7K output tokens of reasoning |

Every cell is at the ceiling, as the deterministic checks already said, so
this proves the plumbing and the evidence rule, not the judges. Two things
it did prove:

- **The verbatim rule catches real hallucination.** Six samples across
  two chat judges quoted text that is not in the material, all on the
  rename case, all substituting `_` for `.` in a namespace, and the cell
  still resolved on the surviving samples. The first grading pass had one
  cell where all three GLM samples did it, which is `error`; the retry
  after the cache fix passed. At temperature 0 through the gateway, GLM's
  answers differ between calls with an identical prompt.
- **Cloudflare's AI Gateway rejects `application/json; charset=utf-8`**
  with "Required value missing: input". The client sends the bare media
  type. Found because every decide errored on the first pass; an error
  verdict is now never reused from the file, so the retry cost nothing
  but the calls.

Closed the same day: `--judge a=b` is now a comparison pass. It evaluates
only the checks `a` grades, by `b`, into `b`'s files marked `applied:
false`, prints both verdicts per cell and an agreement count, and never
writes `checks.json`. The report's judge rows count applied files only.
