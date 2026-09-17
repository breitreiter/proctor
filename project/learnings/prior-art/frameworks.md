# LLM / agent eval frameworks: survey for proctor

Research date: 2026-09-17. Scope: Inspect (UK AISI), promptfoo, OpenAI Evals, Braintrust,
LangSmith, DeepEval, lm-evaluation-harness, HELM. Depth on Inspect and promptfoo.

Framing for proctor: nb emits one JSONL transcript per run; proctor defines arms, runs them N
times, grades, and emits deterministic tables. Each section answers the six questions
(definition / bookkeeping / grading / statistics / report shape / model-written prose) and
closes with "lessons" and "bad fit for whole-agent transcripts".

---

## 1. Inspect (UK AISI, `inspect_ai`)

Docs: https://inspect.aisi.org.uk/ . Reference: https://inspect.aisi.org.uk/reference/inspect_ai.html

### 1.1 Experiment definition on disk

- A task is a Python function decorated `@task` returning `Task(dataset=..., solver=..., scorer=...,
  epochs=, config=GenerateConfig(...), name=, version=, metadata=, tags=, message_limit=,
  token_limit=, time_limit=, sandbox=)`. https://inspect.aisi.org.uk/tasks.html
- **Variants / arms**: task function parameters with defaults, set on the CLI with `-T key=value`
  or `--task-config=config.yaml`. Also `task_with(base_task, solver=..., message_limit=...)` for
  programmatic overrides, and derived `@task` functions that call a base. Solver args via `-S`.
- **Sample count**: `--epochs N` repeats each dataset sample; `--epochs-reducer mean|median|mode|max|
  at_least_{n}|pass_at_{k}|pass_k_{k}` collapses epochs to one score per sample before metrics.
  https://inspect.aisi.org.uk/options.html
- **Sweep**: `inspect eval-set` / `eval_set(tasks, model=[...], log_dir=...)` runs tasks x models,
  balances active tasks across models, retries failures, is resumable, and is idempotent: rerunning
  on the same `log_dir` is "a no-op as there is no more work to do". Adding models/tasks to the
  command extends the set. Dynamic task params must be plain Python types so identity serialises
  stably. https://inspect.aisi.org.uk/eval-sets.html
- **Dataset**: `Sample(input, target, id, choices, metadata, files, setup, sandbox)`; loaders
  `csv_dataset`, `json_dataset` (JSON or JSONL), `hf_dataset`; `FieldSpec` or `record_to_sample`
  maps foreign columns. `--limit 10-20`, `--sample-id 44,63`, wildcard ids `*_advanced`,
  `--sample-shuffle 42`. https://inspect.aisi.org.uk/datasets.html
- **Boundary**: tasks are discovered from any file (`file.py@task_name`), package entry points, or
  HF `eval.yaml`. No mandated layout; the convention in `inspect_evals` is one package dir per eval
  with `dataset` + `task.py` + `scorer`. The code under test is a *solver* (or an *agent*) the task
  composes; the eval never lives inside the solver. The agent-under-test interface is
  `solve(state: TaskState, generate)`, i.e. the eval owns the loop and the agent is a callable.
  A whole external agent is wrapped as a solver or bridged via `sandbox_agent_bridge`.
- Model roles: `--model-role grader=openai/gpt-4o` binds a named model separate from the
  model under test; `ModelRole("grader", required=True)` prevents fallback to the evaluated model.

### 1.2 Run bookkeeping

- Log dir `./logs` (or `--log-dir`, `INSPECT_LOG_DIR`); filename `{timestamp}_{task}_{id}`,
  customisable with `INSPECT_EVAL_LOG_FILE_PATTERN` (can include model). Format `.eval` (binary,
  zip-based, ~1/8 the size of JSON, incremental sample reads) default since v0.3.46, or `.json`.
  `inspect log list --json`, `inspect log dump`, `inspect log convert`, `inspect log export-config`
  (regenerates a runnable YAML/JSON config from a log). https://inspect.aisi.org.uk/eval-logs.html
- `EvalLog` top-level: `version, status(started|success|error|cancelled), eval: EvalSpec,
  plan: EvalPlan, results: EvalResults, stats: EvalStats, error, samples[], tags, metadata,
  log_updates[], config_updates[], invalidated, location, etag`.
