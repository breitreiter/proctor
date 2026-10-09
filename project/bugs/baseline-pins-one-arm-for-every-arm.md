---
type: bug
title: A baseline pins one arm, so a suite that reruns the same arms under a new condition cannot compare arm to arm
created: 2026-10-09
status: open
found-by: a downstream suite, planning a variant of itself with tools enabled
---

# A baseline pins one arm, so a suite that reruns the same arms under a new condition cannot compare arm to arm

`baseline <experiment> --arm <id>` pins one arm's cells per task, and `report`
compares **every** arm of the new experiment with those same scores. That
answers "is each arm better than what we had?". It cannot answer "did the new
condition help each arm?", which needs each arm paired with its own earlier
run.

Strictly this is a missing capability rather than a defect. It is filed here
because the workaround is error-prone.

## What happens

Suite `a` has five arms, each giving the model the same input in a different
format (`a1` to `a5`). The model reads the input in context, with no tools. One
of its experiments is pinned with `--arm a3`.

Suite `b` reruns the same five arms with a shell, to ask whether computing over
the input removes the format effect. Its question is per arm: `a1` with a shell
against `a1` without one, and so on. A program is
per-suite and the shell changes the program, so this has to be a second suite
with the same task ids. Comparing across them already works through a copied
`baseline.json` (see `baseline-missing-pass-check-scores-zero.md` for the one
trap). But the copy holds one arm, so four of the five sandbox arms are
compared with `a3`, not with themselves.

The workaround is a loop: for each arm, `baseline <experiment> --arm <arm>`, copy the
file into the second suite, `report`, keep that arm's row, and repeat. It takes
five reports to read one comparison, every report except the one matching row
is misleading, and `baseline.json` ends up as whichever arm was pinned last.

## What we want instead

**A baseline that can pin per arm.** For example, `baseline <experiment>
--arm a --as a` repeated, or `--all-arms`, writing pins keyed by arm then task.
At report time, an arm with its own pin is compared with it, and an arm without
one falls back to the suite-wide pin, as now. The guard section would then
read "a1 vs its baseline", not "vs baseline".

Any suite that reruns the same arms under a changed condition (a new model
version, a new bundle, tools on or off) needs this. Without it the choices are
the loop above or putting both conditions in one suite, which a per-suite
program rules out.
