# proctor

Runs an eval's arms through [nb](../nb), grades the transcripts with declared
checks, computes the statistics once, and renders one self-contained HTML
report plus a markdown twin.

## Install

Build nb first (`dotnet build` in its repo), then:

```bash
dotnet build
alias proctor='dotnet /path/to/proctor/bin/Debug/net10.0/proctor.dll'
```

## Configure

A repository that uses proctor has `evals/` (yours, in git), `runs/` (raw
output, gitignored) and `reports/` (derived, written by `proctor report`).

`evals/proctor.json` says where nb is and which config it runs with; paths are
relative to `evals/`:

```json
{ "nb": { "path": "../../nb/bin/Debug/net10.0/nb", "config": "nb-mock.json" } }
```

Without it, `nb` is taken from `PATH` and nb resolves its own config. `--nb
<path>` overrides either.

One eval is one directory, `evals/<id>/`:

```
eval.json        arms, samples, tags, hooks, grading (checks, pass, validity)
program.nb       the nb program template; {{prompt}}, {{case}}, {{work}}, {{provider}}, {{model}}, {{harness}}, {{arm}}, {{sample}}
cases/*.json     one case per file; the id is the file name; names a fixture and adds the goal
checks/*.sh      script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason)
hooks/*.sh       arm and sample setup/teardown
```

A fixture is the repository a case is run against, with what "done" looks
like in it. Fixtures are repo-level, `fixtures/<id>/`, and reused across
evals:

```
fixture.json     id, source ({path} or {git, rev}), stack, default expect, checks
checks/*.sh      the fixture's own checks (builds, tests pass); merged into every cell run on it
repo/            the checkout source; the only thing copied into the work directory
```

An arm may name a `bundle`, the pinned version of whatever it puts under
test: `{ "path": "bundles/x" }` under the repository root, or
`{ "git": url, "rev": sha }`. Proctor hands its directory to the program
template as `{{bundle}}` and to hooks and checks as `PROCTOR_BUNDLE`, and
records its identity in every manifest; what is inside it is the eval's
business. Two arms with two bundles compare them in one experiment.

Proctor checks the fixture out into the work directory before each sample,
commits it, and collects `diff.patch` afterwards. A check named in `pass`
that the eval does not declare must come from every case's fixture. A check
named in `validity` decides whether a sample counts at all: a sample that
fails one is excluded, not failed.

`evals/smoke/` with `fixtures/note/` is a complete example that runs against
nb's Mock provider. The check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md` and `project/plans/fixtures-arms-baselines.md`.

## Use

```bash
proctor list [eval]          # validate; print the cells that would run
proctor run <eval>           # run every cell into runs/<id>/; prints the id
proctor resume <id>          # rerun cells that did not complete
proctor grade <id>           # checks over every completed cell -> checks.json
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--cases x,y]   # pin the arm's analysed cells as evals/<eval>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
```

Two ways of working. In explore mode several arms run in one experiment and
the report compares them to each other. In guard mode one arm runs against
a baseline: pinned cells per case, committed with the eval, written by
`proctor baseline` from an experiment you trust. The report then adds a
section comparing every arm to the baseline with the same paired method,
a verdict of held, improved or regressed on the point estimate against the
tolerance, and the interval beside it so a small case count cannot hide.
While the pinned experiment is still under `runs/`, its cells are re-read,
so a regrade flows through; once it is archived the pinned scores stand,
and the report says which.

`report.html` is one file with no external assets; open it from `file://`
or attach it to a message. `summary.md` has the same sections as plain tables.

Every rate carries a 95% interval and its counts; comparisons between arms are
paired on shared cases with their own interval and a fixed verdict word; and
the minimum detectable effect is stated so an underpowered result cannot read
as "no difference". The methods are named in the report's last paragraph.
