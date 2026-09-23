---
type: bug
title: A decide check's reason omits the expected answer, so a failure reads as a pass
created: 2026-09-23
status: open
found-by: the design-system docs-qa suite, reading its first 0.2.2 report
---

# A decide check's reason omits the expected answer, so a failure reads as a pass

A `decide` check's reason is the model's label and its probability, and
nothing else: `yes p=0.98`. Whether `yes` is the answer that passes depends on
the check's `expect`, and the reason does not say. For a check that expects
`false`, the reason printed on a *failed* run is `yes p=0.98`, which reads as
confident agreement.

## What happens

The design-system suite checks an exact list by asking Jev about extras, so the
question is phrased so that *no* passes:

```json
"decide": {
  "ask": "Apart from animation, aspect, easing, filter, framework-default, letter-spacing, perspective, does the answer present any other token dimension as entirely inherited from Tailwind?",
  "expect": false,
  "threshold": 0.8,
  "with": "jev"
}
```

In experiment `20260923-1756-docs-qa-3zmd` the agent listed 17 dimensions,
including color and spacing. Jev answered yes, the check failed on all three
runs, and every place the report gives a reason shows:

```
answer: all 7 present; yes p=0.98
```

That is in the task card's failure bullets, the *Every run* table, and
`checks.json`. A reader sees "all 7 present" and "yes, 98%" and concludes the
run passed. To find out why it failed, you have to open the task file and read
`expect`.

The string is built in `Grade/Judge.cs`, in the systemone path:

```csharp
: Verdict.Of(label == expected, $"{label} p={p:0.00}");
```

`expected` is in scope and is used to decide the verdict, but it is not put in
the reason.

## Why it matters

A reason exists to tell a reader why a run failed without opening its files.
This one only gives a correct impression when the pass answer happens to be
`yes`. A suite author who writes a question the natural way for an exact list
("does it name anything else?") gets a report that looks wrong on every failure.
The only workaround is to rephrase the question so that yes means pass, which
makes it harder for the judge to read.

## What we want instead

**A decide reason says which answer was expected whenever the label is not that
answer.** For example:

- fail: `yes p=0.98, expected no`
- pass: `no p=0.93` (unchanged; nothing to explain)
- below threshold: `yes p=0.70 (below 0.80), expected no`

A shorter form of the question would help further, but is optional. The
expected answer is the part that is missing. The `choice` path has the same
shape (`{chosen} p=…` when the chosen option is not the expected one) and should
be treated the same way.

`JudgeTests.cs` pins the current strings (`"yes p=0.98"`), so those cases will
need updating alongside the fix.
