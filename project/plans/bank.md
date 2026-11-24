---
type: plan
title: The bank — transcripts kept per job and setup, with no batch
created: 2026-11-16
status: direction agreed with the user 2026-11-16 (B1 to B9); steps 1 and 2 built 2026-11-17 and 18 (see "Step 1 as built", "Step 2 as built"); step 4 under way since 2026-11-23 ("Step 4, in slices"), steps 3 and 5 not. Changes `exam-variant-attempt.md` where that plan says batch. Written as files and commands so a wrong assumption is a wrong line; the lines marked OPEN are mine and not agreed
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
- Harbor already does the agent side (read in the source of 0.24.0 on
  2026-11-17, `harbor/trial/multi_step.py`; not run). A task declares
  `[[steps]]`, each with `steps/<name>/instruction.md`; they run in order
  in one environment; the agent starts a fresh conversation per step
  unless `agent.resume_trajectory` is set; agent logs, verifier output
  and artifacts are kept under `steps/<name>/`; a step that throws ends
  the trial. So an agent setup's stages compile to Harbor steps and
  proctor launches nothing itself. Before step 4, run one multi-step
  task with a scripted agent to see that the per-step output is what
  ingest expects.
- Harbor also offers three things this plan does not use: a verifier
  after every step (see B8 below), a step's own files and `setup.sh`,
  and `min_reward` to stop early.
- A rep where a stage did not run (a provider error at stage 9 of 14)
  is lost. Agreed: that is understandable, and the cost of a long job.
- Existing exams with several tasks are not carried over. Agreed: fine
  to lose them. Each is rewritten as one exam with stages, or as
  several exams, when it is next wanted.
- **A stage is never a unit of comparison** (the user, 2026-11-22).
  Stages are chunks for legibility and share a workspace. A variant is
  compared with live try against try, on headline checks held per try
  and flagged steps per try, with Mann–Whitney; `Stats.cs`'s pairing by
  task goes with tasks. See `check-evidence.md`, C5.4.

## How a stage ends (2026-11-23)

Three ways, taken from nb's own split between a run that stopped and a
run that failed (`docs/conversation-program-cli.md`, exit codes):

| A stage | nb's exit reasons | What it is | The try |
|---|---|---|---|
| **ended** | `ok`, `oracle_miss` | the agent finished its turn | goes on |
| **stopped** | `max_tool_calls`, `tool_error_limit`, `token_budget`, `time_budget`, `oracle_budget`, `approval_denied` | a result about the agent: it was cut off by a limit or a policy the exam set | graded; what happens next is the exam's `chained` |
| **did not run** | `provider_error`, `rate_limited`, `cancelled`, a startup error, no trailer, the container or proctor itself failing | nothing about the agent | lost |

- **A lost try** stops there. It is kept on disk with the stage named,
  marked failed, not graded and not counted; `run` makes another whole
  try. There is no resuming, because the workspace is half way through.
- **A stage that ran and failed its checks is a result**, never a lost
  try.
- A vendor agent's ending maps onto the same three through ingest.
  Harbor ends a trial when a step throws; whether its agent timeout
  counts as stopped or did-not-run is for the Harbor check to show.

### Chained or independent (the user, 2026-11-23)

An exam says which its stages are. `"chained": true` in `exam.json`, absent meaning independent.

- **Independent** (default): the stages share a workspace but none needs
  another's output. A stopped stage is that stage's result and the next
  stage runs. The caveat stands: a stage that wrecks the workspace
  wrecks the rest, and nothing detects that.
- **Chained**: a later stage consumes what an earlier one made. After a
  stage that failed, the stages downstream have missing or broken input,
  so their results say nothing about the agent.
  - At run time the only thing known is how the stage ended (B8: no
    check runs between stages). So a stopped stage ends the try there
    and the later stages are not launched.
  - At grading, the stages after the first stage that failed a headline
    check are **not reached**: shown as such, never as failures of
    their own.

**A broken chained try counts** (the user, 2026-11-23). Proctor does not
call it invalid. The break is a known fault that cascades, so spending
more tokens and time on the try is pointless and the junk downstream is
not generated. The try is compared like any other, on the headline
checks it held before the break; stages not reached hold nothing. A
setup that breaks early scores low and the page says where it broke.
Excluding the try would flatter that setup, since it would be compared
only on the tries where it did not break.

