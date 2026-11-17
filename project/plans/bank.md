---
type: plan
title: The bank — transcripts kept per job and setup, with no batch
created: 2026-11-16
status: direction agreed with the user 2026-11-16 (B1 to B9); nothing built. Changes `exam-variant-attempt.md` where that plan says batch. Written as files and commands so a wrong assumption is a wrong line; the lines marked OPEN are mine and not agreed
---

# The bank

## Why

A batch was "the attempts one `proctor run` happened to make". Its
content depended on what already existed, the flags, and what had
failed, so it was never a thing a person could ask a question about.
It was the top-level folder, so it looked like the main thing, and a
page was nearly designed around it.

What a person has is a **bank**: for one exam and one setup, the
transcripts made while both were what they are now. A transcript made
last week is as good as one made today. The date is a log line.

## What was agreed

- **B1. No batch.** Not on screen, not in an address, not in a path.
  What a run did is kept as a log.
- **B2. The bank is keyed by what the agent met.** The exam's job (its
  stages, programs, hooks and fixture) by hash, and the setup's hash. A transcript is in the bank while both match the exam
  as it stands. Changing either empties that bank; this is forced, not
  a choice.
- **B3. Reps are the number that matters.** How many transcripts the
  bank holds, and for each check, how many of them it has graded.
- **B4. A new check has zero graded.** It catches up by regrading the
  bank, with no new run. A check that cannot be regraded from what was
  kept is a documented gap, fixed as a fast follow ("Back-fill").
- **B5. An exam is one job, run top to bottom.** The job may have many
  small disconnected stages. One container, its content set up once; the
  agent (nb, Claude Code) is launched once per stage, which is what
  clears its context. Stages are not tracked or run separately, and have
  no fixture of their own. A stage is a bookmark in the rep's transcript.
  The user expects this to be opaque to a newcomer and powerful once
  understood: lean into it, and explain it with care.
- **B7. Several jobs are several exams.** A view that rolls a set of
  exams up may come later. The user's word for it is on the old-words
  gate with its former meaning, so the roll-up needs the gate changed
  or another name.
- **B6. A freeze, later.** A way to declare an exam and setup good enough
  and be stopped from changing them by accident, because a careless
  edit can turn hundreds of dollars of transcripts into nothing. Early
  on a person breaks the bank constantly and that is fine; this is for
  afterwards. A todo, not designed here.

## On disk

Before:

```
attempts/20261116-1208-exam-authoring-k20g/docs-and-examples-haiku/guard-the-tests/6/
```

After:

```
bank/readme-answers/as-shipped/6/
  manifest.json      job hash, setup hash, made 2026-11-16T12:08Z, log entry k20g
  stages/1-install/transcript.jsonl      one per launch of the agent
  stages/2-tags/transcript.jsonl
  diff.patch
  checks.json
  verdicts/
log/20261116-1208-k20g.json    the command, host, versions, which reps it made
exams-seen/sha256-8e0cf0ae.json   the exam as it stood, kept once per hash
```

- The address is `readme-answers / as-shipped / 6`, then a stage and a
  step inside it. Rep numbers already run on across runs, so nothing else is needed.
- A rep whose hashes no longer match is not in the bank. OPEN: it stays
  in place and is skipped, or moves to `bank/.../old/`. I lean to
  staying in place: moving files on an edit is a surprise.
- The copy of the exam that a batch held moves to one file per exam
  hash; each manifest names the hash.
- Resuming after a crash reads the log entry's list of reps it meant to
  make.

## What a person sees

```
$ proctor run readme-answers
readme-answers                 one job, 6 stages
  as-shipped        5 of 5 reps
  quickstart-first  2 of 5 reps   making 3
```

```
$ proctor grade readme-answers
  correct          graded 7 of 7
  new-check        graded 0 of 7   grading 7
```

After a check that needs the workspace is changed, on an agent setup:

```
  acceptance       graded 0 of 10   cannot be regraded from what was kept (see Back-fill)
```

## Counting

- A rep is the whole job once. Reps are counted per setup.
- A check with a right answer per stage gives a verdict per stage inside
  each rep: "tags: right in 4 of 5 reps". A check on the whole job
  gives one verdict per rep.
- Nothing adds stages together into a pass rate over tries. Across
  stages the count is of stages: "right every time on 4 of 6 stages".
- A setup with one rep says it cannot tell a hard stage from bad luck.

## Stages

`readme-answers` today is six tasks, one question each, each with its
own attempts. It becomes one job with six stages:

```
exams/readme-answers/exam.json
  fixture: "jot"
  stages: [ {id: "install", prompt: ...}, {id: "tags", prompt: ...}, ... ]
```

- One rep is all six stages in order, in one container.
- The workspace is set up once and is not reset between stages. A stage
  that writes files changes what the next stage meets. That is the
  model, and an exam's author orders stages knowing it.
- The agent is launched once per stage. For nb that is one program run
  per stage; for another agent, one start per stage in the same
  container. Neither is built.
- A rep where a stage did not run (a provider error at stage 9 of 14)
  is lost. Agreed: that is understandable, and the cost of a long job.
- Existing exams with several tasks are not carried over. Agreed: fine
  to lose them. Each is rewritten as one exam with stages, or as
  several exams, when it is next wanted.

## Checks and stages (B8)

- **No check runs between stages.** Nothing stops the job part way to
  look at the workspace.
- **A check on the workspace runs once, at the end,** and sees what the
  last stage left. Stage 7 can undo what stage 6 made, and a check for
  stage 6 then fails though stage 6 did its work.
- **So a stage whose result is checked must leave it where a later
  stage will not touch it.** The exam's author arranges that in the
  stage's prompt: "write your result to ./output_6".
- **A check on the transcript is per stage for free.** What the agent
  said, which tools it called and what came back are kept per stage,
  so trajectory and tool-use checks need nothing special.

## Guidance for the author (B9)

The author of an exam is usually a coding agent, so this model has to
be written down where that agent reads it (the exam-writing skill and
`docs/configuration.md`), with the trade-off stated:

- **One exam with many stages** when the stages are small, share one
  workspace, and are worth reading as one result. Cheap to set up; a
  failure late loses the rep; stages can disturb each other.
- **Several exams** when each job needs its own workspace, takes long
  enough that losing it hurts, or would be changed on its own schedule.
  Changing one stage empties the whole exam's bank; changing one exam
  leaves the others alone.
- The author must say where each checked stage leaves its result.

Not written yet. It is part of step 4.

## Back-fill (B4's gap)

A check that reads the transcript or the diff regrades free. A script
check that needs the workspace as the agent left it cannot be rerun on
a banked rep from an agent setup; today it is marked outdated. The fix
is to rebuild the workspace from the fixture's image and the kept diff
and run the script there, recording that the verdict came from a later
build of the image. Until then `grade` says so, as above, and the only
way to grade such a check is new reps.

## The words

Task goes, since an exam is one job; its place is taken by stages.
Proctor's files and commands may keep exam, variant, attempt for
now; the screen says exam, setup, try (auspex
`project/inventory.md`). OPEN: whether to rename in proctor too. The
last rename was costly and the reader of proctor's files is a coding
agent, so I lean to leaving them, except batch, which goes.

## Order of work

1. The bank layout and the log, with `run`, `grade`, `report`, `list`
   and `friction` reading it. Existing attempts move by script.
2. `run` and `grade` print reps and graded-of as above.
3. Back-fill.
4. Stages.
5. The freeze.
