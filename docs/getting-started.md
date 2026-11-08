# Getting started

Three steps, each a little more real than the last: a suite that needs
nothing, a suite on a free hosted model, then a suite of your own. Allow
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
keep them. Proctor finds nb on PATH unless `suites/proctor.json` says
otherwise (`nb.path`).

## 1. The playground: no model, no key

```bash
proctor setup                   # skip every job for now: this writes the config the playground runs on
proctor run playground          # prints an experiment id
proctor grade <id>
proctor report <id>             # then open reports/data/<id>/report.html
```

The playground's one arm is nb's Mock provider, which answers from a
script in the prompt, so nothing leaves your machine. The point is to see
the loop once (run, grade, report) and to read a report before anything
real is measured. `suites/playground/README.md` says what each part shows.

## 2. A real model: `proctor setup` again, then `readme-answers`

```bash
proctor setup
```

Setup is a short wizard, one job at a time; run again, it opens on a menu
of the jobs. Run it in a terminal yourself,
not through a coding agent: it asks for API keys at a hidden prompt and
writes them to a file in your user profile (`~/.config/proctor/env`, or
`%LOCALAPPDATA%\proctor\env` on Windows), never to this repository. The
config files it writes in `suites/` refer to keys by name and are
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
proctor run readme-answers
proctor grade <id>
proctor report <id>
```

`readme-answers` asks Gemini Flash-Lite six questions about the
playground's CLI, answered from its README alone, under two versions of
that README. The report compares them: a documentation change, measured,
with an interval around it. It is 36 requests, free on Gemini's free tier
(whose requests Google may use to improve its models), and takes a few
minutes because a free tier allows only a few requests a minute.
`suites/readme-answers/README.md` says what to expect.

## 3. Your own suite

What to measure is your project's question, so this step is a conversation
with your coding agent. Ask it to read this repository's `README.md` and
`docs/configuration.md`, then your project, and to help you decide what an
agent should be able to do with what you built and how you would know it
did. Two things it can read for itself:

- `proctor setup --check` prints what is set up on this machine, which
  providers you have, which models each key can reach, and what is
  missing, in plain lines meant for an agent. It never prints a key.
- `suites/` holds worked examples: `readme-answers` for a question answered
  from a document, `cli-for-agents` for a code change checked by running
  the result, `suite-authoring` for an agent that writes suites.

Your agent should never handle a key. When a suite needs a provider you
have not added, run `proctor setup` yourself.

**Claude Code, Codex and other coding agents** run inside a sandbox
(Harbor, in containers), since an agent under test is often given vague or
risky instructions on purpose. In `proctor setup`, choose *On this machine*
at **2. Sandbox**: setup looks at what is installed and writes
`suites/sandbox-plan.md`, a brief for your coding agent to finish the job
with you. What it needs and why is in `docs/podman.md` and
`docs/configuration.md`, "Agent arms". Suites on the built-in harness, like
the two above, need no sandbox.
