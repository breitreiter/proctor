---
type: plan
title: Fixtures, arm bundles and baselines — the two tiers that move at different rates
created: 2026-09-21
status: built 2026-09-21; see "As built" at the end
---

# Fixtures, arm bundles and baselines

The second design plan. It changes the model in
[on-disk-layout.md](on-disk-layout.md) in four places, all forced by
[../learnings/two-modes-and-baselines.md](../learnings/two-modes-and-baselines.md)
and by a live case: a repository with its grading instructions that moves
rarely and is reused, and a set of instructions and tools for getting a job
done in that repository that changes every week. Those are two artifacts,
not one. Mushed together, every change to the instructions makes a new
repository and nothing keeps its meaning across time; held apart, the two
modes of working fall out directly.

| tier | what it is | moves | proctor's word |
|---|---|---|---|
| the repository and what "done" looks like in it | the thing measured against | rarely | **fixture** |
| the instructions and tools under test | the thing measured | often | **arm**, pinned by its **bundle** |

Everything else in this plan follows from keeping those two apart: a
baseline pins cells that ran one bundle version against fixtures that have
not changed, and a guard report compares the next bundle version to it.

The word "experiment" is not renamed. The interview's "experiment" is our
arm; ours stays one execution of an eval across the matrix, which is what
every file under `runs/` already says.

Written as a worked example in the style of the layout plan: the code-change
eval as it will look, then the diff to the model, then the build order.

## The worked example

```
fixtures/                                 repo-level, beside evals/: reused across evals
  fetchcli/
    fixture.json                          identity, source, stack, what done looks like
    checks/
      builds.sh
      tests-pass.sh
    repo/                                 the checkout source; only this is copied into the work dir
      src/ tests/ README.md ...
  ledger/
    fixture.json
    checks/ ...
    repo/ ...
  ...
evals/
  code-change/
    eval.json
    program.nb
    baseline.json                         pinned cells per case; committed; written by `proctor baseline`
    cases/
      add-retry-flag.json                 names a fixture, adds the goal and what it asks for
    acceptance/
      add-retry-flag/*.cs                 case-owned acceptance tests, as today
    checks/
      stays-in-work.sh                    eval-owned checks: what no fixture knows about itself
    hooks/
      ensure-model.sh
bundles/                                  optional: a bundle that lives in this repo; usually it does not
  exporter-v3/
```

`fixtures/fetchcli/fixture.json`:

```json
{
  "id": "fetchcli",
  "source": { "path": "repo" },
  "stack": "dotnet",
  "checks": {
    "builds":     { "script": "checks/builds.sh" },
    "tests-pass": { "script": "checks/tests-pass.sh" }
  }
}
```

A fixture with a remote source: `"source": { "git": "https://…/fixture-a", "rev": "3f2c1e9" }`.
Either way the fixture's identity is a hash: the content hash of `repo/`,
or the revision. It goes into every cell manifest.

The checker lives beside the source, not inside it. This is the layout
answer to the interview's one observed cheat, which was an oracle file one
directory above the codebase: proctor copies `repo/` and nothing else into
the work directory, so the checks and the expectations are never reachable
from inside it by construction. What remains reachable is the repository
root far above the checkout, and that is what a validity check is for.

A case, `evals/code-change/cases/add-retry-flag.json`:

```json
{
  "id": "add-retry-flag",
  "fixture": "fetchcli",
  "prompt": "Add a `--retry <n>` flag to fetchcli. …",
  "expect": {
    "files_touched": { "paths": ["src/FetchCli/**", "tests/FetchCli.Tests/**", "README.md"], "mode": "at_most" }
  }
}
```

The case names its fixture and says what it is asking for. The fixture
knows whether the repository builds and its tests pass; the case knows
which files the request should touch. A fixture may carry a default
`expect` block that a case's block overlays key by key, for expectations
that belong to the repository rather than to any one request.

`evals/code-change/eval.json`:

```json
{
  "id": "code-change",
  "tags": ["deterministic"],
  "arms": [
    { "id": "floor", "runner": "nb", "harness": "nb", "provider": "imp-qcoder", "model": "qwen3-coder-next", "samples": 3 },
    { "id": "v3",    "runner": "nb", "harness": "nb", "provider": "imp-qcoder", "model": "qwen3-coder-next", "samples": 3,
      "bundle": { "git": "git@…/exporter-instructions", "rev": "8a1f02c" } }
  ],
  "hooks": {
    "arm": { "setup": "hooks/ensure-model.sh" }
  },
  "grading": {
    "checks": {
      "exit_ok":       { "exit_reason": "ok" },
      "no_denials":    { "denied_calls": { "max": 0 } },
      "not_nudged":    { "loop_nudged": false },
      "under_budget":  { "max_tool_calls": 60, "max_duration_ms": 1800000 },
      "stays-in-work": { "script": "checks/stays-in-work.sh" },
      "diff-in-scope": { "files_touched": "@expect" }
    },
    "pass":     ["exit_ok", "builds", "tests-pass"],
    "validity": ["stays-in-work"]
  }
}
```

Three things changed against today's file:

- **No sample hooks.** Proctor materialises the fixture into the work
  directory before the sample setup hook and collects the diff after the
  sample teardown hook. The two scripts that did that were the same in every
  eval, which is the sign they were proctor's job. Sample hooks remain for
  what is specific: starting a fake service, seeding a database.
- **`builds` and `tests-pass` are not declared here.** They are fixture
  checks, declared once per fixture in whatever way that stack needs, and
  merged into the check set of every cell that runs on that fixture. `pass`
  may name them. Validation requires every fixture a case names to declare
  every `pass` check the eval does not declare itself; a name declared in
  both places is a problem.
- **`validity` is a third list.** A check named there whose verdict is
  `fail` makes the sample invalid: excluded from analysis with a reason, the
  way a failed cell is excluded, and reported in the accounting. It is the
  interview's anti-cheat check and its cost is one glance at the exclusions
  column. A check is one of three kinds by where it is named: in `pass` it
  is capability, in `validity` it decides whether the sample counts, in
  neither it is a guardrail rate. `error` never invalidates a sample; it is
  reported as an error, as now.

The `tool_calls` vocabulary is untouched. The interview's finding that
scoring the transcript punishes novelty is a finding about what to put in
`pass`, and code-change already puts only outcomes there. The vocabulary's
job is the two other kinds.

### The arm's bundle

An arm's `bundle` is the pinned version of the thing under test, in the
same two forms as a fixture's source: a path relative to the repository
root, or a git URL and revision. Proctor resolves it once per arm, hashes
it, makes it available to the program template as `{{bundle}}` and to every
hook and check as `PROCTOR_BUNDLE` (an absolute path to the resolved
directory), and records it in the experiment manifest under the arm and in
every cell manifest.

What is inside a bundle is not proctor's business. The live case has a
seed prompt, a documentation snapshot and tool definitions; the program
template and the arm setup hook know what to do with them. Proctor's
guarantee is identity: two experiments whose manifests show the same bundle
hash ran the same instructions, and `resume` refuses an arm whose bundle no
longer resolves to the hash the experiment recorded.

Two arms can name two bundles, which is explore mode in one experiment.
One arm can name today's bundle and be compared to a baseline that pinned
last week's, which is guard mode.

An arm without a bundle is what every arm is today: a harness, a provider
and a model. Nothing existing changes shape.

### The baseline

`evals/code-change/baseline.json`, written by `proctor baseline`, committed
with the eval:

```json
{
  "set": "2026-09-21T16:40:12Z",
  "command": "proctor baseline 20260921-1128-code-change-epoc --arm v3",
  "eval_hash": "sha256:9c1e…",
  "cases": {
    "add-retry-flag": { "experiment": "20260921-1128-code-change-epoc", "arm": "v3", "samples": [1, 2, 3],
                        "bundle": "sha256:41ab…", "fixture": "sha256:c07d…", "score": 0.667 },
    "fix-null-deref": { "experiment": "20260921-1128-code-change-epoc", "arm": "v3", "samples": [1, 2, 3],
                        "bundle": "sha256:41ab…", "fixture": "sha256:7e21…", "score": 1.0 },
    "rename-module":  { "experiment": "20260918-2210-code-change-b4kq", "arm": "v2", "samples": [1, 2, 3],
                        "bundle": "sha256:19f0…", "fixture": "sha256:0a9c…", "score": 0.333 }
  }
}
```

The baseline pins cells: an experiment, an arm and the samples, per case.
It also records the score those cells had when pinned, the eval hash they
were graded under and the identities of the bundle and the fixture, so the
guard report can say in one line what it is comparing against.

Per case, so `proctor baseline <experiment> --arm v3 --cases add-retry-flag`
re-baselines one case and leaves the rest pinned where they were, and adding
cases for a new feature does not reset the old ones. The default is every
case the arm analysed; a case the arm has no analysed sample for keeps its
old pin and the command says so.

