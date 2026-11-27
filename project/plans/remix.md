---
type: plan
title: Remix — proctor and auspex as something to build on
created: 2026-11-27
status: draft; nothing built; six decisions open (end of file)
---

# Remix

## The change of stance

Until now the README, the site and the onboarding work have presented
proctor as a product a person adopts whole. People with a coding agent
increasingly build their own tools instead, and a tool that one person
maintains cannot be everything to each of them. So:

1. **Proctor and auspex are built solidly enough for one person's daily
   work.** That is the bar for the core, and the only thing with a
   schedule.
2. **A requested feature is built when it is cheap or when daily work
   wants it too.** Otherwise the answer is a recipe (below), and the
   request stays an open issue.
3. **A documentation set, written for a coding agent to read, says how
   to make proctor do something it does not do.** Where the seams are,
   what is promised about them, and worked recipes.

This fits what was already decided: proctor is a source of truth and a
data access layer for an agent and what the agent builds (`axes.md`,
decision 17). Remix is that decision applied to the whole tool and not
only to the report.

## Two ways to remix, and only one is promised

**Beside.** Your code reads proctor's files and calls its commands.
Proctor is unchanged, and an upgrade is a version bump. Every promise
below is about this.

**Inside.** You fork and change the C#. Nothing is promised: internals
move every week. What helps is that the repository is already written
for an agent to work in (`CLAUDE.md`'s table of files, one concern per
file, tests that cost nothing), and a recipe that needs a fork says
which file and which test.

A recipe is written beside whenever it can be. A recipe that needs a
fork is a signal that a seam is missing, and is how a requested feature
earns its place in the core.

## What the core supports (the user, 2026-11-27)

"Solid for daily work" names a platform, and it is small: **rootless
podman on Arch and on Ubuntu.** That is where attempts are made every
day and the only place a break is found the same day.

Everything else was built or is being chased to help adoption, and
under this stance adoption is not the core's job:

| | today | under this plan |
|---|---|---|
| podman, Arch and Ubuntu | used daily | **supported**: the gate and conformance are run here, a break is a bug |
| Docker on Linux | the default engine, a guide, run now and then | **not maintained**: last known to work at a version and a date, said on its guide |
| macOS | never run; guides written from other people's issues | **not run**, as the guides already say. No more work to make it so |
| Modal | built in three spikes, on a free account kept only to check it | **out of the core**: the code is moved out and kept (below), to come back as the first example of an engine written against the seam |

Two consequences beyond the table.

**`container-network-finish.md` narrows.** Its goal 3 (exams portable
from a macOS laptop to a Linux server) and the Modal column of its
table are adoption work. What daily use needs of it is goals 1 and 2
on rootless podman: the relay, the allowlist, a service fixture the
agent cannot read. Issue 14 stays, since it is about what a try is
worth and not about an engine. That plan is under way in another
session and is edited there, not here; this is the scope it should be
cut to.

**An engine becomes a seam, not a list.** What proctor asks of an
engine is short (`harbor.engine`, the checks in `HarborRun.Preflight`
and `CheckPodman`, how nb's layer gets into an image, what the network
policy compiles to). Written down once in `docs/remix/`, it is the
recipe "run on an engine proctor does not support", and Docker and
macOS are its dated examples. Someone who needs one of them owns it,
with their agent, which is the stance in one case.

**Modal moves out of the core and is kept** (the user, 2026-11-27).
Three steps, the third much later than the first two:

1. **Out.** The `modal` engine leaves `HarborConfig`, `HarborRun`
   (`Preflight`, `Unreachable`, the `harbor[modal]` extra),
   `HarborCompile` (`ModalIdle`, the publish copied into the task's
   context), the runner, and setup's wizard, check and sandbox plan,
   with their tests. `harbor.engine: modal` is then refused with a
   sentence saying where the code went. `--config` stays if a second
   machine file is useful without Modal; that is checked while
   removing, not assumed.
2. **Kept.** The commit before the removal is tagged, and
   `project/archive/modal-runs.md` (already the plan and its spikes)
   gains the tag and the list of files it touched, with `docs/modal.md`
   moved beside it. A tag and not a directory of uncompiled source:
   code that sits in the tree and is never built reads as current to an
   agent, and it is not.
