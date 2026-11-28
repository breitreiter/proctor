---
name: proctor-exam
description: Plan and build a proctor exam for the user's project — what an agent should be able to do with what they built, how to tell whether it did, and the exam, fixture and stages that measure it. Use when the user wants to evaluate how coding agents or models cope with their SDK, CLI, docs, MCP server, skill, prompt or assistant, or asks for help writing a proctor exam.
---

# Planning and building a proctor exam

Proctor runs an exam's stages against agents and models, grades each stage
with checks, and compares what is under test try against try. You are helping a user measure
something they built. This skill assumes you can read their repository and
proctor's; it covers what you would not guess and the places proctor
differs from other eval tools.

Work in two parts, with the user's approval between them: **plan**
(what to measure and how you would know), then **build** (the files, a
free shakedown, then the paid run they agree to).

## Before anything

- **Find proctor's checkout.** Ask, or resolve the user's `proctor` command
  (`type proctor`; `Get-Command proctor` in PowerShell). Read its
  `README.md`, then `docs/configuration.md`, the reference for every file
  below. Its `exams/` are worked examples; read the one closest to the
  user's case before writing anything: `exams/readme-answers` (a model
  answering from a document), `exams/tern-docs` (a coding agent, with
  docs served beside it), `exams/code-change` (one question in three
  repositories).
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
   concrete things a real user would hand an agent, each with one outcome
   you can check. Each becomes a stage. Prefer ones the user has seen
   agents get wrong. A stage every agent passes measures nothing; so does
   one none can.
3. **How would you know it was done?** For each stage, the cheapest check
   that decides it (below). If you cannot say how to check a stage, it is
   not a stage yet.
4. **Do they share one environment?** Usually an exam has one fixture,
   and every stage runs in it. When the same question is asked in several
   repositories, the repository is an axis whose values each name a
   fixture, and each stage says which it belongs to (`when`). Then: does a
   later stage need what an earlier one made?
   Usually not, and each stage starts from the fixture as it was. If so,
   the exam is `"chained": true`: the workspace carries over, and a stage
   that is stopped ends the try, since the rest would run on broken input.
5. **What is being compared?** That is the axes. An axis is one thing you
   vary, a value one choice on it: two versions of the thing (a docs
   rewrite, a new tool description) are two values of one axis, with
   everything else equal; several agents or models are the values of an
   `assistant` axis. Two things that only ever change together are one
   axis. A configuration is one value on every axis, and the exam names
   the ones to run; nobody runs the whole grid. Mark the value in use
   `"live"` on its axis: the others on that axis are compared with it.
6. **Which path runs it?** If the work needs a shell or file edits, or the
   question is about a shipped agent (Claude Code, Codex, Qwen Code), the
   value names that `agent` and runs in a sandbox through Harbor;
   `--check` says whether one is set up. If the model only reads and
   answers (a document, a prompt, a tool definition, a chatbot turn), the
   value names a `provider` and a `model`, nb runs it bare with
   `tools none`, and it needs no sandbox. Start there when you can.
7. **What will it cost?** Model runs = named configurations × the exam's `tries` × the stages in each: a try is every stage once.
   Estimate tokens per stage, price it, and agree the number with the user
   before any paid run. A free tier or a local model is the place to iterate.
8. **How current must it stay?** Agents ship new versions every week or
   two, and each one followed is another paid run. If the question is the
   agent, the exam follows the versions people run; if it is the user's own
   work (docs, tools, a prompt), one pinned agent can serve for months.
   Agree which, and what would be a reason to move the pin.

Write the plan as a short `exams/<id>/PLAN.md`: the question in one
sentence, the stages with their checks, the axes and the configurations to run, the number of tries and the cost,
and what result would change the user's mind. Get approval before Part 2.

**On numbers.** The `n` of a comparison is tries, never stages: stages
share an environment, so they share causes, and proctor compares one
configuration's tries with another's. Each try gives two numbers, how
many of its correctness checks held and how many of its steps were flagged
as friction. Plan at least 4 tries of each configuration (`tries`), or 3
against 5; fewer can show a gap and never confirm one. More stages make
each try's number finer; more tries are what confirm a difference. One
try is a shakedown, for setup problems and plain failures.

## Part 2: build

### Where the files go