The cells are the truth and the score is a cache. When `runs/<experiment>`
is present, `report` re-reads the pinned cells' `checks.json`, so a regrade
of the baseline experiment flows into the next guard report. When the raw
tier is gone, `report` uses the recorded scores and the report's baseline
line says "scores as pinned", not "recomputed". Retro-grading old cells with
a new check is `grade` on the old experiment followed by `report`, and the
interview says that is the exception, so no verb is added for it.

### The guard report

When the eval has a baseline, `report` loads the pinned cells as one more
arm named `baseline` and compares every declared arm to it with the same
paired Newcombe difference the arms already get against each other. The
baseline is not an arm in the experiment: it is not in the accounting table,
not in the matrix, and it has no exit reasons. It appears in one new section
and one new block of `stats.json`:

```json
"baseline": {
  "set": "2026-09-21T16:40:12Z", "scores": "recomputed",
  "tolerance_points": 10,
  "arms": {
    "v3": { "n_pairs": 3, "diff_points": -11, "ci95": [-48, 29], "won": 0, "lost": 1, "tied": 2,
            "verdict": "regressed", "cases": { "add-retry-flag": { "baseline": 0.667, "arm": 0.333 }, … } }
  }
}
```

`verdict` is `held`, `improved` or `regressed`, on the point estimate
against `--tolerance <points>` (default 0). The interval is printed beside
it because at three cases it will always include zero, and the MDE sentence
already says how many cases a ten-point difference needs. The report does
not hide that; guard mode at small n is a smoke alarm, not a measurement,
and the way to make it a measurement is more cases on the cheap local arm,
which the economics already favour.

`proctor report <id> --fail-on regression` exits 1 when any arm's verdict is
`regressed`, which is the CI shape from `learnings/prior-art/ci.md`. The
markdown summary gets the same section, one line per arm, for the PR
comment.

## The diff to the model

