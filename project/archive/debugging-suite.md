---
type: plan
title: A debugging suite, to press on the beat cut
created: 2026-11-11
status: built as `suites/nb-changes`, five defect tasks and the feature task, each shaken down on the host three ways (`reference/shakedown.sh`). Harbor's oracle agent with each reference as the solution passes every script check in the real images. No model has run. Spend not agreed. The cut it tests is `project/todo.md`, "Beats, second cut"
---

# A debugging suite

## Why this suite

Every trace the beat cut has seen has one shape: read, create files, run
them. The cut says "everything before the first write is orienting", and no
run so far has made that false. Debugging does. This suite exists to produce
traces of the shapes the cut has not met, on an agent we care about.

It is also a real suite: "can an agent find and fix a defect in a codebase of
this size from a bug report" is a question the report can answer.

## What each task is for

One codebase, five defect tasks and one feature task (below). Each task has
its own fixture, holding only its own defect, so no task's breakage can be
seen, fixed or tripped over in another. Each is chosen for the shape of run
it should produce, not for difficulty.

| Task | What the agent is given | Shape it should produce | What it tests in the cut |
|---|---|---|---|
| `red-test` | "The build is red." One existing test fails. | Tries before any write, then a small fix. | A try inside orient: is orient still one beat, or is "reproduce" its own? |
| `report-only` | A user's bug report with a command and its wrong output. No test fails. | A reproduction written and run before the fix. | A write that is not the work: a scratch script opens iteration 1. |
| `two-causes` | One report; two defects in different files both contribute. | The first fix does not clear the symptom. | Repair loops that are real, and a long run. |
| `far-cause` | A wrong number at the end of a pipeline; the defect is three modules upstream. | Long diagnosis: many reads and probes, added logging removed again. | Writes that are instruments (logging), later reverted. |
| `not-a-bug` | A report that describes documented behaviour. | No source change; an answer that says so and cites the docs. | A run with no write at all. |

`not-a-bug` is also the one task where the right outcome is an empty diff,
which a pass-rate-only report cannot tell from giving up.

## The fixture: nb at a pinned commit

nb itself, cloned in the image at a pinned revision, with the defects applied
as patches during the image build.

- **Size.** About 13,000 lines of C# and 9,600 lines of tests in 143 files:
  large enough that the agent cannot read it all, small enough to build in a
  cell.
- **No memorised fix.** The defects are ours, planted in code no model has
  seen a fix for. A reverted upstream bugfix in a well-known project risks
  the agent recalling the patch and skipping the diagnosis.
- **Already proven in an image.** `agent-onboarding` clones and builds nb at
  a pin in the same base image; its Mock provider runs offline.
- **Cost of this choice.** `dotnet build` and `dotnet test` are slow, so a
  try costs a minute and runs will have fewer iterations per hour than a
  Python fixture would. The `NETSDK1188` flood (`lab.md` step 1) must be off
  in this image or it buries every try.

One fixture directory per task (`fixtures/nb-red-test`, ...), each the same
pin plus its own `plant.patch`, because a fixture is the repository a task
runs against and the planted defect is part of it.

How the plant gets in: the fixture's source is nb's repository at the pin
(`source.git`, `source.rev`), which proctor commits in the image; the first
`image.run` line then applies the patch (carried in the line as base64,
since the build context holds only `repo/`), deletes `.git` and commits
again, so the plant is in the only commit and no history or reflog shows
it. `plant.patch` beside `fixture.json` is the readable copy, and the
`planted-present` check holds the image to it: it reverse-applies the file
to the fixture commit in the verifier.

Rejected: a well-known open-source Python project (faster tries, but
memorised fixes, and a licence and a network-free test run to confirm per
project); a generated application (no memorisation, but small and tidy in
the way `first-acceptance-run` found sits at the ceiling).

## Candidate defects (drafted 2026-11-11, for review)

Against nb at `0e11b21`. Accepted by the user 2026-11-11; what planting them
showed follows the list.

### 1. `red-test`: the loop detector misses a two-call loop

- **Plant.** `nb.Core/DoomLoopDetector.cs`, `DetectLoop`: the pattern-length
  bound `patternLen <= _signatures.Count / _threshold` becomes `<`.
