---
date: 2026-11-16
status: superseded 2026-11-22 (the user), never revised. Written before proctor's bank, so it speaks of batches. Its first pass is covered by `stage-page.md`, `exam-page.md` and `build-plan.md`. Still live, for build step 6: the marks proposal (one address, four writers) and three of its open questions (whether the diff and answer are enough, where dispositions live, where "my goal was wrong" is recorded). Was: step 2 of the design process, every task and every thing shown, grouped by what belongs in one continuous view
---

# Inventory: the moving parts, and what clusters

## What the journeys settled

- **One person, one machine.** Sharing is a report or a link.
- **Journey 1 (will it run) is the coding session's**, on proctor's
  agent-readable output. auspex has no part beyond showing an attempt.
- **Journey 2 is the centre, and it is wider than friction.** The person
  is checking three things on a green batch: did the agent really do the
  task, is the output as good as they meant, did it cheat. They read the
  conversation and the final state, and often find their own goal was
  badly put. Friction points are one way in, not the whole job.
- **No comparing a trajectory across several variants.** auspex explains
  how one variant's attempts went. My reading: the live variant against
  one change is still in scope, as counts of friction that appeared or
  went, not as two trajectories side by side.
- **The coding session acts; auspex shows.** Anything a person would do
  in auspex must also be doable by their session, on files.
- **The record is long-lived.** Months later the person needs "this
  changed then, it has been like that since, and this fixes it".

## Things shown

Each row is something a person has to be able to see. "Have" means
proctor writes it today on the new layout.

| # | Thing | Source | Have |
|---|---|---|---|
| S1 | Exams that have attempts, and their batches | `attempts/<batch>/batch.json` | yes |
| S2 | A variant's attempts by task, with outcome and whether it counts | `manifest.json`, `checks.json` | yes |
| S3 | Which attempts are current and which stale | hashes in the manifest against the exam now | yes, but only proctor computes it |
| S4 | Which variant is live | proctor's live mark | yes |
| S5 | What recurs across a variant's attempts: friction by label and by part of the surface | `friction.json` per attempt | partly, see C1 |
| S6 | One attempt's steps: what the agent said, called and got back | `trajectory.json` or `transcript.jsonl` | yes, two formats |
| S7 | One attempt's friction points, each on its step with the quote marked | `friction.json` | partly, see C1 |
| S8 | One attempt's checks, each with its reason and, for a judge, its quote | `checks.json`, `verdicts/` | yes |
| S9 | The task as the agent met it: prompt, fixture, bundle | exam files, manifest | yes |
| S10 | What the agent left behind: the diff, and the final answer | `diff.patch`, the transcript's last turn | diff yes; files outside the diff no, see C3 |
| S11 | What the agent was given that it should not have reached, or avoided that it should have used | the exam's `surface`, validity checks | only where the exam declares a surface |
| S12 | The person's disposition on a point, and the check written for it | auspex's own file | not built |
| S13 | Friction that is new or gone between the live variant and a change | two variants' `friction.json` | blocked, see C2 |
| S14 | The history of an exam: what was live when, which batches, what changed | batches, live mark | partly; the live mark keeps no history that I know of |

## Tasks

| # | Task | Who does it | Journey |
|---|---|---|---|
| T1 | Find the batch I just ran | person | all |
| T2 | See at a glance whether a variant's attempts are worth opening | person | 2, 3 |
| T3 | Go from a recurring pattern to the attempts that show it | person | 2 |
| T4 | Read one attempt far enough to judge it: did it do the task, how well, did it cheat | person | 2 |
| T5 | Check a label or a judge's reason against the step it cites | person | 2, 3 |
| T6 | Compare a failed attempt with a passing sibling on the same task | person | 3 |
| T7 | Tell a budget cap, a hard check and a mis-tuned judge apart | person | 3 |
| T8 | Decide a point matters, or leave it | person | 2 |
| T9 | Hand a point, a step or an attempt to the coding session | person, then session | 2, 4 |
| T10 | Write the check, try it, record it against the point | session | 4 |
| T11 | Notice my goal was wrong and say what I meant | person, then session edits the exam | 2 |
| T12 | See what a change did besides what was claimed | person | 5 |
| T13 | Produce receipts for a PR | session, from proctor's report | 6 |
| T14 | Run more attempts when someone asks for them | session, `proctor run` | 6 |
| T15 | Find out when something changed and what it looked like before | person | 7 |

## Constraints

- **C1. Friction is only coded from `trajectory.json`.** An nb attempt on
  the bare runner keeps `transcript.jsonl` and has no friction file.
  Either proctor's friction reads the transcript too, or such attempts
  show as "not coded". The reader stage is a paid call per attempt and
  is opt-in, so a batch may have rules-only points.
- **C2. No rule says two friction points are the same.** S13, and
  dispositions that survive a new batch (S12 over time), both wait on
  it. Until then a disposition belongs to one attempt.
- **C3. The final state is a diff against the fixture.** Files the agent
  wrote outside the checkout, a database, a running service's state are
  not kept. For "is the output good" the diff and the answer are what
  there is.
- **C4. auspex makes no model call and holds no key.** Anything that
  summarises or labels is proctor's, written to a file first.
