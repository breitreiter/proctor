---
date: 2026-11-12
status: superseded in part by `loop.md` (2026-11-14). The reasoning stands (prose from the model, every number a query, every quote checked); where the tools live does not. They are proctor's, and auspex is a viewer. Earlier it superseded "the build waits until beats are stable" (`workbench.md`, step 4)
---

# Direction: the model writes the account, the tools make it checkable

> **Superseded in part (2026-11-14), see `loop.md`.** Anything that needs a
> model, or feeds something that does, lives in proctor. That covers the
> event table, operators, normalisers, citation checks and kept queries
> below. auspex renders proctor's output and makes no model calls. Read
> this note for the reasoning, not for what auspex builds.

## The turn

A model's short account of one run ("what happened, what problems it hit")
is already reliable; the user does it routinely. What it cannot do is
**count across runs** (it greps and eyeballs a number) or **be checked**
(nothing ties "most runs stumbled on X" to evidence). The work on beats was
an attempt to get both by cutting runs by rule, and the rule keeps failing,
because agents chunk work differently (proctor `todo.md`, "Beats, second
cut").

So move the determinism from the cut into the tools. The model writes the
prose; every number in it comes from a query that can be rerun, and every
quote resolves to a step. auspex is the toolset an agent uses to analyse
trajectories, far better than `rg`, `awk` or a bare Python interpreter, and
the analysis is the user's own agent session driving it.

Proctor's report is unchanged: deterministic tables, model labels only from
a fixed vocabulary with verified quotes. An auspex analysis is a different
artifact, read by a person, and it is honest about what is prose and what is
counted.

## The tools

1. **The event table.** Every run of an experiment, one row per event: run,
   arm, task, sample, outcome; step (the ATIF step id, original index);
   kind (proctor's agent-neutral kinds, `Ingest.Kinds`); tool; a normalised
   command or path; error and exit status; the agent's own text before the
   call. A compound shell call is split into its actions, each a row
   carrying the call's step. SQLite or DuckDB. Built from what proctor
   already writes (`trajectory.json`, `core.json`, the manifest, `checks.json`),
   no model. "The same error from the same tool in 7 of 9 runs" is one
   query.
2. **Sequence operators**, which SQL and awk do badly: error then the same
   tool within n steps; help (a `--help`, a docs read, a skill or web call)
   after a failure; an edit later reverted; everything before the first
   write; a write never tried. The order-dependent patterns from proctor's
   "An analyst over the transcripts", which tallies lose.
3. **Normalisers.** Error text with paths, numbers and ids stripped;
   commands reduced to their shape. The plain keys aggregation needs, and
   what an agent gets wrong every time with an ad hoc regex.
4. **Run primitives.** A slice of a run by step ids; two runs side by side;
   a task's reference run (the cheapest passing one: the reference nobody
   had to write).
5. **Citation checks.** A write-up cites `run:step` with a quote; a checker
   confirms each quote is in that step, by proctor's judge rule (substring
   with whitespace removed, raw or JSON-unescaped). A claim that does not
   resolve is flagged, not trusted.
6. **Kept queries.** An analysis saves its queries beside its prose. Rerun
   on the next experiment, they are the "did the treatment help" comparison.
   A query that keeps earning its place becomes an observation, then a
   check (proctor's `grade --try`, `lab.md` step 4): the path from a found
   problem to a test.

Delivery: a CLI with JSON output and a skill that teaches its vocabulary
(the shape of proctor's `skills/proctor-suite`). No UI first.

## What changes

- **Beats become a derived column**, not the reading unit: an iteration
  index (orient | iteration n | wrap) the agent may use or ignore. The
  frame need not be right for every agent any more, so the build does not
  wait on it.
- **Docent's split is the one to copy** (`learnings/docent-analysis-plans.md`,
  revised): a query decides which rows exist, a model reads only what it
  was handed. Free-text findings are allowed in an analysis, provided
  every number is a query and every quote resolves.
- **ET²'s views** (`learnings/et2-progression-analysis.md`) remain the
  likely picture once there is one, fed by queries rather than learned
  stages.

## First test, at $0

`agent-onboarding`'s two experiments (Sonnet, 15 runs; Qwen Code, 9). Two
sessions answer the same three questions from proctor's "Three things a
reader does": one with only the event table and two or three operators,
one with `rg` and Python over the cells. Compare accuracy (against a
reading by hand) and how much of each answer can be checked. The table is
built from the cells as they are; the sessions run in-session, not through
a paid route.

## Open

- Which operators earn a place first: start from the patterns the
  `agent-onboarding` reading found, not from a catalogue.
- How a compound shell call is split, per agent (Sonnet puts six things in
  one command).
- Whether the table's normalisers are proctor's (beside `Ingest`) or
  auspex's, given proctor's checks will want the same keys.
- Where kept queries live, and how one becomes a proctor check.
