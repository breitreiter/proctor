---
type: plan
title: A report from the future (worked example for the lab plan)
created: 2026-11-09
status: hand-written, not rendered by any code; every number is invented but adds up; the plan it leads is `lab.md`
---

# A report from the future

This is the `summary.md` proctor should be able to write once `lab.md` is
built. It is written by hand so the disagreement can be about actual lines.
The product ("Beacon", a hosted telemetry service), its docs and the pull
request are invented. Every sentence below must be producible from a template
and `stats.json`; where one is not, that is a defect in this example.

Lines marked `[new]` need machinery proctor does not have. Everything else
exists today.

---

# otel-setup: docs `main` against docs `pr-412`

From the documentation site alone, can an agent instrument a service for
telemetry export to Beacon and verify that it arrives?

Experiment `20270114-0932-otel-setup-k3vd` · 60 runs · 60 counted · 3 h 12 min · $58.10

## What changed between the two bundles  `[new]`

| | `main` | `pr-412` |
|---|---|---|
| Docs bundle | `docs@a41c9e0` | `docs@7be2f15` |
| Pages | 14 | 19 |
| Pages changed | | 6 changed, 5 added, 0 removed |

Same agents, same tasks, same fixtures, same checks. Nothing else differs.

## Result

| | `main` | `pr-412` | Difference | |
|---|---|---|---|---|
| **Outcome**: runs that passed | 8 of 8 | 7 of 8 | 1 run | **No difference detected** |
| **Use of the docs**: runs that ended on the documented endpoint `[new]` | 7 of 8 | 1 of 8 | 6 runs changed, 0 the other way | **Worse** |
| **Effort**: tokens per run, same agent and task `[new]` | median 498k | median 686k | +34% (range +12% to +49%), higher in 8 of 8 pairs | **Worse** |
| **Right the first time**: first export accepted `[new]` | 7 of 8 | 2 of 8 | 5 runs changed, 0 the other way | **Worse (unconfirmed)** |

These rows are the two expert agents (Claude Code on Sonnet 5.5, Codex on
GPT-5.5), four tasks each. The novice agent is reported apart, below.

"Unconfirmed" means the difference could be chance at this size: 5 of 5
changed runs in one direction has an exact probability of 0.06. The two rows
without it have 0.03 and 0.008.

## The control: the same agents with no docs  `[new]`

| | no docs | `main` | `pr-412` |
|---|---|---|---|
| Claude Code · Sonnet 5.5 | 4 of 4 passed | 4 of 4 | 4 of 4 |
| Codex · GPT-5.5 | 3 of 4 | 4 of 4 | 3 of 4 |
| nb · qwen3-coder-next (novice, 3 runs per task) | 1 of 12 | 9 of 12 | 4 of 12 |
| Ended on the documented endpoint, expert agents | 0 of 8 | 7 of 8 | 1 of 8 |

**The expert agents pass these tasks without the docs (7 of 8).** Outcome
cannot tell the two bundles apart for them, and this report does not use it
to. The documented endpoint is reachable only from the docs (0 of 8 without
them), so that row measures the docs.

The novice agent needs the docs (1 of 12 without). With `pr-412` it passed
4 of 12 against 9 of 12, lower on 4 of 4 tasks. Unconfirmed: four tasks
cannot confirm a difference of this size.

## Where the effort went  `[new]`

Tool calls across the 8 expert runs, by what the call touched.

| Phase | `main` | `pr-412` | Change |
|---|---|---|---|
| Reading the docs | 62 | 104 | +42 |
| Editing the repository | 118 | 124 | +6 |
| Verifying export | 190 | 271 | +81 |
| Other | 46 | 51 | +5 |
| Total | 416 | 550 | +134 |

## What the agents ran into  `[new]`

Events proctor counted in the transcripts. Each is a rule declared in the
suite, not a model's reading.

