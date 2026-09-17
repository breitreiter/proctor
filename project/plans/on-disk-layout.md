---
type: plan
title: The on-disk layout of an experiment
created: 2026-09-17
status: draft
---

# The on-disk layout of an experiment

The first of the design plans the research pass made obvious. Everything else
in proctor reads or writes this layout: the runner fills it, the grader adds to
it, the reporter derives from it, and `archive` and `fetch` move part of it.
So it goes first, and it is written as a worked example of the experiment in
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
| `evals/` | definition | yes | the human |
| `runs/` | raw | no, gitignored | the runner, grader and judge |
| `reports/` | derived | yes | the reporter, never by hand |

`evals/` is the sidecar the brief asks for. `runs/` is what `archive` uploads.
`reports/` is what the site bundle reads and what CI posts.

The root names are fixed. A repo that wants them elsewhere sets paths in
`evals/proctor.json`, but the default has to work with no configuration, and
a reader should be able to guess where things are.

## The definition tier: `evals/`

One eval is one behaviour under test, in its own directory. Cases are data
beside it. This is the JUnit `@CsvFileSource` shape from the test-framework
learnings, and the "one file, one thing" rule from lore.

```
evals/
  proctor.json                  repo-level: archive location, default arms, tool versions
  code-change/                  one eval: "can the agent make this code change"
    eval.json                   arms, samples, tags, hooks, grading
    program.nb                  the nb program template; {{case}} placeholders
    cases/
      add-retry-flag.json       one case: which fixture, what prompt, what "done" means
      fix-null-deref.json
      rename-module.json
    rubric.md                   judged criteria, if any; absent means deterministic only
    checks/                     deterministic graders, one script per named check
      builds.sh
      tests-pass.sh
      diff-in-scope.sh
```

`eval.json` for the experiment in the brief:

```json
{
  "id": "code-change",
  "tags": ["deterministic", "judged"],
  "arms": [
    { "id": "floor",  "runner": "nb",      "harness": "nb",          "provider": "imp-qcoder", "model": "qwen-coder", "samples": 3 },
    { "id": "codex",  "runner": "command", "harness": "codex",       "model": "gpt-5",   "samples": 1,
      "command": "codex exec -m gpt-5 --skip-git-repo-check {{prompt}}" },
    { "id": "claude", "runner": "command", "harness": "claude-code", "model": "sonnet-5", "samples": 1,
      "command": "claude -p {{prompt}} --model sonnet --allowedTools 'Bash(*)'" }
  ],
  "hooks": {
    "arm":    { "setup": "hooks/start-fakes.sh", "teardown": "hooks/stop-fakes.sh" },
    "sample": { "setup": "hooks/reset-fixture.sh", "teardown": "hooks/collect-diff.sh" }
  },
  "grading": {
    "checks": ["builds", "tests-pass", "diff-in-scope"],
    "judge":  { "provider": "cf-glm", "criteria_from": "rubric.md", "window": "last-assistant+diff" }
  }
}
```

Points to notice:

- **Arms are a declared list, not a cartesian product.** Each arm names its
  runner, harness, model and sample count. The mixed-runner shape (nb for the
  floor, real CLIs for the rest) is the brief's open question made explicit
  and recorded per arm. Proctor never compares a costume arm to a real-CLI arm
  without the manifest showing which is which.
- **Hooks are the project's scripts, run at a named level.** Proctor's
  contract is the order (run, arm, case, sample; teardown in reverse) and the
  recording. The fixture reset lives in the sample-level setup because a case
  here is a repository that the run mutates.
- **Tags are registered.** `deterministic` and `judged` are built in and
  drive the PR-versus-nightly split. Unknown tags are an error, as with
  pytest's strict markers.
- **Grading is declared, not discovered.** Checks are named scripts under
  `checks/`. The judge is named by provider and reads a declared window, never
  the whole transcript.

A case:

```json
{
  "id": "add-retry-flag",
  "fixture": { "git": "https://github.com/org/fixture-a", "rev": "3f2c1e9" },
  "prompt": "Add a --retry <n> flag to the fetch command. Existing tests must pass.",
  "expect": { "files_touched": ["src/fetch.cs", "tests/FetchTests.cs"] }
}
```

