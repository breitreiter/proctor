---
type: plan
title: The on-disk layout of a batch
created: 2026-09-17
status: draft
---

# The on-disk layout of a batch

The first of the design plans the research pass made obvious. Everything else
in proctor reads or writes this layout: the runner fills it, the grader adds to
it, the reporter derives from it, and `archive` and `fetch` move part of it.
So it goes first, and it is written as a worked example of the batch in
`brief.md` rather than as a schema, because a wrong assumption is easier to
see as a wrong line in a tree than as a wrong sentence in a table.

The sources for every choice are `learnings/prior-art.md` §1 and §2,
`learnings/test-framework-patterns.md`, and `learnings/where-runs-live.md`.
Where this plan departs from them it says so.

## The three roots

A repository that uses proctor has three directories, one per tier from
`learnings/where-runs-live.md`:

| root | tier | in git | who writes it |
|---|---|---|---|
| `exams/` | definition | yes | the human |
| `attempts/` | raw | no, gitignored | the runner, grader and judge |
| `reports/` | derived | yes | the reporter, never by hand |

`exams/` is the sidecar the brief asks for. `attempts/` is what `archive` uploads.
`reports/` is what the site bundle reads and what CI posts.

The root names are fixed. A repo that wants them elsewhere sets paths in
`exams/proctor.json`, but the default has to work with no configuration, and
a reader should be able to guess where things are.

## The definition tier: `exams/`

The words are the ones `project/plans/exam-variant-attempt.md` fixes; the
second column is the word the evals literature uses for the same thing.

| word | the literature's word | one sentence |
|---|---|---|
| **exam** | evaluation suite | The question being answered: related tasks with one goal, graded by one shared checklist so a pass rate across them means one thing. |
| **task** | task | One input and one desired outcome: a prompt against a fixture, with the checks that say whether the outcome was reached. |
| **check** | grader | One yes-or-no item on the checklist, asked of a finished attempt. Its role in the headline is set by where it is listed. |
| **variant** | agent harness + configuration | What is under test: a harness, a provider, a model and a bundle, given every task. |
| **fixture** | environment | The repository a task runs against and what "done" looks like in it; reused across exams. |
| **attempt** | trial | One measurement of a stochastic system: one variant at one task, once. On disk, the directory its evidence lives in. |
| **batch** | eval run | The attempts one `proctor run` made: what the exam lacked at the time. An exam's attempts are every batch's. |
| **live** | control | A mark on one variant (`live.json` beside the exam): the one in use now, and the reference the report compares the others with. |

> Superseded in part by [the fixtures plan](../archive/fixtures-arms-baselines.md)
> (2026-09-21): fixtures are repo-level under `fixtures/` and a task names
> one; the attempt hooks that reset the fixture and collected the diff are
> proctor's; `grading.validity` is a third list beside `pass`.

One exam is one behaviour under test, in its own directory. Tasks are data
beside it. This is the JUnit `@CsvFileSource` shape from the test-framework
learnings, and the "one file, one thing" rule from lore.

```
exams/
  proctor.json                  this machine's config, gitignored: nb, agents' routes, judges, Harbor
  code-change/                  one exam: "can the agent make this code change"
    exam.json                   labels, attempts (how many per variant and task), nb (runner, mounts), hooks, grading
    variants/                   one file per variant: floor.json, codex.json, claude.json
    program.nb                  the nb program template; {{task}} placeholders
    live.json                   which variant is live: {"variant": {"id", "hash"}, "since"}; absent until one is picked
    tasks/
      add-retry-flag.json       one task: which fixture, what prompt, what "done" means
      fix-null-deref.json
      rename-module.json
    rubric.md                   judged criteria, if any; absent means deterministic only
    checks/                     scripts for what the built-in checks cannot say
      builds.sh
      tests-pass.sh
      diff-in-scope.sh
```

`exam.json` for the batch in the brief:

