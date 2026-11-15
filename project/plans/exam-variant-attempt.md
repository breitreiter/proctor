---
type: plan
title: Exam, variant, attempt — the data model as a worked example
created: 2026-11-15
status: agreed with the user 2026-11-15 (D1 to D15, D8 and D12 as revised); built through steps 1 to 4 of the work-list (the words, variants as files, the pool, live), claims and the change report not. The work-list is `exam-variant-attempt-removal.md`. The reasoning is in `/srv/aiwork/auspex/project/model.md`. This file is the target written out as files and commands, so that a wrong assumption is a wrong line
---

# Exam, variant, attempt

Everything here was the target when written. Steps 1 to 4 of
`exam-variant-attempt-removal.md` are built (the words, variants as
files, the pool, live); claims and the change report (stage 3) are not,
and where this file shows them it is still the target. Values from
the `tern-docs` runs of 2026-11-13 are real. Anything marked
**(invented)** is made up to carry the example forward.

Every place this file had to choose is numbered **D1** to **D15** and
listed at the end with what was chosen. The user went through them on
2026-11-15; D8 and D12 changed, and D7 and D10 are to be watched.

## The words

| Word | Meaning | Replaces |
|---|---|---|
| exam | the question, the tasks, the checks | suite, minus its arms |
| task | one prompt against a fixture, with its answer key | task |
| check | one yes-or-no grading question | check |
| variant | an agent, a model and a bundle | arm |
| attempt | one variant at one task, once | sample, run, cell |
| batch | the attempts one `proctor run` made | experiment |
| live | the variant in use now; the reference for a change | baseline, "the first arm" |
| claim | what should change between two variants, said beforehand | (none) |

## The files

```
exams/tern-docs/
  exam.json            the question, checks, pass list, surface, limits
  tasks/order-status.json  ... six of them
  variants/as-shipped.json
  variants/restructure.json
  variants/no-docs.json
  bundles/as-shipped/  bundles/restructure/
  live.json            which variant is live (absent until one is picked)
  claims/              one file per claim (empty until there is one)
  checks/  acceptance/  program.nb  README.md      as today
fixtures/tern-shop/    as today
attempts/20261113-0240-tern-docs-icih/      one directory per batch
  batch.json
  as-shipped/order-status/1/   manifest.json, trajectory.json, checks.json, ...
```

**`exams/tern-docs/exam.json`** (D1, D2). No variants in it.

```json
{
  "id": "tern-docs",
  "description": "Does a restructure of an API's documentation change what a coding agent builds from it? ...",
  "attempts": 2,
  "limits": { "wall_ms": 1800000 },
  "surface": { "description": "Tern's documentation site, served at localhost:8080 ...", "hosts": ["localhost:8080"] },
  "grading": {
    "checks": { "exit_ok": {}, "acceptance": {}, "current-endpoint": {}, "docs-fetched": {}, "docs-curled": {} },
    "pass": ["exit_ok", "acceptance", "tests-pass"]
  }
}
```

`"attempts": 2` is how many attempts of each task a variant should have
(D3). It is a target the exam sets, where `samples` was a property of
each arm.

**`exams/tern-docs/variants/as-shipped.json`** (D4).

```json
{
  "id": "as-shipped",
  "note": "Tern's docs as shipped: one quickstart on /v2 that states the units, a reference, a changelog that retires /v1",
  "agent": "claude-code",
  "agent_version": "2.1.301",
  "model": "claude-sonnet-5-5",
  "bundle": { "path": "bundles/as-shipped" }
}
```

`note` says what is different about it. It is never a prediction.

A variant's identity is a hash of its agent, agent version, model and
bundle contents, not its id. For `as-shipped` the bundle is
`sha256:a2ed391d…`. Edit the file or the bundle and it is a different
variant under the same name, which proctor refuses (D5):

```
$ proctor run tern-docs
exams/tern-docs/variants/as-shipped.json: this is not the as-shipped that made 12 attempts
  (bundle a2ed391d then, 77c0e5aa now). A changed variant is a new variant: give it a new id.
```

**An attempt's `manifest.json`.** Today's fields, with three changes:
`batch` for `experiment`, `variant` and `task` each carrying a hash, and
`attempt` for `sample`. The old `attempts` field (retries) becomes
`tries` (D6).

```json
{
  "id": "8c6503297a672f39",
  "exam": "tern-docs",
  "batch": "20261113-0240-tern-docs-icih",
  "variant": { "id": "no-docs", "hash": "sha256:…" },
  "task": { "id": "order-status", "hash": "sha256:…" },
  "attempt": 2,
  "fixture": { "id": "tern-shop", "hash": "sha256:c744146f…" },
  "status": "completed",
  "tries": 1
}
```

