# Getting started

Auspex measures how well coding agents and models work with something you
built, such as a CLI, an SDK, documentation or a prompt. You write an exam,
auspex runs agents or models through its stages, grades each stage with
checks, and reports how one version of your work compares with another.
The counting and the report are done by deterministic code, with no model
involved.

This page takes you from a clone to a first report, then to an exam of your
own. Each step says who does it. Allow half an hour for steps 1 and 2.

## If you are a coding agent

Follow these rules through every step of this page.

- **Never handle an API key.** Do not ask for, read, echo or write one, in
  a command, a file or a message. Keys are kept in the user's env
  file (`~/.config/auspex/env`, or `%LOCALAPPDATA%\auspex\env` on
  Windows), outside the repository. Do not open that file. Config files
  under `exams/` refer to keys as `${VAR}` and must stay that way.
- **`auspex setup` belongs to the user.** It is interactive, asks for keys
  at a hidden prompt, and exits with code 2 when there is no terminal. Ask
  the user to run it in their own terminal and say which choices to make.
- **Read the state with `auspex setup --check`.** It prints one plain line
  per provider and per job and never shows a key. A line that starts with
  `problem:` says what to fix, and the exit code is then 1. The models each
  key can reach are listed in `exams/setup.json`. `--probe` repeats each key
  check, which lists models and spends no tokens.
- **Agree the spend before any run that calls a hosted model.** Estimate
  the requests (configurations × `tries` × stages) and get the user's
  yes first. Exams on nb's Mock provider cost nothing, and
  `readme-answers` fits in Gemini's free tier.
- **Plan an exam with the shipped skill**, `skills/auspex-exam/`. It
  covers planning with the user, building the files, and proving the checks
  for free before a paid run.
- **Leave the machine's files out of git.** Setup writes
  `exams/auspex.json`, `exams/nb.json`, `exams/setup.json` and
  `exams/sandbox-plan.md`, and lists them in `exams/.gitignore`. Edit only
  the entries an exam needs.
- **Never open the host's loopback interface to containers**, and never
  suggest it. A model or dev server on the user's machine is named as
  `localhost`, and auspex relays that one port. The rules are in
  `docs/network.md`.

## 0. Install

Who: you or your agent.

You need the .NET 10 SDK, git, and Node 22 (the first fixture is a small
TypeScript CLI). Auspex and its built-in harness, nb, are built from
source for now.

```bash
git clone https://github.com/breitreiter/nb && (cd nb && dotnet build)
git clone https://github.com/breitreiter/proctor && cd proctor && dotnet build
export PATH="$PWD/../nb/bin/Debug/net10.0:$PATH"            # auspex finds nb on PATH
alias auspex="dotnet $PWD/bin/Debug/net10.0/auspex.dll"
```

On Windows, in PowerShell:

```powershell
git clone https://github.com/breitreiter/nb; cd nb; dotnet build; cd ..
git clone https://github.com/breitreiter/proctor; cd proctor; dotnet build
$env:PATH = "$PWD\..\nb\bin\Debug\net10.0;$env:PATH"
function auspex { dotnet "$PWD\bin\Debug\net10.0\auspex.dll" @args }
```

Put the PATH line and the alias (or the function) in your shell profile.
Windows runs the built-in harness, which covers steps 1 to 3; the sandbox
in step 4 needs Linux.

## 1. The playground, with no model and no key

Who: you run `auspex setup`. You or your agent run the rest.

```bash
auspex setup                   # you, in a terminal: choose "Skip for now" at every job
auspex run playground          # makes its tries; run again, it has nothing to do
auspex grade playground
auspex report playground       # then open reports/playground/explore/summary.md
```

Skipping every job still writes the config the playground runs on. Its one
configuration is nb on the Mock provider, which answers from a script in
the prompt, so nothing leaves your machine. This is the loop once (run,
grade, report), with a report to read before you measure anything.
`exams/playground/README.md` explains each part of the exam.

## 2. A hosted model on Gemini's free tier

Who: you run `auspex setup` and enter the key. You or your agent run the
rest.

Run `auspex setup` again. This time it opens on a menu of jobs.

1. Choose **Built-in harness**, then *Add a provider*, then *Gemini
   (Google AI Studio)*. Setup prints where to get a key, and the free tier
   needs no card. Paste the key at the hidden prompt. Setup checks it by
   listing the models it can reach, which costs nothing. Keep the suggested
   name, `gemini`, because the exam names its provider that way.
2. Choose *Done*, then *Done* again.

```bash
auspex setup --check                 # agent: confirm "job nb: ready (gemini)"
auspex run readme-answers            # if it stops part way, run it again
auspex grade readme-answers
auspex report readme-answers         # then open reports/readme-answers/explore/summary.md
auspex compare readme-answers docs=for-scripts   # the same comparison as JSON
```

`readme-answers` asks Gemini Flash-Lite six questions about the
playground's CLI, answered from its README alone. Its `docs` axis has two
values: `as-shipped`, the live value, is the README as it ships, and
`for-scripts` adds a section on using the CLI from a script. The report
shows how `for-scripts` differs from `as-shipped`, try against try.

The run is 48 requests. On Gemini's free tier that costs nothing, though
Google may use free-tier requests to improve its models. The free tier
allows only a few requests a minute, so the run takes a few minutes.
`exams/readme-answers/README.md` says what result to expect.

## 3. Your own exam

Who: you and your agent together. You approve the plan and any spend.

Copy `skills/auspex-exam/` into your agent's skills directory
(`~/.claude/skills/` for Claude Code, `~/.codex/skills/` for Codex). Then,
in your own repository, ask the agent to help you plan an auspex exam. It
goes through your project with you, proposes the stages and the check for
each one, writes the plan as `exams/<id>/PLAN.md`, and builds the exam
after you approve it. Without the skill, ask the agent to read this
repository's `README.md` and `docs/configuration.md` first.

The agent has worked examples in `exams/` to read. `readme-answers` is a
model answering from a document, `tern-docs` is a coding agent with docs
served beside it, and `code-change` asks one question in three
repositories.

Before any paid run, the agent proves the checks with no model and shows
you the cost. These commands are free:

```bash
auspex list <id>               # validates the exam and shows its configurations
auspex build <id>              # shows what each value changes, with nothing run
```

When the exam needs a provider you have not added, run `auspex setup`.

## 4. The sandbox, for coding agents under test

Who: you choose it in `auspex setup`. Your agent helps you finish it.

Claude Code, Codex and other coding agents under test run in containers
through Harbor, because an agent under test is often given vague or risky
instructions on purpose. Exams on the built-in harness, like the two
above, need no sandbox.

The supported platform is rootless podman on Arch and Ubuntu, with uv
installed. Docker is not maintained and macOS is not run. Start with
`docs/podman.md`, which lists what the machine needs and why.

In `auspex setup`, choose **Sandbox**, then *On this machine*. Setup looks
at what is installed and writes `exams/sandbox-plan.md`, a brief for your
agent that says what auspex needs. Ask the agent to read the plan and
`docs/podman.md`, then work through it with you. You run the steps that
need root yourself. `docs/network.md` describes what an agent's
container can reach once the sandbox works.

The Claude Code and Codex jobs in `auspex setup` come after the sandbox.
You enter an API key for each.
