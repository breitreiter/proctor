# smoke — 20260917-1432-smoke-k7px

Eval smoke, run 2026-09-17 on imp. 2 arms: floor (imp-qcoder / qwen-coder through nb, 3 samples per case); b (imp-glm / glm through nb, 3 samples per case). 3 cases, 18 cells planned, 17 analysed.

With 3 paired cases this experiment can reliably detect a difference of about 81 points. To detect 10 points you need about 200 cases. b vs floor: +33 pts [−31, +75], no detectable difference (won 2, lost 0, tied 1 of 3).

> **Warning.** Arms lost runs unequally: floor analysed 8 of 9, b analysed 9 of 9. Rates are over the analysed cells; see Accounting for what was excluded and why.

## Headline

| Arm | Analysed / attempted | Pass rate [95% CI] (cells) | vs floor [95% CI] | Verdict |
|---|---|---|---|---|
| `floor` | 8 / 9 | 56% [15, 90] (5/8) | — | reference |
| `b` | 9 / 9 | 89% [35, 99] (8/9) | +33 pts [−31, +75] | no detectable difference |

## Accounting

| Arm | Planned | Attempted | Completed | Graded | Analysed |
|---|---|---|---|---|---|
| `floor` | 9 | 9 | 8 | 8 | 8 |
| `b` | 9 | 9 | 9 | 9 | 9 |

Excluded cells, in the accounting and out of the rates:

- `floor/uses-bash/2` failed: sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed

## Case by arm

● pass, ○ fail, × not analysed; one glyph per sample. Reasons are in the runs table.

| Case | `floor` | `b` |
|---|---|---|
| `loops` | ● ● ○ | ● ● ● |
| `plain` | ● ● ● | ● ● ● |
| `uses-bash` | ○ × ○ | ● ○ ● |

## Checks

| Check | Role | `floor` | `b` |
|---|---|---|---|
| `exit_ok` | headline | 89% [35, 99] (7/8) | 100% [44, 100] (9/9) |
| `builds` | headline | 56% [15, 90] (5/8) | 89% [35, 99] (8/9) |
| `no_denials` | guardrail | 100% [44, 100] (8/8) | 100% [44, 100] (9/9) |

## Exit reasons

| Exit reason | `floor` | `b` |
|---|---|---|
| `ok` | 7 (88%) | 9 (100%) |
| `max_tool_calls` | 1 (13%) | 0 (0%) |

## Cost and effort

Durations in min, wall time of the cell including hooks. Cost is not shown: nb's trailer does not carry it.

| Arm | Duration median | p90 | mean | range | Tokens median | p90 | mean | range |
|---|---|---|---|---|---|---|---|---|
| `floor` | 16.9 | 23.5 | 17.7 | 13.5–23.5 | 47,200 | 67,200 | 51,700 | 41,200–67,200 |
| `b` | 17.3 | 18.2 | 17.3 | 16.5–18.2 | 63,000 | 65,000 | 63,000 | 61,000–65,000 |

## Runs

Failures first.

| Arm | Case | Sample | Status | Exit | Pass | Duration | Tokens | Run | Reason |
|---|---|---|---|---|---|---|---|---|---|
| `b` | `uses-bash` | 2 | completed | ok | ○ | 17.3 | 63,000 | `bu20000000000000` | builds: 2 of 41 tests failed |
| `floor` | `loops` | 3 | completed | max_tool_calls | ○ | 23.5 | 47,200 | `fl30000000000000` | exit_ok: exit_reason=max_tool_calls |
| `floor` | `uses-bash` | 1 | completed | ok | ○ | 13.5 | 41,200 | `fu10000000000000` | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 3 | completed | ok | ○ | 16.9 | 47,200 | `fu30000000000000` | builds: 2 of 41 tests failed |
| `floor` | `uses-bash` | 2 | failed |  | × | 15.2 |  | `fu20000000000000` | sample setup hook failed: hooks/reset-fixture.sh exited 1: clone failed |
| `b` | `loops` | 1 | completed | ok | ● | 16.5 | 61,000 | `bl10000000000000` |  |
| `b` | `loops` | 2 | completed | ok | ● | 17.3 | 63,000 | `bl20000000000000` |  |
| `b` | `loops` | 3 | completed | ok | ● | 18.2 | 65,000 | `bl30000000000000` |  |
| `b` | `plain` | 1 | completed | ok | ● | 16.5 | 61,000 | `bp10000000000000` |  |
| `b` | `plain` | 2 | completed | ok | ● | 17.3 | 63,000 | `bp20000000000000` |  |
| `b` | `plain` | 3 | completed | ok | ● | 18.2 | 65,000 | `bp30000000000000` |  |
| `b` | `uses-bash` | 1 | completed | ok | ● | 16.5 | 61,000 | `bu10000000000000` |  |
| `b` | `uses-bash` | 3 | completed | ok | ● | 18.2 | 65,000 | `bu30000000000000` |  |
| `floor` | `loops` | 1 | completed | ok | ● | 20.2 | 41,200 | `fl10000000000000` |  |
| `floor` | `loops` | 2 | completed | ok | ● | 21.9 | 44,200 | `fl20000000000000` |  |
| `floor` | `plain` | 1 | completed | ok | ● | 13.5 | 61,200 | `fp10000000000000` |  |
| `floor` | `plain` | 2 | completed | ok | ● | 15.2 | 64,200 | `fp20000000000000` |  |
| `floor` | `plain` | 3 | completed | ok | ● | 16.9 | 67,200 | `fp30000000000000` |  |

## Reproducibility

- proctor: `0.1.0`
- nb: `1.0.0 at /usr/local/bin/nb`
- eval hash: `sha256:9c1e0000`
- repository: `3f2c1e9a`
- command: `proctor run smoke`
- created: `2026-09-17T14:32:00Z`

Each case is scored as its mean over its analysed samples; n is the case count. Per-arm rates: Wilson 95%. Paired differences: Newcombe 95% (Wilson square-and-add, phi from the per-case scores). MDE at 80% power assumes a per-case paired-difference sd of 0.5. Durations are the cell's wall time including hooks; tokens are nb's trailer. No multiplicity adjustment; 1 comparison shown.