```json
{
  "id": "code-change",
  "labels": { "area": "coding/change" },
  "attempts": 3,
  "nb": { "runner": "../runners/container.sh", "mounts": { "work": "/work", "bundle": "/bundle" } },
  "hooks": {
    "variant": { "setup": "hooks/start-fakes.sh", "teardown": "hooks/stop-fakes.sh" },
    "attempt": { "setup": "hooks/reset-fixture.sh", "teardown": "hooks/collect-diff.sh" }
  },
  "grading": {
    "checks": {
      "exit_ok":      { "exit_reason": "ok" },
      "no_denials":   { "denied_calls": { "max": 0 } },
      "not_nudged":   { "loop_nudged": false },
      "under_budget": { "max_tool_calls": 40, "max_duration_ms": 1800000 },
      "builds":       { "script": "checks/builds.sh", "description": "the repository builds" },
      "tests-pass":   { "script": "checks/tests-pass.sh", "description": "the repository's own tests pass" },
      "diff-in-scope":{ "script": "checks/diff-in-scope.sh", "description": "only the files the task names were changed" }
    },
    "pass": ["exit_ok", "builds", "tests-pass"],
    "judge":  { "provider": "cf-glm", "criteria_from": "rubric.md", "window": "last-assistant+diff" }
  }
}
```

A variant is a file of its own, `variants/floor.json`, holding only what
it declares. Its hash covers everything but `id` and `note`:

```json
{
  "id": "floor",
  "note": "the local floor: nb on qwen3-coder-next",
  "agent": "nb",
  "harness": "nb",
  "provider": "local-qcoder",
  "model": "qwen3-coder-next"
}
```

Points to notice:

- **Variants are a declared list, not a cartesian product.** Each variant names its
  runner, harness, model and attempt count. The mixed-runner shape (nb for the
  floor, real CLIs for the rest) is the brief's open question made explicit
  and recorded per variant. Proctor never compares a costume variant to a real-CLI variant
  without the manifest showing which is which.
- **Hooks are the project's scripts, run at a named level.** Proctor's
  contract is the order (run, variant, task, attempt; teardown in reverse) and the
  recording. The fixture reset lives in the attempt-level setup because a task
  here is a repository that the attempt mutates.
- **Labels are the consumer's, and there are no tags** (2026-09-22; this
  replaced a registered `tags` list of `deterministic` and `judged`). A
  label is a key with one or more string values on the exam, a fixture or a
  task; proctor stores, prints, filters and records them on every result
  row and never interprets a key. Whether an exam is judged follows from a
  check naming a judge, so the PR-versus-nightly split is the consumer's to
  express, as a label its pipeline filters on.
- **Grading is declared, not discovered.** Checks are a named map. Most are
  one-line built-ins over a field nb already emits (the vocabulary below);
  scripts under `checks/` are for what the built-ins cannot say, which is the
  repo-specific trio here. `pass` names the subset that constitutes success;
  every other check is a guardrail rate in the report. The judge is named by
  provider and reads a declared window, never the whole transcript.

A task:

```json
{
  "id": "add-retry-flag",
  "fixture": { "git": "https://github.com/org/fixture-a", "rev": "3f2c1e9" },
  "prompt": "Add a --retry <n> flag to the fetch command. Existing tests must pass.",
  "expect": {
    "files_touched": { "paths": ["src/fetch.cs", "tests/FetchTests.cs"], "mode": "at_least" },
    "tools_used": ["bash"]
  },
  "checks": {
    "retries-twice": {
      "script": "checks/retries-twice.sh",
      "description": "a getter that fails twice is called three times with --retry 2"
    }
  }
}
```

A task's `checks` block is the check set only this task can state: its
desired outcome. It joins the exam's and the fixture's for that task's
attempts (a name declared at two levels is a problem); its scripts resolve
against the exam directory like the exam's own; its rate in the report is
over the tasks that carry it. A check every task needs belongs on the exam,
parameterised through `expect`.

The `expect` block is where a task supplies values to the exam's built-in
checks. A field that says `"@expect"` reads `expect.<check name>.<field>`
first and `expect.<field>` second, so two checks over one field can differ
per task. A model check reads `expect.<check name>`: the expectation as a
value, or `{ask, expect}` when its `ask` is `"@expect"` too, so one `correct`
check can carry a rubric per task. `files_touched` has a mode, `at_least`, `exactly` or `at_most`, over
the diff the teardown hook collected; without a mode the check would have no
semantics, which was the state of this example before the promptfoo pass.

The task id is the stable identity; it is a path segment in `attempts/` and a
column in every table. Renaming a task is a new task. The fixture is pinned
to a revision so the task means the same thing next month.

### The check vocabulary

`learnings/prior-art/promptfoo-checks.md` read promptfoo's assertion
catalogue as a demand signal and found the first draft of this plan had the
right stance and the wrong bottom rung: every check was a script, and the
checks people write daily are one-line declarations over a known field.
nb's transcript already carries those fields typed, so built-ins are cheap.

Built-ins read a named **window** of the transcript. The windows are fixed:

| window | source in the JSONL |
|---|---|
| `answer` | last `assistant_text` |
| `answer_json` | last `assistant_json` |
| `trailer` | the `result` event |
| `tool_calls` | every `tool_call`, with `approved`, `approval_reason`, typed `arguments` |
| `tool_results` | every `tool_result` |
| `oracle_turns` | `user` events with `source: oracle` |
| `diff` | `diff.patch` in the attempt, if the teardown hook wrote one |

The built-ins, each binary, each negatable with a `not_` prefix, values
supplied either in `exam.json` (same for every task) or in a task's `expect`
block (per task):

| check | window | shape |
|---|---|---|
| `exit_reason` | trailer | one of nb's exit reasons; `ok` is the implicit default check on every task |
| `answer_contains`, `answer_regex`, `answer_equals` | answer | string or pattern |
| `answer_json_schema` | answer_json | path to a schema file beside the task |
| `answer_words` | answer | `{min, max}` |
| `tools_used`, `tools_used_any` | tool_calls | list of tool names |
| `tool_args` | tool_calls | `{name, args, mode: partial \| exact, match: equals \| regex, ignore: [globs]}`; `partial` means expected is a subset of actual; `regex` makes each string in `args` a .NET pattern over the call's string argument (other values still compare equal). `not_tool_args` passes when no call matches |
| `tool_sequence` | tool_calls | `{names, mode: in_order \| exact}`; `in_order` allows gaps |
| `denied_calls` | tool_calls | `{max}` over `approved == deny`; `approval_reason == no-match` is "reached outside its surface" |
| `tool_errors` | tool_results | `{max}` |
| `loop_nudged` | user events | boolean; needs nb to tag the nudge (see `todo.md`, candidates for nb); until then matches the reminder text |
| `oracle_hit`, `oracle_misses`, `oracle_turns` | oracle_turns, trailer | keys, `{max}`, `{max}` |
| `files_touched` | diff | `{paths, mode: at_least \| exactly \| at_most}` |
| `max_tool_calls`, `max_tokens`, `max_duration_ms`, `max_cost` | trailer | number; cost needs a price on the provider entry until nb carries it on the trailer |

**A script is a check too**, declared as `{ "script": "checks/name.sh",
"description": "..." }`. It runs in the attempt directory with the task's `expect` block in an
environment variable, exits 0, 1 or 2 for pass, fail, needs-judge, and its
first line of stdout is the reason. That is weaver's grader contract plus
promptfoo's custom-assertion habit of returning a reason beside the verdict,
so a matrix entry can show both without opening a log.

**Every check has a sentence** (2026-09-22, after the first real report read
as a wall of ids). `description` is the one key in a check spec that is not
a check. A built-in derives one from its fields when none is given; a script
check must declare one, because from outside a script says nothing. The
exam, a variant and a task take an optional `description` too, and a task
falls back to the first line of its prompt. `stats.json` carries them in a
`descriptions` block and the report opens with the exam's, then "Where it
fell down": per variant, each check that did not hold in an analysed attempt, in
its own words, most often first, with the tasks it happened in (the
`failures` list on each variant's stats). The tables print the sentence beside
the id. The narrative is the author's prose and the counts, never a model's.

**What is deliberately absent.** Scalar metrics that hide two booleans
(`tool-call-f1` is `tools_used` plus `not_tools_used_any`); reference-text
similarity (ROUGE, BLEU, embeddings), because our tasks have no reference
prose and overlap measures phrasing; pairwise or holistic judging; and score
averaging with weights and thresholds, because a pass is a named subset of
binary checks, not a weighted sum.

**Deferred: a classifier rung.** Between built-ins and the judge there is
room for a small fixed-weight classifier over the last assistant text, for
the one question that is neither regex nor worth a judge call: did the
closing message claim completion, ask, or report blocked. The oracle bench
calls the wrong answer to that "done-on-waiting" and names it the dangerous
number. It is deterministic for fixed inputs, cheap, and outside every variant's
family. It is not in the first version, and when it arrives it is held to
the judge's standard: label out, never a confidence; threshold in versioned
code; validated against one to two hundred human-labelled attempts with kappa
reported; and a `needs-judge` fallback below the bar, not a replacement.

## The raw tier: `attempts/`

One directory per batch, attempts addressed by their coordinates, one file
per concern inside an attempt so `ls`, `cat`, `jq` and `grep -l` are the query
language. An exam's attempts are every attempt of every batch of it (the
pool); the nesting under the batch that made an attempt is where it lives,
not what it belongs to.

