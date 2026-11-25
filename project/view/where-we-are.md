---
date: 2026-11-21
status: a snapshot of the moving pieces, written while the user gets back up to speed after time away. Update or delete it once the mocks are reviewed and the inventory is revised; it is not a design note
---

# Where we are (2026-11-21)

The user was pulled onto other work before a vacation and is
reassembling context. Next step is theirs: **review the mocks**. Until
then, decide nothing below on their behalf.

## The design work, in the order it happened

All on 2026-11-16 unless dated.

1. **Journeys** (`journeys.md`): corrected by the user. Settled: one
   person, one machine; journey 1 is the coding session's; journey 2
   ("it passed and I do not believe it") is the centre; no trajectory
   compared across several variants; the session acts, auspex shows.
2. **Inventory** (`inventory.md`): a draft for review, never revised.
   Its "first pass" section (the session says done and gives a URL; the
   URL opens on how it went; an attempt opens as a transcript) is what
   the mocks draw.
3. **The mocks**, a design canvas, "auspex core flow" (created 19:30
   UTC): https://claude.ai/artifact/XE4Baec27NiQMhLuXh5a6Q
   - `1 · One exam under one setup`: the exam's question and a
     one-sentence result; a setup switcher with tries per setup; a
     stage × try grid (right, wrong, lost, not run) with a tally per
     stage; "Your agent's analysis", numbered findings linking to steps;
     "Every try", a row per try with a tool-kind strip per stage, calls,
     tokens, time; stale tries hidden behind "Show them"; a lost try not
     counted.
   - `2 · One try`: breadcrumb, result, Copy address; tabs Transcript,
     The exam as given, Left behind; a side panel with search over folded
     steps, the stages, the marks (your agent, check) and fold buttons;
     the transcript grouped by stage, right stages folded, marked steps
     open; checks saying where they fired.
   - Built on `readme-answers` as one job in six stages. The exam,
     questions, setups and model are real; the results are made up.
   - Step 3 of the design process (pages) has therefore started, though
     nothing in the repo said so until this note.
4. **Proctor's bank** (proctor `project/plans/bank.md`, that evening):
   took the canvas's decisions into proctor. No batch; a bank per exam
   and setup; an exam as one job in stages; the screen says exam,
   setup, try. It cites `inventory.md` for the words, but the canvas is
   where they are. Step 1 built 2026-11-17, step 2 2026-11-18.

## Since then (2026-11-22)

- **Web Awesome** is the component library, vendored and embedded in
  the assembly, with pages still rendered on the server. No JS build;
  React and Blazor were ruled out (see `workbench.md`, "Packaging").
  Vendored 2026-11-23 (build step 2): no autoloader, the components a
  page uses imported from one module, and a crawl test that follows
  imports, so no chunk is shipped untested.
- **The mocks are reviewed on substance, not visuals.** The visual pass
  happens in code, on Web Awesome's theme, not on the canvas.
- **The stage page** is agreed for IA, layout, content and words:
  `stage-page.md`, and artboard 3 on the canvas. It supersedes the
  transcript layout of artboard 2.
- **Proctor's `report.html` is gone** (proctor, 2026-11-22), removed
  before the stages refactor so that work doesn't pay for a second
  renderer. `proctor report` writes `results.jsonl`, `stats.json` and
  `summary.md`; auspex is the only HTML viewer. Proctor's public site
  still links two frozen `report.html` copies as its showcase.
- **Proctor's `check-evidence.md` is agreed** (the user's calls,
  2026-11-22, on a decision page:
  https://claude.ai/artifact/AiHcvfFQ2qyqcDCPrncUGL). `why` is required;
  `describes`, `why` and a script's path go into each verdict; evidence
  is steps or files, with `absent` dropped; the friction roll-up is
  written by `proctor report`; the surface verdict waits; friction
  differences are confirmed over tries (Mann–Whitney: at least 4 tries a
  side, or 3 against 5). Built in the order the pages need, stage page
  first. Outcomes are confirmed the same way, on headline checks held per
  try. Every check belongs to a stage: workspace checks to the last
  stage, guardrails per stage, so the stage page has no try-level
  checks. Still OPEN there: a spend cap for the hosted reader. A line in
  `bank.md` B9 (stage ids are screen labels, so keep them terse) is
  still only asked.
