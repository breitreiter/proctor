# smoke — 20260917-1432-smoke-k7px

Can a local coder model make a small change to a .NET repository so that it builds and the tests pass?

| | |
|---|---|
| Eval | `smoke` |
| Run | 2026-09-17 on `imp` |
| Arms | 2: `floor` (the reference), `b` |
| Cases | 3, each run 3 times per arm |
| Runs | 18 planned, 16 counted |
| Result | `floor` (the reference) passed 56% of its runs; `b` 89%. With 3 cases the difference between `b` and `floor` (+33 points) is not statistically detectable. Against the pinned baseline, `floor` regressed and `b` improved at a tolerance of 10 points. The arms lost runs unequally; see What ran. |

## Arms

An arm is one configuration under test: a harness, a provider and a model, run over every case. The first arm is the reference the others are compared with.

| Arm | What it is | Harness | Provider | Model | Runs per case |
|---|---|---|---|---|---|
| `floor` | the local floor | nb | imp-qcoder | qwen-coder | 3 |
| `b` |  | nb | imp-glm | glm | 3 |

## Cases

A case is one task, given to every arm: a prompt against a fixture repository. Each case is run 3 times per arm, so a score is not one lucky or unlucky attempt.

| Case | What it asks |
|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop |
| `plain` | The model answers in one turn with no tools |
| `uses-bash` | The model runs one bash command and answers |

## Checks

A check is one yes-or-no test over a finished run. The headline checks together decide whether a run passed. A validity check decides whether a run counts at all: a run that fails one is left out of every rate, not counted as a failure. Any other check is a guardrail: reported, not part of the pass. A check that cannot decide a run leaves that run out of its rate on both sides.

| Check | What it tests | Role |
|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline |
| `builds` | the repository builds after the change | headline |
| `no_denials` | no denied tool calls | validity |

## Results

Pass rate is the share of counted runs in which every headline check held, averaged case by case so that one case with many runs does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with 3 cases it is wide.

| Arm | Pass rate | 95% interval | Passed / decided runs |
|---|---|---|---|
| `floor` | 56% | 15% to 90% | 4 / 7 |
| `b` | 89% | 35% to 99% | 8 / 9 |

**`b` against `floor`.** `b` passed 33 points more of its runs than `floor`. The 95% interval on that difference runs from −31 to +75 points, so with 3 cases the difference could be noise: no detectable difference. Case by case, `b` did better on 2, worse on 0, the same on 1.

With 3 paired cases this experiment can reliably detect a difference of about 81 points. To detect 10 points you need about 200 cases.

**Against the baseline.** The baseline is the score pinned for each case on 2026-09-14, scores as pinned. The verdict compares each arm's score with it and calls anything more than 10 points either way a change; the interval is shown so a small number of cases cannot hide behind the verdict.

| Arm | Difference from baseline | 95% interval | Cases better / worse / same | Verdict |
|---|---|---|---|---|
| `floor` | −22 points | −67 to +39 | 0 / 2 / 1 | regressed |
| `b` | +11 points | −46 to +63 | 1 / 0 / 2 | improved |

| Case | Baseline | `floor` | `b` |
|---|---|---|---|
| `loops` | 100% | 67% | 100% |
| `plain` | 100% | 100% | 100% |
| `uses-bash` | 33% | 0% | 67% |

## Failures

For each arm, the checks that failed in at least one counted run, most frequent first, with the cases they failed on. A check that could not decide a run is listed apart: that is the check's weakness, not the arm's.

**`floor`** (the local floor) failed 2 checks:

- **the repository builds after the change** (`builds`, headline) failed in 3 of 7 runs: twice on `uses-bash`, once on `loops`.
- **nb exits with 'ok'** (`exit_ok`, headline) failed in 1 of 7 runs: once on `loops`.

**`b`** failed 1 check:

- **the repository builds after the change** (`builds`, headline) failed in 1 of 9 runs: once on `uses-bash`.

## What ran

