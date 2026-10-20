---
type: plan
title: Harbor conformance — proctor's layer over Harbor, checked at no cost
created: 2026-10-20
status: done 2026-10-20 (proctor 0.3.3); rows stay as the regression benchmark
---

# Harbor conformance

## Why this plan exists

Harbor is a working project with its own tests. Proctor does not regression
test it; proctor checks that it **invokes Harbor correctly and reads back
what Harbor wrote**. That is a mechanistic check and it costs nothing: no
hosted model, no paid judge, no gateway cap. Staging 5's first real run
(`20261020-0239-code-change-f3h0`) spent money on hosted agents and a judged
grade to learn things a scripted endpoint would have shown: every bug it found
(nb's `todo_write`, Codex's `get_goal`, the musl lookup, `resume` and a
changed route) was plumbing.

The rule from here: **a check of the layer costs $0.** A hosted model runs
only in an experiment whose question is about that model, and its spend is
agreed before it runs.

## What proctor owns, what Harbor owns

Proctor owns five seams, and this plan covers each:

1. **Compile**: the task directory (`instruction.md`, the Dockerfile,
   `task.toml` with its network policy and allowlist, `tests/`), and the job
   file with each agent's kwargs and env.
2. **Invoke**: `uv tool run harbor@<pin>`, the key in the environment only.
3. **Verifier**: the script checks into `reward.json`, `diff.patch`
   collected.
4. **Ingest**: the trial into a cell, ATIF into `core.json`, every tool to a
   kind, failed told apart from completed.
5. **Grade**: the windows from ATIF.

Per vendor agent, only these differ: the install at the pin, the agent's
config and env, its tool names, and its trajectory's shape.

Harbor owns the rest, and proctor does not test it: the container lifecycle,
the egress sidecar's matching, the agents' own command lines, and ATIF
conversion inside Harbor.

## The stub model

A scripted model endpoint, the vendor agents' counterpart to nb's Mock
provider. One stdlib Python server (`fixtures/stub/services/stub.py`) speaks
the three dialects the agents use:

| dialect | path | agent |
|---|---|---|
| Anthropic Messages | `/v1/messages` | Claude Code |
| OpenAI Responses | `/v1/responses` | Codex (a WebSocket upgrade gets a 404, and Codex falls back to HTTP) |
| OpenAI Chat Completions | `/v1/chat/completions` | Qwen Code |

It is stateless and reads its script from the prompt, as Mock does
(`STUB:…`):

- No tool result in the conversation yet: it answers with one call to the
  agent's shell tool, whichever of `Bash`, `exec_command` and
  `run_shell_command` the request offers, running the command the prompt
  names.
- A tool result present: it answers with the final text, which lists every
  tool name the request offered (`tools: a, b, c`). So the trajectory itself
  carries the agent's tool surface at its pin, and the test checks it against
  `Ingest.Kinds` with no model involved.
- `STUB:fail`: every request gets a 500 (the outage path).
- `STUB:hang`: the first answer is the tool call, and the second never comes
  (the agent-timeout path).
- Any other path or request gets a 404. A request that was not streamed gets
  the same answer, unstreamed.

It runs as a compose service in the task's environment. With egress control
on, Harbor puts such a service in the main container's network namespace, so
the agent reaches it at `http://localhost:<port>` and nothing on the host
listens. A consequence: a stub trial does not exercise the allowlist of a
remote host. The host string proctor writes is unit-tested
(`HarborCompile.Policy`); the sidecar's matching is Harbor's.

## The fixture and the suite

- **`fixtures/stub/`** is a minimal `repo/`, with an `image` whose `services`
  names a directory (`services/`, holding `docker-compose.yaml` and
  `stub.py`) that compile copies into the task's `environment/`. That is the
  one compile change: `fixture.image.services`, a fixture's extra services,
  which a real fixture with a database would use the same way.
- **`suites/conformance/`** has one arm per vendor agent at its suite pin and
  three tasks:
  - `ok`: the agent runs `echo stub > made.txt`, and the script check
    `made-file` passes in the verifier.
  - `fail`: `STUB:fail`.
  - `hang`: `STUB:hang`, with a short `wall_ms`.
