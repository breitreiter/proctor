---
type: bug
title: A check with several fields lists its reasons in declaration order, so the failing part is buried
created: 2026-09-23
status: fixed
fixed: 2026-09-23, 0.2.3
found-by: the design-system docs-qa suite, reading its first 0.2.2 report
---

# A check with several fields lists its reasons in declaration order, so the failing part is buried

A check spec with more than one field (for example a `script` and a `decide`)
is evaluated as their conjunction. The combined verdict takes the worst result,
but its reason joins every field's reason in declaration order, with nothing
marking which part passed and which failed. When a passing part comes first,
the failure reads as a pass until the reader gets to the end of the line.

## What happens

`Grade/Checks.cs`, `Evaluate`:

```csharp
var worst = verdicts.MaxBy(v => v.Result switch { Verdict.Error => 3, Verdict.NeedsJudge => 2, Verdict.Fail => 1, _ => 0 })!;
return worst with { Reason = string.Join("; ", verdicts.Select(v => v.Reason)) };
```

Two examples from experiment `20260923-1756-docs-qa-3zmd`:

```
guidance-forms, run 1:  answer: yes p=0.96; read none of ['patterns/forms.md', 'patterns/forms-checklist.md']; read ['llms.txt']
fanout-inherited-dimension, run 1:  answer: all 7 present; yes p=0.98
```

In the first, the judge's verdict passed and the route script failed. The
agent answered from `llms.txt` without opening the page. The reason opens with
the passing verdict, so the part that explains the failure comes second.

The second also has the problem described in
`decide-reason-omits-the-expected-answer.md`. The passing script part ("all 7
present") comes first, and the failing decide part ("yes p=0.98") comes second
with nothing to say that it failed.

Both lines appear in the task card's failure bullets and in the *Every run*
table, which is described there as "the first check that did not hold and what
it saw". The check is the right one, but its reason gives no sign of which part
failed.

## Why it matters

The failure bullets are the first thing a reader looks at in a task card, and
they are meant to be skimmed. A reason that opens with a passing result sends a
skimming reader the wrong way. The problem gets worse the more parts a check
has.

## What we want instead

**In a combined reason, the parts that decided the verdict come first, and a
part that passed is marked as passing.** Either of these would do:

- **Order by severity.** Put the parts whose result equals the combined result
  first, in declaration order, followed by the rest:
  `read none of [...]; read ['llms.txt'] · also: yes p=0.96`
- **Mark each part.** `✗ read none of [...] · ✓ yes p=0.96`

The first changes the least and keeps the reason readable in a table cell. For
a passing combined verdict, every part passed and declaration order is fine.

## Resolution

Fixed in 0.2.3 by ordering on severity: the parts whose result is the
combined result come first, in declaration order, then the rest, still
joined with `; `. No `also:` marker; with the `decide` fix a passing part
reads as passing on its own. The example above now reads
`yes p=0.98, expected no; all 7 present`.
