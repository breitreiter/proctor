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
  ("How you use it", step 1). There is no suite-authoring skill and no
  guide written for a coding agent; `plans/onboarding.md` is the plan for
  them. Build them, or cut the sentence.
- **README's opening links to the site** (`breitreiter.github.io/proctor`),
  which does not exist until Pages is on, so the README carries a dead
  link until then. Do not push it to a public audience before Pages, or
  drop the link for now.

(The "Get started" links to `suites/playground` and `suites/cli-for-agents`
were the third; both suites are committed now.)

`proctor setup` will print links to `docs/providers.html#<kind>` on the site
(`plans/setup/README.md`, "The provider catalogue"), so setup cannot ship
before Pages is on and that page exists, with an anchor for every kind in
the catalogue. Once released, those anchors are permanent.

Before switching Pages on, read the page against the repository once more,
line by line: every capability it names should be one a stranger can run
from the README.

## Charts at the top of the report

The report's layout is clean, but its top is all tables and sentences: the
reader has to read numbers to get the headline. Float the topline up with
one or two charts above the fold. Which ones is open; think before
building. Candidates to weigh: pass rate per arm with its 95% interval as
a dot-and-whisker strip, the paired difference against the baseline on the
same axis (held, improved, regressed at a glance), a task-by-arm matrix as
a heatmap, and cost or tool calls against pass rate per arm. Constraints
from what exists: every number still comes from `stats.json` (the renderer
draws, it never computes), `summary.md` needs a text equivalent of each
chart since the two renderings are one list of blocks, the page must work
without script (inline SVG, not a chart library), colour stays semantic,
and the snapshot tests will need new approvals.

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
