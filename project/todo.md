---
type: todo
title: Loose ends
created: 2026-09-17
---

# Loose ends

Small things worth doing that are not worth a plan. An entry here is a
reminder with enough context to start from, not a design.

## Decide the process boundary with nb

Proctor can drive nb two ways: spawn `nb` as a subprocess and parse stdout, or
reference `nb.Core` and call `Nb.RunAsync` in-process. The subprocess path keeps
the boundary at the program file and the JSONL stream, which is the seam every
existing consumer already uses. The in-process path gives a typed `RunResult`
and no quoting or stream-deadlock problems, but couples proctor's build to nb's.
Pick one before the experiment manager grows a second code path.

## Define the JSON the site reads before the report skeleton hardens

Resolved the generation question: the site is a prebuilt bundle plus `data/`
JSON (brief, "The site is a prebuilt app plus data files"). What remains is
the data contract: the index file, the per-experiment files, and where the
derived statistics live (computed by proctor in .NET and written as JSON, so
the browser never does statistics and the markdown summary reads the same
numbers). Settle that schema before implementing the report skeleton in
`learnings/prior-art.md` §5, since both renderers depend on it.

## The first slice renders one self-contained HTML file

Before the site, `proctor report` emits `reports/data/<id>/report.html`, a
single file with inline CSS and script, plus `summary.md`. Both render from
`stats.json` and `results.jsonl`. Keep the template plain: a .NET string
template or a small templating package, no front-end toolchain. Its sections
are the per-experiment page from `learnings/prior-art.md` §5. The point is
to put a page in front of stakeholders in the first week and learn what they
read first. The component-library choice below only matters once the site
starts.

## Pick the component library for the report site

The brief wants an opinionated library with complete components and
micro-interactions, and no design system of our own. The bundle is built once
in proctor's repo, so a React app costs a consumer nothing, and MUI is a real
candidate: its DataGrid does client-side sort, filter and virtualised
rendering of large tables, which is the client-side load the brief now
expects. Alternatives with the same completeness: Mantine (less "Firebase
clone"), Ant Design (table-centric). Web-components libraries such as Web
Awesome remain an option if the bundle should stay framework-free. Tables are
the whole product, so judge candidates on their data table first, then tabs,
tooltip and details.

## CI for proctor

`.github/workflows/test.yml`: build, then `dotnet test`. The runner tests
spawn nb, so the workflow has to get an nb binary from somewhere: check out
`breitreiter/nb` beside proctor and build it (what `TestRepo.NbPath` assumes
today), or, once nb is on nuget.org as a tool, `dotnet tool install` it and
point `NB_PATH` at the shim. The second is the one to keep; the first is
fine until the nb package exists. The Mock provider is all the tests need,
so no keys. `first-slice.md` "Steps 1 through 7 run in proctor's own CI" is
the promise this pays.

## Publish proctor as a dotnet tool

`PackAsTool` on `Proctor.csproj` (`first-slice.md` says "later"; this is the
reminder), a tag-triggered release workflow that packs and pushes, mirroring
nb's `Feature_Release_Workflow_Publishes_To_Nuget.md`. Package id is
`Dreamlands.Proctor`: bare `Proctor` is taken on nuget.org
(`learnings/name-collisions.md`). Command name stays `proctor`. Do nb first,
so a consumer's CI can install both with two lines and proctor's own CI can
drop the sibling-checkout build.

## Candidates for nb, not proctor

All six are filed in nb's own tracker as of 2026-09-17, untracked there until
reviewed. Each is additive; none changes behaviour for a program that does
not name the new thing or for anyone building from source.

- `Feature_Injected_Reminders_Carry_A_Source_Tag.md`: loop and todo reminders
  carry `source` on their user event, so `loop_nudged` counts events.
- `Feature_Trailer_Carries_Cost_When_The_Entry_Declares_A_Price.md`: optional
  per-entry prices, `cost` in USD on the trailer, inherits `estimated`.
- `Feature_Trailer_Carries_Program_Hash_And_Nb_Version.md`: always-on
  `program_sha256` over the resolved events and `nb_version`. Found that
  neither csproj sets a version today.
- `Feature_Sample_Seed_Directive_For_Providers_That_Accept_One.md`:
  `sample seed <n>` in the `budget` key/value shape, named to avoid the
  existing `--seed <file>` flag; warns when the provider ignores it.
- `Feature_PackAsTool_Carries_Providers_Into_The_Tool_Package.md`: the
  provider copy targets are conditioned on a runtime identifier, so a RID-less
  pack would ship no providers; relax that, add the tool metadata.
- `Feature_Release_Workflow_Publishes_To_Nuget.md`: tag-triggered pack and
  push plus per-RID archives on a GitHub Release. The README already points
  at a releases page nothing fills.

## `resume` does not notice a changed agent route

`resume` refuses a changed suite and a changed compiled task (`harbor/tasks`
hashed), but reads `proctor.json`'s `agents.<agent>.base_url` fresh. The
task's `allowed_hosts` was compiled from the old host, so a changed route
gives a cell whose base URL the allowlist does not admit: Harbor's egress
sidecar (gost, transparent, sniffing `Host`/SNI) accepts the TCP connection
and drops it, and Codex logs `Handshake not finished` then `error sending
request`. Seen 2026-10-19 on `20261019-2118-code-change-ax3m` after `library`
became `library.lan`. Record each agent arm's route in `experiment.json` and
refuse a changed one, as for the suite.

## A bare gateway hostname does not resolve for a musl agent

Docker writes `search lan` with `options ndots:0` into the task container's
`resolv.conf`. glibc (Node: Claude Code, Qwen Code) still tries the search
list for `library`; musl applies it only to names with fewer dots than
`ndots`, so Codex (a static musl binary) asks for bare `library` and gets
`failed to lookup address information: Try again` until the agent timeout.
Worked around in the machine's `proctor.json` by spelling Codex's route
`library.lan`. Options: validation warns on a dotless agent host, or compile
pins the host's address in the task (an `extra_hosts`-style entry, if Harbor
allows one) so the spelling does not matter.

