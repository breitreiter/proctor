---
date: 2026-11-14
status: proposed; nothing built. A suite-level declaration for proctor, read by auspex. Replaces the per-experiment `observe.json` of the lab scripts, which was never part of proctor
---

# The surface under test, declared in the suite

## Why

A suite usually tests one thing inside a fixture that has many. The
user's example: an MCP server that shows a novel view of GitHub history,
in a terrible codebase, in a container missing useful tools. The question
is how the server moves the needle, not the overall pass rate and not the
tasks where the agent got snarled in something else.

Nothing in a run says which part is which. What that cost us:

- **The same action has a different standing on a different task.**
  Searching the repository is the work on `code-change` and friction about
  the tool on `agent-onboarding` (`surprise.md`, cross-check). The symptom
  rules worked on one suite because the tool's name and source paths were
  written into them by hand.
- **Background friction is noise in every comparison.** A bad codebase
  and a missing `python3` hit both arms. They cancel on average and add
  variance per run, and variance is what buys more runs.
- **A run that never reached the surface counts against it.**
- **A pass that went round the surface looks like a pass**
  (`off-path.md`).

It is also the fourth column in Suchman's transcripts, the design
rationale, written once per suite
(`learnings/suchman-plans-and-situated-actions.md`).

## The declaration

In `suite.json`, beside `grading`. Two parts carry the weight: what the
surface is, and what an agent would use in its place.

```json
"surface": {
  "description": "the gh-history MCP server: a view of a repository's GitHub history",
  "tools": ["mcp__gh-history__*"],
  "commands": [],
  "docs": ["docs/gh-history.md"],
  "substitutes": {
    "commands": ["git log", "git blame", "git show", "gh"],
    "tools": []
  },
  "internals": []
}
```

- `internals` (added after the first test): the surface's own source and
  private notes, where reading them is going round the published surface.
- `tools`: tool names, globs allowed. An agent's own tool or an MCP
  server's.
- `commands`: shell commands, matched against the start of one action of
  a call after a compound command is split. For a CLI under test, such as
  `proctor` in `agent-onboarding`.
- `docs`: paths or hosts that describe the surface: its README, schema,
  `--help` output, skill.
- `hosts` (added 2026-11-14, with the `tern-docs` test): host and port
  of a surface reached by URL, such as a docs site. Also allowed under
  `substitutes`. Matched against any argument of an action or a tool
  call's `url`.
- `fails` (added 2026-11-14): patterns that mark a surface call's result
  as a failure, beside a 4xx or 5xx status line. "Abandoned" needs one.
- `substitutes`: the other ways to meet the same need. A missing entry
  here hides exactly the runs worth seeing, so it is the part to review.
- `description`: one sentence, shown wherever the split is shown.

A task says whether it needs the surface, in its `expect`:

```json
"expect": { "surface": "needed" }
```

`needed`, `optional` or `unneeded`. An `unneeded` task is a control: the
arm with the server should do no better on it.

## What a run sorts into

By rule, from the declaration and the calls. No model.

| Part | A call is here when | What it says |
|---|---|---|
| On the surface | it matches `tools` or `commands` | it was used |
| About the surface | it reads `docs`, asks for help on a surface command, or repeats a surface call that just failed | the cost of learning it |
| Around the surface | it matches `substitutes` | it was not reached for, or not trusted |
| Background | anything else | priced and set aside |

A compound call can sit in more than one part; its cost is split as the
ledger splits it now (`regimes.md`, open).

## What it yields per run

- **Reached.** The run made a call on the surface or on a substitute:
  it got to the point of needing what the surface offers. A run on a
  `needed` task that never reached is uninformative about the surface
  and is reported apart, not as a failure of it.
- **First reach.** Which came first, the surface or a substitute.
  Rasmussen's technicians took the cheap familiar check over the better
  one (`learnings/rasmussen-trouble-shooting.md`); this is whether the
  surface is the one reached for.
- **Abandoned.** A surface call that failed, followed by a substitute
  for the same need. The result was not usable. The sharpest of the four.
- **Went round.** Reached, passed, and never on the surface.
- **Three costs:** on, about and around the surface, in calls and in
  dollars where the run records them. The ledger for the thing under
  test.
- **Background cost**, as one number, so it can be subtracted.

Across arms these are the comparison that answers "did the server move
the needle": pass rate on `needed` tasks among runs that reached, first
reach, abandonment, and cost on and around the surface, with the
`unneeded` tasks as the control.

## What the split is for: a scent that ends as a check

The user's position (2026-11-14). There are two kinds of friction.

- **Something that should be a failing check.** We did not expect an
  agent to try it, or did not know the surface would fail that way. Very
  capable agents power through it, so the final-state checks pass. It
  starts as a scent and ends as a check.
- **Everything else.** Too fuzzy to code in a durable way. Less of it is
  better, returns diminish, and a practitioner makes the call against
  their own goals.

A hard requirement is a check. So there is no separate notion of intended
friction: a confirmation that must happen is a `tool_sequence` check, and
it is proctor's.