- `EvalSpec`: `run_id, task_id, task_version, task_file, task_args (full), model, model_args,
  model_generate_config, config: EvalConfig(epochs, limit, approval, ...), revision:
  EvalRevision(type='git', origin, commit, dirty), packages{name: version}, metadata, tags,
  created`. https://inspect.aisi.org.uk/reference/inspect_ai.log.html
- `EvalSample`: `id, epoch, input, target, messages[], output, scores{name: EvalSampleScore},
  events[] (ModelEvent, ToolEvent, ScoreEvent, BranchEvent, ErrorEvent, SpanBegin/EndEvent...),
  metadata, store, error, limit (which limit tripped)`. Large repeated content is de-duplicated
  as attachments.
- Tags/metadata are editable after the fact and the edit history is kept (`log_updates`).
- Errors: `--fail-on-error` threshold (fraction or count) before the whole eval is marked error;
  `--retry-on-error N`; `inspect eval-retry` resumes; `EvalResults.total_samples` vs
  `completed_samples` makes the denominator explicit. Per-sample limits `message_limit`,
  `token_limit` (e.g. `"output:1m"`, `"(input*0.1)+output:1m"`), `time_limit`, `working_limit`;
  a limited sample is still scored and `sample.limit` records which limit fired.

### 1.3 Grading

- Scorer contract: `async def score(state: TaskState, target: Target) -> Score`. The scorer sees
  the *whole* `TaskState` (messages, output, store, metadata, tool events), not just a string, so
  transcript-level checks are natural. `Score(value, answer, explanation, metadata)`;
  constants `CORRECT='C'`(1.0), `PARTIAL='P'`(0.5), `INCORRECT='I'`(0), `NOANSWER='N'`.
  https://inspect.aisi.org.uk/scorers.html , https://inspect.aisi.org.uk/reference/inspect_ai.scorer.html
- Deterministic: `includes, match(location=begin|end|any|exact), pattern(regex), answer(prefix),
  exact, f1, choice, math`. `multi_scorer([...], reducer)` runs several and reduces.
- Model-graded: `model_graded_qa(template, instructions, grade_pattern, include_history,
  partial_credit, model=[...], model_role='grader', reducer='majority')` and `model_graded_fact`.
  Template vars `{question} {answer} {criterion} {instructions}` plus sample metadata.
  Default instructions ask for reasoning then `GRADE: C|P|I`. Default `grade_pattern` is
  `(?is).*(?<!\w)GRADE(?!\w)[\s...]*:[\s...]*([CPI])`, deliberately matching the **last**
  occurrence so a model cannot steer the grade by mentioning "GRADE: C" early, and tolerant of
  zero-width unicode between the tokens. `include_history=True|callable` feeds the full transcript
  (or a custom rendering) rather than just the input. A list of grader models produces a panel with
  strict-majority voting; unparseable grades abstain without lowering the threshold.
  https://inspect.aisi.org.uk/model-graded.html
- Judge bias handling: explicit advice to grade with a different model than the one under test,
  `temperature=0` and fixed seed on the grader, `ModelRole(required=True)`.
- `inspect score <log>` rescores an existing log (deferred / edited scoring without rerun).

### 1.4 Statistics

- Metrics on `inspect_ai.scorer`: `accuracy, mean, std, var, stderr(cluster=<metadata key>),
  bootstrap_stderr(num_samples=1000), ci() -> {lower, upper}, ci_wilson()` (binary scores),
  `frequency()`, `grouped(metric, "category", all="samples"|"groups")` for per-slice breakdown.
  https://inspect.aisi.org.uk/metrics.html
- Epochs are reduced to one score per sample before metrics (`scores="auto"|"reduced"|"unreduced"`
  on `@metric`) so the stderr denominator is samples, not sample x epoch. Clustered stderr for
  samples drawn in related groups (multi-question passages).
