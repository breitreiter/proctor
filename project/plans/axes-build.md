---
type: plan
title: Building axes — the order of work, and what each slice should teach us
created: 2026-11-24
status: proposed 2026-11-24, for the user. Slices 0 to 2 and proctor's half of slice 3 are built. Revised later the same day for `axes.md` decisions 13 to 16 and its "Who asks, and through what": slices 3a to 3d are new, the page of slice 3 is no longer where the model is judged, and `run` filling every configuration was changed in 3a (built). What is new is marked "mine" where the user has not said it. The model is `axes.md`; this is only the order it is built in and where we stop to look. It takes over what `bank.md` step 4 left open (the eleven exams still on tasks, deleting tasks) and puts the viewer's exam page after the first lesson. The move-in and the rename (`rename-to-auspex.md`) and hosting (the hosting plan, kept outside this repository) are deferred until this is done (the user, 2026-11-24: they are administrative and teach nothing)
---

# Building axes

## What this plan is for

`axes.md` is a model nobody has used. The fastest way to learn whether
it is right is to put real exams through it, cheapest test first, and to
say beforehand what would count against it. So the slices are ordered
by what they teach, not by what the code needs first, and each ends
with something to look at.

Nothing is shown to anyone before 2026-11-29. The aim by then was slices
0 to 3, with a page to judge the model by. Slices 0 to 2 and the report
of slice 3 are done. The aim now (mine, to confirm): slices 3a to 3c, so
the model has been tried on one exam for real with each value reviewed,
and an agent has been asked the reader's questions from proctor's
answers alone.

## Revised 2026-11-24: what changed, and why

Four things were said after slices 1 to 3 were built (`axes.md`,
decisions 13 to 16):

- **The reader is a product manager with an exam six months old, and
  proctor does not do decision support** (`axes.md`, decision 17).
  Proctor is the source of truth, a layer for inspecting the evidence,
  and a data access layer for Claude and what Claude builds. So the
  thing to get right first is what proctor answers when asked, and the
  page comes after it.
- **An axis is not narrow.** A dozen axes is ordinary, so nobody fills
  or reads the cross product. `run` makes what somebody named.
- **A value is built alone and what it changed is reviewed against its
  sentence.** This replaces "every value's diff is on the page", and
  the tab the earlier plan called "the exam as given" is gone.
- **A script that reads several axes is unsafe code**: allowed, never
  refused, and outside what the reviews cover. A marker comes later.

What that does to this plan:

| Was | Is |
|---|---|
| `run` fills every configuration not excluded (built) | `run` makes the configurations named, and a value alone against the reference (3a) |
| each value's diff shown beside the attempt | each value built alone and its change shown, for whoever asks to read against its note (3b) |
| the exam page is where the model is judged (slice 3) | the answer to a query is (3c); the page shrinks to what tells a reader what to ask (3d) |
| signs 4, 5 and 7 tested on a page | tested on an answer read by an agent, then on the page |
| the stop for the user after the page | after 3c |

"Reference" below is the value every other axis is held at when one
value is looked at: the live value where the axis has one, and
otherwise a value the axis names for the purpose (`axes.md`, OPEN 5).

"Variant" below means only what proctor has today.

## What would count against the model

Written before any of it is built, so a bad result is recognised and
not explained away. Any of these is a reason to stop and talk:

1. **Exams that do not fit.** More than one or two of the thirteen
   exams need something `axes.md` forbids (a value that adds a step, a
   hook that must branch on a value, two axes that must touch one file).
2. **Refusal 2 blocks honest work.** Values of different axes
   legitimately need the same path or variable, and the author is
   pushed to merge two axes into one to get past it, or into a script
   that reads both (decision 16). If most real exams end up with one,
   the safe form is not the ordinary one and the reviews cover little.
3. **An axis without a live value turns out to need a kind after all.**
   Reports or `run` keep needing to know "is this one ours or the
   customers'" beyond whether a live value is set.
4. **Live across several configurations cannot be read.** With two
   swept axes, a reader cannot say what "against live" compared. Since
   the revision the first reader is an agent given the answer to a
   query (3c): it counts against the model if the agent states a
   comparison wider than its scope.
5. **`run` cannot be told which part to make** without a long command.
   (The other half, "the grid does not scale to three axes", is given
   up as a test: with a dozen axes nobody reads the grid, decision 14.)
6. **Authors reach for the old split.** Writing an exam, we keep
   wanting "the bundle" back as a separate thing.
7. **The states multiply past reading.** Pass, fail, undecided, not
   applicable, not yet graded, excluded, not run, lost, stale: if one
   configuration's state needs a legend to read, the page has failed,
   whatever the model. For an answer (3c): if the agent reads not run
   as passed, or the same as better.
8. **Building a value alone does not show what it does** (added with
   3b). The change is too large or too noisy to hold against a
   sentence, on more than the odd value: a build that does not repeat,
   a value whose whole effect is inside a service.
9. **Nobody builds a value before running it.** Proctor keeps no
   record of who looked (3b), so this cannot be counted; it shows as
   values whose notes turn out wrong after tries were paid for.
10. **The combination check cries wolf.** It reports configurations as
    more than their values put together on exams where no script reads
    two axes.

The opposite result is also worth naming: every exam ports with no
hook left that branches on what is under test, and the three cases in
`axes.md` ("The problem") each come out as one exam with no copies.

## How it is built: beside the old path, as stages were

An exam is written one of two ways, never mixed:

| | Old | New |
|---|---|---|
| Work | `tasks/` | `stages` |
| What is under test | `variants/`, a bundle | `axes` in `exam.json` |

An exam with `axes` must be an exam of stages. That keeps two paths in
the code, not four, and the old one is deleted whole at the end (slice
7). `readme-answers` and `tern-docs` are stages on variants today; they
are the first two ported, and no bank of either exists, so nothing is
lost.

## Slice 0: every exam on paper

**No code.** For each of the thirteen exams, write its axis
declarations as they would stand in `exam.json`: the axes, each value
and what it sets, the live value where there is one, the base. Into one
document, `project/plans/axes-ports.md`, with a line per exam on what
did not fit.

This also settles the declaration's form, which `axes.md` leaves to the
build: what a value looks like for each kind (files, image argument,
variables, service parameter, agent and model), and how a named
configuration and an exclusion are written.

What is already known to be awkward, from the files (2026-11-24):

- **Four exams have a variant hook** (`agent-onboarding`, `code-change`,
  `nb-changes`, `exam-authoring`), and `smoke` and `exam-authoring` an
  attempt hook. Each must be read: does it branch on the variant? If so
  it is the case `axes.md` warns of, and the port says where that
  difference goes.
- **`exam-authoring`** has two variants that differ only in what a hook
  builds, plus a runner and mounts on the exam.
- **`tern-docs`'s bundle reaches the agent through a service** (the
  docs site serves it), not through the checkout: a value of the
  service kind.
- **`readme-answers`'s bundle is read by the program**
  (`system @{{bundle}}/README.md`), on a fixture with nothing in it. As
  a file overlay it would land in the checkout; the program then reads
  it from `{{work}}`.
- **Bare or through Harbor** is today a variant field (`agent`,
  `runner`). `gate`, `gate-oracle` and `oracle-change` exist to compare
  the two routes on one model, so the route is itself an axis there.
- **Provider** rides with model on bare variants. One axis or two?

**Look at:** the ports document. **It teaches:** signs 1, 2 and 6,
before a line of C#. **Stop for the user** here.

## Slice 1: declared, validated, listed