- **C5. Whether an attempt is stale, counts, or passes is proctor's
  logic.** auspex should read a file proctor writes for this, not
  compute it again. Proctor has no such file today; `report` computes
  it in memory.
- **C6. Two transcript formats** (nb JSONL, ATIF). Proctor already turns
  both into the windows its checks read; auspex should reuse that shape
  rather than parse twice.
- **C7. The session must be able to point at what the person sees.**
  Every attempt, step and point needs an address that is both a URL in
  auspex and a path on disk.
- **C8. `grade --try` is not built**, so T10's "try it" has no verb.
- **C9. Claims are not designed**, so T12 has nothing to be "besides".
- **C10. Attempts are gitignored and large.** A six-month record (S14,
  T15) only exists if they are kept somewhere on purpose.
- **C11. The viewer reads the old layout today.** Everything above
  assumes it is moved first.

## Marks: one way to point, four writers

From the user, 2026-11-16: because the coding session is the interface,
"look here" is written by four parties and read by all of them: proctor's
checks, proctor's friction detector, the session (something it found and
wants the person's read on), and the person (telling the session exactly
what to look at). File and line number is the floor. A proposal for
something better, not agreed:

- **An address.** `<batch>/<variant>/<task>/<attempt>#<step>`, with an
  optional range (`#12-15`) and nothing after the `#` for the whole
  attempt. It is the path on disk plus the step's own id, so it is the
  same string in a URL, in a file and in a sentence to the session. Not
  a line number: the two transcript formats (C6) number lines
  differently, and a step id survives both.
- **A mark.** An address, who wrote it (`check`, `friction`, `session`,
  `person`), a short note, and an optional quote. A quote is checked
  against the step by the judge's rule before the mark is shown as
  verified; a mark whose quote does not match is shown as such, not
  dropped.
- **Where they live.** Friction points and check verdicts stay where
  proctor writes them; auspex reads them as marks. The session's and
  the person's go in one plain file per attempt that either may append
  to. The disposition (S12) is then a mark's reply, not a separate
  thing.
- **Both directions.** The person marks a step in auspex and gets the
  address to paste. The session writes a mark and gives the person the
  URL. A small proctor verb that prints an address's step would let the
  session read what the person pointed at without parsing either
  format.

What it needs that is missing:

- A check says where it fired. A judge's quote can be located; a script
  check on final state cannot, and falls back to the whole attempt
  (proctor `todo.md`, "Ids in the judge's material").
- Step ids for an nb transcript, which has events, not ATIF steps.
- A rule for a mark on an attempt that has since gone stale or been
  remade: my guess is it stays with the attempt it was made on.

## Clusters

Grouped by what a person does in one sitting without changing what they
are thinking about.

**A. Where am I.** T1. S1, S3, S4. Short, a way in, not a place to stay.

**B. How did this variant's attempts go.** T2, T3, T7. S2, S5, S11.
The entry to journey 2 and 3. One variant, all its tasks and attempts,
and what recurs. This is the "explain how all ten went" view the user
named as the complexity already being paid for.

**C. One attempt, read closely.** T4, T5, T8, T9, T11. S6, S7, S8, S9,
S10, S12. The largest cluster and where the time goes. The steps, the
points and checks laid against them, the task as given and what was
left behind. These belong together because every judgement in T4 needs
at least two of them at once.

**D. Two attempts on one task.** T6. Two of cluster C with the same
task. Stands apart from C because it is a different question (what
differs) from "what happened". Could wait: a person can open two
attempts.

**E. What the change did.** T12. S13. Blocked on C2 and C9. Stands on
its own when it exists.

**F. The record.** T15. S14. What was live when, batch by batch.
Stands on its own; nobody is reading steps here.

**Not auspex.** T10, T13, T14 are the session's and proctor's. auspex's
only part is T9: making the handover exact.

## The first pass, as the user set it (2026-11-16)

Comparison is deferred, across attempts and across variants, knowing it
is a key part of the value and comes back later. That removes D and E,
and T6 and T12. The core flow to get right first:

1. The session says the batch is done, gives its own read of how it
   went, and gives a URL.
2. The URL opens on how the batch went: each attempt with its shape and
   health, and a way to move between them.
3. An attempt opens as a transcript, with folding to disclose detail
   selectively, navigation that characterises the attempt at a high
   level, and search. Its main use is "I do not believe what you said
   about this attempt".

## What I would cut from a first pass

D, E and F. A, B and C serve journey 2 end to end, and C7 (addresses)
makes the session able to do the rest. E and F are blocked or need
proctor work first; D is two tabs.

## Open

1. **S10: is the diff and the final answer enough** to judge output
   quality, or do the exams you care about leave their result somewhere
   a diff does not show?
2. **Where dispositions live.** Beside the attempt (proctor's tree,
   gitignored) or with the exam (checked in, survives). C10 and journey
   7 argue for with the exam.
3. **C5: should proctor write a per-batch summary file** for auspex to
   read, so the two never disagree on what counts?
4. **T11 has no home.** When the person sees the goal was wrong, the
   fix is an exam edit by the session. Is there anything to record in
   auspex, or is the edit the record?
