# smoke

Thursday, 17 September 2026, started 14:32 UTC

Can a local coder model make a small change to a .NET repository so that it builds and the tests pass?

**Against the baseline pinned 14 Sep 2026**

- **floor** (reference): **Worse** (unconfirmed) −22 points; 95% interval −67 to +39 points. Lower on loops (−33) and uses-bash (−33); same on plain.
- **b**: **Better** (unconfirmed) +11 points; 95% interval −46 to +63 points. Higher on uses-bash (+33); same on loops and plain.

**Why unconfirmed:** with 3 tasks this run can only confirm a change of about 81 points. Confirming a 10-point change takes about 200 tasks. For the same reason, **b** passing 33 points more than **floor** is not a detectable difference.

**Read floor with care:** it lost 2 of its 9 runs and b lost none, so floor's rate rests on fewer runs. See What ran.

| | |
|---|---|
| Host | bench |
| Arms | 2: `floor` (the reference), `b` |
| Tasks | 3, each run 3 times per arm |
| Runs | 18 planned, 16 counted |

## Arms

An arm is one configuration under test: a harness, a provider and a model, run over every task. The first arm is the reference the others are compared with.

| Arm | What it is | Harness | Provider | Model | Runs per task |
|---|---|---|---|---|---|
| `floor` | the local floor | nb | local-qcoder | qwen-coder | 3 |
| `b` |  | nb | local-glm | glm | 3 |

## Tasks

A task is one input and one desired outcome, given to every arm: a prompt against a fixture repository, with the checks that say whether the outcome was reached. Each task is run 3 times per arm, so a score is not one lucky or unlucky attempt.

A check is one yes-or-no test over a finished run. Every task carries the checks the suite declares; a fixture or the task itself can add more. A check's role decides what it does to a run:

| | |
|---|---|
| headline | Together, the headline checks decide whether a run passed. |
| validity | Decides whether a run counts at all: a run that fails one is left out of every rate, not counted as a failure. |
| guardrail | Reported, not part of the pass. |

A check that cannot decide a run leaves that run out of its rate on both sides.

Each task's results show one mark per run: ● passed, ○ failed, ? undecided, × not counted. Hover a mark for the reason; click it for the run. A task scores its mean over its counted, decided runs, and is called better or worse than its baseline beyond 10 points either way.

### loops

The model repeats a bash command until nb nudges it out of the loop

Fixture **note** · Run **3 times** per arm · **3 checks**: 3 from the suite, none of its own

**Checks**

| Check | What it tests | Role | Declared by |
|---|---|---|---|
| exit_ok | nb exits with 'ok' | headline | suite |
| builds | the repository builds after the change | headline | suite |
| no_denials | no denied tool calls | validity | suite |

**Results**

| Arm | Runs | Score | Passed / decided | vs baseline (100%) | Verdict |
|---|---|---|---|---|---|
| floor | ● ● ○ | **67%** | 2 of 3 | −33 points | worse |
| b | ● ● ● | **100%** | 3 of 3 | ±0 points | same |

- floor run 3 failed: exit_ok: exit_reason=max_tool_calls

### plain

The model answers in one turn with no tools

Fixture **note** · Run **3 times** per arm · **3 checks**: 3 from the suite, none of its own

**Checks**

| Check | What it tests | Role | Declared by |
|---|---|---|---|
| exit_ok | nb exits with 'ok' | headline | suite |
| builds | the repository builds after the change | headline | suite |
| no_denials | no denied tool calls | validity | suite |

**Results**

| Arm | Runs | Score | Passed / decided | vs baseline (100%) | Verdict |
|---|---|---|---|---|---|
| floor | ● ● × | **100%** | 2 of 2 | ±0 points | same |
| b | ● ● ● | **100%** | 3 of 3 | ±0 points | same |

- floor run 3 not counted: no_denials: 1 denied call: bash (no-match)

### uses-bash

The model runs one bash command and answers

