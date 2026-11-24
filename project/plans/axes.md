---
type: plan
title: Axes, values and configurations — every change to the fixture named and declared
created: 2026-11-23
status: proposed 2026-11-23, from a conversation with the user. The model's shape is theirs (marked "the user"); the refusals, the outcomes and the mapping from today are recommendations to confirm. Decisions recorded 2026-11-23 and 24 (see "Decided since"), the words among them. Nothing built. Would revise `exam-variant-attempt.md` (variant, live, claim, D5, D16) and the vocabulary table in `CLAUDE.md`. Written first as `buckets.md` in working words (bucket, family); renamed when the words were fixed
---

# Axes, values and configurations

## The words (the user, 2026-11-24)

| Word | One sentence |
|---|---|
| **axis** | A named thing with several possible values, declared in the exam: `go`, `agent`, `model`, `docs`. |
| **value** | One choice on an axis, written `go=1.20`. A value may carry content (files, an image argument, variables), and its identity is that content, hashed. |
| **configuration** | One value on every axis: what an attempt ran as. Its identity is its values. |

**"Variant" is retired, not redefined.** Today it means roughly what a
configuration will mean, and the common usage elsewhere gives it to a
single value (a product line's variant at a variation point). Kept with
either meaning, it would be a word with two senses across this
project's own history (`project/archive/`, D1 to D18, the CHANGELOG,
every transcript), and the old-words gate cannot tell senses apart. A
retired word it can ban outright, so every leftover is a hit. With no
outside users yet, the only constraint is our own: fewer loose
references for a coding agent to misread.

In this plan, **variant** appears only for what proctor has today.

Why these over the alternatives, briefly (the comparison was made in
conversation, 2026-11-24): axis, value and configuration are the words
of Jenkins matrix jobs and of combinatorial testing, so a practitioner
of either expects them; a statistician says factor, level and
treatment combination, and follows. "Setup" (the viewer's word for the
same idea) is taken by `proctor setup`, the install wizard, and goes.

**What the words cost:**

- `docs/configuration.md` is renamed (say `exam-files.md`); most of
  proctor's 70 or so mentions of "configuration" point at it.
- **"Config" is not a configuration.** Agents will abbreviate, and
  "config" already means `proctor.json`, nb's settings and appsettings.
  The gate cannot catch that, so `CLAUDE.md` says it: a configuration is
  one value per axis; settings are settings, never "config" for a
  configuration.
- **Across into nb**, "configuration" means nb's own settings (45 files
  of nb's C#). Proctor's prose does not write "an nb configuration"; it
  writes "the agent axis at nb".
- **"Value" against expected values.** A check's answer key is the
  `expect` block, whose entries are also values. Prose says "expected"
  for the answer key and "value" only for a choice on an axis.

## The problem

Today an attempt is a variant (agent, model, bundle) at a task (prompt
plus fixture). That split assumes, from the moment an exam is written,
that you know which things you will change (the bundle) and which are
the world (the fixture). Three cases from the conversation that broke
it:

- **What is under test lives in the repository.** A README at the root,
  `AGENTS.md`, `.claude/`. A bundle is a separate directory, reached as
  `{{bundle}}` or through a fixture service, so it cannot put a file in
  the checkout; changing the fixture instead changes every task's hash
  and strands every paid attempt (D16).
- **A customer's world nobody chose.** A Go auditor is reported wrong on
  Go 1.20. As a variant, Go version gives a grid of variants (1.20 live,
  1.23 live, each with the treatment) and needs two live marks, where an
  exam has one (`live.json`). As a task, it needs every task copied per
  version (`model.md`, Open: "Environment as its own axis").
- **The agent and model are the customer's too.** Users bring Claude
  Code or Codex as they bring Go 1.20, yet both sit inside the variant,
  so an exam over two agents already needs two live variants.

At bottom this is the matrix. The aim is not to avoid it but to make its
parts legible, so a run makes a precise claim about the part it covered
(the user).

## The model (the user, 2026-11-23)

- **Everything is the fixture.** The repository, the toolchain image,
  the services, the environment, the agent and the model: the whole of
  what an attempt runs in, before any value applies. This is the
  **base**.
