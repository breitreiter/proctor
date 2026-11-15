---
type: plan
title: From pass rates to a lab: what to build, in order
created: 2026-11-09
status: proposed; nothing built. The worked example is `lab-report-worked-example.md`; the threads it draws on are the 2026-11-09 sections of `project/todo.md`
---

# From pass rates to a lab

## What changes and what does not

The purpose stands: decisions about an agent-facing surface should rest on
runs, not on the author's sense of it. Harbor, the static checks, the decider
and the judge, nb as a configurable agent, and the report's visual language
all stay.

What changes is the instrument the report leads with. Pass rate saturates on
frontier agents, so a report that leads with it says "no difference" for
every revision past the first. The report instead compares two bundles on the
same agents and tasks, and leads with four rows: outcome, use of the surface,
effort, and right the first time.

## The test of the report

A person who was not there reads it and can take any of four next steps
without asking the author anything:

1. Hand it to their agent and get a useful investigation.
2. Read the transcripts themselves and know which, and where to start.
3. Approve the change.
4. Refuse the change, and defend that to someone who would rather ship.

Step 4 sets the bar. Whatever the report says in support of a refusal must be
a count that anyone can reproduce with no model involved, shown with the runs
it came from, next to a plain statement of what the report cannot tell.

## Step 0: agree the worked example (no code)

`lab-report-worked-example.md` is the leading artifact. Nothing below starts
until it has been read against the four next steps and corrected. The
decisions it forces, each with the answer the example currently takes:

| # | Decision | The example's answer |
|---|---|---|
| 1 | Is there one verdict, or a row per measure? | A row per measure, each with its own direction and confidence. No overall word. |
| 2 | Does the report ever say "ship" or "hold"? | No. It says which statements support each. |
| 3 | What is the word for an arm without its bundle? | "agent". An arm stays harness, provider, model and bundle; the page lays arms out as agent by bundle. |
| 4 | What is the unit of a paired comparison? | An agent on a task. Agents that share a task are not independent; the method section must say so. |
| 5 | Is the planted detail a `pass` check or a guardrail? | A guardrail, promoted to the Result table. A run that used the old way still passed. |
| 6 | Are novice and expert agents pooled? | Never. Expert rows lead; the novice is reported apart. |
| 7 | Is the no-surface arm required? | Required for a suite that declares a surface; the report says so when it is missing. |
| 8 | May the report search the bundle for declared strings? | Yes, as literal search. Proctor still reads nothing in a bundle for meaning. This relaxes "proctor never reads what is inside it". |
| 9 | Where does phase come from? | From what a call touched (docs host, repository, the service), by rule. Unproven; see step 2. |

Done when: each row is confirmed or changed, and the example is edited to
match.

## Step 1: fix what the cold read found

Independent of everything else, and cheap. Reproduce each, then file it.

- The nb image's `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` floods `dotnet`
  output with `NETSDK1188` on the nb arm only.
- `ran-tests` cannot see a command past the window's per-call cut. Replace
  it with a pattern over the full command.
- `change-fits` rejects every quote on `rename-module`.
- Whether three samples on the `qwen` arm are one.

Done when: each is an issue or struck from `project/todo.md`, and effort on
the nb arm is comparable with the vendor arms.

## Step 2: prove the event table on runs already paid for (throwaway)

A script outside the build, over the 379 trajectories under `runs/`. One line
per tool call: run, step, kind, what it touched (host and path, file, or
command), size in and out, whether it errored. Then compute, for
`code-change`, the tables in the worked example that need no declared
surface: effort by pair, calls by phase, error events.

Then the same script where the telemetry project's transcripts live, since
no local suite has a documentation site.

Stop here if: phase cannot be assigned by rule for more than a small share
of calls, or the vendor agents' trajectories do not expose the URL or path a
call touched. Either one sends decision 9 back to step 0.

Done when: the tables exist for one local experiment and one field
experiment, and a person has compared ten rows with the transcripts.

## Step 3: the suite declares its surface

- `suite.json` gains a `surface` block: hosts, paths, commands, and the
  strings a check or the report may look for.
- Two built-in `validity` checks: the surface answered from inside the
  container, and the run touched it at least once.
- The no-surface arm: an arm with an empty bundle, recognised as the control.
- The planted-detail pattern, written up for `skills/proctor-suite` and
  `docs/configuration.md`, with `oracle-change` as its precedent.

Done when: `readme-answers` has a no-README arm and one planted detail, runs
on the free tier, and its no-README arm scores zero on that detail.

## Step 4: the middle layer

- `Ingest` writes `events.jsonl` beside `core.json` from the same walk; one
  concatenation per experiment.
- `proctor steps <cell> <range>` prints steps. No query language.
- `summary.md` and `stats.json` carry the cell path of every run they name.
- `proctor grade --try <check spec>` prints verdicts for a proposed check
  over existing cells and writes nothing.

Done when: the step 2 script is deleted, and a fresh session given only a
report and the experiment directory finds the step 1 defects again.

## Step 5: paired statistics on effort and on changed runs

Method first, in `project/learnings/prior-art/stats.md`: the paired
difference on tokens, calls and duration, the exact test on changed runs,
and what decision 4 does to `n`. Then `Report/Stats.cs`.

Done when: `stats.json` holds every number in the worked example's Result
table for a real experiment, and the renderers compute none of them.

## Step 6: the report

Rebuild `Report.Blocks` to the worked example: what changed, Result as rows,
the control, where the effort went, events, pages, runs to read first, what
you can do, what it cannot tell. Existing sections follow unchanged.
`project/plans/report-structure.md` gains a review section pointing here.

Done when: the snapshot of a real two-bundle experiment reads like the
worked example, and someone who has not seen this plan can name their next
step from it.

## Step 7: one published experiment that shows it

A suite whose arms differ only in the bundle, on a vendor agent, with a
planted detail and a no-surface arm. This replaces the `code-change` showcase
on the site, which is a comparison of models. Hosted spend, agreed first.

## Step 8: what the corpus says about itself

Until this pass, every document still says pass rate is the headline.

- `docs/index.html`, `README.md`: the central act is two bundles on the same
  agents; "propensity" as a claim the planted detail now backs.
- `project/brief.md`: the paragraph on what a report states.
- `CLAUDE.md`: the verdict card convention, the bundle convention
  (decision 8), the vocabulary row for arm.
- `skills/proctor-suite`: the no-surface arm and the planted detail as the
  first two things a suite author does.
- An investigation skill beside it, for the session that reads a report.

Done when: a fresh session given the repository describes proctor as
comparing bundles, and does not describe it as ranking models.

## Not in this plan

Observations beyond rule-declared events, the blinded specimen sheet, the
forked interview, a cheap model as a validated proxy, hosted sandboxes. Each
stays in `project/todo.md` until steps 0 to 6 show whether it is still
wanted.

## Order and cost

Steps 0, 1 and 2 cost nothing and can each change the plan; do them first
and in that order. Steps 3 to 6 are the build. Step 7 is the only one that
spends on a hosted model.
