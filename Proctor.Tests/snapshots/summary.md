# smoke

Thursday, 17 September 2026, started 14:32 UTC

Can a local coder model make a small change to a .NET repository so that it builds and the tests pass?

**Against the baseline pinned 14 Sep 2026**

| Variant | Passed | 95% interval | Change against the baseline | 95% interval |
|---|---|---|---|---|
| floor (reference) | 56% | 15% to 90% | −22 points, **worse** (unconfirmed) | −67 to +39 |
| b | 89% | 35% to 99% | +11 points, **better** (unconfirmed) | −46 to +63 |

**floor**: lower on loops (−33) and uses-bash (−33); same on plain.

**b**: higher on uses-bash (+33); same on loops and plain.

**Why unconfirmed:** with 3 tasks this report can only confirm a change of about 81 points. Confirming a 10-point change takes about 200 tasks.

**Read floor with care:** it lost 2 of its 9 attempts and b lost none, so floor's rate rests on fewer attempts. See Attempt summary.

| | |
|---|---|
| Host | bench |
| Variants | 2: `floor` (the reference), `b` |
| Tasks | 3, each attempted 3 times per variant |
| Attempts | 18 planned, 16 counted |

## Variants

A variant is one configuration under test: a harness, a provider and a model, given every task. The first variant is the reference the others are compared with.

| Variant | What it is | Harness | Provider | Model | Attempts per task |
|---|---|---|---|---|---|
| `floor` | the local floor | nb | local-qcoder | qwen-coder | 3 |
| `b` |  | nb | local-glm | glm | 3 |

## Tasks

A task is one input and one desired outcome, given to every variant: a prompt against a fixture repository, with the checks that say whether the outcome was reached. Each task is attempted 3 times per variant, so a score is not one lucky or unlucky attempt.

A check is one yes-or-no test over a finished attempt. Every task carries the checks the exam declares; a fixture or the task itself can add more. A check's role decides what it does to an attempt:

| | |
|---|---|
| headline | Together, the headline checks decide whether an attempt passed. |
| validity | Decides whether an attempt counts at all: an attempt that fails one is left out of every rate, not counted as a failure. |
| guardrail | Reported, not part of the pass. |

A check that cannot decide an attempt leaves that attempt out of its rate on both sides.

**Checks the exam declares, carried by every task**

| Check | What it tests | Role |
|---|---|---|
| exit_ok | the attempt ends with 'ok' | headline |
| builds | the repository builds after the change | headline |
| no_denials | no denied tool calls | validity |

Each task's results show one mark per attempt: ● passed, ○ failed, ? undecided, × not counted. Hover a mark for the reason; click it for the attempt. A task scores its mean over its counted, decided attempts, and is called better or worse than its baseline beyond 10 points either way.

### loops

The model repeats a bash command until nb nudges it out of the loop

Fixture **note** · Attempted **3 times** per variant · **3 checks**: 3 from the exam, none of its own

**Results**

| Variant | Attempts | Score | Passed / decided | vs baseline (100%) | Verdict |
|---|---|---|---|---|---|
| floor | ● ● ○ | **67%** | 2 of 3 | −33 points | worse |
| b | ● ● ● | **100%** | 3 of 3 | ±0 points | same |

- floor attempt 3 failed: exit_ok: exit_reason=max_tool_calls

### plain

The model answers in one turn with no tools

Fixture **note** · Attempted **3 times** per variant · **3 checks**: 3 from the exam, none of its own

**Results**

| Variant | Attempts | Score | Passed / decided | vs baseline (100%) | Verdict |
|---|---|---|---|---|---|
| floor | ● ● × | **100%** | 2 of 2 | ±0 points | same |
| b | ● ● ● | **100%** | 3 of 3 | ±0 points | same |

- floor attempt 3 not counted: no_denials: 1 denied call: bash (no-match)

### uses-bash

The model runs one bash command and answers

Fixture **note** · Attempted **3 times** per variant · **4 checks**: 3 from the exam, 1 of its own

**Checks beyond the exam's**

| Check | What it tests | Role | Declared by |
|---|---|---|---|
| used-bash | uses bash | guardrail | this task |

**Results**

| Variant | Attempts | Score | Passed / decided | vs baseline (33%) | Verdict |
|---|---|---|---|---|---|
| floor | ○ × ○ | **0%** | 0 of 2 | −33 points | worse |
| b | ● ○ ● | **67%** | 2 of 3 | +33 points | better |

- floor attempt 1 failed: builds: 2 of 41 tests failed
- floor attempt 2 never completed: attempt setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed
- floor attempt 3 failed: builds: 2 of 41 tests failed
- b attempt 2 failed: builds: 2 of 41 tests failed

## Results

Pass rate is the share of counted attempts in which every headline check held, averaged task by task so that one task with many attempts does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with 3 tasks it is wide.

| Variant | Pass rate | 95% interval | Passed / decided attempts |
|---|---|---|---|
| `floor` | 56% | 15% to 90% | 4 / 7 |
| `b` | 89% | 35% to 99% | 8 / 9 |

**Against the reference variant, `floor`.** Each variant's pass rate minus `floor`'s, task by task. The verdict is the direction of the difference, confirmed only when the whole 95% interval is above or below zero; unconfirmed, with 3 tasks, it could be noise.

| Variant | Difference from `floor` | 95% interval | Tasks better / worse / same | Verdict |
|---|---|---|---|---|
| `b` | +33 points | −31 to +75 | 2 / 0 / 1 | **better** (unconfirmed) |

