# proctor — developer notes

Experiment manager, grader and reporter around nb (`../nb`). The intent is in
`project/brief.md`; the layout every verb reads and writes is
`project/plans/on-disk-layout.md`; what this version does and does not do is
`project/plans/first-slice.md`.

## Build and test

```bash
dotnet build
dotnet test --no-build                 # ~1 min; the runner tests spawn nb per cell
PROCTOR_APPROVE=1 dotnet test --no-build --filter ReportTests   # approve a changed rendering
dotnet bin/Debug/net10.0/proctor.dll list             # or: dotnet run -- list
```

Tests need nb built: `$NB_PATH`, else `../nb/bin/Debug/net10.0/nb` (build nb
with `dotnet build` in its repo; its Mock provider drives every test here).

## Structure

One project, one executable, no library split. The root holds the entry point
and the project files; the source sits in one directory per pipeline stage,
in the order the verbs run them. Every file is one concern:

| file | holds |
|---|---|
| `Program.cs` | flag parsing and verb dispatch; `ProctorException` is a user-facing failure |
| `Verbs.cs` | one method per verb: `list`, `run`, `resume`, `grade`, `report`, `baseline` |
| `Eval/Layout.cs` | paths and file names, nothing else |
| `Eval/Eval.cs` | `proctor.json` (the host binary and its config), `eval.json` (arms, the `nb` block with the runner and mounts, hooks, grading), cases, the program template: records, loading, validation (`Problem` = file, field, message); the merged check set and expect block per case |
| `Eval/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Eval/Judges.cs` | `proctor.json`'s `judges` block: an endpoint per name by wire shape (`systemone`, `chat`), key resolution, which judge a check gets |
| `Eval/Tree.cs` | a directory's files minus excluded names, and their content hash (fixtures and bundles) |
| `Eval/Baseline.cs` | `evals/<eval>/baseline.json`: pinned cells per case; scores recomputed from the cells while they exist |
| `Run/Runner.cs` | experiment and cell manifests, the matrix loop, hooks, nb as a subprocess, resume |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, script checks, git |
| `Grade/Transcript.cs` | nb JSONL into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
| `Grade/Window.cs` | what a model check sees: named extractions from the transcript, joined with `+`, compiled into labelled fences |
| `Grade/Judge.cs` | `JudgeClient` (the transports), the `decide` check over systemone, the `judge` check over chat, the verdict file as cache, the report's judge provenance |
| `Grade/Grade.cs` | checks over a cell into `checks.json`; the headline pass |
| `Report/Results.cs` | `results.jsonl` rows |
| `Report/Stats.cs` | Wilson, Newcombe paired, MDE, summaries into `stats.json` |
| `Report/Report.cs` | `report.html` and `summary.md`, pure functions of stats and results |
| `evals/smoke/` | proctor's own eval: every case scripts nb's Mock provider |
| `evals/code-change/` | the first real eval: three cases on three fixtures, acceptance tests, reference solutions |
| `fixtures/` | the repositories cases run against, each with its own checks beside (never inside) its `repo/` |
| `Proctor.Tests/` | xunit, flat; `fixtures/` are captured Mock transcripts; `snapshots/` are the approved renderings |
| `project/` | the brief, the plans, the research notes and the loose ends |

The directories are for reading, not for namespaces: everything is
`namespace Proctor`. The data directories `evals/`, `fixtures/`, `bench/`,
`runs/` and `reports/` are lowercase and excluded from compilation in
`Proctor.csproj`.

## Conventions and gotchas

- **`failed` is infrastructure; a bad transcript is `completed`.** A hook that
  exits non-zero or an nb startup error (exit 1, no `result` trailer) marks the
  cell failed with a reason. A transcript whose exit reason is `max_tool_calls`
  is completed; grading decides what it means.
- **Grading writes beside the evidence.** `checks.json` is the only file the
  grader adds to a cell. `report` reads it; a completed cell without one is
  reported as not analysed, and `report` says to run `grade`.