```
attempts/
  20260917-1432-code-change-k7px/           batch id: date, exam, 4 random chars
    batch.json                              what is the same for every attempt of the batch, and the attempts it set out to make (below)
    status.json                             {planned, counts by attempt status, updated}; rewritten as attempts finish
    harbor/                                 agent variants only: tasks/<variant>/<task>/ compiled once per batch, agents/
    floor/                                  variant
      hooks/
        variant.setup.log
        variant.teardown.log
      add-retry-flag/                       task
        1/                                  attempt
          manifest.json                     random id, coordinates with hashes, timing, versions
          status                            one word: pending | running | completed | failed | skipped
          program.nb                        the program exactly as run, placeholders resolved
          program.jsonl                     what went down stdin: the program compiled on the host by `nb --compile`, includes inlined
          transcript.jsonl                  nb's output, untouched
          trajectory.json                   nb's own ATIF of the same attempt (NB_TRAJECTORY), what the Harbor gate compares
          stderr.txt
          hooks/
            attempt.setup.log
            attempt.teardown.log
          diff.patch                        the checkout's diff against the fixture, collected after teardown
          checks.json                       {name: {result, reason}}, written by `proctor grade` (below)
          friction.json                     where the attempt paid a cost the task did not ask for, written by `proctor friction`
          friction-reading.haiku.9f2c1ab04d7e.json   what `friction --reader` sent a reader and what it answered; the cache
          verdicts/
            sensible.glm.a1b2c3d4.json      a model check's verdict: check, judge, hash of the request
        2/
      fix-null-deref/
      rename-module/
  20260918-0910-code-change-p2xv/           a later batch: only what the exam still lacked
    batch.json
    floor/
      add-retry-flag/
        3/                                  numbered after every attempt of floor/add-retry-flag on disk
```

> Never built, kept from the first draft: run-level hooks
> (`hooks/run.setup.log`, refused by validation as not yet) and the
> `command` runner's attempts (`prompt.txt`, `transcript.txt`; a `command`
> variant is refused as not yet). Agent variants run through Harbor
> instead, and their attempts hold `trajectory.json`, `core.json` and
> `harbor/` (`CLAUDE.md`).

**Batch id.** The trackers all warned against timestamp-only names and
against parameter-derived ids. This takes both halves: a date prefix so `ls`
sorts chronologically, the exam id so a human can tell batches apart, and
four random characters so two batches started in the same minute do not
collide. The random part is what makes it an id; the rest is a courtesy.

**Attempt path is coordinates.** `<variant>/<task>/<n>/` is the compound id from
the test-framework learnings, and it is deliberately not random. A new
attempt is numbered after every attempt of its variant and task in any
batch, so `variant/task/n` names one attempt across the exam's pool. The
random id lives inside the manifest.

**Current and stale.** An attempt is current while the variant hash and task
hash it was made under (its manifest's; for one never started, its batch's)
are the exam's today, and stale otherwise. `run` makes, per variant and
task, the exam's `attempts` less the current, completed ones; `grade`,
`friction` and `report` read the current ones and list the stale ones with
the hash that moved.

**`status` is a one-word file** so `grep -l completed attempts/*/*/*/*/status`
answers "what finished" without parsing anything. The five values are the
Sacred-style enum. `failed` means infrastructure (the runner could not
produce a transcript, or a hook failed); an attempt that produced a transcript
whose exit reason is `provider_error` is `completed`, because the transcript
is the evidence and grading decides what it means. This distinction is what
lets retries target infrastructure only.

**`checks.json` is a map from check name to verdict and reason.** The four
verdicts are `pass`, `fail`, `needs-judge` and `error`, where `error` means
the check itself could not run and is reported as such rather than folded
into `fail`. Every named check gets its own per-variant rate with an interval in
the report; the checks listed in `grading.pass` combine into the headline
pass, everything else is a guardrail column. That is weaver's `pass.jq` made
data, and it is the report-versus-gate split from `learnings/ci-distribution.md`
at the level of a single check.

```json
{
  "exit_ok":       { "verdict": "pass", "reason": "exit_reason=ok" },
  "no_denials":    { "verdict": "fail", "reason": "1 denied call: bash (no-match)" },
  "builds":        { "verdict": "pass", "reason": "dotnet build: 0 errors" },
  "tests-pass":    { "verdict": "fail", "reason": "2 of 41 tests failed" },
  "diff-in-scope": { "verdict": "needs-judge", "reason": "touched 1 file outside expect.files_touched" }
}
```

