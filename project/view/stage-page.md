---
date: 2026-11-22
status: IA, layout, content and words agreed with the user 2026-11-22 ("ia/blocking/content/words lgtm"). Typography and colour are not settled; they wait for the visual pass in code, with Web Awesome's theme. Mocked as artboard 3 on the design canvas
---

# The stage page

The page for one stage of one try: what a person reads after "it passed
and I do not believe it" (journey 2), and after a failure. Mocked as
"3 · One stage of a try" on the design canvas:

https://claude.ai/artifact/XE4Baec27NiQMhLuXh5a6Q#artboard-Stage

It supersedes the transcript layout of "2 · One try" (the side outline,
marks listed in a panel, fold buttons in a panel). That artboard is
kept until the next canvas cleanup, not polished.

## What the page has to show

- A try has 1 to n stages, and one stage can be long and complex.
- A stage is graded once it ends. Its checks grade the transcript, the
  files, or neither.
- The same check can point at a step in one result and at nothing in
  another. "Must call save_data" points at the call when it passes and
  at nothing when it fails.
- On a passed stage the person wants raw detail, and maybe search. On a
  failed stage they first want to understand the failure, and then
  maybe read the step.
- Understanding a failure means the reason, then what the check does
  and why the author thought it worth checking. The agent's marks
  matter only when the person suspects an accurate answer that didn't
  fit the expected form (the user, 2026-11-22).

## Layout

There are four relations to show: try → stages, stage → checks, stage
→ steps, check → steps. Only the first three take space. The fourth is
shown by what's selected.

- **The try is a frame around its stages.** A short header (result,
  setup, model, calls, tokens, time), then a row of stage tabs. The
  page opens on the first stage with a failed check, else on the first
  stage. A one-stage try has no tabs: the try page is its stage.
- **Stage tabs show their labels by default.** Nobody remembers what
  stage 12 is. Failed stages are tinted and marked ✕. A stage whose
  answer was right but where a check failed shows "· 1 check failed".
  When there are too many tabs, failed stages, flagged stages and the
  current one stay, and runs of the rest fold into an "N more ▾" menu.
  Hovering a tab shows the stage's one-sentence description.
- **Two panes under the tabs.** Checks on the left (about 420px, its
  own scroll), the transcript on the right (the rest). Below about
  1000px they stack, checks first.
- **The right pane has tabs Transcript and Files.** There is no "exam
  as given" tab: the question is step 1, and the name read as nothing
  (the user's comment on both artboards).

## The checks pane

- **Stage header:** the label, right or wrong, the one-sentence
  description, and counts (✕ failed, ✓ passed). There is no per-check
  colour strip; the user found it superfluous next to the list.
- **Every check is shown, at two weights.** At most about 20 checks
  per stage, so showing them all costs little. The passes show what
  held, which is context for a failure.
  - A failed check is a card: the check's name, the reason (what
    happened), then "Why it is checked" with its label on its own line,
    then "Steps 4 7", each number a link. There is no "what it does"
    line on the card.
  - A check that points at no step has no evidence line. An earlier
    version filled the search box with the absent term; the user
    dropped it as not durable.
  - The agent's mark folds under "Dispute it: your agent marked step
    N", below the card.
  - A passed check is one line: a mark, its name, its verdict ("25
    words").
- **Friction** sits between the failure cards and the passed checks,
  as one row per point: its label (hovering shows the label's
  `describes`), its step span, the rule or reader that found it, and
  the point's `because` sentence. The quote is marked in the
  transcript, not repeated in the row. Rows are only ever read from
  `friction.json` (see "Friction" below).
- **The stage header** shows the stage's flagged steps, in friction's
  own colour beside the result, never tinting it. It doesn't place the
  stage against the other tries: tries are exchangeable, and friction
  across them is read per kind on the exam page (`journeys.md`).
- **Every check belongs to a stage** (the user, 2026-11-22; proctor's
  `check-evidence.md`, C4). There is no "checks on the whole try"
  section. The guardrails (exit_ok, no_denials, not_nudged,
  under_budget) are per stage, and a check on the workspace belongs to
  the last stage, which is what it reads under B8. A one-stage try has
  all its checks on its one stage.

## Which pane leads

- **Any failed check opens the stage with the failure first,**
  whatever the stage's result. The first failed check is selected and
  its steps are highlighted in the transcript. So a stage that was
  right, but where `found-in-readme` failed, opens failure first: that
  is journey 2.
- **A stage with no failed check opens on the transcript.** The checks
  pane narrows to its header and the friction rows, with "Show the
  checks" to open the list. On a green stage the friction rows are the
  way in: checks only answer what the author thought to ask.

## Linking checks and steps

- Selecting a check (its card or row) highlights its steps in the
  transcript.
- Each step has a gutter mark per check that cites it: ● for a failed
  check, ○ for a passed one. Clicking a mark selects its check.
- A friction point covers a span of steps. Each step in the span has a
  tag with the point's label; selecting the tag or the row highlights
  the whole span.
- Search sits above the transcript and searches this stage.

## Friction

Friction dropped out of the mocks of 2026-11-16 by accident, not by
decision. It went because the mocks were built on `readme-answers`,
whose one-question stages give it little to find. It is back
(2026-11-22) under one rule from the user: **proctor scaffolds friction,
auspex only renders it.** Detection got complex, and detection in auspex
would make it a second proctor on top of proctor. That applies the
line in `CLAUDE.md` to friction's analysis as well as its models.

- Proctor detects (`proctor friction`: the rules plus the Haiku reader)
  and writes `friction.json` per attempt (`friction-contract.md`).
- auspex shows each point as written. It doesn't group, rename, score
  or merge points. The one check it runs is the shared quote rule
  (`Quote.cs`), to mark where the quote sits in the step.
- Anything across attempts (what recurs, by label, in how many tries)
  is a file proctor writes, not a query auspex runs. Two things in
  today's viewer break this and go when the viewer moves to the bank:
  `Pages.Across` groups points across attempts, and `SurfaceWords`
  turns the surface flags into words. Both become fields proctor
  writes (asked in proctor's `check-evidence.md`, C5).

## What it needs from proctor

Proctor's `project/plans/check-evidence.md` (agreed 2026-11-22):

- `why` on every check, required (C1);
- each verdict in `checks.json` carries `describes` and `why` as graded
  (C2), so auspex copies none of proctor's logic;
- `evidence` on each verdict: steps or files (C3). `absent` evidence was
  dropped; a failure with nothing to point at has none, and its reason
  says what was missing.

- friction on the same addresses, and the cross-attempt and surface
  summaries as files proctor writes (C5): `because` per evidence item
  and `describes` per label (C5.1), flagged steps (C5.3), and the
  per-exam roll-up with per-try values and what recurs per kind (C5.4),
  which `proctor report` writes beside `stats.json`. The surface verdict
  (C5.2) waits until a page shows it, so auspex drops `SurfaceWords`.

The stage page is the third of three levels (`journeys.md`, "Three
levels, three questions"). The other two are on the exam page: did
the treatment help (setups against live), and what friction costs
(per kind: how often, how big, which first).

Proctor's `project/plans/bank.md`, B9: a stage's id is its label on
screen, so keep it to two or three kebab-case words naming what the
stage asks (`clear-wait`, not `stage-12`). An optional `about` sentence
per stage is OPEN there.

## Not settled here

- Typography and colour. The user has small nits; they go into the
  visual pass in code, on Web Awesome's theme, not into the canvas.
- Files: the tab exists but isn't drawn.
- Stages themselves (bank step 4). Until they're built, proctor has
  tasks, each with its own attempts.
