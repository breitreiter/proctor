---
type: plan
title: Axes, values and configurations — every change to the fixture named and declared
created: 2026-11-23
status: proposed 2026-11-23, from a conversation with the user. The model's shape is theirs (marked "the user"); the refusals, the outcomes and the mapping from today are recommendations to confirm. Decisions recorded 2026-11-23 and 24 (see "Decided since"), the words among them. Built since, per `axes-build.md`: slices 1 and 2 and proctor's half of slice 3 (declared, run bare, reported); the declaration as built is in `axes-ports.md`, and where it differs from this file it is the later word. Revised 2026-11-24 for who reads an exam and through what ("Who asks, and through what"), which is not built and partly not settled. Would revise `exam-variant-attempt.md` (variant, live, claim, D5, D16) and the vocabulary table in `CLAUDE.md`. Written first as `buckets.md` in working words (bucket, family); renamed when the words were fixed
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
  version (auspex's `project/model.md`, Open: "Environment as its own axis").
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
  only to some values (`when go != 1.20`; a condition is `=` or `!=`
  on a value's name, never an ordering). Where it does not apply it
  is neither pass, fail nor undecided, and counts toward nothing. A
  condition naming an axis or value the exam does not declare is
  refused at load.

The loop that produces a treatment (from auspex's `project/loop.md`), in
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

Not every change is files. **A value sets parameters the base declares;
it never adds steps** ("Decided since", 10). The table is what a value
can reach, not a set of kinds proctor knows: as declared and built
(`axes-ports.md`, "The declaration") a value is named parameters the
base's own scripts act on, plus three things proctor does itself (files
laid over the checkout, who does the work, a whole fixture for an axis
of worlds).

| Kind | Example |
|---|---|
| files in the checkout | a README, `AGENTS.md`, `.claude/`, a docs tree |
| the image | the Go toolchain version, as a build argument the fixture's Dockerfile declares (`ARG GO_VERSION`) |
| the environment | an account or plan (`demo`, `paid`), as variables, secrets by name |
| a service | the docs service's content, a sandbox API's version, as parameters the service's definition declares |
| the agent and model | which agent at which pin, which model, which provider |

Files are an overlay applied after checkout. The rest reaches the
attempt as declared parameters: a build argument, `{{param.NAME}}` in
the program, `PROCTOR_PARAM_<NAME>` for hooks and checks. The value's
name on each axis is `PROCTOR_AXIS_<NAME>`.

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
2. **Values of different axes cannot change the same path, variable or
   parameter.** Otherwise two axes interact and the result depends on
   the order they apply.
3. **Values on an axis are mutually exclusive**, and a configuration
   has exactly one per axis.
4. **A value sets only parameters the base declares.** No build step,
   no service, no script of its own. With that, two axes setting the
   same parameter is refusal 2, and nothing depends on order.
5. **A change to the base says what it costs before it is spent.** `run`
   already knows which hash moved (`Bank.Stale`). On a base change it
   refuses with the count of attempts it would strand, and points at
   axes: "the base changed; this strands N attempts; if the changed
   file is something you might change again, make it a value on an
   axis."

And one rule it cannot check, only make checkable (revised 2026-11-24;
the first form was "every value's diff is on the page", given up
because a value can be a seeded database, a documentation site or a
whole repository, which nobody reads as a diff):

6. **A value is built alone and what it changed is reviewed against
   what it says it does** (the user, 2026-11-24). Four parts:
   - the setup is built with only the named value, every other axis at
     its reference value (the live value, or one the axis names);
   - what changed from that reference build is looked at, by a person or an
     agent;
   - that is held against the value's own sentence (`note`), which is
     the claim of what the value does. A value that quietly does two
     things (`docs=v2` also fixing the planted bug) is caught here;
   - a setup script that reads more than one axis is strongly
     discouraged, since what it does in a configuration cannot be
     predicted from the values built alone.

   Building costs no model, so this is a shakedown: free, and done
   before any attempt is paid for. What follows from it is mine and to
   confirm ("OPEN", 5).

## Where next

A run makes a precise claim about one configuration and not much else.
Deciding which configurations warrant more runs, or any, is the user's
(the user). What proctor and the viewer can put next to each empty or
thin configuration:

- **The coverage grid**: which configurations have attempts, which are
  stale, which are empty, and which are excluded (declared impossible:
  shown, never a gap to fill). The empty configuration is where
  over-generalisation happens. Written when an exam had two or three
  axes; with a dozen, nobody reads the grid whole ("Who asks, and
  through what").
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

## Who asks, and through what (2026-11-24)

Marked as elsewhere: "the user" for what the user said, the rest mine
and to confirm. None of it is built. It changes what slice 3's page is
for; `axes-build.md` was revised for it the same day (slices 3a to
3d).

### The reader (the user)

A product manager, or someone building alone, looking at an exam six
months old. It holds treatments that were tried and dropped, values
added because a customer complained, stages that crept in. The
questions are not about one claim:

- Are we doing a good job checking our work before we ship it?
- Where is the only testing our customers trying it?
- Given all these axes, what should we actually ship, and behind a
  feature flag or straight to production?

### An axis is not narrow (the user)

The earlier picture, each axis one thing you could point at as a
commit, is given up. "The system prompt for your chatbot" is a dozen
axes or more. So nobody fills the cross product or looks at it: twelve
axes of two values are 4,096 configurations. The configurations that
exist are the ones somebody named or ran.

### Settled: proctor does not do decision support (the user, 2026-11-24)

The leaning below is now the decision ("Decided since", 17). Decision
support is too big for proctor: one person builds it, and it is
MIT-licensed. Proctor is three things:

- **an excellent source of truth**;
- **a solid layer for inspecting and navigating the evidence** (the
  viewer);
- **a solid data access layer for Claude and the dashboards Claude
  builds.**

It is not now, and likely never, good reports for product managers or
makers, dashboards, or anything that says "ship this configuration and
not that one". The best it can do is see that Claude, on the desktop
or in a terminal, has accurate information and the levers for the deep
analysis a recommendation to a person needs.

What that moves (mine):

- The release table under "The three questions" is not something
  proctor outputs or holds. At most it is guidance in the skill for
  the agent that makes the recommendation.
- Proctor's own verdicts stay, since they are measurements: better,
  worse or no detectable difference against live, with the interval.
- `summary.md` stays as it is and is not built up for this reader.
- The facts under "What the exam would declare" are judged as source
  of truth: each earns its place if Claude cannot answer the reader
  without it.

### Claude carries it; proctor holds the evidence (the user)

First said as a leaning, because where this reader meets proctor is
hard to say: the experience is Claude's, in conversation and in the
dashboards it builds. Proctor is a good citizen: its docs and tools
steer Claude toward right conclusions for that reader. Proctor owns
the evidence store, and the debugging and analysis views that let
Claude defend what it said. "Users on Codex are more likely to run
into this" links to the attempts where Codex stumbled.

- A reader on Claude Desktop means an MCP server over the hosted bank.
  the hosting plan, kept outside this repository puts off an API for the agent until it is wanted;
  this is the want.
- Even then, something in the viewer says what to ask Claude about
  (the user).
- Someone working in Claude Code has the files and needs neither.

### What follows (mine, to confirm)

1. **Claude says; proctor counts.** A rate or an interval worked out in
   a conversation cannot be checked, and two people asking one question
   get two numbers. "Every number is computed once" reaches the tools:
   a query ("along `agent`, holding these, read as one across those")
   answers with what the report would hold, and Claude quotes it.
2. **The cautions travel in the answer.** Guidance in the docs is
   skipped; what is in the result is read. Every comparison's answer
   carries its scope, each configuration not run as a row of its own,
   what it was read as one across, when its attempts were made, how
   small a difference it could have seen, and how many comparisons
   were asked for beside it.
3. **Everything has an address, and the address shows the number.** A
   try, a stage, one tool call, a verdict, a comparison. If Claude says
   40% against 15%, the link opens a view where proctor shows 40%
   against 15%. A friction point already has its call and a checked
   quote; this is the same rule the judges' quotes are held to.
4. **Two axes that only ever moved together are said to be so.** If
   tone and examples changed in the same configurations, nothing
   separates them. Proctor can see that from the attempts in scope and
   answer "cannot be told apart from: `examples`". It is the mistake
   Claude would otherwise make fluently, and telling the two apart
   takes configurations made for the purpose, which Claude proposes
   and the user decides, as in "Where next".
5. **`run` stops filling every configuration.** With a dozen axes the
   default has to be the configurations somebody named. This changes
   slice 2 as built, where `run` fills all that are not excluded.
   `exclude` matters less; naming matters more.

### The three questions

**"Are we checking before we ship" is about the times live moved.**
Each time a live value changed: was there a claim first, which
configurations did its attempts cover, what was not run. Today live is
a field in `exam.json`, so that history is git's. Proposed: a move is
recorded, with the claim it rested on or the fact that there was none.

**"Where are our customers the only testing" has an inside and an
outside.** Inside what is declared: the configurations live spans (the
live values, across the axes with none) that have no attempts, few, or
old ones. Outside: a value nobody declared cannot show as a gap. The
weighting in "Where next" is the hook, if it has a share for
everything else, so the answer can be "the evidence covers what about
70% of users run".

**"What should we ship" is about a combination.** A claim moves one
axis and holds the rest at live. Two treatments that each beat live
alone, shipped together, are a configuration nobody ran. So the thing
proposed for release is a named configuration (`candidate` against
`shipped`), filled across the axes with no live value and compared
there. Then flag or not reads off it:

| What the attempts show | What it suggests |
|---|---|
| better or the same everywhere, most users covered, a small difference would have been seen | ship it |
| better in some configurations, worse in others | a flag aimed along that axis, if the product can tell those users apart |
| good where run, a large share of users not run | a flag or a staged release: customers test the rest, so keep the way back |
| no difference seen, but only a large one could have been | more attempts, or a flag; this reads as "the same" and is not |

Proctor cannot supply how far a bad release reaches, how easily it is
undone, or what a bad outcome costs. Those are the user's, and Claude
asks for them. The recommendation is Claude's, from stated facts;
proctor still never picks what to run and never reports more than it
measured.

### What the exam would declare for this (mine, to confirm)

Small facts, none of them in a hash:

- **A value can be retired.** A dropped treatment leaves the grid and
  `run`, and keeps its attempts. Today removing a value makes them
  stale, so six months on the choice is clutter or lost history.
- **Why a value or a check is there.** A customer's report, or our own
  initiative. The count of things customers found first is the most
  direct answer to the second question, and it accounts for the
  complaint-driven values instead of hiding them.
- **The moves of live**, as above.
- **Where users are, on every axis.** With a share for everything
  else. On an axis you choose it says a release under way
  (`docs: as-shipped 90%, v2 10%`), which one live value per axis
  cannot; live stays the reference for comparisons.

And two cautions for whoever builds it:

- **Old attempts.** For this reader an attempt from five months ago, on
  an agent version nobody runs, should not count as covered.
- **Copies.** A change to the base copies the exam (D16), so six months
  is probably several exams, and this reader's view has to run across
  them. That is "something above an exam" (`todo.md`) again.

### From how others handle this (looked up 2026-11-24; mine)

What is ahead of practice and should be kept: a configuration not run
shown as not run (no tool found tells that apart from passing, and
GitHub's cancelled or skipped jobs count as success); axes declared and
hashed with a reference on each (elsewhere a reference is a whole run);
the claim written first; regrading as the ordinary path.

What practice or the literature says against the plan as written:

- **A claim's two sides are made at different times.** The live
  attempts exist; the treatment's are made later. Hosted models change
  under one name, and resources and time of day move pass rates by
  several points. Either the claim's run makes fresh live attempts
  beside the treatment's (as Chromium's queue reruns without the patch)
  or the report says how far apart the two sides were made.
- **Topping up where a result is near the line** is stopping when you
  like what you see. A claim says how many attempts it will read; a
  top-up after looking is labelled, beside the late-claim label.
- **"Nothing else worse" is many comparisons.** The checks a claim
  names are the claim; the rest are for looking, and the report says
  how many it showed.
- **Pairwise coverage is for finding what breaks, not for rates.** It
  assumes axes do not interact, and agent with model is the known
  counter-example. Where users are is the better guide.
- **Configurations that disagree cancel when read as one.** Slice 3's
  reading (the van Elteren test) shows each configuration, but has no
  word for "better here, worse there". It needs one.
- **Large projects declare what they promise to check** (Rust's and
  CPython's tiers, Kubernetes's blocking jobs). That is the named
  configurations of item 5, said as policy.

Sources: Jenkins's matrix (axes, values); Rust's platform tiers;
Anthropic on infrastructure noise in agentic coding exams; Epoch, "Why
benchmarking is hard"; the Holistic Agent Leaderboard paper; Miller
on error bars (arXiv 2411.00640); Bach and Schroeder on pairwise testing;
Medeiros and others on configuration sampling.

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
| bundle | a value's files, or a tree parameter a service reads |
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
- **Porting.** Every exam's variants become axes when it is next run
  ("Decided since", 11). No attempt is migrated.

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
- **10. Values set parameters; they never add steps** (the user,
  2026-11-24). The fixture's Dockerfile and service definitions declare
  their parameters (`ARG GO_VERSION`, `ARG AUDITOR_VERSION`, a service's
  content directory); a value sets declared ones and adds no build
  step, service or script. The fixture already says how its parameters
  combine, so there is no order to decide, and refusal 2 covers
  parameters as it covers paths and variables. The file overlay is
  unchanged (paths of different axes are disjoint). The agent axis is
  proctor's own: the runner installs the agent, through Harbor or bare,
  as today. Two cases remain, neither an order problem:
  - **A configuration that cannot build** (auditor v2 does not support
    Go 1.20) is `failed`, with the build's error as its reason, like any
    infrastructure failure, and never graded.
  - **A configuration known to be impossible** is declared in the exam
    as excluded, after CI's `exclude`. `run` does not make it, and the
    coverage grid shows it as excluded, not as a gap.

  The cost: varying something new in the image means declaring a new
  parameter in the fixture, which is a base change (D16). In practice
  the parameters are declared when the fixture is written. Considered
  and dropped: a fixed order of layered steps (image, services, files,
  environment; then the exam's order of axes), which makes each
  configuration's image a stack of steps from several authors that
  nobody can read.
- **11. No migration of attempts; exams port when next run** (the user,
  2026-11-24: there is no bank anywhere). Proctor's bank, log and
  `exams-seen/` do not exist on any machine. The paid evidence that
  exists is the viewer's frozen `datasets/` (28 old proctor runs and
  one private one, about 300 MB, in the layout before the bank), already
  left behind by the bank's own change, and relevant only for trying new
  analyses for friction detection (the user). So:
  - An exam ports its definition when it is next run, as exams moved to
    stages: each variant field becomes a value on an axis, and a field
    with one value becomes an axis of one, so it stays in the
    configuration's identity.
  - No hash is carried over and there is no mapping table.
  - The viewer's `project/trials/datasets_to_bank.py` learns to write
    configurations; it is the only code that touches old data. The
    datasets stay research material for friction, never evidence a
    claim is judged on.
  - The old-words gate allow-lists `project/archive/` and that script
    for "variant".
- **12. The shared bank is in the hosting plan, kept outside this repository** ("The shared bank",
  2026-11-24): attempt ids unique when made, the first verdict pushed
  stands, definitions and value contents stored by hash, log entries
  never pushed.

- **13. The reader and their three questions** (the user, 2026-11-24).
  See "Who asks, and through what", "The reader".
- **14. An axis is not narrow** (the user, 2026-11-24). A system prompt
  is a dozen axes or more; the picture of an axis as one commit is
  given up.
- **15. A value is built alone and reviewed against its sentence** (the
  user, 2026-11-24). "What proctor enforces", 6. Values stay as
  flexible as they are (files proctor lays, parameters the base's
  scripts act on); the risk is taken out by looking at what each one
  did by itself, and by discouraging scripts that read several axes.
  The looking is done by whoever asks `proctor build`; proctor keeps
  no record that it was done (the user, 2026-11-24: any way of showing
  one reads as an approval or means nothing).
- **16. A script that reads several axes is unsafe code** (the user,
  2026-11-24), as `unsafe` is in C#, without a marker in the syntax
  for now. It is allowed and never refused. Whoever does it is on
  their own: the review of each value built alone says nothing about
  the configurations where the script acts, and proctor's checks do
  not reach there. With several collaborators, or changes made without
  review, keeping axes isolated is the safe route; where that is not
  possible, care is the only guard. So there is no declared dependence
  between axes yet, and what proctor may do is say where the safe
  ground ends ("OPEN", 5).
- **17. Proctor does not do decision support** (the user, 2026-11-24).
  A source of truth, a layer for inspecting the evidence, and a data
  access layer for Claude and what Claude builds. No reports for
  product managers, no dashboards, no "ship this one". See "Who asks,
  and through what", "Settled".
- **18. A try, and an attempt** (the user, 2026-11-24, agreeing to my
  recommendation). A **try** is one configuration run once through the
  exam's stages. An **attempt** is one stage of a try, which is what
  each stage's manifest already is. `CLAUDE.md`'s table,
  `docs/configuration.md` and the hosting plan, kept outside this repository follow when the old
  path goes (`axes-build.md`, slice 7).
- **19. The build plan's questions** (the user, 2026-11-24): `--value`
  to `run` is that value with every other axis at its reference; a
  verb builds without running. The three answers about a record of
  reviews were withdrawn the same day, with the record.
  `axes-build.md`, "Decided".

## OPEN

1. **Making it legible** (the user: "it will be my/our job"). The
   coverage grid; "the standard moved" against "got better or worse";
   standard changed after the claim; not yet graded against undecided
   against not applicable; each value's diff. This is the viewer's and
   the report's work, and where the model succeeds or fails. If Claude
   carries it (2), legible means to Claude first: what a query answers
   and what its answer carries. The debugging views stay a person's.
2. **Does Claude carry the experience for the product manager.**
   Answered 2026-11-24: yes (decision 17). An MCP server over the
   hosted bank follows from it, with hosting. The five consequences
   and the declared facts in "Who asks, and through what" are still
   mine and to confirm, one by one.
3. **What `axes-build.md` becomes.** Revised 2026-11-24 as proposed
   here, its slices 3a to 3d, to confirm. If 2 holds: the query and its
   answer come before the exam page, the page shrinks to what tells a
   reader what to ask, and signs 4, 5 and 7 stop being tests of a page
   a person reads. `run` filling every configuration (built) changes.
4. **Left as they were, and contradicting what is built or decided**
   (found 2026-11-24, each needing the user): the unit, attempt or
   try, is decided (18) and the files not yet brought to it; "no kinds of axis" (9) against
   the world axis in the code; "a value sets only parameters" (10)
   against a value that names a fixture, and what a changed fixture
   under such a value does; "agent and model are axes" (3) against one
   `assistant` axis (`axes-ports.md`, finding 2). This file is also
   still written in tasks where an exam with axes is one of stages.
5. **What building a value alone needs** (mine, 2026-11-24, from
   decision 15):
   - **A reference on every axis.** Live serves where an axis has one.
     An axis of the customer's (`go`) has none, so it needs a value
     named as the one others are built against, for this purpose only.
   - **The combination can be checked too, for nothing.** If no script
     reads two axes, what a configuration changed from the reference build is
     the changes of its values put together. Proctor can build the
     configuration, compare, and say where it is not: that is a script
     reading several axes, found without reading the script. Under
     decision 16 this is a report and never a refusal: it says which
     configurations the reviews of single values do not cover, and a
     comparison that reads one of them carries that in its answer. It
     stands in for the marker the syntax does not have.
   - **Where the review is kept.** Nowhere (decision 15). What is
     kept is the combination check, which proctor measures.
   - **What the comparison reaches.** The checkout as committed before
     the first stage, fully. An image only by digest (different, not
     how). A running service or a database inside one, not at all,
     unless the fixture gives a way to dump it. A build that does not
     repeat shows as a difference with no value behind it, so the
     reference is built twice first.
   - **Axes that do depend on each other.** A model only some agents
     can use is why agent and model became one `assistant` axis.
     Answered by decision 16: one axis or `exclude` is the safe form;
     a script reading both is allowed and unguarded; nothing is
     declared. A marker comes later (the user, 2026-11-24); whether it
     is on the script, the axis or the pair is for then.
   - "The exam as given" (`axes-build.md`, sign 5) names a tab that was
     dropped; it becomes the value's sentence, its extent and its
     review.