- Open comments on artboard 1 (deep links read as chips, the warning
  tint is too faint in light mode) and artboard 2 ("The exam as given")
  are left for the next canvas pass. Artboard 1's two comments were
  fixed later the same day. The setup switcher that read as buttons is
  gone. The setup comparison is now a plain table, with setup tabs below
  it: the tabs show which setup is selected, and everything under them
  (per-setup facts, outcome grid, friction, analysis, tries) is about
  that setup alone. Using the comparison's column headers as the tabs
  was clever but illegible (the user).

## Friction, decided 2026-11-22

The day's friction decisions, across this repo, proctor's
`check-evidence.md` (C5, partly settled in a parallel proctor session)
and the canvas. Details in `journeys.md` (journey 2: "Four classes of
run" and "Three levels, three questions") and `stage-page.md`
("Friction").

- **Friction dropped out of the mocks by accident, and is back.** The
  mocks were built on `readme-answers`, where it had little to find.
- **Proctor scaffolds friction; auspex only renders it.** Detection,
  grouping, roll-ups and wording all live in proctor, so auspex doesn't
  become a second proctor. Today's viewer breaks this twice
  (`Pages.Across`, `SurfaceWords`); both go to proctor (C5.2, C5.4).
- **Why friction exists: class 2 against class 4.** A frontier model
  powering through tools that fight it passes, exactly like a run on
  well-suited tools. Only friction tells them apart; "it doesn't feel
  right to grade that experience 100% full success".
- **One measure: flagged steps,** each step once, every label equal.
  No severity, weights, score or tokens (proctor's C5.3, "Not taken").
- **A pass stays a pass.** Friction sits beside a result in its own
  colour, as a count, and never tints or qualifies it. No "heavy"
  threshold anywhere.
- **Three levels, three questions:**
  - *setup*: did the treatment help? Headline checks held per try
    (with "passed in k of n tries") and flagged steps per try, each as a
    spread across tries, against live, plus proctor's verdict word. Both
    confirmed over tries, never stages, which are only chunks for
    legibility;
  - *across tries*: constant or rare, and which costs most? One row
    per kind of friction: in k of n tries, min / median / max flagged
    steps when it happens, flagged steps per try overall (rows sort by
    it);
  - *stage*: why? The transcript, with each point's `because` sentence
    and its quote marked.
- **No per-try friction profile.** Tries are exchangeable ("same
  environment, same engine"), so there's no friction column per try,
  no counts in the outcome grid's cells, and the stage page doesn't
  place a stage against the other tries.
- **Confirming a difference: over tries, not stages** (recommended,
  OPEN in proctor's C5.4). Stages share a workspace, so a sign test
  over them counts one cause many times, and a one-stage exam could
  never confirm.
- **Labels renamed so each reads bare:** `read-internals`,
  `used-substitute`, `fell-back`, `searched-docs`, `ran-help`,
  `inline-script`, `machine-refused`, `harness-refused`,
  `fixed-own-work`, `wasted-read`, `bad-instruction`; `reread` and the
  reader's `workaround` kept. "Lost" now means only a try or stage that
  died. Built in proctor 2026-11-23 (`friction/2`, rules 0.10); a file
  written before keeps the old names until `proctor friction` reruns.
- **Settled in proctor since:** the surface verdict waits (C5.2), and
  both measures are confirmed over tries (C5.4).
- **On the canvas:** artboard 1 has "Did the change help?" and the
  friction table; artboard 3 has friction rows and span tags. The
  numbers are made up.

## Decided

- **Screen words: exam, setup, try, stage, step.** Proctor's files and
  commands keep exam, variant, attempt (bank.md leans to not renaming
  proctor; OPEN there).
- **No batch** anywhere: not on screen, in an address or a path. What a
  run did is a log entry.
- **The address** is `<exam>/<setup>/<try>/<stage>#<step>`, e.g.
  `readme-answers/as-shipped/3/clear-wait#3`; up to the stage it is the
  folder under `bank/`.
- **Setups are compared with live, try against try** (2026-11-22): on
  headline checks held and flagged steps per try, at the top of the
  exam page (`exam-page.md`). Comparing trajectories side by side,
  across tries or setups, stays out (journeys, "least sure of" 2).
- **auspex folds into proctor eventually**, not yet (2026-11-21).

## Built since

- **Stages exist in proctor** (2026-11-23, `bank.md` step 4, slices 1
  to 3 of 8): an exam can be one job of stages, a bare nb setup runs a
  try of them in one workspace, and `grade` writes checks per stage.
  auspex reads the layout: `/<exam>/<setup>/<try>/<stage>`, a strip of
  the try's stages, proctor's reason on a stage not reached. Decided
  with it: how a stage ends (ended, stopped, did not run), and
  `chained`, under which a stopped stage ends the try and the try still
  counts. Not yet: friction and the report on a staged exam, Harbor
  setups, and no real exam is converted.
- **The viewer reads the bank** (2026-11-23, build step 1): `Bank.cs`
  over proctor's root; `Pages.Across` and `SurfaceWords` deleted, not
  ported; the exam page lists tries as plain links until step 5.
- **Web Awesome is in** (2026-11-23, build step 2): 3.14.0, ten
  components, embedded and served by the one handler; no page uses a
  component yet.
- **The stage page is built** (2026-11-23, build step 3) on today's
  attempts, each a one-stage try. It shows `why`, `describes`, step
  evidence and `because` as soon as proctor writes them (C1, C2, C3,
  C5.1); until then cards carry the reason only. Steps 4 and 5 wait on
  proctor (stages, then the friction roll-up).
- **Proctor's C1, C2 and C3 are built** (2026-11-23): a check's `why`
  (not yet required; `proctor list` names the checks without one),
  `describes`, `why` and a script's path in each verdict, and `evidence`
  (steps or files) for the built-in checks. Grading now reads the
  trajectory for every attempt, bare nb included, so evidence uses the
  step ids auspex shows. Cards fill in once tries are regraded.
- **Proctor's C5.1 and C5.3 are built** (2026-11-23): `friction/2`,
  labels that read bare with their sentences, `rule` and `because` on
  every evidence item, `flagged_steps`. The stage page already reads all
  of it. What's left in proctor's plan: C5.4 (waits on stages), the
  judge and oracle evidence, and `why` as required.

## Not built, and what waits on it

**The exam page waits for axes (2026-11-24).** Proctor's
`project/plans/axes.md` replaces the variant with axes, values and
configurations, makes live a value per axis, and gives attempts ids
that are not `<variant>/<n>`. That changes what the exam page is
organised by, the file it reads and its addresses, so `exam-page.md`
and build step 5 are not built as written. The order is proctor's
`project/plans/axes-build.md`: the exam page is its slice 3, designed
against the new report file after `exam-page.md` is rewritten. The
stage page is unaffected apart from its address. The fold into proctor
and the rename are deferred until that plan is done.

- **Stages** (bank step 4). The mocks assume them; proctor today has
  tasks, each with its own attempts (`bank/<exam>/<variant>/<task>/<n>/`).
  Settled in `build-plan.md`: the stage page is built on today's
  attempts, each a one-stage try; the exam page waits for stages.
- **Back-fill and the freeze** (bank steps 3 and 5).
- **`grade --try`** (inventory C8) and **claims** (C9), unchanged.
- **Live-mark history** (S14): `live.json` keeps only the current
  variant and since when.
- **Partly moved since the inventory:** C1 (bare nb attempts now write a
  trajectory, so friction may cover them; not confirmed) and C5
  (`proctor report` writes `stats.json` and `results.jsonl` per exam;
  no per-attempt current-or-stale file).

## Waiting on the user

1. Confirm `exam-page.md`, written 2026-11-22 from artboard 1 as
   reviewed. (The mocks are reviewed; the stage page is agreed.)
2. Confirm `build-plan.md`. It settles stages-first against a task view:
   the stage page is built now on today's attempts, each a one-stage
   try, and the exam page waits for stages.
3. A spend cap for the friction measurement in proctor's C5.4. The
   reader stays Haiku (2026-11-23, after a shakedown of cheaper
   off-family readers: `friction-contract.md`, "Off-family readers"),
   so it can run on Anthropic credits through minrouter; that route is
   untested.

`inventory.md` is superseded (the user, 2026-11-22), not revised.

## Housekeeping on this laptop

- Proctor: work is on `exam-variant-attempt`, expected to replace
  `master` within the week of 2026-11-21. Local `master` holds `ed85faf`
  (Docker docs), already cherry-picked onto the branch; drop it then.
  The machine's `proctor.json` and `nb.json` are in `exams/`.
- auspex: on GitHub (private) since 2026-11-21. The repo was copied from
  the desktop owned by the user's account; group write was restored and
  `bin/`/`obj/` rebuilt.
- `/srv/aiwork/proctor-private/` does not exist here, though
  `CLAUDE.md` sends private notes and renderings (`views/`) there.
- Everything was pushed from the desktop before the trip (the user
  checked over WireGuard), so nothing is missing from GitHub.
