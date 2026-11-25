---
source: https://sel.ist.osaka-u.ac.jp/lab-db/betuzuri/archive/1340/1340.pdf (Kawamoto, Higo, Kula, The University of Osaka; "A Time-Based Visualization for Agent-Based Codebase Interactions", IPSJ SIG Technical Report)
read: 2026-11-12, the whole paper, 8 pages; the prototype itself was not found or run
take: the same problem one level below beats; rule-tagged calls on parallel timelines, grouping left to the eye, no validation yet. Three measures worth taking, and their multi-tag choice supports attributes over more beat labels
---

# Agent ATO (Agentic Trajectory Observer)

## What it is

A static HTML page generated from one coding agent's event log (Pi, recorded
by a TypeScript extension as JSONL, rendered by a Python script). No server.

- **Unit: an "interaction"**, one model response and the tool calls it
  triggered (`turn_start` to `turn_end`). That is a step, not a beat: nothing
  in the tool groups steps into larger units.
- **Four tags by rule**, from a table of tool names and bash commands
  "determined through discussion among the authors after inspecting commands
  observed in several agent executions":
  - Discovery: "operations for finding relevant files or information"
    (`ls`, `find`, `rg`, `grep -l`, `tree`, `which`, `wc`).
  - Reading: "operations for inspecting file contents or diffs" (the `read`
    tool, `cat`, `git diff`, `grep` without `-l`, `sed -n`).
  - Writing: "operations that modify files or repository state" (`edit`,
    `write`, `rm`, `sed -i`, inline Python with `open(..., 'w')`).
  - Execution: "validation or execution operations, such as running tests,
    builds, linters, type checkers, or scripts".
- **A step may carry several tags.** `sed -i` is Reading and Writing by
  their table; a multi-tagged mark gets a blended colour.
- **Five rows on one time axis**: all steps, then one row per tag with the
  other steps greyed, "without losing the surrounding temporal context".
- **Token graph on the same axis**, each step split into model-output tokens
  and tool-result tokens.
- **Each step has a representative label** (`find | grep | head`,
  `npm test`); hover highlights the other steps with the same label. The
  user can select a run of consecutive steps and find the same pattern
  elsewhere in the run.
- A later version adds a sidebar of runs within one session, one per user
  prompt.

## What they found with it

Two GitHub issues (immich, metabase), ten runs each, same prompt and
environment. They did not score success.

- Among runs with a similar fix, one wrote then ran tests each time, one
  wrote several times and never ran anything. Visible as the Writing row
  with or without the Execution row beneath it.
- The first test run came several steps after the first write; later ones
  came immediately. Their reading: the agent first had to find the test
  command, then reused it.
- Two runs with high token use had different causes: one long stretch of
  model output reconsidering the fix, the other large tool results from
  reading whole files. Different remedies for each.
- On a UI bug the ten runs took three approaches, none the merged one;
  passing tests did not show the visible defect fixed. Their argument for
  reading the process beside the outcome.

## What it lacks

- No agreement figure for the tags and no user study (planned).
- One agent, two tasks. The rule table is npm- and Python-shaped.
- Their own limitations: a command's role depends on its arguments
  (`python3` runs or writes), and Execution is too coarse: "tests, builds,
  package installation, linting, type checking, and program execution may
  need to be distinguished".

## Worth taking

- **Tokens per step, split by model output and tool result, on the reading
  axis.** Separates "thought for a long time" from "read something huge".
  Proctor's counts cannot tell these apart today. Whether every agent's ATIF
  carries per-step tokens is unchecked.
- **Steps from the first write to the first try**, against the same gap for
  later writes. A friction measure: how long it took to learn how to
  validate.
- **Discovery apart from Reading.** Finding where to look and reading what
  is there are different costs with different remedies (a map of the repo
  against a smaller file).
- **Select a sequence, find it again.** A reading-tool feature, cheap once
  steps carry a representative label.
- **Greyed context instead of filtering out.** A filtered row that keeps the
  positions of everything else is still a timeline.
- **Tags that are not exclusive.** They chose several yes-or-no attributes
  per step over one label. The same move one level up is to give a beat
  separate attributes (what was written; what came before) in place of a
  longer list of beat labels.

## Not to take

- Grouping left to the reader's eye. Fine at 70 steps, the size of their
  figures; proctor's blind read of 78 beats exists because the cut has to be
  stated and checked, not seen.
- The rule table as it stands. Proctor's lab hit the same holes (writes
  through inline scripts, one "ran something" kind) and their list is
  shorter than ours.

## Where auspex stands against it

Same unit underneath, same rule-first stance, same faults admitted. Auspex's
difference is the beat: a stated cut above the step, with a label a blinded
reader agreed with on 79% of 78 beats (proctor's lab, 2026-11-11). This
paper is the closest published neighbour found so far and is worth citing as
such; it has no measure of whether its tags or its pictures are right.