- `axes` in `exam.json`: loaded, with a value's content hashed and a
  configuration's identity its values.
- The refusals of `axes.md` ("What proctor enforces", 1 to 4), each at
  load, each naming the file and field: a value touching the answer
  key, two axes on one path, variable or parameter, a parameter the
  base does not declare, a condition or exclusion naming something
  undeclared.
- `proctor list <exam>`: the axes, the live values, and the
  configurations as a grid of wanted against made (all empty for now),
  excluded ones marked.
- How `run` is told which part to fill: by value (`--value go=1.20`),
  by named configuration, or everything. Decided here, on paper first.

**BUILT 2026-11-24**, to the declaration in `axes-ports.md` (parameters
and the base's scripts, not kinds; a world axis and `when` on a stage,
from the user's answer B). `Exam/Axes.cs`; `params` in `fixture.json`
and `exam.json`; `list` shows two varied axes as a grid and anything
else as a row per configuration. `run`, `grade`, `friction`, `report`
and `live` refuse an exam with axes until slice 2. Who does the work in
a configuration is handed to the rest of the loader as a variant was,
so Harbor's, the oracle's and the template's rules apply unchanged.
Tried on four real exams ported in a scratch copy (`readme-answers`,
`tern-docs`, `code-change` with its repositories as the world axis,
`nb-changes` with the planted defect as one image parameter): all load.
No exam in the repository is ported yet, since a ported exam cannot run
before slice 2.

Not built here, and why:

- A git source's paths are not checked against another axis's overlay
  (only directories are), since that needs the revision fetched.
- Some messages about who does the work still say "variant", because
  they are the variant's own validators. They change in slice 7.
- A condition is `axis = value` or `!=`, joined by `and`. No ordering
  (`go >= 1.21`): a value is a name.

**Telling `run` which part to fill (decided on paper, built in slice
2, and to change in 3a):** `proctor run <exam>` fills every
configuration not excluded.
`--value docs=for-scripts` (repeatable) narrows to the configurations
holding every value named; `--configuration shipped` to one by name.
`--variant` and `--tasks` are refused on an exam with axes.

**Look at:** `list` on `readme-answers` and `tern-docs` as ported.
**It teaches:** whether the declaration is pleasant to write and
whether the refusals fire on real mistakes and only those (sign 2).

## Slice 2: one configuration run bare

- The file overlay (applied after checkout, before the first stage)
  and environment values. Model and provider as values.
- An attempt is a configuration at a try. **Its id is unique when
  made** (time and four random characters; the hosting plan, kept outside this repository, "The
  shared bank", 1), so the bank's layout changes once more:
  `bank/<exam>/<attempt id>/`, its manifest naming the values and their
  hashes. Which try of its configuration it is, is counted when read.
- The manifest records a hash of the final workspace (`axes.md`,
  decision 5). Keeping the contents is not built yet.
- A changed value under a kept name is refused, as a changed variant
  is today.
- Grading and friction read the new layout; nothing else about them
  changes yet.

The exam: `readme-answers` with `docs: as-shipped | for-scripts` (live
`as-shipped`) and a second axis so there is a grid to look at. On the
shakedown the second axis is two Mock providers, so a 2 × 2 costs
nothing.

**BUILT 2026-11-24**, bare only.

- A try of a configuration runs the stages whose condition holds in
  it. `run`, `grade` and `friction` read an exam with axes; `--value`
  and `--configuration` narrow them. `report` and `live` still refuse
  (slice 3; live is declared on the axis).
- The bank: `bank/<exam>/<try id>/stages/<i>-<id>/`, the id made of
  the time and four random characters. Each stage's manifest names the
  try, the value on each axis by name and hash, and a hash of the
  workspace as the stage left it. The checkout is
  `.proctor/work/<exam>/<try id>`.
- Values' files are laid over the checkout and committed before the
  first stage, at run and again when grading rebuilds a stage's
  workspace. Parameters reach the program as `{{param.NAME}}` and hooks
  and checks as `PROCTOR_PARAM_<NAME>`; an axis's value as
  `{{axis.name}}` and `PROCTOR_AXIS_<NAME>`.
- A value whose content changed under the name it made attempts by is
  refused, naming the axis and value. A removed value's attempts are
  stale.
- `list` counts tries made per configuration.

Shortcuts taken, to be undone when they matter:

- **The bank is still read through the log**, and a try still has a
  number there and in its manifest (`attempt`), beside its id. Reading
  the bank from its own files, with the number counted when read, waits
  for hosting, which is what needs it.
- **Inside the code a configuration is carried as a variant whose id is
  the configuration's key** (`docs=shipped,assistant=mock`), so the
  manifest's `variant` holds that key and log lines print it. Slice 7
  renames it.
- **A stage belongs to one world.** On an exam with a world axis every
  stage must name its world in `when`; a stage that runs in several is
  refused.
- **Not run yet:** a configuration through Harbor (slice 4), and a
  value whose files come from a git revision. Both are refused by `run`
  with the reason.
- A secret parameter is read from the environment variable its value
  names; nothing checks yet that it stays out of the bank's files.

Tried for real on a scratch port of `readme-answers` as docs by two
Mock assistants: two configurations run with `--value docs=for-scripts`,
graded 24 of 24, the grid in `list` filled for that row only, and an
edit to the value's README refused on the next `run`.

**Look at:** the bank after a shakedown run. **It teaches:** whether
"every change is a value" survives contact with a program template,
and whether the layout by attempt id is workable for a person at a
terminal.

## Slice 3: the report and the page, on that grid

The first legibility test, deliberately before Harbor.

- `proctor report`: the coverage grid (made, thin, stale, empty,
  excluded), and each axis with a live value compared along it, the
  other axes held, with the scope written out: which configurations
  the comparison covered and which it did not. A configuration not run
  is listed as not run. Pooling across an axis is never silent. The
  file is a new schema beside `tries/1`.
- In the viewer (`/srv/aiwork/auspex`): the bank by attempt id, the
  stage page's address, and **the exam page**. As first written the
  stage page had an "exam as given" showing each value's diff and the
  exam page led with the grid; both are withdrawn (see "Revised"), and
  the viewer's work is slice 3d.

**Proctor's half BUILT 2026-11-24.** `proctor report` on an exam with
axes writes `stats.json` as `configurations/1` (`Report/Configurations.cs`):

- every configuration as a row with its state (`made`, `partial`,
  `not_run`, `excluded`), its tries, lost and stale counts, and its
  tries as a staged exam's variant has them;
- for each axis with a live value, each other value against it: one
  cell per configuration of the other axes, each compared by
  Mann–Whitney or marked not run (and which side), and the cells read
  as one;
- a sentence per comparison that states its scope and its gaps.

**The pooled comparison is the van Elteren test**: Mann–Whitney inside
each cell, the cells' statistics added with weight 1/(n+m+1), the p
exact while the arrangements stay under 400,000. A try is only ranked
against tries of its own cell, so worlds that differ from each other do
not read as a difference between values. This is my choice, for the
user to confirm; it is the standard stratified form of the test already
in use. It reports whether the tries made could confirm anything: one
try a side in three cells cannot (p is at least 0.25), which is
`code-change`'s shape today.

Tried on the scratch port of `readme-answers` (two docs by two Mock
assistants, three tries each, a scripted check that fails where the
README does not say the answer): with one live cell empty the report
said so and compared the other alone (p = 0.1, cannot confirm); filled,
the two cells read as one confirmed it (p = 0.005). A copy is in
`/srv/aiwork/proctor-private/views/axes-sample/`.

**The viewer's half is slice 3d.** auspex's `project/exam-page-axes.md`
was proposed for it before the revision and two of its points no longer
hold ("the grid leads", "the exam as given").

**Still owed in the report** (mine, from `axes.md`, "From how others
handle this"): configurations that disagree cancel when read as one,
and the report has no word for "better here, worse there". It gets one
in 3c, where the answer is defined.

**It teaches:** whether the comparison along an axis can be computed
and scoped at all. Signs 4, 5 and 7 move to 3c and 3d.

## Slice 3a: `run` makes what is named

Changes what slice 2 built. Small, and first, because every later
slice runs configurations.

- `proctor run <exam>` makes the configurations the exam names
  (`configurations` in `exam.json`), and no others. An exam that names
  none is told to name one.
- `--configuration <name>` makes one of them.
- `--value docs=for-scripts` (repeatable) makes the configuration with
  those values and every other axis at its reference: the value alone,
  as 3b builds it. Today it narrows the cross product instead.
- An axis with no live value names its reference (`reference` on the
  axis; refused when an axis has neither, once anything needs it).
- `run` opens with how many configurations and tries it will make, as
  it does today, so a wide request is seen before it is spent.
- `exclude` stays, for a named configuration or a `--value` request
  that cannot exist. `list` shows the named configurations and those
  with tries, as rows; the grid of two varied axes goes.
- `grade`, `friction` and `report` read what was made; `--value` on
  them still narrows.

**BUILT 2026-11-24.**

- `Axes.Configurations` is what the exam carries: named, asked for, or
  with tries in its bank (read from the log entries when the exam
  loads). Any other is made from its values when asked for
  (`Axes.Make`, `Axes.Alone`); the cross product is never built.
- `run` as above. `--value` and `--configuration` together are
  refused. `"reference"` on an axis; an axis of one value needs none.
- A configuration made by `--value` and not named stays carried, so
  its tries are current and are read; a plain `run` leaves it alone.
- At load each value is also checked alone, where every other axis has
  a reference, so a value nobody has named is still validated once.
- `list`: a row per carried configuration, "not named" where it has no
  name, the exclusions as lines.
- The report: rows are the carried configurations; `excluded` is a
  list of the declared exclusions, not a state.

Decided while building, and confirmed by the user 2026-11-24:

- **A comparison looks only where its value was named or made.** The
  first cut also listed every setting where only the live side
  existed as "not run", which on six axes was a line per made
  configuration per value. A value nobody tried anywhere reads
  "nothing to compare yet".
- **A named configuration with no stage is refused where it is
  named.** One asked for by `--value` with no stage is refused then.
- **An exam whose every configuration is excluded** is no longer
  caught at load, since that needed the cross product.

Tried on a scripted exam of six axes of two values (64 configurations,
one named, one exclusion): `list` shows one row, `run` makes one try,
`--value tone=terse` makes that value alone, an axis with no reference
is named in the refusal, an excluded request is refused, and the
report compares the two values that were made. The scratch port of
`readme-answers` did not survive the machine's restart, so it was not
tried there.

**Look at:** `list` and `run` on a scripted exam with six axes of two
values. **It teaches:** sign 5: one flag per value changed from the
reference, so a configuration three values away is three flags or a
name.

## Slice 3b: a value built alone

Decision 15, on the bare path. Free: nothing here calls a model.

- **The starting state is recorded.** Today the checkout is committed
  after the values' files are laid and before the setup hook, so what
  a hook does with a parameter is not in it. The commit moves to after
  setup, and its tree hash goes in the try's manifest. This also makes
  a stage's diff what the agent changed and nothing a hook did.
- **A verb builds without running** (`proctor build <exam>`): checkout, values' files, setup, record, teardown.
  With `--value` it builds that value with every other axis at its
  reference; with none, the reference configuration, each value of
  each axis alone, and each named configuration.
- **What it writes, per value:** what changed from the reference
  build, as the list of paths added, changed and removed with sizes,
  the text diff where it is small text, and the parameters set. Kept
  under `.proctor/`, by the value's hash and the base's.
- **The reference is built twice first.** Paths that differ between
  two builds of the same thing are noise, listed as such and left out
  of every value's change.
- **The combination check.** For each named configuration: is what it
  changed the changes of its values put together? Where it is not,
  `build` says which paths, and that some script reads more than one
  axis. A report and never a refusal (decision 16).
- **No record of a review** (the user, 2026-11-24; this replaces the
  bullet first written here, which was built and then removed). The
  first form kept who looked and what they found in a committed file.
  Any way of showing that is either misleading, since it reads as
  review and approval, or useless, since all it proves is that a
  command was run. So `build` shows the change, whoever asked reads
  it against the note, and proctor records nothing about that.
- **`note` on every value and `about` on every axis are required** on
  an exam with axes, since the note is the claim the change is held
  against.
- **`list` and the report say what is unsafe**: a configuration the
  combination check flagged, or never checked. Each comparison that
  reads one says so in its scope. This is a measurement of proctor's,
  kept in `exams/<exam>/combinations.json`.

Not here: an image and a service (slice 4, where they first exist).

**BUILT 2026-11-24**, bare only (`Run/Build.cs`, `Exam/Combinations.cs`).

- `proctor build <exam>` as above, and `--value` for one value with
  its text. It runs the variant and the attempt hooks, setup then
  teardown, around each build, and nothing else. Each value's change is
  left as `.proctor/builds/<exam>/<axis>=<value>.json`.
- The starting state: `Checkout.CommitSetup` after the setup hook, in a
  try and in a build alike; `start_tree` in each stage's manifest; the
  hook's changes kept as `setup.patch` in the try, which a regrade
  applies before the stage's diff.
- `combinations.json` holds the check of each several-valued
  configuration, written by `build` when it changes.
- `list`, the report's rows and every comparison's sentence carry
  which configurations are more than their values, cannot be told, or
  were never checked (`cautions` on a comparison in `stats.json`).

Decided while building, to confirm:

- **What a combination's check was made against** ("the base's
  hash") is the reference configuration, its fixture, the declared
  parameters and the hook scripts. A prompt or a check can change
  without asking for it again; a hook cannot.
- **A third answer from the combination check: cannot be told.** Two
  values that each change the same file are not a fault when they
  change different lines (one prompt file written from several
  parameters), so their changes are merged line by line over the
  reference and held against what was built. When they change the
  same or neighbouring lines, a merge means nothing, and the check
  says so in place of guessing. This is sign 10 met once already: the
  first cut called every such file a script reading two axes.
- **Which configurations are checked:** every one the exam carries
  that is more than one value off the reference, named or made.
- **The starting commit moved for every bare staged try,** with axes
  or without, since a stage's diff meaning the agent's changes is
  right for both. Tries by task are untouched (they go in slice 7).
- **`start_tree` is the git tree of that commit:** tracked files only,
  so restored packages and build output a `.gitignore` covers are in
  no value's change.
- **The latest change of a value is kept, not one per hash.** Nothing
  reads an old one.
- **`build --value` takes one value.** Several together are a
  configuration: named, and checked by a plain `build`.
- **Built and removed the same day:** `--reviewed`, `--by`, `--found`,
  `reviews.json`, and "not reviewed" in `list` and the report. See "No
  record of a review" above.

Tried on the six-axis scripted exam with a setup hook that writes a
prompt file from the parameters: a value that also rewrote another
file showed both paths; a line written only when two parameters were
both set was found in the named configuration that holds both, and
not in the others; a file stamped with the time was left out as the
build's own. Not tried on `readme-answers` or `nb-changes` as ported:
the scratch ports did not survive the machine's restart.

**Look at:** `build` on the scratch port of `readme-answers`; on a
copy with a value that quietly does a second thing; on a copy with a
hook that reads two axes; on `nb-changes` as ported, whose value is
one image parameter, to see how little the bare path can say there.
**It teaches:** signs 8 and 10, and whether a note can be held against
a change by an agent without a person.

## Slice 3c: the query and its answer

The data access layer, and the legibility test moved from the page to
the answer. This is now the centre of the plan (`axes.md`, decision
17). It needs no server: the test is whether an agent with only
proctor's answers says the right things, and an agent in a terminal
can call a verb. The same queries through an MCP server over a hosted
bank, for Claude on the desktop, follow with hosting
(the hosting plan, kept outside this repository) and are not built here.

The queries answer with what was measured. They never recommend.

- **One query** (mine): along this axis, these two values, holding
  these axes at these values, read as one across those. `proctor
  report` already computes it for every axis with a live value; this
  asks for one, as JSON on standard output, from the same code. No
  number is worked out anywhere else.
- **The answer carries its cautions** (`axes.md`, "What follows", 2):
  its scope in words; every configuration in scope not run, as a row;
  what it was read as one across; when each side's tries were made and
  how far apart; how small a difference it could have seen; how many
  other comparisons this report holds; which configurations in it are
  more than their values, cannot be told, or were never checked (3b).
- **A word for disagreement.** When the configurations read as one
  point different ways, the answer says "better here, worse there" and
  names where, and gives no pooled verdict.
- **Two axes that only moved together.** When, among the tries in
  scope, two axes changed in the same configurations, the answer says
  the axis asked about cannot be told apart from the other.
- **Everything in the answer has an address** that the viewer opens to
  the same number: the comparison, a configuration, a try, a stage, a
  step, a verdict. The stage page lacks the selected check or friction
  point in its address; that is added in auspex here.
- **An address of a number says as of when** (agreed 2026-11-24). A
  comparison's numbers move as tries are added, so a link Claude gave
  last week would open on a number Claude never quoted. The address
  carries the time it was answered; the view shows the number as it
  was then and, when tries have been added since, as it is now.
- **A second query for coverage**: the named configurations and the
  ones live spans, each with its tries and their age. It answers the inside half of "where are our customers the only
  testing". The outside half needs where users are (slice 6a).

The test, on a scripted bank made for it: three axes; holes; one value
that is better in one world and worse in another; two axes that only
moved together; one configuration more than its values; one difference too small for the
tries made to see. A fresh agent is given the skill, the two queries
and the reader's three questions (`axes.md`, "The reader"), and
nothing else. Its answer is read against what the bank holds.

**BUILT 2026-11-24**, proctor's half (`Report/Answers.cs`,
`skills/proctor-answers/`, `Proctor.Tests/exams/axes-answers/`).

- `proctor compare <exam> <axis>=<value>` with `--against`, `--within`
  and `--as-of`; `proctor coverage <exam>`. JSON, schema `answer/1`.
  Both come from `Configurations.Compare`, which the report now calls
  too, so a number is worked out in one place.
- A comparison answers with: each setting and its tries, each try's
  id, time, what it held, an address and its directory; per headline
  check, how many tries of each side held it; what was not run; the
  reading as one; what the counts of tries could have shown; when each
  side was made and how far apart; any axis that only moved with this
  one; the configurations in it that are more than their values; the
  other comparisons the report holds, by name; `as_of` and the tries
  made since.
- "Better here, worse there" is a verdict of its own, in the report
  too, and such a comparison is not read as one.
- `compare` is refused while a try in it is not graded.

Not built, and why:

- **The viewer does not open these addresses yet.** They are written
  (`/<exam>/compare?…&as_of=…`, `/<exam>/configuration/<key>`,
  `/<exam>/try/<id>`), and each try also carries its directory for
  whoever has the files. auspex still reads the bank by variant and
  number, so reading it by try id, the comparison's page and the
  selected check in a stage's address are all slice 3d.
- **"How small a difference it could have seen"** is given as the
  smallest p the counts of tries can reach, in words. A rank test has
  no size of difference to state.
- **Two sides made at different times** could not be planted: a try's
  time is in its id, and the scripted bank is made in one go.

**The test, 2026-11-24.** `axes-answers` is a scripted exam on Mock
whose checks pass at a chance set by the configuration; `make.sh`
makes its bank (28 tries, two minutes, no model). Planted: the
rewritten guide better on one assistant and worse on the other; tone
and examples only ever changed together; a named configuration never
run; a configuration with a hook line that reads two axes; a length
difference too small for four tries. A fresh agent was given the
skill, the three verbs and the reader's three questions, and told to
read no files.

What it did:

- **All five found and none overstated.** It gave no overall result
  for the rewritten guide and said where it was better and worse; it
  said tone and examples "cannot be judged at all"; it named the
  configuration never run; it said the combined configuration is not
  predicted by its values; it called the length result "better, not
  confirmed" and recommended against shipping on it.
- **No number of its own.** Where it wanted a rate it listed the
  tries' counts and said there was no rate.
- **Findings and recommendation kept apart,** and it asked for the
  four things proctor cannot know (share of users per assistant,
  whether a flag can be aimed, how fast a release is undone, what a
  bad answer costs).
- **One reading slightly generous:** it called the rewritten guide on
  the first assistant "the one change with evidence behind it", where
  the plain setting was not confirmed alone (p = 0.086) and the
  confirmed one is the configuration that is more than its values. It
  was marked as its own recommendation.
- **It said the exam is scripted, first,** from the description `list`
  prints, and asked for a real one.

What it reported about the tools, and what was done:

| It said | Done |
|---|---|
| top-level `confirmed: false` beside "better here, worse there" misreads, since cells were confirmed | absent now when there is no one reading |
| no way to see which check a value fails | per-check counts in each compared setting |
| `other_comparisons` a bare number | the comparisons by name |
| `together`, `flagged`, `listed`, and live against reference not explained | in each answer's `methods` |
| addresses escaped (`\u0026`) | written plain |
| the skill did not say how to point at a repository | it does |
| `list` says attempts, the answers say tries | left: the words change together in slice 7 |
| **no way to compare a configuration with what is live** (`rewritten-long` against `shipped`), which is what "what should we ship" asks | **not built: see OPEN** |
| configurations nobody named are invisible in `coverage` | left: nobody spans the cross product; `methods` says so |

The user on the answer's shape (2026-11-24): directionally right. The
skill is ongoing work, shaped by surprises in how it is used and how
it behaves on new data, not by how the model behaves. So no exam of
the skill yet (this settles "Decided", 8).

Against the signs: 4 (a comparison stated wider than its scope) did
not happen. 7 (not run read as passed, the same read as better) did
not happen. One trial of one agent on one bank is not much.

**Look at:** that agent's answer, and each link in it. **It teaches:**
signs 4 and 7, and whether "Claude says, proctor counts" holds: every
number in the answer is one proctor gave, and nothing is claimed past
its scope. **Stop for the user** here, the only stop left; this is the
point to judge the abstraction.

If the user wants real numbers under it, `readme-answers` on Gemini's
free tier is the run to make then (it needs `proctor setup` with a key,
which only the user can do).

## Slice 3d: the pages

In auspex, after the user's answer to 3c. `exam-page-axes.md` is
rewritten first, as a short proposal.

- The bank read by try id; "setup" becomes configuration.
- **The stage page** stays as built. Where a value is named it shows
  the author's sentence, the value's extent (files, parameters, hash,
  the revision it came from), each marked as whose it is. No diff of a database or a site.
- **The exam page** is rows, one per configuration somebody named or
  ran, never the cross product. A comparison is a value against live
  with its scope, at the address 3c's answer links to. No section of
  agent-written findings (the user, 2026-11-24): Claude's prose lives
  in the conversation and in what Claude builds, and links in; an
  agent's mark on a step stays, since it points at evidence. So the
  mock's "Your agent's analysis" goes, "Did the change help?" is renamed to
  what the table is, and the friction table counts without ranking.
- Something on the page says what to ask Claude (the user). What it
  says is designed with the proposal.

**BUILT 2026-11-24**, as a first cut (`View/AxesPages.cs`,
`View/Measured.cs`; the proposal is `project/view/exam-page-axes.md`).

- **The viewer moved in first** (`rename-to-auspex.md`, stage 1, in
  part), so that a page can show proctor's own number by calling
  proctor's code. It computes nothing itself and calls no model.
- The bank is read by try id; a try is `/<exam>/try/<id>/<stage>`.
- The exam page is rows: the configurations named or run, and what is
  live across the axes with no live value. No grid.
- A comparison's page is at the address a `compare` answer gives. It
  shows proctor's sentence, its cautions, each setting with every
  try's count linked to the try, the counts per check, and the reading
  as one. Pinned to a time it shows that time's numbers, and the
  sentence as it is now when tries were made since.
- A configuration's page gives, per value, the author's note (marked
  as the author's), what the value carries and its hash; whether the
  configuration is its values put together; and its tries.
- The stage page links to its configuration and takes `?check=` (and
  `?point=` with script), so a link can point at one verdict.
- The exam page and the comparison page each say what to ask an agent,
  with the command it would run. No page holds an agent's prose.

Not built, or left for the user:

- **Nothing was drawn first.** The pages are tables in the viewer's
  existing type and colours. How they look is the user's to judge.
- **The stage-by-try grid, friction per kind and "every try"** of the
  earlier exam page are not on the new one: they read the report's
  `stats.json`, which these pages do not use yet.
- **A check's own address** selects a failed check on the server; a
  passed check or a friction point needs the page's script.
- `exam-page.md` and `where-we-are.md` still speak of setups.

Tried on the scripted bank: every linked page answers, and the
comparison of the rewritten guide shows the counts and p-values the
agent of 3c quoted.

**The user's first look, 2026-11-25, on six real tries of `tern-docs`:**
the parts make sense; the presentation is muddy, and the layout has a
lot of Claude's habits in it. It has not been held against what was
landed on in the design canvas. Deferred to a day of its own (the 26th
or 27th), likely a full day. Until then the pages stand as a first cut
that works and is not designed: what each page holds and its address
are the part to keep.

**It teaches:** sign 7 on a page, for the person debugging.

## Slice 4: through Harbor

- The agent as an axis: which route an attempt takes comes from a
  value. Image arguments the fixture's Dockerfile declares, service
  parameters, and the overlay inside the container.
- A configuration that cannot build is `failed` with the build's
  error. One declared impossible is excluded and never made.
- **A value built alone, through Harbor** (3b's other half): the image
  by digest, which says different and not how, so `build` also lists
  what the build was handed (its arguments); a service by whatever its
  fixture gives to dump it, and "not looked at" where it gives
  nothing. The reference image is built twice, as the checkout is.
- The exam: `tern-docs` (`docs` as a service value, `agent` as an
  axis), proved free with its shakedown and one run of nb as the agent
  in Docker. A small scripted control with an image argument stands in
  for the Go auditor's toolchain axis, since no such fixture exists.
- Still owed from step 4, and done here because the compile changes
  anyway: a one-stage exam with an oracle compiles as a plain Harbor
  task, so the oracle's bridge works.

**BUILT 2026-11-25**, the oracle item included (see "Not built").

- **A configuration is compiled into its Harbor task** (`HarborCompile.Job`
  and `Task`): the values' files into `environment/repo/` before the
  image commits it; each text parameter the image reads as `ARG NAME`
  ahead of the image's own steps; each tree parameter as
  `environment/params/<NAME>/`, empty when nothing sets it; the stages
  that apply as the steps; `PROCTOR_AXIS_*` and `PROCTOR_PARAM_*` in
  each step's verifier contract.
- **Who does the work is a value.** A vendor's agent always goes
  through Harbor; nb does when its value says `route: harbor`, and
  runs bare otherwise (`agent: nb` alone is bare, which the first run
  of the control showed by finishing in five seconds).
- **`build` reads a Harbor configuration's build context**: the
  compiled `environment/` of the reference and of each value alone. A
  toolchain value shows as one `ARG` line of the Dockerfile; a docs
  value as the pages under `params/DOCS/`.
- **A try whose image cannot be built is lost,** and its reason is the
  build's own last error, where it was the compose command Harbor ran.
- **`tern-docs` is ported** in the repository: `docs` sets the
  fixture's `DOCS` tree, which the docs service mounts; `assistant` is
  Claude Code on Sonnet. `fixtures/toolchain` and
  `Proctor.Tests/exams/axes-harbor` are the scripted control.

Run for real, in Docker, with no model:

- The control, nb on Mock as the agent: two configurations in 98 and
  44 seconds. Each image held its own toolchain (`/etc/toolchain`, read
  by the agent and by a check in the verifier), each checkout its own
  README, and the stage with a condition ran on one toolchain only.
- `tern-docs` from a scratch copy with one stage that opens the docs
  site and a value for nb on Mock: `docs=as-shipped` got Tern's front
  page, `docs=none` got a 404.
- `tern-docs`'s shakedown, all four modes, as ported.

Decided while building, to confirm:

- **A parameter is a build argument and nothing more.** `axes-ports.md`
  says a parameter is also handed over "in the attempt's environment".
  Through Harbor that would be the agent's own shell, and a parameter
  can name what is planted (`PLANTED=far-cause`), so it is not put
  there. The verifier gets it; the agent does not.
- **`build` builds no image.** The plan asked for the image by digest,
  built twice. A digest says only "different", and each build is
  minutes and a network. What the build is handed says how two
  configurations differ, at once and for nothing. What it cannot say
  is what a Dockerfile step did with an argument; the control holds
  that to account with a check in the verifier, and that is the advice
  for a real fixture. Sign 8 stands here: for a value whose whole
  effect is inside a `RUN` step, `build` shows one changed line.
- **A value that moves the work between the host and a container** is
  not compared path by path with the reference; `build` says so.
- **The value for nb on Mock is not in `tern-docs`.** It proved the
  exam in Docker from a scratch copy. In the exam it would sit on the
  assistant axis as a setting some user is in.

Not built:

- **A one-stage exam with an oracle as a plain Harbor task** was left
  out on the 24th and **built on the 25th** (the user: go ahead, and
  make sure transcripts show oracle turns accurately). A try that is
  one stage with an answer sheet is compiled as a plain task
  (`HarborCompile.Plain`) and run by the single-attempt path into the
  try's layout. On an exam with axes a try is the stages that apply in
  its configuration, so `gate-oracle` was ported with each ending as
  one stage on one value of an `ending` axis, and bare and Harbor as a
  `route` axis. The ported gate passes in Docker: for all three
  endings the two routes give the same counted core, the same checks,
  and the same user turns by text, source and sheet keys. The old gate
  was run first, as it stood, and passed.

  What the transcripts show, checked on four real tries: an oracle's
  answer is a user step marked as the oracle's with the sheet entries
  it used, identical bare and bridged. A question the oracle did not
  answer leaves no turn at all, and through Harbor nb's own trajectory
  ends "ok" while proctor's count says `oracle_miss` or
  `oracle_budget`. So the viewer now labels the oracle's turns, counts
  its answers, and says an unanswered ending at the end of the
  transcript, reading proctor's count first.
- **Text parameters to a service.** A compose file gets trees. Whether
  Harbor's compose invocation reads a `.env` beside the task's file
  was not tried.
- **A secret parameter** reaches neither the image nor the verifier.
- **A value's files from a git revision** are still refused by `run`.

A paid run of `tern-docs` is the first real test of a vendor agent on
stages and on axes at once. It is agreed with the user first.

**Run 2026-11-25, one try** (the user: go ahead; one try first, to
learn the cost): `as-shipped`, Claude Code 2.1.301 on Sonnet, through
Harbor on rootless Docker, the model reached through the laptop's
gateway. The try was whole: six stages, each `ok`.

- **The path works for a vendor agent.** The steps were ingested, the
  script checks ran in the verifier, and the agent read the docs from
  the fixture's service, which served the `DOCS` tree the value set.
  `grade`, `friction`, `report`, `coverage` and the viewer read it.
- **Every stage passed its headline checks,** in three or four tool
  calls and 15 to 24 seconds each. The agent opened the docs with
  `curl` from the shell and never with `WebFetch`.
- **Cost:** 25 requests and $0.54 for the try, $0.04 to $0.09 a stage.
  The image build took 21 minutes on that connection, once.
- So the exam's own question needs four tries a side, 12 in all:
  about 300 requests and $6.50 at this rate.

**It teaches:** whether "values set parameters, never steps" (decision
10) holds for a real image and a real service, and sign 8 where it is
hardest.

## Slice 5: grades over time

- Each grade names the standard it was graded under; a regrade adds a
  record beside the old one.
- `when` on a check or a stage; not applicable as a fourth outcome;
  narrowing withdraws verdicts and the report says the standard moved.
- `grade` states what a judged regrade will cost before spending it,
  and may leave configurations not yet graded.

**BUILT 2026-11-25** (`Grade/Standard.cs`).

- **A grade is kept under its standard.** `grades/<standard>.json` in
  the attempt, beside `checks.json`, which stays the verdicts as they
  are now. The standard is a stage's checks (fields, condition, a
  script's content), the pass and validity lists and the expected
  values. A regrade under the same standard replaces its file; under
  another it adds one.
- **`when` on a check**, in the words a stage's condition uses, refused
  at load when it names something undeclared or the exam has no axes.
  Where it does not hold the verdict is `not-applicable`: no script
  runs, no judge is asked, and the check is not in that
  configuration's headline, so a try there holds of fewer checks.
- **Not yet graded** is a try with no grade under the checks as they
  are now. The report leaves it out of every number and says how many;
  `coverage` counts it; `compare` refuses a comparison that would read
  it. `grade --value` or `--configuration` grades part of an exam and
  says how many tries elsewhere are left.
- **The standard moved**: from the grades kept, a check narrowed away
  from where it had been decided, one added after the first grade, one
  removed, each with the verdicts concerned. Said by `grade`, in the
  report, and in every comparison that reads those tries.
- **The cost first.** Before a judged `grade` asks anything it prints,
  per judge, the verdicts it would ask, the votes they come to, and the
  verdicts already on file. It counts by compiling each request and
  looking for its file, and sends nothing.
- The stage page lists a check that does not apply apart from passed
  and failed.

Tried on the scripted bank of 3c, 28 tries. One check was narrowed to
one assistant and another added, and the tries graded again without
being made again: 8 verdicts were withdrawn (5 failed, 3 passed), and
the rewritten guide on the second assistant, which had read "worse,
confirmed (p = 0.029)", read "worse, not confirmed (p = 0.286)" with
"the standard moved" beside it. That is the case `axes.md` warns of: a
narrowing changed a verdict with nothing re-run, and the answer says
so.

Decided while building, and confirmed by the user 2026-11-25:

- **The standard is the checks, not the exam's hash.** `axes.md` says a
  grade names the exam's hash. That changes with a prompt or a note,
  and would make every try "not yet graded" after an edit that touched
  no check. The exam's hash is recorded beside it.
- **A bank graded before this reads as not yet graded** on an exam with
  axes, until `grade` is run once. It reuses the verdicts on file.
  Exams without axes are read as before.
- **`compare` refuses rather than leaving ungraded tries out.** A
  comparison quietly made of the tries that happen to be graded would
  be a different comparison.
- **The cost is counted in verdicts and votes, not money.** Proctor
  does not know a judge's price.
- **"Added later" is measured against a stage's first grade**, so it
  keeps being said after further regrades. A narrowing is measured
  against the last grade that decided the check.

Not built:

- **The verdict under both standards for a claim** ("standard changed
  after the claim"): claims are slice 6. The grades kept are what it
  will read.
- **A check that was wrong** and is corrected shows as nothing here: a
  changed check gives a new standard, and no move is named unless it
  was narrowed, added or removed.
- **Re-reading a final workspace that was not kept** is as before: a
  script check on an agent's try goes `outdated`.

**It teaches:** sign 7 again, with the states that only appear once
checks change. The page from slice 3 gains them.

## Slice 6: claims

Not built in any form today. One axis, two values, the checks expected
to flip, the standard it was made under, its scope reported, and the
two labels (late claim, standard changed after the claim). The worked
example in `axes.md` is the acceptance test, on the scripted control
from slice 4.

Added from `axes.md`, "From how others handle this" (mine, to confirm):

- A claim says how many tries it will read. More made after looking
  are labelled as a top-up, beside the late-claim label.
- The claim's run makes fresh tries of the live side beside the other
  side's, or the report says how far apart the two were made.
- The checks a claim names are the claim. The rest are shown for
  looking, and the report says how many it showed.

**Decided (the user, 2026-11-25), and built the same day in a first
form:**

- **One word, one thing: a prediction.** It is the whole statement,
  written before the tries are made: "if this configuration is run,
  this is what will change." It stands where "claim" stood. The
  things it expects are its **changes**: each a check or a metric, and
  a direction.
- **There is no "candidate".** It was taken up for an hour as the name
  of the bundle and dropped: the bundle is the prediction, and a
  configuration already has a name. The user had read "candidate" as
  "a configuration that is not live and might replace it", and that
  construct is not needed yet. "Treatment" goes with it.
- **The two problems this is for** (the user): is there something
  better than live that we should move to; and, of all possible
  configurations, which do we pay a provider to test, and how many
  tries does it take to tell better from worse.
- **A prediction points at one full configuration,** by the name the
  exam gives it. A stretch is a listed set of them. Never "this value
  with every other axis however it falls", which is filling the matrix
  again.
- **A change may be about a metric** (flagged steps, time, tokens, tool
  calls, checks held) as well as a check, with a direction and no size.
- **Each change has its own outcome,** so a prediction can come out
  right on two and wrong on one.
- **Written after its tries exist, it is labelled late,** not refused.
- **Todo, pinned by the user:** what becomes of old predictions. Kept
  for good they are tombstones of theories that failed, which is worth
  something, and in time they are clutter. Not decided.

**BUILT 2026-11-25** (`Exam/Predictions.cs`,
`Report/PredictionOutcomes.cs`).

- **A prediction is a file**, `exams/<exam>/predictions/<name>.json`,
  committed and in no hash: `configuration` (one the exam names),
  optionally `against` (another named one; without it, what is live
  for the same user), a `note`, and `changes`. A change is `{"check":
  …}` or `{"metric": …}` with `"direction": "more" | "less"` and
  optionally `because`. The metrics are `held`, `flagged_steps`,
  `tool_calls`, `tokens` and `duration`. A check's number is the stages
  of a try in which it held.
- **Pinned by proctor, once.** `proctor predict <exam> [name]` writes
  `written` and each stage's `standard` into the file; `run` does the
  same for a prediction whose configuration it is about to make tries
  of. The author's fields are left as written.
- **Held against the tries** by the measure every comparison uses. A
  change went that way (confirmed or not), did not happen, went the
  other way (confirmed or not), or is not run yet. Each is one
  sentence, and says when the tries are too few to confirm anything.
- **Two labels, neither a refusal:** late, and the standard changed
  after the pin. A prediction never pinned says its order cannot be
  known.
- In `list`, in the report (`predictions` in `stats.json`, a section
  of `summary.md`) and in a `compare` answer that reads the
  prediction's configuration.

On the real `tern-docs` tries, two predictions written from the exam's
own reasons: `no-docs` (acceptance in fewer stages, more tool calls)
went as predicted on both, which two tries a side cannot confirm;
`restructure` (the retired endpoint used, fewer checks held, more
flagged steps) did not happen on any of the three. Both are labelled
late, since the tries came first.

Not built:

- **A listed set of configurations** for one prediction.
- **The verdict under both standards** when the standard changed after
  the pin: the label is there, the second verdict is not.
- **Fresh tries of the other side beside the prediction's**, the number
  of tries said beforehand, and a top-up after looking labelled as one.
- **A nudge from `run`** naming the tries no prediction asked for. This
  is the user's second problem, which tries to pay for, and nothing
  here answers it yet.
- **What constitutes proof.** A prediction does not say how many tries
  it will read, and proctor does not say beforehand what a number of
  tries can show. The user (2026-11-25): leave it for a later version
  ("we'll math it out someday"), put what is known into the docs
  written for the agent, and trust the person's agent to steer their
  enthusiasm round a single good run. It is in
  `skills/proctor-answers/SKILL.md`: the table of what each number of
  tries a side can confirm, which the test fixes and needs no data.
- The viewer shows none of this; its pages wait for their day.
- `CLAUDE.md`'s vocabulary table still says "claim", and the changelog
  has no entry: both files hold another session's uncommitted edits.

What follows was written on the 24th, in the earlier words.

**Pinned (the user, 2026-11-24): a treatment, now that the rest
exists.** Not built, no rush, and the word is open: **candidate** if
"treatment" is too opaque. With axes, values and configurations in
place it collapses to three things:

- a name;
- a configuration;
- a series of claims about how that configuration should move checks
  or metrics (friction, wall time, token cost, failures).

What it is for: before a configuration is run many times, the person
or agent writes "I think we should X, because it will improve Y",
instead of filling the whole matrix. The claim is the reason the
attempts are made.

To settle when this is built (mine):

- **A claim here hangs on a configuration; above it is one axis, two
  values.** They meet if a treatment's configuration is read against
  live: differing on one axis, it is the claim above with a name;
  differing on several, it is the combination `axes.md` already names
  ("What should we ship": `candidate` against `shipped`).
- **Claims about metrics are new.** So far a claim names checks.
  Friction, wall time, tokens and failure counts are numbers, so such
  a claim needs a direction and, to be held to, a size.
- **"Candidate" is already the example name** of the configuration
  proposed for release in `axes.md`; taking it as the word would make
  that the general case, which fits.
- **Encouraged, not required.** Where the nudge lives (the skill,
  `run` saying which attempts no claim asked for) is open; refusing an
  unclaimed run is not proposed.

Caveats (mine, agreed by the user 2026-11-24):

- **Looking first stays allowed.** The loop in `axes.md` starts from
  friction coded on existing attempts, so some attempts are made with
  no claim, or there is nothing to find friction in. Two modes, each
  said plainly: a small unclaimed look, labelled as one, and claimed
  runs. Hence a nudge, never a gate.
- **A claim does not stop hill-climbing.** `candidate-v2`, `v3`, `v4`
  against the same tasks, each with a tidy claim, still fit the exam.
  What guards against that is above and not yet built: fresh attempts
  of the live side beside the candidate's, the number of attempts read
  said beforehand, and in time tasks held back.
- **A claim counts only if the report holds it to account.** An agent
  will write a plausible claim for every cell. "Claimed, did not
  happen" is shown as plainly as a claim that held.
- **One sentence must be enough**: a check or metric and a direction.
  Anything heavier gets skipped.

## Slice 6a: what the exam remembers

**The user on each, 2026-11-25:**

| Proposal | Answer |
|---|---|
| A value retired: out of `run` and the lists, its tries kept | **Can wait.** Not enough evidence yet that exams live that long, except as historical records |
| Why a value or a check is there | **"Gotta have it."** |
| Each move of live recorded, with what it rested on | **No immediate need**, maybe someday; it too depends on how long exams live |
| Where users are on every axis, as shares | **Distant future, if at all.** An agent can own it ad hoc |

And on what was decided alone while building slices 4 and 6:

- **A parameter is a build argument and never in the agent's
  environment: yes.** "We've got bit by this before. Especially tricky
  for testing impossible requests, as models are clever."
- **The nb-on-Mock value stays out of `tern-docs`: reasonable.**
- **A prediction with no `against` is compared with what is live for
  the same user: reasonable, and tricky.** If it ever shows in a page a
  person reads, the page says that this is what it did. The only
  sensible alternative is to make every prediction name what it is
  compared with.
- **The two `tern-docs` predictions stay.**
- **`build` reads what an image build is handed, and builds no image:
  correct** (once explained with an example: for `toolchain=1.20` it
  shows one changed `ARG` line and nothing about what got installed; a
  check that looks inside the container is what holds that to account).

**Built from those answers, 2026-11-25:**

- **Why a value is there.** A value may carry `why`, a sentence beside
  its `note`: the note says what it changes, the why says what it is
  there for (a customer's report, an idea of the author's). It is in no
  hash. `build`, `coverage` and the report carry it. A check has had
  `why` since `check-evidence.md`. Counting the things customers found
  first, which is what the proposal was after, would need a category
  and not a sentence; not built.
- **`readme-answers` is on axes** (slice 7's first port in the
  repository, after `tern-docs` and `gate-oracle`): `docs` sets the
  exam's `README` tree parameter, which the program reads
  (`system @{{param.README}}/README.md`); `assistant` is the model. Its
  shakedown passes both ways. The getting-started guide lost its
  `proctor live` step, since live is declared on the axis.
- **`build` reads a tree parameter on the bare path too.** The port
  showed that it said nothing of how two READMEs differ, since a tree
  the program reads is outside the checkout. Its files are now read
  under `params/<NAME>/`, as a task through Harbor has them.
- Issue #13 (`grade` counted every verdict of a staged exam as new) is
  fixed.

What was written before those answers:

Facts Claude needs for the reader's first two questions, which only
the exam can hold. Each is mine and to confirm, and earns its place
only as source of truth:

- a value retired: out of `run` and the rows, its tries kept;
- why a value or a check is there (a customer's report, or our own);
- each move of live recorded, with the claim it rested on or none;
- where users are on every axis, with a share for everything else;
- a try's age given wherever coverage is read.

A configuration proposed for release is an ordinary named
configuration, compared like any other. The table that reads "ship
it" or "a flag" off the result is not proctor's (decision 17).

## Slice 7: port the rest, delete the old path

- Each remaining exam is ported to stages and axes in one go, from its
  entry in the ports document. The four with a fixture per task are
  where "something above an exam" (`todo.md`) is answered or shown not
  to be needed: several of them may be one exam with a `repository`
  axis, which slice 0 will have said.
- Delete tasks, `--tasks`, variants, bundles, `Stats.cs` and
  `Report.cs`, `tries/1`, and the tests that lean on them.
- The words: "variant" joins the old-words gate; `docs/configuration.md`
  is renamed; `CLAUDE.md`'s table, the skill and
  `docs/porting-from-suites.md` are rewritten for axes.
- `PROCTOR_VARIANT` becomes `PROCTOR_AXIS_<NAME>`.

**Under way, 2026-11-27** (the user: "go ahead with slice 7"; Modal's removal, `remix.md`, was taken first, in the
same files: commit `0364ac0`, tag `modal-in-core`). In stages, each a commit that leaves both suites green:

1. **The exams ported.** Done but two: `playground`, `cli-for-agents`, `oracle-change`, `code-change`,
   `nb-changes`, `smoke`, `gate`, `conformance`, and the test exam `harbor-unit`. What each became:
   - `code-change`: `repository` is the world axis (a value names a fixture) and each stage belongs to one; agent and
     model are one `assistant` axis; all fifteen configurations are named, since `run` makes what is named.
   - `nb-changes`: `planted` is the world axis, its values the five fixtures as they are. The ports document proposed
     one fixture with a `PLANTED` parameter; the fixtures were left alone, which needed no rebuild of anything.
   - `gate`, `oracle-change`: a `route` axis, as `gate-oracle`. The gate's writing stage is second, so each route
     is also held to starting the next stage from a clean workspace.
   - `conformance`: `script` and `assistant` axes, a try one script's stage, as `gate-oracle`'s endings. An outage
     then loses only its own try, which is what "`fail` apart" in the ports document was for.
   - `smoke`: `assistant` and `extra` (files), four stages. The teardown hook that stood in for the agent's edit
     became a stage that writes a file (finding 6).
   - **Lost in the port:** a task's `labels` (a stage has none: `kind` on `code-change` and `nb-changes`).
   - **Not ported yet:** `agent-onboarding` and `exam-authoring`. Their shape ports like the rest, but what they
     hand the agent and grade it against is proctor's docs and example exams at a pinned revision, in the earlier
     shape: the prompts, the reference exams, the planted attempts and the trial scripts all say tasks and variants.
     They are rewritten last, against the docs as this slice leaves them, at a revision that exists only then.
     `exam-authoring` also needs a value's files from a git revision, which `Axes.NotYet` still refuses.
2. **The tests** moved to configurations and tries with the ports (`TestRepo.OneConfiguration`, `Run`, `StageDir`,
   `TryDir`). Removed with what they tested: the tests of the earlier report, its statistics and the live mark.
3. **The old path deleted** (next): `tasks/`, `variants/`, bundles, `live.json` and `proctor live`, `--variant` and
   `--tasks`, `Stats.cs`, `Report.cs`, the `tries/1` report, continuing an interrupted run (a try is never taken up
   part way, so only the old path used it), the viewer's page for an exam of variants.
4. **The words**: variant out of code, flags, files and docs; `PROCTOR_VARIANT`; the hooks' names.
5. **The docs, the skill and the two exams about proctor itself.**

Found while porting, and fixed: the container name handed to hooks (`PROCTOR_CONTAINER`) changed with the stage and
held `=` and `,` on an exam with axes, so no hook could have made a container by it (it is the try's now); the
outdated summary told the user to move a directory that is not where a try is kept.

## Not in this plan

- **The move-in and the rename** (`rename-to-auspex.md`): deferred
  until slice 7 is done. Its 2026-11-29 target no longer holds. Doing
  it after slice 7 also means renaming less code.
- **Hosting** (the hosting plan, kept outside this repository): `push`, `pull`, sign-in, the first
  verdict standing. Slice 2 takes only its attempt ids, because they
  set the bank's layout.
- **Decision support** (the user, 2026-11-24): reports for product
  managers or makers, dashboards, a recommendation of what to ship.
  Claude makes those from what proctor answers. `summary.md` stays as
  it is.
- **An MCP server**, and anything else that puts the queries of 3c in
  front of someone without the files. It follows hosting.
- **A marker for a script that reads several axes** (the user,
  2026-11-24: later). Until then the combination check of 3b is how
  one is found.
- **Keeping final workspaces**, proposals for what to run next,
  pairwise coverage. Where users are is now slice 6a.
- **One reader's view across the copies of an exam** that base changes
  leave behind ("something above an exam", `todo.md`).
- **Retrying a stage in place** (`todo.md`).

## What stays as it is meanwhile

The eleven exams on tasks keep running on the old path until slice 7.
The stage page keeps working on both layouts. Nothing is run on a paid
model without the user's agreement, and every slice is proved on Mock,
the scripted controls or nb in Docker first.

## Decided (the user, 2026-11-24)

Each was an open question here, with my recommendation, and agreed.

1. **One stop, after 3c.** None after 3a or 3b; their output is shown
   as they are built.
2. **An exam with axes is an exam of stages.** As built.
3. **The bank is laid out by try id.** As built.
4. **`--value` to `run`** is those values with every other axis at its
   reference. An unnamed configuration that is not that takes several
   `--value` flags or a name in `exam.json`.
5. **A new verb, `proctor build`.** The file of reviews agreed with it
   was withdrawn with 6.
6. **A review gates nothing.** Superseded the same day: there is no
   record of a review at all (slice 3b, "No record of a review").
7. **An agent's review counts.** Superseded with 6.
8. **3c is tested by hand first**: one fresh agent, one scripted bank,
   its answer read together. An exam of its own only if the answers
   are worth tracking.
9. **A try is one configuration run once through the stages; an
   attempt is one stage of a try** (`axes.md`, decision 18). This file
   still says attempt for a try in what was written first.

## OPEN, for the user

1. **A configuration against another** (from the 3c trial). `compare`
   moves one axis and holds the rest. "What should we ship" is about a
   combination: a named candidate against what is live, which
   `axes.md` already says ("What should we ship is about a
   combination"). Proposed: `proctor compare <exam> --configuration
   <name> [--against <name>]`, the same test between the two
   configurations' tries, with every axis that differs named in the
   answer. It would come before 3d. **Agreed** (the user, 2026-11-24:
   "what does x change and how much evidence do we have for that?"
   will be a very common question). **BUILT 2026-11-24:** the change
   is the axes on which the two differ, taken together, compared in
   every setting of the other axes where the exam carries it, by the
   same function as one value (`Configurations.Compare` on a change of
   several axes). `--against` defaults to the live value on every
   axis that has one, the rest as the configuration has them: what is
   in use, for the same user. Every other axis with a live value is
   held as the configuration has it, and the change is spread only
   across the axes with none (the ones not ours to choose): the first
   cut spread it across every setting, so `rewritten-long` against
   `rewritten` also read the long prompt on the shipped guide. The answer names the configuration and
   the change, and carries a caution that it does not say which value
   made the difference. A configuration already at live on every such
   axis is told to name what to compare with.

Beside that, what is still to confirm is in `axes.md`: the five
consequences and the declared facts of "Who asks, and through what",
and what building a value alone needs (OPEN 5).
