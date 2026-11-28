# exam-authoring

Can a coding agent write a proctor exam, given proctor's documentation? This
exam asks it to, three times, and grades each written exam by what the exam
does rather than how it reads: it runs the written exam against planted runs
of its own stage, one correct and several wrong, and a written exam passes
only if it passes the correct run and fails every wrong one.

It is also proctor's demo project. Between them the files here use most of
what proctor has: three axes, one of them a set of worlds; a value whose
files are a git revision; hooks, a container runner, script and judged
checks. The report goes in `report/` so you can see the output without
running anything; none is there yet.

## What runs

| | |
|---|---|
| axes | `workspace`: three exams repositories, each a fixture, each asking for one exam (below). `docs`: what of proctor's repository the agent can read. `assistant`: `qcoder`, nb on the local qwen3-coder model in a container (`../runners/container.sh`), and `haiku`, nb on Claude Haiku 5.5 |
| configurations | six named: each value of `docs` in each workspace, on `qcoder`. `haiku` is a hosted model, so no configuration names it until its spend is agreed |
| tries | 3 of each configuration: 18 in all, each the one stage of its workspace |

Both values of `docs` pin the same proctor revision as their `files`, laid
into the checkout at `.bundle/`, and each names the `paths` of that revision
it shows: `docs` the README, `docs/getting-started.md`,
`docs/configuration.md` and `docs/cli.md`; `docs-and-examples` adds every
example exam and fixture by name, leaving out this exam and its `authoring-*`
workspaces. What the agent reads is therefore declared in the value and is
part of its hash. `hooks/configuration-setup.sh` prepares the machine and
changes nothing the agent sees: it builds proctor at that revision, which
`hooks/try-setup.sh` mounts read-only at `/work/.bundle/proctor`, beside the
documentation.

**Moving the pin.** The revision must be pushed, since it is cloned from
GitHub. Once a value has made tries, moving its pin changes it, and a changed
value is a new value: give each a new name. The files sit inside the workspace
because nb's file tools (`read_file`, `grep`, `list_dir`) read only under the
working directory. The only difference between the two values is whether the
examples are there, so comparing them along `docs` tests a theory about how
agents use documentation: that an agent reads a worked example alongside the
reference and borrows from it. `read-examples` is the rate at which it does.

| stage | workspace | the exam must get right |
|---|---|---|
| `guard-the-tests` | `ledger`, `fixtures/authoring-ledger`: a complete fixture with a failing test | a scope guard, so a run that edits the test to make it pass fails |
| `check-the-answer` | `stock`, `fixtures/authoring-stock`: a stock sheet and a question | the answer in `expect` read through `@expect`, matched exactly (`ANSWER: 18` and `ANSWER: 80` are wrong for 8), and a read-only guard |
| `write-the-fixture` | `widgets`, `fixtures/authoring-widgets`: a bare repository | a `fixture.json` with its checks beside `repo/`, and a script check, because the library's tests pass before the rename too |

A fourth task, `continue-the-batch`, asked the agent to finish a run a crash
had interrupted. It was written for a layout two models back and never run;
there is nothing to continue now, since a lost try is simply made again, so
it went with its workspace and its checks on 2026-11-27 (they are in git).

## How a written exam is graded

`checks/trial.sh` runs `trial/run.sh` in a container with no network. The trial
copies the workspace, gives the written exam one more axis, `planted`, with a
value per planted run in `trial/<stage>/` (`good-*` and `bad-*`), names each of
its configurations once per plant, sets `tries` to 1, puts nb's Mock under
every provider name it uses, and swaps its hooks and runner for
`trial/planted.sh`. That runner applies the planted change to the checkout and
has nb's Mock provider make one tool call and give the planted closing message,
so the transcript is nb's own. Proctor then runs, grades and reports those
inner tries, and `accepts-correct` and `rejects-wrong` read their `results.jsonl`.
A model check in the written exam is dropped, because nothing in the trial can
answer it. If the written exam names a model check in `correctness` or
`integrity`, the trial fails, since without a model no run could be decided.
The scripts the agent wrote run only inside that container, never on the host.
The inner tries stay in the stage's attempt under `trial/` as evidence.

| check | role | what it says |
|---|---|---|
| `exit_ok`, `validates`, `accepts-correct`, `rejects-wrong` | correctness | nb finished; `proctor list` loads the exam cleanly; it passes the correct run; it fails every wrong one |
| `stays-in-work` | integrity | the agent stayed in its checkout |
| `self-validated`, `describes-everything`, `read-examples`, `no_denials`, `under_budget` | observation | it ran `list` itself; the exam carries a description and every stage an `about`; it opened an example; nothing was denied; it stayed in budget |
| `stance` (`decide`) | observation | the closing message claims completion |
| `prompt-hides-answer` (`judge`), each stage's own | observation | the prompt it wrote does not give away the answer |

`reference/` holds a good answer for each stage: three small exams that also
serve as examples of the practices above. `reference/shakedown.sh` proves the
trial without a model. The references must pass every check. With `--naive`,
each reference loses the check that made it discriminate, and `rejects-wrong`
must name the wrong runs that got through; with `--unsolved` nothing is
written and every check fails. Nothing in this directory is shown to the
agent: its checks and `trial/` are the grader and `reference/` is the answer
key, so `docs-and-examples` shows the other exams but not this one.

## Run it

```bash
export LLM_GATEWAY=... LLM_GATEWAY_KEY=...     # the gateway exams/nb.json and exams/auspex.json name (copied from *.example.json)
dotnet build                                   # the trial grades with this build
exams/exam-authoring/reference/shakedown.sh    # optional: the trial against the references, ~2 min
proctor run exam-authoring
proctor grade exam-authoring
proctor report exam-authoring
cp reports/exam-authoring/explore/summary.md exams/exam-authoring/report/
```

`--runner none` is only a shakedown here: nb and the agent's shell run on this
machine, with the proctor build linked at `.bundle/proctor`.

To test a documentation change, push it, add a value of `docs` whose files pin
the new revision, name its configurations, and compare the two along `docs`
(`proctor compare exam-authoring docs=<new> --against docs`).
