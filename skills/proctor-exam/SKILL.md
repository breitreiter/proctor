---
name: proctor-exam
description: Plan and build a proctor exam for the user's project — what an agent should be able to do with what they built, how to tell whether it did, and the exam, fixture and tasks that measure it. Use when the user wants to evaluate how coding agents or models cope with their SDK, CLI, docs, MCP server, skill, prompt or assistant, or asks for help writing a proctor exam.
---

# Planning and building a proctor exam

Proctor runs tasks against agents and models, grades each attempt with checks,
and reports pass rates with intervals. You are helping a user measure
something they built. This skill assumes you can read their repository and
proctor's; it covers what you would not guess and the places proctor
differs from other eval tools.

Work in two parts, with the user's approval between them: **plan**
(what to measure and how you would know), then **build** (the files, a
free shakedown, then the paid batch they agree to).

## Before anything

- **Find proctor's checkout.** Ask, or resolve the user's `proctor` command
  (`type proctor`; `Get-Command proctor` in PowerShell). Read its
  `README.md`, then `docs/configuration.md`, the reference for every file
  below. Its `exams/` are worked examples; read the one closest to the
  user's case before writing anything.
- **Run `proctor setup --check`** in the user's repository. It says which
  providers exist, which jobs are set up and what is missing; the model ids
  each key could reach are in `exams/setup.json`. Neither holds a key.
- **Never handle a key.** Not in a command, a file or a message. When an
  exam needs a provider or a job that is not set up, ask the user to run
  `proctor setup` themselves; it refuses to run without a terminal, on
  purpose. Config files hold `${VAR}` only.

## Part 1: plan

Inventory the project, then talk it through with the user. A less
experienced user will not have answers to hand; propose them from what you
read and let the user correct you.

1. **What did they build, and who is the agent?** An SDK or CLI a coding
   agent calls; docs a coding agent reads; an MCP server or skill an agent
   uses; a prompt, chatbot or headless job where a model *is* the product.
2. **What should the agent be able to do with it?** Name four to ten
   concrete tasks a real user would hand an agent, each with one outcome
   you can check. Prefer tasks the user has seen agents get wrong. A task
   every agent passes measures nothing; so does one none can.
3. **How would you know it was done?** For each task, the cheapest check
   that decides it (below). If you cannot say how to check a task, it is
   not a task yet.
4. **What is being compared?** That is the variants. Usually one of: two
   versions of the thing (a docs rewrite, a new tool description), as two
   variants with different `bundle`s and everything else equal; or several
   models or agents on the same version. Change one thing between variants.
5. **Which path runs it?** If the task needs a shell or file edits, or the
   question is about a shipped agent (Claude Code, Codex, Qwen Code), the
   variant is an agent variant and runs in a sandbox through Harbor; `--check` says
   whether one is set up. If the model only reads and answers (a document,
   a prompt, a tool definition, a chatbot turn), it is a bare nb variant with
   `tools none` and needs no sandbox. Start there when you can.
6. **What will it cost?** Attempts = variants × tasks × the exam's `attempts` (per task, for every variant).
   Estimate tokens per attempt, price it, and agree the number with the user
   before any paid batch. A free tier or a local model is the place to iterate.

Write the plan as a short `exams/<id>/PLAN.md`: the question in one
sentence, the tasks with their checks, the variants, the attempt count and cost,
and what result would change the user's mind. Get approval before Part 2.

**On numbers.** Every interval's `n` is the task count, not the attempt count:
each task is scored as the mean of its attempts. Ten tasks with three
attempts gives intervals on ten. More tasks beat more attempts; two or three
attempts is enough to see a task's variance. Say plainly when a plan has too
few tasks to detect the difference the user cares about (the report states
the minimum detectable effect).

## Part 2: build

### Where the files go

In the user's repository, `exams/` at the root holds their exams and
`fixtures/` their fixtures; proctor runs from that root (or `--root`).
`proctor setup` already wrote `exams/proctor.json`, `exams/nb.json` and
`exams/setup.json` there, all gitignored. Edit only the entries an exam
needs; setup tracks its own and leaves the rest alone.

```
exams/<id>/exam.json      attempts, grading (checks, pass, validity), labels, description
exams/<id>/variants/*.json one variant per file, the id its name: agent or runner/harness/provider, model, bundle, note
exams/<id>/program.nb      the nb program template (nb variants only)
exams/<id>/tasks/*.json    one task per file: fixture, prompt, expect, its own checks
exams/<id>/checks/*.sh     script checks
fixtures/<id>/fixture.json  source, checks that hold for every task on it, default expect
fixtures/<id>/repo/         what is copied into the work directory, and nothing else
```

Use proctor's vocabulary in every file and in what you tell the user:
exam, task, attempt, check, variant, fixture, batch, live.

### Proctor's gotchas