## Drop the pasta AppArmor workaround when Ubuntu fixes it

`docs/podman.md` §6 has Ubuntu 26.04 users add an include line and a
signal rule to pasta's AppArmor profile, without which every sidecar and
its network leaks (#7, closed with that workaround). Watch
[LP#2154379](https://bugs.launchpad.net/bugs/2154379). Once a passt
update ships a profile that lets podman signal pasta, say from which
version §6 is no longer needed. If more people trip on it first,
`HarborRun.CheckPodman` could warn when pasta's profile and its local
file have no `signal` rule; both files are world-readable.

## An oracle for vendor arms, the hokey way, if the refusal starts costing us

A task with an `oracle` is refused on Claude Code, Codex and Qwen Code arms
(`plans/oracle-in-proctor.md`, question 4): bridged through Harbor's simulated user, cc
and Codex leave no ATIF, and Qwen Code can't be a target at all. Revisit only if a
real use case is being disqualified by it, i.e. a suite we want to run on a vendor
agent needs the scripted user.

The cheap route skips the bridge entirely. Run the vendor agent direct, as today, and
give it a way to ask:
- **The channel:** inject an MCP server (`proctor_chat`, say) whose one tool takes a
  question and answers it with `nb --judge` against the task's sheet. The server runs
  on the host side of the wall, so the sheet stays out of the container.
- **The prompt:** append a line telling the agent it exists, e.g. "I'm not at my desk
  right now. Use the proctor_chat tool if you need to ask me a question; I'll respond
  immediately."

Costs to weigh first:
- **The prompt under test changes.** That's the thing nb's oracle refuses to do (no ask
  tool, no system-prompt steer). The results compare only to other arms run the same
  way, never to an nb arm on the in-process oracle.
- **A tool call is not a halt.** A model that would have stopped and asked in prose may
  call the tool instead, or may not. Ingest would count `proctor_chat` calls as oracle
  turns, and the method section has to say this arm's oracle is a tool.

## Find out why glm's multi-line diff quotes fail verification

Stage 2's grade (`runs/20261102-1143-code-change-jy0n`) had `change-fits` on glm `error` in
12 of 27 runs, every one a quote the verbatim check refused. On `add-retry-flag` the refused
quotes span several added lines and drop the diff's `+` markers
(`codex/add-retry-flag/1/verdicts/change-fits.glm.*.json` against its `diff.patch`), and a
single-line quote from the same answer passes; since the check strips only whitespace, the
markers alone would explain it, which would make it proctor's bug, not glm's. Unconfirmed:
reproduce it in `JudgeTests` with that window and quote before deciding. If it holds, the
likely fix is one more candidate form for a diff part (each side's lines with the marker cut,
old and new kept apart so a quote cannot stitch them), and then it is an issue. The
`rename-module` errors are a different thing: glm wrote `Legacy_Widgets` for
`Legacy.Widgets`, a wrong quote rightly discarded.

## Two loose ends of the move to OpenRouter

Cloudflare is gone (CHANGELOG, Unreleased): code, both configs and the docs are on
OpenRouter, and the candidate decision models are in `project/learnings/jev-judges.md`.

- **The judges are new models.** The move pinned `typesafe/jev-1.14` (served as
  `typesafe/jev-1.14-20261014`) and `z-ai/glm-5.3` where Cloudflare served `jev-1.13.0`
  and `@cf/zai-org/glm-5.2`, as each verdict file's `model` records. Stage 2's verdicts
  (`20261102-1143-code-change-jy0n`) and every bench number in `jev-trial.md` are the old
  models'. No threshold was ever set by calibration (`jev-trial.md` is open), so nothing
  in `grading.pass` rests on them, but a regrade asks the new models afresh (the model is
  in the verdict hash) and can move guardrail rates. Say which models in any comparison
  across the move. A one-cell regrade (2026-11-04, `cc-sonnet/fix-null-deref/1`) went
  through both paths on OpenRouter and agreed with the old verdicts: `ran-tests` yes
  p=0.99 (was 0.98), `stance` complete p=1.00, `change-fits` yes 3/3.
- **minrouter holds about $1 per decision call** (`X-Minrouter-Charged-Usd: 0.9616` for a
  call that settled at $0.000016): decision models are not in its price table, so it
  reserves its top rate until the call settles. Sequential grading fits the $5 wallet; give
  minrouter a row for decision models (minrouter's repository) before grading in parallel.

## A GitHub Pages site to show off the reports

Proctor has no public place where someone can see what a report looks like
without running it. Build a GitHub Pages site for the repository: what
proctor is for (the README's opening), how a suite, task, arm and check fit
together, and a handful of real `report.html` pages from `reports/data/`
linked from it, so the first thing a sceptical reader meets is a finished
report rather than a config file. The bar is the brief's: it reads as done
properly at a glance and holds up to an expert who opens the intervals.
Pick the showcased experiments deliberately (a `code-change` run across
agent arms, an `oracle-change` run, a baseline comparison), and read each
before it goes up: no machine names, no employer, endpoints as written.
The wizard hat is CC BY 3.0, so its credit goes on the site
(`assets/`). Decide whether it is a plain static page beside copied
reports or the first cut of the report site the entries above describe;
the cheap one first is fine if it does not paint the data contract into a
corner. The review is done (`plans/report-structure.md`, *Review, 2026-11-02*); before a
report goes up, scrub the host and gateway names it records there.

## Do not enable Pages until the site's promises are kept

`docs/index.html` is committed as a draft, and Pages stays off until every
claim on it is true of the repository. Today two are not:

- **"Proctor ships with agent-friendly documentation, examples and skills"**
  ("How you use it", step 1). Since 2026-11-08 there is a skill
  (`skills/proctor-suite/`) and a guide (`docs/getting-started.md`); read
  the sentence against them before Pages, and measure the docs with the
  onboarding suite (`plans/onboarding.md`) when it exists.
- **The README no longer links to the site** (`breitreiter.github.io/proctor`):
  the 2026-11-09 rewrite dropped the link rather than carry a dead one.
  Put it back in the opening when Pages is on, and point the README's
  example reports (`docs/reports/`) at their rendered pages there.

(The "Get started" links to `suites/playground` and `suites/cli-for-agents`
were the third; both suites are committed now.)

`proctor setup` prints links to `docs/providers.html#<kind>` on the site
(`plans/setup/README.md`, "The provider catalogue"). The page exists
(2026-11-08; `SetupTests` holds it to an anchor per catalogue kind), but
until Pages is on, every link setup prints is dead, so a public release
of setup waits for Pages. Once released, those anchors are permanent. Add
the weekly lychee check over its vendor links when Pages goes on.

Before switching Pages on, read the page against the repository once more,
line by line: every capability it names should be one a stranger can run
from the README.

## The wrap-up list for 2026-11-01

Cross-repo checklist for tying up the agent-neutral and oracle arc:
`../nb/plans/wrap-up-2026-11-01.md`. B3 of that list is this paragraph.

## Thin the onboarding once binaries ship

When #6 (prebuilt binaries for macOS and Linux, no .NET needed) lands,
revisit the README's Install, `docs/getting-started.md` and the onboarding
skill: no .NET SDK, no sibling nb checkout and build, no `alias` to a dll.
The install step becomes a download, and the guide should get shorter by
about that much.

## Hosted sandboxes for agent arms

For users with a budget who would rather not run Docker or podman on their
own machine (and for CI), run agent arms in a hosted sandbox. Harbor's
`environment.type` is believed to cover several hosted services already
(Daytona, E2B, Modal, Runloop; verify at the pinned version), and proctor
writes that field from `proctor.json` `harbor.engine`, so the first question
is whether this is mostly a validation and docs change: credentials
reaching Harbor's process, the nb layer image living in a registry the
service can pull rather than the local store, the egress allowlist on each
service, and what the report records about where a cell ran. Intermediate
term, after onboarding and the usability sessions.

## Sample suites that demonstrate the site's thesis

A read of `docs/index.html` against what it links to (2026-11-09) found
the page's claims ahead of its examples. The capability has been used in
the field; what is missing is committed suites and reports that show it.

- The showcase contradicts "not a leaderboard". `code-change` compares
  five agent and model arms, and `oracle-change` compares nb bare with nb
  through Harbor. Neither shows one bundle against another on a real
  agent, which is the pitch. Wanted: a suite whose arms differ only in the
  bundle, on a vendor agent, with its report on the site.
- The page says the arms are the population and the thing under test is
  yours, while an arm includes its bundle, so two bundles are two arms.
  The design is agent by bundle. Decide whether the vocabulary names the
  bundle as its own axis or the page's wording changes.
- "Assistants and conversations" addresses a different reader (someone
  building an assistant) from the headline (someone whose product agents
  use), and offers nb one section after the page says only the real
  harness counts. Demote it to "also works for", and describe the cheap
  model as what it is: see the next entry.
- The hero names APIs and websites; every example is a repository
  fixture, a mounted bundle and an offline verifier. Wanted: one example
  graded against a service the fixture runs (`image.services`), in the
  manner of the Stripe study the page cites.
- "Propensity" is claimed in full. A mounted bundle measures whether the
  agent opened and used what was there, not whether it would find it
  unprompted. Say which, and show a check that reads the tool calls for
  it.
- "Nothing else does all three" is a conjunction of our own three
  properties. Soften or cut.

## Pass rate has no headroom on frontier agents

Field use so far (2026-11-09): every fix proctor has driven was a large
stumble (missing data, a broken or mislabelled tool, a known issue absent
from the docs), found by reading failures on a small local model. Once
that model usually completes the task, changes stop being detectable: the
current frontier agents finish anyway, inefficiently, on sketchy tools and
incomplete instructions. So the binary outcome is saturated exactly where
most users are, and more tasks would not help. The paired intervals are
attached to the least informative measure.

What still has headroom on a frontier agent:

- Efficiency: turns, tool calls, tokens, wall time, tool errors met on the
  way. `Stats.cs` summarises duration and tokens per arm (median, p90,
  mean) and compares nothing; only pass rates get a paired difference with
  an interval. A budget check (`max_tool_calls`) turns a continuous
  measure back into a yes or no and discards the power. Wanted: paired
  differences on the counted core, per task, with an interval, as a result
  beside the pass rate. Method to be chosen (a ratio on the log scale is
  the likely shape) and written up in `learnings/prior-art/stats.md`
  first.
- Propensity and workarounds: the run passed, but through the tool under
  test, or around it?
- Harder tasks: friction compounds over a long trajectory, so a
  three-step task sits at the ceiling where a fifty-step one may not.

Two things to decide alongside:

- Which question `n` answers. `n` as the task count answers "does this
  generalise to tasks like these". A regression guard over a fixed suite
  asks "did these tasks change", where samples are the unit and power is
  bought with reruns, not authoring. The report could state which it is
  answering, or offer both.
- How the page describes the cheap model. It is the stress probe: it
  fails where a frontier agent only pays. That is a method worth stating,
  and it is at odds with "anything but the real harness measures
  something your users will never see".

The risk to keep in view: as models get more robust, defect finding
shrinks toward "lint your tools with a weak model". What remains is what
the friction costs, propensity, and the cheaper models users and
subagents actually run, so the pitch should rest there and not on whether
the agent finishes.

## Specimens for human review

A use that has grown up beside the written one (2026-11-09): tasks with
many correct work products, some of which a domain expert prefers. Some
of the preference can go in the prompt, but without seeing a spread of
outputs there is no telling when the prompt over-specifies and narrows
them. Proctor fits because the agent sees the prompt and the bundle and
nothing of the author's own agent configuration, the prompt variants are
bundles (so arms), and the samples, which for a pass rate only reduce
noise, are here the thing being looked at.

What does not fit yet:

- The report is built on checks and rates. A reviewer needs the work
  products: per task, side by side, the arm hidden and the order shuffled,
  as the arm is already hidden from a judge. Wanted: a specimen sheet,
  deterministic, from each cell's diff or final answer. No model writes
  any of it.
- A check is asked of one finished sample. "Would you accept this as it
  is?" has that shape, and a person's answer could fill the `needs-judge`
  slot as a verdict file, after which the existing paired intervals apply.
  "Which of these two?" is a comparison across cells and needs its own
  statistic; leave it until the yes or no proves too coarse.
- Nothing measures how different one task's samples are from each other,
  which is the over-specification signal.

Later: the expert's stated reasons become a rubric, and the human
verdicts the labels a judge check is calibrated against (the bench's job,
as now). Review cost is arms by tasks by samples and reviewers tire, so
these suites stay small on purpose.

## The framing: a usability lab for agent-facing surfaces

A step back (2026-11-09), to be weighed before the site or the README is
rewritten. The parts held with confidence are the ones that run, observe
and present: Harbor, the static checks, the decider and judge checks, nb
as a harness that can be reshaped per question, and the report's visual
language. The part the three entries above put in doubt is inference: the
paired pass-rate verdict as the headline.

The problem underneath is social, and the brief already states it. Tool,
doc, CLI and prompt decisions are made on the author's impression and "it
worked for me once", so a critique of the surface becomes a critique of
its author, and there is no equivalent of "show both proposals to users
and see". User research solved that for interfaces, with small numbers
and observed behaviour, not with significance. The site drifted from this
toward "measure agent experience, with confidence intervals", a different
and weaker promise.

Read that way, the parts fit:

- Harbor and nb supply the participants. A study recruits novices as
  well as experts, because novices expose what experts work around. The
  small local model is the novice, the vendor agents on frontier models
  the experts, and nb the participant that can be configured.
- Checks are the observation protocol: what happened, with quotes, and
  nobody's opinion.
- The report is the neutral third party. Its authority is observed
  behaviour and visible accounting, and that is what takes the author's
  competence out of the argument.

What it would change:

- The central act is two proposals, the same agents, the same tasks. The
  vocabulary serves it worst, the bundle being inside the arm.
- The evidence has an order: observed stumbles decide most often, then
  what it cost to get there, then expert preference among specimens. The
  pass rate is a gate, not the headline.
- The verdict card leads with the least informative result; in most real
  use it reads "Unchanged (unconfirmed)". The visual language stays; the
  hierarchy is what to revisit.
- The statistics keep one job, stopping over-reading. "Could not tell at
  this size" is a useful end to a design dispute: either proposal will do.

Open: whether the page still reads as rigorous at a glance once the
intervals are no longer the lead. The accounting table, the per-run
matrix and the quoted evidence may carry it; that is a judgement about
the audience.

## An analyst over the transcripts, not a moderator in the run

Follows from the usability-lab framing (2026-11-09). A moderator watches
for a participant who pauses, wanders through menus, tries something
wrong and recovers, or opens help, and asks why. In a trajectory those
are: long reasoning with no action or the same call repeated; `read` and
`search` calls the task never needed, and a long stretch before the first
edit; a tool result that errored followed by the same tool with other
arguments, or an edit later undone; `--help`, a docs read, a `skill` or
`web` call. Proctor holds the material (a kind per call, `IsError` per
result) and uses it only as tallies and maxima, which lose the order.
Order matters: help opened first is propensity, help opened after a
failure is a stumble.

Not a moderator: a question put into the run reads to a model as a
correction and changes the rest of it, it works only on nb, and the
answer is a self-report. The text an agent writes before a call is the
think-aloud, to be quoted beside the event (vendor agents may summarise
or hide their reasoning; check at the pins). If an interview is ever
wanted, fork it: replay the transcript to the moment in a separate nb
conversation, so the measured run never sees the question. And "why" has
a better answer here than in a lab, since the participant can be rerun:
the analyst says where runs stumble, the fix and the rerun say whether
the guess was right.

Observations, as a second thing beside checks:

- Proctor's, not the suite's: every run of every suite, nothing to author.
  A check is a question someone knew to ask.
- Events, not verdicts: each has a step and a quote, and none touches the
  pass rate.
- Deterministic first (error then retry, repeated calls, help after a
  failure, calls before the first edit), tuned to catch too much. A judge
  labelling what is left, from a fixed vocabulary with verified quotes,
  comes later.
- Aggregated across runs by a plain key (the tool and its normalised
  error text, the file, the command). "The same error from the same tool
  in 7 of 9 runs" is the finding, and needs no judgement.
- Relevance from the other runs: the cheapest successful run on a task is
  the reference nobody had to write. What most runs read was needed.
- Reported as a short list of moments worth reading, each linked to its
  step. The analyst ranks; a person concludes.
- Ten samples of one model are one participant on ten days. A stumble
  that recurs across arms belongs to the surface; one confined to an arm
  is that model's habit.

How to know it is any good, at no cost: replay the cells behind fixes
proctor has already driven and see whether it surfaces what reading
found; and planted defects (`jotcli` has one), with a clean control that
should stay quiet. Start with the replay, if those cells still exist.

## An event table per cell, indexed by the surface under test

The problem behind the analyst (2026-11-09). When a run was three tool
calls the transcript was the analysis. A recent task of the present kind
(from a documentation site, instrument an arbitrary codebase for
telemetry export and verify it) runs to hundreds of calls across several
languages. No expected trajectory can be written for it, and outcome
checks reduce megabytes of ATIF to one bit and two scalars. That answers
"does it work and what does it cost", not "what should the author
change".

Trajectories cannot be prescribed but can be described against what does
not vary: the surface under test, its pages, commands and error messages,
which every run touches whatever the repository. So the suite declares
the surface (hosts, paths, commands), one small declaration in place of
the expected trajectory.

What that would measure, all of it already in the trajectories:

1. Page-level use: which pages were fetched, in what order, repeatedly,
   never; what the agent searched for, against the docs' own vocabulary.
2. Cost by phase: orient, read docs, edit, verify, repair. The boundaries
   are the uncertain part; try by script on real transcripts first.
3. Right the first time: whether the first verification passed. A yes or
   no with headroom on frontier agents, so the existing paired statistics
   apply. Needs the suite to name the verification command.
4. Docs or memory: an edit before any fetch came from training data, and
   a run can pass on a deprecated pattern without exercising the docs.
5. Spread of solutions: dependencies added and files touched, per run.
6. Cost by fixture stratum, from the fixture's labels.

The shape, so that nothing reads the megabytes:

- One event table per cell: a row per tool call with its step, kind,
  target (URL, path or command), whether it errored, and the tokens and
  time since the previous row. A derived file beside `core.json`, from the
  same walk in `Ingest` that makes the counts.
- Aggregates over the tables, per page, command, label and arm, which the
  report renders with each row linking to its steps.

Before any of it: a throwaway script over real cells. The trajectories
under `runs/` here are small tasks with no documentation site, enough to
try the table, measure 3 and measure 4; page-level use needs transcripts
from a task of the kind above.

## A middle layer for an agent that investigates

Three readers, three layers (2026-11-09): the raw capture, which stays
the ground truth; the report, which in practice is a person's quick look
at whether the treatment moved the needle; and between them something for
the user's own coding agent, told "this looks off in the report, go and
find out why". Proctor already writes for an agent elsewhere
(`setup --check`, `skills/proctor-suite`).

Two observations behind it:

- A reader needs to be stronger than the participant. Given a frontier
  model's trajectory, the same model rarely finds anything, and when
  pressed suggests nonsense; given a small model's, it finds the errors
  and missteps and usually the correction. The stronger reader carries
  the expected trajectory nobody could write. Unchecked: whether it holds
  with the model's name hidden, as a judge window hides it.
- The strongest model a user has is the one in their working session,
  which is ahead of anything they would run as an arm day to day. So the
  review need not be a proctor feature or a paid judge: it is the session
  the user is already in, and it may reach the frontier arms' traces too.
  Untested.

The reader's question has to be put right. "What did this agent do
wrong" lists the participant's faults. The useful question is what in
the surface invited each misstep: the ambiguous page, the misleading tool
description, the missing fact.

It also moves the cost from doing to reading: a free local run leaves a
trajectory dense with stumbles, the session's model reads it and blames
parts of the surface, one thing is fixed, the local run repeats. A pass
per change, with the paid agents run only to confirm a bundle. Limits:
some stumbles are the small model's habit (does the frontier agent spend
extra at the same place?), and a fluent diagnosis is a guess until the
rerun.

An agent's limit is context, so the layer must answer most questions in a
few thousand tokens and address into the raw by step. Smallest first:

1. `events.jsonl` per cell (the event table above), one truncated line
   per tool call with its step id, and one concatenation per experiment
   so a question across runs is one search.
2. One verb that prints a range of a cell's steps, readable and
   truncated. No query language; files and grep.
3. The way from the report to the cells: `summary.md` and `stats.json`
   carry the cell paths behind each number.
4. An investigation skill beside `skills/proctor-suite`: start from the
   failures in `stats.json`, go to the events, open steps, cite cell and
   step for every claim, check that a stumble recurs across runs, blame
   the surface.

This makes the agent, not the report, the event table's first reader, so
the first version needs no rendering and no statistics. The aggregates a
user keeps asking their agent for are the ones to promote into the report
later. The agent's prose stays in the session; what lasts from an
investigation is a fix to the surface or a new check. The report's part
is to make "looks off" noticeable (a run whose cost is far from its
siblings), once the event files exist.

## What checks are for, once trajectories are long

2026-11-09. From reading `runs/20261102-1143-code-change-jy0n` cold in a
session (7 of 27 cells closely, counts across all; arm ids not hidden).

- **`pass` collapses toward the final state, and that is right.** On a long
  task with no expected trajectory the outcome is the only thing that can be
  stated in advance. In that experiment the checks that read the work
  directory (`acceptance`, `builds`, `tests-pass`, `diff-in-scope`) and the
  counted core (`exit_ok`, `no_denials`, `under_budget`) decided every cell.
  The two that did not were model checks: `ran-tests` (decider over the
  trajectory) and `change-fits` (judge over the diff).
- **Three things stay outside the final state.**
  - Validity: whether the agent edited the acceptance tests, reached the
    answer, never got a model response, or never reached the surface under
    test is not visible in the end state.
  - Propensity: whether the agent opened the page or called the tool is a
    lookup in the event table, not a model's opinion.
  - Claim against state: `stance` compares what the agent said with what it
    did. The one model check worth carrying on every suite.
- **A process question with a yes-or-no answer is a query over events, not a
  decider.** `ran-tests` asked jev what a regex over the untruncated command
  answers exactly, and was undecided on a cell where 14 tests ran. A process
  question without a yes-or-no answer ("why did it wander here?") is an
  observation, not a check.
- **The judge stays for final-state questions no script decides.** Not because
  it reads better than a session model: because it is blind, cached,
  repeatable over every cell and every regrade, and its verdict can go in the
  report. Nothing said in a session has those properties.
- **Order to try for a new question:** event query, then decider, then judge.
- **Wanted: try a proposed check over old cells without writing
  `checks.json`.** A check spec and a set of cells in, verdicts out.
  `grade --judge a=b` swaps the judge, not the check. This closes the loop
  from a session's finding to a check proven on evidence already paid for.
  The session does not need a "run this by glm" tool for reading; a fresh
  subagent with arm ids hidden is the better guard against anchoring.

To investigate, from the same read (not yet reproduced, so not issues):

- `ran-tests`: the `tool_calls` window cuts each call's arguments, so a
  `dotnet test` at the end of a long heredoc is invisible
  (`cc-sonnet/add-retry-flag/1`, no at p=0.87).
- `change-fits` errors on every `rename-module` cell in all five arms: the
  judge quotes `Legacy_Widgets` where the diff says `Legacy.Widgets`, in all
  three samples, so every quote is rejected.
- The nb image sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`
  (`Harbor/Compile.cs`, `AgentInstall`); every `dotnet test` on the `floor`
  arm prints 51 to 62 copies of `NETSDK1188`, 21 to 25 KB of the 33 to 38 KB
  a cell reads back, truncated. No vendor arm has one. The `floor` arm's
  tokens and wall time are not comparable with the others until this is gone.
- The fixture images have no `python3`; Sonnet's first edit in
  `add-retry-flag` was a 4 KB Python script that failed, one turn of five.
- The cell's nb config carries MCP servers that cannot start in the sandbox
  (`floor/add-retry-flag/1` stderr). That cell ended `provider_error` at zero
  turns; cause not established.
- The three `qwen` samples of `fix-null-deref` and of `rename-module` have
  identical call counts and observation sizes. If the transcripts are
  identical, three samples on that arm are one.
- nb's system prompt ("Use the file tools to read and edit; use bash for
  `dotnet build` and `dotnet test`") has the local model walk the tree with
  five `list_dir` calls where every vendor agent uses one listing command;
  `todo_write` is 8 of 29 calls in `floor/add-retry-flag/3`.

## Docs or memory

2026-11-09. From field use: on the task "from a documentation site,
instrument an arbitrary codebase for telemetry export and verify it", one arm
ran with the in-container docs host broken. Sonnet completed the task from
memory with full marks on every outcome check. Nothing flagged the arm.

- **The author's premise was a vibe too.** The assumption was that full
  instrumentation was impossible without the docs naming each landmine and
  forcing each incremental step and test; the author could not have done it
  without them. That an expert human needs the docs is not evidence that the
  agent does. This is the same error the project exists to counter, made by
  the person running the experiment, and found only by accident.
- **The arm should not have counted.** Two `validity` checks were missing: a
  setup-hook probe that the declared surface answers from inside the
  container, and an event-table query that the agent fetched at least one
  page of it. A run that never touched the docs is not a sample of the docs.
- **A no-surface arm is the control, and it comes first.** If the arm without
  the docs scores the same as the arm with them, no revision of the docs can
  move the outcome on that task and model, and treatments are wasted spend.
  This is the saturation problem ("Pass rate has no headroom on frontier
  agents") seen from the other side. It is one more arm, not a new mechanism:
  an arm whose bundle is empty.
- **Outcome checks cannot tell memory from docs, and the two agree only until
  the product changes.** When the docs say the new way and memory says the
  old, an agent that skips the docs ships the old way, and the checks pass
  for as long as the old way still works.
- **The fix is the one `oracle-change` already uses.** Its semantics are in
  the answer sheet only and unconventional, so a guess fails acceptance. For
  docs: plant a detail that exists nowhere but the surface under test and
  that a check verifies in the final state (an endpoint path, a required
  header, a renamed config key). Then "docs or memory" is an outcome, not an
  inference from fetches.
- **Propensity changes meaning for a product already in the training data.**
  The question is not whether the agent can do the task but whether it
  consults the surface at all, and whether consulting changed what it did.
  One broken arm suggests the first answer may often be no. Untested beyond
  that arm.
- **What to write down for suite authors** (`skills/proctor-suite`,
  `docs/configuration.md`): run the no-surface arm before any treatment;
  declare the surface so reachability and use are checked; give each task one
  detail memory cannot supply.

## An agent arm cannot carry a bundle

2026-11-10. Found while scoping `agent-onboarding`.

- **What is true today.** Validation refuses `bundle` on an agent arm, vendor
  or nb: "a bundle on an agent arm is not yet implemented" (`Suite/Suite.cs`,
  both agent-arm branches). `Harbor/Compile.cs` copies no bundle into a task's
  image, and the verifier's contract sets `PROCTOR_BUNDLE` and
  `PROCTOR_BUNDLE_MOUNT` empty. Only a bare nb arm sees `{{bundle}}`.
- **Why it matters.** `project/plans/lab.md` centres on two bundles under the
  same vendor agent, and its step 7 is a published experiment of exactly that.
  Proctor cannot run it. The site and the brief say the surface under test is
  the arm's; for the agents most users run, it cannot be.
- **The workaround, and what it costs.** Put the surface in the fixture's
  `image` (clone at a pinned revision). Then the surface is the fixture's, not
  the arm's: two versions are two fixtures or two experiments, compared with
  `report --against`, and the manifest records no bundle source or hash for
  it. `agent-onboarding` does this.
- **What delivery would need deciding.** Where the bundle lands in the
  container and whether the agent is told (a vendor agent sees only the task's
  prompt, no template); whether it is copied into the image per (arm, task) or
  mounted; that the verifier must not see it change the diff; that `resume`
  refuses a changed bundle as it does on the bare path; and how a skill or MCP
  server in a bundle gets installed where the agent looks, which is per agent.
- **Order.** This sits ahead of lab step 3 (the suite declares its surface)
  and the no-surface arm: an empty bundle on an agent arm is the same feature.

## Beats

2026-11-10. The unit a run is cut into for reading is a **beat**, as in a
story: consecutive calls of one kind with one outcome ("explored", "edited
to fix the compile error", "verified, failed"). Not a tool call, which is
too fine and differs by agent (Codex splits one edit into many calls;
Sonnet puts six things in one shell command), and not a phase, which is too
coarse. `project/lab/observe.py` cuts beats by rule; a model's part, if it
keeps one, is the agent's own sentence attached to a beat. The word is not
yet in `CLAUDE.md`'s vocabulary table; it goes there when beats reach the
report.

## Beats, second cut: orient, iterations, wrap

2026-11-11. Supersedes the definition under "Beats" above; the word stays.

- **What failed.** Beats as "consecutive calls of one kind with one outcome"
  gave more beats than calls on Claude Code with Sonnet (17 calls, 28 beats:
  one shell call holds several actions) and routes that group nothing (15
  runs, 14 or 15 routes). Evidence: `agent-onboarding` on Sonnet
  (`20261111-0120-…-dd5u`, 15 runs, 11 passed, $3.90) and on Qwen Code
  (`20261111-0309-…-1u5l`, 9 runs, 0 passed, $0).
- **What was tried** (`project/lab/trials/strategies.py`, throwaway): one
  mode per call; write-try folded into a loop (too coarse); a fixed frame
  alone (says nothing); the agent's own narration as the cut (Sonnet barely
  narrates, Qwen narrates every call); by artifact written (one Sonnet call
  writes many).
- **What coheres** (`trials/s6.py`). A run is **orient** (everything before
  the first write), then one beat per **iteration** (what was written, then
  what was tried, up to the next write), then **wrap**. Cut on events, not
  calls, so a call that writes and runs is cut inside. Sonnet's 17 calls
  and Qwen's 32 both come to 4 to 6 beats; an 82-call run and a 23-call run
  both come to about 12. A write that is never tried is its own beat.
- **Three intents fall out by rule**: advance (writes an artifact for the
  first time), rework (only artifacts already written), repair (follows a
  failed try). Not built.
- **Still weak.** What a try said: a compound call shares one result, so a
  failure can show under two iterations, and the last line of a result is a
  poor summary. Pass or fail per iteration is unreliable (an agent's own
  shakedown fails on purpose in one mode). The cut was checked against
  call-by-call strips, not yet against full transcripts by eye.
- **Routes are dropped.** Runs are compared by counts within the frame:
  orient size, docs reads, iterations, writes per iteration, writes never
  tried, returns to the docs (`trials/two.py`).
- **Embeddings are not the instrument for grouping beats.** They would group
  by agent syntax and file names, are unstable at 90 points, and a cluster
  cannot be defended or reproduced. Kept for discovery only: grouping error
  text, finding the odd beat, suggesting what to count.

## Three things a reader does with trajectories

2026-11-11, from the user. A narrative timeline serves these better than a
coloured strip; each may need its own view.

1. **How is this agent solving this task**, so friction and errors can be
   reduced. Draft: `trials/timeline.py`, a templated timeline per run (beat,
   what was written and tried, the tool's and the agent's own words quoted)
   and a per-arm table of what the runs ran into and where in the run.
   Missing: one typical run per task with "n of N runs did this too" against
   each beat; a better rule for what a try said; friction filtered from
   noise.
2. **Did a treatment improve outcomes.** There is a manual step in the
   middle: the person codes failures from view 1, then says which ones the
   treatment is meant to correct. The treatment's report leads with those
   named counts.
3. **Do different agents need different affordances.** View 1 per agent,
   then view 2 per agent. No alignment of trajectories across agents.

Open, for the lab's canvas (a React frontend is being specified in a
parallel session; a free canvas and a SQLite database tracking revisions,
problems, tests and changes):

- When a problem is detected, how it is marked, and on what (a run, a beat,
  a step).
- How a marked problem becomes a test that checks for it in future runs
  (`observe.py count` and `grade --try` in `lab.md` step 4 are the seeds).
- How a new treatment is recorded as introduced, and against which revision
  of the surface.
- How a claim about a treatment's impact is made, and what it rests on.
- How two runs are compared side by side.
- How variance within an arm is characterised (every run shown, task by
  arm, never an average, is the position so far).

## What `agent-onboarding` found on its first two experiments

2026-11-11. Sonnet `dd5u` and Qwen Code `1u5l`, as above.

- **Every Sonnet run met the same dead end**: `proctor setup --check` says to
  ask the user to run `proctor setup` at a terminal (15 of 15, while
  orienting). All carried on.
- **Six of nine Qwen Code runs never loaded the skill** and built something
  that is not proctor (shell scripts, `.qwen/workflows/*.js`). Only
  `revised-prompt` triggered it, 3 of 3. Why that prompt and not the other
  two is unexamined.
- **The docs fall short**: 6 Sonnet runs read
  `project/plans/on-disk-layout.md`, 3 read `Grade/Checks.cs`.
- **The image lacks what agents reach for**: `python3` (4 runs), a model at
  `localhost:8080` (4 runs).
- **A grader fault, unfixed**: `trial/stays-on-topic/plants.json` sorts the
  agent's tasks by keyword and filed a bike-framed tax question as on-topic
  (`cc-sonnet/stays-on-topic/5`). The other three Sonnet failures are the
  agent's.
- **Qwen Code's skill tool is `skill`, lowercase**; an `observe.json` that
  names only `Skill` misses it.

## The cut read blind: 73% of beats called right

2026-11-11. The cut is in `project/lab/observe.py` (routes and fixes are
gone; it writes `observe-timelines.md` beside `observe.md`). A blinded
reader (one Sonnet subagent, `observe.py blind` renderings, fixed labels)
read ten runs across Claude Code on Sonnet and Haiku, Qwen Code, Codex and
nb, from `agent-onboarding` and `code-change`: 78 beats.

- **57 of 78 cuts right (73%)**; 17 it would join to the beat before, 4 it
  would split. Below the 80% set in `plans/debugging-suite.md`.
- **Joins are fix chains.** One problem worked over several write-then-try
  rounds reads to a person as one piece of work, and the cut makes each
  round a beat. No join rule tried matches the reader: joining every repair
  and rework agrees on 67%, every repair on 62%. The reader's own repair
  label predicts its joins no better (63%).
- **Splits are long unbroken writes** (19 and 32 steps with no try). No rule
  cuts them; that the run wrote that long without trying anything is the
  finding.
- **Two joins were scratch files**: a throwaway under `/tmp` opened the
  first iteration. Fixed after the read (a write wholly under a scratch
  directory is a try, not a write); not re-read.
- **Intent labels kept on 62 of 78 (79%).** Repair is the weak one: it
  fires after any error-looking line and misses repairs whose failure the
  rules did not read. 10 beats the rule called advance or rework the reader
  called repair; 3 the other way.
- **One reader, one model.** How far two readers agree with each other on
  "one piece of work" is not known, so 73% has no ceiling to compare with.
- **Read as:** the iteration is a sound atom and a poor sentence. A second
  level, the episode (an advance and the rounds that follow it on the same
  problem), is what a person names; it has no rule yet. The timeline can
  nest rounds under their advance without claiming where an episode ends.

## `nb-changes`: built, no model run yet (2026-11-11)

`suites/nb-changes` is the debugging suite of `project/plans/debugging-suite.md`:
five defect tasks on nb at `0e11b21`, one fixture each, and the feature task
`ask-user-question`. Loose ends:

- **nb has a timing-dependent test.**
  `RetryingChatClientTests.MinRequestInterval_PacesEveryCall...` failed once
  in 18 full runs of nb's tests, on a loaded machine. Seen once, not
  reproduced. `suite-green` repeats a failing run for this reason. Worth
  reproducing in nb and filing there.
- **`not-a-bug` passes on an empty diff**, which is what giving up looks like
  too. `named-cause` separates them but is a guardrail. A per-task `pass`
  check is not expressible: `grading.pass` is the suite's list.
- **The plants are readable in this repository** (`fixtures/nb-*/plant.patch`),
  so after one public run a later model could have seen them.
- **`ask_user_question` exists only as a patch** in
  `suites/nb-changes/reference/`. Whether it goes into nb is undecided.
- **Arms.** The suite lists only `qwen`. The Haiku shakedown and the Sonnet arm
  wait on agreed spend.
- **nb's golden tool-surface tests fail when `SHELL` is unset** (confirmed
  2026-11-11 in the fixture image). `ShellEnvironment` falls back to
  `/bin/sh`, and `ToolSurfaceGoldenTests.Scrub` replaces the shell's name
  wherever it occurs, so "finish" becomes "fini<SHELL>" and three goldens
  miss. The `nb-*` fixtures set `SHELL=/bin/bash` in the image. An nb issue,
  not yet filed.
- **Proven in the images** (2026-11-11): Harbor's oracle agent with each
  reference as the solution scores every script check 1 on all six tasks,
  in podman, verifier offline. The pass is a scratch script, not a test.