- **A change to the base is a value, and it has a name.** You may change
  anything, but you put what you changed in a value and name it, so it
  is tracked as a thing that changed. A value's identity is its content
  (hashed), as a variant's is today.
- **Values are grouped into axes**: mutually exclusive values that are
  nameable variations of one well-understood thing. `go: 1.20 | 1.23`,
  `docs: as-shipped | restructured`, `plan: demo | paid`,
  `agent: claude-code | codex`.
- **It is incumbent on the author not to make a mess of this.** Proctor
  enforces what it can mechanically (below) and makes the rest visible.

From that:

- **An attempt is a configuration at one task**: the base plus one value
  from each axis. Its coordinates are the task and those values.
- **A treatment is a new value on an axis** (`docs=restructured-v2`).
  A one-off change is an axis of two values: without it and with it.
  There is nothing else; every change is on an axis.
- **Changing the base itself is still D16**: the exam is copied and the
  old one closed. The point of axes is that the base rarely needs to
  change, because a change goes in a value instead.

There is no bundle as a separate thing, and no decision up front about
which box a file goes in.

## Configuration

**A configuration is one value from each axis: what an attempt ran
as.** Thin to define, and still the word a user reaches for, because
evidence accumulates per configuration: "which configurations need more
validation?" is the coverage grid read by row (few attempts, wide
intervals, results near the line, tasks never run), and Claude answers
it from the files with no further concept. It stays crisp in the
vocabulary for that reason.

**Configurations that matter get names** (the user, 2026-11-23):
`shipped`, `candidate-v2`, aliases for a set of values. Names make the
matrix less insane. Most configurations stay unnamed and are addressed
by their values.

## Live is per axis

**An axis may declare one live value: its reference.** An axis without
one is swept. There are no kinds of axis (the user, 2026-11-24); what a
kind would say follows from whether the axis has a live value.

- **On an axis you choose**, live is what you ship now: `auditor=v1`,
  `docs=as-shipped`. A treatment is compared with it.
- **On an axis your users choose**, there is usually none: `go`,
  `model`. You test across its values, because your users pick them.
