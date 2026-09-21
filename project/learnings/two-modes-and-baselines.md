---
type: learnings
title: Two modes and a baseline — from a bench that is already running
created: 2026-09-21
---

# Two modes and a baseline — from a bench that is already running

Taken from an interview with someone running a working agent bench in
industry: a vendor measuring how well arbitrary coding agents can install
their telemetry exporter into a customer's repository. The vendor, the
product and the customers are deliberately not named here; nothing in this
document is specific to them, and everything in it is a shape proctor either
has, has wrong, or does not have at all.

It is the first outside look at proctor's model from a bench that has already
survived contact with real runs, so where it disagrees with our plans, it
wins by default and our plans have to argue back.

## The bench, in one paragraph

Three things are steered at the agent: a seed prompt the user pastes in, a
helper API whose key and URL arrive in that prompt, and a documentation URL
the agent fetches while it works. In production those three are entirely
disconnected and each moves on its own. In the bench they are pinned together
into one snapshot: the API is simulated, the documentation is an isolated
copy, and the destination the exporter reports to is a simulated collector.
Each cell runs in its own container with the fakes inside it. The measured
questions are: which agent vendors handle this well, how it behaves against
healthy and flawed codebases, and what happens when the prompt, the API or
the docs change.

## Seven things to import

### 1. A baseline is pinned cells, not pinned numbers

The concept we were missing is a baseline: a pinned set of samples meaning
"this is what we believe is achievable right now." Its job is to narrow a
human's decision space later — *we tried X; Y measured a little better but Z
got worse* — without anyone reading the full history of runs.

It has to pin the cells, by run id and coordinates, and not a frozen
`stats.json`, because a check added later can only be applied backwards if
the evidence is still there. It pins per case, so adding cases for a new
feature does not reset the baseline for the old ones.

### 2. Re-baselining is the common path; retro-grading is the exception

Asked what happens to existing runs when a new check is added, the answer was
that they re-baseline: too many pieces move at once for archaeology on old
runs to be worth it. That is cheaper than the design we were reaching for, and
it makes re-baselining a one-command operation the primary requirement rather
than cross-run regrading.

The case that does earn retro-grading has not happened to them yet and is
easy to see coming: a mature bench in maintenance mode is handed a large new
feature, and the question becomes both *did we meet the new challenge* and
*did we damage what already worked*. There the old cases must keep their
meaning while the check set and the case set both grow.

### 3. Two modes of working, not one

The sharpest observation in the interview, offered in retrospect by someone
who had been living in both:

| | explore | guard |
|---|---|---|
| what moves | many things at once | one commit |
| comparison | between arms, inside one experiment | against a baseline, across time |
| checks | under construction; every awful failure becomes a new one | frozen |
| the question | is this better | did this damage anything |
| cadence | a campaign | every commit |

Their exporter bench started in explore — *oh wow, that was awful, add a
check for that* — and has reached a local maximum where the job is now guard.
Their second bench (documentation quality for coding agents) was born in
guard mode and runs evals after each commit.

Proctor is built entirely for explore. `stats.json` compares arms within one
experiment and nothing compares an experiment to its predecessor, so proctor
could not serve their second bench at all today.

### 4. Outcome over process: scoring the transcript punishes novelty

They stopped scouring transcripts for behaviours. It got expensive, and worse,
there are many correct ways to solve the task, so behavioural checks were
penalising agents for solutions that were right but unlike the expected one.

Grading is now over the final state: the simulated collector received traffic
consistent with the fixture's call graph, the key was stored safely, the code
still compiles, the files touched were the expected ones. Transcripts are
still kept and still read — to debug a failure and to understand how the crime
scene came to look that way — but they do not score capability.

This demotes most of our `tool_calls` vocabulary from pass criteria to
debugging aids and validity checks. It does not delete it: see §7.

### 5. Evidence is more than the transcript and the diff

The strongest signal in that bench is traffic arriving at a simulated
endpoint — evidence that is neither a transcript nor a diff, and has no home
in our model. Our `diff.patch` turns out to be one instance of a general
thing: whatever the teardown hook collected, deposited in the cell, read by
script checks. The special case should become the general one.

### 6. Expectations live with the fixture

Each fixture is a small codebase in a different stack, and each carries its
own expected-spans file and its own checker script. "Sensible traffic" is not
a property of the eval or of the case; it is a property of the codebase,
because knowing what the call graph should look like *is* knowing the fixture.

Proctor declares checks once on the eval and feeds them per-case values from
the case's `expect` block. That is the wrong shape for anything a fixture
knows about itself, and it is an argument for fixtures as a first-class thing
that cases refer to, rather than a field inside a case.

### 7. Isolation is per-cell and verified, not enforced

Each cell is a container with the fake services inside it, in an out-of-the-way
location. The agent is not prevented from finding those processes and prying
an answer key out of them. It is steered toward the fixture, and afterwards
the logs get a cheap review for cheating.

In practice they have seen cheating once, and it was their own fault: the
oracle file had been placed one directory above the codebase under test. No
shade to the agent — it was reachable, so it was read.

Two things follow. Verify-don't-enforce is the affordable posture, and it is
the one honest use of the transcript in a bench that otherwise grades final
state: an anti-cheat check is a *validity* check, asking whether this sample
counts, not a capability check contributing to the score. And our layout
can walk into that same trap. The cell is not above the work directory
(cells sit under `runs/`, checkouts under `.proctor/work/`), but the
repository root is, five levels up, and it holds the whole eval: the cases,
the expect blocks and the check scripts. Worse, a fixture that is copied
wholesale into the checkout carries anything stored beside it, which is the
exact mistake they made. The fixture's own expectations and checker must
live beside the checkout source, never inside it.

## What this changes for proctor

- **The word "experiment" is taken twice.** Theirs is the pinned snapshot of
  the intervention — prompt, fake services, frozen docs — versioned as one
  unit though the real things are disconnected. Ours is one execution of an
  eval across the matrix. One of the two has to be renamed before either
  appears in a report someone reads.
- **The variable under test is not always an overlay on the workspace.** The
  earlier guess was that a files-shaped variable would be a directory copied
  over the fixture checkout. Here only one of the three surfaces is a file at
  all: one is the agent's input, one is a live service, one is a URL fetched
  during the run. An arm has to be able to name a pinned bundle of *all
  three* and record its identity, not just copy files.
- **A baseline, and a guard-mode report.** Pinned cells per case, a
  one-command re-baseline, and a second report shape that compares this
  experiment to the baseline with a tolerance instead of comparing arms to
  each other. The research already pointed here (`prior-art/ci.md`'s
  committed baseline and `--fail-on regression --tolerance`); the interview
  says it is not optional.
- **Fixtures become first-class**, carrying their own expectations and
  checker, with cases referring to them.
- **Collected artifacts become general**, with `diff.patch` as one instance,
  and script checks reading whatever is in the cell.
- **Check kinds are not all the same kind.** Capability checks decide the
  pass; validity checks decide whether a sample counts at all; the rest are
  guardrails. We currently only distinguish "in `grading.pass`" from "not in
  it".
