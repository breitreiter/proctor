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
| `Verbs.cs` | one method per verb: `list`, `run`, `resume`, `grade`, `report` |
| `Eval/Layout.cs` | paths and file names, nothing else |
| `Eval/Eval.cs` | `proctor.json`, `eval.json`, cases, the program template: records, loading, validation (`Problem` = file, field, message); the merged check set and expect block per case |
| `Eval/Fixture.cs` | `fixtures/<id>/fixture.json`: source, checks, default expect; the source-tree hash |
| `Run/Runner.cs` | experiment and cell manifests, the matrix loop, hooks, nb as a subprocess, resume |
| `Run/Checkout.cs` | the work directory: materialise the fixture, collect the diff, restore both for a regrade |
| `Run/Subprocess.cs` | the one process helper: hooks, nb, script checks, git |
| `Grade/Transcript.cs` | nb JSONL into the windows checks read (trailer, answer, answer JSON, tool calls, tool results, user turns, diff) |
| `Grade/Checks.cs` | the built-in vocabulary and the script contract; `Glob` |
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
  fixture plus diff for a regrade whose checkout is gone.
- **Every number is computed once, in `Stats.cs`.** The renderers format; they
  never compute. If a number looks wrong, fix it in `stats.json` first.
- **Each case is scored as its mean over its analysed samples**, so `n` in
  every interval is the case count. Per-arm rates: Wilson. Paired differences:
  Newcombe method 10 with his continuity-corrected phi from the 2x2 case
  table, built from the case means when there are several samples. This
  reduces to the textbook binary methods at one sample and never gives a
  zero-width interval.
- **nb's trailer carries no `duration_ms`** (as of 2026-09-17), so durations
  are the cell's wall time from the manifest, hooks included, both in the
  report and in the `max_duration_ms` check. It carries no cost, so the
  report has no cost column rather than an estimate.
- **nb emits no `assistant_json` event**; the `answer_json` window is the last
  ` ```json ` fence in the last assistant message.
- **Program templates.** Placeholders are `{{prompt}}`, `{{case}}`, `{{work}}`
  (the fixture checkout), `{{provider}}`, `{{model}}`, `{{harness}}`, `{{arm}}`,
  `{{sample}}`. A prompt's newlines become nb continuation lines (` \`), so a
  multi-line prompt stays one directive. A prompt line that itself ends in a
  backslash cannot be expressed.
- **Check values from the case.** A check field whose value is `"@expect"`
  reads `expect.<field>` from the case; a case without it yields `error`.
- **Hooks and script checks** run with `PROCTOR_EVAL_DIR`, `PROCTOR_FIXTURE`,
  `PROCTOR_EXPERIMENT`, `PROCTOR_ARM`, `PROCTOR_CASE`, `PROCTOR_SAMPLE`,
  `PROCTOR_CELL`, `PROCTOR_WORK`, `PROCTOR_CASE_JSON`, `PROCTOR_EXPECT` (the
  merged block), `PROCTOR_TRANSCRIPT`, `PROCTOR_DIFF`. Hooks run in the eval
  directory; script checks run in the cell, an eval's resolved against the
  eval directory and a fixture's against the fixture directory. Scripts exit
  0/1/2 for pass/fail/needs-judge; anything else is `error`, never folded
  into fail. The sample order is: fixture checkout, setup hook, nb, teardown
  hook, diff. `PROCTOR_WORK` is never deleted (`.proctor/` is gitignored).
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