3. **Back, as an example** (a loose end in `todo.md`, written when step
   1 lands). Once the engine seam is written down and proctor has
   whatever its extension point turns out to be, Modal is rewritten
   against it from the tagged code, outside the core, as the first
   worked example of an engine someone else owns. It is the test of the
   seam: if Modal cannot be written against it without touching the
   core, the seam is wrong.

**What the code allows** (read 2026-11-27, nothing run; a gauge, not a
decision). Proctor is one executable of about 13,000 lines and 188
types, every one internal, with no interface anywhere. Four ways out
were weighed:

| way | what is there | what it would take | reach |
|---|---|---|---|
| An interface and loaded assemblies, as nb's providers | nothing. What would be plugged in already has a seam that is not an assembly: a check is a script, a judge a wire shape, an agent a Harbor agent in Python, an engine Harbor's own `environment.type` | an abstractions package to version, a load context, and the duplication across it that nb's own comments complain of | .NET only. Least value for most cost |
| A public library | the viewer is this already: `View/Measured.cs` is 85 lines over eight entry points (`Exam.Load`, `Answers.Compare` and `Coverage`, `Bank.Select` and `Of`, `Configurations.Compute`, `Results.Collect`, `Predictions.Load`), by `InternalsVisibleTo` | reading: a small facade of those, a day. Wider: the records are the file formats and move weekly, an executable is awkward to reference, and some flags are static state (`Verbs.ValueFilters`) | .NET only, and a remixer's agent writes Python |
| The commands, with JSON out | `compare` and `coverage` answer in `answer/1`; the bank and the report are files; most verbs already write to a `TextWriter` | JSON from `list`, and what `run` and `grade` did as a file or a last line; exit codes beyond 0 and 1, written down | any language. Cheapest, and what "beside" already means |
| An API on the viewer | ten GET routes, loopback and a token; the compare and coverage routes already hold the answer objects | reading: serialise what the page was given, hours. Driving a run: jobs, keys and a container engine behind a server, against "the viewer has no key" and "v1 reads only" | nothing to install only where someone hosts it |

**The fourth is decided: nothing ships** (the user, 2026-11-27). The
viewer gets no API and proctor no server. If running proctor for other
people is ever worth building, it is a separate repository that stands
beside proctor like any other remix: it calls the commands, reads the
bank and the report's files, and starts attempts on an engine off the
machine. An API, an MCP server and anything a person installs nothing
to use belong to that repository and not to this one. Two things follow
for the core, neither of them work to do now: the commands with JSON
out are the contract such a service would stand on, so they are worth
doing well for their own sake; and it would be the second thing that
needs the engine seam, after Modal's rewrite.

None of the four is how proctor is extended *inward* today. That is a
fifth way, already the dominant one: **proctor runs your script under a
contract** (hooks, the runner, script checks: an environment and an exit
code). The engine fits it better than any of the four. What is
Modal-specific in the core is five places (the `harbor[modal]` extra,
the login check, the sandbox lifetimes in the job, the teardown
sentence, setup's pages); the rest is keyed on `HarborConfig.Remote`
and is generic already (the publish copied into the build context, the
hosts a sandbox cannot reach, no services, no relay). So an engine
could be declared in the machine's file (its Harbor type and extra,
remote or not, its job arguments, a preflight script) with no assembly
and no API.

The catch: with Modal out, the remote path has no user in the core.
Either it goes too, and the extension cannot be written without
changing the core, or it stays and is held up by a scripted remote
engine in `HarborTests` (the fake uv is already there), at no cost.

What the extension point looks like is not decided here. Today an
engine is a value of a setting and branches in several files; whether
it becomes a script contract like the runner's, a second executable, or
only a documented place to fork is decided when step 3 is taken up,
with Modal's needs (no local image build, a login, an idle limit, hosts
it cannot reach) as the requirements.

## The seams

What exists today, and how firm each is. "Named" means the file carries
a schema name and number.

