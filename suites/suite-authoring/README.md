# suite-authoring

Can a coding agent write a proctor suite, given proctor's documentation? This
suite asks it to, three times, and grades each written suite by what the suite
does rather than how it reads: it runs the written suite against planted runs
of its own task, one correct and several wrong, and a written suite passes
only if it passes the correct run and fails every wrong one.

It is also proctor's demo project. Between them the files here use most of
what proctor has, and `report/` holds the report of the latest run, so you
can see the output without running anything.

## What runs

| | |
|---|---|
| agent | nb with the local qwen3-coder model, in a container (`../runners/container.sh`) |
| arms | `docs`: the README and the layout plans; `docs-and-examples`: the same, with the example suites and fixtures beside them |
| tasks | three workspaces, each asking for one suite (below) |
| samples | 3 per arm and task: 18 runs |

Both arms pin the same proctor revision as their `bundle`. `hooks/arm-setup.sh`
builds proctor from it and prepares the arm's *view*: the files the agent sees at
`/bundle`. The only difference between the arms is whether the examples are in
the view, so comparing them tests a theory about how agents use documentation:
that an agent reads a worked example alongside the structured docs and borrows
from it. `read-examples` is the rate at which it does.

| task | workspace | the suite must get right |
|---|---|---|
| `guard-the-tests` | `fixtures/authoring-ledger`: a complete fixture with a failing test | a scope guard, so a run that edits the test to make it pass fails |
| `check-the-answer` | `fixtures/authoring-stock`: a stock sheet and a question | the answer in `expect` read through `@expect`, matched exactly (`ANSWER: 18` and `ANSWER: 80` are wrong for 8), and a read-only guard |
| `write-the-fixture` | `fixtures/authoring-widgets`: a bare repository | a `fixture.json` with its checks beside `repo/`, and a script check, because the library's tests pass before the rename too |

## How a written suite is graded

`checks/trial.sh` runs `trial/run.sh` in a container with no network. The run
copies the workspace, replaces the written suite's arms with one per planted run
in `trial/<task>/` (`good-*` and `bad-*`), and swaps its hooks and runner for
`trial/planted.sh`. That runner applies the planted change to the checkout and
has nb's Mock provider make one tool call and give the planted closing message,
so the transcript is nb's own. Proctor then runs, grades and reports that inner
experiment, and `accepts-correct` and `rejects-wrong` read its `results.jsonl`.
A model check in the written suite is dropped, because nothing in the trial can
answer it. If the written suite names a model check in `pass` or `validity`, the
trial fails, since without a model no run could be decided. The scripts the
agent wrote run only inside that container, never on the host. The inner
experiment stays in the cell under `trial/` as evidence.

| check | kind | what it says |
|---|---|---|
| `exit_ok`, `validates`, `accepts-correct`, `rejects-wrong` | pass | nb finished; `proctor list` loads the suite cleanly; it passes the correct run; it fails every wrong one |
| `stays-in-work` | validity | the agent stayed in its checkout and the bundle (shared with code-change) |
| `self-validated`, `describes-everything`, `read-examples` | guardrail | it ran `list` itself; every id carries a description; it opened an example |
| `stance` (`decide`), `prompt-hides-answer` (`judge`) | guardrail | the closing message claims completion; the task prompt it wrote does not give away the answer |

`reference/` holds a good answer for each task: three small suites that also serve
as examples of the practices above. `reference/shakedown.sh` proves the trial
without a model. The references must pass every check. With `--naive`, each
reference loses the check that made it discriminate, and `rejects-wrong` must name
the wrong runs that got through. `trial/` and `reference/` are never shown to the
agent.

## Run it

```bash
export LLM_GATEWAY=... LLM_GATEWAY_KEY=...     # the gateway suites/nb.json and suites/proctor.json name
dotnet build                                   # the trial grades with this build
suites/suite-authoring/reference/shakedown.sh    # optional: the trial against the references, ~2 min
proctor run suite-authoring
proctor grade <id>
proctor report <id>
cp reports/data/<id>/{report.html,summary.md} suites/suite-authoring/report/
```

`--runner none` is only a shakedown here. It skips the views, so the agent sees
the whole clone, answer key included, and gets no proctor build.

To test a documentation change, push it, add an arm whose bundle pins the new
revision, and compare the two in one experiment.

Both arms still pin a revision from before the suite/task rename
(2026-09-23), so the agent they show reads `evals/` and `cases/` and writes
that layout; the trial and the checks here accept it, and proctor reads it,
for one release. `report/` is from that revision too and says eval and case.
Bump the bundle revisions once the rename is pushed, then rerun and replace
`report/`.