- **Symptom.** A, B, A, B, A, B at threshold 3 is not detected; only A, A, A
  is. Tests in `DoomLoopDetectorTests` fail by name.
- **Prompt.** "The build is red on main. Find out why and fix it." It says
  the unit tests are the signal and `evals/run.sh` is not needed (the user's
  decision, 2026-11-11: on this build the evals do not finish).
- **Why this one.** One file, one character, a failing test that names the
  class. It should be the short run: try, read, fix, try.

### 2. `report-only`: headings vanish from a multi-line prompt

- **Plant.** `nb.Core/Transcript/ProgramParser.cs`, `CoalesceLogicalLines`:
  the blank-line and `#` comment skip moves out of `if (!building)`, so it
  also applies inside a continued line.
- **Symptom.** A `system` prompt written over continuation lines loses every
  line that starts with `#` and every blank line: Markdown headings and
  paragraph breaks disappear from what the model is sent. Nothing errors.
- **Prompt.** A user's report with their program and "the model never sees
  my `# Rules` heading"; no file or function named.
- **Why this one.** The agent has to write a program and run `nb --compile`
  to see it, so the first write is a reproduction, not the fix. It is also a
  defect proctor's own multi-line prompts would hit.

### 3. `two-causes`: a patch to the end of a CRLF file

- **Plant A.** `nb.Core/Shell/ApplyPatch/SeekSequence.cs`, `Find`: the scan
  bound `i + pattern.Count <= hay.Count` becomes `<`, so a chunk whose old
  lines are the last lines of the file is never found.
- **Plant B.** `nb.Core/Shell/ApplyPatch/PatchApplier.cs`, `ApplyUpdate`: the
  restored final newline is `"\n"`, not `sep`, so a CRLF file comes back with
  one LF ending.
- **Symptom.** `apply_patch` on the last function of a CRLF file fails with
  "failed to locate chunk". With A fixed it succeeds, and the file's last
  line ending is wrong: B was hidden behind A.
- **Prompt.** A report with the file, the patch, and "the patched file should
  differ from the original only in the changed line".
- **Why this one.** The second defect cannot be seen until the first is
  fixed, so a run that stops at "it applies now" fails acceptance.

### 4. `far-cause`: a replayed transcript has a message out of place

Replaced 2026-11-12. The first plant (a side call's input tokens left out
of the session sum) was found by reading in 12 calls on Haiku: the missing
line sat beside its two siblings. It is kept outside the repository.

- **Plant.** `nb.Core/Transcript/RunJournal.cs`, `Append`: the turn counter
  no longer advances for a message nb injected (`source` set: the loop
  reminder, the todo reminder), so the reminder takes the turn of the tool
  round before it.
- **Symptom.** Nothing in the run. When its transcript is replayed with
  `--seed`, the loader rebuilds each turn as user messages, then the
  assistant message, then the tool message, so the reminder lands before
  the call it followed. Only transcripts of runs that were nudged do it.
- **Prompt.** "Some transcripts replay with a user message earlier than it
  was; most replay exactly; I can't share them." No transcript, no mention
  of reminders.
- **Why this one.** The wrong thing happens in the loader, the cause is in
  the journal, and the condition is in the conversation loop that injects
  reminders. Reproducing it needs a run that loops. The line reads as
  deliberate, so the agent has to decide which side is wrong; the class
  comment still states the right rule. Acceptance tests the round trip, so
  a fix in the loader that restores the order would pass too.

### 5. `not-a-bug`: `@file` in the middle of a prompt is not included

- **Plant.** None.
- **Report.** "`run @notes.md and summarise it` sends the literal text; the
  file is never included. `run @notes.md` works."
- **Right outcome.** No source change. The answer says this is documented
  (`docs/conversation-program-cli.md`: an include is a directive whose entire
  content is `@<path>`; any other `@` is literal) and says how to get what
  the user wants.
- **Why this one.** It reproduces exactly as reported, so "could not
  reproduce" is not available as an answer, and the tempting move is to
  implement inline includes.

## What planting them showed (2026-11-11)

Each patch applied to a clone at the pin, built, and run against nb's 671
tests and its 154 integration evals (`evals/run.sh --skip-llm`).