**Grading writes beside the evidence, never into it.** `checks.json` and
`verdicts/*.json` are separate files, so a regrade with a new rubric adds a
file and touches nothing. The verdict filename carries the judge provider and
a short hash of the rubric text, so a verdict from a changed rubric is a
different file and the old one is still there. That is the record-review-
approve loop from the test-framework learnings, in file form.

**Secrets never reach the raw tier.** The oracle bench wrote a config
snapshot with live keys beside its results. Proctor records which provider
entry was used by name in the manifest and does not copy config at all; the
config that matters for reproducibility is the provider entry's endpoint and
model, which the manifest holds as plain fields. If a future need for a full
config snapshot appears, it is written with every value of a key-like field
replaced by `<redacted>`, and that rule is a test.

The attempt manifest, as `run` writes it now:

```json
{
  "id": "b7e2f9c04d1a4e6b",
  "exam": "code-change",
  "batch": "20260917-1432-code-change-k7px",
  "variant": { "id": "floor", "hash": "sha256:4b1e…" },
  "task": { "id": "add-retry-flag", "hash": "sha256:0c3f…" },
  "attempt": 1,
  "runner": "nb", "harness": "nb", "provider": "local-qcoder", "model": "qwen-coder",
  "fixture": { "id": "ledger", "hash": "sha256:c744…" },
  "nb": { "path": "/usr/local/bin/nb", "config": "/repo/exams/nb.json" },
  "transcript": { "file": "transcript.jsonl", "format": "nb-jsonl" },
  "started": "2026-09-17T14:33:02Z", "ended": "2026-09-17T14:51:40Z", "duration_ms": 1118000,
  "host": "bench",
  "versions": { "proctor": "0.3.6", "nb": "0.9.0" },
  "program_hash": "sha256:41ab…",
  "status": "completed", "tries": 1
}
```

`bundle` (source, path, hash) is added when the variant has one, `agent`
and `oracle` on an agent variant's attempt, `status_reason` on a failed
one, and `nb_exit_code` once nb ran.

The batch manifest, `batch.json`, holds what is the same for every attempt
of the batch, so attempts do not repeat it, and the attempts it set out to
make, which is how the pool knows an attempt that never started:

```json
{
  "id": "20260918-0910-code-change-p2xv",
  "exam": "code-change",
  "exam_hash": "sha256:9c1e…",
  "exam_def": { "id": "code-change", "attempts": 3, "grading": { "…": "…" } },
  "variants": [ { "id": "floor", "hash": "sha256:4b1e…", "def": { "id": "floor", "runner": "nb", "…": "…" } } ],
  "tasks": [ { "id": "add-retry-flag", "hash": "sha256:0c3f…" } ],
  "attempts": [ { "variant": "floor", "task": "add-retry-flag", "attempt": 3 } ],
  "created": "2026-09-18T09:10:00Z",
  "command_line": "proctor run code-change",
  "host": "bench",
  "versions": { "proctor": "0.3.6", "nb": "0.9.0" },
  "repo": { "commit": "3f2c1e9a…", "dirty": false },
  "nb": { "path": "/usr/local/bin/nb", "config": "/repo/exams/nb.json", "runner": { "script": "runners/container.sh", "path": "…", "hash": "sha256:…" } },
  "bundles": { "floor": { "source": "bundles/floor", "path": "/repo/bundles/floor", "hash": "sha256:…" } },
  "harbor": { "version": "0.24.0", "tasks_hash": "sha256:…", "nb_image": "proctor-nb:…", "engine": "docker" }
}
```

`variants` and `tasks` are only those the batch made attempts of. `harbor`
is there when it made an agent variant's attempts; `run` continues an
interrupted batch only while its runner, `harbor.version`,
`harbor.tasks_hash`, `harbor.engine` and `harbor.nb_image` still hold. A
`batch.json` without `attempts` was written by an earlier proctor and is
refused, not read. The archive location the first draft put here was never
built.

## The derived tier: `reports/`

What the reporter writes, what the site reads, what gets committed. Every
number that appears anywhere is computed here, in .NET, once. The browser and
the markdown renderer both read these files and neither computes anything.

```
reports/
  code-change/
    explore/                                the exploration report of an exam: the latest
      results.jsonl                         one row per current attempt read: coordinates, batch, status, exit_reason, trailer fields, check results, id
      stats.json                            per-variant rates with intervals, pairwise differences, MDE sentence, methods footnote; built_from, the stamp
      report.html
      summary.md                            the markdown rendering, for CI and humans
      20261115-183450/                      an earlier report, moved aside by the next, named by when it was built
```

