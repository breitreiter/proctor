# jotcli

A tiny TypeScript to-do CLI (`repo/`): `add`, `list`, `done <n>`, `clear`,
tasks in a JSON file, one devDependency (`typescript`), tests on Node's own
runner. `npm ci && npm test` is the whole build. Small enough to read in two
minutes, which is what it is for: the first repository a new proctor user
meets, and a fixture for suites about how agents cope with a CLI.

## The planted behaviour

`clear` asks `Delete N tasks? [y/N]` before deleting, and reads the answer
from **the terminal itself** (`/dev/tty`), not from stdin, with no `--yes`
or `-y` flag. The author's rationale is in the comment in `src/confirm.ts`:
a stray redirect must not be able to say yes on the user's behalf. The
consequences for a program driving the CLI are the point:

- `echo y | jot clear` and `yes | jot clear` do nothing: stdin is never
  read.
- With no controlling terminal (an agent's shell tool, a container, CI)
  the command waits for one to appear, polling for 30 seconds
  (`JOT_CONFIRM_TIMEOUT_MS` shortens that for tests), then gives up.
- When it gives up it prints `Aborted.` and **exits 0**, so a caller that
  reads the exit code believes the list was cleared.

This is a real shape of agent-unfriendly CLI, seen in the wild in an
instrumentation tool that paused for a keypress with no `-y` and could not
be fed by a pipe. It is also two of the failure modes in cli-agent-spec's
catalogue (interactive prompt with no non-interactive path; success exit
code on a non-success).

The repository's own tests pass before and after any fix: they cover
`add`, `list`, `done`, the exit codes and `clear` on an empty list, and
deliberately do not exercise `clear` with tasks present. A fix that adds
`--yes`, honours a non-TTY stdin, or exits non-zero on abort breaks no
test, so `tests-pass` is a regression guard, not the acceptance check.

## What a task on this fixture looks like

The natural task is the one the planted behaviour invites: *make `clear`
usable from a script or an agent.* A good solution adds a `--yes` (or
`-y`) flag, and, better, exits non-zero when it aborts. The acceptance
check belongs to the suite, beside the task, and is a script that:

1. seeds a store with tasks, runs `node dist/src/cli.js clear --yes` with
   stdin closed and no TTY, and expects exit 0 and an empty store, within
   a few seconds (so a 30-second wait fails it);
2. runs `clear` without the flag the same way and expects either an exit
   code other than 0 or a prompt that reads stdin, and an unchanged store.

A `files_touched` expectation of `src/**`, `tests/**`, `README.md` at
most keeps the change where it belongs.

For a Tier 0 playground, a scripted Mock task against this fixture can
run `node dist/src/cli.js list` and check the answer contains `Nothing to
do.`, so the first report a new user sees is about this repository.

## Why TypeScript, and why these choices

The audience this fixture is for writes JavaScript, TypeScript and Python
far more than C#, and the CLI-and-MCP world the positioning targets lives
on Node. The build is deliberately conventional (`tsc` to `dist/`, no
bundler, no test framework beyond `node:test`) so nothing about the
toolchain competes with the task for the reader's attention. Node 22 is
the image because it is already in the Harbor images for Codex and Qwen
Code. The one devDependency means a checkout needs `npm ci` once; both
checks do it when `node_modules` is missing, and the image does it at
build.