Fixture **note** · Run **3 times** per arm · **4 checks**: 3 from the suite, 1 of its own

**Checks**

| Check | What it tests | Role | Declared by |
|---|---|---|---|
| **used-bash** | uses bash | guardrail | this task |
| exit_ok | nb exits with 'ok' | headline | suite |
| builds | the repository builds after the change | headline | suite |
| no_denials | no denied tool calls | validity | suite |

**Results**

| Arm | Runs | Score | Passed / decided | vs baseline (33%) | Verdict |
|---|---|---|---|---|---|
| floor | ○ × ○ | **0%** | 0 of 2 | −33 points | worse |
| b | ● ○ ● | **67%** | 2 of 3 | +33 points | better |

- floor run 1 failed: builds: 2 of 41 tests failed
- floor run 2 never completed: sample setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed
- floor run 3 failed: builds: 2 of 41 tests failed
- b run 2 failed: builds: 2 of 41 tests failed

## Results

Pass rate is the share of counted runs in which every headline check held, averaged task by task so that one task with many runs does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with 3 tasks it is wide.

| Arm | Pass rate | 95% interval | Passed / decided runs |
|---|---|---|---|
| `floor` | 56% | 15% to 90% | 4 / 7 |
| `b` | 89% | 35% to 99% | 8 / 9 |

**`b` against `floor`.** `b` passed 33 points more of its runs than `floor`. The 95% interval on that difference runs from −31 to +75 points, so with 3 tasks the difference could be noise: no detectable difference. Task by task, `b` did better on 2, worse on 0, the same on 1.

With 3 paired tasks this experiment can reliably detect a difference of about 81 points. To detect 10 points you need about 200 tasks.

**Against the baseline.** The baseline is the score pinned for each task on 2026-09-14, scores as pinned. The verdict compares each arm's score with it and calls anything more than 10 points either way a change; it is confirmed only when the whole 95% interval agrees, so a small number of tasks cannot hide behind the verdict.

| Arm | Difference from baseline | 95% interval | Tasks better / worse / same | Verdict |
|---|---|---|---|---|
| `floor` | −22 points | −67 to +39 | 0 / 2 / 1 | **Worse** (unconfirmed) |
| `b` | +11 points | −46 to +63 | 1 / 0 / 2 | **Better** (unconfirmed) |

How each task scored against its baseline, and why runs failed, is shown with the task under Tasks.

## Failures

For each arm, the checks that failed in at least one counted run, most frequent first, with the tasks they failed on. A check that could not decide a run is listed apart: that is the check's weakness, not the arm's.

**`floor`** (the local floor) failed 2 checks:

- **the repository builds after the change** (`builds`, headline) failed in 3 of 7 runs: twice on uses-bash, once on loops.
- **nb exits with 'ok'** (`exit_ok`, headline) failed in 1 of 7 runs: once on loops.

**`b`** failed 1 check:

- **the repository builds after the change** (`builds`, headline) failed in 1 of 9 runs: once on uses-bash.

## What ran

Every run planned by the matrix, and how far it got. *Attempted* runs started; *completed* runs ended with a transcript from nb; *graded* runs have their checks; *counted* runs are graded and passed every validity check; *decided* runs are counted runs whose headline checks all reached a verdict. Pass rates are over decided runs only.

| Arm | Planned | Attempted | Completed | Graded | Counted | Decided |
|---|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 7 | 7 |
| `b` | 9 | 9 | 9 | 9 | 9 | 9 |

> **Warning.** Arms lost runs unequally: `floor` decided 7 of 9, `b` decided 9 of 9. The rates are over decided runs; the runs lost are listed here.

Runs left out, and why:

- floor/plain/3: not counted, failed a validity check. no_denials: 1 denied call: bash (no-match)
- floor/uses-bash/2: never completed. sample setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed

How nb ended each completed run, per arm. `ok` is a normal finish; anything else is nb stopping the run, which the checks then grade like any other.