| seam | what it is | where it is described | state |
|---|---|---|---|
| The definition | `exams/`, `fixtures/`, `proctor.json` | `docs/configuration.md` | documented; two shapes until slice 7 of `axes-build.md` (variants, and axes with stages) |
| The commands | every verb, its flags and exit codes | `docs/cli.md` | documented; exit codes not written down in one place |
| The answers | `compare` and `coverage` as JSON | `answer/1`, `skills/proctor-answers/` | named |
| The script check | exit 0, 1 or 2, the `PROCTOR_*` environment | `docs/configuration.md` | documented, stable since September |
| Hooks and the runner script | setup and teardown, how nb is started | `docs/configuration.md` | documented |
| The bank | an attempt's directory: `manifest.json`, `status`, `trajectory.json` (ATIF), `core.json`, `diff.patch`, `checks.json`, `grades/`, `verdicts/`, `friction.json` | `on-disk-layout.md`, a plan and not a guide | `friction/2` and `tries/1` named; the rest unnamed |
| The log | `log/<id>/entry.json` | `bank.md` | unnamed |
| The report's files | `results.jsonl`, `stats.json`, `summary.md` | `docs/cli.md` | `configurations/1` on an exam with axes; the earlier shape unnamed |
| The judge | an endpoint by wire shape (`systemone`, `chat`) | `docs/configuration.md` | documented |
| The engine | `harbor.engine` and `--config` | `docs/podman.md`, `docker.md` | documented as guides; what an engine must provide is not written, and there is no extension point |
| The skills | `proctor-exam`, `proctor-answers` | themselves | the agent's way in |
| The viewer | reads the bank; calls proctor's code through `View/Measured.cs` | `View/CLAUDE.md` | internal: the same seam a remixer's own page would use for the files, and no promise for the code |

Two things this table shows. The bank is the seam most recipes stand
on and the least documented for a user. And nothing can be promised
about the definition or the report's files while both shapes are alive,
so **slice 7 comes before any promise.**

## What is promised

Three levels, said on one page (`docs/remix/promises.md`):

- **Held.** A named file keeps its meaning under its number. A breaking
  change is a new number, a changelog entry saying what broke, and the
  old number read for at least one minor version. A command keeps its
  flags and its exit codes. The script check's contract does not change.
- **Described.** Documented and likely to stay, but it may change in a
  minor version with a changelog entry. Everything in the table that is
  documented and not yet named starts here.
- **Internal.** Any C# type, the viewer's pages and routes, compiled
  Harbor tasks under `log/`, verdict file hashes, the rendering of
  `summary.md`. Read them at your own risk.

A file moves from described to held by getting a schema name and one
captured example under `Proctor.Tests/fixtures/contract/` that a test
reads, so that breaking a promise fails the build and is not found by a
remixer. This is the only code the plan asks for.

What is never promised: that proctor will recommend anything, that it
will write prose, that a hosted model is called without being asked for.
Those are the tool's character and a recipe may not work around them
silently; a recipe that has a model write a report says the report is
the agent's.

## The cookbook

`docs/remix/`, one page per recipe, and `skills/proctor-remix/` as the
way in: a short skill that says what proctor is and is not, lists the
recipes with one line each, and points at the seams and the promises.
The skill carries only what an agent would not guess, as `proctor-exam`
does.

Every recipe has the same parts, in this order:

1. **What you want**, in the words a person would use to ask for it.
2. **What proctor already does toward it**, with the commands.
3. **What you build**, and where it lives (your repository, never
   proctor's roots other than `exams/` and `fixtures/`).
4. **The seams it stands on**, each with its level.
5. **What is not there**: the gap, said plainly, and the issue number
   if there is one.
6. **Proven how**: run once for real and what it cost, run on Mock, or
   not run. A recipe marked not run is still worth having; one that
   claims more than was done is not.

The first recipes, with what looking at each turned up:

| recipe | beside or inside | what is there | the gap |
|---|---|---|---|
| Run on an engine proctor does not support (Docker, macOS, Modal) | inside today; beside once an engine has an extension point | Docker built and dated (`docs/docker.md`); Modal's code at a tag, with its plan and guide in `project/archive/` | what an engine must provide is written nowhere in one place, and there is no extension point yet. Modal is to be its first example |
| Gate a pull request on an exam | beside | `run`, `compare` as JSON, the live mark | exit codes for "worse, confirmed"; the bank is not in git, so CI needs it from somewhere |
| Watch for surprises from a vendor's agent | beside | pinned `agent_version`; an agent or a version as a value of an axis; `compare` against live | **an unchanged exam makes no new attempts**: an attempt made last month counts as one made today, by design (`bank.md`). A new pin is a new value and works. A model that changed behind the same name is invisible to the bank and needs its own answer (a date as a value of an axis, or a way to ask for fresh attempts) |
| Build a set for people to grade | beside | the windows a judge sees (`Grade/Window.cs`), verdict files as a record, exit 2 as "needs a judge" | no command writes the windows out, and nothing reads a person's verdict back in. Out is a script over the bank; back in wants a decision (a judge whose endpoint is a file of labels would do it with no new concept) |
| Audit a company's API, MCP server or docs | beside | `proctor-exam` to plan and build, friction, `compare` and `coverage`, `proctor-answers` | the deliverable is a document the agent writes from proctor's numbers, each number linked to its evidence. Proctor writes no such report and will not. The recipe is mostly how to keep that document honest |
| Compare your product with a competitor's | beside | an axis whose values set what the fixture serves, as `tern-docs` does with its docs | writing tasks and checks that favour neither product is the whole difficulty, and it is advice, not code |
| Add an agent proctor does not run | inside | `Ingest.Kinds`, the stub model, conformance at no cost | a fork today; the recipe names the files and the tests. If asked for twice, it becomes a seam |
| Put a page or a chart of your own over the results | beside | `stats.json`, `results.jsonl`, the answers | the earlier shape of `stats.json` is unnamed until slice 7 |
| Hand the attempts to another tool | beside | trajectories are ATIF already | nothing but a mapping; cheap, and a good check that the bank's description is enough |

More will come from use, and from every request that is answered with
"here is how" in place of a feature. A recipe that three people ask
about is the evidence that something belongs in the core.

## What "solid for daily work" still needs

Not new work, only what is already owed, in the order remix depends on
it:

1. Slice 7 of `axes-build.md`: every exam on stages and axes, the old
   path deleted. Until then two shapes would have to be described and
   neither promised.
2. The report in the old shape (`Report/Report.cs`) goes with it.
3. The viewer's pages, designed (`project/view/`).
4. `container-network-finish.md`, cut to rootless podman (above).

The Docker and Modal guides are no longer owed an update for axes
(`where-we-are.md`, "Owed, small"): each gets a date and a line
saying it is not maintained.

## The order of work

1. **Promises and seams, as two pages** (`docs/remix/seams.md`,
   `promises.md`), with everything honest about its level today. A day.
   Writing them will show which unnamed files matter.
2. **The bank described for a user**, as a worked example of one
   attempt's directory, file by file. It is the page every recipe links
   to and `on-disk-layout.md` is a plan in a planner's voice.
3. **Three recipes that cost nothing to prove**: another tool (a
   mapping), a set for people to grade (the way out only), a page of
   your own over the results. Each proven on `playground` or
   `tern-docs`' existing bank.
4. **The skill**, once there are three recipes to list.
5. **Modal out and kept** (steps 1 and 2 under "What the core
   supports"). Any time; sooner is cheaper, since the networking work
   and the next move of Harbor's pin both stop carrying it. Its
   changelog entry says what broke.
6. Slice 7, on its own schedule. After it: names on the remaining
   files, the contract fixtures, and described becomes held.
7. **The README and the site say it.** The pitch moves from "a tool
   that does this" to "a base that does this much, and here is how to
   make it yours". Last, so that it describes something true.
8. The recipes with gaps (the schedule, the pull request gate, the
   audit), each when daily work or a request reaches it.
9. The engine seam written down, an extension point chosen, and Modal
   rewritten against it (step 3). No date.

**Measuring the docs with proctor itself** is the obvious check and is
not in the order above: `agent-onboarding` already hands an agent the
skill and the docs at a pinned revision. A task there per recipe ("make
proctor do X", graded by whether what the agent built works against a
planted bank) would say whether the cookbook does its job. It costs
real money on vendor agents, so it waits for a decision.

## Open

1. **Is "inside" encouraged or only tolerated?** The plan says
   tolerated, with recipes pointing at files. Encouraging forks means
   keeping `CLAUDE.md` good for strangers, which it nearly is.
2. **How long is an old number read?** The plan says one minor version.
   Longer is a real cost for one maintainer.
3. **Does a person's verdict come back in as a judge?** It would make
   the grading-set recipe complete with no new concept, and it would be
   the first judge that is not a model.
4. **Fresh attempts on an unchanged exam.** The scheduled recipe needs
   an answer, and the bank's rule ("one made last week counts as one
   made today") was decided on purpose. A date as a value of an axis
   keeps the rule; a flag on `run` bends it.
5. **Is the remix exam worth its spend**, and on which agents.
6. **Is `docker` still the default of `harbor.engine`** when podman is
   what is run and supported? Changing it breaks every machine file
   that leaves the field out.