The task's hash covers its prompt, its fixture's definition, its answer
key and the checks that apply to it. Two attempts are the same task when
that hash matches.

## Part 1: finding a first pass

The person, or their agent, has written the exam and three variants.

```
$ proctor list tern-docs
tern-docs: Does a restructure of an API's documentation change what a coding agent builds from it?
  6 tasks, 7 checks (5 of the exam's, 2 of the fixture's), 2 attempts wanted of each task
  variant       attempts   wanted
  as-shipped    0          12
  restructure   0          12
  no-docs       0          12
  nothing is live; no claims
```

```
$ proctor run tern-docs
batch 20261113-0240-tern-docs-icih: 36 attempts to make
  ...
36 made: 29 completed, 7 failed
```

`run` makes the attempts that are missing and nothing else (D7). Run it
again and it has nothing to do. There is no `resume` verb; what it did
is under "After a crash" below.

```
$ proctor run tern-docs
tern-docs: every variant has its 2 attempts of each task; nothing to make
$ proctor grade tern-docs
graded 36 attempts
$ proctor report tern-docs
reports/tern-docs/explore/report.html
```

`grade`, `friction` and `report` take the exam, not a batch id (D8).
They read every attempt of the exam, whichever batch made it.

- **Which attempts count.** Those whose task still matches a current
  task, by hash, and whose variant still has a file. The rest stay on
  disk and are listed as stale, never compared.
- **`--batch <id>` narrows any of the three to one batch**, for "what
  happened in last night's run". `--variant` and `--tasks` narrow too,
  so a paid judge is not sent every old attempt.
- **A report says exactly what it was built from** (the batches, the
  variants' hashes, the tasks' hashes), and is kept: the exploration
  report under `reports/tern-docs/explore/`, each change report under
  `reports/tern-docs/<claim>/`. Shipping a second change does not
  overwrite the evidence for the first.
- **Retiring a variant is deleting its file.** Its attempts stay.
- **With several open claims, `report` is told which**:
  `proctor report tern-docs --claim 20261120-py38-quickstart`.

**The report, with nothing live and no claim**, is the exploration
report:

```
tern-docs
Does a restructure of an API's documentation change what a coding agent builds from it?

Three variants, side by side. None is live yet.

  variant       attempts passed   tasks passed
  as-shipped    12 of 12          6 of 6
  restructure   12 of 12          6 of 6
  no-docs        2 of 12          1 of 6     acceptance failed in 10

Picking the best of several flatters it: part of any lead is luck.
With 6 tasks this exam can confirm a difference of about 57 points.

Variants · Tasks · Failures · Friction · Every attempt · Method
```

Those counts are the real ones, and 57 is today's formula at six tasks. `as-shipped` and `restructure` tie, so
the pick is a judgement, and the person ships `as-shipped`:

```
$ proctor live tern-docs as-shipped
exams/tern-docs/live.json: as-shipped is live
```

`live.json` is committed with the exam (D9):

```json
{ "variant": { "id": "as-shipped", "hash": "sha256:…" }, "since": "2026-11-13T04:10:00Z" }
```

## After a crash

The laptop dies with the batch half made. After the reboot the command
is the same one, and it says what it is doing:

```
$ proctor run tern-docs
batch 20261113-0240-tern-docs-icih was interrupted: 21 of 36 attempts finished, 1 was running
  continuing it: 15 attempts to make
  ...
15 made: 15 completed
```

- **It continues the interrupted batch in place** when the conditions
  that batch recorded still hold: the runner script, the Harbor version,
  the container engine, the nb image. Those are what `resume` checks
  today.
- **If a condition changed, it says which and starts a new batch** for
  what is missing. Every attempt records its conditions, and the report
  warns when one variant's attempts were made under mixed ones.

  ```
  $ proctor run tern-docs
  batch 20261113-0240-tern-docs-icih was interrupted: 21 of 36 attempts finished
    not continuing it: Harbor is 0.25.0 now, 0.24.0 then
  batch 20261113-0612-tern-docs-p2xv: 15 attempts to make
  ```

- **Missing means the attempt never finished, not that it went badly.**
  Proctor already keeps these apart. An attempt's `status` is the
  machine's account: `completed`, or `failed` with a reason such as a
  hook failing, Harbor not starting, or Harbor killed at the wall
  limit. Its `exit_reason` is the agent's: `ok`, `time_budget`,
  `agent_error`. An attempt that completed with `time_budget` is a
  result and is graded. Only `pending`, `running` and `failed` are
  made again, as `resume` does now.