| Arm | `ok` | `max_tool_calls` |
|---|---|---|
| `floor` | 7 | 1 (`loops` run 3) |
| `b` | 9 | 0 |

## Check pass rates

How often each check held, per arm, over the runs it applies to. Headline and guardrail checks are over counted runs; a validity check is over every graded run, because it says how many were counted. A run the check could not decide is out of its rate and counted beside it.

| Check | What it tests | Role | `floor` | `b` |
|---|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | 89% (6/7) | 100% (9/9) |
| `builds` | the repository builds after the change | headline | 56% (4/7) | 89% (8/9) |
| `no_denials` | no denied tool calls | validity | 89% (7/8) | 100% (9/9) |
| `used-bash` | uses bash | guardrail | 100% (2/2) | 100% (3/3) |

## Cost and effort

Per completed run. Duration is wall time in minutes, hooks included; tokens are what nb reported. Cost is not shown: nb does not report it.

| Arm | Duration (min): median | p90 | mean | range | Tokens: median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Every run

Every run, failures first. *Reason* is the first check that did not hold and what it saw, or why the run never completed.

| Arm | Task | Run | Result | nb ended | Duration (min) | Tokens | Reason |
|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | ○ failed | `ok` | 17.3 | 63,000 | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | ○ failed | `max_tool_calls` | 23.5 | 47,200 | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | ○ failed | `ok` | 13.5 | 41,200 | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | ○ failed | `ok` | 16.9 | 47,200 | builds: 2 of 41 tests failed |
| `floor` | `plain` | 3 | × not counted | `ok` | 16.9 | 67,200 | no_denials: 1 denied call: bash (no-match) |
| `floor` | `uses-bash` | 2 | × never completed |  | 15.2 |  | sample setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed |
| `b` | `loops` | 1 | ● passed | `ok` | 16.5 | 61,000 |  |
| `b` | `loops` | 2 | ● passed | `ok` | 17.3 | 63,000 |  |
| `b` | `loops` | 3 | ● passed | `ok` | 18.2 | 65,000 |  |
| `b` | `plain` | 1 | ● passed | `ok` | 16.5 | 61,000 |  |
| `b` | `plain` | 2 | ● passed | `ok` | 17.3 | 63,000 |  |
| `b` | `plain` | 3 | ● passed | `ok` | 18.2 | 65,000 |  |
| `b` | `uses-bash` | 1 | ● passed | `ok` | 16.5 | 61,000 |  |
| `b` | `uses-bash` | 3 | ● passed | `ok` | 18.2 | 65,000 |  |
| `floor` | `loops` | 1 | ● passed | `ok` | 20.2 | 41,200 |  |
| `floor` | `loops` | 2 | ● passed | `ok` | 21.9 | 44,200 |  |
| `floor` | `plain` | 1 | ● passed | `ok` | 13.5 | 61,200 |  |
| `floor` | `plain` | 2 | ● passed | `ok` | 15.2 | 64,200 |  |

## Reproducibility and method

What produced this report, so it can be run again, and how the numbers were computed.

| | |
|---|---|
| run id | 20260917-1432-smoke-k7px |
| proctor | 0.1.0 |
| nb | 1.0.0 at `/usr/local/bin/nb` |
| suite hash | sha256:9c1e0000 |
| repository | 3f2c1e9a |
| command | `proctor run smoke` |
| created | 2026-09-17T14:32:00Z |

Each task is scored as its mean over its counted, decided runs, so n in every interval is the number of tasks. Per-arm rates use the Wilson 95% interval. Differences between arms, and against the baseline, use Newcombe's paired method (Wilson square-and-add, with phi from the per-task scores). The detectable difference assumes 80% power and a per-task paired-difference sd of 0.5. Durations are the run's wall time including hooks; tokens are what nb reported. No multiplicity adjustment; 1 comparison shown. The baseline verdict is the point estimate against a tolerance of 10 points, for each arm and each task; an arm's verdict is confirmed only when its whole 95% interval lies beyond the tolerance on the same side (within it, for unchanged).
