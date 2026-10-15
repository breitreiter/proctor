---
type: bug
title: grade cannot be limited to one arm, so a paid judge pass cannot be staged
created: 2026-10-15
status: open
found-by: a suite outside this tree with two arms on a free local model and judges that cost money
---

# grade cannot be limited to one arm, so a paid judge pass cannot be staged

`proctor grade <experiment>` grades every completed cell. When the judges cost
money and the arms do not, the natural plan is to generate every arm in one
experiment, so the report compares them, and then grade one arm first to see
whether the judges hold up before paying for the rest. There is no way to say
that: `--arm` and `--tasks` are accepted by `baseline` only.

## The workaround in use

Move the other arm's cell directories out of `runs/<experiment>/` before
grading and back afterwards. `grade` enumerates cells from the suite and skips
any whose status is not completed, so the moved cells are reported as not
graded rather than failed. It works, but it relies on how `grade` finds cells,
and a report written while the cells are away would describe a different
experiment.

The alternative, one experiment per arm, loses the paired comparison in the
report, which is the point of putting two arms in one suite.

## What would fix it

`--arm <id>` (repeatable) and `--tasks <a,b>` on `grade`, as on `baseline`:
grade only those cells and leave the others' `checks.json` as they are. `report`
should then say which arms' cells are ungraded rather than counting them as
missing.
