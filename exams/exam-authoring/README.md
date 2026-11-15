# exam-authoring

Can a coding agent write a proctor exam, given proctor's documentation? This
exam asks it to, three times, and grades each written exam by what the exam
does rather than how it reads: it runs the written exam against planted attempts
of its own task, one correct and several wrong, and a written exam passes
only if it passes the correct attempt and fails every wrong one.

It is also proctor's demo project. Between them the files here use most of
what proctor has. The report of a batch goes in `report/` so you can see the
output without running anything; none is there
yet.

## What runs

The three task prompts were reworded on 2026-11-15 to the words they test:
"Write an exam with the id `stock-count`. ... It has one variant: nb with the
provider `local-qcoder` and the model `qwen3-coder-next`. Each task is
attempted three times."

| | |
|---|---|
| agent | nb with the local qwen3-coder model, in a container (`../runners/container.sh`) |
| variants | `variants/docs.json`: the README, the configuration and CLI references and two layout plans; `variants/docs-and-examples.json`: the same, with the example exams and fixtures beside them |
| tasks | three workspaces, each asking for one exam (below) |
| attempts | 3 per variant and task: 18 in all |

Both variants pin the same proctor revision as their `bundle`, and each names the
`paths` of that revision it shows: the README, `docs/configuration.md`, `docs/cli.md`
and two plans, `on-disk-layout` and `report-structure`; `docs-and-examples` adds every
example exam and fixture by name, leaving out this exam and its `authoring-*`
workspaces. What the agent reads is therefore declared in the variant's file and is
part of its hash. `hooks/variant-setup.sh` prepares the machine and does not branch on
the variant: it builds proctor at the bundle's revision and puts the build beside the
bundle in the *view*, the files the agent sees at `/work/.bundle`, read-only.

**The pin predates the rename, so this exam does not run yet.** The pinned
revision documents the layout before exam, variant and attempt and has no
`docs/` and no `exams/`, so the bundle's `paths` are not in it and `proctor run`
stops while resolving the bundle. The paths name the new layout, which is what
the agent should be shown; the pin moves, in both variant files and as a new
id for each (a changed variant is a new variant), to a pushed revision with the
new words. Moving the pin is a push, and the user's. The view sits inside the workspace because nb's file
tools (`read_file`, `grep`, `list_dir`) read only under the working directory;
mounted beside it, the documentation could be read only through `bash`. The only difference between the variants is whether the examples are in
the bundle, so comparing them tests a theory about how agents use documentation:
that an agent reads a worked example alongside the structured docs and borrows
from it. `read-examples` is the rate at which it does.

| task | workspace | the exam must get right |
|---|---|---|
| `guard-the-tests` | `fixtures/authoring-ledger`: a complete fixture with a failing test | a scope guard, so an attempt that edits the test to make it pass fails |
| `check-the-answer` | `fixtures/authoring-stock`: a stock sheet and a question | the answer in `expect` read through `@expect`, matched exactly (`ANSWER: 18` and `ANSWER: 80` are wrong for 8), and a read-only guard |
| `write-the-fixture` | `fixtures/authoring-widgets`: a bare repository | a `fixture.json` with its checks beside `repo/`, and a script check, because the library's tests pass before the rename too |

## How a written exam is graded

`checks/trial.sh` runs `trial/run.sh` in a container with no network. The trial
copies the workspace, replaces the written exam's `variants/` with one variant file per
planted attempt in `trial/<task>/` (`good-*` and `bad-*`), sets `attempts` to 1, and swaps its hooks and runner for
`trial/planted.sh`. That runner applies the planted change to the checkout and
has nb's Mock provider make one tool call and give the planted closing message,
so the transcript is nb's own. Proctor then runs, grades and reports that inner
batch, and `accepts-correct` and `rejects-wrong` read its `results.jsonl`.
A model check in the written exam is dropped, because nothing in the trial can
answer it. If the written exam names a model check in `pass` or `validity`, the
trial fails, since without a model no attempt could be decided. The scripts the
agent wrote run only inside that container, never on the host. The inner
batch stays in the attempt under `trial/` as evidence.

| check | kind | what it says |
|---|---|---|
| `exit_ok`, `validates`, `accepts-correct`, `rejects-wrong` | pass | nb finished; `proctor list` loads the exam cleanly; it passes the correct attempt; it fails every wrong one |
| `stays-in-work` | validity | the agent stayed in its checkout and the bundle (once shared with code-change, which runs through Harbor now) |
| `self-validated`, `describes-everything`, `read-examples` | guardrail | it ran `list` itself; the exam and every task carry a description and every variant a note; it opened an example |
| `stance` (`decide`), `prompt-hides-answer` (`judge`) | guardrail | the closing message claims completion; the task prompt it wrote does not give away the answer |

`reference/` holds a good answer for each task: three small exams that also serve
as examples of the practices above. `reference/shakedown.sh` proves the trial
without a model. The references must pass every check. With `--naive`, each
reference loses the check that made it discriminate, and `rejects-wrong` must name
the wrong attempts that got through. Nothing in this directory is shown to the agent:
its checks and `trial/` are the grader and `reference/` is the answer key, so
the examples variant sees the other exams but not this one.

## Run it

```bash
export LLM_GATEWAY=... LLM_GATEWAY_KEY=...     # the gateway exams/nb.json and exams/proctor.json name (copied from *.example.json)
dotnet build                                   # the trial grades with this build
exams/exam-authoring/reference/shakedown.sh    # optional: the trial against the references, ~2 min
proctor run exam-authoring
proctor grade <id>
proctor report <id>
cp reports/data/<id>/{report.html,summary.md} exams/exam-authoring/report/
```

`--runner none` is only a shakedown here. It skips the views, so the agent sees
the bundle without a proctor build.

To test a documentation change, push it, add a variant file whose bundle pins the new
revision, and compare the two in one batch.