| Task | Tests that fail | Evals | Symptom by hand |
|---|---|---|---|
| `red-test` | 5 in `DoomLoopDetectorTests` | did not finish in 280 s | not needed |
| `report-only` | none | 154 pass | yes: `nb --compile` drops `# Rules` and `# Tone` from a continued `system` |
| `two-causes` | 10 with both or with A alone (5 `SeekSequenceTests`, 5 `PatchApplierTests`); 1 with B alone (`Apply_PreservesCrlfLineEndings`) | not run | not needed |
| `far-cause` (as replaced) | none | 154 pass | yes: a run nudged for looping, replayed as a seed, has the reminder before the third call |

What this changes:

- **No test needs cutting anywhere.** `report-only` and `far-cause` are
  silent under the whole suite and the evals as they stand; `red-test` and
  `two-causes` are meant to be seen.
- **`red-test` is wider than drafted.** With `<`, three identical calls are
  not detected either (3 / 3 is 1, and the loop never runs), so no loop is
  caught below six calls. The evals then appear to hang, which the task
  should either keep as part of its shape or avoid by saying the unit tests
  are the signal.
- **`two-causes` is test-visible, not report-only.** Plant A breaks ten
  tests, too many to cut honestly. The task keeps its report as the prompt;
  the agent will find the red tests, fix A, and be left with one failing
  test that B explains. The masked second cause survives; the "no test
  fails" part does not.

The patches are outside the repository for now
(`proctor-private/views/nb-defect-patches/`); they move into each fixture
when it is built.

## The feature task: `ask_user_question`, answered by the oracle

The user's choice (2026-11-11), replacing a smaller draft. nb has no user, so
today a model that needs one ends its turn with a question and the oracle,
if a sheet is attached, judges that last message and may answer. The feature
gives the model a tool to ask with, and routes the tool to the same oracle.

The contract, accepted by the user 2026-11-11 as proposed. Acceptance tests are written from it, so the task's prompt states
it in full; the unguessable parts are marked.

| # | Question | Proposed |
|---|---|---|
| 1 | The tool's name and argument | `ask_user_question`, one string `question`, in nb's snake case |
| 2 | When the tool is offered | Only when the program attaches an `oracle`; `tools -ask_user_question` removes it. A tool that can never answer is a trap |
| 3 | A hit | The tool result is the selected sheet bodies, verbatim, as the end-of-turn oracle appends them |
| 4 | A miss (unguessable) | The tool result is one fixed sentence (`The user has no answer to that question. Proceed on your own judgement.`) and the run continues. Unlike an end-of-turn miss, it does not end the run or set `oracle_miss` |
| 5 | The budget | A hit counts against `budget oracle_turns`, shared with end-of-turn resolutions; a question that would be a hit once it is spent gets `The user is no longer answering questions in this run.` and the run ends `oracle_budget` |
| 6 | The transcript | An ordinary `tool_call` and `tool_result`; the result carries `source: "oracle"` and `keys`, and the trailer's `oracle_turns` counts it |
| 7 | Tokens | The judge call is a side call, counted in the session's usage like any other |

- **What it crosses.** The tool surface and its golden tests, the harness's
  tool dispatch, `OracleResolver`, the evaluator's oracle budget, the
  transcript events and serializer, the Mock provider (a scripted
  `MOCK:tool=ask_user_question` call whose question carries the
  `MOCK:oracle=` rider), the docs, the evals.
- **Acceptance.** Hidden programs run against the built nb on Mock: a hit, a
  miss that continues, a spent budget, no oracle and so no tool, and the
  transcript fields. `suite-green` holds nb's own tests and goldens.
- **The reference implementation is written** (2026-11-11):
  `suites/nb-changes/reference/ask-user-question.patch`, 155 lines over
  eight files against the pin (a new `AskUserTool.cs`, the conversation's
  tool list and dispatch, the evaluator's judge call and budget, the journal
  and serializer for `source` and `keys`, one line in the Mock provider, the
  docs). nb's 671 tests pass with it unchanged, so no golden moved: the tool
  is offered only with an oracle, and no existing test attaches one and
  lists tools. It was written in a scratch clone and is not in nb; whether
  it goes upstream is the user's call.
- **What the prompt has to say beyond the seven rows.** The two fixed
  sentences, word for word, and how the Mock provider scripts a call
  (`MOCK:tool=ask_user_question <text>` sends `<text>` as `question`), since
  the acceptance script drives the built nb on Mock and cannot work
  otherwise. The task runs on the unplanted fixture (`nb-pinned`), which
  `not-a-bug` shares.
