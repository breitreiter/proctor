# Getting started

Three steps, each a little more real than the last: an exam that needs
nothing, an exam on a free hosted model, then an exam of your own. Allow
half an hour for the first two.

## 0. Install

You need the .NET 10 SDK, git, and Node 22 (the first fixture is a small
TypeScript CLI). Proctor and nb are built from source for now; released
binaries will make this step shorter.

```bash
git clone https://github.com/breitreiter/nb && (cd nb && dotnet build)
git clone https://github.com/breitreiter/proctor && cd proctor && dotnet build
export PATH="$PWD/../nb/bin/Debug/net10.0:$PATH"
alias proctor="dotnet $PWD/bin/Debug/net10.0/proctor.dll"
```

On Windows, in PowerShell:

```powershell
git clone https://github.com/breitreiter/nb; cd nb; dotnet build; cd ..
git clone https://github.com/breitreiter/proctor; cd proctor; dotnet build
$env:PATH = "$PWD\..\nb\bin\Debug\net10.0;$env:PATH"
function proctor { dotnet "$PWD\bin\Debug\net10.0\proctor.dll" @args }
```

Put the PATH line and the alias (or the function) in your shell profile to
keep them. Proctor finds nb on PATH unless `exams/proctor.json` says
otherwise (`nb.path`).

## 1. The playground: no model, no key

```bash
proctor setup                   # skip every job for now: this writes the config the playground runs on
proctor run playground          # makes its attempts; run it again and it has nothing to do
proctor grade playground
proctor report playground       # then open reports/playground/explore/report.html
```

The playground's one variant is nb's Mock provider, which answers from a
script in the prompt, so nothing leaves your machine. The point is to see
the loop once (run, grade, report) and to read a report before anything
real is measured. `exams/playground/README.md` says what each part shows.

## 2. A real model: `proctor setup` again, then `readme-answers`

```bash
proctor setup
```

Setup is a short wizard, one job at a time; run again, it opens on a menu
of the jobs. Run it in a terminal yourself,
not through a coding agent: it asks for API keys at a hidden prompt and
writes them to a file in your user profile (`~/.config/proctor/env`, or
`%LOCALAPPDATA%\proctor\env` on Windows), never to this repository. The
config files it writes in `exams/` refer to keys by name and are
gitignored.

For this step you need one job, the built-in harness, on one provider:

1. Choose **Built-in harness**, then *Add a provider*, then *Gemini*.
   Setup prints where to get a key; Google AI Studio's free tier needs no
   card. Paste the key. Setup checks it by listing the models it can reach,
   which costs nothing. Keep the suggested name, `gemini`.
2. Choose *Done*, then *Done* again. Run `proctor setup` whenever you want
   another job.

Then:

```bash
proctor run readme-answers      # if it stops part way (a throttle, a closed laptop), run it again: it continues where it stopped
proctor grade readme-answers
proctor report readme-answers
```

`readme-answers` asks Gemini Flash-Lite six questions about the
playground's CLI, answered from its README alone, under two versions of
that README. The first report shows them side by side, since nothing is
live yet. Mark the README as it ships live and report again, and the page
gives the other version's difference from it: a documentation change,
measured, with an interval around it:

```bash
proctor live readme-answers as-shipped
proctor report readme-answers
```

It is 36 requests, free on Gemini's free tier
(whose requests Google may use to improve its models), and takes a few
minutes because a free tier allows only a few requests a minute.
`exams/readme-answers/README.md` says what to expect.

## 3. Your own exam

What to measure is your project's question, so this step is a conversation
with your coding agent. Proctor ships a skill for it,
`skills/proctor-exam/`: copy that directory into your agent's skills
directory (`~/.claude/skills/` for Claude Code, `~/.codex/skills/` for
Codex), then, in your own repository, ask the agent to help you plan a
proctor exam. It inventories your project with you, proposes what an
agent should be able to do with what you built and how you would know it
did, and builds the exam once you agree. Without the skill, ask it to
read this repository's `README.md` and `docs/configuration.md` first.
Two things it can read for itself:

- `proctor setup --check` prints what is set up on this machine, which
  providers you have and what is missing, in plain lines meant for an
  agent; `exams/setup.json` beside it lists the models each key could
  reach. Neither holds a key.
- `exams/` holds worked examples: `readme-answers` for a question answered
  from a document, `cli-for-agents` for a code change checked by running
  the result, `exam-authoring` for an agent that writes exams.

Your agent should never handle a key. When an exam needs a provider you
have not added, run `proctor setup` yourself.

**Claude Code, Codex and other coding agents** run inside a sandbox
(Harbor, in containers), since an agent under test is often given vague or
risky instructions on purpose. In `proctor setup`, choose *On this machine*
at **2. Sandbox**: setup looks at what is installed and writes
`exams/sandbox-plan.md`, a brief for your coding agent to finish the job
with you. What it needs and why is in `docs/podman.md` and
`docs/configuration.md`, "Agent variants". Exams on the built-in harness, like
the two above, need no sandbox.
