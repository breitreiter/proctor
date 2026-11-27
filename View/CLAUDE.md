# auspex — developer notes

A light viewer for agent trajectories (ATIF): what an agent did on a
task, where it stumbled, and how one variant's attempts went. Split off
from proctor (`/srv/aiwork/proctor`, the runner, grader and reporter of
evals) on 2026-11-11. Proctor keeps checks, the report and the
problem → change → rerun loop. auspex reads what proctor wrote.

**The line (2026-11-14):** auspex renders, proctor codes. Anything that
needs a model, analytic or generative, and so an API key or serious local
compute, belongs in proctor, along with the deterministic tools that feed
it (event table, normalisers, operators). auspex makes no model calls.

**auspex lives in proctor's repository since 2026-11-24**, as `View/`
(stage 1 of proctor's `project/plans/rename-to-auspex.md`): its own
project in the same solution, its own namespace and binary, started on
proctor's root as before. Its design notes are `project/view/` at the
repository's root, and every `project/…` path below means that
directory. Two things did not come along and are still in the old
checkout (`/srv/aiwork/auspex`): `project/trials/`, the throwaway
scripts and their outputs, which quote transcripts; and `datasets/`.

**It may call proctor's code for what only proctor may work out**
(`Measured.cs`): which configurations an exam carries, where it has
tries, one change against another. The rule is unchanged, auspex
renders and proctor codes: the viewer computes no rate, no test and no
count of its own, and makes no model call. A hosted viewer has no model
key, so anything a model does is done on a person's own machine by the
agent they work with, and reaches a page only as a link to it.

The viewer's first pass exists (2026-11-15).

## The program

.NET 10, one project at the root, pages rendered on the server, no
JavaScript build. **It reads proctor's bank** (since 2026-11-23, build
step 1 of `project/build-plan.md`): give it proctor's root, the directory
holding `bank/` and `exams/`.

- `Program.cs`: `auspex <proctor root> [--port N]`. Prints the URL to
  open, which carries that start's token.
- `Server.cs`: loopback only, token cookie on every route, every asset
  embedded in the assembly and served by one handler. No development
  mode (`project/workbench.md`, "Packaging").
- `Bank.cs`: reads what proctor wrote, and nothing more: the exams in
  `bank/`, each attempt's files (`bank/<exam>/<variant>/<task>/<n>/`,
  or a stage of a try, `bank/<exam>/<variant>/<n>/stages/<i>-<id>/`:
  `manifest.json`, `status`, `trajectory.json`, `checks.json`,
  `friction.json`, `core.json`), and from `exams/<exam>/` the
  description and live mark. Proctor's file names live here only.
- `Pages.cs`: an exam of variants, `/<exam>`: its tries as plain links
  by variant and task, the live one marked, nothing counted (proctor's
  numbers for such an exam are in its report, not behind
  `Measured.cs`); the shared page helpers. `Open` is the one page head:
  every page is in the designed style (since 2026-11-27) and none loads
  a script.
