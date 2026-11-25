---
date: 2026-11-13
status: hypothesis, from the user; untested. Replaces the drill-in use case noted earlier the same day, which is now a minor case below
---

# Hypothesis: the hard case is a run that succeeds off the path

## The hypotheses

1. **Frontier models find a way.** Given bash or python they solve the
   problem. The solution may be gnarly, but it meets the stated criteria.
2. **The exception is an obvious, insurmountable problem**, and
   insurmountable is a very high bar. In the user's instrumentation
   testing, the documented way to instrument Ruby code could not be
   completed at all. The agents reverse-engineered the config files and
   wrote the config by hand. That is hypothesis 1 again: they passed by
   going around the documented path.
3. **A fully blocked run is easy to read.** If the task is truly
   impossible, evidence of being stuck should be most of the transcript.
   Another agent can find where it got stuck without help (the loud
   regime in `regimes.md`).
4. **So the part that needs analysis, and a UI, is the off-path pass.**
   The agent finished the task in a way nobody anticipated or wanted, or
   one at odds with the tools and docs it was given. Example: we build a
   CLI for slicing data, and the model sends raw SQL using the CLI's
   credentials. The test's expectations are met. What we hoped to learn
   about the CLI is not.

## What follows

- **An off-path pass is a pass.** No outcome check fails, so the arrival
  state is 1, 2 or 4 in `regimes.md`, not 3 or 5. The trigger to look is a
  suspicion or a measure, not a red mark. That rules out drill-in from a
  failed check as the main way in.
- **It is a measure against the surface under test.** Proctor's
  `todo.md`, "An event table per cell, indexed by the surface under test",
  has the suite declare the surface: hosts, paths, commands. Off-path is
  what that declaration makes countable: work done towards the goal by
  calls that don't touch the surface. Examples are SQL in place of the
  CLI, a config written by hand in place of the documented installer, or
  the tool's own files read in place of its docs. Item 4 there ("docs or
  memory") is a sibling: a pass on a pattern from training data never
  exercised the docs.
- **The signature is likely a pivot.** A stretch on the surface, often a
  failure, then a turn to a general tool that reaches the same effect.
  That is close to the orientation response in `surprise.md`, which
  connects the two notes.
- **Siblings are the reference.** Arms or runs that stayed on the path,
  against the ones that left it. The question per run is where it left
  the path, and whether every run that left did so at the same place. A
  shared exit point is a defect in the surface.
- **Where the UI earns its place:** reading the run around the exit
  point, with the surface calls marked apart from the rest, to judge
  whether leaving the path was a workaround for a real defect or the
  agent's preference.

## A second anecdote: on the path, compensating

From the user, anonymised. Claude built a dashboard against an in-house
MCP server. The dashboard was fine. Only later, reading the server's code,
did the user find out what the agent had been working around:

- **Markdown tables for data.** The server returned results as markdown
  tables, and Claude wrote python to turn them back into JSON.
- **Zeros for missed polls.** The server filled missed polls with zeros,
  so Claude consulted another table to tell missing data apart from data
  that really was zero.

Here the agent stayed on the surface, so it is not off-path in the sense
above. It paid a hidden tax for the surface's defects. That gives a third
category, between on the path and off it: **on the path, compensating.**
It is the quiet regime's "output that needs work", with two signatures to
look for:

- **A transform right after a surface call.** Code that parses or reshapes
  the surface's own output. This is friction, a cost on every run.
- **A cross-check that repairs meaning.** A second lookup to recover what
  the first result hid. This is worse than friction: a run that misses it
  passes with wrong numbers, and no outcome check sees it. The moment the
  agent notices is likely a surprise (`surprise.md`).

The user found both by reading the server, not the transcript. The
transcript held the evidence all the same: the parsing code and the
second lookup are steps. A per-surface count of "results that were
post-processed" and "results followed by a lookup to another source"
would have flagged both.

### The two counts, tried (2026-11-13)

`trials/compensate.py` ran over the agent-onboarding runs: Sonnet 15,
Haiku 3, Qwen 9. Their surface under test is proctor's CLI (`proctor`, or
a suite's shakedown script, which wraps it). These are the only copied
runs with a tool surface. The other suites change code, and their
surface is the codebase.

| arm | runs | calls | surface calls | piped to a parser | piped to head/tail/grep | lookup elsewhere next (real, by hand) |
|---|---|---|---|---|---|---|
| cc-haiku | 3 | 103 | 33 | 0 | 24 | 1 |
| cc-sonnet | 15 | 254 | 108 | 0 | 70 | 1 |
| qwen | 9 | 331 | 28 | 0 | 2 | 0 |

- **Post-processing: nothing.** No surface output went into python, jq
  or awk. Trimming with head and tail is everywhere and harmless: it is
  how agents read long output, not a sign of a defect.
- **Lookup elsewhere: one real case, seen twice.** The rule flagged 8.
  By hand, 2 are real, the same event in different runs; the rest are
  `sed` editing the shakedown script and one source read.
  - Haiku `exact-answers/1` s29→s30: `proctor grade` printed
    `states-the-figure=error` on every cell with no reason. The agent
    said "I'll read the reason in one cell's checks file" and catted
    `runs/…/checks.json`.
  - Sonnet `exact-answers/4` s17→s18: the same, with more effort. It
    edited its shakedown script to keep the temporary directory
    (`KEEP=`), then ran `find runs -name checks.json` to get the reason.
    The reason was a misnamed key, and it fixed that in s19.
- **The cause is in proctor.** `Grade/Grade.cs:39` prints `name=result`
  for each check that didn't pass, and drops the `Reason` that
  `checks.json` holds. This is the dashboard's zeros-for-missed-polls in
  miniature: the surface reports a status without its meaning, and the
  agent goes to another source to recover it. Neither agent complained,
  and both worked around it.
- **Reading proctor's source** (`.cs` files) happened in 6 of 18 Claude
  runs, 11 calls in all, mostly to learn check semantics
  (`Grade/Checks.cs`). That's off-path in the "tool's files in place of
  its docs" sense, and worth a separate look: what were the docs missing?

The rules are crude. Command substitution, `sed -i` and heredocs all
fool them, so at this size every hit gets read by hand. On 27 runs the
counts say little. What they did is find one real surface defect, which
is the point.

## The minor case: drill-in from a failed check

From earlier the same day, kept because it is still wanted, though not
the user's habit: jump from a failed check to its step and read it by
hand, with the run's shape in view. That means knowing what was done,
what is outstanding, what the failure cost, whether the agent recovered,
and whether the failure caused trouble downstream. Each of those is a
query, not a model's judgement. Under hypothesis 3 a truly blocked run
doesn't need this view. It matters for a failure in the middle of a run
that otherwise went on. It needs a check to say where it failed: a cited
id (proctor `todo.md`, "Ids in the judge's material"), or for a check on
final state, the step that last wrote what it checks.

## How to test the hypotheses

- **1 and 4:** on the copied datasets, look for passes whose calls to the
  surface stop before the work is done. That needs a surface declared for
  at least one suite.
- **3:** in runs that failed for good, measure what share of steps come
  after the last forward progress.
- The copied data is small (median 16 calls, maximum 93). The
  instrumentation runs that prompted hypothesis 2 are the better test, if
  they can be had.