`stats.json` `built_from` says what the report was built from: when, the
batches its attempts came from, each variant's and task's hash, how many
current attempts it read and how many stale ones it left out (each with
why), the narrowing asked for (`--batch`, `--variant`, `--tasks`), and per
variant the conditions (versions, runner, engine) that differed between
the batches its attempts came from.

> Never built: `reports/data/index.json` (every batch with its headline and
> archive url) and the site bundle that would read it. The change report
> under `reports/<exam>/<claim>/` waits on claims
> (`exam-variant-attempt.md`, stage 3).

`results.jsonl` carries the coordinates, the batch and the attempt id on
every row, which is the oracle bench's `--variant` lesson generalised: a
row can be joined to anything without opening another file. It carries no
transcript text. The judge's evidence quotes are in the raw-tier verdict
files.

## The verbs, and what each touches

| verb | reads | writes |
|---|---|---|
| `proctor run <exam>` | `exams/<exam>/`, every batch of the exam | an interrupted batch's missing attempts, in place; then what is still missing as a new batch, `attempts/<id>/` |
| `proctor grade <exam>` | the exam's current attempts, `exams/<exam>/checks/` | `checks.json` (and `verdicts/`) in each current, completed attempt |
| `proctor friction <exam>` | the exam's current attempts, the exam's `surface` | `friction.json` in each current, completed attempt that has a `trajectory.json` |
| `proctor report <exam>` | the exam's current attempts, its `live.json` | `reports/<exam>/explore/`, an earlier report moved aside |
| `proctor live <exam> [variant]` | `exams/<exam>/`, its `live.json` | `exams/<exam>/live.json` when given a variant; else prints which is live |
| `proctor list` | `exams/`, every batch | nothing; prints each variant's current attempts against those wanted, the stale ones, an interrupted batch |

`--batch`, `--variant` and `--tasks` narrow `grade`, `friction` and
`report`; `--variant` and `--tasks` narrow `run`. `report` is a pure
function of the attempts it reads and can be re-run after any regrade.

> Never built, from the first draft: `proctor judge` (model checks are
> graded by `grade`), `proctor archive` and `proctor fetch` (the bucket,
> the archive url), and `list`'s cost estimate.

## Decisions taken here

- **Coordinates in the path, random id in the manifest.** Not one or the
  other. Continuing a batch needs the path key; joins need the id.
- **No content-addressed input store in the first version.** The research
  recommended it so N attempts do not carry N copies of shared inputs. A
  resolved `program.nb` is a few kilobytes and a task's fixture is a git
  revision, not a copy. Revisit when an attempt routinely carries something large
  that is identical across attempts; the trigger is `du` showing duplicated
  megabytes, not a principle.
- **`failed` is infrastructure; a bad transcript is `completed`.** The
  evidence decides, not the runner.
- **Config is never snapshotted; provider identity is recorded by name and
  endpoint.** Keys cannot leak from a file that does not exist.
- **Statistics are computed once, in .NET, into `stats.json`.** The markdown
  and the site render it; neither recomputes.
- **Built-in checks over nb's fields, scripts only for the repo-specific
  remainder.** The first draft made every check a script; the promptfoo pass
  showed that reproduces the scaffolding problem one level down.
- **A pass is a named subset of binary checks.** No weights, no thresholds,
  no averaged scores.

## Open questions

- **Normalising real-CLI transcripts.** The `command` runner captures raw
  CLI output as `transcript.txt`. The checks can grade from the diff and the
  build, so this is enough for the worked batch. Whether proctor should
  also emit a normalised `transcript.jsonl` for those variants, so the judge's
  window extraction works uniformly, is deferred until a judged criterion
  needs to read a Codex or Claude Code transcript.
- **Where fixture checkouts live during an attempt.** Under the attempt directory
  is simplest and makes `archive` upload them, which is wrong. Under a
  gitignored `.proctor/work/` beside `attempts/`, deleted on teardown, is the
  likely answer, with the teardown hook deciding what to keep (the diff).
- **Whether a report should copy `batch.json`.** Settled by the pool: a
  report reads several batches, so it copies none, and `stats.json`
  `built_from` names them.

## Not in this plan

The JSON schema of `stats.json` and the site's index (`todo.md`, "Define the
JSON the site reads"). The grader contract and the report skeleton, which
have their own plans. The archive transport (rclone versus a native client),
which is an implementation choice inside `archive`.
