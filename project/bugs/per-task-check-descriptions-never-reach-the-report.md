---
type: bug
title: A task's own description of a shared check never reaches the report
created: 2026-09-23
status: open
found-by: the design-system docs-qa suite, restructuring for 0.2
---

# A task's own description of a shared check never reaches the report

0.2 moved the success criteria onto the task: "the prompt is what the user
wants; the checks are how it is measured" (`plans/suite-task-check.md`). The
headline, though, has to be a check every task carries under one name. So in an
answer-a-question suite, every task declares `answer`, and each task's
`answer` says something different about what counts as correct. The report
shows none of those sentences. It shows one of them, for all of them.

## What happens

Two tasks, one headline check, each describing its own criterion:

```json
// tasks/lookup-semantic.json
"prompt": "What is the light-mode value of the token `--color-surface-error`?",
"checks": { "answer": { "script": "checks/answer.py",
                        "description": "the answer gives `#fbedef`" } }

// tasks/fanout-no-guidance.json
"prompt": "How many constructs have no authored guidance?",
"checks": { "answer": { "script": "checks/answer.py",
                        "description": "the answer gives 120" } }
```

with `"pass": ["answer"]` in `suite.json`. In the report:

- **Checks table.** `answer` gets one row, and its "What it tests" is whichever
  task's sentence `Suite.CheckDescriptions()` saw first. `TryAdd` keeps the first,
  and `AllChecks()` walks tasks in order. So every task's criterion reads as
  "the answer gives `#fbedef`". That is not a missing fact; it is a wrong one.
- **Tasks table.** "Its own checks" is `OwnChecks`, which leaves out anything in
  `SharedChecks`, meaning any check every task carries. A headline check is
  carried by every task by construction, so it never appears here. The row
  shows "—".
- **`stats.json`.** `descriptions.checks` is keyed by check name only, and
  `task_details` holds check *names*. The per-task sentence is lost before the
  report is rendered, so nothing downstream can recover it.

The only way to keep the report truthful today is to give every task's `answer`
the same generic sentence, which puts the suite back where 0.1 had it: a
checklist of generic checks, and no statement anywhere of what this task needed
to get right.

## Why it matters

The story a reader should get from one row of the Tasks table is:

> here is what we asked the model to do, and here is how we decided whether it
> did it.

With the criterion missing, a report of generic checks tells a reader that the
suite cares about agent hygiene (exited cleanly, stayed on budget) and not about
whether the agent succeeded at the task. The distinction the 0.2 plan draws, the
fuzzy prompt against the precise check, is invisible on the page, which is where
it matters most.

## What we want instead

**A task's own description of a check is shown on that task's row, whichever
level the check is required at.**

- **Tasks table:** beside "What it asks", a column such as "How it is measured"
  holding the task's own sentences for the checks it declares, headline first.
  For the example above: `lookup-semantic` · "What is the light-mode value of…" ·
  "`answer` (headline): the answer gives `#fbedef`".
- **Checks table:** a check whose description differs between tasks says so
  rather than picking one. For example, "What it tests" reads "per task; see
  Tasks", or shows the suite-level sentence if one exists. Only a check with one
  description everywhere shows that description.
- **`stats.json`:** `task_details.<task>.checks` carries each check's description
  as well as its name, for example `[{ "name": "answer", "description": "…",
  "level": "task" }]`, so both renderings, and anything else reading the file,
  get it.
- **Failures section:** where it names a check that failed on a task, it uses
  that task's sentence rather than the shared one.

"Shared" in `SharedChecks` should then mean *declared by the suite* (or by a
fixture every task uses), not *carried by every task*. Being carried by every
task is what the headline rule requires; it says nothing about whether the check
means the same thing everywhere.

## Consumer side, for reference

The design-system `docs-qa` suite generates its 14 task files from an answer
key. Once this lands, each task's `answer` will state its criterion, for example
"gives `#e5e5e5`", "names all seven dimensions", "says Link has no authored
guidance rather than inventing some", or "judged by GLM faithful to
`patterns/buttons.md`". Each task's `route` check will name that task's page.
Until then it keeps one generic sentence on both, so the report does not mislead.
