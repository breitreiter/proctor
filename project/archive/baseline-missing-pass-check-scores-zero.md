---
type: bug
title: A baseline pin graded without one of the suite's pass checks recomputes to 0, silently
created: 2026-10-09
status: fixed
fixed: 2026-10-10, 0.2.4
found-by: a downstream suite, planning a second suite to compare against the first's baseline
---

# A baseline pin graded without one of the suite's pass checks recomputes to 0, silently

When `report` resolves a baseline, it rereads each pinned cell's `checks.json`
and scores it under the **reading** suite's `pass` list. If that list names a
check the pinned cell was never graded on, `Grade.Pass` reads the missing
check as an error, the cell scores 0, and the task's baseline score is 0. The
pinned score in `baseline.json` is never consulted, because the cell existed
and was "decided".

## What happens

`Grade/Grade.cs`, `Pass`:

```csharp
var results = passChecks.Select(name => checks.TryGetValue(name, out var v) ? v.Result : Verdict.Error).ToList();
if (results.Any(r => r is Verdict.Fail or Verdict.Error)) return false;
```

`Suite/Baseline.cs`, `Resolve`, uses the reading suite's lists:

```csharp
var decided = verdicts.Where(v => Grade.Invalid(v!, suite.Grading.Validity) is null && Grade.Pass(v!, suite.Grading.Pass!) is not null).ToList();
if (decided.Count > 0) { scores[taskId] = decided.Average(...); recomputed++; continue; }
```

`Pass` returns `false`, not `null`, so the cell counts as decided and the
fallback to `pin.Score` never runs. The report then says the baseline was
"recomputed", which reads as the more trustworthy of its two sources.

Two ways to reach it:

- **The suite's own pass list grows.** Add a check to `grading.pass` after
  pinning, and every pinned task drops to 0 at the next `report` until someone
  re-grades the pinned experiment.
- **A baseline copied into a second suite.** Suite `a` runs with no tools and
  pins one of its arms. Suite `b` runs the same task ids with a shell, and
  copying `baseline.json` across is otherwise the way to compare them;
  `Resolve` finds cells by experiment, arm, task and sample, and pairing is by
  task id. If suite `b` puts a shell-only check such as "used the shell" in
  `pass`, the no-tools baseline scores 0 on every task and every arm of `b`
  reads as a large, confirmed improvement.

Validity checks do not have the problem: `Invalid` only looks at checks that
are present.

## Why it matters

It produces a confident, wrong number with no warning, in the direction that
flatters whatever is being compared with the baseline. The guard comparison
exists to catch regressions, and this hides them.

## What we want instead

**A pass check the pinned cell was not graded on makes that cell undecided for
the baseline, not failed.** Then `Resolve` falls back to the pinned score (and
says "as pinned" or "partly recomputed"), and the report can name the missing
check. Within an experiment a missing check is still an error, as now: there
it means grading went wrong.

A warning from `report` naming the check and the pinned experiment would make
the fallback visible.

## Resolution

Fixed in 0.2.4, in `Baseline.Resolve` rather than `Grade.Pass`, so a missing
pass check inside an experiment is still a failure. A task whose pinned cells
lack any current pass check is not rescored: it keeps `pin.Score`, which
makes the source "as pinned" or "partly recomputed". The check comes back as a
gap (`stats.json` `baseline.gaps`: check, experiment, tasks), which `report`
prints as a warning and the report shows as a warning band at the top. The
pinned score was decided by the pass list of its time, and the warning says
so, because for a baseline copied from another suite that is a different
question.
