---
type: plan
title: Building axes — the order of work, and what each slice should teach us
created: 2026-11-24
status: proposed 2026-11-24, for the user. Nothing built. The model is `axes.md`; this is only the order it is built in and where we stop to look. It takes over what `bank.md` step 4 left open (the eleven exams still on tasks, deleting tasks) and puts the viewer's exam page after the first lesson. The move-in and the rename (`rename-to-auspex.md`) and hosting (`hosted-auspex.md`) are deferred until this is done (the user, 2026-11-24: they are administrative and teach nothing)
---

# Building axes

## What this plan is for

`axes.md` is a model nobody has used. The fastest way to learn whether
it is right is to put real exams through it, cheapest test first, and to
say beforehand what would count against it. So the slices are ordered
by what they teach, not by what the code needs first, and each ends
with something to look at.

Nothing is shown to anyone before 2026-11-29. The aim by then: slices 0
to 3 done, so the model has been tried on every exam on paper and on
one exam for real, with a page to judge it by.

"Variant" below means only what proctor has today.

## What would count against the model

Written before any of it is built, so a bad result is recognised and
not explained away. Any of these is a reason to stop and talk:

1. **Exams that do not fit.** More than one or two of the thirteen
   exams need something `axes.md` forbids (a value that adds a step, a
   hook that must branch on a value, two axes that must touch one file).
2. **Refusal 2 blocks honest work.** Values of different axes
   legitimately need the same path or variable, and the author is
   pushed to merge two axes into one to get past it.
3. **An axis without a live value turns out to need a kind after all.**
   Reports or `run` keep needing to know "is this one ours or the
   customers'" beyond whether a live value is set.
4. **Live across several configurations cannot be read.** With two
   swept axes, a person cannot say from the page what "against live"
   compared.
5. **The grid does not scale to three axes** on a page, or `run`
   cannot be told which part to fill without a long command.
6. **Authors reach for the old split.** Writing an exam, we keep
   wanting "the bundle" back as a separate thing.
7. **The states multiply past reading.** Pass, fail, undecided, not
   applicable, not yet graded, excluded, not run, lost, stale: if a
   cell needs a legend to read, the page has failed, whatever the model.

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
2):** `proctor run <exam>` fills every configuration not excluded.
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
  stage page's address and its "exam as given" showing each value's
  diff, and **the exam page**, designed against this file and a sample
  bank. Its design doc (`exam-page.md`) is rewritten first, as a short
  proposal for the user: the grid leads, a comparison is along one
  axis. "Setup" becomes configuration.

**Look at:** the exam page on the free 2 × 2, and on a sample bank with
three axes and holes in it. **It teaches:** signs 4, 5 and 7, which are
the ones `axes.md` says the model succeeds or fails on. **Stop for the
user** here; this is the point to judge the abstraction.

If the user wants real numbers under the page, `readme-answers` on
Gemini's free tier is the run to make then (it needs `proctor setup`
with a key, which only the user can do).

## Slice 4: through Harbor

- The agent as an axis: which route an attempt takes comes from a
  value. Image arguments the fixture's Dockerfile declares, service
  parameters, and the overlay inside the container.
- A configuration that cannot build is `failed` with the build's
  error. One declared impossible is excluded and never made.
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
10) holds for a real image and a real service.

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
- **Keeping final workspaces**, weighting by where users are,
  proposals for what to run next, pairwise coverage.
- **Retrying a stage in place** (`todo.md`).

## What stays as it is meanwhile

The eleven exams on tasks keep running on the old path until slice 7.
The stage page keeps working on both layouts. Nothing is run on a paid
model without the user's agreement, and every slice is proved on Mock,
the scripted controls or nb in Docker first.

## OPEN, for the user

1. **Stop points.** After slice 0 and after slice 3, as written. More,
   or fewer?
2. **An exam with axes must be an exam of stages.** It halves the
   paths; it also means no exam can try axes without first becoming
   stages. All thirteen will anyway.
3. **The bank's layout by attempt id** is taken from the hosting plan
   ahead of hosting. The alternative is to keep `<variant>/<n>` paths
   with the configuration spelled into the directory name, and change
   the layout a third time when hosting comes.
