---
type: plan
title: The first slice — run, grade, one report
created: 2026-09-17
status: steps 1–7 built and tested 2026-09-17; step 8 run on imp 2026-09-18 (experiment `20260919-0343-code-change-hpzr`)
---

# The first slice — run, grade, one report

The smallest proctor that replaces what we keep rebuilding and puts a page in
front of a stakeholder. It runs nb-only arms through the layout in
`on-disk-layout.md`, grades with built-in checks and scripts, computes the
statistics once, and renders a single self-contained HTML report plus a
markdown twin. No judge, no real-CLI arms, no site, no archive.

The acceptance test is the floor arm of the work experiment in `brief.md`:
three code-change fixtures, three samples each on qwen-coder through nb,
nine runs, about three hours. When that produces a report that can be posted
as a link and survives a sceptical read, the slice is done.

## Scope

In:

- `proctor list`, `run`, `resume`, `grade`, `report`.
- Arms with `runner: nb` only. Any number of them, so two local models or
  two prompts can be compared, but every arm goes through nb.
- Hooks at the arm and sample levels. Run and case levels are parsed and
  rejected with "not yet" so an eval written for later does not silently
  skip them.
- Built-in checks over the trailer, tool calls, answer and diff windows, plus
  script checks. The full vocabulary in the layout plan, minus the oracle
  checks, which wait for an eval that uses an oracle.
- `results.jsonl`, `stats.json`, `report.html`, `summary.md`.
- Wilson intervals per arm and per check; Newcombe paired differences
  between arms that share cases; the minimum detectable effect sentence.

Out, each with the trigger that brings it in:

- The judge and `verdicts/`. Trigger: a case whose pass cannot be decided by
  build, tests and diff.
- The `command` runner. Trigger: the hosted arms of the work experiment.
- The site bundle and `index.json`. Trigger: the second experiment, when
  history exists.
- `archive` and `fetch`. Trigger: the first time a run directory is wanted
  after it was deleted locally.
- In-process nb via `nb.Core`. The slice spawns `nb` as a subprocess, which
  keeps the boundary at the program file and the JSONL stream and matches
  how a consumer would install both tools. Trigger to revisit: subprocess
  overhead or quoting actually hurting, measured.
- Unpaired comparisons. Every arm in one experiment runs every case, so
  arms always share cases and the paired method is the only one needed.

## The proctor repository

One project, one executable, no library split. nb needed `nb.Core` because a
library consumer exists; proctor has none yet, and the global guidelines
say no abstraction for a boundary that is not there.

```
proctor/
  Proctor.csproj              net10.0, Exe, PackAsTool later
  Program.cs                  verb dispatch; hand-rolled args like nb's Args.cs
  Layout.cs                   paths: experiment dir, cell dir, the file names, nothing else
  Eval.cs                     eval.json, case files, proctor.json: records + loading + validation
  Runner.cs                   the matrix loop, cell status, hooks, nb subprocess
  Transcript.cs               reads nb JSONL into the windows the checks use
  Checks.cs                   the built-in vocabulary; ScriptCheck alongside
  Grade.cs                    runs checks over a cell, writes checks.json
  Stats.cs                    Wilson, Newcombe, MDE; writes stats.json
  Results.cs                  results.jsonl rows from cells
  Report.cs                   report.html and summary.md from stats + results
  Proctor.Tests/              xunit
  evals/                      proctor's own evals, against nb's Mock provider
  project/                    this directory
```

Dependencies: `System.Text.Json` and `System.CommandLine` are not needed;
nb parses its own arguments and so does this. No templating package: the
report is a handful of tables and a few sentences, and C# raw string
literals with a small escaping helper are enough. Revisit if the template
grows past a few hundred lines.

## Contracts the report reads

Fixed here because the site will read the same files later and the markdown
twin reads them now. Both renderers are pure functions of these two files.

**`results.jsonl`**, one row per cell, coordinates on every row:

