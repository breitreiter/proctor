---
date: 2026-11-14
status: agreed with the user; nothing built. Supersedes `direction.md` on where the tools live: auspex is a light viewer, and anything that codes a run is proctor's
---

# The loop, and what auspex is in it

## The line

auspex renders; proctor codes. The decider is whether a piece of logic
needs a model, analytic or generative, and so an API key or serious local
compute. As soon as it does, it belongs in proctor. auspex makes no model
calls and needs no key.

Coding friction needs a model. The hand-built ledger (`regimes.md`) found
that rules decide search, locate, help and skill, and that repair,
scaffolding and rereading need a reader. So the friction coder is
proctor's.

## The loop

1. **Proctor runs the experiment** as it does now.
2. **Proctor codes one arm for friction.** Rules where a rule decides, a
   model reader for the rest. Every point cites `run:step`.
3. **The person reviews in auspex.** Each friction point is either left
   alone for now or becomes a proctor check. The person, or their
   session, writes the check.
4. **The check is tried before it is trusted.** `proctor grade --try`
   runs it over that run. It should fail, and its reason or cited step
   should fall in the section the person coded. The stretch version
   regrades the whole run with the new checks, no treatment, to see they
   do what the person thinks.
5. **The person prepares a treatment**: a change, and the checks it
   should flip.
6. **Proctor runs the treatment arm** and reports as normal. Whether the
   named checks flipped is in proctor's report.
7. **Side effects.** Proctor codes the treatment arm afresh, and the
   person reviews what is new.

Proctor's checks are first-class, so auspex adds nothing to checking.

## The file contract

Proctor writes friction points per cell, likely `friction.json` beside
`checks.json`. Each point carries:

- `run:step`, or a range of steps;
- a label from a fixed vocabulary;
- a quote, verified by proctor's judge rule before it is written;
- its source: rule, or model and which model.

auspex reads that, `trajectory.json`, `checks.json` and the manifest.
Nothing else.

## What auspex owns

- **The review view**: one run, its friction points, and the steps
  around each.
- **The person's disposition** on each point: left alone, or the check
  written for it.
- **The treatment record**, a change and the checks it should flip,
  unless proctor takes it.

The last two are plain documents. With no runtime model, the static
demo in `demo.md` comes almost free.

## What moves to proctor

From `direction.md`'s toolset: the event table, sequence operators,
normalisers and citation checks. They are deterministic, but they feed
the coder and the checks. That settles the open question there about
whose normalisers they are. Kept queries become checks, which proctor
already has.

Also proctor's:

- the friction coder;
- `grade --try`, planned in proctor's `project/plans/lab.md` step 4 and
  not built. Step 4 of the loop depends on it.

## Stretch

- **"Acceptable."** A third disposition that drops a friction point from
  future reviews. It needs a rule that recognises the same friction in a
  new run, which is new and unproven. Until it exists, points left alone
  in one arm show up again in the next.
- **The narrow check trial.** Running a check over only the offending
  section means checks must accept a slice. A whole-run `--try` whose
  failure points into that section gives the same assurance more
  cheaply. It needs checks to say where they fired: proctor `todo.md`,
  "Ids in the judge's material". For a check on final state, the step
  that last wrote what it checks is the fallback.

## Open

- **The coder's cost.** A Haiku reader matched the reference's first
  label on 31 of 35 calls. Whether a local model on `imp` does as well
  is untested. If it does, coding is $0 plumbing. If not, it is a paid
  judge step and needs a spend decision per experiment.
- **Off-path passes** (`off-path.md`) show little friction. The coder
  needs the suite's declared surface to flag "left the path" as a kind
  of friction point.
- **How many treatment runs.** For a common stumble, about five. For a
  rare one, the price table in `regimes.md`.
