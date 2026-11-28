# proctor

**See how AI agents behave with what you built.**

Agents are reading your docs and your website. They're using your API, CLI
or MCP server. Proctor measures whether what you built helped an agent
reach a goal or a correct answer, and whether your last change made it
better or worse.

You describe the work and what "done" looks like. Proctor runs real agents
at it in sandboxed environments, several times each, grades every try with
checks you can read, and says what changed and whether so few tries can
confirm it.

## Why measure

The thing you shipped for agents is often not used at all. An agent says
it is done when it is not, and its own summary tells you what it planned,
not what it did. A fix that works for one model and harness may not hold
for the next version. Models are non-deterministic, so one good attempt in
your own setup says little about what your users' agents will do.

Proctor measures outcomes, efficiency and unwanted side effects across
the agents your users actually have, and keeps the comparison honest:
one good attempt doesn't mean the problem is gone.

## What it runs

- **Coding agents.** Claude Code, Codex and Qwen Code run through
  [Harbor](https://github.com/harbor-framework/harbor), one container
  per try, each pinned to a version. Each try gets a fresh checkout of a
  fixture repository, with your MCP server, skill, docs or prompt laid
  into it or served beside it. What is under test is declared as axes:
  the docs, the agent and model, the repository. A configuration is one
  value on each, so two configurations that differ only in the docs
  measure the docs.
- **Assistants and conversations.** For an in-product assistant, a
  chatbot or a headless job, proctor bundles a lightweight harness: you
  supply the system prompt, the tools and the model, from any local or
  hosted provider. A stage can carry an *oracle*, what the user knows but
  did not say, and proctor plays the user when the model stops to ask.

## How it grades

A check is one yes-or-no question asked of a finished stage of a try.
Three kinds mix as an exam needs:

- **Static**, no model: how it ended, turns and tool calls, wall
  time, whether the diff stayed in scope, your own tests against the
  result.
- **Judge**: a model sees one named window of the transcript and one
  question. It must answer with quotes, every quote is verified against
  the window, and several votes must agree. A split is undecided, never
  a default.
- **Classifier**: a typed classifier reads the window and returns a label
  with a probability, against a threshold.

Proctor counts turns, tool calls and how a stage ended from the trajectory
itself, never from the agent's own report.

## The report

Numbers as files (`stats.json`, `results.jsonl`) and a plain markdown
summary, with a viewer (`View/`) for reading tries, transcripts and
comparisons on a page. Mark the value in use as live on its axis, and
every other value of that axis is compared with it, try against try,
with every other axis held: better, worse or the same, and whether the
tries made can confirm it. A comparison says what it could not have
shown, so "no difference" can't stand in for "couldn't tell". No model
writes any of it; a judge's role ends at a label and a quote.

`proctor compare` and `proctor coverage` give the same answers as JSON,
for a coding agent answering a person.

Two example reports are in [`docs/reports/`](docs/reports/), HTML only.
They were rendered by a much earlier version, in its words and its
shape, and are kept as they were.

## Install

You need the .NET 10 SDK and git. Running coding agents also needs
rootless podman (Docker works and is not maintained) and uv. Proctor is built from source for now, together with
its bundled harness, which lives in a repository of its own:

```bash
git clone https://github.com/breitreiter/nb && (cd nb && dotnet build)
git clone https://github.com/breitreiter/proctor && cd proctor && dotnet build
export PATH="$PWD/../nb/bin/Debug/net10.0:$PATH"
alias proctor="dotnet $PWD/bin/Debug/net10.0/proctor.dll"
proctor setup
```

`proctor setup` is a short wizard for providers and keys. Run it in a
terminal yourself, not through a coding agent: keys go to a file in your
user profile, never to the repository. Windows commands and the full walk
through are in [`docs/getting-started.md`](docs/getting-started.md).

## Get started

[`docs/getting-started.md`](docs/getting-started.md) goes from a clone to
a first report in three steps:

1. **The playground** ([`exams/playground`](exams/playground)): a
   scripted exam on a small TypeScript CLI. No model, no key.
2. **A real model** ([`exams/readme-answers`](exams/readme-answers)):
   two versions of a README compared on a free hosted model. A
   documentation change, measured.
3. **Your own exam**, planned with your coding agent using the skill in
   [`skills/proctor-exam/`](skills/proctor-exam/).

The loop is always the same:

```bash
proctor run <exam>      # makes the tries the exam lacks; after a crash, the same command makes what was lost
proctor grade <exam>
proctor report <exam>   # reports/<exam>/explore/summary.md
```

## Documentation

- [`docs/getting-started.md`](docs/getting-started.md): clone to first report
- [`docs/cli.md`](docs/cli.md): every command and flag; tries, what is live, the report
- [`docs/configuration.md`](docs/configuration.md): exams, stages, axes and values, fixtures, checks, judges, and the machine's config
- [`docs/network.md`](docs/network.md): what a coding agent's container can reach, and why
- [`docs/service-fixtures.md`](docs/service-fixtures.md): a fixture that brings an API or a docs site the agent can call and cannot read
- [`docs/podman.md`](docs/podman.md): running coding agents on rootless podman, the engine that is supported; [`docs/docker.md`](docs/docker.md) is not maintained
- [`docs/porting-to-agent-neutral.md`](docs/porting-to-agent-neutral.md): what a fixture needs for a coding agent to run in it
- [`docs/porting-from-suites.md`](docs/porting-from-suites.md): moving an exam of the earlier shapes to stages and axes

## What it is not

- **Not a cloud service.** Exams, transcripts, verdicts and reports are
  files in your repository. The only network traffic is the agents' model
  calls and the judge's.
- **Not a leaderboard.** The question is never which agent is best. It is
  whether what you built works for the agents your users have, and
  whether your last change helped.

## License

MIT. The wizard hat icon (`assets/pointy-hat.svg`) is adapted from
["Pointy hat"](https://game-icons.net/1x1/lorc/pointy-hat.html) by Lorc,
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