```json
{"run_id":"b7e2f9c04d1a4e6b","arm":"floor","case":"add-retry-flag","sample":1,
 "status":"completed","exit_reason":"ok",
 "usage":{"input":48211,"output":9120,"total":57331,"estimated":false},
 "tool_calls":23,"denied_calls":0,"duration_ms":1118000,
 "checks":{"exit_ok":"pass","no_denials":"pass","builds":"pass","tests-pass":"fail","diff-in-scope":"pass"},
 "pass":false,
 "reasons":{"tests-pass":"2 of 41 tests failed"}}
```

`pass` is the conjunction of the checks named in `grading.pass`. A cell
whose status is `failed` or `skipped` has `pass: null`, no `checks`, and a
`status_reason`; it stays in the accounting table and out of the rates.

**`stats.json`**, computed once:

```json
{
  "experiment": "20260917-1432-code-change-k7px",
  "n_cases": 3, "paired": true,
  "arms": {
    "floor": {
      "planned": 9, "attempted": 9, "completed": 9, "graded": 9, "analysed": 9,
      "excluded": [],
      "pass": {"k": 6, "n": 9, "rate": 0.667, "ci95": [0.354, 0.879]},
      "checks": {
        "builds":     {"k": 9, "n": 9, "rate": 1.0,   "ci95": [0.701, 1.0]},
        "tests-pass": {"k": 6, "n": 9, "rate": 0.667, "ci95": [0.354, 0.879]}
      },
      "exit_reasons": {"ok": 8, "max_tool_calls": 1},
      "duration_ms": {"n": 9, "median": 1118000, "p90": 1402000, "mean": 1150000, "min": 812000, "max": 1402000},
      "tokens_total": {"n": 9, "median": 57331, "p90": 71002, "mean": 58900, "min": 41200, "max": 71002}
    }
  },
  "comparisons": [],
  "mde": {"n_pairs": 3, "points": 63, "sentence": "With 3 paired cases this experiment can reliably detect a difference of about 63 points. To detect 10 points you need about 200 cases."},
  "methods": "Per-arm rates: Wilson 95%. Paired differences: Newcombe 95%. Durations and tokens: median, p90, mean, range. No multiplicity adjustment; 0 comparisons shown.",
  "cases": ["add-retry-flag", "fix-null-deref", "rename-module"],
  "matrix": {"floor": {"add-retry-flag": ["pass","pass","fail"], "fix-null-deref": ["pass","pass","pass"], "rename-module": ["fail","pass","fail"]}}
}
```

With two or more arms, `comparisons` holds one entry per pair against the
first arm declared: `{"arm":"b","vs":"floor","diff_points":24,"ci95":[6,41],
"won":6,"lost":1,"tied":14,"verdict":"better|worse|no-detectable-difference"}`.
When cases have several samples, each case reduces to its mean before the
interval, per the statistics learnings, and `n` is then the case count.

## The report

`report.html` is one file: inline CSS, a few lines of script for row
highlighting, no fonts fetched, no external assets, opens from a Slack
attachment or `file://`. `summary.md` has the same sections with plain
tables. Sections, in order, taken from `learnings/prior-art.md` §5 with the
site-only pieces removed:

1. **Title line and templated summary.** Eval id, experiment id, date, arms,
   samples. One or two sentences filled from `stats.json`, including the MDE
   sentence verbatim. Never a free sentence.
2. **Headline table.** One row per arm in declared order: analysed of
   attempted, pass rate as `67% [35, 88] (6/9)`, and, when there is a
   comparison, the difference with its interval and a fixed verdict word.
3. **Accounting.** Planned, attempted, completed, graded, analysed per arm,
   with an exclusions list naming each excluded cell and why. A warning band
   above the headline when arms lost runs unequally.
4. **Case by arm matrix.** Rows are cases, columns are arms, cells show the
   per-sample verdicts as glyphs and the reason of the first failure on
   hover, with the run id. This is the table a reader of the work experiment
   actually uses.
