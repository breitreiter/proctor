# promptfoo's assertion catalogue as a check on proctor's grading proposal

Reading-only pass, 2026-09-17, against
https://www.promptfoo.dev/docs/configuration/expected-outputs/ and the
deterministic, classifier and trajectory sub-pages. `frameworks.md` §2 already
lists promptfoo's assertion types; this note asks a different question: given
what practitioners evidently want asserted (promptfoo's catalogue is the
largest demand signal we have), what does the grading half of
`plans/on-disk-layout.md` get wrong, leave out, or do better?

The proposal under review, as it stands in the layout plan:

- `grading.checks` names scripts under `checks/` (`builds.sh`,
  `tests-pass.sh`, `diff-in-scope.sh`); exit 0/1/2 (pass, fail, needs-judge)
  after weaver.
- A case carries an `expect` block; the only example is
  `files_touched: [...]` with no stated semantics.
- The judge reads a declared window and returns labels with quoted evidence.
- Length, tokens, tool-call count and exit reason are trailer columns in the
  report, never asked of the judge.

## The one-line verdict

The proposal has the right stance and the wrong bottom rung. Everything in
promptfoo's catalogue that people actually use daily is a one-line declarative
check over a string or a JSON object, and the proposal has no place to write
one: every check is a script. That is the scaffolding problem the brief
complains about, reappearing one level down. nb's transcript already carries
the fields that promptfoo has to reconstruct from OpenTelemetry spans, so a
small set of built-in checks over known fields is cheaper for proctor than it
was for promptfoo, and it is the part of the catalogue that transfers.

## The catalogue, sorted by what proctor should do with it

### Take: declarative checks over nb's known fields

promptfoo's workhorses are `equals`, `contains`, `icontains`, `regex`,
`starts-with`, `contains-any/all`, `is-json`, `contains-json` (with a JSON
schema), `word-count`, and the `not-` prefix on any of them. Weaver's
`valid_answer` is `contains-json` by another name; oracle-bench's key-set
comparison is `equals` on a set. Each of these needs a named input, which
promptfoo supplies through `transform` and proctor should supply as a named
window. The windows nb already defines:

| window | source | promptfoo equivalent |
|---|---|---|
| `answer` | last `assistant_text` | the output string |
| `answer_json` | `assistant_json` (last fenced JSON) | `contains-json` + `transform` |
| `trailer` | the `result` event | `finish-reason`, `latency`, `cost`, `perplexity` |
| `tool_calls` | every `tool_call` with `approved`, `approval_reason`, typed `arguments` | `trajectory:*`, `tool-call-f1`, `trace-*` |
| `tool_results` | `tool_result.result` (exit_code, truncated) | `trace-error-spans` |
| `oracle_turns` | `user` events with `source: oracle` and `keys[]` | none |
| `diff` | `diff.patch` from the teardown hook | none (`javascript` custom) |

The built-ins worth having, each binary, each negatable:

- **Answer shape.** `answer_json_schema` (a schema file beside the case),
  `answer_contains` / `answer_regex` / `answer_equals`, `answer_words
  {min,max}`. This is `valid_answer` made declarative.
- **Exit.** `exit_reason: ok` as the implicit default on every case. A run
  that ended in `max_tool_calls` or `approval_denied` fails and stays in the
  denominator; the proposal already says so, but as a report rule rather than
  a check. Make it a check so it shows in `checks.json` like everything else.
- **Tools used.** `tools_used: [names]` (all present), `tools_used_any`,
  `tool_pattern {pattern, min, max}`. promptfoo's `trajectory:tool-used`.
- **Tools forbidden.** `not tools_used`. The nb-native and better version is
  `denied_calls: {max: 0}` over `approved == deny`, and
  `approval_reason == no-match` as "reached outside its surface", which
  promptfoo cannot express at all.
