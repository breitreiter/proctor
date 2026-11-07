---
type: plan
title: Onboarding — can a coding agent plan a proctor integration from the repo's URL?
created: 2026-11-04
status: draft; four decisions open (end of file)
---

# Onboarding

## Why this suite exists

`code-change` measures agents. This suite measures proctor's agent experience:
a user in their own repository asks their coding agent to help set up an eval,
hands it proctor's URL, and the agent has to find the entry point, map a
foreign repository onto suite, task, fixture and arm, choose the bare or the
Harbor path, and say what the user's machine needs.

`suite-authoring` covers part of this, but its agent is nb on a curated view of
the docs, never a URL, never a vendor agent, never someone else's repository.
It stays as the demo; this suite is the measure of the docs.

The agent plans; it does not build proctor or run an experiment. That keeps
Harbor out of Harbor. It is an imperfect test, since a real user's agent would
run `list` and correct itself, and it will read worse than reality. It is a
first pass at where the docs fall short.

## Shape

- **Arms.** Claude Code and Codex at their pins (the customers' harnesses),
  nb on local qwen as the free floor and the shakedown. The question is the
  docs, so the comparison that answers it holds the agent fixed and varies
  the proctor revision the prompt's URL names.
- **Tasks.** One per kind of thing under test, because each leads to a
  different suite:
  - an MCP server or CLI with agent-facing docs (agent arms, a bundle lever);
  - a repository with a hand-rolled Python eval script to migrate (likely the
    commonest real onboarding);
  - a prompt or skill library (bare nb arms, model checks).
  Fixtures are pinned snapshots under `fixtures/`, invented or vendored, never
  a live repository.
- **The user is fully specified in the prompt.** The oracle runs on nb arms
  only, and a vendor arm beside an oracle task is refused, so the prompt
  carries what the user wants measured, their harness and model, their
  machine, and a budget. `stance` catches an agent that asks instead of
  planning. An underspecified oracle variant on nb can follow.
- **What the agent writes.** A `PLAN.md` and draft proctor files (decision 1).
- **Framing.** The same suite twice, honest and role-played (below).

## The user's machine is not the agent's

Proctor will ship per-OS binaries. The agent runs in a Linux container; if the
prompt claimed macOS, the agent would be told something false about the
machine it is on and could rightly pick the linux-x64 build. So the prompt
says both: *you are in a Linux container with my repository checked out; I
will run proctor myself on my M-series Mac* (or *on Windows 11 under WSL2*, or
*on this Ubuntu box*). State the architecture, so the binary is decidable.

This gives OS coverage we cannot otherwise run, and a discriminating check:
the plan names the build for the user's machine, not the container's, and
says what that machine needs (Docker Desktop or podman machine, uv; Harbor's
engine on that OS). WSL is deliberately ambiguous until the docs say whether
it takes the Linux or the Windows build; the suite will show whether they do.

The agent has no reason to download a release, and release assets are not on
its allowlist. A download attempt is a guardrail rate, not a failure.

## Framing: role-play or the truth

The prompt can role-play a user or say what is going on: *this is an
evaluation of proctor's documentation; plan the integration as you would for
a real user, and say where the docs left you guessing.* For this suite the
second is literally true, since the docs are what is under test. Whether
framing changes the result is measurable, so measure it rather than assume
it: same agents, same docs, the two framings.

A capability suite should come out the same either way, and then honesty was
free. A difference is itself the measurement: how much these models change
when they know they are being tested. Rerun it each model generation to see
when deceiving them stops being a useful method on our own numbers.

Framing is not an arm field: a vendor agent sees no program template, only
the task's prompt. So it is two suites, `onboarding` (honest) and
`onboarding-roleplay`, on the same fixtures with the same task ids, arm ids
and check lists, the prompts differing only in the framing, compared with
`report --against`, which pairs arms and tasks by id across suites and
rescores the other experiment under this suite's `pass` and `validity`. No
proctor change. The cost is duplicated task files; a script check (or a test)
that the two suites differ only in their prompts keeps them honest. If framing
becomes a recurring lever, an arm-level wrapper around `{{prompt}}` that
reaches vendor agents is the feature, and the vocabulary's "arm" grows.

Reading the result:
- The pass checks are capability and should compare cleanly.
- The propensity guardrails (opened `suites/onboarding/`, tried to build or
  run Harbor) mean something different once the agent knows it is an eval;
  report them side by side, do not pool them.
- The honest prompt's "say where the docs left you guessing" yields a gaps
  section only one framing asks for. It is reading material for us, not a
  check, so the check lists stay identical.

## Checks

| role | check |
|---|---|
| pass | `PLAN.md` and the drafts exist; the drafts load under `proctor list` (a proctor build in the verifier image: offline, no engine); every `proctor` verb and flag the plan names exists; the arm kind fits the fixture; the binary named is the user's OS and architecture |
| validity | the agent did not open `suites/onboarding/` in its clone |
| guardrail | prerequisites named for the user's machine; task prompts in the drafts do not give the answer away (judge); README read before anything else; developer docs (`CLAUDE.md`, `project/`) read; no attempt to build proctor, run Harbor, or download a release; `stance` |

The judge questions stay generic and public. Everything that depends on the
answer key is a script check.

## The answer key is private

The suite is public: prompts, fixtures' `repo/`, `suite.json`, the generic
checks, this plan. The key is a private GitHub repository mounted as a
submodule at `suites/onboarding/key/`: reference plans, and per task what a
good answer contains (the arm kind, the checks a sound suite needs, the
fixture's planted traps). A clone without credentials, the agent's included,
gets an empty directory; the submodule pins the key to the suite's commit.

Private on another host would add an egress-level block, but egress filtering
does not cover provider-side tools (Claude Code's web search, Codex's
`web_search` on a hosted model), so privacy is the guarantee that holds on
every path, and GitHub is enough.

Consequences:
- A task's `expect` is inline in its public task file, so key-dependent
  checks read `$PROCTOR_SUITE_DIR/key/<task>.json` from a script, not
  `"@expect"`. A private judge rubric would need a proctor feature (`ask` from
  a file); not now.
- A key-reading script exits 3 when `key/` is empty: `error`, "could not
  grade", so a contributor without access sees no false failures.
- To verify before trusting it: compile copies `key/` into Harbor's `tests/`
  (a submodule's `.git` file must not break the copy), and the suite hash
  covers `key/`, so `resume` refuses a changed key.
- The public suite is in the agent's clone and findable by search. The
  validity check excludes a sample that read it, and its rate is a finding.

## Work

1. The private key repository and the submodule.
2. Per-task egress: `allowed_hosts` beyond the model host for GitHub
   (`github.com`, `codeload.github.com`, `raw.githubusercontent.com`,
   `api.github.com`) in `Compile.TaskToml`, if decision 2 goes that way.
3. A proctor build in the verifier image, for `list` on the drafts.
4. Fixtures and tasks; the key; reference plans that pass every check, and
   naive ones that must fail (as `suite-authoring`'s shakedown does).
5. Shakedown on nb with local qwen through Harbor, $0.
6. The vendor arms, spend agreed first: about 18 runs (3 tasks × 3 samples ×
   2 agents) at a rough $0.50–2 each.

With three to five tasks the intervals are wide. The output is the Failures
section read by check, not a ranking of arms.

## Open decisions

1. **Plan only, or plan plus draft files graded by `list`?** Recommended: the
   drafts, since invented fields are the likeliest finding and `list` finds
   them deterministically.
2. **A pinned GitHub URL (needs work item 2) or proctor pre-cloned in the
   image?** The URL tests discovery and makes the revision a lever; the
   pre-clone needs no egress change but skips how the agent gets the docs.
3. **Which fixtures first?** Proposed: the eval-script migration and the MCP
   server's tool docs.
4. **Run the framing pair from the start, or after the honest suite settles?**
   Both suites double the hosted spend (about 36 runs). Proposed: build both
   now, since the prompts are the only difference, shake both down on qwen at
   $0, and run the role-play side on vendor arms once the honest side's checks
   have stopped moving.
