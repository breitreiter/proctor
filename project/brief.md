---
type: brief
title: What proctor is for
created: 2026-09-17
status: draft
---

# What proctor is for

This is a statement of intent and rough shape, written before any code. It is
not a constitution. Expect it to be wrong in places and rewritten as the first
experiments run. When something here hardens into a rule, it moves to
`CLAUDE.md`; until then it is a sketch.

## The problem

nb (`../nb`) evaluates a conversation-program and is very good at that narrow
job. We have used it several times to build test harnesses: for prompts, for
MCP servers, for agent-facing documentation. Every time, a run ends with a large
JSONL log per arm, usually several arms, and nothing else.

Every time, we then built two more things by hand:

- a one-off grader, judge, and reporter to make those logs tell a human a story;
- some sketchy Python to spin up arms, vary a lever, and keep track of which run
  was which.

That "lightweight scaffolding" has been rebuilt often enough, and grown large
enough, that it is nearly as complex as nb itself. Proctor is that scaffolding
built once.

## What it has to do

Three jobs. They may or may not end up as three separate surfaces.

**Manage experiments.** Know the levers nb exposes and make it cheap to vary
them: provider, model, harness costume, tool surface, approval policy, loop and
budget guards, the oracle answer sheet, and a seed transcript. Optimise for
writing nb programs. An experiment is a set of arms, each arm a program and a
sample count, and the manager runs them and keeps the outputs organised so that
nothing has to be labelled by hand afterwards.

**Grade.** Read nb's JSONL without loading the whole thing into a model's
context. nb already puts the useful facts in known places: the `result` trailer
carries the exit reason, usage, turn and tool-call counts, the provider that
actually answered, and the harness worn; the answer is the last
`assistant_text`; every `tool_call` carries whether it was approved and which
ladder rung decided. A grader should be able to answer most questions from
those fields and only reach for the full transcript when a rubric needs it.

**Report.** Turn several arms' worth of graded runs into something a person can
read in one sitting: what was varied, what happened, and where the arms
differ. The oracle bench in nb (`evals/oracle-bench/`) produces a
`results.jsonl`, a `summary.md`, and a `runs/` directory per experiment. That
is the report shape we keep rebuilding and a reasonable starting point.

## The shape, roughly

- **Opinionated about nb.** Proctor is not a general eval framework. It knows
  nb's directive set and wire format and it should feel like nb's other half,
  not a wrapper that could target anything.
- **The seam is the program file and the JSONL stream.** Those are the only
  interfaces nb's existing consumers rely on, so they are what proctor stands
  on. Whether proctor spawns `nb` or calls `nb.Core` in-process is still open
  (see `todo.md`); either way the contract is the same schema.
- **Some features land in nb.** We own nb, so things that are really nb
  concerns, such as a new stop condition, a new exit reason, or a new costume,
  can go there. The test for "belongs in nb" is roughly: would it make sense
  for a user who has never heard of proctor? Everything about arms, sampling,
  comparison, and reporting fails that test and stays here.
- **Do not break nb's existing consumers.** Changes to nb for proctor's sake
  are additive. A program that names none of the new things behaves exactly as
  before. nb's own docs already state this rule for `loop` and `budget`; keep
  it.
- **Logs stay authoritative.** Proctor derives everything from nb's output and
  never edits it. A graded verdict is a separate artefact that points at the
  run, so a regrade with a new rubric never touches the evidence.
- **Runs live in three tiers.** Eval definitions and the small derived layer
  (report, results table) are committed beside the code. The raw layer
  (transcripts, stderr, config snapshots) is gitignored and archived to object
  storage in the same layout as on disk. `proctor archive <experiment>` syncs
  it up and records the location in the report; `proctor fetch <run-id>` pulls
  one cell back when a verdict is challenged. See
  `learnings/where-runs-live.md`.

## Two audiences, two standards

The instrumentation side has one consumer: Joseph. It should pick up patterns
that have worked for other people, but it owes nothing to industry convention or
anyone else's UX. If a lever is easier to reach in a shape nobody else uses,
use that shape.

The reports have a different audience: ordinary programmers, PMs, and
documentation writers, across a range of skill levels. A report has to be
legible to all of them on first read, and still hold up when an expert reviews
or challenges it. Those two requirements pull the same way: plain tables,
counts, and named evidence satisfy both, while prose interpretation satisfies
neither.

## Reports are not written by a model

The tempting shortcut is to hand the logs to a model and let it draw out
meaning and author the report. It was always a poor idea to make report
quality depend on a non-deterministic step. Since roughly August 2026 it has
stopped being a judgement call: Opus, at least when run inside Claude Code, has
been producing prose that reads as word salad often enough that a model-written
report cannot be trusted to be legible at all.

So the rule of thumb, held loosely until the research pass tests it: the
report's structure, tables, counts, and comparisons are produced
deterministically from graded runs. A model may participate in grading, and
where it does its output is constrained to a fixed vocabulary plus quoted
evidence from the transcript, never free prose. Any narrative a report carries
is short, templated, and derived from the numbers rather than the other way
round.