- `EvalResults.headline: HeadlineMetric` marks the one number to show.
- No built-in paired comparison across evals; that is left to `inspect_ai.analysis` dataframes
  (`evals_df`, `samples_df` joined on `sample_id`) and pandas. The inspect_evals maintainers paper
  (https://arxiv.org/abs/2507.06893) reports adding "statistical methodologies for optimal
  resampling and cross-model comparison with uncertainty quantification" on top.

### 1.5 Report shape

- `inspect view` (port 7575): task list, per-eval header with headline metrics, sample rows with
  score/answer columns; per-sample tabs Messages / Scoring (answer, target, explanation) /
  Metadata; filter by score value; sort chronological / by score / by sample (all epochs of one
  sample together). `inspect view bundle` produces a static site. No side-by-side arm comparison
  in the viewer. https://inspect.aisi.org.uk/log-viewer.html
- Analysis API: `evals_df()` one row per log with column groups `EvalInfo, EvalTask, EvalModel,
  EvalDataset, EvalConfiguration, EvalResults, EvalScores`; `samples_df()` one row per sample
  (`SampleSummary` header-only fast path; `SampleScores`, `SampleMessages` for full);
  `messages_df()`, `events_df()`. Columns are JSONPath-defined (`EvalColumn("task_arg_*",
  path="eval.task_args")`). `prepare(df, [model_info(), task_info(), log_viewer(...), frontier()])`.
  Ids `eval_id`, `sample_id` (globally unique), `event_id` make joins trivial; DuckDB example.
  https://inspect.aisi.org.uk/dataframe.html
- The "manager table" is therefore whatever you build from `evals_df` + `EvalScores`; Inspect ships
  the data model, not the table.

### 1.6 Model-written prose

None. Model output appears only as the grader's `explanation` on a per-sample score.

### Lessons and fit

- Sharp lessons visible in the design: last-occurrence grade regex (score steering); grader panel
  with abstention; `revision.dirty`; `completed_samples` vs `total_samples`; `sample.limit`
  recorded on the sample; idempotent eval-set keyed on log_dir; `.eval` binary format because JSON
  logs of agent transcripts got huge; attachments de-dup; tags/metadata edit history.
- Fit for whole-agent transcripts: **good**. Scorer sees full TaskState; events are a typed
  timeline; limits are first class. The mismatch for proctor is only that Inspect wants to *own*
  the agent loop (solver), whereas nb already produces the transcript.

---

## 2. promptfoo

Docs: https://www.promptfoo.dev/docs/

### 2.1 Experiment definition on disk

- Single `promptfooconfig.yaml` (can pass several with `-c a.yaml -c b.yaml`). Top-level keys:
  `description, tags, prompts, providers, tests, scenarios, defaultTest, outputPath, sharing,
  extensions, derivedMetrics, metadata, evaluateOptions{maxConcurrency=4, repeat=1, delay, cache}`.
  https://www.promptfoo.dev/docs/configuration/reference/
- **Arms** = the cartesian product prompts x providers. A provider is `{id, label, config, prompts
  (filter), delay, transform}`; two entries with the same `id` and different `config`/`label` are
  two arms. Prompts are `file://prompt.txt`, `file://prompts.py:fn`, or chat JSON.
- **Cases** = `tests: [{description, vars, assert, threshold, metadata, options{repeat,...},
  provider}]`, or `tests: file://tests.csv` (special columns `__expected`, `__expected1..n`,
  `__prefix`, `__suffix`, `__description`, `__metadata:field`), or a JS/Python generator.
  Array-valued vars expand as a cartesian product. `scenarios` group `config` + `tests`.
  `defaultTest` supplies shared vars/asserts. https://www.promptfoo.dev/docs/configuration/test-cases/
- **Sample count**: `--repeat N` / `evaluateOptions.repeat` / per-test `options.repeat`.
- **Boundary / wrapping an app**: custom provider `file://myProvider.mjs|.py|.ts` implementing
  `callApi(prompt, context{vars, test.metadata, logger}, options) -> {output, tokenUsage, cost,
  error, cached, metadata, guardrails}`; also `http:` and `exec:` (argv = prompt, options-JSON,
  context-JSON; stdout is the output and *cannot* populate structured fields).
  https://www.promptfoo.dev/docs/providers/custom-api/ ,
  https://www.promptfoo.dev/docs/providers/custom-script/
  Eval config lives entirely outside the app; the app is reached only through the provider seam.

### 2.2 Run bookkeeping

- Results go to a SQLite DB under `~/.promptfoo` (`PROMPTFOO_CONFIG_DIR`); each eval gets an
  `evalId`; `latest` alias. `promptfoo list evals`, `promptfoo show <id>`, `promptfoo export eval
  <id>`, `promptfoo import`. `-o results.{json,csv,yaml,html,xml,jsonl,junit.xml}`.
  Exit code 100 when any test fails or pass rate is below `PROMPTFOO_PASS_RATE_THRESHOLD`.
  https://www.promptfoo.dev/docs/usage/command-line/
- JSON export (schema `version: 3`): `evalId, timestamp, config (full, secrets redacted),
  shareableUrl, results{prompts[] (with per-prompt metrics), providers[], tests[], outputs[], stats}`.
  Per output: `vars, prompt (rendered), response, gradingResult{pass, score, reason,
  componentResults[{type, pass, score, reason}]}, namedScores, latencyMs, cost, tokenUsage`.
  https://www.promptfoo.dev/docs/configuration/outputs/
- No git revision capture; provenance is the embedded rendered config. Disk cache of provider
  responses is on by default (`--no-cache` to disable), which is a footgun for repeat counts.

### 2.3 Grading

- What is extracted: the provider's `output` string (or object), plus for trace/trajectory
  assertions the OpenTelemetry-style spans the provider emits. Tool names are read from span
  attributes `tool.name`, `function.name`, `ai.toolCall.name`; args from `tool.arguments`,
  `tool.args`, `tool.input`, `function.arguments`.
  https://www.promptfoo.dev/docs/configuration/expected-outputs/deterministic/
- Deterministic assertions: `equals, contains, icontains, regex, starts-with, contains-any/all,
  is-json, contains-json, is-xml, is-sql, javascript, python, ruby, webhook, latency, cost,
  rouge-n, bleu, levenshtein, is-valid-openai-tools-call, tool-call-f1, is-refusal,
  trace-span-count, trace-span-duration, trace-error-spans, trajectory:tool-used,
  trajectory:tool-args-match (partial|exact, defaults, ignore), trajectory:tool-sequence
  (in_order|exact), trajectory:step-count`. Every type can be negated with `not-`.
- Model-graded: `llm-rubric, g-eval, factuality (OpenAI A-E categories), model-graded-closedqa,
  answer-relevance, context-faithfulness/recall/relevance, conversation-relevance, select-best
  (grader picks an index across the row's outputs), max-score, classifier, moderation, similar
  (embeddings), pi, trajectory:goal-success`.
  https://www.promptfoo.dev/docs/configuration/expected-outputs/
- Grader constraints: grader must return JSON `{pass, score, reason}`; `rubricPrompt` override in
  chat format with `{{output}}` `{{rubric}}`; grader chosen by `--grader` > assertion `provider` >
  test `options.provider` > `defaultTest.options.provider` > auto by available API key; default
  grader `temperature=0`. No positional-bias mitigation for `select-best`.
  https://www.promptfoo.dev/docs/configuration/expected-outputs/model-graded/
- Score combination: test score = weighted mean of assertion scores (`weight`), pass if all
  assertions pass or score >= test `threshold`; assertion sets with their own threshold;
  `metric:` label groups assertions into named metrics; `derivedMetrics` (MathJS / JS) compute
  composites post hoc, with `__count` for averaging.

### 2.4 Statistics

- None. Pass rate per column, mean score, score-distribution histogram. `--repeat` exists but
  repeats are just more rows; no CI, no paired test. Community blog posts recommend computing
  bootstrap CIs yourself from the JSON export.

### 2.5 Report shape

- Web UI (`promptfoo view`): the **results matrix**: rows = test cases, columns = prompt x provider,
  each column header shows pass rate and mean score; cell shows output + assertion pass/fail +
  reason; filters All/Failures/Passes/Errors/Different/Highlights; metric filters `= contains > <`;
  "Compare" diffs against another evalId (green/red); scatter of one prompt vs another per test;
  human overrides (mark pass/fail, 0-1 score, comment) persisted and exported.
  https://www.promptfoo.dev/docs/usage/web-ui/
- CLI `--table` prints the same matrix as text. HTML export is a standalone table.
- The "manager row" is the column header (pass rate). Detail is the cell.

### 2.6 Model-written prose

None for reports. Model output only in `gradingResult.reason`.

### Lessons and fit

- Lessons: `select-best` is the only true side-by-side judge and it is per-row; the disk cache
  default surprised people (repeats hit cache); `exec:` cannot return structured metadata, so
  trajectory assertions push you to the JS/Python provider; results in a global `~/.promptfoo`
  DB rather than beside the project means provenance = the copied config, not a git commit.
- Fit for whole-agent transcripts: **mixed**. The provider seam is exactly nb-shaped (call
  external thing, get output + metadata), and `trajectory:*` / `trace-*` assertions are the right
  idea, but they require the provider to emit spans in promptfoo's attribute vocabulary. The
  simulated-user provider flattens the conversation to a `---`-separated string and runs
  assertions on that string (https://www.promptfoo.dev/docs/providers/simulated-user/), which is
  the wrong representation for structured transcript grading.

---

## 3. OpenAI Evals (`openai/evals`)

Docs: https://github.com/openai/evals/blob/main/docs/build-eval.md ,
https://github.com/openai/evals/blob/main/docs/eval-templates.md ,
https://github.com/openai/evals/blob/main/docs/run-evals.md

1. Definition: registry YAML at `evals/registry/evals/<name>.yaml`:
   ```yaml
   name:               {id: name.split.version, description:, metrics: [accuracy]}
   name.split.version: {class: evals.elsuite.basic.match:Match, args: {samples_jsonl: name/samples.jsonl}}
   ```
   Data at `evals/registry/data/<name>/samples.jsonl`, each line `{"input": [chat msgs], "ideal": str|[str]}`.
   Model-graded evals point `class` at `ModelBasedClassify` with a `modelgraded_spec` YAML:
   `prompt, input_outputs, choice_strings ("ABCDE" or list), choice_scores, eval_type
   (cot_classify | classify_cot | classify), output_template`. Variants = new registry ids; no
   sweep tool beyond `oaievalset <model> <set>` (a named list of evals; `progress.txt` resume).
2. Bookkeeping: `oaieval <model> <eval> --record_path`; default `tmp/evallogs/{run_id}_{model}_{eval}.jsonl`.
   First line is the spec (`run_id, eval_name, base_eval, split, run_config, created_by,
   created_at`), then events `{run_id, event_id, sample_id, type: sampling|match|metrics|raw_sample, data}`,
   then `final_report`. No git capture.
3. Grading: `Match` (startswith), `Includes`, `FuzzyMatch`, `JsonMatch`; `ModelBasedClassify`
   with fixed `choice_strings` and mapped `choice_scores`; built-in specs `fact` (A-E subset /
   superset / agree / disagree / differ), `closedqa` (relevance, conciseness, correctness in
   sequence), `battle` (pairwise). Explicit guidance that `cot_classify` (reason **then** choice)
   "typically provides the most accurate model-graded evaluations". Battle has no order swap.
4. Statistics: `accuracy` only (plus per-choice counts for model-graded). No CI.
5. Report: `final_report` JSON line; no viewer. The "manager number" is one accuracy.
6. Model prose: none.

Lessons: fixed choice letters + mapped scores; CoT-before-answer; the per-event JSONL with
`sample_id` join key is the minimal viable log. Bad fit for agents: everything is a single
completion; input is a chat list, output a string.

---

## 4. Braintrust

Docs: https://www.braintrust.dev/docs/evaluate/run-in-code ,
https://www.braintrust.dev/docs/evaluate/write-scorers ,
https://www.braintrust.dev/docs/evaluate/compare-experiments

1. Definition: `Eval(name, {data, task, scores, experiment_name, metadata, trial_count,
   max_concurrency, update, base_experiment_name})` in `*.eval.ts` / `eval_*.py`; `braintrust eval`
   (alias `bt eval`) discovers and runs all such files in cwd, `--watch` reruns. `data` is a list
   of `{input, expected, metadata}` or `initDataset()`. Arms = separate `Eval()` calls or
   `metadata` values; `trial_count` = repeats per input. Evals sit beside code like tests; the app
   is reached through `task(input)`.
2. Bookkeeping: experiments are hosted records (permanent); git metadata (branch, commit, dirty)
   captured automatically; `.env` auto-loaded; CI action posts a summary comment with links.
   No local file format documented; export is via API/UI.
3. Grading: scorer `(input, output, expected, metadata) -> number 0..1 | {name, score, metadata}`.
   autoevals library: `Factuality`, `ClosedQA`, `LLMClassifier(name, prompt_template,
   choice_scores, use_cot, model, temperature)` etc. Classifier output = categorical `{name, id,
   label}`. No bias handling documented beyond "be specific".
4. Statistics: none documented; per-score means and per-row deltas only.
5. Report: comparison against a **baseline experiment** (explicit, project default, or "most
   recent experiment on the same git branch"); rows aligned by `input` hash by default or a
   custom comparison key SQL expression (`[input.query, metadata.category]`); every row gets
   score-delta columns; header counts improvements / regressions per score; filter by regression;
   Summary / Summary table layouts show aggregates across experiments; group by metadata or by
   input (which collapses trials into an expandable group). Diff mode limited to trace view.
6. Model prose: "Loop" assistant can "identify patterns across failures" (optional, UI-side, not
   the report).

Lessons: the comparison key is a first-class configurable thing because inputs contain volatile
fields (timestamps, session ids) that break row alignment; baseline defaults to same-branch
latest. Bad fit: hosted-only; transcript is a trace of spans, comparison disables custom views.

---

## 5. LangSmith evaluations

Docs: https://docs.langchain.com/langsmith/evaluation-concepts ,
https://docs.langchain.com/langsmith/evaluate-pairwise ,
https://docs.langchain.com/langsmith/compare-experiment-results

1. Definition: Dataset of Examples `{inputs, outputs (reference), metadata}` with `splits` and
   automatic dataset **versions**; `evaluate(target, data=, evaluators=[...], experiment_prefix=,
   metadata=, num_repetitions=)`; also pytest/vitest plugins. Arms = separate experiments with
   metadata. Datasets live in the service, code in repo.
2. Bookkeeping: an Experiment = one target version x one dataset version; every example run is a
   trace. Hosted.
3. Grading: evaluators return feedback `{key, score|value, comment}`; types: code, LLM-as-judge
   (reference-free or reference-based), human (annotation queues), pairwise. Pairwise evaluator
   returns `{key, scores: {run_id: score}, comment}` or a two-item list; ties `[0,0]`;
   `randomize_order=True` to counter position bias (default False). `evaluate_comparative()` for
   >2 experiments. Summary evaluators compute one feedback over the whole experiment.
4. Statistics: none; repetitions are averaged in the UI.
5. Report: Comparison view: pick a source (baseline) experiment; feedback columns per experiment;
   red/green per run vs baseline; header counts improved/regressed with click-to-filter; Compact /
   Full / Diff (2 experiments, JSON/YAML only) layouts; "Pairwise Experiments" tab with
   thumbs up/down filter.
6. Model prose: none in reports.

Lessons: they split "score independently then compare" from "pairwise judge" as two different
products and note pairwise is better for open-ended tasks; explicit `randomize_order`. Bad fit:
hosted; whole-agent runs are traces, evaluators typically see `outputs` dict not the trace unless
you write a custom one over `run`.

---

## 6. DeepEval

Docs: https://deepeval.com/docs/evaluation-introduction , https://deepeval.com/docs/metrics-llm-evals

1. Definition: pytest style. `LLMTestCase(input, actual_output, expected_output, context,
   retrieval_context, tools_called, expected_tools)`; `ConversationalTestCase(turns=[...])`;
   `EvaluationDataset` + `@pytest.mark.parametrize`; `assert_test(test_case, metrics)`; run with
   `deepeval test run test_*.py`. Arms are not a concept; you parametrise yourself.
2. Bookkeeping: local `.deepeval/` cache; hosted Confident AI for reports. No git capture
   documented.
3. Grading: 30+ mostly LLM-judged metrics. `GEval(name, criteria, evaluation_params,
   evaluation_steps, rubric=[Rubric(score_range=(a,b), expected_outcome)], threshold=0.5, model,
   strict_mode, flaky)`. If steps are omitted the judge *generates its own evaluation steps* from
   the criteria; score = probability-weighted expectation over 1-10 output tokens (G-Eval paper),
   normalised to 0-1. `reason` field. `ToolCorrectness` compares `tools_called` vs
   `expected_tools`; `TaskCompletion` judges the trace. A test passes when every non-flaky metric
   passes its threshold.
4. Statistics: none.
5. Report: pytest pass/fail table; hosted dashboards.
6. Model prose: none in report, but the auto-generated `evaluation_steps` means the rubric itself
   may be model-written unless you pin it.

Lessons: `rubric` with non-overlapping score ranges was added to make G-Eval outputs less
arbitrary; `flaky=True` to keep a noisy judge from gating CI. Bad fit: token-probability scoring
requires logprobs (unavailable on many providers, then degrades silently to plain sampling);
auto-generated steps are not defensible to experts.

---

## 7. lm-evaluation-harness (EleutherAI)

Docs: https://github.com/EleutherAI/lm-evaluation-harness/blob/main/docs/task_guide.md ,
https://github.com/EleutherAI/lm-evaluation-harness/blob/main/docs/interface.md

1. Definition: one YAML per task under `lm_eval/tasks/<group>/`: `task, task_alias, tag,
   dataset_path, dataset_name, dataset_kwargs, test_split, doc_to_text (Jinja2), doc_to_target,
   doc_to_choice, output_type (generate_until | loglikelihood | loglikelihood_rolling |
   multiple_choice), num_fewshot, generation_kwargs, filter_list (named post-processing
   pipelines, e.g. regex extract then take_first), metric_list [{metric, aggregation,
   higher_is_better}], metadata: {version: N}`. Groups: `aggregate_metric_list`, `weight_by_size`
   (micro vs macro average). Variants = `include: base.yaml` overrides; sweeps by `--tasks a,b`.
2. Bookkeeping: `--output_path dir` writes `results_<timestamp>.json` with `results, group_subtasks,
   configs (full task YAML), versions, n-shot, git_hash, date, pretty_env_info, transformers_version,
   model args`; `--log_samples` writes `samples_<task>_<timestamp>.jsonl` with doc, prompt,
   filtered response, per-metric score. `--seed` takes four seeds (python, numpy, torch, fewshot).
3. Grading: deterministic only (`acc, acc_norm, exact_match, f1, bleu, ...`), with `filter_list`
   as the explicit "extract answer from generation" stage. No model graders.
4. Statistics: every metric gets `<metric>_stderr,<filter>` (`acc_stderr,none`); mean metrics use
   analytic SE, others bootstrap (`--bootstrap_iters`, default 100000, multiprocess); groups get
   pooled stderr. No paired comparison.
5. Report: printed markdown table `| Tasks | Version | Filter | n-shot | Metric | Value | Stderr |`
   with a `±` column, one row per task/metric/filter; group rows above subtasks.
6. Model prose: none.

Lessons: task `version` bumped on any change, printed in the table, so numbers across versions
are never silently compared; filters make extraction explicit and multiple filters can be scored
in parallel on the same generations; stderr is printed next to every number by default. Bad
fit: single completion, loglikelihood-centric, HF datasets.

---

## 8. HELM (Stanford CRFM)

Docs: https://crfm-helm.readthedocs.io/en/latest/tutorial/ , https://crfm-helm.readthedocs.io/en/latest/metrics/

1. Definition: run entries as description strings `mmlu:subject=anatomy,model=openai/gpt2` on
   the CLI or in `run_entries.conf` (with `priority`). `RunSpec = scenario + adapter spec
   (prompting/decoding) + metric specs`, separated by design. `--max-eval-instances N`,
   `num_train_trials` for repeats with different few-shot draws.
2. Bookkeeping: `benchmark_output/runs/<suite>/<run_name>/{run_spec.json, scenario.json,
   scenario_state.json (all requests+responses), per_instance_stats.json, stats.json}`.
   Run name is the description string; suite is the experiment.
3. Grading: metric classes over `scenario_state`; reference-based metrics take
   `max_i score(reference_i, prediction)`; perturbations (typos, dialect) recorded in the stat
   name. Critique metrics use human/LLM raters but are a small minority.
4. Statistics: `Stat{name{split, sub_split, perturbation}, count, sum, mean, min, max, variance,
   stddev}` aggregated across instances and trials; leaderboard shows means, no CI displayed.
5. Report: `helm-summarize --suite` writes `summary.json, run_specs.json, runs.json, groups.json,
   groups_metadata.json, groups/{json,latex}/` (per-group tables: rows = models, columns =
   scenarios/metrics) and `schema.yaml` controls which metrics appear; `helm-server` serves the
   leaderboard plus per-instance predictions.
6. Model prose: none.

Lessons: the separation scenario / adapter / metric is the cleanest "what varies" model; every
stat is name-qualified by split and perturbation so tables never mix them; `schema.yaml` is a
declarative description of the report. Bad fit: single-request instances; heavy config surface.

---

## Cross-cutting observations

### What "extract before scoring" looks like
- lm-eval: explicit `filter_list` pipelines; multiple extractions scored side-by-side.
- Inspect: scorer receives full `TaskState`; `answer()`/`pattern()` scorers extract; `grade_pattern`
  extracts the judge's verdict with last-match semantics.
- promptfoo: `transform` on provider/test/assertion; trajectory normalisation from span attributes.
- OpenAI evals: `choice_strings` parsed from the judge; nothing on the sample side.

### Statistics availability (summary table)

| framework | per-arm SE/CI | clustered | epochs reduced before SE | paired arm comparison |
|---|---|---|---|---|
| Inspect | stderr, bootstrap_stderr, ci, ci_wilson | yes (`cluster=`) | yes | no (DIY via dataframes) |
| promptfoo | no | no | no | no (row diff only) |
| OpenAI evals | no | no | n/a | no (battle judge only) |
| Braintrust | no | no | trials grouped | row-aligned deltas, counts |
| LangSmith | no | no | averaged | row-aligned deltas, pairwise judge |
| DeepEval | no | no | no | no |
| lm-eval | stderr per metric, bootstrap | no | n/a | no |
| HELM | stddev only | no | trials averaged | no |

Nobody ships a paired difference with a CI across arms out of the box, even though every
comparison UI aligns rows by case. That gap is where proctor can be strictly better.

### Judge constraint techniques seen
- Fixed label set + regex extraction (Inspect C/P/I; OpenAI `choice_strings`; promptfoo JSON
  `{pass, score, reason}`).
- Reason-before-verdict (`cot_classify`, Inspect default instructions).
- Last-occurrence match to defeat steering (Inspect).
- Panel of graders with strict majority and abstention (Inspect `model=[...]`).
- Separate grader model role, required (Inspect `ModelRole(required=True)`).
- Order randomisation for pairwise (LangSmith `randomize_order`).
- Rubric with score ranges (DeepEval) and weights/thresholds (promptfoo).
- Not seen anywhere: mandatory evidence quoting from the transcript, verified against the
  transcript. This is an opening for proctor.

### Model-written summaries
None of the eight write reports with a model. Braintrust "Loop" and Confident AI dashboards offer
optional assistants outside the deterministic report. Every framework confines model output to the
per-sample `reason`/`explanation` field.

### Sharp design lessons (learned the hard way)
1. Inspect's `grade_pattern` takes the *last* `GRADE:` and tolerates zero-width unicode: graders
   were being steered by the graded text.
2. Inspect moved from JSON to binary `.eval` with attachment de-dup: agent transcripts made logs
   8x too big and slow to open.
3. Inspect `eval-set` idempotent on `log_dir`: long provider-flaky runs must be resumable and must
   not double-count.
4. Inspect records `revision.dirty`, package versions, and `total` vs `completed` samples; the
   `HeadlineMetric` field exists because viewers had to guess which number mattered.
5. Braintrust's configurable comparison key: aligning rows by input hash broke as soon as inputs
   held timestamps/session ids.
6. lm-eval's `version` column in the results table and `stderr` next to every value: silent
   cross-version comparison and bare point estimates burned them.
7. promptfoo's `exec:` provider limitation and span attribute vocabulary: stdout-as-output was
   fine for completions and immediately inadequate for agents.
8. DeepEval's `flaky=True` and `rubric` ranges: LLM judges gating CI produced too many false
   failures.
9. HELM's stat names qualified by split/perturbation: tables that mix perturbed and clean
   instances mislead.

### Bad fit for whole-agent transcripts
- Flattening the conversation to a string before assertions (promptfoo simulated-user, most
  `llm-rubric` usage, OpenAI evals).
- Single-completion data models (`input`/`ideal`, `doc_to_text`), loglikelihood metrics.
- Judges that see only `output` and not the tool-call timeline (Braintrust/LangSmith default
  scorer signatures, DeepEval unless `tools_called` is populated by hand).
- Frameworks that want to own the agent loop (Inspect solvers) when the transcript already exists.
- Probability-weighted judge scores (DeepEval G-Eval) needing logprobs.
- Hosted-only run stores (Braintrust, LangSmith, Confident AI) when the requirement is
  deterministic tables checked in beside code.
