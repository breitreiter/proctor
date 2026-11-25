---
date: 2026-11-22
status: written 2026-11-22 from artboard 1 as reviewed with the user that day (the setup comparison, tabs below it, friction per kind, no cards, headline checks held per try). For the user to confirm. Typography and colour are not settled; the user is trying visual variants on the canvas, and the visual pass happens in code on Web Awesome's theme
---

# The exam page

The page for one exam: how its setups did against live, and what
recurs across their tries. Mocked as "1 · One exam under one setup" on
the design canvas:

https://claude.ai/artifact/XE4Baec27NiQMhLuXh5a6Q#artboard-Main

It answers the first two of the three questions in `journeys.md`
("Three levels, three questions"). The third, why a piece of friction
happened, is the stage page (`stage-page.md`).

| Level | Question | Where on this page |
|---|---|---|
| Setup | did my treatment make things better or worse? | "Did the change help?", at the top |
| Across tries | is this happening all the time or rarely, and which costs most? | "Friction", under the selected setup |

## Order of the page

1. The exam: its id, its question, and the facts that hold for every
   setup.
2. **Did the change help?**: every setup against live.
3. Setup tabs. Everything below them is about the selected setup.
4. Each stage, in every try.
5. Friction.
6. Your agent's analysis.
7. Every try.
8. Footnotes.

The sections have no cards around them: a heading and its rows on the
page (the user, 2026-11-22: a box around every section reads as
something separate). The visual treatment is open.

## The exam

- The exam id, in monospace, above the question (`readme-answers`).
- The question as the heading: "Does a README tell a reader what they
  need to use jot from a script?"
- Facts that hold for every setup: model, stages, number of setups.
  Facts that differ by setup are under the tabs, not here.

## Did the change help?

A plain table: one column per setup, live first, labelled "live" with
its tries ("live · 5 of 5 tries"). The column headers are labels, not
controls. An earlier version made them the setup switcher; the user
found the hybrid clever but illegible.

| Row | Each setup's cell |
|---|---|
| Headline checks held per try | the spread across its tries ("12 to 15 of 18"), with "passed in k of n tries" under it |
| Flagged steps per try | the spread and the median ("3 to 13, median 4"), in friction's colour |
| Against live | proctor's verdict sentence; "this is live" in live's column |