A report also states what it could not have detected. At the sample sizes we
actually run, most differences under thirty points are invisible, and a report
that says "no difference" without saying so misleads both audiences.

## Reports are a per-repo site

The reports for one repository are one coherent unit, not a pile of files:
a static site, generated by proctor, that the owner can deploy wherever they
like. GitHub Pages is the obvious target and must work with no configuration
beyond pointing at the output directory, but the output is plain HTML and
assets and owes nothing to any host.

This changes what a report is. The skeleton in `learnings/prior-art.md` §5
describes one experiment's readout. The site is those readouts plus an index:
every experiment the repo has run, newest first, with the headline number and
the verdict visible from the index, and a per-experiment page underneath.
Two consequences:

- The site is regenerated from committed derived data (`results.jsonl` and
  manifests), never hand-edited, so the whole thing can be rebuilt from the
  repo alone. Transcripts are linked by run id to the archive, not embedded.
- Cross-experiment views become possible and should be cheap: the same
  headline metric over time for one eval file is the view a PM actually wants,
  and the data is already there.

Legibility rules for the site are the same as for a report: tables, intervals,
fixed vocabulary, templated sentences. No model writes any of it.

## The test environment is not our problem

Real test environments are complex: API fakes, fake web servers, podman or
bwrap isolation, fixtures, seeded databases. Weaver's runner spent most of its
lines on a bwrap read-jail and per-harness sandbox flags. Proctor should not
have an opinion about what a project needs to do to test its thing, and should
not fight it.

What that means in practice: an arm has a setup and teardown hook that runs
whatever the project says, and proctor waits for it, records that it ran, and
captures its output beside the run. The lifecycle levels from
`learnings/test-framework-patterns.md` (run, arm, case, sample) are where a
project attaches its own scripts. Proctor's contract is the ordering and the
recording, not the contents. If a project wants every sample in a fresh
container, that is the project's script; proctor's job is to call it at the
right level and note in the manifest that it did.

## Cheap local inference is a resource, not a compromise

We test heavily on qwen-coder, not because any customer uses it but because
two Strix Halo boxes in the house run it for the cost of electricity, and it
is currently the best speed-to-smarts trade at that price. The rough exchange
rate, from experience: a run that takes about twenty minutes at full GPU on a
128 GB Strix takes about five minutes on GPT-5 or Sonnet and costs one to two
dollars. Local is slow and free; hosted is fast and metered. Proctor should
treat the two as different resources and let an experiment use each for what
it is good at:

- **Shakedown runs.** Before spending money, run the whole experiment once on
  local to prove the eval definition, the grader, the fixtures and the
  archive path all work. A shakedown is a first-class thing, not a manual
  habit, and it should be the default before any arm that costs money.
- **A floor arm.** Weaver's harness matrix always carried a local model as the
  floor control, so every result was a gradient rather than a single number.
  Proctor should make that a one-line addition to any experiment.
- **Large N where it is free.** Statistical power comes from cases, and local
  inference lets deterministic evals run at an N that hosted models would not
  justify. The nightly judged pass in `learnings/ci-distribution.md` can be
  local for the subject and hosted only for the judge.
- **Throughput, not cost, is the local constraint.** Two boxes means two lanes;
  a big model needs a box to itself, and switching models is a blocking
  operation. The experiment manager should schedule local arms per box and
  avoid interleaving models on one box.

What local is not for: the judge. The judge should be stronger than, and
outside the family of, every arm it grades (`learnings/prior-art.md` §3), and
that will usually mean a hosted model. The judge's cost is small because it
reads a window, not the run.

## Nice to have: an eval sidecar with a clear boundary

Ideally a project under test gets something analogous to its `test/`
directory: a sidecar that defines its evals in a reproducible way, sitting
beside the code without eval concerns leaking into the core codebase. "Here is
the repo that does the thing, here is its evals sidecar, the two are related
but the boundary is obvious." This is not mandatory, but it should tilt the
design of how an experiment is defined on disk, and it argues for a definition
that lives in plain files a project can check in rather than in proctor's own
state.

Both surviving in-house precedents already have this shape: nb keeps its bench
under `evals/` and weaver keeps its rungs under `evals/`, each inside the repo
it tests. What neither has is a shared convention, which is the part proctor
would supply.

## Open questions

Deliberately unanswered. Each one gets a plan when it is time to answer it.

- Subprocess or in-process? Both are viable and the choice shapes the build.
- ~~Is the grader a model, a rubric, or deterministic checks?~~ Answered by
  the research pass (`learnings/prior-art.md` §3): deterministic checks first,
  then named binary criteria per dimension, with a model only as the
  classifier for criteria that need reading, emitting a label and verified
  quotes, never a verdict.
- What does an experiment definition look like on disk? One file per arm is
  what nb's own README recommends for a harness comparison; whether proctor
  adds a manifest above that is open.
- Language and packaging. nb is C# on .NET 10. The scaffolding we are replacing
  was Python. The in-process option only exists if proctor is .NET.
