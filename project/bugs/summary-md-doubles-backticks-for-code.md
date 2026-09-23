---
type: bug
title: summary.md writes code as ``double-backtick`` spans, which are noisy to read raw
created: 2026-09-23
status: open
severity: low
found-by: the design-system docs-qa suite, reading its first 0.2.2 report
---

# summary.md writes code as ``double-backtick`` spans, which are noisy to read raw

The report uses two inline forms: `` `id` `` for identifiers and
` ``code`` ` for what is typed or opened. `Inline()` turns them into two
different HTML styles. The markdown renderer keeps both forms as they are
(the comment above `Html()` in `Report/Report.cs` says so). `Paths()` also
wraps every path it finds in a reason in ` `` `` `.

**This is valid CommonMark.** A span delimited by double backticks is a code
span, so on GitHub or any other renderer `summary.md` looks right. It is not a
rendering bug.

## What happens

Read raw, which is how an agent or `cat` reads `summary.md`, the doubled
delimiters are noise. They look like an escaping mistake, and they are worst
inside quoted strings that a check script produced:

```
| `http-index` | The local model, … | … | ``build/eval-corpus`` |
- http-index run 1 failed: answer: yes p=0.96; read none of ['``patterns/forms.md``', '``patterns/forms-checklist.md``']; read ['llms.txt']
```

In the second line `Paths()` has wrapped paths that sit inside a Python list
repr, which is how the design-system route check formats its reason. The HTML
turns these into clean `<code>`. The markdown gets `'``patterns/forms.md``'`.
`llms.txt` is not wrapped, because it has no directory part, so the same list
formats its two kinds of entry differently.

## Why it matters

Low severity. Nothing is wrong once rendered. But `summary.md` is the rendering
aimed at readers who do not render it: an agent asked "what failed?" reads it
as text. Both markdown forms render identically anyway, since markdown has one
code style. So the id/code distinction gives nothing in `summary.md` and costs
legibility.

## What we want instead

**In markdown, write ` ``code`` ` as single-backtick `` `code` `` unless the
content itself contains a backtick.** Keep the two forms in the block IR so the
HTML can still style them differently. The change belongs in the markdown
renderer, not in `Paths()` or at the call sites.

Separately, and optional: `Paths()` could leave a path alone when it is already
inside quotes, so a quoted string in a script's reason is not wrapped again.