| where | today | after |
|---|---|---|
| `fixtures/` | inside each eval, copied per eval | repo-level, one directory per fixture, `fixture.json` + `checks/` + `repo/` |
| case `fixture` | an object with `path` or `git`/`rev` | a string naming `fixtures/<name>` |
| `expect` | on the case only | fixture default, case overlay |
| checks | all on the eval | eval checks plus the named fixture's checks, merged per cell |
| `grading` | `checks`, `pass` | `checks`, `pass`, `validity` |
| arm | runner, harness, provider, model, samples | plus optional `bundle` |
| placeholders | prompt, case, work, provider, model, harness, arm, sample | plus `bundle` |
| env | `PROCTOR_*` as documented | plus `PROCTOR_BUNDLE`, `PROCTOR_FIXTURE` (the fixture's directory, for checks) |
| sample lifecycle | setup hook, nb, teardown hook | materialise fixture, setup hook, nb, teardown hook, collect diff |
| cell manifest | as today | plus `fixture: {id, hash}`, `bundle: {source, hash}` |
| experiment manifest | as today | plus resolved bundles per arm |
| eval hash | eval files, cases, scripts, hooks | plus each named fixture's `fixture.json` and check scripts (not `repo/`; that is the fixture hash, recorded per cell) |
| exclusions | failed, skipped, not graded | plus `invalid: <check>: <reason>` |
| `stats.json` | arms, comparisons | plus `baseline` |
| verbs | list, run, resume, grade, report | plus `baseline` |
| report | seven sections | plus "Against baseline" when a baseline exists; exclusions show validity failures |

## Build order

Each step builds, tests pass, and the code-change eval still runs end to
end on the Mock provider before the next starts. Snapshots are re-approved
where a rendering changes, and reviewed first.

1. **Validity checks.** `grading.validity`, the exclusion path in
   `Results.cs` and `Stats.cs`, the exclusions in the report. Smallest
   change, no layout change, and the accounting column it adds is what the
   later steps report into.
2. **Fixtures first-class.** `fixtures/` root, `fixture.json`, the case's
   string reference, the merged check set, `PROCTOR_FIXTURE`, the fixture
   hash in the manifest, proctor materialising the checkout and collecting
   the diff. Move the three code-change fixtures and their two checks; drop
   the two sample hooks and `lib.sh`'s rebuild path, which becomes
   proctor's: a regrade with no checkout materialises the fixture and
   applies `diff.patch` itself. The smoke eval gets a trivial fixture.
3. **Arm bundles.** The field, the resolution, the hash, `{{bundle}}` and
   `PROCTOR_BUNDLE`, the manifests, `resume`'s refusal. The smoke eval gets
   an arm with a path bundle so the placeholder and the hash are tested.
4. **`proctor baseline`.** The verb and the file, with `--arm` and `--cases`.
5. **The guard report.** The virtual arm, the `baseline` block in
   `stats.json`, the section in both renderings, `--tolerance` and
   `--fail-on`. New snapshot fixtures: the worked experiment with a baseline.
6. **A validity check that earns its place.** `stays-in-work.sh` in
   code-change: a script over `PROCTOR_TRANSCRIPT` that fails when any tool
   call's arguments reach outside `PROCTOR_WORK`. This is the first honest
   use of the transcript in an eval that otherwise grades final state.
7. **Docs.** CLAUDE.md, README, the layout plan's tree and its check table,
   `learnings/README.md` if a learning came out of it.

Steps 1, 2 and 3 are independent of each other in principle but touch the
same records, so they go in that order. Steps 4 and 5 need 2 and 3 for the
hashes the baseline records, but the file format does not depend on them.

## Decisions taken here

- **Two artifacts, two tiers, never one.** The fixture is the case's; the
  bundle is the arm's. What moves rarely is measured against; what moves
  often is measured.
- **The checker lives beside the checkout, never in it.** `repo/` is the
  only thing copied. The layout prevents the one cheat the interview saw.
- **Proctor materialises fixtures and collects diffs.** Two hooks that were
  identical in every eval were proctor's job.
- **Check kind is where the check is named.** `pass`, `validity`, or
  neither. No `kind` field on the check itself.
- **A baseline pins cells and caches scores.** The cells are the truth when
  present; the cache is what survives archiving, and the report says which
  it used.
- **The baseline is a virtual arm in the statistics, not in the
  experiment.** The paired machinery is reused unchanged; the accounting is
  not polluted.
- **"Experiment" keeps its meaning.** Our word for the interview's
  experiment is "arm".
- **No retro-grade verb.** `grade` on an old experiment plus `report` is
  the exception path, and it already works.

## Open questions

- **Where bundles live when they are in this repository.** `bundles/` at
  the root is the guess; the live case's bundle is another repository, so
  the git form is the one that gets exercised first.
- **Whether a bundle should be able to carry checks**, the way a fixture
  does. A bundle that knows how to verify its own installation is
  plausible, and it would make the bundle a peer of the fixture rather than
  an opaque input. Not until a bundle asks for it.
- **Baseline per arm or per eval.** This plan pins one baseline per eval,
  and every arm is compared to it. A repo running two bundles side by side
  for months might want one baseline each. The file format allows it later
  (a `baselines/` directory keyed by name); the verb does not offer it yet.

## Not in this plan

Generalising collected artifacts beyond `diff.patch` (script checks already
read anything in the cell; a second built-in over a second artifact is the
trigger). The `command` runner. The judge. The site.

## As built (2026-09-21)

Seven commits, one per step, each leaving the suite green and the
code-change shakedown correct on both sides. What differs from the plan
above:

- **Acceptance tests became their own check.** The old `tests-pass`
  copied the case's acceptance tests into the fixture's suite. Split by
  ownership, `tests-pass` is the fixture's (does its suite still pass) and
  `acceptance` is the eval's (do the case's tests pass), both in `pass`.
  Unsolved, each case now fails in the check that owns the reason.
- **The check kinds needed no accounting change.** A validity failure is
  one more exclusion beside `failed`, with `invalid` as its status word in
  the exclusions list and the matrix. A validity check's own rate is over
  every graded sample, so it says how many counted.
- **`stays-in-work` looks only at path-like arguments.** The first draft
  scanned every argument and flagged `GET /summary` inside a file the
  agent wrote. Content, edit strings and descriptions are prose; the check
  reads the rest. Zero false positives on the 27 bench transcripts.
- **Bundles are hashed with `.git` excluded and nothing else.** A bundle
  in this repository is a directory of instructions, not a build tree; if
  one ever carries build output the fixture's `exclude` list is the shape
  to copy.
- **The baseline's `resolve` word has three values**, not two: `recomputed`,
  `as pinned`, and `partly recomputed` when some pinned experiments are
  gone and some are not.

The open questions stand. The next thing the guard report needs is not
code: it is enough cases on the cheap local arm for a ten-point regression
to be more than a smoke alarm, and the MDE sentence says how many.