OPEN: a chained stage that ends `ok` with the wrong work is only caught
at grading, after the later stages ran. The bare runner could grade a
stage's built-in transcript checks as soon as it ends and stop on a
failed headline check, at no cost. That bends B8 (no check between
stages) for transcript checks only, and Harbor would need its per-step
verifier to do the same. Not built.

## Tasks go; stages gain two things (the user, 2026-11-23)

Looking at the exams themselves, only `readme-answers` staged as
stages stood: the others have a workspace check per task, or need a
clean workspace per task. The user ruled out keeping tasks: each has
its own configuration and transcripts, which makes it an exam within an
exam, "confusing and hard to explain". Stages stay the one way to
bundle small work items, with two changes.

- **A workspace check is its stage's, and sees the workspace as that
  stage left it.** This replaces B8's "runs once, at the end" and the
  rule of slice 6 (workspace checks on the last stage only). Harbor's
  verifier already runs after every step with the diff so far. The bare
  runner launches nb once per stage, so it can keep the diff after
  each. B8's objection, that nb had no equivalent, no longer holds.
- **The workspace is fresh for each stage, unless the exam is
  chained.** `"chained": true` stays the one setting; absent, each
  stage starts from the workspace as the try began, so stages cannot
  disturb each other. A third mode, one workspace shared by stages that
  do not depend on each other, was dropped: it is fresh done by hand,
  with room for one stage to break the next unseen.
  - Fresh restores the tracked files and removes new untracked ones.
    Ignored files (restored packages, build caches) are left, or an
    offline build would fail. A stale build output in an ignored
    directory can survive.
  - Services are not reset. A fake API that keeps state between stages
    is the author's to handle.

