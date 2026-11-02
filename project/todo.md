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

## After dropping Cloudflare (done 2026-11-02)

The judges are on OpenRouter (CHANGELOG, Unreleased). What is left of the item:

- **minrouter holds about $1 per decision call** (`X-Minrouter-Charged-Usd: 0.9616` for a
  call that settled at $0.000016): decision models are not in its price table, so it reserves
  its top rate until the call settles. Calls are sequential today, so the $5 wallet admits them;
  give minrouter a row for decision models before grading in parallel.
- **Families:** Clef, Baseten's joey/elephant/mammoth, Kev and Tev1 are Qwen fine-tunes, so
  they are not off-family for qwen arms. If one becomes a judge, set its `family` to `qwen` so
  `SameFamilyNotes` warns.
- **Kind names** stay `systemone` and `chat`: they name the wire shapes, not Cloudflare.
- **Candidates** (input $/M; decision models bill no output): `typesafe/jev-1.14` 0.042 (the
  judge now), `perplexity/pplx-decider-v1.2` 0.02, `liquid/d1.1` 0.02, `upstage/solar-decide`
  0.05, `inception/mercury-decide` 0.02, `microsoft/microsoft-decision-1` 0.042,
  `openai/gpt-6-luna-decisions` 0.10; `GET /x/openrouter/v1/models?output_modalities=decisions`
  lists them. Recalibrating a new decision model is the bench's job (`jev-trial.md`).

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

## The wrap-up list for 2026-11-01

Cross-repo checklist for tying up the agent-neutral and oracle arc:
`../nb/plans/wrap-up-2026-11-01.md`. B3 of that list is this paragraph.