- **Tool arguments.** `tool_args {name|pattern, args, mode: partial|exact,
  ignore: [globs]}`. promptfoo's `partial` (expected is a subset of actual),
  `ignore` (drop `*_id`-style keys) and `defaults` (strip arguments equal to
  the tool's documented default) are the right vocabulary and cost nothing to
  adopt. nb keeps argument types, so numbers compare as numbers.
- **Tool order.** `tool_sequence {names, mode: in_order|exact}`. `in_order`
  allows gaps and is the useful one.
- **Tool errors.** `tool_errors {max}` counted from `tool_result.result.exit_code
  != 0` or a result marked as an error. promptfoo's `trace-error-spans` with
  `max_count` or `max_percentage`.
- **Loop nudged.** `loop_nudged: false`. The doom-loop reminder is injected
  as a user message and so appears as a `user` event in the stream
  (`ConversationManager.cs:724` in nb). Countable today by matching the
  reminder text; brittle, so see "candidates for nb" below.
- **Oracle.** `oracle_hit: [keys]`, `oracle_misses {max}`, `oracle_turns
  {max}`. oracle-bench's four-way hit/miss/done/error classification, which is
  today jq over the stream, is a built-in over the `oracle_turns` window plus
  `exit_reason == oracle_miss`.
- **Files touched.** `files_touched {paths, mode: at_least|exactly|at_most}`
  over `diff`. The proposal's only `expect` example, now with semantics
  borrowed from `tool-args-match`'s `partial|exact`.
- **Budget caps.** `max_tool_calls`, `max_tokens`, `max_duration_ms`,
  `max_cost` over the trailer. promptfoo's `latency`, `cost`,
  `trajectory:step-count {max}` show that a per-case cap is a check people
  want, not only a report column. Cost is not in nb's trailer; proctor
  computes it from `usage` and a price on the provider entry (zero for imp).

Anything repo-specific, which is exactly `builds`, `tests-pass` and
`diff-in-scope`, stays a script. That is promptfoo's `javascript` /
`python` / `webhook` seam, and the proposal's exit-code contract is the
right shape for it. Two things to borrow from promptfoo's custom-assertion
contract: the script gets the case's `expect` block and the cell directory as
context, and it returns a one-line reason on stdout that lands in
`checks.json` beside the verdict, so the results matrix cell can show
"verdict plus reason" without opening the log.

### Take, but as counts and rates rather than pass/fail

Every declarative check above produces a boolean per run. promptfoo then
averages weighted assertion scores into a per-test score and passes on a
threshold. Reject the averaging (the judge and stats notes already say why:
count labels, interval the counts) but keep promptfoo's `metric:` label idea
in its simplest form: `checks.json` is a map from check name to
`pass | fail | needs-judge | error` plus reason, every named check gets its
own per-arm rate with a Wilson interval in the report, and `grading.pass`
lists the subset that constitutes success. Every other check is a guardrail
column. That is weaver's `pass.jq` made data, and it is the blocking versus
report-only split from `ci.md` at the level of a single check.

### Decompose: scalar metrics that hide two booleans

- **`tool-call-f1`** compares the set of tool names called against an
  expected set and thresholds the F1, default 1.0. At threshold 1.0 it is set
  equality; below it, it is a scalar that will be averaged. Recall equal to 1
  is `tools_used: all expected`; precision equal to 1 is `not tools_used: any
  unexpected`. Two booleans, two rates, no F1.
- **`perplexity` / `perplexity-score`** need logprobs, which most of our
  providers do not return, and produce a scalar with no agreed meaning per
  case. Reject.

### Reject: reference-text similarity

`rouge-n`, `bleu`, `gleu`, `meteor`, `levenshtein` and embedding `similar`
score prose against a reference answer. Our cases have no reference prose:
the reference is a diff that builds, tests that pass, a JSON answer, or a
rubric. n-gram overlap between two agents' closing summaries measures
verbosity and phrasing, which is the verbosity trap the judge notes warn
about. `similar` is the one that tempts, because qembed on imp makes it free,
but it is still a scalar threshold on a quantity nobody can calibrate per
case. Not now; if a case ever needs "did it say roughly this", it is a judged
criterion with quoted evidence.

### Reject: pairwise and holistic

`select-best` and `max-score` pick a winner across a row's outputs. The judge
literature pass measured a 13.6% verdict flip rate on rerun for pairwise
judging and worst position bias exactly when candidates are close; proctor
grades each run in isolation and compares by counting. `trajectory:goal-success`
is a holistic judge over the whole trace, which is the single-call-over-
everything design the same pass rejected.

### Treat as a middle rung, with judge-grade validation: `classifier`

promptfoo's `classifier` runs the output through a HuggingFace
text-classification model and passes if the named class's confidence clears a
threshold. The shipped examples are toxicity, PII, prompt injection and bias,
none of which matter for coding agents on fixture repos. But the mechanism is
the interesting one: a small fixed-weight model is deterministic for fixed
inputs, cheap, and by construction outside every arm's model family. The
check we keep wanting that is neither regex nor worth a judge call is
"did the closing message claim completion?", which is oracle-bench's
"done-on-waiting", the one number that summary calls dangerous. A zero-shot
NLI classifier on the last `assistant_text` with labels
`claims-done | asks | reports-blocked` is plausible there.

Conditions, all from `judges.md`: the label comes out as a label, never a
confidence averaged across runs; the threshold lives in code and is
versioned; the classifier is validated against one to two hundred
human-labelled runs with kappa reported, exactly as a judge check would be;
and it is a `needs-judge` fallback, not a replacement, when confidence is
below the bar. `is-refusal`, which promptfoo implements as a phrase list
plus empty-output detection, is the same problem in disguise: for nb the
structural signals (`exit_reason`, `approval_denied`, oracle turns) come
first, and the phrase-list version is the classifier's job, not a regex.

Not for the first version. Note it in the grading plan as the third rung
between built-ins and the judge so the door is visibly open.

## What promptfoo's popularity should not talk us into

- Its assertion model is single-turn: one prompt, one output string. The
  `trajectory:*` family is bolted on through OpenTelemetry spans, which is why
  it has to guess tool names from six different span attributes. nb emits
  typed `tool_call` events with approval outcomes and the exit reason, which
  is strictly more than promptfoo can see. Proctor's checks should be
  written against the transcript schema, not against a string.
- Provider-response caching is on by default and silently turns `--repeat 5`
  into five copies of one sample. Proctor never caches subject runs. It
  caches judge verdicts only, keyed by rubric hash, which the layout plan
  already does through the verdict filename.
- Weighted score averaging and per-test thresholds make the pass rate a
  function of the weights. Proctor's pass is a named subset of binary checks.
- No `needs-judge` third state, no quoted-evidence verification, no
  statistics. Those stay proctor's.

## Changes this asks of the layout plan

1. Add a `checks` vocabulary section: the built-ins above, each with its
   window, its options, and its negation, and the rule that the `expect`
   block of a case is where their values live. Scripts are for what the
   built-ins cannot say.
2. Define `checks.json` as `{name: {verdict, reason}}` with four verdicts,
   and `grading.pass` as the subset that counts as success. Every named check
   is a rate in the report.
3. Give `files_touched` its `mode`.
4. Record the classifier rung as a deferred option with its validation
   conditions attached.

## Candidates for nb, not proctor

- Tag the doom-loop and pending-todo reminders with `source: "loop"` and
  `source: "todo"` on the injected `user` event, the way the oracle's turns
  carry `source: "oracle"`. A check should not have to match reminder prose.
- Carry `cost` in the trailer when the provider entry declares a price, so a
  transcript is attributable without proctor's price table.
