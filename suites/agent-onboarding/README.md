# agent-onboarding

Can a shipped coding agent, given only proctor's own onboarding surface, set
up a working suite for a project it has never seen? This suite hands Claude
Code a small chatbot project, a request in a user's words, and a container
holding proctor, nb and the `proctor-suite` skill at a pinned revision.
Nothing else. It grades each suite the agent writes by what that suite does,
not by how it reads: it runs the written suite against planted runs of the
suite's own tasks on nb's Mock provider. The written suite must pass the
right replies and fail the wrong ones.

The suites asked for are *chat suites*: bare nb arms with `tools none`, where
the bot only reads and answers. The agent can run and prove one inside its own
container on Mock, with no key, no sandbox and no Harbor inside Harbor.

The suite has two jobs. Its runs are long, realistic trajectories of an agent
using an agent-facing surface (the skill, the docs, the CLI), which feed a
study of the report's design. And it is a standing measure: rerun it after
changing the skill, the docs or the CLI, to see whether onboarding got better.
For that reason the surface is the real one at a pinned git revision, not a
simplified copy. The task prompts read like requests, not like specs that
solve the onboarding in advance.

## What runs

| | |
|---|---|
| arms | `cc-sonnet`: Claude Code 2.1.301 on `claude-sonnet-5-5`, 5 samples. `qwen`: Qwen Code 0.25.2 on the local `qwen3-coder-next`, 1 sample, the free shakedown arm (`../code-change/hooks/ensure-model.sh` loads the model) |
| tasks | three chatbot projects, one request each (below) |
| runs | 18: 15 on Sonnet, 3 local |
| wall | 45 minutes per run (`limits.wall_ms`) |

| task | fixture | the written suite must get right |
|---|---|---|
| `exact-answers` | `onboarding-toolshed`: a tool library's help bot, its handbook, the five questions members ask | each question's figure in `expect` through `@expect`, matched with its unit, so near misses fail: `19 days` and `9 weeks` for 9 days, `$7.50` and `$0.75 a week` for $0.75 a day, `$120 a year` and `$12 a month` for $12 a year |
| `revised-prompt` | `onboarding-allotment`: an allotment society's bot with its live system prompt and a draft rewrite | two arms that differ only in their `bundle`, one prompt version each, and a figure check that catches the version that answers wrong |
| `stays-on-topic` | `onboarding-bikekitchen`: a bike workshop's bot whose system prompt gives the one sentence that turns down an off-topic request | a check that fails a reply that played along, including one that declines with the sentence and then plays along anyway (`system.md` says "this sentence and nothing else") |

## The surface, pinned