The case id is the stable identity; it is a path segment in `runs/` and a
column in every table. Renaming a case is a new case. The fixture is pinned
to a revision so the case means the same thing next month.

## The raw tier: `runs/`

One directory per experiment, cells addressed by their coordinates, one file
per concern inside a cell so `ls`, `cat`, `jq` and `grep -l` are the query
language.

```
runs/
  20260917-1432-code-change-k7px/           experiment id: date, eval, 4 random chars
    experiment.json                         the resolved matrix and provenance (below)
    status.json                             counts by cell status; rewritten as cells finish
    hooks/
      run.setup.log                         output of the run-level hooks
    floor/                                  arm
      hooks/
        arm.setup.log
        arm.teardown.log
      add-retry-flag/                       case
        1/                                  sample
          manifest.json                     random run id, coordinates, timing, versions
          status                            one word: pending | running | completed | failed | skipped
          program.nb                        the program exactly as run, placeholders resolved
          transcript.jsonl                  nb's output, untouched
          stderr.txt
          hooks/
            sample.setup.log
            sample.teardown.log
          diff.patch                        whatever the teardown hook chose to collect
          checks.json                       deterministic grading, written by `proctor grade`
          verdicts/
            cf-glm.rubric-a1b2c3.json       judge phase, one file per judge and rubric version
        2/
        3/
      fix-null-deref/
      rename-module/
    codex/
      add-retry-flag/
        1/
          manifest.json
          status
          prompt.txt                        what the command runner passed
          transcript.txt                    the CLI's raw output; format named in the manifest
          ...
    claude/
```

**Experiment id.** The trackers all warned against timestamp-only names and
against parameter-derived ids. This takes both halves: a date prefix so `ls`
sorts chronologically, the eval id so a human can tell experiments apart, and
four random characters so two experiments started in the same minute do not
collide. The random part is what makes it an id; the rest is a courtesy.

**Cell path is coordinates.** `<arm>/<case>/<sample>/` is the compound id from
the test-framework learnings, and it is deliberately not random: it is the
key `resume` uses. The random id lives inside the manifest.

**`status` is a one-word file** so `grep -l completed runs/*/*/*/*/status`
answers "what finished" without parsing anything. The five values are the
Sacred-style enum. `failed` means infrastructure (the runner could not
produce a transcript, or a hook failed); a run that produced a transcript
whose exit reason is `provider_error` is `completed`, because the transcript
is the evidence and grading decides what it means. This distinction is what
lets retries target infrastructure only.

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

The cell manifest:

```json
{
  "run_id": "b7e2f9c04d1a4e6b",
  "experiment": "20260917-1432-code-change-k7px",
  "arm": "floor", "case": "add-retry-flag", "sample": 1,
  "runner": "nb", "harness": "nb", "provider": "imp-qcoder", "model": "qwen-coder",
  "transcript": { "file": "transcript.jsonl", "format": "nb-jsonl" },
  "started": "2026-09-17T14:33:02Z", "ended": "2026-09-17T14:51:40Z", "duration_ms": 1118000,
  "host": "imp",
  "versions": { "proctor": "0.1.0", "nb": "0.9.0" },
  "eval_hash": "sha256:9c1e…", "program_hash": "sha256:41ab…",
  "status": "completed", "attempts": 1
}
```

The experiment manifest holds what is the same for every cell, so cells do
not repeat it: the resolved `eval.json`, the git commit and dirty flag of the
repository under test, the command line, the proctor and nb versions, the
planned cell count, and the archive location once `archive` has run. This is
Hydra's triple (resolved config, what was typed, tool runtime) plus Inspect's
provenance fields, in one file.

## The derived tier: `reports/`

What the reporter writes, what the site reads, what gets committed. Every
number that appears anywhere is computed here, in .NET, once. The browser and
the markdown renderer both read these files and neither computes anything.

