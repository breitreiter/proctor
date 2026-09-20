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