- **Reported apart.** The suite is named for the fixture (`nb-changes`),
  tasks carry `labels: kind=defect` or `kind=feature`, and the headline is
  read per kind.

## Checks

All script checks, run by Harbor's offline verifier. No judge.

| Check | Role | What it asks |
|---|---|---|
| `builds` | `pass` | The solution builds. |
| `acceptance` | `pass` | Hidden tests for this task's defect pass. They live in the suite (`acceptance/<task>/`), which only the verifier sees. For `not-a-bug`: the diff touches nothing but `.md` files. |
| `suite-green` | `pass` | nb's own tests still pass. |
| `planted-present` | `validity` | The defect is in the checkout the agent was given (the patch applied). |
| `tests-untouched` | guardrail | No existing test was deleted or weakened. |
| `diff-in-scope` | guardrail | The diff stays within nb's code, tests and docs, so a reproduction or log file left at the root shows. The built-in `files_touched`, paths per task. (Drafted as `scratch-left`.) |
| `named-cause` | guardrail | The final answer names the file that holds the defect. The built-in `answer_regex`, pattern per task. |
| `exit_ok` | `pass` | The run ended on its own. |

Each task has `reference/<task>.sh` (the fix) and the suite a
`reference/shakedown.sh` proving every check on the host at $0: the
references pass everything; nothing changed fails acceptance wherever
something is planted or asked for; a half fix, a wrong fix and a deleted
test each fail the check meant for them.

`suite-green` repeats a failing run once. nb has a test that depends on
timing (`RetryingChatClientTests.MinRequestInterval_PacesEveryCall...`),
which failed once in 18 shakedown runs on a loaded machine; without the
repeat it would fail a correct run now and then.

## Arms and cost

| Arm | Runs | Purpose | Estimated cost |
|---|---|---|---|
| Claude Code on Haiku | 5 tasks × 1 | Shakedown: do the tasks produce the intended shapes? | about $2 |
| Claude Code on Sonnet | 5 tasks × 4 | The traces | $20 to $35 |
| Qwen Code on the local model | 5 tasks × 3 | A second agent's shapes | $0 |

The Sonnet estimate rests on `agent-onboarding`: $0.26 a run at 17 calls and
700k tokens. A debugging run is expected at three to five times that. The
wall limit per cell is 45 minutes; cells run one after another, so the
Sonnet arm is an overnight run.

Codex is left out of the first round.

## What would count as the cut failing

Decided before the runs, so the result cannot be read to fit.

- `red-test` and `far-cause`: a reader of the timeline cannot tell where the
  agent stopped reproducing and started diagnosing, because both are inside
  one orient beat of 30 or more calls.
- `report-only`: the reproduction script makes iteration 1 an "advance" and
  the real fix a "rework" or "repair", so the intent labels mislead.
- `far-cause`: logging added and removed shows as advance then rework, the
  same as real work.
- `not-a-bug`: the run is one beat and the timeline says nothing about what
  was checked.
- "Other" above 15% on any arm.
- A blinded reader calls fewer than 80% of the cuts right.

Each of these has a likely remedy (a "reproduce" beat inside orient when a
try precedes the first write; scratch paths, declared or outside the
repository, do not open an advance), but none is built until a run shows the
failure.

## Order

1. Pick the pin and write the five patches against nb; confirm each
   symptom by hand.
2. Fixtures, tasks, checks, references; `shakedown.sh` green through the
   oracle.
3. Haiku shakedown; read the five timelines against the table above; change
   a task that did not produce its shape.
4. Sonnet and Qwen Code runs; `observe.py`; a blinded read of ten runs.
5. Record what failed against the list above in `project/todo.md`.

Step 1 needs someone who knows nb's code well enough to plant a defect whose
symptom is far from its cause. That is the slowest part and the one most
worth the user's eye.

## Not decided

- Whether the planted defects being in nb's public history (the fixture
  pins a commit and carries patches in this repository) matters for reuse:
  after one published run, a later model could have seen them.
- Whether `not-a-bug` belongs in this suite's pass rate or is its own suite,
  since "a pass rate across them means one thing" and its outcome is of a
  different kind.