- **This has to be easy to find.** "Run it again" is not what a person
  or an agent looks for after a crash; they look for a word like
  resume. So `list` says it on an interrupted exam, `run --help` and
  `docs/cli.md` lead with it, and the authoring exam gets a task that
  starts from an interrupted batch (D15).

```
$ proctor list tern-docs
  ...
  batch 20261113-0240-tern-docs-icih was interrupted: 15 attempts missing. `proctor run tern-docs` continues it.
```

## Part 2: changing something live

**(invented from here.)** A shop on Python 3.8 reports that the agent's
`order_status` crashes. The quickstart in the as-shipped docs uses a
`match` statement, which needs 3.10.

**1. The complaint becomes a task.** A new fixture, `tern-shop-py38`,
and a task that asks the same thing in it:

```json
{ "id": "order-status-py38", "fixture": "tern-shop-py38",
  "description": "The shop's order status from Tern's, on Python 3.8",
  "prompt": "This is the backend of Larkspur Tea ... Implement `order_status` in `shop/shipping.py` ..." }
```

**2. The live variant attempts it.** Only the gap is run: the three
variants on one new task.

```
$ proctor list tern-docs
  7 tasks, 7 checks, 2 attempts wanted of each task
  variant       attempts   wanted
  as-shipped    12         14       live
  restructure   12         14
  no-docs       12         14
$ proctor run tern-docs --variant as-shipped
batch 20261120-0915-tern-docs-k3pd: 2 attempts to make
2 made: 2 completed
$ proctor grade tern-docs
graded 2 attempts; 36 already graded
```

It fails `acceptance` on both. The complaint is reproduced.

**3. The change is a new variant.** `variants/py38-quickstart.json`, the
same agent and model, a new bundle whose quickstart avoids `match`.

**4. The claim is written before the variant runs** (D10, D11):

```
$ proctor claim tern-docs py38-quickstart \
    --flips acceptance --on order-status-py38 \
    --because "the quickstart's match statement fails on Python 3.8"
exams/tern-docs/claims/20261120-py38-quickstart.json
```

```json
{
  "variant": { "id": "py38-quickstart", "hash": "sha256:…" },
  "against": { "id": "as-shipped", "hash": "sha256:…" },
  "because": "the quickstart's match statement fails on Python 3.8",
  "flips": [ { "check": "acceptance", "tasks": ["order-status-py38"], "from": "fail", "to": "pass" } ],
  "made": "2026-11-20T09:40:00Z",
  "attempts_at_claim": 0
}
```

`against` defaults to the live variant. `attempts_at_claim` is how many
attempts of the claimed tasks the variant already had; anything above
zero and the report calls the claim late. That nothing else gets worse
is not written down: every claim carries it.

**5. Run it.** Fourteen attempts for the new variant; the live one has
its fourteen already.

```
$ proctor run tern-docs --variant py38-quickstart
batch 20261120-0950-tern-docs-w8ne: 14 attempts to make
```

**The report, with a live variant and a claim**, is the change report:

```
tern-docs: py38-quickstart against as-shipped (live)

Claimed  the quickstart's match statement fails on Python 3.8
  acceptance on order-status-py38     fail → pass     held: 0 of 2 before, 2 of 2 after

Everything else
  6 tasks unchanged on the pass checks
  1 movement not claimed: docs-curled on quote-options, 2 of 2 → 1 of 2
    within what two attempts of one task can show; not evidence of harm

Where the live variant stands · The change · Friction, both variants · Every attempt · Method
```

**6. Ship it.**

```
$ proctor live tern-docs py38-quickstart
exams/tern-docs/live.json: py38-quickstart is live (was as-shipped)
```

## What goes away

| Today | Why it goes |
|---|---|
| `arms` in `suite.json` | variants are files beside the exam, outside its identity |
| `samples` on an arm | `attempts` on the exam |
| `proctor resume` | `run` fills gaps |
| "a changed suite is a new experiment" | attempts match by task and variant, so adding a task or a variant invalidates nothing |
| `baseline.json`, `proctor baseline` | `live.json`, `proctor live` |
| `report --against <experiment>` | a claim names the two variants |
| "the first arm is the reference" | the live variant is |
| an experiment id as the argument to `grade`, `friction`, `report` | the exam |
| `experiment.json` | `batch.json`: when, on what machine, at what versions |
| "explore mode" and "guard mode" in `docs/cli.md` | the two reports, chosen by whether something is live and claimed |

## Two things the existing suites show