This sets what the surface split is for. "Abandoned" and "went round" are
the rule-made signature of powering through: the surface failed or was
passed over, the agent got there another way, and the run passed. Each is
a candidate check with its evidence attached (the runs and steps), and it
can be tried on the runs already on disk at no cost before it is kept
(`direction.md`, kept queries; proctor's `grade --try`).

The costs on, about and around the surface are for the second kind. They
are shown to the practitioner and are not gates. If the practitioner
picks a number ("no more than five calls spent learning it"), that is a
check too, and it moves to the first kind.

## Proctor's and auspex's

- **Proctor owns the declaration**: it is part of the suite, it is
  hashed with it, and it belongs in `experiment.json` with the rest of
  `suite_def`.
- **Proctor may want checks from it.** It already has `tools_used`,
  `tools_used_any`, `tool_args` and `tool_sequence`. "Reached",
  "went round" and "abandoned" are those checks with the surface's names
  filled in, so a suite could grade on them without restating the list.
  Whether they are checks or stay observations is proctor's call.
- **Auspex reads it** for the split, the ledger and the symptom rules,
  which stop carrying a suite's names in their code.
- **`observe.json` goes away.** It was a per-experiment file read by
  `project/lab/observe.py`, a lab script. Its `service_commands`,
  `service_tools`, `docs_paths` and `docs_hosts` are `commands`, `tools`
  and `docs` here. Its `service_fail`, `verify` and `run` patterns are
  not about the surface and are not carried over.

## Cautions

- **Background is not always independent of the surface.** A surface
  result of 200 kB sits in context and can cause trouble that sorts as
  background later. Record result sizes on surface calls so it can be
  seen.
- **A wrong declaration gives confident wrong splits.** Report every run
  with its unmatched commands by frequency, so a missing substitute
  shows up as a common background command.
- **A need met without a call.** An agent can know the answer, or read
  it from a file it already had. "Reached" will say no. For `needed`
  tasks, never reached and passed is worth a list of its own.
- **Tool names differ by agent.** Claude Code calls an MCP tool
  `mcp__server__tool`; other agents name it otherwise. Proctor's
  `Ingest.Kinds` already maps tools per agent, and the declaration
  should name the server once and let ingest resolve it.

## First test (2026-11-14)

`trials/surface.py` with a hand-written declaration,
`trials/surface/agent-onboarding.json`, on the 15 Sonnet runs: 254 calls,
1,133 actions, $3.66. Surface commands `proctor` and `nb`; docs are the
two tools' published docs, the skill and the shipped example suites and
fixtures. This suite has no substitutes (there is no other way to run a
suite), so first reach, abandoned and went round could not be exercised.
What it has in their place is reading the tools' internals, which the
declaration above does not name.

**The split, by rule, beside the readers' ledger lines mapped to the same
parts** (Haiku labels, Sonnet labels):

| Part | Actions | By rule | Readers | Per-run rank agreement with each reader |
|---|---|---|---|---|
| On the surface | 90 | $0.29 (8%) | $0.63, $0.56 | 0.15, 0.43 |
| About it | 306 | $1.14 (31%) | $0.71, $0.81 | 0.61, 0.84 |
| Around it (internals, locating) | 129 | $0.45 (12%) | $0.79, $0.69 | 0.76, 0.59 |
| Background | 608 | $1.78 (49%) | $1.52, $1.59 | 0.84, 0.82 |

The readers agree with each other at 0.76, 0.66, 0.87 and 0.96 on the
same four.

**Candidate checks it produced**, each a rule over paths and commands:

| What the run did | Runs of 15 | Of those, passed | Cost |
|---|---|---|---|
| Located the install (`find /`, `readlink`, `which`) | 15 | 11 | $0.20 |
| Inspected the binary | 13 | 10 | $0.02 |
| Read other internals (anything under the install that is not docs) | 12 | 8 | $0.13 |
| Read tool source (`.cs`) | 4 | 1 | $0.07 |
| Read private notes (`project/`) | 3 | 1 | $0.02 |

Of 15 runs, 11 passed. The counts differ from `regimes.md`, which found
`on-disk-layout.md` in 6 runs and `Checks.cs` in 3 by matching the name
anywhere in a result, so a recursive grep that printed a line from the
file counted. Here a path has to be an argument. Both are right about
different things, and a check has to pick one.

**What doing it showed.**

- **The declaration needs an `internals` field.** The surface's own
  source and private notes are neither docs nor a substitute. Going
  there is this suite's way of going round, and it is where the candidate
  checks came from. Settles the first open question below.
- **A surface is often used through a script the run wrote.** The agent
  writes `shakedown.sh`, which calls `proctor`, and runs that. Without
  following it, "on the surface" halves (47 actions against 90). The rule
  now treats a file the run wrote that contains a surface command as the
  surface when it is run.
- **A pipeline is one action.** `proctor grade | tail` first sorted as
  half surface, half background, and `head`, `cat` and `tail` were the
  commonest background commands. Naming a pipeline by its first command
  fixed it.
- **The rule and the readers differ mostly by definition.** A grep over
  the docs is about the surface by rule and a tree search (around) to a
  reader; copying a shipped example into the new suite is about the
  surface by rule and writing the suite to a reader. That accounts for
  the rule's larger "about" and smaller "around". The per-run rankings
  agree about as well as the two readers agree with each other, except
  on the surface itself.
- **"On the surface" is the weakest number.** A call's cost is split
  evenly over its actions, and the call that runs the shakedown also
  writes files and edits them. Splitting cost inside a call is still the
  open problem from `regimes.md`.
- **The unmatched-command list earned its place.** It is how the
  pipeline and wrapper-script problems were seen.

Not tested here: anything involving substitutes. Tested since on
`tern-docs`, 54 runs (`friction-contract.md`). At the time: the next
suite has to have them. `nb-changes` may (reading nb's source against its docs is the
task, not a detour), and the user's MCP-server case does by construction.

## Open

- Suites with no single surface. `code-change` tests the agent. The
  declaration may be absent, and everything then is background, which is
  honest.
- How "the same need" is decided for abandonment. The first version is
  adjacency: a substitute within a few steps of a surface call.