Every run planned by the matrix, and how far it got. *Attempted* runs started; *completed* runs ended with a transcript from nb; *graded* runs have their checks; *counted* runs are graded and passed every validity check; *decided* runs are counted runs whose headline checks all reached a verdict. Pass rates are over decided runs only.

| Arm | Planned | Attempted | Completed | Graded | Counted | Decided |
|---|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 7 | 7 |
| `b` | 9 | 9 | 9 | 9 | 9 | 9 |

> **Warning.** Arms lost runs unequally: `floor` decided 7 of 9, `b` decided 9 of 9. The rates are over decided runs; the runs lost are listed here.

Runs left out, and why:

- `floor/plain/3`: not counted, failed a validity check. no_denials: 1 denied call: bash (no-match)
- `floor/uses-bash/2`: never completed. sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed

How nb ended each completed run, per arm. `ok` is a normal finish; anything else is nb stopping the run, which the checks then grade like any other.

| Arm | `ok` | `max_tool_calls` |
|---|---|---|
| `floor` | 7 | 1 (`loops` run 3) |
| `b` | 9 | 0 |

## Results by case

One row per case, one column per arm, one mark per run: ● passed, ○ failed, ? undecided, × not counted. Hover a mark for the reason; click it for the run.

| Case | What it asks | `floor` | `b` |
|---|---|---|---|
| `loops` | The model repeats a bash command until nb nudges it out of the loop | ● ● ○ | ● ● ● |
| `plain` | The model answers in one turn with no tools | ● ● × | ● ● ● |
| `uses-bash` | The model runs one bash command and answers | ○ × ○ | ● ○ ● |

## Check pass rates

How often each check held, per arm, over the runs it applies to. Headline and guardrail checks are over counted runs; a validity check is over every graded run, because it says how many were counted. A run the check could not decide is out of its rate and counted beside it.

| Check | What it tests | Role | `floor` | `b` |
|---|---|---|---|---|
| `exit_ok` | nb exits with 'ok' | headline | 89% (6/7) | 100% (9/9) |
| `builds` | the repository builds after the change | headline | 56% (4/7) | 89% (8/9) |
| `no_denials` | no denied tool calls | validity | 89% (7/8) | 100% (9/9) |

## Cost and effort

Per completed run. Duration is wall time in minutes, hooks included; tokens are what nb reported. Cost is not shown: nb does not report it.

| Arm | Duration (min): median | p90 | mean | range | Tokens: median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Every run

Every run, failures first. *Reason* is the first check that did not hold and what it saw, or why the run never completed.

| Arm | Case | Run | Result | nb ended | Duration (min) | Tokens | Reason |
|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | ○ failed | `ok` | 17.3 | 63,000 | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | ○ failed | `max_tool_calls` | 23.5 | 47,200 | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | ○ failed | `ok` | 13.5 | 41,200 | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | ○ failed | `ok` | 16.9 | 47,200 | builds: 2 of 41 tests failed |
| `floor` | `plain` | 3 | × not counted | `ok` | 16.9 | 67,200 | no_denials: 1 denied call: bash (no-match) |
| `floor` | `uses-bash` | 2 | × never completed |  | 15.2 |  | sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed |
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
| proctor | `0.1.0` |
| nb | `1.0.0 at /usr/local/bin/nb` |
| eval hash | `sha256:9c1e0000` |
| repository | `3f2c1e9a` |
| command | `proctor run smoke` |
| created | `2026-09-17T14:32:00Z` |

Each case is scored as its mean over its counted, decided runs, so n in every interval is the number of cases. Per-arm rates use the Wilson 95% interval. Differences between arms, and against the baseline, use Newcombe's paired method (Wilson square-and-add, with phi from the per-case scores). The detectable difference assumes 80% power and a per-case paired-difference sd of 0.5. Durations are the run's wall time including hooks; tokens are what nb reported. No multiplicity adjustment; 1 comparison shown. The baseline verdict is the point estimate against a tolerance of 10 points; the interval is shown so a small n cannot hide.