5. **Checks.** One row per check, per arm: rate with interval. The checks in
   `grading.pass` marked as such; the rest labelled guardrails.
6. **Failure breakdown.** Exit reason by arm, counts and share.
7. **Cost and effort.** Duration and tokens per arm: median, p90, mean,
   range. Cost when nb's trailer carries it; until then omitted, not
   estimated.
8. **Per-run table.** Every cell, failures first: arm, case, sample, status,
   exit reason, pass, duration, tokens, run id, and the reason.
9. **Reproducibility.** Proctor and nb versions, eval hash, git commit and
   dirty flag of the repo under test, command line, the methods footnote from
   `stats.json`.

Visual rules, since the gravitas argument in the brief is the point:
restrained typography, one accent colour for the verdict glyphs, tables with
right-aligned numbers and monospace ids, no charts in this slice. It should
look like a benchmark report, not a dashboard.

## Build order

Each step ends with something that runs. Tests go in with the step, not
after. nb's Mock provider drives everything below step 7, because a Mock
program can produce any transcript the checks need without a model.

1. **`list`.** Load `proctor.json`, `eval.json`, cases; validate (registered
   tags, known check names, arm ids unique, `pass` names existing checks);
   print the cells that would run and their count. Tests: a good eval loads,
   each validation error is reported with its file and field.
2. **`run` for one cell.** Resolve the program template for one case, write
   the cell directory and manifest, invoke `nb` as a subprocess with
   `--output jsonl`, capture stdout to `transcript.jsonl` and stderr to
   `stderr.txt`, set `status`. Test: against Mock, the cell directory matches
   the layout plan file for file.
3. **The matrix and `resume`.** Loop arms, cases, samples; skip cells whose
   status is `completed`; write `experiment.json` and `status.json`; hooks at
   arm and sample level with logs captured; a failed hook marks the cell
   `failed` with a reason and continues. Tests: resume skips completed cells;
   a hook failure is recorded and does not stop the matrix.
4. **Transcript windows.** Read nb JSONL into the trailer, tool calls, tool
   results, answer, answer JSON and diff windows. Tests: fixture transcripts
   captured from Mock runs, one per window shape, including an `estimated`
   trailer and a denied tool call.
5. **Checks and `grade`.** The built-in vocabulary with negation; the script
   contract with environment variables and the stdout reason; `checks.json`
   written per cell; `pass` computed. Tests: every built-in has a passing and
   a failing fixture; a script that exits 2 yields `needs-judge`; a script
   that cannot run yields `error`, not `fail`.
6. **Statistics.** Wilson, Newcombe paired, MDE, the distribution summaries,
   `stats.json`. Tests: Wilson against published values including 0 of n and
   n of n; Newcombe against the worked numbers in
   `learnings/prior-art/stats.md`; case-mean reduction when samples exceed
   one.
7. **`report`.** `results.jsonl`, then `report.html` and `summary.md` from
   the two files. Tests: snapshot both renderings for a fixed `stats.json`,
   reviewed and approved by hand, the way the test-framework learnings
   describe; an assertion that the HTML references no external URL.
8. **The acceptance run.** The work experiment's floor arm, for real, on
   imp. Post the report. Fix what the first reader trips on before anything
   else is added.

Steps 1 through 7 are Mock-only and run in proctor's own CI. Step 8 is a
person and a GPU.

## Decisions taken here

- **Subprocess, not in-process.** Stated above with its trigger to revisit.
- **One project.** No `Proctor.Core` until a second consumer exists.
- **No templating dependency.** Raw string literals and an escaper.
- **The matrix is the primary view for small experiments.** The headline
  table stays, but at three cases the matrix is where the information is,
  and the report orders sections so a reader hits it early.
- **Paired only.** Every arm runs every case; unpaired comparison is not
  implemented.
- **Cost is omitted, not estimated.** Until nb's trailer carries it, the
  report has no cost column rather than a guessed one.

## Decisions taken while building (2026-09-17)

