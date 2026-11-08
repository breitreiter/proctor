# proctor

Proctor measures what coding agents do with what you built: an SDK, a
CLI, a docs site, an MCP server, a skill, an assistant. You describe
tasks and the checks that say whether each was done, proctor runs them
against the agents and models you name (Claude Code, Codex, Qwen Code,
or its own harness), grades the transcripts with checks you can read,
and writes a report that says whether the change you shipped helped,
with an interval. The first suite to run is `suites/playground`; the
site is [breitreiter.github.io/proctor](https://breitreiter.github.io/proctor/).

It runs locally. There is no cloud service: the suite definitions, the
transcripts, the verdicts and the reports are files on your filesystem,
and the only network traffic is the agents' own model calls and, if you
use one, the judge's.

## What runs the agent

Proctor runs an agent in one of two ways.

Through [Harbor](https://github.com/harbor-framework/harbor), an agent runs
in a container against a checkout of the fixture repository. Harbor
isolates and runs Claude Code, Codex, Qwen Code, and
[nb](https://github.com/breitreiter/nb), and in general any harness that
speaks ACP. Use Harbor when the model needs tools that are unsafe on your
own machine, such as a shell, or when the question is about a particular
agent product rather than the model behind it.

Bare, nb runs on this machine with no container. nb is a small runner for
conversation programs: a scripted exchange with a model, with whatever
system prompt and tool surface you give it. It is there for the cases
where there is no CLI agent to test and you are not going to write one:
a chatbot embedded in your product, a model's raw comprehension of a data
structure or a tool definition, a prompt or a document on its own. Use
bare nb for those; use Harbor for anything with a shipped agent in it.

## Checks

A check is one yes-or-no question asked of a finished run. Proctor has
three kinds:

- **Static checks** read the transcript and the work directory without a
  model: how the run ended, turns and tool calls taken, denials, wall
  time, whether the answer matched, and script checks that run your own
  tests against the result.
- **LLM-as-judge checks** send a model a named window of the transcript and
  a question. The judge must answer with quoted evidence, every quote is
  verified against the window, and several samples must agree.
- **Classifier checks** send the window to a Jev typed classifier and read
  a label with a probability against a threshold.

Every verdict is cached beside the evidence, so a new check or a different
judge is a regrade, not a rerun.

## Oracles

For multi-turn tasks, a task can carry an oracle: an answer sheet that
plays the user when the agent stops to ask a question. The oracle answers
from the sheet, judges whether the agent's question was answered by it,
and records every exchange, so a check can require that the agent asked
and was told a given thing, or cap how many answers it needed. Oracles run
on nb arms, bare or through Harbor, where nb's ACP bridge carries the
exchange; the vendor agents leave no record the oracle could use.

## Reports

`proctor report` writes one self-contained HTML page and a markdown twin.
Every rate carries a 95% interval and its counts. Arms are compared
pairwise on the tasks they share, with an interval and a fixed verdict
word. The minimum detectable effect is stated, so an underpowered result
cannot read as "no difference". A run can be pinned as a baseline and a
later report can fail on regression against it. No model writes any part
of the report; a judge's role ends at a label and a quote.

Proctor counts turns, tool calls, denials and how a run ended itself, from
the trajectory Harbor returns (ATIF, Harbor's agent trajectory format) or
from nb's own log, by its own definitions. It never takes an agent's word
for its own numbers.

## Install

Build nb first (`dotnet build` in its repo), then:

```bash
dotnet build
alias proctor='dotnet /path/to/proctor/bin/Debug/net10.0/proctor.dll'
```

To run this repository's own suites, copy `suites/proctor.example.json` to
`suites/proctor.json` and `suites/nb.example.json` to `suites/nb.json`, and
point their routes at your gateway ([`docs/configuration.md`](docs/configuration.md)). Both copies are gitignored,
since they name your machine.

## Configure

A repository that uses proctor has `suites/` (yours, in git), `runs/` (raw
output, gitignored) and `reports/` (derived, written by `proctor report`).
What goes in a suite, a fixture, a task, an arm and the machine's
`suites/proctor.json`, the judges, runners and agent arms through Harbor,
is in [`docs/configuration.md`](docs/configuration.md).

## Use

```bash
proctor list [suite]          # validate; print the labels and the cells that would run
proctor list --label kind=bugfix   # only the suites carrying that label
proctor run <suite>           # run every cell into runs/<id>/; prints the id
proctor resume <id>          # rerun cells that did not complete
proctor grade <id>           # checks over every completed cell -> checks.json; model verdicts cached in verdicts/
proctor grade <id> --rejudge # call the judges again instead of reusing the cells' verdict files
proctor grade <id> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <id> --arm a [--tasks x,y]   # only those cells (--arm repeatable); the rest keep their checks.json, so a paid judge pass can be staged
proctor report <id>          # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--tasks x,y]   # pin the arm's analysed cells as suites/<suite>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
proctor report <id> --against <earlier>   # each arm against the same arm in an earlier experiment, in place of the baseline
proctor --version
```

Two ways of working. In explore mode several arms run in one experiment and
the report compares them to each other. In guard mode one arm runs against
a baseline: pinned cells per task, committed with the suite, written by
`proctor baseline` from an experiment you trust. The verdict card then
compares every arm to the baseline with the same paired method, and each
task card shows every arm against that task's pinned score. The verdict is
held, improved or regressed on the point estimate against the tolerance,
and `--fail-on regression` reads only that; it is *confirmed* only when the
whole interval lies beyond the tolerance on the same side (or within it,
for held), so a small task count cannot pass off a guess as a finding.
Without a baseline the card compares each arm with the first, the
reference. `--against <experiment>` stands in for the baseline for one
report: each arm is compared with the arm of the same id in that earlier
experiment, and the page says "earlier run" where it said "baseline".
While the pinned experiment is still under `runs/`, its cells are re-read,
so a regrade flows through; once it is archived the pinned scores stand,
and the report says which.

`report.html` is one file that loads nothing from outside (styles, a few
lines of script for the section rail, and the logo are inline); open it from
`file://` or attach it to a message. The page reads the same without the
script. `summary.md` has the same content as plain markdown: the verdict
card as a list and a heading per task.

Every rate carries a 95% interval and its counts; comparisons between arms are
paired on shared tasks with their own interval and a fixed verdict word; and
the minimum detectable effect is stated so an underpowered result cannot read
as "no difference". The methods are named in the report's last paragraph.

## Credits

The wizard hat icon (`assets/pointy-hat.svg`) is adapted from
["Pointy hat"](https://game-icons.net/1x1/lorc/pointy-hat.html) by Lorc,
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