- `ExamPage.cs`: the page of an exam with axes, `/<exam>`, the first of
  the pages designed on the canvas to be built (2026-11-27). Sections are vocabulary words:
  Configurations (each named one against what is live on five measures,
  a strip centred on live's median), Predictions, Axes, Stages, Checks
  with their roles. It reads `Measured.Stats` and, per configuration,
  `Measured.Compare`. No form and no button: the viewer changes nothing
  in v1.
- `ConfigurationPage.cs`: one configuration, `/<exam>/configuration/<key
  or name>`, the second designed page (2026-11-27): against live on the
  five measures, its predictions, each stage as shares of correctness
  checks held (average, lowest, highest) and what did not hold, each
  check's rate beside live's, friction by kind, and every try with its
  stages as squares that open that stage. It reads `Measured.Report`
  (the stats, the friction rollup, each try's id).
- `ComparePage.cs`: one configuration against another in one setting,
  `/<exam>/compare?configuration=<name>[&against=<name>]`, the third
  designed page (2026-11-27): "X vs Y", the name before vs the subject
  of every number. Predictions about the pair, the five measures as two
  lanes on one scale, each check's rate, each stage as plain
  statistics, friction by label. `AxesPages.Compare` hands over to it
  when one setting is the subject, and draws every setting of a change
  itself (`&settings=all`): a row a setting, both sides' tries, what
  was found, and proctor's reading of them as one.
- `AxesPages.cs`: coverage, `/<exam>/coverage` (each value's tries, the
  configurations named or run, what is live across an axis nobody
  chooses), and the every-setting comparison. Neither was drawn on the
  canvas; both use the designed pages' parts.
- `TryStagePage.cs`: one stage of a try, the fourth designed page
  (2026-11-27), at `/<exam>/try/<id>/<stage>` on an exam with axes and
  at the bank's path on an exam of variants
  (`/<exam>/<variant>/<try>/<stage>`, or `/<exam>/<variant>/<task>/<n>`
  for a try of one task, which has no tabs). Stage tabs with proctor's
  right or wrong (from `Measured.Report`, exams with axes only; without
  it a stage that did not complete says its status), verdicts grouped
  by the role `checks.json` records ("role not recorded" for a grade
  from before roles), each with why it is checked and its script when
  the file has them, evidence and friction spans as pills that open the
  step (`:target` marks it, no script), each step a native `details`,
  and `?show=prompt|files`. It replaced the first pass's `StagePage.cs`
  and `stage.js`; search and selecting a check went with the script.
- **A mark beside a configuration's name is about its own tries** (the user,
  2026-11-27; `ExamPage.Own`, `ExamPage.Light`). Proctor's answer about a
  configuration compares the change in every setting that has it and pools
  them; the mark on the index, the exam page and the configuration page reads
  only the cell that is the configuration itself, on correctness checks held,
  as the comparison page does. A model is an axis, so tries on another model
  are another configuration's. The pooled verdict is on the comparison page's
  "Every setting".
- **A retired configuration** (`ConfigurationRow.Retired`, from proctor's
  `retired.json`) is left out of the exam page's Configurations rows and the
  index's list, and counted beneath them: on the exam page a `<details
  id="retired">` with each one's name, date, tries and reason, which the
  index's "N retired" links to. Its own page says it is retired and why.
- `IndexPage.cs`: the exams, `/`, the fifth designed page (2026-11-27):
  a row an exam, most recently tried first. An exam with axes shows its
  named configurations with the marks `ExamPage.Standing` gives them,
  its predictions and its tries; an exam of variants is a plain row. It
  lists exams that have a bank, as before.
- **Type is six roles and nothing else** (the canvas's board "6 · Type",
  agreed 2026-11-27): Title, Heading, Lead, Body, Small and Code, each
  one face, size and line height, as `--t-*` tokens at the top of
  `one.css`. Two weights, 400 and 600 (`.strong`); 700 is the two
  heading roles'. Mono (`.mono`, which is Code) is only for what could
  be typed or pasted, so tags, marks and pills are Small in Geist. No
  page and no rule sets a font size: a new piece of text takes a role's
  class (`.small`, `.mono`, `.name`, `.desc`, `.said`, …) or the body's.
  The Title's 26px on a narrow screen is the one exception.
- `assets/one.css` and `assets/fonts/`: the one stylesheet (dark
  ground, Geist, Space Grotesk heads, lime rules), copied from the
  canvas's `auspex.css`, and its three fonts, vendored (SIL OFL) so a
  page asks nothing of the network.
- `Quote.cs`: finds a quote by proctor's judge rule.
- **Interactive parts are Web Awesome components** (the user,
  2026-11-27: a component library keeps the micro-interactions a
  hand-made part loses, and its theme forces type and colour to be
  named). `Pages.Open` links Web Awesome's stylesheet, then `one.css`,
  which sets the theme's tokens from ours (faces, sizes folded onto the
  six roles, weights, surfaces, text), so `size="small"` is Small,
  medium Body, large Lead. In use: `wa-details` for a transcript step
  and the fold-outs (`.quiet`), `wa-button` for step pills, `wa-tooltip`
  for what a tag means (`Pages.Tagged`). `assets/page.js` opens the
  step a link points at. Stage tabs are links to other pages, so they
  are not a tab group. Tags and marks are still our own spans. Web
  Awesome's base layer styles every native element (`details`, `pre`,
  `button`): where a page uses one, `one.css` says what it should be.
- `assets/webawesome/` and `assets/components.js`:
  Web Awesome 3.14.0 (MIT), vendored by
  `tools/vendor_webawesome.py` from the npm tarball's `dist-cdn/`: only
  the components in its `COMPONENTS` list, the chunks they import, and
  the stylesheet with its `@import`s. `VERSION` records the version and
  checksum. `assets/components.js` (written by the same script) is the
  one module a page loads; there is no autoloader. To use another
  component, add it to `COMPONENTS` and rerun. Never use `wa-icon`
  without `library=`: the default library fetches from Font Awesome's
  CDN. Dark mode is the `wa-dark` class, set from the system setting by
  an inline script in each page's head.
- `Measured.cs`: the one doorway to proctor's code (`Proctor.Exam.Load`,
  `Proctor.Answers.Compare` and `Coverage`, and `Stats`, an exam's
  report as `proctor report` computes it). What it gives is rendered
  as given; what proctor refuses is shown in proctor's words.
- `AxesPages.cs`: an exam with axes (proctor's `axes-build.md`, slice
  3d; `project/exam-page-axes.md`). The exam as the configurations it
  names or has tries of, `/<exam>`, and as of a time,
  `/<exam>/coverage?as_of=`; one configuration,
  `/<exam>/configuration/<key>`; one comparison,
  `/<exam>/compare?change=…&against=…&within=…&as_of=…` or
  `?configuration=<name>`. These are the addresses `proctor compare`
  and `proctor coverage` put in their answers. A try of such an exam is
  `/<exam>/try/<id>/<stage>`. A comparison pinned to a time shows the
  numbers as they stood then, and as they are now when tries were made
  since. No page holds an agent's prose; each says what to ask one.
- The stage page takes `?check=<name>` (and, with script, `?point=<id>`),
  so a link can point at one verdict or one friction point.
- `Auspex.Tests/`: starts the real server on a free port and crawls it.
  `AxesBank` is a proctor root with an exam of axes whose bank the
  proctor built beside the tests made; `AxesPagesTests` holds each page
  against what `proctor compare` and `proctor coverage` answer.

```
dotnet build                                   # at the repository's root: proctor and the viewer
dotnet test View/Auspex.Tests --no-build       # AxesPagesTests makes a bank with the proctor just built, so nb must be built too
AUSPEX_TEST_PUBLISH=1 AUSPEX_TEST_ROOT=<proctor root> dotnet test View/Auspex.Tests --no-build
dotnet View/bin/Debug/net10.0/auspex.dll <proctor root>
# no bank on hand: Proctor.Tests/exams/axes-answers/make.sh <empty dir> makes one, with axes, on Mock
```

Not built: the person's disposition on a point and the treatment record
(`project/loop.md`). auspex writes nothing yet.

## Read first

**Start with `project/where-we-are.md`** (2026-11-21): the moving
pieces, what is decided and what waits on the user, while they get back
up to speed. It supersedes anything below that it contradicts.

The design work in progress, in order (journeys, then the inventory,
then pages, whose mocks are a design canvas,
https://claude.ai/artifact/XE4Baec27NiQMhLuXh5a6Q):

- `project/journeys.md`: where the person is when they open auspex
  (2026-11-16, corrected by the user; their notes are the quoted lines
  and settle the guesses). Journey 2, "it passed and I do not believe
  it", is the centre; the coding session acts and auspex shows; no
  trajectory compared across several variants.
- `project/inventory.md`: superseded (2026-11-22), never revised; it
  predates the bank and speaks of batches. The page docs and the build
  plan cover its first pass. Read it only for the marks proposal (one
  address, four writers) and its open questions on dispositions, which
  wait for build step 6.

- `project/build-plan.md`: the order of work from the designs to
  running pages (2026-11-22, proposed): onto the bank, the Web Awesome
  skeleton, the stage page on today's attempts, then stages and the
  exam page as proctor delivers them.
- `project/exam-page.md`: the page for one exam (2026-11-22, written
  from artboard 1 as reviewed, for the user to confirm). Setups against
  live on correctness checks held and flagged steps per try, setup tabs,
  the stage-by-try grid, friction per kind, the agent's findings, every
  try.
- `project/stage-page.md`: the page for one stage of a try (2026-11-22,
  agreed apart from typography and colour). Stage tabs, a checks pane
  beside the transcript, failure first whenever a check failed. Asks
  proctor for `why`, `describes` and evidence on each verdict
  (proctor's `project/plans/check-evidence.md`).

The model and the direction:

- `project/model.md`: the data model from first principles (2026-11-15,
  agreed). Exam, task, check, variant, attempt, claim; the two modes of
  use (finding a first pass, changing something live); what becomes of
  proctor's suite, arm, experiment and baseline. Built in proctor on
  branch `exam-variant-attempt` except claims; since then proctor's
  `project/plans/bank.md` replaced the batch with the bank and a log
  entry per run. Proctor's `project/plans/exam-variant-attempt.md` is
  the model as files and commands.
- `project/loop.md`: the current direction (2026-11-14). The loop from
  coded friction to a check to a treatment, the file contract with
  proctor (`friction.json` per attempt), and what auspex owns: the
  review view, the person's dispositions, the treatment record.
- `project/direction.md`: superseded in part by `loop.md` (2026-11-12).
  Keep it for the reasoning: the model writes the account of a run, and
  every number is a query and every quote checked. Its toolset (event
  table, operators, normalisers, citation checks, kept queries) now
  belongs in proctor.
- `project/regimes.md`: what a person needs after a run of attempts
  (2026-11-12, quiet regime revised 2026-11-14). Six arrival states,
  three regimes (loud, rare, quiet), and the price to chase a rare
  failure. Quiet friction is two kinds: a scent that ends as a check,
  and the rest, which a practitioner judges with the ledger as an aid.
  `project/trials/` holds the throwaway scripts behind its numbers.
- `project/surprise.md`: a research direction (2026-11-12, first test
  2026-11-13). Per-step "how surprising was that result", next to
  friction; five candidate measures and how each sits with the rules
  below.
- `project/surface.md`: proposed (2026-11-14). An exam-level
  declaration of the surface under test and its substitutes, owned by
  proctor and read by auspex, so an attempt's steps sort by rule into
  on, about and around the surface, and background.
- `project/friction-contract.md`: built and measured (2026-11-14). The
  `friction.json` file per attempt that the viewer and proctor depend
  on, and how good the detector behind it is: rules
  (`project/trials/friction.py`) plus a reader stage, which is Haiku
  (decided; three local models fell short). Tested on real runs with a
  way round the surface (`tern-docs`). The rules are ported to proctor
  (`proctor friction`), which is where a rule changes from now on.
- `project/off-path.md`: hypothesis (2026-11-13). Frontier agents pass
  given bash, so the case that needs analysis and a UI is a pass that
  left the surface under test (raw SQL in place of the CLI). Drill-in
  from a failed check is kept there as a minor case.
- `project/demo.md`: nice to have (2026-11-13). A static read-only demo
  for people trying auspex and for employers; writes to local storage, no
  runtime model. Lists the choices that keep it cheap (one built file,
  the local reader behind a read-only flag, writes as plain documents).
- `project/workbench.md`: the earlier plan, moved from proctor
  uncommitted. Its step 4 ("the reading tool waits on beats") is
  superseded by `direction.md`. Paths in it like `todo.md`, `lab.md` and
  `project/lab/` are proctor's.
- `project/learnings/`: notes on prior art, one per source, each with the
  user's take in the frontmatter and what the source actually says
  checked against it. ET², InconLens, Docent, HoneyHive, ATO timelines,
  randomness in agentic evals, Inspect Petri, bloom-viewer (with Bloom's
  judge), process mining, Suchman's Plans and Situated Actions, studies
  of where programmers' time goes, Rasmussen on electronic trouble
  shooting. One is our own measurement, not a source:
  `off-family-friction-reader.md` (Haiku kept as the friction reader;
  no sign it favours Claude's runs, and the task may be less like
  judging than assumed).
- `datasets/README.md` and `datasets/INDEX.md`: 31 proctor experiments
  in the old layout, copied on 2026-11-12, frozen.

Background that stays in proctor's repo, read it there:

- `project/todo.md`, the sections "Beats", "Beats, second cut", "Three
  things a reader does with trajectories" and "An analyst over the
  transcripts".
- `project/lab/observe.py` and `project/lab/trials/`: the throwaway
  beat-cutting trials, the seed for the code.
- `project/plans/on-disk-layout.md` and `project/plans/bank.md`: the
  layout proctor writes now (the bank and the log).
- `Run/Ingest.cs`, `Ingest.Kinds`: proctor's agent-neutral tool kinds per
  agent, and how a Qwen Code `tool_call` wrapper is unwrapped.

## Vocabulary

Proctor's, fixed in its `CLAUDE.md` on branch `exam-variant-attempt`
(from `project/plans/exam-variant-attempt.md`, 2026-11-15), and used here
the same way:

- An **exam** is the question being answered, its tasks, and the checks
  that grade them.
- A **task** is one prompt against a **fixture**, the repository it runs
  against.
- A **check** is one yes-or-no grading question asked of a finished
  attempt; where it is listed sets its role (`correctness`, `integrity`,
  `quality`, or in no list an observation).
- A **variant** is what is under test: an agent, a model and a bundle.
- An **attempt** is one variant at one task, once, with its evidence.
  Several per task, because the agent is stochastic.
- The **bank** is every attempt of an exam
  (`bank/<exam>/<variant>/<task>/<n>/`). What one `proctor run` did is a
  **log entry**, a record, never a grouping a person reads results by.
- **Live** marks the variant in use now, the reference a change is
  compared with. A **claim** is what should change between two variants,
  said before the attempts are made (designed, not built).

The old words (suite, arm, experiment, sample, cell, baseline, batch)
name the old layout only: `datasets/`, the viewer's code and its pages
until they move. Use them for those paths and nothing else, and write
nothing new in them.

auspex's own words:

- A **step** is an ATIF step, by its original id. Anything a person or a
  model says points back at one.
- A **beat** is a stretch of an attempt: orient, then one per iteration,
  then wrap. It is a derived column now, not the unit a reader reads,
  and it is never used for measurement.

## Rules carried over from proctor

- **No count from a model.** Every number in an analysis comes from a
  query that can be rerun. Every quote a model gives is checked against
  the step it cites: a substring match with all whitespace removed, the
  same rule as proctor's judge. Prose is fine; an unchecked count or
  quote is not.
- **Plumbing costs $0.** Building and testing tools runs on the copied
  datasets, the scripted runs, or local models on `imp`, never a hosted
  model or a paid judge. Model work done for analysis (summaries, labels)
  happens in-session or through a subagent, never through the minrouter
  gateway, whose daily caps are budget guardrails. A hosted model runs
  only when the question is about that model, and its spend is agreed
  first.
- **Embeddings do not group anything.** At these sizes they are unstable,
  they group by syntax, and a cluster cannot be defended. They are kept
  for discovery only.
- **Scripted runs are controls.** `smoke`, `gate-oracle`, `playground`
  and `conformance` answer from Mock or the stub model. Use them to test
  plumbing, never as findings about agents.

## Working with the user

- **Private material stays in `/srv/aiwork/proctor-private/`.** That
  covers anything not meant for a public repo: session notes and job
  framing. `datasets/proctor-private/` is private as well.
- **Public copy:**
  - say evals, measure and grade, never "AI testing";
  - never name Grafana, the user's employer or the employer's product,
    anonymising any precedent from them.
- **Clickable links:** opening files with `xdg-open` does nothing here.
  Give every page or rendering as a bare URL or path on its own line.
  The scratchpad cannot be read by the user, so copy renderings to
  `/srv/aiwork/proctor-private/views/`.
- **GitHub:** show the exact text of any issue or comment before posting
  it as the user, and file an issue only for a confirmed bug.