- **`ConformanceTests`** is opt-in like the gate (`PROCTOR_CONFORMANCE=1`;
  Docker, uv and Harbor needed). It writes its own `proctor.json` routes at
  `http://localhost:<port>` with dummy keys, runs the suite, grades it (every
  check deterministic), and asserts the outcomes below.

## The benchmark

Each row is one assertion. "Status" is what this plan is measured by; update
it as rows go green.

| # | seam | assertion | how | status |
|---|---|---|---|---|
| 1 | compile, job | the task files, the kwargs and env per agent, no key in any file | `HarborTests` (unit) | done |
| 2 | invoke → verifier → ingest → grade, nb | nb bare and nb through Harbor give the same transcript, core and checks | `GateTests` (`PROCTOR_GATE`) | done (staging 4) |
| 3 | invoke, per vendor | `ok` completes on Claude Code, Codex and Qwen Code: installed at the pin, the version check passes | `ConformanceTests` | done |
| 4 | verifier, per vendor | `ok`'s `made-file` is 1 in `reward.json`, and `diff.patch` adds `made.txt` | `ConformanceTests` | done |
| 5 | ingest, per vendor | `core.json` counts one `shell` call and the turns; the strict ingest maps every call | `ConformanceTests` | done |
| 6 | tool surface, per vendor | every tool the agent offers at its pin is in `Ingest.Kinds` | `ConformanceTests`, from the stub's final answer | done |
| 7 | grade, per vendor | `checks.json` passes `ok`, and the answer window is the stub's final text | `ConformanceTests` | done |
| 8 | failure path, per vendor | `fail` is `failed` (no turn, an outage), never `completed` | `ConformanceTests` | done |
| 9 | timeout path, per vendor | `hang` is `completed` with `time_budget` | `ConformanceTests` | done |
| 10 | the suite's own tasks | each `code-change` task's reference solution passes every script check under Harbor's `oracle` agent | `ConformanceTests` (oracle) | done |
| 11 | retire `agent-smoke` | `HarborTests` load a test-built suite; `suites/agent-smoke/` deleted; `CLAUDE.md` updated | unit | done |

Rows 3 to 9 together are one run of `suites/conformance` across three arms
and three tasks: nine trials, all at $0.

## Steps

1. **Stub and fixture.** Write `stub.py` and its compose file, and test the
   stub by itself (in a container, on its own network, no host port) for each
   dialect, streaming and not.
2. **`fixture.image.services`.** Validation (the directory exists and holds a
   `docker-compose.yaml`), the copy into `environment/` at compile, and the
   tree hash covering it. Unit tests.
3. **`suites/conformance` and `ConformanceTests`.** Rows 3 to 9. Fix in
   proctor whatever they find, each fix with a unit test.
4. **Oracle pass.** Row 10: the test compiles `code-change` as `run` does,
   writes each task's `reference/<task>.*` as its `solution/`, and runs
   Harbor's `oracle` agent. It is test-only, with no compile change: no suite
   declares a reference solution yet.
5. **Retire `agent-smoke`.** Row 11.
6. **Write it down.** `CLAUDE.md` (how to run conformance; the stub
   convention), and `learnings/harbor.md` (what the stub trials showed). The
   staging list in `agent-neutral-runs.md` points here.

## Results (2026-10-20)

All 11 rows are green: `PROCTOR_CONFORMANCE=1 PROCTOR_GATE=<nb publish>
dotnet test --no-build --filter "FullyQualifiedName~ConformanceTests|FullyQualifiedName~GateTests"`
passes in about 19 minutes at $0. What the rows found on the way, all in
proctor or its suites and none in Harbor:

- 16 tools that ingest did not map, offered by the agents at their pins
  (row 6).
- `add-retry-flag`'s reference solution, broken since the 2026-09-23 rename
  and dependent on python, which the image lacks (row 10).

Rerun the benchmark when an agent pin or Harbor's version moves, or when
compile or ingest changes.

## Not in this plan

- **Grading `f3h0`.** Its judged checks would spend Cloudflare budget to
  re-check plumbing. It stays ungraded unless a question needs it.
- **Codex on GPT-5, Claude Code on Sonnet.** These are model questions, for
  an experiment that is agreed and budgeted (staging 10's version bump is the
  first).
- **The two loose ends in `project/todo.md`** (`resume` and a changed route;
  musl and a bare hostname). Both wait until the codebase is stable.