```
reports/
  data/
    index.json                              every experiment: id, eval, date, headline, verdict, archive url
    20260917-1432-code-change-k7px/
      experiment.json                       the raw-tier manifest, verbatim (it has no secrets)
      results.jsonl                         one row per cell: coordinates, status, exit_reason, trailer fields, check results, verdict labels, run_id
      stats.json                            per-arm rates with intervals, pairwise differences, MDE sentence, methods footnote
      summary.md                            the markdown rendering, for CI and humans
```

`results.jsonl` carries the coordinates and the run id on every row, which is
the oracle bench's `--variant` lesson generalised: a row can be joined to
anything without opening another file. It carries no transcript text. The
judge's evidence quotes are in the raw-tier verdict files and reachable by
run id through `fetch`.

The site bundle is not in `reports/`. It is a versioned asset of proctor,
copied beside `reports/data/` at deploy time, so `reports/` stays pure data
and a bump of the bundle never touches a data commit.

## The verbs, and what each touches

| verb | reads | writes |
|---|---|---|
| `proctor run <eval>` | `evals/<eval>/` | a new `runs/<id>/`, every cell |
| `proctor resume <id>` | `runs/<id>/` | cells whose `status` is not `completed` |
| `proctor grade <id>` | `runs/<id>/`, `evals/<eval>/checks/` | `checks.json` in each completed cell |
| `proctor judge <id>` | `runs/<id>/`, `rubric.md` | `verdicts/<judge>.<rubric-hash>.json` where absent |
| `proctor report <id>` | `runs/<id>/` | `reports/data/<id>/`, `index.json` |
| `proctor archive <id>` | `runs/<id>/` | the bucket; the archive url into both manifests |
| `proctor fetch <run-id>` | the bucket, `index.json` | one cell back under `runs/` |
| `proctor list` | `evals/` | nothing; prints cells and a cost estimate |

`run` is `resume` on an empty directory. `judge` is idempotent per judge and
rubric hash, which is how a nightly judged pass can run against transcripts
the PR pass produced. `report` is a pure function of `runs/<id>/` and can be
re-run after any regrade.

## Decisions taken here

- **Coordinates in the path, random id in the manifest.** Not one or the
  other. Resume needs the path key; joins need the id.
- **No content-addressed input store in the first version.** The research
  recommended it so N samples do not carry N copies of shared inputs. A
  resolved `program.nb` is a few kilobytes and a case's fixture is a git
  revision, not a copy. Revisit when a cell routinely carries something large
  that is identical across samples; the trigger is `du` showing duplicated
  megabytes, not a principle.
- **`failed` is infrastructure; a bad transcript is `completed`.** The
  evidence decides, not the runner.
- **Config is never snapshotted; provider identity is recorded by name and
  endpoint.** Keys cannot leak from a file that does not exist.
- **Statistics are computed once, in .NET, into `stats.json`.** The markdown
  and the site render it; neither recomputes.

## Open questions

- **Normalising real-CLI transcripts.** The `command` runner captures raw
  CLI output as `transcript.txt`. The checks can grade from the diff and the
  build, so this is enough for the worked experiment. Whether proctor should
  also emit a normalised `transcript.jsonl` for those arms, so the judge's
  window extraction works uniformly, is deferred until a judged criterion
  needs to read a Codex or Claude Code transcript.
- **Where fixture checkouts live during a run.** Under the sample directory
  is simplest and makes `archive` upload them, which is wrong. Under a
  gitignored `.proctor/work/` beside `runs/`, deleted on teardown, is the
  likely answer, with the teardown hook deciding what to keep (the diff).
- **Whether `reports/data/<id>/experiment.json` should be a copy or a
  pointer.** A copy makes the site self-contained after `archive` removes
  the raw tier locally; a pointer avoids duplication. Copy, until the file
  grows.

## Not in this plan

The JSON schema of `stats.json` and the site's index (`todo.md`, "Define the
JSON the site reads"). The grader contract and the report skeleton, which
have their own plans. The archive transport (rclone versus a native client),
which is an implementation choice inside `archive`.