**A variant is declared data only; a hook never defines one** (D12).
`suite-authoring` has two arms with the same bundle, agent and model.
They differ in what `hooks/arm-setup.sh` builds from the arm's id: the
docs alone, or the docs with the example suites beside them. That script
does two jobs. It prepares the machine (the model loaded, proctor
built), which is not under test. And it decides what the agent sees,
which is the whole difference between the two, and is a bundle.

A script cannot be a variant's identity: its hash misses what it calls,
and a fixed comment would make a "new" variant. So:

- **What the agent sees is always the bundle.** A bundle may be a subset
  of a git revision, so the two differ in content proctor can hash:

  ```json
  { "id": "docs", "note": "the README and the layout plans, no example exams",
    "runner": "nb", "harness": "nb", "provider": "local-qcoder", "model": "qwen3-coder-next",
    "bundle": { "git": "https://github.com/breitreiter/proctor.git", "rev": "1e71808b…",
                "paths": ["README.md", "docs/configuration.md", "docs/cli.md", "project/plans/on-disk-layout.md"] } }
  ```

  `docs-and-examples` is the same with `"exams/"` and `"fixtures/"`
  added to `paths`.
- **Hooks belong to the exam and prepare the machine.** They are told
  which variant they are running for. One that branches on it is hiding
  part of the variant from proctor, and the docs say so.
- **Not yet:** variants that differ by an agent setting (a permission
  mode, a tool server). That is declared data too, a `settings` block
  counted in the identity, added when the first exam needs it.

**The exam that tests authoring an exam has the old words in its
prompts.** `suite-authoring` becomes `exam-authoring`, and it is the
test that this model is usable by an agent. Its first task today:

> Write a suite with the id `stock-count`. It has one task, on the
> `stock` fixture already under fixtures/: ... It has one arm: nb with
> the provider `local-qcoder` and the model `qwen3-coder-next`, three
> samples. ... Validate the suite with proctor's `list` before you
> finish.

and as it would read (D13):

> Write an exam with the id `stock-count`. It has one task, on the
> `stock` fixture already under fixtures/: ... It has one variant: nb
> with the provider `local-qcoder` and the model `qwen3-coder-next`.
> Each task is attempted three times. ... Validate the exam with
> proctor's `list` before you finish.

Its checks `validates`, `accepts-correct` and `rejects-wrong` run the
written exam, so they move with the tool and need no rewording.

## What was chosen, to argue with

| | Choice | The other way |
|---|---|---|
| D1 | The directory is `exams/`, the file `exam.json` | keep `suites/` and `suite.json` on disk and say "exam" only in what people read |
| D2 | Old spellings are not accepted. The suites, fixtures and tests are migrated once by a script. Existing runs are not migrated: they are dropped and made again (the user, 2026-11-15) | accept old spellings for a release, as the `evals` rename did |
| D3 | The number of attempts wanted is set on the exam | per variant, as `samples` is now; or only on the command line |
| D4 | One file per variant, in the exam's directory | a `variants` list in `exam.json`, left out of its hash; or variants outside the exam so one can sit several exams |
| D5 | A changed variant under an old id is refused | accepted as a new variant with the same name, older attempts kept apart by hash |
| D6 | `attempt` is the unit; today's retry counter `attempts` becomes `tries` | another name for the unit |
| D7 | `run` only fills gaps and continues an interrupted batch; `--variant` and `--tasks` narrow it; no `resume` verb. **To watch:** if people or agents hunt for it, `resume` comes back as a synonym | keep `resume` from the start |
| D8 | `grade`, `friction`, `report` take the exam; `--batch` narrows to one; reports are stamped with what they were built from and kept per claim (revised 2026-11-15) | take a batch id as the argument |
| D9 | `live.json` beside the exam, committed | a field on the variant file; or outside the repository |
| D10 | A claim is made with a verb, which stamps the time and counts existing attempts. **To watch:** whether the verb is found and used, or claims get written by hand | a hand-written file, trusted |
| D11 | A late claim is allowed and labelled | refused |
| D12 | A variant is declared data only: agent, version, model, bundle. A bundle may be a subset of a git revision (`paths`). Hooks are the exam's and never define a variant (reversed 2026-11-15) | a `setup` script on the variant, counted in its identity |
| D13 | The authoring exam's prompts are reworded and it is rerun as the acceptance test for this change | leave it until after |
| D14 | Batches live under `attempts/` | keep `runs/` |
| D15 | Continuing after a crash is announced by `list` and `run`, leads the CLI docs, and is a task in the authoring exam | rely on the docs alone |
