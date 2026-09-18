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
    checks/                     scripts for what the built-in checks cannot say
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
    "checks": {
      "exit_ok":      { "exit_reason": "ok" },
      "no_denials":   { "denied_calls": { "max": 0 } },
      "not_nudged":   { "loop_nudged": false },
      "under_budget": { "max_tool_calls": 40, "max_duration_ms": 1800000 },
      "builds":       { "script": "checks/builds.sh" },
      "tests-pass":   { "script": "checks/tests-pass.sh" },
      "diff-in-scope":{ "script": "checks/diff-in-scope.sh" }
    },
    "pass": ["exit_ok", "builds", "tests-pass"],
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
- **Grading is declared, not discovered.** Checks are a named map. Most are
  one-line built-ins over a field nb already emits (the vocabulary below);
  scripts under `checks/` are for what the built-ins cannot say, which is the
  repo-specific trio here. `pass` names the subset that constitutes success;
  every other check is a guardrail rate in the report. The judge is named by
  provider and reads a declared window, never the whole transcript.

A case:

```json
{
  "id": "add-retry-flag",
  "fixture": { "git": "https://github.com/org/fixture-a", "rev": "3f2c1e9" },
  "prompt": "Add a --retry <n> flag to the fetch command. Existing tests must pass.",
  "expect": {
    "files_touched": { "paths": ["src/fetch.cs", "tests/FetchTests.cs"], "mode": "at_least" },
    "tools_used": ["bash"]
  }
}
```

The `expect` block is where a case supplies values to the eval's built-in
checks. `files_touched` has a mode, `at_least`, `exactly` or `at_most`, over
the diff the teardown hook collected; without a mode the check would have no
semantics, which was the state of this example before the promptfoo pass.

The case id is the stable identity; it is a path segment in `runs/` and a
column in every table. Renaming a case is a new case. The fixture is pinned
to a revision so the case means the same thing next month.

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
| `diff` | `diff.patch` in the cell, if the teardown hook wrote one |

The built-ins, each binary, each negatable with a `not_` prefix, values
supplied either in `eval.json` (same for every case) or in a case's `expect`
block (per case):

| check | window | shape |
|---|---|---|
| `exit_reason` | trailer | one of nb's exit reasons; `ok` is the implicit default check on every case |
| `answer_contains`, `answer_regex`, `answer_equals` | answer | string or pattern |
| `answer_json_schema` | answer_json | path to a schema file beside the case |
| `answer_words` | answer | `{min, max}` |
| `tools_used`, `tools_used_any` | tool_calls | list of tool names |
| `tool_args` | tool_calls | `{name, args, mode: partial \| exact, ignore: [globs]}`; `partial` means expected is a subset of actual |
| `tool_sequence` | tool_calls | `{names, mode: in_order \| exact}`; `in_order` allows gaps |
| `denied_calls` | tool_calls | `{max}` over `approved == deny`; `approval_reason == no-match` is "reached outside its surface" |
| `tool_errors` | tool_results | `{max}` |
| `loop_nudged` | user events | boolean; needs nb to tag the nudge (see `todo.md`, candidates for nb); until then matches the reminder text |
| `oracle_hit`, `oracle_misses`, `oracle_turns` | oracle_turns, trailer | keys, `{max}`, `{max}` |
| `files_touched` | diff | `{paths, mode: at_least \| exactly \| at_most}` |
| `max_tool_calls`, `max_tokens`, `max_duration_ms`, `max_cost` | trailer | number; cost needs a price on the provider entry until nb carries it on the trailer |

**A script is a check too**, declared as `{ "script": "checks/name.sh" }`.
It runs in the cell directory with the case's `expect` block in an
environment variable, exits 0, 1 or 2 for pass, fail, needs-judge, and its
first line of stdout is the reason. That is weaver's grader contract plus
promptfoo's custom-assertion habit of returning a reason beside the verdict,
so a matrix cell can show both without opening a log.

**What is deliberately absent.** Scalar metrics that hide two booleans
(`tool-call-f1` is `tools_used` plus `not_tools_used_any`); reference-text
similarity (ROUGE, BLEU, embeddings), because our cases have no reference
prose and overlap measures phrasing; pairwise or holistic judging; and score
averaging with weights and thresholds, because a pass is a named subset of
binary checks, not a weighted sum.

**Deferred: a classifier rung.** Between built-ins and the judge there is
room for a small fixed-weight classifier over the last assistant text, for
the one question that is neither regex nor worth a judge call: did the
closing message claim completion, ask, or report blocked. The oracle bench
calls the wrong answer to that "done-on-waiting" and names it the dangerous
number. It is deterministic for fixed inputs, cheap, and outside every arm's
family. It is not in the first version, and when it arrives it is held to
the judge's standard: label out, never a confidence; threshold in versioned
code; validated against one to two hundred human-labelled runs with kappa
reported; and a `needs-judge` fallback below the bar, not a replacement.

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
          checks.json                       {name: {verdict, reason}}, written by `proctor grade` (below)
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

**`checks.json` is a map from check name to verdict and reason.** The four
verdicts are `pass`, `fail`, `needs-judge` and `error`, where `error` means
the check itself could not run and is reported as such rather than folded
into `fail`. Every named check gets its own per-arm rate with an interval in
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
- **Built-in checks over nb's fields, scripts only for the repo-specific
  remainder.** The first draft made every check a script; the promptfoo pass
  showed that reproduces the scaffolding problem one level down.
- **A pass is a named subset of binary checks.** No weights, no thresholds,
  no averaged scores.

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