- **A check is one of three kinds by where it is named.** In `grading.pass`
  it is capability; in `grading.validity` it decides whether the sample
  counts (a `fail` excludes the sample with its reason, `error` never does);
  in neither it is a guardrail rate. A name in both lists is a problem.
- **The fixture is the case's; what is under test is the arm's.** A case
  names a fixture; the fixture's checks join the eval's for that cell (a
  clash is a problem) and its default `expect` sits under the case's. Only
  `repo/` is ever copied into the work directory, so the checker and the
  expectations are unreachable from inside it by construction. Proctor does
  the checkout (commit, then `diff.patch` after teardown) and restores
  fixture plus diff for a regrade whose checkout is gone. An arm's `bundle`
  (`{path}` under the repository root, or `{git, rev}` cloned under
  `.proctor/bundles/<rev>`) is resolved once per experiment and recorded
  by source, path and hash in `experiment.json` and every cell manifest;
  proctor never reads what is inside it. `resume` refuses a changed bundle
  as it refuses a changed eval.
- **The baseline is a virtual arm in the statistics, not in the experiment.**
  `report` resolves `baseline.json` to per-case scores (re-read from the
  pinned cells' `checks.json` under the current `pass` and `validity`
  lists when the experiment is still under `runs/`, else the pinned score)
  and compares every arm to them with the same Newcombe pairing. The verdict
  is `held`, `improved` or `regressed` on the point estimate against
  `--tolerance`; `--fail-on regression` turns it into an exit code. The
  baseline is not in the accounting or the matrix.
- **Every id in the report carries a sentence.** `description` is the one
  key in a check spec that is not a check (`Checks.Description`); a script
  check must declare one, a built-in derives one from its fields
  (`Checks.Describe`). Eval, arm and case descriptions are optional; a case
  falls back to the first prompt line (`Eval.Describe`). They travel in
  `stats.json` as `descriptions`, with a per-arm `failures` list (each
  non-validity check that failed or errored in a counted cell, most often
  first, with its cases), which the Failures section renders.
