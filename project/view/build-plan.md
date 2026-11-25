---
date: 2026-11-22
status: proposed 2026-11-22, for the user to confirm. The order of work from the agreed designs (`stage-page.md`, `exam-page.md`) to running pages, and what each step needs from proctor. Lines marked OPEN are mine
---

# Build plan

What gets built, in what order, and what each step reads. The designs
are `stage-page.md` (agreed) and `exam-page.md` (written from artboard
1, to confirm). Proctor's side is `check-evidence.md` (agreed
2026-11-22, built in the order these pages need) and `bank.md`.

## The order, in one line each

1. **Onto the bank.** The viewer reads `bank/` instead of `datasets/`'
   old layout, and loses the two places it does proctor's work.
2. **The Web Awesome skeleton.** Vendored, embedded, served by the one
   asset handler, with the crawl test covering what it loads.
3. **The stage page, on today's attempts.** An attempt of a task is
   drawn as a one-stage try, which the agreed design already covers.
4. **Stages arrive** (proctor's bank step 4). Final addresses, stage
   tabs, workspace checks on the last stage.
5. **The exam page.** It needs stages and `proctor report`'s friction
   roll-up.
6. **Later:** the agent's findings, the Files tab, dispositions, the
   static demo.

Steps 1 to 3 need nothing from proctor that isn't built or in hand.
Step 3 gets better as proctor's C1, C2, C3, C5.1 and C5.3 land, and
shows what the files hold until then. Steps 4 and 5 wait on proctor.

## 1. Onto the bank

Today `Cells.cs` reads `<experiment>/<arm>/<task>/<sample>/`. It moves
to proctor's bank as built (step 1 of `bank.md`):

```
bank/<exam>/<variant>/<task>/<n>/
  manifest.json    trajectory.json (ATIF)    core.json
  checks.json      friction.json             diff.patch
exams/<exam>/live.json                       the live variant
```

- **`Bank.cs` replaces `Cells.cs`.** It lists exams, variants, tasks
  and attempts, and reads one attempt's files. It reads proctor's file
  names from one place, so they're easy to find when auspex folds into
  proctor.
- **What goes.** `Pages.Across` (grouping friction across attempts) and
  `SurfaceWords` (turning surface flags into words) are proctor's work
  done in auspex, and are deleted, not ported. The experiment page goes
  with them: the exam page replaces it in step 5. No page in between
  summarises anything.
- **Until step 5 the index is plain links:** exam, then variant, then
  task, then attempts by number, each with its status. Nothing counted
  or grouped.
- **Test data.** `SampleBank` writes a small proctor root. The opt-in
  real-data crawl (`AUSPEX_TEST_ROOT`) runs over any proctor root. With
  no bank on hand, `project/trials/datasets_to_bank.py` links samples
  from `datasets/` into a scratch root (proctor's own
  `attempts-to-bank.py` doesn't apply: `datasets/` predates batches);
  `datasets/` stays frozen and out of git.
- **Built 2026-11-23.** Not built in this step: anything about whether an
  attempt is current. That needs the variant and task hashes, which is
  proctor's logic; auspex shows every attempt in the bank until proctor
  writes which are current (the exam page needs it, step 5).
- **Words.** The code and the screen move to proctor's words with this
  step: exam, variant (setup on screen), attempt (try on screen). The
  old words leave the code.

## 2. The Web Awesome skeleton

`workbench.md`'s packaging rules hold: the bytes the tests fetch are
the bytes a user gets, through one handler, with no mode switch and no
JavaScript build.

- **Vendored.** One pinned Web Awesome release (its free, MIT part) in
  `assets/`, embedded in the assembly, served by the asset handler.
  Fonts and icons too: nothing loads from a CDN. auspex is loopback
  only, and is used offline (a hotel connection is reason enough).
- **Only the components a page uses,** registered by explicit imports
  from one `components.js`, not by Web Awesome's autoloader. The
  autoloader fetches a component's chunk the first time a tag appears,
  so no page names it and the crawl can't see it.
- **The crawl test follows imports.** Besides every route and every
  asset a page names, it parses `import` statements in the JavaScript it
  fetches, follows relative ones, and fails on any non-200. A chunk the
  pages can reach is a chunk the test fetched.
- **Built 2026-11-23:** Web Awesome 3.14.0 (3.15.0 was six days old),
  ten components (badge, button, details, dropdown, dropdown-item,
  input, tab, tab-group, tab-panel, tooltip), 107 files, 502 KiB. The
  crawl follows script `import`s and stylesheet `@import`s and requires
  every shipped file to be reached; it fetched all of them. Checked once
  by hand in headless Chrome: the modules load, no console errors.
- OPEN: a headless-browser test that loads each page and fails on any
  failed request or console error. It catches what static crawling
  misses (a chunk imported by a string built at run time), at the cost
  of a browser in the test run. I'd add it only if the import crawl
  misses something.
- **The theme.** Web Awesome's theme tokens are where the visual pass
  happens. Friction's colour (purple) and the right / wrong / lost
  colours are tokens there, in light and dark. The user is trying
  variants on the canvas; their pick becomes the token values.
- **Server-rendered pages,** with Web Awesome's components as the
  markup. Script only where the design needs it: selecting a check
  highlights its steps, a tab switches panes. A page reads correctly
  without script.

## 3. The stage page, on today's attempts

`stage-page.md` as agreed. Before stages, an attempt of a task is a
try with one stage, and the design says a one-stage try has no tabs:
the page is its stage. So the page built now is the page kept.

- **Address, for now:** `/<exam>/<variant>/<task>/<n>`, with the task
  as the stage's label. It changes in step 4. One person on one
  machine opens these, so a link breaking once is acceptable.
- **Header:** result, setup, model, tool calls, tokens, time; the
  stage's label, right or wrong, its description, counts, and its
  flagged steps in friction's colour.
- **Checks pane:** failure cards (name, reason, "Why it is checked",
  "Steps N"), the agent's mark folded under "Dispute it" once marks
  exist, friction rows (label with its `describes` on hover, span,
  finder, `because`), passed checks one line each. Failure first
  whenever any check failed.
- **Transcript:** steps with gutter marks per check (● failed, ○
  passed), span tags per friction point, the cited quote marked by the
  shared quote rule (`Quote.cs`), search within the stage.
- **What it shows depends on what the files hold.** Before C1 and C2,
  a verdict has only its reason, so the card has no "Why it is checked"
  line. Before C3, no steps. Before C5.1, a friction row has a label
  but no `because`. auspex shows what's there and makes up nothing: it
  doesn't fall back to reading the exam definition (that would be
  proctor's `Checks.Describe` again in auspex).
- **Guardrails,** under C4, belong to the stage. Before stages they're
  simply the attempt's checks.
- **Tests:** the crawl, plus assertions on the sample bank: a failed
  check opens failure first and its steps are linked; a passed attempt
  opens on the transcript; a quote is marked; a check with no evidence
  has no steps line.
- **Built 2026-11-23** (`StagePage.cs`, `assets/stage.js`). Checked by
  screenshot in headless Chrome on real tries linked from `datasets/`,
  with the trials' friction files. Web Awesome's base layer styles every
  native `details` and `button`, so the steps and rows override it.
  Uses two components so far: `wa-input` (search) and `wa-details`
  ("Show the checks"). Not built: "Dispute it" (no marks yet), Files
  (not designed), and anything that needs proctor's C1, C2, C3 or C5.1:
  the page shows those fields as soon as the files carry them.

## 4. Stages arrive

When proctor builds bank step 4 (an exam as one job in stages, one
container per try):

- **Addresses become final:** `/<exam>/<setup>/<try>/<stage>#<step>`,
  from `bank/<exam>/<variant>/<n>/stages/<i>-<id>/`.
- **Stage tabs** with labels (B9: two or three kebab-case words),
  failed tabs tinted and marked, "· N check failed", runs of quiet
  stages folded into "N more ▾" when there are too many. The page opens
  on the first stage with a failed check.
- **Workspace checks on the last stage** (C4), with `{file}` evidence.
- **A lost try** (a stage didn't run) is a status on the try, shown in
  the header.

## 5. The exam page

`exam-page.md`. It needs, from proctor:

- stages (step 4);
- the friction roll-up `proctor report` writes beside `stats.json`:
  per try, headline checks held, passed and flagged steps; per kind, in
  k of n, min / median / max, per try overall; per setup against live,
  the verdict sentence on both measures and what moved (C5.4);
- each stage's id and description (B9), and each step's tool kind for
  the strip (`core.json`, or the trajectory with `Ingest.Kinds`).

Built from top to bottom: the setup comparison, setup tabs, the stage
grid, the friction table, every try, footnotes. **Your agent's
analysis is left out** until it is settled where the findings are kept
(`inventory.md`'s marks proposal).

## 6. Later

- The agent's findings, and the marks behind them (one address, four
  writers: `inventory.md`).
- The Files tab on the stage page (designed as a tab, not drawn).
- The person's disposition on a friction point, and the treatment
  record (`loop.md`). auspex writes nothing until then.
- The static demo (`demo.md`).

## Not decided here

- OPEN: the headless-browser test (step 2).
- OPEN: what "Dispute it" shows before marks exist. I'd leave it out.
- `inventory.md` is superseded (the user, 2026-11-22). Its marks
  proposal and its open questions on dispositions wait for step 6.