- **Both measures are one number per try,** compared try against try
  (proctor's `check-evidence.md`, C5.4, decided 2026-11-22). Headline
  checks held is what the agent got right; flagged steps is what it
  cost. A stage is never a unit of comparison: stages are chunks for
  legibility and share a workspace.
- **"Passed in k of n tries"** (every headline check held) is the plain
  result. Alone it is too coarse at five tries, so it sits under the
  count it summarises.
- **The verdict sentence is proctor's,** written with the comparison:
  direction on each measure, whether it is confirmed, and what moved
  most ("Better on both. 2 tries, too few to confirm. Moved most:
  wasted-read on clear-wait and piped-yes"). Confirmation is
  Mann–Whitney over tries: at least 4 tries a side, or 3 against 5.
  auspex words nothing itself.
- **A pass stays a pass.** Flagged steps sit beside the result in
  their own colour and never tint or qualify it. Classes 2 and 4 of
  `journeys.md` look the same in checks; this row is where they come
  apart.

## Setup tabs

Bare tabs under the table, one per setup, live first and marked
"live". The selected tab is underlined. The user asked for tabs here
because they show selection and also say that what follows is only
about the selected setup. No box joins the tabs to the content.

Under the tabs, first, the facts for that setup: tries (and how many
were lost), stages right every time, stages never right, when the
tries were made.

## Each stage, in every try

A grid: a row per stage, a column per try.

- The stage's row label is its number, its one-line description, and
  its id in monospace (`5. How long clear waits with no terminal` /
  `clear-wait`). The id is the stage's label on screen, so proctor
  keeps it terse (`bank.md`, B9).
- Each cell says right, wrong, lost (the stage was running when the try
  died) or not run, in a word and a colour. The word carries the
  meaning; the colour only helps.
- A cell links to that stage of that try, on the stage page.
- At the end of each row, a tally: "right every time", "never right",
  "right in 3 of 5".
- A last row gives each try's stages right. It explains; the
  comparison above uses headline checks held.
- **No friction in the cells.** Results vary between tries, so the
  grid stays. Friction is read per kind (below), never per try: tries
  are exchangeable, "same environment, same engine" (the user).

## Friction

One row per kind of friction, under the user's description: "Signs that
the agent's strategy for completing a task isn't working. This often
(but not always) indicates an opportunity to improve tools, prompts, or
documentation."

| Column | What it says |
|---|---|
| Kind | what fired ("jot: command not found"), then the label in monospace and where (`machine-refused` · store-location) |
| In | the tries with it, k of n: constant or rare |
| min, median, max | flagged steps over the tries that had it: how big when it happens |
| Per try overall | its flagged steps over all tries, divided by n: which first |

- **Rows sort by per try overall,** which folds how often and how big
  into one number. A last row gives all friction's per-try total.
- **A kind is a label and what the rule fired on.** A reader's points
  have no detail and group by label alone.
- A row links to a stage where it happened, on the stage page.
- Labels read bare (`read-internals`, `wasted-read`, ...); see
  `friction-contract.md` and proctor's C5.1 for the list.
- The rows are read from the file `proctor report` writes beside
  `stats.json` (C5.4). auspex groups and counts nothing.

## Your agent's analysis

Numbered findings in prose, each with links to where it shows: "→ Try 3
· clear-wait · step 3". The links are plain underlined text with an
arrow; as chips they read as tags (the user's comment). They open the
stage page at that step.

Who writes the findings, and where they are kept, is not settled: the
marks proposal in `inventory.md` (one address, four writers) is the
draft. The session writes; auspex shows.

## Every try

A row per try: its number, when it was made, its result ("4 of 6 stages
right", or "Lost: the model provider failed in stage 5"), a strip of
what it did, tool calls, tokens read, time.

- **The strip** is one segment per stage, a cell per step coloured by
  kind: the question, reading a file, a shell command, the answer
  (proctor's agent-neutral tool kinds, `Ingest.Kinds`). A stage
  answered wrong has a filled segment with a solid outline; a lost
  stage, a dashed grey outline, so the two differ in pattern as well as
  colour (the user found the first version illegible in light mode).
- A legend sits with the section heading.
- A row opens the try on the stage page, at its first failed stage.
- **No flagged-steps column,** for the reason the grid has none.

## Footnotes

- Which checks were run in every try, how many headline checks a try
  has, and when a try counts as passed.
- Which tries are not counted and why: a lost try (a stage didn't run)
  is left out of every number, and the try made to replace it says so.
- Tries made before the exam changed are stale and hidden, with "Show
  them". They no longer answer the same question.

## What it needs from proctor

- Per try: headline checks held (k of n), passed, flagged steps, lost
  or not, stale or not, tool calls, tokens, time, and each step's tool
  kind.
- Per setup against live: the verdict sentence on both measures,
  confirmed or not, and what moved (C5.4).
- Per kind of friction: in k of n, min / median / max when it happens,
  per try overall (C5.4), in the file `proctor report` writes.
- Per stage: its id and one-line description (B9; `about` is OPEN
  there).
- Stages themselves (bank step 4). Until they exist proctor has tasks,
  each with its own attempts, and this page waits: a grid of tasks by
  attempt number would draw unrelated runs as one try.

## Not settled

- Typography and colour.
- Where the agent's findings are kept.
- Many setups: the table gains a column per setup; past three or four
  it needs a rule for which to show.
- What a row of the friction table links to when a kind happened in
  several tries.
