---
type: bug
title: No model check can sort an answer into a vocabulary and grade the label against per-task lists
created: 2026-10-15
status: open
found-by: a suite outside this tree that grades plans for charts, while wiring its first checks (nothing run yet)
---

# No model check can sort an answer into a vocabulary and grade the label against per-task lists

A suite wants to put each answer in one category from a fixed vocabulary, plus
any number of tags from a second vocabulary, and then grade that against the
task's own lists: some labels pass, some fail, some are deliberately undecided,
and a label the task has no opinion on is undecided too (or fails, on some
tasks). Neither model check can do it, and a script check cannot see a model
check's result.

## What the suite needs

The answer is a short JSON plan for a chart. A judge reads the answer alone and
returns one *family* from 22 (`line`, `heatmap`, `number`, ... `other`) and a set
of *modifiers* from 10 (`small-multiples`, `log-scale`, ...). Then each task's
rubric decides:

```json
"acceptable": [ { "family": "heatmap" }, { "family": "line", "modifier": "highlight-with-context" } ],
"contested":  [ { "modifier": "small-multiples" } ],
"wrong":      [ { "family": "line", "modifier": "colour-per-series" }, { "family": "stacked-area" } ],
"unlisted":   "undecided"
```

Acceptable passes, wrong fails, contested is `needs-judge`, the most specific
entry wins, and a label in no list is `needs-judge` unless `unlisted` is
`wrong`. The undecided outcomes are the point: they are the review queue.

## Why the existing checks can't express it

- **`judge`** (chat) answers yes, no or unknown. It has no `options`, so it
  cannot return a label from a vocabulary at all.
- **`decide`** (systemone) takes `options`, but `expect` is exactly one label:
  `ValidateSpec` refuses anything but a single key of `options`. A set of
  passing labels, a set of failing ones and an undecided remainder cannot be
  written. It also returns one label, so the modifiers (a set) would need one
  `decide` per modifier, and their results have nowhere to meet.
- **A `script`** can do the matching, but it runs independently of the model
  checks in the same cell: `Grade.Cell` evaluates the checks as a dictionary,
  with no order and no way for one check to read another's verdict. Reading
  `verdicts/` from the script would depend on evaluation order.

## The workaround in use

The script calls the chat judge itself, at the endpoint `proctor.json` names,
and keeps its own cache file in the cell's `verdicts/` directory keyed on the
request. That costs proctor's evidence checking (reimplemented in the script),
its judge usage accounting (the report's judging cost leaves this check out),
`--judge a=b` remapping, and `grade --rejudge` (the script reads its own
environment variable instead).

## What would fix it

Any of these, in rough order of how much they'd cover:

1. **A `judge` with `options`**, returning a label with verbatim evidence like
   the yes/no judge does, plus an `expect` that accepts lists:
   `{ "pass": [...], "fail": [...] }`, anything else `needs-judge`. That covers
   the family. The modifiers would still need a set-valued answer.
2. **A model check whose label is handed to a script**: the script check names
   a model check it depends on (`"after": "sort"`) and receives its label and
   evidence in an environment variable. Most general; the matching stays the
   suite's business.
3. **`decide` with list-valued `expect`**, as in 1, for the systemone kind.