| Event | `main` | `pr-412` | First seen |
|---|---|---|---|
| Beacon answered 401 | 2 events in 1 run | 17 events in 6 runs | `codex.pr-412/aspnet/1` step 41 |
| Export sent to the retired `/v1/traces` | 1 run | 7 runs | `cc-sonnet.pr-412/flask/1` step 23 |
| A docs page returned 404 | 0 | 0 | |

The 401, as the agent saw it (`codex.pr-412/aspnet/1`, step 41):

```
HTTP/1.1 401 Unauthorized
{"error":"missing X-Beacon-Dataset header"}
```

Beacon accepts `/v1/traces` and answers 200, so a run that used it still
passes. The task's fixture records which endpoint the final configuration
exports to.

## Which pages were opened  `[new]`

Expert runs that fetched the page at least once, of 8.

| Page | `main` | `pr-412` | Note |
|---|---|---|---|
| `/quickstart` | 8 | 8 | |
| `/quickstart/<language>` | not in bundle | 8 | added by `pr-412` |
| `/reference/headers` | 1 | 4 | in `pr-412`, always after the run's first 401 |
| `/troubleshooting` | 2 | 6 | |
| `/migrating-to-v2` | not in bundle | 1 | added by `pr-412`; the only page in it that names `/v2/otlp` |

In `main`, `/quickstart` names both `/v2/otlp` and `X-Beacon-Dataset`. In
`pr-412` neither string is on `/quickstart` or `/quickstart/<language>`.
(Proctor searched the two bundles for the strings the suite declares; it did
not read the pages.)

## The novice agent's failed runs

8 of 12 runs with `pr-412` failed. In 7 of them `telemetry-received` failed,
and the last answer from Beacon in the transcript was the 401 above. With
`main`, 3 of 12 failed; in 1 the last answer was that 401.

## Runs to read first  `[new]`

| Why | Run | Calls | Tokens | Start at |
|---|---|---|---|---|
| Cheapest pass with `pr-412` | `codex.pr-412/express/1` | 51 | 492k | step 1 |
| Costliest with `pr-412` | `codex.pr-412/aspnet/1` | 90 | 911k | steps 41 to 67: nine 401s |
| Same agent and task with `main` | `codex.main/aspnet/1` | 61 | 610k | step 38: first export, accepted |
| The one run that failed | `codex.pr-412/chi/1` | 88 | 869k | step 79: `forced-exception-seen` |
| A typical novice failure | `novice.pr-412/express/2` | 34 | | step 22 to the end |

`proctor steps <run> 41-67` prints those steps.

## What you can do with this

**Ask your agent.** Hand it this file and the experiment directory. Every
run above is a path under `runs/20270114-0932-otel-setup-k3vd/`, and
`events.jsonl` there holds every event counted on this page with its run and
step. A question that works: "what in the docs invited each of these?"

**Read the runs yourself.** The five above, in that order, are about 320
tool calls. The table says where to start in each.

**Ship `pr-412`.** This report supports that only for outcome: 7 of 8
against 8 of 8 is no detectable difference. It does not support it for the
other three rows.

**Hold `pr-412`.** Three statements on this page are counts anyone can
reproduce with `proctor grade` and `proctor report` on the directory above,
with no model involved:

1. With `pr-412`, 7 of 8 expert runs configured the retired endpoint;
   with `main`, 1 of 8.
2. With `pr-412`, every expert run used more tokens than the same agent on
   the same task with `main`.
3. The strings `/v2/otlp` and `X-Beacon-Dataset` are on the quickstart in
   `main` and not in `pr-412`.

## What this report cannot tell you

- Whether the 6 pages `pr-412` changed are better written. Nothing here
  reads prose.
- Whether an outcome difference under about 45 points exists for the expert
  agents. Eight pairs cannot see one.
- Whether these results hold for other languages or frameworks. Four tasks,
  four fixtures.
- Why any agent did what it did. The events say what happened and where.
- Anything about agents not listed under Arms.

## Arms, Tasks, Checks, Every run, Reproducibility and method

As today, with two changes: Arms is laid out as agent by bundle, and the
method section states the unit of each comparison (here, an agent on a task:
8 pairs for the expert rows, 4 tasks for the novice row).