With 3 paired tasks this report can reliably detect a difference of about 81 points. To detect 10 points you need about 200 tasks.

**Against the baseline.** The baseline is the score pinned for each task on 2026-09-14, scores as pinned. The verdict compares each variant's score with it and calls anything more than 10 points either way a change; it is confirmed only when the whole 95% interval agrees, so a small number of tasks cannot hide behind the verdict.

| Variant | Difference from baseline | 95% interval | Tasks better / worse / same | Verdict |
|---|---|---|---|---|
| `floor` | −22 points | −67 to +39 | 0 / 2 / 1 | **Worse** (unconfirmed) |
| `b` | +11 points | −46 to +63 | 1 / 0 / 2 | **Better** (unconfirmed) |

How each task scored against its baseline, and why attempts failed, is shown with the task under Tasks.

## Failures

For each variant, the checks that failed in at least one counted attempt, most frequent first, with the tasks they failed on. A check that could not decide an attempt, or could not grade it at all, is listed apart: that is the check's weakness, not the variant's.

**`floor`** (the local floor) failed 2 checks:

- **the repository builds after the change** (`builds`, headline) failed in 3 of 7 attempts: twice on uses-bash, once on loops.
- **the attempt ends with 'ok'** (`exit_ok`, headline) failed in 1 of 7 attempts: once on loops.

**`b`** failed 1 check:

- **the repository builds after the change** (`builds`, headline) failed in 1 of 9 attempts: once on uses-bash.

## Attempt summary

This is how far each attempt got before terminating. *Started* attempts began; *completed* attempts ended with a transcript; *graded* attempts have their checks; *counted* attempts are graded and passed every validity check; *decided* attempts are counted attempts whose headline checks all reached a verdict. Pass rates are over decided attempts only.

| Variant | Planned | Started | Completed | Graded | Counted | Decided |
|---|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 7 | 7 |
| `b` | 9 | 9 | 9 | 9 | 9 | 9 |

> **Warning.** Variants lost attempts unequally: `floor` decided 7 of 9, `b` decided 9 of 9. The rates are over decided attempts; the attempts lost are listed here.

Attempts left out, and why:

- floor/plain/3: not counted, failed a validity check. no_denials: 1 denied call: bash (no-match)
- floor/uses-bash/2: never completed. attempt setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed

How each completed attempt ended, per variant. `ok` is a normal finish; anything else is the harness stopping the attempt (a limit, an error from the provider), which the checks then grade like any other.

| Variant | `ok` | `max_tool_calls` |
|---|---|---|
| `floor` | 7 | 1 (`loops` attempt 3) |
| `b` | 9 | 0 |

## Check pass rates

How often each check held, per variant, over the attempts it applies to. Headline and guardrail checks are over counted attempts; a validity check is over every graded attempt, because it says how many were counted. An attempt the check could not decide is out of its rate and counted beside it.

| Check | What it tests | Role | `floor` | `b` |
|---|---|---|---|---|
| `exit_ok` | the attempt ends with 'ok' | headline | 89% (6/7) | 100% (9/9) |
| `builds` | the repository builds after the change | headline | 56% (4/7) | 89% (8/9) |
| `no_denials` | no denied tool calls | validity | 89% (7/8) | 100% (9/9) |
| `used-bash` | uses bash | guardrail | 100% (2/2) | 100% (3/3) |

## Cost and effort

Per completed attempt. Duration is wall time in minutes, hooks included; tokens are the attempt's own count. Cost is not shown: no variant's harness reports one.

| Variant | Duration (min): median | p90 | mean | range | Tokens: median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Every attempt

Every attempt, failures first. *Reason* is the first check that did not hold and what it saw, or why the attempt never completed.

| Variant | Task | Attempt | Result | Ended | Duration (min) | Tokens | Reason |
|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | ○ failed | `ok` | 17.3 | 63,000 | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | ○ failed | `max_tool_calls` | 23.5 | 47,200 | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | ○ failed | `ok` | 13.5 | 41,200 | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | ○ failed | `ok` | 16.9 | 47,200 | builds: 2 of 41 tests failed |
| `floor` | `plain` | 3 | × not counted | `ok` | 16.9 | 67,200 | no_denials: 1 denied call: bash (no-match) |
| `floor` | `uses-bash` | 2 | × never completed |  | 15.2 |  | attempt setup hook failed: `hooks/reset-fixture.sh` exited 1: clone failed |
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
| batch | 20260917-1432-smoke-k7px |
| proctor | 0.1.0 |
| nb | 1.0.0 at `/usr/local/bin/nb` |
| exam hash | sha256:9c1e0000 |
| repository | 3f2c1e9a |
| command | `proctor run smoke` |
| created | 2026-09-17T14:32:00Z |

Each task is scored as its mean over its counted, decided attempts, so n in every interval is the number of tasks. Per-variant rates use the Wilson 95% interval. Differences between variants, and against the baseline, use Newcombe's paired method (Wilson square-and-add, with phi from the per-task scores). The detectable difference assumes 80% power and a per-task paired-difference sd of 0.5. Durations are the attempt's wall time including hooks; tokens are the attempt's own count. No multiplicity adjustment; 1 comparison shown. The baseline verdict is the point estimate against a tolerance of 10 points, for each variant and each task; a variant's verdict is confirmed only when its whole 95% interval lies beyond the tolerance on the same side (within it, for unchanged).