- **The fixture is what the agent sees; keep the answers out of it.**
  Only the source tree is copied into the work directory. A fixture whose
  `source.path` points at the user's repository root copies everything
  but `.git`, including `exams/` (the expected answers), `attempts/` and any
  local secrets. Point it at a pinned `{"git": ..., "rev": ...}`, a
  snapshot under `fixtures/<id>/repo/`, or exclude those directories with
  `source.exclude`. A task that needs no repository uses a fixture with an
  almost empty `repo/` (proctor's `fixtures/empty`).
- **What is under test belongs to the variant, the task's world to the
  fixture.** The README version, the tool description, the skill: a variant's
  `bundle`. The repository the task runs in: the fixture.
- **Program templates name the model before the provider:**
  `model {{model}}`, then `provider {{provider}}`. nb builds a provider's
  client as soon as it reads `provider`, and setup's provider entries
  carry no `Model`; the other order runs the wrong model or refuses to
  start. The variant's `model` must be an id the provider serves
  (`exams/setup.json` lists them per provider).
- **An `@file` include is a whole directive:** `system @{{bundle}}/README.md`
  works; text around it, or a path with a space in it, does not.
- **A check's role is where it is listed.** In `grading.pass` it decides
  the attempt; in `grading.validity` a failure excludes the attempt (use it for
  "the provider answered", so a rate limit is not a wrong answer); in
  neither it is a rate the report shows. Checks are declared on the exam,
  the fixture or the task, and an attempt carries the union.
- **Per-task answers go in `expect`.** A check field set to `"@expect"`
  reads the task's `expect`, so one shared check (`answer_regex`,
  `files_touched`, a judge's rubric) carries each task's answer.
- **Script checks** exit 0 pass, 1 fail, 2 needs a judge, anything else
  "could not grade"; the first line of stdout is the reason; each needs a
  `description`. They read `PROCTOR_WORK`, `PROCTOR_DIFF`,
  `PROCTOR_TRANSCRIPT` and `PROCTOR_EXPECT`. On an agent variant they run in an
  offline verifier, never on the host, and see no transcript.
- **Deterministic first.** A regex, an exit code, the user's own tests run
  against the result: free, repeatable, and they never drift. Use a model
  judge only for what cannot be checked otherwise, from a provider other
  than the one under test, with its entry in `exams/proctor.json`
  `judges` naming a model; setup writes none, since the model is the
  exam's choice.
- **Every check and task gets a one-sentence `description`, every variant a
  `note`.** The report puts it beside every number. A note says what is
  different about the variant, never what it is expected to do.
- **A variant is declared data, and its identity is its content.** Editing a
  variant's file or its bundle makes a new variant; once the old one has
  made attempts, `run` refuses the edit under the same id, so give it a new
  file and id. Hooks are the exam's: one that branches on `PROCTOR_VARIANT`
  to change what the agent sees hides that difference from proctor; put it
  in the bundle (a git bundle's `paths` can pick part of a revision).
- **A changed task makes its attempts stale.** A task's hash covers what
  the agent meets: the prompt, the fixture, an oracle's answer sheet, the
  program template and the hooks. Editing any of them leaves the attempts
  already made out of every count; `run` makes them again. Settle those
  before a long batch.
- **A changed check is a regrade.** The checks and the expected values are
  the standard. Add or change one and `grade` applies it to every current
  attempt; nothing is attempted again. Adding a task leaves the other
  tasks' results alone; adding an exam-level check regrades all of them.
  The exception is a script check on an agent variant: it runs when the
  attempt is made, so change the script (or a file it reads, or its
  expected values) and those verdicts are `outdated` until the attempts are
  made again, out of every rate. Prove the scripts for free before an agent
  batch, not after.
- **After a crash, `proctor run <id>` again.** There is no resume verb: run
  continues the interrupted batch and says so, and `proctor list <id>`
  names an interrupted batch.

### Prove it before anyone pays

1. `proctor list <id>` until it validates.
2. Prove every check both ways, with no model: that it passes a correct
   attempt and fails a wrong one. Proctor's examples show two ways. A
   `reference/<task>.sh` applies a known-good change and runs the checks on
   it (`exams/cli-for-agents/reference/shakedown.sh`, with `--unsolved`
   for the failing side). Or nb's Mock provider gives a scripted answer:
   a prompt of `MOCK:response=<text>` makes Mock reply `<text>`
   (`exams/readme-answers/reference/shakedown.sh`, with `--wrong`).
3. One attempt of one task on a free or local model, read the transcript
   and the report, and fix what looks wrong.
4. Then the agreed batch: `proctor run <id>`, then `proctor grade <id>` and
   `proctor report <id>` (the exam's id: they read every current attempt of it,
   whichever batch made it). Walk the user through the report: the verdict,
   the intervals, the failures and their quoted reasons. Until a variant is
   live the report shows the variants side by side; when the user picks the
   one in use, `proctor live <id> <variant>` marks it (commit
   `exams/<id>/live.json`), and later reports compare the others with it.

If a check fails an attempt that looks right to you, the check is the suspect
before the agent is.
