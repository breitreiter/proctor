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

## Static site generation: pick the shape before the report skeleton hardens

The brief now requires reports to be a per-repo static site. Decide early
whether the site is rendered by proctor directly (a small .NET templating
step, no external tool) or whether proctor emits markdown and a conventional
generator builds it. The former keeps the consumer's CI to one tool; the
latter is more familiar but adds a dependency and a second config file.
Either way the index page and the per-experiment page need to exist before
the report skeleton in `learnings/prior-art.md` §5 is implemented, or the
skeleton gets built as a single file and then rebuilt.

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