Each fixture's `image` clones nb and proctor from GitHub at pinned revisions
and builds both. It puts `proctor` (a wrapper around the build's `proctor.dll`)
and `nb` on PATH. It copies `skills/proctor-suite` to `~/.claude/skills/`,
where Harbor's Claude Code agent picks skills up, and to `~/.qwen/skills/`,
Qwen Code's. The proctor clone loses its history and this suite's own
directories (`suites/agent-onboarding`, `fixtures/onboarding-*`) before it is
built. Its one commit is named after the revision. A later pin that includes
this suite therefore cannot show the agent the answer key or the grader.
`surface-present` records the revision on every run.

**To measure a new revision of the surface,** change the proctor revision in
the third `image.run` line of the three `fixtures/onboarding-*/fixture.json`.
The block is identical in all three, so one command does it:

```bash
sed -i 's/76494f192dc303d4b4bb5256702bb4fc1e04e7b2/<new sha>/g' fixtures/onboarding-*/fixture.json
```

The revision must be pushed to GitHub, since the image clones it. A fixture's
image block cannot be shared between fixtures, so the pin appears three times.
The suite hash covers the fixtures, so `resume` refuses an experiment whose pin
changed.

## How a written suite is graded

The checks run in Harbor's offline verifier, a fresh container of the same
image with the run's diff applied to the fixture commit and no transcript. So
every check works from the final work directory alone. `checks/written.sh`
finds the suite the agent wrote: the one new `suites/<id>/suite.json` under
any root. A scratch copy is ignored when its id says so (`*-mock`,
`*-shakedown`, and so on).

`checks/trial.sh` runs `trial/run.sh` once. It copies the written root and
clones each written arm once per planted variant in `trial/<task>/plants.json`,
with one sample, keeping its bundle and every other field. It drops hooks,
the nb runner and model checks; a model check named in `pass` or `validity` is
a problem, since nothing here can answer one. It then runs, grades and reports
that inner experiment with the image's proctor. The runner is
`trial/planted.sh`, which stands in for the bot. From the program proctor
compiled for the cell, it reads which system prompt and knowledge file the
bot was given (marker sentences from each file), and from the task's prompt,
which question is asked. Then it answers as the planted variant would, through
nb's Mock, and records what the suite should make of that reply. A task
asking something the trial does not recognise is answered and not counted.
The inner experiment, the records and `trial.json` stay beside the reward
(`verifier/trial/`) as evidence.

The trial reports a problem, which fails both trial checks, when:

- no arm gives the bot its system prompt and its knowledge file. The suite
  would not measure the bot.
- on `revised-prompt`, either version is missing, or one program carries both.
- an arm switches version from task to task.
- the live and draft arms differ in anything but `bundle` (id, description and
  samples aside), or share a bundle.
- the runs failed before the bot answered.

| check | kind | what it says |
|---|---|---|
| `exit_ok` | pass | the agent finished its turn |
| `validates` | pass | one suite was written, and `proctor list` (the pinned build) loads it with no problem |
| `accepts-correct` | pass | every planted right reply passed and was counted |
| `rejects-wrong` | pass | every planted wrong reply completed, was counted and failed. An undecided, excluded or broken run got through |
| `surface-present` | validity | proctor, nb and the skill were in the image and ran, and nb answered on Mock |
| `chat-only`, `no-secrets`, `describes-everything` | guardrail | every arm is bare nb and the program says `tools none`; no key value in any file written (`${VAR}` only); the suite, its arms and its tasks are described |

That is deliberately all. There are no process checks (whether the agent read
the skill, ran `list`, or proved the suite on Mock), no model checks and no
budget checks. Finding process problems is the job of the report study, which
reads the trajectories.

| task | planted variants |
|---|---|
| `exact-answers` | `good-terse`, `good-chatty` (figures in the handbook's words, then "per day" and "per year"); `bad-extra-digit`, `bad-wrong-unit`, `bad-dodge` |
| `revised-prompt` | `same` (the draft answers as well as the live prompt: every run passes); `regressed` (live right, the draft gives the gist: every draft run fails) |
| `stays-on-topic` | `good`; `bad-complied`, `bad-own-words`, `bad-declined-then-complied`. Every variant answers a Bike Kitchen question well, so those runs must pass in all four |

## Proving it at no cost

`reference/<task>/` holds a good answer for each task, laid over the
checkout as the agent would write it. `reference/shakedown.sh` checks each
fixture out, applies a reference, collects the diff and runs every check
but `surface-present`, the trial included. It runs on the host, takes about
three minutes per mode and needs `dotnet build` at the repository root and nb:

```bash
suites/agent-onboarding/reference/shakedown.sh             # every check passes
suites/agent-onboarding/reference/shakedown.sh --naive     # rejects-wrong fails, naming what got through
suites/agent-onboarding/reference/shakedown.sh --sloppy    # chat-only, no-secrets, describes-everything fail
suites/agent-onboarding/reference/shakedown.sh --unsolved  # nothing written: the suite checks fail
```

The script exits non-zero if any check does not come out as expected for its
mode. To check the image and the verifier too, compile the tasks and run each
reference through Harbor's oracle agent, with `reference/solve.sh` and
`reference/<task>/` as the task's `solution/`. That is what
`ConformanceTests` does for `code-change`. Every key in `reward.json` must be
1.

## Run it

```bash
dotnet build
suites/agent-onboarding/reference/shakedown.sh      # optional, ~3 min
proctor run agent-onboarding --arm qwen              # the free shakedown arm, local model
proctor run agent-onboarding                         # both arms: 15 Sonnet runs, real spend
proctor grade <id>
proctor report <id>
```

**Cost.** A Sonnet run reads the skill, the README, `docs/configuration.md`,
an example suite or two and the fixture, then writes about a dozen files and
runs `proctor list` and a Mock shakedown a few times. Expect 40 to 80 tool
calls, about 30 to 60 model requests, and 1 to 3 million input tokens (mostly
cache reads) per run. Agree the number before the 15-run experiment.

## Answer key (private)

What a correct written suite contains, per task. Nothing in this directory is
shown to the agent. The image removes it from the proctor clone.

**All three.**

- One suite under `suites/<id>/`, with a fixture of its own. A chat suite needs
  an almost empty one, like proctor's `fixtures/empty`, which is not in the
  user's repository.
- Bare nb arms: `runner: nb`, `harness: nb`, a provider and a model.
- A program with `model {{model}}` before `provider {{provider}}`, then
  `tools none`.
- The bot's own system prompt and knowledge file as `system` messages, e.g.
  `system @{{bundle}}/system.md` and `system @{{bundle}}/knowledge.md` with the
  arm's bundle `{"path": "bot"}`.
- `run {{prompt}}`, with the member's question as the task prompt, unadorned.
  A suite that appends a reply format (`Reply with ANSWER: ...`, as
  `readme-answers` does) measures a different bot from the one members use.
  The planted replies are the bot's natural sentences, so it fails
  `accepts-correct`.
- Deterministic checks only. A validity check that the provider answered
  (`exit_reason: ok`) is good practice.
- Config that names keys as `${VAR}`. The user's llama-server needs none.
- A description on the suite, every arm and every task.

**`exact-answers`.**

- One task per question in `notes/member-questions.md` (any subset counts).
- Each task's figure in its own `expect`, read by one shared check through
  `"@expect"`.
- A pattern that anchors the number and requires its unit:
  - `\b9 days\b`
  - `\$0?\.75 (a|per) day`
  - `\$12 (a|per) year`
  - `\b3 weeks\b`
  - `\b5 working days\b`
- A bare `9` or `answer_contains: "12"` lets the near misses through.
- `reference/exact-answers/`.

**`revised-prompt`.**

- Two arms, identical but for `id`, `description` and `bundle`:
  - one whose program gives the bot `bot/system.md`;
  - one whose program gives it `bot/system-draft.md`.
- The knowledge file in both.
- Both prompt files are in one directory, so the bundles must be two
  directories: copies, as the reference has, or a layout the agent makes.
- The same figure checks as `exact-answers`, so that the regressed draft
  fails. A suite that only checks that the bot answered cannot see the change.
- `reference/revised-prompt/`.

**`stays-on-topic`.**

- One task per off-topic request in `notes/off-topic-requests.md`.
- A check that the reply is exactly
  `Sorry, I can only help with questions about the Bike Kitchen.` and nothing
  else: `answer_equals`, or an `answer_regex` anchored at both ends.
- `answer_contains` or an unanchored regex lets
  `bad-declined-then-complied` through.
- Optionally, a Bike Kitchen question whose check fails that sentence, so an
  over-refusing bot would fail too.
- `reference/stays-on-topic/`.
