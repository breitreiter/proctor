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
eval.json        arms, samples, tags, hooks, grading
program.nb       the nb program template; {{prompt}}, {{case}}, {{work}}, {{provider}}, {{model}}, {{harness}}, {{arm}}, {{sample}}
cases/*.json     one case per file; the id is the file name
checks/*.sh      script checks (exit 0/1/2 = pass/fail/needs-judge; first stdout line is the reason)
hooks/*.sh       arm and sample setup/teardown
```

`evals/smoke/` is a complete example that runs against nb's Mock provider.
The check vocabulary and the shape of every file are in
`project/plans/on-disk-layout.md`.

## Use

```bash
proctor list [eval]          # validate; print the cells that would run
proctor run <eval>           # run every cell into runs/<id>/; prints the id
proctor resume <id>          # rerun cells that did not complete
proctor grade <id>           # checks over every completed cell -> checks.json
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
```

`report.html` is one file with no external assets; open it from `file://`
or attach it to a message. `summary.md` has the same sections as plain tables.

Every rate carries a 95% interval and its counts; comparisons between arms are
paired on shared cases with their own interval and a fixed verdict word; and
the minimum detectable effect is stated so an underpowered result cannot read
as "no difference". The methods are named in the report's last paragraph.
