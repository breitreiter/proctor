---
type: plan
title: Building axes — the order of work, and what each slice should teach us
created: 2026-11-24
status: proposed 2026-11-24, for the user. Slices 0 to 2 and proctor's half of slice 3 are built. Revised later the same day for `axes.md` decisions 13 to 16 and its "Who asks, and through what": slices 3a to 3d are new, the page of slice 3 is no longer where the model is judged, and `run` filling every configuration (built) is to change. What is new is marked "mine" where the user has not said it. The model is `axes.md`; this is only the order it is built in and where we stop to look. It takes over what `bank.md` step 4 left open (the eleven exams still on tasks, deleting tasks) and puts the viewer's exam page after the first lesson. The move-in and the rename (`rename-to-auspex.md`) and hosting (`hosted-auspex.md`) are deferred until this is done (the user, 2026-11-24: they are administrative and teach nothing)
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
| each value's diff shown beside the attempt | each value built alone, its change recorded and reviewed once (3b) |
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
9. **The reviews do not get done.** A value's review is skipped as
   routine, and comparisons carry "not reviewed" so often that nobody
   reads it.
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
  made** (time and four random characters; `hosted-auspex.md`, "The
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

**Look at:** `list` and `run` on the scratch port of `readme-answers`,
and on a scripted exam with six axes of two values. **It teaches:**
sign 5.

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
- **The review is a record committed with the exam**
  (`exams/<exam>/reviews.json`, in no hash, as `live.json` is): per
  value its hash, the base's hash, who looked (a person or an agent,
  named), when, and one line of what they found. `proctor build
  --reviewed <axis>=<value> --by <who>` writes it after showing the
  change. A value needs one review; a changed base asks again.
- **`note` on every value and `about` on every axis are required** on
  an exam with axes, since the note is the claim the change is held
  against.
- **`list` and the report say what is not reviewed, and what is
  unsafe**: a value with no review at its hash, a named configuration
  the combination check flagged. Each comparison that reads one says
  so in its scope.

Not here: an image and a service (slice 4, where they first exist).

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
(`hosted-auspex.md`) and are not built here.

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
  other comparisons this report holds; which values in it are not
  reviewed and which configurations are unsafe (3b).
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
  ones live spans, each with its tries, their age, and reviewed or
  not. It answers the inside half of "where are our customers the only
  testing". The outside half needs where users are (slice 6a).

The test, on a scripted bank made for it: three axes; holes; one value
that is better in one world and worse in another; two axes that only
moved together; one unreviewed value; one difference too small for the
tries made to see. A fresh agent is given the skill, the two queries
and the reader's three questions (`axes.md`, "The reader"), and
nothing else. Its answer is read against what the bank holds.

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
  the revision it came from) and its review, each marked as whose it
  is. No diff of a database or a site.
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

A paid run of `tern-docs` is the first real test of a vendor agent on
stages and on axes at once. It is agreed with the user first.

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

## Not in this plan

- **The move-in and the rename** (`rename-to-auspex.md`): deferred
  until slice 7 is done. Its 2026-11-29 target no longer holds. Doing
  it after slice 7 also means renaming less code.
- **Hosting** (`hosted-auspex.md`): `push`, `pull`, sign-in, the first
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
5. **A new verb, `proctor build`, and reviews in
   `exams/<exam>/reviews.json`**, committed and in no hash.
6. **A review gates nothing.** An unreviewed value runs; `list` and
   every comparison that reads it say so.
7. **An agent's review counts**, recorded as an agent's by name, and
   the answer says who reviewed.
8. **3c is tested by hand first**: one fresh agent, one scripted bank,
   its answer read together. An exam of its own only if the answers
   are worth tracking.
9. **A try is one configuration run once through the stages; an
   attempt is one stage of a try** (`axes.md`, decision 18). This file
   still says attempt for a try in what was written first.

## OPEN, for the user

Nothing here now. What is still to confirm is in `axes.md`: the five
consequences and the declared facts of "Who asks, and through what",
and what building a value alone needs (OPEN 5).
