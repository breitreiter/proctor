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

## Candidates for nb, not proctor

Two things the research pass surfaced that pass the "makes sense without
proctor" test and so belong in nb: the `result` trailer carrying a content
hash of the program and the nb version, so provenance survives without proctor
capturing it; and a directive for a deterministic sampling seed on providers
that accept one. Neither is urgent. Both are additive.

Two more from the CI pass (`learnings/ci-distribution.md`): a `PackAsTool`
target that carries `providers/` into the tool package, and a release workflow
that publishes to nuget.org. nb currently cuts no releases at all, so a
consumer has to clone and build it.