In the user's repository, `exams/` at the root holds their exams and
`fixtures/` their fixtures; proctor runs from that root (or `--root`).
`proctor setup` already wrote `exams/proctor.json`, `exams/nb.json` and
`exams/setup.json` there, all gitignored. Edit only the entries an exam
needs; setup tracks its own and leaves the rest alone.

```
exams/<id>/exam.json      description, attempts, fixture, stages (id, about, prompt, expect, its own checks), axes (each with its values), configurations (the ones to run), grading (checks, correctness, integrity, quality), labels
exams/<id>/values/<axis>/<value>/   the files a value carries
exams/<id>/program.nb      the nb program template (for a value that runs nb)
exams/<id>/checks/*.sh     script checks
exams/<id>/predictions/*.json   what the user expects of a configuration, written before its tries
fixtures/<id>/fixture.json  source, params, checks that hold for every stage on it, default expect, image
fixtures/<id>/repo/         what is copied into the work directory, and nothing else
```

Use proctor's vocabulary in every file and in what you tell the user:
exam, stage, check, fixture, axis, value, configuration, try (every stage
once; an attempt is one stage of it), bank, live, prediction.

### Proctor's gotchas

- **The fixture is what the agent sees; keep the answers out of it.**
  Only the source tree is copied into the work directory. A fixture whose
  `source.path` points at the user's repository root copies everything
  but `.git`, including `exams/` (the expected answers), `bank/` and any
  local secrets. Point it at a pinned `{"git": ..., "rev": ...}`, a
  snapshot under `fixtures/<id>/repo/`, or exclude those directories with
  `source.exclude`. An exam that needs no repository uses a fixture with an
  almost empty `repo/` (proctor's `fixtures/empty`).
- **What is under test belongs to a value, the stages' world to the
  fixture.** The README version, the tool description, the skill: a
  value's `files`, laid over the checkout, or a parameter it sets, which
  the fixture's image, its services or the program template act on. The
  repository the stages run in: the fixture. A value sets only
  parameters the fixture (or, for the program template, the exam)
  declares under `params`.
- **A value says what it changes.** Its `note` is one sentence on what it
  changes for the agent; `proctor build <id>` shows what it really
  changes in the starting workspace, with nothing run, to read against
  the note. Run it before any paid run.
- **Program templates name the model before the provider:**
  `model {{model}}`, then `provider {{provider}}`. nb builds a provider's
  client as soon as it reads `provider`, and setup's provider entries
  carry no `Model`; the other order runs the wrong model or refuses to
  start. The value's `model` must be an id the provider serves
  (`exams/setup.json` lists them per provider).
- **An `@file` include is a whole directive:** `system @{{param.README}}/README.md`
  works; text around it, or a path with a space in it, does not.
- **A check's role is where it is listed.** In `grading.correctness` it decides
  the stage, and may not ask a model; in `grading.integrity` a failure
  leaves the try out of the comparison; in `grading.quality` it is
  preferred, not required, and decides nothing (a judged check goes
  here); in none it is an observation the report counts. Checks are
  declared on the exam, the fixture or a stage. The exam's and the
  fixture's are asked of every stage, a stage's own of it alone, and a
  check on the workspace sees it as that stage left it.
- **Per-stage answers go in `expect`.** A check field set to `"@expect"`
  reads the stage's `expect`, so one shared check (`answer_regex`,
  `files_touched`, a judge's rubric) carries each stage's answer.
- **A stage that did not run loses the try.** A provider error or a rate
  limit is no result: none of that try counts and `run` makes another. A
  stage stopped by a limit or a denial is a result and is graded.
- **Name stages for the screen.** The `id` is the stage's label: two or
  three words, about 20 characters, saying what it asks (`clear-wait`,
  not `stage-12`). `about` is the one-sentence version.
- **An oracle runs on nb only, and through Harbor only in a try of its
  own.** A stage with an answer sheet beside a vendor's agent is refused.
- **Script checks** exit 0 pass, 1 fail, 2 needs a judge, anything else
  "could not grade"; the first line of stdout is the reason; each needs a
  `description`. They read `PROCTOR_WORK`, `PROCTOR_DIFF`,
  `PROCTOR_TRANSCRIPT` and `PROCTOR_EXPECT`, and each axis's value as
  `PROCTOR_AXIS_<NAME>`. Through Harbor they run in an offline verifier,
  never on the host, and see no transcript.
- **A coding agent reaches its model and what its fixture provides,
  nothing else.** Code that calls an API needs that API as a service of
  the fixture (`image.services`), reached at `localhost`, which the
  agent can call and cannot read; the checks get no services and start
  their own. A model or a dev server on the user's own machine is named
  as `localhost` and proctor relays the one port. Never propose opening
  the host's loopback to containers. `docs/network.md` and
  `docs/service-fixtures.md` have the rules; plan for them before
  writing a fixture, since they decide where things listen.
- **Deterministic first.** A regex, an exit code, the user's own tests run
  against the result: free, repeatable, and they never drift. Use a model
  judge only for what cannot be checked otherwise, from a provider other
  than the one under test, with its entry in `exams/proctor.json`
  `judges` naming a model; setup writes none, since the model is the
  exam's choice.
- **Every check gets a one-sentence `description`, every stage an `about`,
  every axis an `about`, every value a `note`, and every check a `why`.** The report puts the description beside
  every number. The `why` says what the check protects ("secrets in .env
  stay out of git"), where the description says what it does (".env is in
  .gitignore"); a person reads it beside a failure. `proctor list` names
  checks without one; four common checks have proctor's. A note says what
  the value changes, never what it is expected to do: an expectation is a
  prediction, a file of its own, written before the tries.
- **A value is declared data, and its identity is its content.** Editing a
  value or its files makes a new value; once the old one has made tries,
  `run` refuses the edit under the same name, so give it a new name and
  keep the old one. Hooks are the exam's: one that branches on an axis to
  change what the agent sees hides that difference from proctor; put it
  in the value (a value's `files` from git can pick `paths` of a revision).
- **A changed stage makes every try stale.** The exam has one hash
  over what the agent meets: every stage's id and prompt in order, the
  fixture, an oracle's answer sheet, the program template and the hooks.
  Changing, adding, moving or renaming a stage leaves the tries
  already made out of every count; `run` makes them again. Settle the
  stages before a long run.
- **A changed check is a regrade.** The checks and the expected values are
  the standard. Add or change one and `grade` applies it to every current
  try; nothing is made again.
  The exception is a script check through Harbor: it runs when the try is
  made, so change the script (or a file it reads, or its expected values)
  and those verdicts are `outdated` until the tries are made again, out of
  every number. Prove the scripts for free before an agent run, not after.
- **A change to the world is a new exam, not an edit.** A new agent
  version or model is a new value beside the old one; a new check is a
  regrade. But when the fixture, a service the stages' code
  calls, or the machine changes, the tries already made are about a
  world that is gone: copy the exam (and the fixture, if it changed) to a
  new id, try what is live again there, and leave the old exam
  closed. `docs/configuration.md`, "When an exam gets a copy", has the
  rubric.
- **After a crash, `proctor run <id>` again.** A try that was lost or never
  finished is made again, whole.

### Prove it before anyone pays

1. `proctor list <id>` until it validates.
2. Prove every check both ways, with no model: that it passes a correct
   result and fails a wrong one. Proctor's examples show two ways. A
   `reference/` script applies a known-good change and runs the checks on
   it (`exams/tern-docs/reference/shakedown.sh`, with `--unsolved`
   for the failing side). Or nb's Mock provider gives a scripted answer:
   a prompt of `MOCK:response=<text>` makes Mock reply `<text>`
   (`exams/readme-answers/reference/shakedown.sh`, with `--wrong`).
3. `proctor build <id>`: what each value changes, read against its note.
4. One try of one configuration on a free or local model (`"tries": 1`
   for now, and `proctor run <id> --configuration <name>`), read the
   transcripts and the report, and fix what looks wrong.
5. Then the agreed run: `proctor run <id>`, then `proctor grade <id>` and
   `proctor report <id>` (they read every current try of the exam,
   whenever it was made). Walk the user through
   `reports/<id>/explore/summary.md`: each value against the live one,
   the stages that failed and their quoted reasons. `proctor compare <id>
   <axis>=<value>` answers one comparison as JSON, with what it could not
   have shown; the skill for reading those is `proctor-answers`.

If a check fails a stage that looks right to you, the check is the suspect
before the agent is.