What is still several exams: tasks on different environments
(`code-change`'s three stacks, `nb-changes`, the two authoring exams),
and the scripted controls for failure paths. See `project/todo.md`:
something above an exam for `run` to target, and retrying a stage in
place.

## Step 4, in slices (2026-11-23)

Bare runner first; Harbor after, with its scripted multi-step check
moved to just before it (the user: this connection is too slow to pull
images now). The risk taken: the per-stage layout is built on what
Harbor's source says it writes per step, not on a run.

1. **Definition and the job hash.** BUILT 2026-11-23: `stages` and
   `fixture` in `exam.json`, `about` per stage, one hash for the job,
   `list` naming stage ids that will not read as a label. `run` refuses
   a staged exam until slice 2. An exam is tasks or stages, and stays so
   until slice 7 deletes tasks.
2. **Bare runner and the bank layout.** BUILT 2026-11-23, tested on a
   staged copy of `smoke` against the Mock: one workspace, nb launched
   once per stage, `bank/<exam>/<variant>/<n>/stages/<i>-<id>/` with a
   manifest, status, program and transcript per stage, the attempt hooks
   once around the try and one `diff.patch` in the try's directory, the
   three endings above, `chained`. A stage not launched is `skipped`
   with why. A lost try is left out of every reading and never taken up
   again; `run` makes a new one. `run --tasks` and agent variants are
   refused on a staged exam, and `grade`, `friction` and `report` refuse
   one until their slices.
3. **Grading per stage.** BUILT 2026-11-23. Each stage has its own
   `checks.json`, graded on its own transcript with its own expected
   values, so the guardrails and evidence are per stage by where the
   file is. A check that reads the workspace (`files_touched`, a
   script) is graded once, against the try's diff and checkout, on the
   last stage (a stage's own stayed with its stage at first; slice 6
   made it the last stage's only). A stage passes on the headline checks
   it carries. A stage not reached is not graded. auspex reads the
   layout: `/<exam>/<setup>/<try>/<stage>`, a strip of the try's
   stages, proctor's reason on a stage that did not complete.
   `report` still refuses a staged exam.
4. **Friction per stage.** BUILT 2026-11-23. `proctor friction` writes
   a `friction.json` beside each stage's trajectory, its points found in
   that stage's own steps; its `attempt` block carries `stage`, the
   stage's place, as the manifest does. A stage not reached is skipped
   and said so. A reader's kept reading is per stage.
5. **Statistics and the report by try.** BUILT 2026-11-23, as its own
   path (`Report/Tries.cs`); the task report (`Stats.cs`, `Report.cs`)
   is untouched and goes with tasks in slice 7. On a staged exam
   `proctor report` writes `stats.json` (schema `tries/1`: per variant
   its tries, each with headline checks held of all the job has, flagged
   steps, each stage's result, and the variant against live),
   `friction.json` (C5.4's roll-up: per kind, in k of n tries, min,
   median and max when it happens, per try overall), `results.jsonl` a
   row per stage, and `summary.md`. Against live is Mann–Whitney with an
   exact two-sided p, on both measures, with proctor's sentence and what
   moved most. A lost try is in no number; a try with a stage that
   failed a validity check is listed and not counted (my reading: the
   author said such an attempt says nothing). `run` notes when the tries
   wanted cannot confirm a difference. Not built: each step's tool kind
   for the exam page's strip.
6. **Harbor.** BUILT 2026-11-23. Checked first on real runs of Harbor
   0.24.0 with its scripted agent (it was in uv's cache, so no
   download): each step's agent logs, verifier output and artifacts are
   under `steps/<name>/`, the workspace carries over, the verifier runs
   after every step, the diff collected after each step is cumulative,
   the agent timeout is each step's, and a step that timed out is
   recorded on that step while the trial goes on, provided the step has
   a verifier result. A step that throws with no verifier result ends
   the trial.
   - A staged exam compiles to one Harbor task per agent variant, each
     stage a `[[steps]]` entry with its `instruction.md`. Every step has
     a verifier: the last stage's runs the script checks, the others
     only say they ran.
   - One trial is one try. Each step is ingested as its stage, through
     the same ingest as a task's trial.
   - nb's Harbor agent takes a directory of programs, one per stage, and
     runs the nth on the nth step.
   - Harbor cannot stop a try part way on a stopped stage (`min_reward`
     reads the verifier, which does not see how the agent ended). So on
     a chained exam the later stages run in the container and proctor
     does not read them: they are `skipped`, not reached, as on the bare
     path. The two runners grade a try alike; Harbor spends more.
   - **Changed by this slice: a workspace check is the exam's, the
     fixture's or the last stage's.** One declared on an earlier stage is
     refused when the exam loads. Harbor's verifier and the bare path's
     checkout both look at the workspace once, at the end, and a check
     shown on stage 2 but graded on what stage 6 left reads wrongly on
     either. This is the decision as first recorded (2026-11-22).
   - Refused: an answer sheet on a staged exam with an agent variant.
     Harbor's simulated user does not run a task of several steps.
   - Run for real on 2026-11-23: nb as a Harbor agent, three stages in
     one Docker container against the Mock, then `grade`, `friction`,
     `report` and auspex over it. Not run for real: a vendor agent
     (Claude Code, Codex, Qwen Code), which needs their images and a
     model; and Modal.
7. **The exams.** Seven stage as they are (`readme-answers`,
   `tern-docs`, `gate`, `gate-oracle`, `smoke`, `playground`,
   `conformance`): one fixture each. Four cannot, because each task has
   its own fixture (`agent-onboarding`, `code-change`, `exam-authoring`,
   `nb-changes`): each becomes several exams (B7), and loses its one
   comparison across fixtures until a roll-up of exams exists. Then
   tasks, `--tasks` and the per-task code are deleted.
8. **Docs and the skill** (B9).

**Slices 7 and 8, revised 2026-11-23 (the user).** No exam of tasks
exists outside this repository; what is in the wild is suites from
proctor 0.3 and earlier. So the exams here are not all converted now,
and the only port documented is from a suite
(`docs/porting-from-suites.md`).

- BUILT: `readme-answers` and `tern-docs` are staged, with their
  shakedowns (free, both pass). `readme-answers` asks for 4 tries.
  `tern-docs` keeps 2, which cannot confirm a difference; 4 doubles a
  paid run, the user's call. Neither has been run on a real model as
  stages, so a vendor agent on a staged exam is still untested.
- BUILT (slice 8): the skill writes stages; `configuration.md` says to
  write a new exam as stages.
- Each other exam is converted when it is next to be run, since
  converting starts its bank over. The four with a fixture per task
  wait for "something above an exam" (`todo.md`). `gate-oracle` and
  `oracle-change` wait for a one-stage exam compiling as a plain Harbor
  task, so the oracle bridge works. `conformance`'s `fail` task is an
  exam of its own: as a stage it would lose every try.
- Tasks, `--tasks`, `Stats.cs` and `Report.cs` are deleted last, once
  no exam here uses them.

**After slice 6, the same day:** the two changes under "Tasks go;
stages gain two things" are BUILT on both runners and run for real
through Harbor. Every check is a stage's, a workspace check sees the
workspace as its stage left it (a `diff.patch` per stage; on a chained
exam `files_touched` reads what the stage itself changed, by taking the
stage before's diff out), and a stage starts fresh unless the exam is
chained (the bare runner resets the checkout; Harbor's step runs a
compiled `setup.sh`). Slice 3's and slice 6's rule that a workspace
check is the last stage's is gone, with the try-level `diff.patch`.
Grading a bare stage's script checks rebuilds its workspace from the
fixture and its diff, beside the try's checkout.

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
- **Every check belongs to a stage** (2026-11-22): a check on the
  workspace belongs to the last stage, and the guardrails are per
  stage. No check is on the job as a whole (`check-evidence.md`, C4).
- **Harbor could run a check after each stage** on an agent setup, which
  would let a check for stage 6 see the workspace before stage 7 touches
  it. Not used: nb has no equivalent, and the two kinds of setup must
  grade the same way. B8 stands until nb can do it too.

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
- **A stage's id is its label on screen, so keep it terse** (added
  2026-11-22, from auspex's stage page; the user: "no one is going to
  remember what stage 12 is"). auspex shows stage labels in a strip on
  every try page, so nobody has to remember stage numbers. The id names
  what the stage asks, in two or three words, kebab-case, about 20
  characters at most: `clear-wait`, `piped-yes`, `store-location`. It
  doesn't name the stage's place or kind (`stage-12`, `q3`, `test-b`).
  OPEN: an optional `about` per stage for the one-sentence version ("How
  long clear waits with no terminal"), shown in the stage's header and
  on hover; and whether loading an exam warns (never errors) on an id
  over 20 characters or one that is only a number.

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

## Step 1 as built

Tasks still exist until step 4, so the path has a task level and there is
one transcript per attempt, not one per stage:

```
bank/exam-authoring/docs-and-examples-haiku/guard-the-tests/6/
log/20261116-1208-exam-authoring-k20g/entry.json    also status.json, hooks/<variant>.setup.log, harbor/
exams-seen/sha256-8e0cf0aedb1e.json
```

Where it differs from "On disk" above:

- A log entry is a directory, not one file: an agent variant's Harbor
  tasks are compiled once per run and an attempt's script verdicts are
  checked against that copy, so it has to be kept somewhere, and the run
  is what made it.
- The entry's id keeps the exam's name, so `ls log/` reads.
- `--batch` is gone from `grade`, `friction` and `report`.
  `PROCTOR_BATCH` is gone; `PROCTOR_EXAM` is the exam's id and the
  container name is built from it.
- A rep that no longer matches stays in place and is skipped (the lean
  above, taken).
- `run` still refuses a changed variant under an id that made attempts,
  as before. B2 says changing a setup empties its bank; the refusal is
  stricter than that. Kept: the user said the refusal is fine, 2026-11-18.
- An attempt directory moved out of the bank is made again by a new run,
  not by continuing the run that first made it, when that run's compiled
  script checks are no longer current.
- Two `proctor run` of one exam at once would pick the same numbers.
  Nothing guards against it.
- `exams/exam-authoring` (its interrupt hook, two checks and one task)
  reads the layout of the proctor it shows agents, which is the earlier
  one. Left as it is; it waits on the todo about grading those exams
  with the proctor they were shown.

Existing attempts were moved with `project/migrate/attempts-to-bank.py`.

## Step 2 as built

Tasks still exist, so a variant's count is over every task, and proctor
says attempts where this plan says reps:

```
$ proctor run smoke
smoke: 3 tasks, 2 attempts wanted of each
  a  6 of 6 attempts
  b  2 of 6 attempts   making 4
run 20261118-0912-smoke-k20g: 4 attempts to make
```

```
$ proctor grade smoke
  ...one line per attempt, then the summary, then:
  completes   graded 8 of 8
  new-check   graded 8 of 8   8 new
  acceptance  graded 4 of 8   4 cannot be regraded from what was kept
```

Where it differs from "What a person sees" above: `grade` evaluates
every check on every attempt each time, so there is no "graded 0 of 7,
grading 7" to print beforehand. The line comes after grading and says
how many verdicts are new.

## Order of work

1. The bank layout and the log, with `run`, `grade`, `report`, `list`
   and `friction` reading it. Existing attempts move by script.
2. `run` and `grade` print reps and graded-of as above.
3. Back-fill.
4. Stages.
5. The freeze.