- **One interval method at any sample count.** Each case is scored as its
  mean over its analysed samples and `n` is the case count. Per-arm rates
  take a Wilson interval; paired differences take Newcombe's paired method 10
  with his continuity-corrected phi from the 2x2 case table, which with
  several samples is the expected table from the case means. This is exactly
  the textbook method at one sample and never collapses to zero width; the
  CLT-on-case-means alternative does at `n = 3` whenever the cases agree.
- **The MDE uses an assumed per-case paired-difference sd of 0.5**, stated in
  the methods note. At 3 cases that is 81 points, not the 63 the sketch above
  copied from `n = 5`.
- **Durations are wall time of the cell, hooks included.** nb's trailer
  carries no `duration_ms`. Revisit when it does.
- **`{{work}}`, not `{{fixture}}`**, names the checkout directory, and
  proctor never deletes it: script checks such as `builds` need it at grade
  time, which may be long after teardown. A teardown hook may clean up.
- **`"@expect"`** is how a check in `eval.json` takes its value from the
  case's `expect` block.
- **Scripts get the cell in the environment**, not arguments, so a hook and
  a check are written the same way. The list is in `CLAUDE.md`.
- **`answer_json` is the last json fence** in the answer: nb emits no
  `assistant_json` event today.
- **The HTML has no script at all.** Row highlighting from the matrix is CSS
  `:target`.

## Step 8 as built (2026-09-17)

`evals/code-change/` is the acceptance eval: one `floor` arm, qwen-coder on
imp through nb, three samples, three cases against fixtures checked in under
`fixtures/` (small .NET projects with tests; `ledger` ships with the bug its
case asks to fix). Cases pin a fixture by `path`; the reset hook also accepts
`git` + `rev`. `acceptance/<case>/` holds tests the `tests-pass` check copies
into the checkout, so a solution has to meet the task's named interface, not
just keep the old tests green. `reference/shakedown.sh` proves the hooks and
checks with reference solutions and without a model; `--unsolved` shows the
checks failing for the right reasons. To run it for real:

```bash
export MINROUTER_KEY=...          # the minrouter key on imp; evals/nb.json reads it
proctor run code-change           # the arm hook swaps imp to qcoder first
proctor grade <id> && proctor report <id>
```

### The acceptance run (2026-09-18, evening local; the id is UTC)

Experiment `20260919-0343-code-change-hpzr`: 9 of 9 cells completed, 9 pass,
no denials, no loop nudges, every diff in scope. The whole run took 13
minutes against the 3 hours budgeted: cells ran 32–162 s (median 61 s) and
24k–266k tokens. qwen-coder never reached outside the bash allow-list.

What the run found in proctor: `max_duration_ms` read the trailer's
duration, which nb never sets, so `under_budget` was `error` on every cell
while the report already used the manifest's wall time. The check now falls
back to the same wall time (fixed and regraded the same night). Two things
to weigh for the next slice: the floor is at the ceiling on these three
cases, so they do not discriminate until a harder arm or harder cases
arrive; and the 3-hour, 20-minutes-per-cell budgeting in memory was off by
an order of magnitude for this model on tasks this size.

## Open questions

- **The `{{prompt}}` and `{{case}}` template vocabulary.** The layout plan
  shows placeholders in `program.nb` without defining them. The slice needs
  at least the case prompt and the fixture path; whether the whole case JSON
  is exposed as fields, and how a template escapes nb's line-oriented syntax
  when a prompt has newlines, gets decided in step 2.
- **Fixture checkout.** The layout plan leaves where checkouts live open.
  The slice puts them under `.proctor/work/<cell>/`, gitignored, created by
  the sample setup hook from the case's git revision, and deleted by
  teardown after `diff.patch` is collected. Proctor provides the path in an
  environment variable and does not do the checkout itself.
- **What the first reader wants first.** Unknown until step 8. The section
  order above is a guess to be corrected by a real reader, which is the
  reason the slice is this small.
