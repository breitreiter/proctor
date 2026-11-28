---
type: brief
title: What auspex is for
created: 2026-09-17
status: draft
---

# What auspex is for

This is a statement of intent and rough shape, written before any code. It is
not a constitution. Expect it to be wrong in places and rewritten as the first
batches run. When something here hardens into a rule, it moves to
`CLAUDE.md`; until then it is a sketch.

## The words

The vocabulary is fixed in `plans/exam-variant-attempt.md` (2026-11-15),
which replaced the first one (`archive/suite-task-check.md`, 2026-09-23,
drawn against Anthropic's *Demystifying evals for AI agents*): an **exam**
is the question being answered, its tasks and its checks; a **task** is one
input and one desired outcome, with its own checks; a **check** is one item
on the checklist; a **variant** is what is under test; an **attempt** is one
variant at one task, once, one measurement of a stochastic system; a
**batch** is the attempts one `auspex run` made. This brief was written in
earlier words and has been renamed mechanically since; where "eval"
survives below it means evaluation in general, not an auspex exam.

## The problem

nb (`../nb`) evaluates a conversation-program and is very good at that narrow
job. We have used it several times to build test harnesses: for prompts, for
MCP servers, for agent-facing documentation. Every time, a run ends with a large
JSONL log per variant, usually several variants, and nothing else.

Every time, we then built two more things by hand:

- a one-off grader, judge, and reporter to make those logs tell a human a story;
- some sketchy Python to spin up variants, vary a lever, and keep track of which run
  was which.

That "lightweight scaffolding" has been rebuilt often enough, and grown large
enough, that it is nearly as complex as nb itself. Auspex is that scaffolding
built once.

## What it has to do

Three jobs. They may or may not end up as three separate surfaces.

**Manage the variants.** Know the levers nb exposes and make it cheap to vary
them: provider, model, harness costume, tool surface, approval policy, loop and
budget guards, the oracle answer sheet, and a seed transcript. Optimise for
writing nb programs. An exam is a set of tasks and each variant a program; the manager attempts
each task with each variant a set number of times and keeps the outputs organised so that
nothing has to be labelled by hand afterwards.

**Grade.** Read nb's JSONL without loading the whole thing into a model's
context. nb already puts the useful facts in known places: the `result` trailer
carries the exit reason, usage, turn and tool-call counts, the provider that
actually answered, and the harness worn; the answer is the last
`assistant_text`; every `tool_call` carries whether it was approved and which
ladder rung decided. A grader should be able to answer most questions from
those fields and only reach for the full transcript when a rubric needs it.

**Report.** Turn several variants' worth of graded attempts into something a person can
read in one sitting: what was varied, what happened, and where the variants
differ. The oracle bench in nb (`evals/oracle-bench/`) produces a
`results.jsonl`, a `summary.md`, and an `attempts/` directory per batch. That
is the report shape we keep rebuilding and a reasonable starting point.

## The shape, roughly

- **Opinionated about nb.** Auspex is not a general evaluation framework. It knows
  nb's directive set and wire format and it should feel like nb's other half,
  not a wrapper that could target anything.
- **The seam is the program file and the JSONL stream.** Those are the only
  interfaces nb's existing consumers rely on, so they are what auspex stands
  on. Whether auspex spawns `nb` or calls `nb.Core` in-process is still open
  (see `todo.md`); either way the contract is the same schema.
- **Some features land in nb.** We own nb, so things that are really nb
  concerns, such as a new stop condition, a new exit reason, or a new costume,
  can go there. The test for "belongs in nb" is roughly: would it make sense
  for a user who has never heard of auspex? Everything about variants, sampling,
  comparison, and reporting fails that test and stays here.
- **Do not break nb's existing consumers.** Changes to nb for auspex's sake
  are additive. A program that names none of the new things behaves exactly as
  before. nb's own docs already state this rule for `loop` and `budget`; keep
  it.
- **Logs stay authoritative.** Auspex derives everything from nb's output and
  never edits it. A graded verdict is a separate artefact that points at the
  run, so a regrade with a new rubric never touches the evidence.
- **Attempts live in three tiers.** Exam definitions and the small derived layer
  (report, results table) are committed beside the code. The raw layer
  (transcripts, stderr, config snapshots) is gitignored and archived to object
  storage in the same layout as on disk. `auspex archive <batch>` syncs
  it up and records the location in the report; `auspex fetch <attempt-id>` pulls
  one attempt back when a verdict is challenged. See
  `learnings/where-runs-live.md`.

## Two audiences, two standards

The instrumentation side has one consumer: Joseph. It should pick up patterns
that have worked for other people, but it owes nothing to industry convention or
anyone else's UX. If a lever is easier to reach in a shape nobody else uses,
use that shape.

**Narrowed 2026-11-24** (`plans/axes.md`, decision 17): auspex does not
do decision support. It is a source of truth, a layer for inspecting the
evidence, and a data access layer for Claude and what Claude builds. The
reader below is reached through their agent; the report is not built up
into a product manager's report or a dashboard. What follows is as first
written.

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
deterministically from graded attempts. A model may participate in grading, and
where it does its output is constrained to a fixed vocabulary plus quoted
evidence from the transcript, never free prose. Any narrative a report carries
is short, templated, and derived from the numbers rather than the other way
round.

A report also states what it could not have detected. At the attempt sizes we
actually run, most differences under thirty points are invisible, and a report
that says "no difference" without saying so misleads both audiences.

## Reports are a per-repo site

The site is not an add-on. It is in the first release, for two reasons that
are about people rather than data.

First, it is the surface through which the work is legible to its own author.
A results table in a terminal tells you what happened; a page tells you what
you have built.

Second, gravitas. The current output is legible to one person and looks
hacked together to everyone else, and that is now a problem at work: the
verdict is that evals need to be taken seriously. Meanwhile the competing
evidence is someone firing up their coding agent, running a prompt, and
posting "looks good" in Slack. A list of problems a real run found and a
one-line thumbs-up are visually identical when both are Slack messages. A
link to a page with an accounting table, intervals, and a per-attempt matrix is
not the same kind of object as a Slack message, and that difference is the
argument. The site's job is to make rigour visible at a glance to someone
who will not read the numbers.

So the site is judged on whether a manager who opens the link concludes
"this was done properly" before scrolling, and on whether an expert who does
scroll finds nothing to object to. Those are the two audiences from earlier,
and the site is where they meet.

**The first slice is one file, not the site.** A single self-contained HTML
report per batch, styles and the little script it needs inlined, no
bundle, no external assets, so it can be attached to a Slack message or
dropped on any static host and opened with nothing else. It holds the same
sections as a per-batch page in the site, rendered from the same
`stats.json` and `results.jsonl`, and a markdown twin for places that take
markdown. This exists to get stakeholder feedback on the report shape
immediately, before a line of the site is written, and everything learned
from it carries into the site because the data contract is shared. The site
then adds what one file cannot: the index, history, and cross-batch
views.

The reports for one repository are one coherent unit, not a pile of files:
a static site, generated by auspex, that the owner can deploy wherever they
like. GitHub Pages is the obvious target and must work with no configuration
beyond pointing at the output directory, but the output is plain HTML and
assets and owes nothing to any host.

This changes what a report is. The skeleton in `learnings/prior-art.md` §5
describes one batch's readout. The site is those readouts plus an index:
every batch the repo has run, newest first, with the headline number and
the verdict visible from the index, and a per-batch page underneath.
Two consequences:

- The site is regenerated from committed derived data (`results.jsonl` and
  manifests), never hand-edited, so the whole thing can be rebuilt from the
  repo alone. Transcripts are linked by attempt id to the archive, not embedded.
- Cross-batch views become possible and should be cheap: the same
  headline metric over time for one exam file is the view a PM actually wants,
  and the data is already there.

Legibility rules for the site are the same as for a report: tables, intervals,
fixed vocabulary, templated sentences. No model writes any of it.

**The site is a prebuilt app plus data files.** Auspex ships
one static front-end bundle, built once in auspex's own repo and versioned
with it. A consumer's report site is that bundle plus a `data/` directory of
JSON: the index of batches, and per batch the manifest, the results
table and the derived statistics. The browser loads the JSON and does all the
rendering, sorting, filtering and cross-batch views client-side. This is
architecturally ugly and it works: some well-known dashboard products send
hundreds of thousands of records to the browser and let it render them, and
that choice spared them a great deal of backend engineering. Our data is far
smaller than that. Two consequences:

- The derived tier that gets committed is exactly the JSON the site reads, so
  "rebuild the site" means "copy the bundle next to the data". There is no
  generation step in the consumer's CI beyond writing JSON, and no external
  site generator.
- Auspex still renders markdown deterministically for the CI step summary
  and for the pull request. Both renderers read the same JSON, so the
  numbers cannot disagree.

**No design system of our own.** The site uses a component library that
ships an opinionated design out of the box, with the micro-interactions
already done. A default that looks like every other app built on that
library is fine; it is better than hand-rolled CSS, and nobody is coming to
the site for its looks. MUI is the familiar reference for what "complete"
means here. Since the bundle is built once in auspex's repo, a React app
costs nothing in a consumer's CI, so React libraries are back on the table.
The choice is open in `todo.md`.

## The test environment is not our problem

Real test environments are complex: API fakes, fake web servers, podman or
bwrap isolation, fixtures, seeded databases. Weaver's runner spent most of its
lines on a bwrap read-jail and per-harness sandbox flags. Auspex should not
have an opinion about what a project needs to do to test its thing, and should
not fight it.

What that means in practice: a variant has a setup and teardown hook that runs
whatever the project says, and auspex waits for it, records that it ran, and
captures its output beside the attempt. The lifecycle levels from
`learnings/test-framework-patterns.md` (run, variant, task, attempt) are where a
project attaches its own scripts. Auspex's contract is the ordering and the
recording, not the contents. If a project wants every attempt in a fresh
container, that is the project's script; auspex's job is to call it at the
right level and note in the manifest that it did.

## Cheap local inference is a resource, not a compromise

We test heavily on qwen-coder, not because any customer uses it but because
two Strix Halo boxes in the house run it for the cost of electricity, and it
is currently the best speed-to-smarts trade at that price. The rough exchange
rate, from experience: a run that takes about twenty minutes at full GPU on a
128 GB Strix takes about five minutes on GPT-5 or Sonnet and costs one to two
dollars. Local is slow and free; hosted is fast and metered. Auspex should
treat the two as different resources and let a batch use each for what
it is good at:

- **Shakedown runs.** Before spending money, run the whole batch once on
  local to prove the exam definition, the grader, the fixtures and the
  archive path all work. A shakedown is a first-class thing, not a manual
  habit, and it should be the default before any variant that costs money.
- **A floor variant.** Weaver's harness matrix always carried a local model as the
  floor control, so every result was a gradient rather than a single number.
  Auspex should make that a one-line addition to any batch.
- **Large N where it is free.** Statistical power comes from tasks, and local
  inference lets deterministic exams run at an N that hosted models would not
  justify. The nightly judged pass in `learnings/ci-distribution.md` can be
  local for the subject and hosted only for the judge.
- **Throughput, not cost, is the local constraint.** Two boxes means two lanes;
  a big model needs a box to itself, and switching models is a blocking
  operation. The batch manager should schedule local variants per box and
  avoid interleaving models on one box.

What local is not for: the judge. The judge should be stronger than, and
outside the family of, every variant it grades (`learnings/prior-art.md` §3), and
that will usually mean a hosted model. The judge's cost is small because it
reads a window, not the run.

**The judge's home is OpenRouter, through the gateway.** Since 2026-11-02;
until then it was a Cloudflare Workers AI subscription, whose flat fee had
stopped being the argument (stage 2's grade cost $2.79 on it). OpenRouter
serves GLM and Kimi for `judge` and the decision models for `decide`, billed
per call; GLM and Kimi are in the family of no variant we run and no harness a
customer uses. That is the profile the judge literature asks for, and it is
what weaver chose in July.
Calibrate it against human labels before trusting it; the oracle bench is a
small version of that exercise already run against GLM.

## Who the customers are, and what that fixes

Roughly sixty percent of customers are on Claude Code, twenty on Codex, and
twenty spread across Grok, Copilot and other tails. So the default batch
is the one in the next section: Claude Code with Sonnet as the primary variant,
Codex with GPT-5 as the secondary, qwen-coder on nb as the floor. nb has
costumes for the first two and none for the tail, and building one per tail
vendor is not worth it. A report says which harnesses were tested and does not
imply coverage of the rest.

## A typical batch, concretely

The shape auspex has to serve first is the one that runs at work today.
Three fixtures, each a test repository where the agent is asked to make a code
change. Three variants:

| variant | harness | model | attempts per fixture | attempts in all | wall time | cost |
|---|---|---|---|---|---|---|
| floor | nb | qwen-coder, local | 3 | 9 | about three hours | electricity |
| codex | Codex CLI | GPT-5, OpenAI API | 1 | 3 | minutes | metered |
| claude | Claude Code | Sonnet 5, Anthropic API | 1 | 3 | minutes | metered |

Fifteen attempts. The floor variant is the measured one; the hosted variants are spot
checks. That is a sensible use of the resources in the previous section, and
auspex should make it the default shape rather than something assembled by
hand. Three things follow.

**The report must say what this design can and cannot show.** A hosted variant at
one attempt per fixture has no interval; it is three observations. The floor
variant at three attempts per fixture is nine observations and a Wilson interval on
it is wide. A pass-rate comparison between qwen and Sonnet on this design
cannot detect anything smaller than a landslide, and the report should print
that sentence rather than a verdict glyph. What the design does support is
per-fixture reading: did each variant solve each fixture, with the transcript one
fetch away. The task-by-variant matrix from `learnings/prior-art.md` §5 is the
headline table for this shape, not the variant-by-rate table.

**The fixtures are the tasks, and they are repositories.** A task here is not
a prompt row; it is a checkout, possibly with a container and fake services
around it, that gets mutated by the agent and must be reset per attempt. This is
the environment stance above in its most concrete form: the reset is the
project's script, auspex calls it at the attempt level and records it.
Grading is mostly deterministic (does the project build, do its tests pass,
does the diff touch what it should) before any judge reads anything.

**Real harness or nb costume is an open question, per variant.** nb can wear the
`codex` and `claude-code` costumes, and weaver ran the real CLIs instead. The
costume gives one program file and one transcript format for every variant; the
real CLI gives fidelity and a different log for each. The manifest should
record which was used, and the report should not compare a costume variant to a
real-CLI variant without saying so.

## Nice to have: an evaluation sidecar with a clear boundary

Ideally a project under test gets something analogous to its `test/`
directory: a sidecar that defines its exams in a reproducible way, sitting
beside the code without evaluation concerns leaking into the core codebase. "Here is
the repo that does the thing, here is its evaluation sidecar, the two are related
but the boundary is obvious." This is not mandatory, but it should tilt the
design of how an exam is defined on disk, and it argues for a definition
that lives in plain files a project can check in rather than in auspex's own
state.

Both surviving in-house precedents already have this shape: nb keeps its bench
and weaver its rungs in a directory of their own, each inside the repo it
tests. What neither has is a shared convention, which is the part auspex
would supply.

## Open questions

Deliberately unanswered. Each one gets a plan when it is time to answer it.

- Subprocess or in-process? Both are viable and the choice shapes the build.
- ~~Is the grader a model, a rubric, or deterministic checks?~~ Answered by
  the research pass (`learnings/prior-art.md` §3): deterministic checks first,
  then named binary criteria per dimension, with a model only as the
  classifier for criteria that need reading, emitting a label and verified
  quotes, never a verdict.
- What does an exam definition look like on disk? One file per variant is
  what nb's own README recommends for a harness comparison; whether auspex
  adds a manifest above that is open.
- Language and packaging. nb is C# on .NET 10. The scaffolding we are replacing
  was Python. The in-process option only exists if auspex is .NET.