- **But it may have one**, the value most of your users run:
  `agent=claude-code@2.1.330`. When that moves, the claim is that
  nothing should change (D17's example, "Claude Code 2.1.330 is what
  our users run", is exactly a claim along this axis). Typed axes would
  have made that claim illegal or an exception; untyped, it is a claim
  on one axis like any other.

"What we ship" and "what our users mostly run" are the same idea, a
reference point, seen from either side. An author who sets a live value
on `go` has made Go 1.23 the reference for comparisons on that axis:
harmless, and right if that is where the customers are.

`live.json` becomes the live value of each axis that has one. The
one-live rule holds per axis, so the grid of live variants above does
not arise.

**So live spans several configurations** (the user: "by necessity has
to span, which sucks"): the live values, crossed with every value of
the axes that have none. It has to mean *what we are shipping*, as
against *everything we might meet in the wild*, and it does: that is
two groups again, but drawn by one optional setting per axis, cheap to
set and to change, instead of a structural decision about where each
file lives, made before the exam is written. The live value is declared
on the axis in the exam ("Decided since", 2). Statistics has the same
split as control and noise factors (robust design), but needs no type
for it here.

## Claims state their scope

A claim changes one axis's value and holds the others fixed: `docs`
from `as-shipped` to `restructured-v2`, flipping these checks, nothing
else worse (as now: D10, D11, D17). Its **scope** is the values of the
other axes the attempts covered:

> v2 flips `no-false-positive` on `go=1.20 × agent=claude-code`, holds
> on `go=1.23 × agent=claude-code`; `agent=codex` not run.

- **A configuration not run is not run, never "no change".** The report
  lists them as gaps, in its body. The same rule as stating the minimum
  detectable effect.
- **Pooling across an axis is a stated assumption.** Reading Claude
  Code and Codex attempts as one pool assumes they behave alike; a claim
  says so if it pools, and the report shows the split either way.

Experimental design calls a claim's scope its *inference space*, and a
claim written before its attempts a *preregistration*; the late-claim
and standard-changed labels below are deviations from one. Worth
borrowing in the docs, if not the report.

It is the user's job to decide what the gaps mean and which warrant runs
(the user). Proctor's job is to make that cheap; see "Where next".

## Grades over time

Not new with axes, but axes make it routine, so it is written down
here.

**A check added later is graded over every earlier attempt** (the user,
2026-11-23). Proctor already does this: a task's hash leaves out the
checks, which are the standard, so a change to them is a regrade, not
new attempts (D16). The user's framing: once we decide a thing is
wrong, we decide it was always wrong, and we reread prior attempts to
find where it happened. The fault is expected, but not having checked
for it does not exonerate those attempts.

What follows:

- **Not re-readable is undecided, never pass.** A check that reads saved
  evidence (trajectory, transcript) regrades exactly. One that needs the
  final workspace regrades only if it was kept; otherwise the attempt is
  undecided (`Grade.Pass` is already `bool?`). Keeping it is optional,
  and its hash is always recorded ("Decided since", 5).
- **Graded then and graded now are both kept.** A grade names the
  standard it was graded under (the exam hash), so a regrade adds a
  record next to the old one (as `verdicts/` already do by hash).
  Comparisons always use the current standard, both sides alike. The
  page tells apart "failed a check it was graded on" and "fails a check
  added later": both are faults, but the second is a discovery, and
  without the distinction a new check reads as the live value getting
  worse, with nothing re-run.
- **A check that was wrong** exonerates the same way in reverse.
- **Narrowing a check is retroactive in reverse.** A check later scoped
  to one value (`when go = 1.20`) says it was never a coherent question
  elsewhere, so its verdicts on the other values are withdrawn: those
  attempts become not applicable, not excused. The old grade stays on
  record under the old standard; the page shows "graded against this
  check, later scoped to `go=1.20`". A narrowing can raise a
  configuration's pass rate with nothing re-run, so the report says the
  standard moved, not that the configuration improved.
- **A claim records the standard it was made under** (the exam hash).
  Narrowing a check after seeing a claim's results can be right, and
  looks exactly like moving the goalposts. Proctor does not forbid it;
  the report labels it **standard changed after the claim**, beside the
  existing late-claim label, and gives the verdict under both standards.
- **Regrades cost money where a model judges** (the user: easy for
  simple checks, stressful for judged ones, a dollar a regrade or so).
  The verdict cache by hash bounds it: narrowing costs nothing (not
  applicable asks no judge, and the verdicts kept have unchanged
  requests), and a reworded check pays only where its compiled request
  changed. Adding a judged check costs attempts times votes, so `grade`
  states the cost before spending it, as `run` would, and may grade
  lazily: the configurations being read or claimed first, the rest
  **not yet graded**, a state of its own and not undecided.
- **Not applicable is a fourth outcome.** A check or a stage may apply
  only to some values (`when go >= 1.21`). Where it does not apply it
  is neither pass, fail nor undecided, and counts toward nothing. A
  condition naming an axis or value the exam does not declare is
  refused at load.

The loop that produces a treatment (from `View/project/loop.md`), in
order:

1. A friction point is coded on existing attempts.
2. It becomes a check.
3. Every attempt is regraded; the live value now fails where the bad
   thing happened. This is the claim's "before".
4. The claim is written: this value flips that check, nothing else
   worse.
5. The treatment's attempts are made, and the claim is reported.

Step 3 comes before step 4, or the claim has no measured before.

## What a value can change

Not every change is files:

| Kind | Example |
|---|---|
| files in the checkout | a README, `AGENTS.md`, `.claude/`, a docs tree |
| the image | the Go toolchain version, as a build argument |
| the environment | an account or plan (`demo`, `paid`), as variables, secrets by name |
| a service | the docs service's content, a sandbox API's version |
| the agent and model | which agent at which pin, which model, which provider |

Files are an overlay applied after checkout. The other kinds reach the
attempt as the setup reads them, as environment variables (say
`PROCTOR_AXIS_GO=1.20`).

**The setup reads only declared axes, and every value is in the
hash.** Proctor already warns of the trap: a hook that branches on
`PROCTOR_VARIANT` to change what the agent sees hides the change from
the hash. A script that reads something undeclared cannot be detected,
so this stays a documented rule.

## What proctor enforces

The part of "not making a mess" that a machine can check. Each is a
refusal at load or before a run, saying what to change:

1. **A value cannot touch the answer key.** A fixture's checks live
   beside `repo/`, never inside it; a value that writes there is
   refused. Without this, the thing under test could change its own
   standard.
2. **Values of different axes cannot change the same path, or the same
   variable.** Otherwise two axes interact and the result depends on
   the order they apply.
3. **Values on an axis are mutually exclusive**, and a configuration
   has exactly one per axis.
4. **A change to the base says what it costs before it is spent.** `run`
   already knows which hash moved (`Bank.Stale`). On a base change it
   refuses with the count of attempts it would strand, and points at
   axes: "the base changed; this strands N attempts; if the changed
   file is something you might change again, make it a value on an
   axis."

And one rule it cannot check, only show:

5. **Every value's diff is on the page**, next to each attempt, in the
   viewer's "the exam as given". A value that quietly does two things
   (`docs=v2` also fixing the planted bug) is visible to whoever reads a
   comparison that involves it.

## Where next

A run makes a precise claim about one configuration and not much else.
Deciding which configurations warrant more runs, or any, is the user's
(the user). What proctor and the viewer can put next to each empty or
thin configuration:

- **The coverage grid**: which configurations have attempts, which are
  stale, which are empty. The most important thing on the exam page,
  since the empty configuration is where over-generalisation happens.
  The mocks' stage-by-try grid already has a "not run" state.
- **Where users are.** A customer report names a configuration; so does
  knowing most users run Claude Code. Weighting is a fact only the user
  has: declared once per exam, shown beside each configuration.
- **Where a change can reach.** A claim names checks and checks name the
  values they apply to, so proctor can list the configurations a
  treatment could affect.
- **Where the evidence is thin.** Few attempts, a wide interval, a
  result near the line.
- **What a run costs.** Attempts times the price of one, before anything
  runs.

The established ways to contain the matrix are names "where next" can
borrow: pairwise coverage across the axes with no live value (covering arrays)
instead of every configuration; fractional designs; CI's `include` and
`exclude`, which also say that some configurations make no sense and do
not exist; sequential allocation, putting attempts where the evidence
is thin.

The coding session reads these and proposes ("run these four, about
$N"); the user decides. Proctor never picks what to run and never
reports more than it measured.

## Worked example: the Go auditor

The exam asks whether the auditor flags real problems and only those,
for the Go version the code declares.

- Base: a few Go repositories with planted problems, the hidden checks
  beside them.
- Axes: `auditor: v1` (live: v1), `go: 1.23` (no live value),
  `agent: claude-code | codex` (no live value, or live at the version
  most users run).
- A customer reports false positives on Go 1.20. Add the value
  `go=1.20` (the toolchain image, and `go.mod`'s directive through the
  overlay). No task is copied; `run` fills `auditor=v1 × go=1.20 ×`
  each agent.
- Write a check, "flags nothing valid in the declared Go version".
  Regrade: it fails `auditor=v1` on 1.20, and may find false positives
  on 1.23 nobody had seen.
- The fix is a value, `auditor=v2`. The claim: from v1 to v2, flips the
  new check where `go=1.20`, nothing else worse. Its "nothing else
  worse" half is read on the 1.23 attempts, which is why this is one
  exam and not two.
- If the customer meant an old version of the auditor, that is just a
  value on `auditor`, and the report reproduces on it.

## What it replaces

| Today | Under axes |
|---|---|
| variant (agent, model, bundle; id and hash) | retired; a configuration is one value per axis, named when it matters |
| `variants/<id>.json` | the exam's axis declarations |
| bundle | values of the file and service kinds |
| the live variant (`live.json`) | the live value of each axis that declares one |
| D5: a variant id whose hash changed is refused | a value name whose content changed is refused, by the same rule |
| a claim names two variants | a claim names one axis and two of its values |
| `PROCTOR_VARIANT` | `PROCTOR_AXIS_<NAME>` per declared axis |
| fixture | the base: the repository and everything else an attempt runs in |
| the viewer's "setup" | configuration |
| labels | unchanged: stored, filtered on, never interpreted, outside every hash. Axes are the interpreted, hashed thing, and are not called tags or labels so nobody mistakes one for the other |

## What it costs

- **A guarantee becomes a convention.** Under the fixture-and-bundle
  split, what was under test could not touch the world by construction.
  Under axes it is refusals 1 and 2 plus a visible diff, and the rest
  is the author's care.
- **Axes multiply the matrix.** Accepted; the coverage grid and
  where-next are how it is lived with.
- **Harbor's routing.** Which agent an attempt runs, and so its route
  (Harbor or bare), comes from a value instead of the variant.
  Implementation, not model, but not small.
- **The word change.** "Variant" is in about 1,900 lines of 171 tracked
  files in proctor (970 lines of C#, 230 of docs, README and the skill,
  110 of names on disk and in the environment, 29 variant files) and
  about 220 lines of the viewer (2026-11-24 counts). Most of it is owed
  to the model change anyway, since every line that loads, hashes,
  refuses, marks live or claims a variant is rewritten whatever the
  word. "Variant" joins the old-words gate, with the archive
  allow-listed.
- **Migration.** Every exam's variants become axes. Most map
  mechanically: each variant's agent, model and bundle become a value
  on `agent`, `model` and a bundle axis; the variant that was live gives
  each of its axes its live value. Attempts already in banks keep their
  manifests, and the reader maps old coordinates onto configurations.
  Whether old attempts' hashes can be carried over or are stale is OPEN.

## Decided since

- **1. The words: axis, value, configuration; "variant" retired** (the
  user, 2026-11-24). See "The words".
- **2. Axes and their values are declared in the exam, always** (the
  user, 2026-11-23: "if you wanted the devex to be pythonic, you
  shouldn't have picked a C# tool"). Nothing exists by being referred
  to. A condition, a claim, a setup or a `run --value` naming an axis or
  value the exam does not declare is refused at load, saying where to
  declare it. A declared value may do nothing yet. An axis's live
  value, if it has one, is part of the same declaration. The
  declaration is the schema Claude reads to answer questions about
  configurations.
- **3. Agent and model are axes** (the user, 2026-11-23). Harbor's
  routing reads a value instead of the variant; that misery is absorbed.
- **5. What an attempt left behind is kept optionally; its hash always**
  (the user, 2026-11-23). Keeping every workspace is good practice, but
  on Harbor in the cloud it is a download bill, and pushed to the hosted
  bank a storage bill. So the manifest always records a hash of the
  final workspace tree, and the contents are kept when the user chooses.
  A workspace check added later regrades from contents that match the
  hash, wherever they are found, and is undecided otherwise. `push`
  leaves workspaces behind by default: the server needs only what its
  pages show.
- **6. The server checks consistency, not truth** (the user,
  2026-11-23). It can check that a pushed value's content matches its
  hash and that an attempt names values it holds. It cannot check that
  an attempt really ran with them: a pusher who lies about A, B and C
  and the hash X cannot be disproved from what is pushed. Accepted, as
  trust in a team tool. A third party can check provenance only if the
  base and the values are in git.
- **7. Named configurations, and live spans** (the user, 2026-11-23;
  folded into "Configuration" and "Live is per axis").
- **8. Narrowing checks and the standard a claim was made under** (the
  user, 2026-11-23; folded into "Grades over time"). The user's concern
  is the volume of needs-regrading, which is a cost and attention
  problem more than a model one.
- **9. No kinds of axis** (the user, 2026-11-24). An axis may declare
  one live value, its reference; without one it is swept. See "Live is
  per axis". This retires the question of what to call two kinds.

## OPEN

1. **Order for the kinds that are not files.** Refusal 2 catches two
   axes changing one path or one variable, not two build arguments that
   interact.
2. **Migration.** Whether attempts already in banks carry their hashes
   over onto configurations, or go stale.
3. **The shared bank** (pinned, not written): attempt ids unique across
   machines, grades named by standard, definitions (values among them)
   stored by hash on the server, each value's diff shown from there.
4. **Making it legible** (the user: "it will be my/our job"). The
   coverage grid; "the standard moved" against "got better or worse";
   standard changed after the claim; not yet graded against undecided
   against not applicable; each value's diff. This is the viewer's and
   the report's work, and where the model succeeds or fails.