- **The report is one list of blocks rendered twice** (`Report.Blocks`, then
  `RenderHtml` and `RenderMarkdown`), so the two renderings cannot drift in
  wording; a cell may carry HTML for a link or hover. The structure and the
  reader-facing vocabulary (arm, case, run, check; counted, decided; "95%
  interval") are `project/plans/report-structure.md`. On disk a run is still
  a cell and a sample; only the page says run.
- **An undecided run is a third headline outcome.** `Grade.Pass` is `bool?`:
  false when a pass check failed, errored or is missing; null when nothing
  failed but a check is `needs-judge`. An undecided run is counted (it is
  valid) but not decided: out of the pass rate on both sides, out of that
  check's rate, listed under Failures when there are at most five, and a
  warning band when a check is undecided in more than five runs or 5% of
  the runs it applies to (`stats.json` `undecided_checks`). A check that
  cannot decide is the check's weakness, not the arm's.
- **Every number is computed once, in `Stats.cs`.** The renderers format; they
  never compute. If a number looks wrong, fix it in `stats.json` first.
- **Each case is scored as its mean over its counted, decided samples**, so `n` in
  every interval is the case count. Per-arm rates: Wilson. Paired differences:
  Newcombe method 10 with his continuity-corrected phi from the 2x2 case
  table, built from the case means when there are several samples. This
  reduces to the textbook binary methods at one sample and never gives a
  zero-width interval.
- **`versions.nb` is `nb --version` from the host binary**, the `+commit`
  suffix stripped; `unknown` when it fails. It is read once per experiment.
- **nb's trailer carries `duration_ms` since nb 0.9** (nb f7df67f); the
  `max_duration_ms` check reads it and falls back to the cell's wall time
  from the manifest, hooks included, which is what the report shows. It
  carries no cost, so the report has no cost column rather than an estimate.
- **nb emits no `assistant_json` event**; the `answer_json` window is the last
  ` ```json ` fence in the last assistant message.
- **Program templates.** Placeholders are `{{prompt}}`, `{{case}}`, `{{work}}`
  (the fixture checkout), `{{bundle}}` (the arm's resolved bundle directory,
  empty without one), `{{provider}}`, `{{model}}`, `{{harness}}`, `{{arm}}`,
  `{{sample}}`. `{{work}}` and `{{bundle}}` are the paths the *model* will
  see: the eval's `nb.mounts` replaces them when a runner is in effect (`CellContext`
  `WorkMount`/`BundleMount`), and a bare run ignores the mounts so
  `--runner none` stays a host shakedown. A prompt's newlines become nb continuation lines (` \`), so a
  multi-line prompt stays one directive. A prompt line that itself ends in a
  backslash cannot be expressed.
- **The judge is proctor's own client, never nb.** `decide` posts to a
  `systemone` endpoint (Jev on Cloudflare through minrouter today; the
  wire format is the seam, not the vendor); `judge` goes through
  Microsoft.Extensions.AI's `IChatClient` over an OpenAI-dialect endpoint,
  so a provider change is a config line. Both are `Verdict`s like any
  check. The question is the eval's (the check spec), the endpoint is the
  machine's (`proctor.json` `judges`), and the key is resolved at grade
  time only, so `list` and `run` need no key. `Eval.Load` resolves each
  model check's judge when handed the config's judges; the test helper
  passes none, and `grade` resolves again itself.
- **A model check sees a window, never the transcript.** `Window.Known`
  is the whole vocabulary; an empty part or a total over the judge's
  `max_window` is `error` before any call, because a decider answers an
  empty or cut state confidently. Arm id, model name and sample number are
  in no window and no prompt.
- **`verdicts/<check>.<judge>.<hash>.json` is the cache and the record.**
  The hash covers the compiled request, the model, the threshold and the
  expectation; `grade` reuses a file that exists, unless its verdict is
  `error` (an endpoint that did not answer is retried), and `--rejudge`
  reuses nothing.
  The file keeps the request (key redacted), every response, each sample
  as read with why it was discarded, the reasoning, usage and the verdict.
  Deterministic checks are always recomputed. `--judge a=b` is a
  comparison pass: only the checks whose declared judge is `a` are
  evaluated, by `b`, into `b`'s files marked `applied: false`, and the log
  prints both verdicts per cell; `checks.json` is never written. The
  report's judge rows count applied files only, so it names the judge
  whose verdicts the cells hold. Changing `with` in the eval and grading
  plain is how `b` becomes the applied judge, and a plain grade that reuses
  `b`'s file marks it applied.
- **A judge's evidence is verified, its reasoning discarded.** Every quote
  must be a whitespace-normalised substring of the window; a sample with
  an unverifiable quote, no quote on a yes/no, or no JSON block is
  discarded but stays in the file. Fewer than two usable samples (one when
  `samples` is 1) is `error`; unanimous decides; a split or an `unknown`
  is `needs-judge`. The reason string is `yes 3/3 — "first quote"`, so the
  matrix tooltip shows evidence without opening the cell.
- **Calibration is the bench's, not proctor's.** A threshold or a rubric
  earns a place in `grading.pass` by the numbers in `project/plans/jev-trial.md`;
  proctor records which judge, model and prompt hash graded a cell (the
  report's reproducibility section) and carries no kappa.
- **Check values from the case.** A check field whose value is `"@expect"`
  reads `expect.<field>` from the case; a case without it yields `error`.
  A model check's `expect: "@expect"` reads `expect.<check name>` instead,
  since the field name (`decide`) is shared.
- **Hooks and script checks** run with `PROCTOR_EVAL_DIR`, `PROCTOR_FIXTURE`,
  `PROCTOR_BUNDLE`, `PROCTOR_EXPERIMENT`, `PROCTOR_ARM`, `PROCTOR_CASE`, `PROCTOR_SAMPLE`,
  `PROCTOR_CELL`, `PROCTOR_WORK`, `PROCTOR_CASE_JSON`, `PROCTOR_EXPECT` (the
  merged block), `PROCTOR_TRANSCRIPT`, `PROCTOR_DIFF`, `PROCTOR_RUNNER` (the
  runner script in effect, empty on a bare run), `PROCTOR_NB` and
  `PROCTOR_NB_CONFIG` (the host binary and its resolved config, which a
  hook mounts for the runner), `PROCTOR_CONTAINER` (a name derived from the
  cell that proctor never uses), and `PROCTOR_WORK_MOUNT` and
  `PROCTOR_BUNDLE_MOUNT` (the paths the model was told; the host paths
  unless `nb.mounts` moved them). Hooks run in the eval
  directory; script checks run in the cell, an eval's resolved against the
  eval directory and a fixture's against the fixture directory. Scripts exit
  0/1/2 for pass/fail/needs-judge; anything else is `error`, never folded
  into fail. The sample order is: fixture checkout, setup hook, nb, teardown
  hook, diff. Teardown runs whenever setup ran, even when setup failed, so
  it must be idempotent. `PROCTOR_WORK` is never deleted (`.proctor/` is
  gitignored).
- **nb gets the program on stdin, compiled on the host, bare or through a
  runner.** `RunCell` writes the resolved source as `program.nb`, then runs
  `nb --compile` on the host binary in the eval directory (so `@file`
  includes resolve against the eval) and writes the JSONL as
  `program.jsonl`; that is what goes down stdin, so the container never
  holds a path to a sheet. `program_hash` stays the source's. A program nb
  refuses fails the cell before the checkout or any hook. `RunNb` is one
  code path: without a runner it starts `nb --output jsonl [--config] -`
  itself with only `NO_COLOR` added to the environment; with one it starts
  the script with nothing on argv and the cell environment plus `NO_COLOR`,
  and the script starts nb the same way. The bare path deliberately does not get the cell environment:
  `PROCTOR_EXPECT` in the model's reach would leak the answer. A runner that
  forwards its environment into the container wholesale would do the same.
- **The runner is the eval's, the binary is the machine's.** `eval.json`'s
  `nb` block (`runner`, relative to the eval directory like a hook, and
  `mounts`) says how the eval runs nb, because the runner only works with
  the hooks that make its container; `evals/proctor.json` says only where
  the host binary and its config are. `--runner` overrides per run. A
  `runner` or `mounts` left in `proctor.json` is reported as a problem, not
  ignored.
- **Labels are the consumer's; proctor has no tags.** `labels` on an eval,
  a fixture or a case is a key with one or more string values that proctor
  stores, prints, filters on (`list --label k[=glob]`) and records on every
  `results.jsonl` row, never interprets. `Eval.LabelsFor` merges fixture,
  then eval, then case, key by key. A `tags` field is reported as removed.
  Whether an eval is judged is `Eval.Judged`, derived from a check naming a
  `judge` (not yet a known check), not declared.
- **`resume` refuses a changed eval.** The eval hash in `experiment.json` must
  match; a changed eval is a new experiment.
- **Validation says "not yet" rather than silently skipping**: `command` arms,
  run- and case-level hooks, the oracle checks, `answer_json_schema`,
  `max_cost`.
- **Snapshot tests** fail on any rendering change and leave a `.received` file
  (gitignored) beside the approved one. Review it, then approve with
  `PROCTOR_APPROVE=1`. Render to check: `google-chrome --headless=new
  --screenshot=out.png --window-size=1100,2400 file://.../report.html`.
- **The runner tests are slow** (nb takes ~2 s to start per cell); filter them
  out while iterating on anything else: `--filter 'FullyQualifiedName!~RunnerTests'`.
