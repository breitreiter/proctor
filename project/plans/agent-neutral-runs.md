---
type: plan
title: Agent-neutral runs — real harnesses through Harbor, tool-less nb bare, everything above the trial in proctor
created: 2026-09-29
revised: 2026-10-12 (Harbor replaces the container runner and the agent adapters; see learnings/harbor.md)
status: draft; nb's half (`../nb/plans/agent-neutral-runs.md`) revised to match on 2026-10-14
---

# Agent-neutral runs

## Why this plan exists

Evals increasingly need bash, and once bash is open nb's own controls stop
controlling anything; the guardrails already moved outward into containers.
Proctor's arms today are nb plus costumes of other harnesses
(`harness: claude-code`, `harness: codex`). Simulating Claude Code gets harder
every release as it entangles with Anthropic's other products; the honest way
to ask "does this ship in Claude Code?" is to run Claude Code.

Proctor's reports talk about runs, arms, checks and tasks, and never say nb.
That freedom is what this plan spends: the machinery under an arm changes,
the report does not, and the numbers must not move for machinery reasons.

## The revision of 2026-10-12

The first draft had proctor building the container runner and one adapter
per vendor CLI. Harbor (`learnings/harbor.md`) already is that: the
Terminal-Bench 2.0 harness, which runs ~40 agents, including Claude Code,
Codex and Qwen Code, with pinned versions in docker or podman, with network
policy per phase, and writes every agent's run as one trajectory format
(ATIF). It stops at a mean reward per job, which is where proctor starts.

So the split is: **Harbor runs a trial and returns its evidence; proctor
decides what that evidence means across a suite.** Proctor compiles a suite
into Harbor tasks and a job, runs `harbor`, ingests the trials as cells, and
grades and reports as it does today. nb stays for what no vendor CLI is: a
custom harness, and the tool-less test that needs no container at all.

We take this route knowing its costs (a Python dependency, a fast-moving
upstream, benchmark-shaped defaults to override; see *Risks*), because it buys
the real harnesses months sooner and puts a recognised harness under every
number in the report.

## Decisions

1. **Two paths, chosen by whether the arm has tools.** An arm with tools
   (every vendor agent, and nb when the program grants any) runs through
   Harbor in a container. A tool-less nb arm runs bare on the host, as today,
   ~2 s per cell, with no Docker and no Python. Validation refuses a tool on
   the bare path rather than running it unguarded. This replaces the first
   draft's *a container is mandatory*: the reason for the container was
   bash, and a tool-less arm has none. Every Mock-driven test in
   `Proctor.Tests` stays on the bare path.

2. **An arm names an agent, and the agent fixes the vendor.**
   `"agent": "nb" | "claude-code" | "codex" | "qwen-code"`, absent meaning
   `nb`, so classic suites run unchanged. `claude-code` takes Anthropic models
   only, `codex` OpenAI only, `qwen-code` Qwen models only (on the gateway or
   the imp box). Only `nb` takes any provider. Harbor will run any agent on
   any model behind a compatible base URL; proctor refuses it, and no flag
   overrides that. A real harness with another vendor's brain answers a
   question nobody asked.

   **No costumes, one route per harness.** `harness` leaves the arm entirely.
   `harness: claude-code` and `harness: codex` are refused with a pointer to
   `agent:`, and so is `harness: qwen-code`, now that the real Qwen Code CLI
   is an arm. The first draft kept that costume because qwen was the one arm
   where harness and model were decoupled; under Harbor the free local arm is
   real Qwen Code against imp. nb retires its costumes (its plan, item 5).

3. **The lingua franca is ATIF, with proctor's counted core over it.** Harbor
   writes `agent/trajectory.json` (ATIF v1.7) for every agent proctor
   supports, so ATIF replaces nb's transcript schema as the format the grader
   reads. A shared format alone is not comparability, which was the point of
   nb's plan item 1: proctor computes the counted fields (`turns`,
   `tool_calls`, `denied`, `exit_reason`) and the normalised tool `kind`
   (`shell | read | write | edit | search | agent | …`) **at ingest, by its
   own definitions**, per agent, from the ATIF steps. It never copies an
   agent's own count. Agent-specific fields go under ATIF's `extra`. The
   native log is kept beside it. A check matching on a tool `name` or an
   `extra` field is agent-specific: it reports n/a on other agents, and
   validation flags it in a multi-agent suite. nb arms are converted at
   ingest (bare and in Harbor alike) until nb emits ATIF itself.

4. **Harbor is a pinned, invisible dependency.** The Harbor version is
   pinned per suite, run as `uv tool run harbor@<version>`, recorded as
   `versions.harbor` in `experiment.json` and every cell manifest, and named
   in the report's method section ("run on Harbor 0.24.0"). `resume` refuses a
   changed Harbor version as it refuses a changed suite. Proctor sets
   `HARBOR_TELEMETRY=off` on every invocation. Harbor's vocabulary never
   reaches the report: a trial is a run, a job is an experiment, a dataset is
   not a suite. Harbor's `oracle` agent (it runs `solution/solve.sh`) is not
   proctor's oracle. Proctor calls it the reference solution, and it runs as
   a validity check on the fixture, not as an arm.

5. **Proctor overrides Harbor's benchmark defaults explicitly.** Harbor's
   defaults suit leaderboards, not product questions. On a cc arm it runs
   `bypassPermissions` with `IS_SANDBOX=1`. It installs the agent from the
   network at trial time. On a custom base URL it maps every cc model alias
   to one model, which silently collapses subagent models. Each arm's
   compiled agent config states permission mode, tools, version and model
   aliases. Nothing is inherited, and the cell manifest records what was set.

6. **Limits are run policy in `suite.json`, enforced as far down as each
   layer allows.** `tool_calls`, `wall_ms`, `tokens`, `cost_usd`. `wall_ms`
   compiles to Harbor's `agent.timeout_sec`, which every agent honours. The
   rest compile to an agent's own flag where one exists (cc `--max-turns`,
   `--max-budget-usd`). Ingest always checks all four against the counted
   core and writes the shared exit reason (`max_tool_calls`, `time_budget`,
   `token_budget`, `cost_budget`) on any run that went over. A limit that
   could only be checked afterwards is recorded as `enforced: false` in the
   manifest, and the method section says so per arm. A live supervisor that
   kills a run mid-flight is a follow-on (*Open questions*: `--stream`).

7. **`approval` and `loop` stay agent behaviour, not limits.** `approval`
   is a refusal the model sees. It compiles to cc's `--allowedTools` /
   `--disallowedTools` with a non-bypass permission mode, to Codex's approval
   policy, and to nb natively. `loop` is an nb nudge. Other agents have
   nothing equivalent, so `not_nudged` reports n/a on them rather than pass.

8. **Where checks run.** Script checks compile into Harbor's separate
   verifier container (`[verifier] environment_mode = "separate"`), built from
   the fixture's image with the fixture's checks and expectations, which never
   enter the agent's container. Each check writes one key to `reward.json`:
   1 is pass, 0 is fail, a missing key is error, and 2 is needs-judge. Harbor's
   regrade then reruns them with no agent and no key. Built-in checks and
   model checks run on the host at `grade`, from the ingested cell, and
   `checks.json` stays the one file grading writes. Provisional until the
   spike (Work, 0) shows the verifier container sees what script checks need.

9. **The oracle stays an answer sheet on the host, and becomes a simulated
   user.** Harbor already runs a second agent as the user, talking to the
   agent under test over ACP (`--user-agent`, `--bridge acp`; cc, Codex,
   Gemini and opencode as targets). Proctor's oracle is a custom external user
   agent (`BaseAgent`, whose loop runs in the Harbor process, not the
   container). It answers each turn with `nb --judge --sheet <host path>`:
   `hit` sends the answer, `done` ends the run, `miss` records it and ends.
   The sheet never enters the container. The answer turn is marked
   `user{source: "oracle", keys}` at ingest. Unverified: whether an external
   agent can be the user side of the bridge. If not, see *Open questions*.
   A tool-less nb arm keeps nb's in-process oracle on the bare path, where
   the sheet is on the host already and the model has no tool to read it.
   A tool-using nb arm with an oracle is refused until nb speaks ACP, the
   bridge's only way to a target (nb plan, *Open questions*). nb speaks it
   since 2026-10-28 (`nb --acp`, `../nb/plans/atif-and-acp.md` stage 5), and
   `NbAgent` is a bridge target; the refusal was never coded, and what is
   left is step 7.

10. **The report's method section records, per arm**, agent, agent version,
    model, Harbor version, which limits were enforced, and the permission
    mode. With the costumes gone, a report can no longer compare a costume
    with the harness it imitates, so the brief's labelling rule for that
    (~:277–325) has nothing left to guard.

11. **Harness compliance is a product loop, not a risk to be mitigated.** The
    story proctor exists to tell is "we're fine on Codex, but the latest
    Claude Code has problems." That needs the harness version to be a
    dimension of the experiment, and it commits proctor to keeping up with
    every harness it supports, permanently. Harbor carries the adapters; proctor
    carries the judgment of whether they still tell the truth.
    - **An agent version is a pinned, named thing.** `agent_version` on an
      arm, compiled to Harbor's `version` kwarg and checked against
      `claude --version` (or the equivalent) in the trial. There is no
      floating `latest`.
    - **A version bump is an experiment.** The old and new versions run as
      two arms of the same agent on the same model, and the report is the
      changelog: "cc 2.1.285 → 2.1.290: two tasks regress". A Harbor bump is
      the same: one suite, old and new Harbor, the numbers must not move.
    - **Ingest is strict.** An ATIF step, tool name or ending proctor's
      per-agent mapping doesn't recognise fails the cell as `adapter_unmapped`
      (infrastructure), never a silent drop. A vendor or Harbor format change
      shows up on the first run after the bump.
    - **Each supported version has a conformance kit**
      (`Agents/<agent>/conformance/<version>/`): a recorded native stream,
      Harbor's ATIF for it, and the expected counted core. It covers each
      `kind` mapping, a denial, a subagent, a resume and each ending. The kit
      now tests two conversions, Harbor's native → ATIF and proctor's ATIF →
      core, and a disagreement with the native stream is a Harbor bug we file.
    - **Comparisons across versions are labelled.** The report refuses to
      pool samples from different `agent_version`s or Harbor versions into one
      arm.

12. **Machinery changes are proven deterministic before they are trusted.** A
    statistical A/A on `code-change` (3 tasks × 3 samples) cannot detect
    anything. The gate is an nb arm on the Mock provider run bare and through
    Harbor (nb as a Harbor agent). The two must give identical counted cores
    and identical `checks.json` apart from timestamps and durations. Then one
    real `code-change` sample on each vendor agent.

## Work

### 0. The spike (half a day, before anything lands)

From `learnings/harbor.md`, *Unverified*:

1. `uv tool run harbor@0.24.0 run -t hello-world/hello-world -a claude-code
   -m anthropic/<haiku>` on local docker, `ANTHROPIC_BASE_URL` at the
   gateway. Cost and wall time per trial.
2. One `code-change` task hand-written as a Harbor task dir, with a separate
   verifier. Run on cc and on `qwen-coder` against imp. Read its
   `trajectory.json` against what `Grade/Transcript.cs` needs (answer, tool
   calls, results, user turns) and its verifier against decision 8.
3. `network_mode = "allowlist"` with only the gateway host on this box
   (needs nftables for Harbor's egress sidecar).
4. A pinned `version` in a prebuilt image runs with no network at install
   time.
5. An external `BaseAgent` as `--user-agent` over the ACP bridge (decision 9).

A spike that fails 1–3 sends this plan back to the first draft's own runner.
A failure in 4 or 5 narrows the plan but does not reverse it.

**Run on 2026-10-15: go.** All five pass (`learnings/harbor.md`, *The spike,
run*). Step 5 narrows the oracle: Qwen Code cannot be a bridge target, and
the target's record in bridge mode is raw ACP history, not ATIF.

**Staging 2 landed on 2026-10-16** (proctor 0.2.5) for Claude Code: compile
(1), run (2) and ingest (3), with `suites/agent-smoke` (cc on Haiku,
`fix-null-deref` on `ledger`) as its suite. Where it differs from the text
below:

- **One Harbor job per cell, not one per experiment.** A cell's job is one
  trial of one task by one agent (`<cell>/harbor/job.json`), so a trial maps
  to its cell by construction, a cell's attempts and status stay proctor's,
  and `resume` reruns exactly the cells that did not complete. The cost is
  Harbor's concurrency (cells run one at a time, as nb cells do) and a few
  seconds of Harbor start-up per cell; one job per arm with
  `n_concurrent_trials` is the follow-on once the paid arms make wall time
  matter.
- **Tasks compile per (arm, task)**, under
  `runs/<id>/harbor/tasks/<arm>/<task>/`, because the image installs the
  arm's agent at its pin. Docker's layer cache shares the fixture layers.
- **The instruction is the task's prompt.** The program template is nb's;
  `system` → `append_system_prompt` and the tool directives are
  `Agents/ClaudeCode` (5). Permission mode is stated as `bypassPermissions`
  and every model alias as the arm's model, recorded in the manifest.
- **Limits:** `limits.wall_ms` only; the rest are "not yet".
- **The verifier runs script checks only.** It has no transcript, so a
  script check that reads `PROCTOR_TRANSCRIPT` (code-change's
  `stays-in-work`) cannot run there; it becomes a built-in over ATIF in (4).
  `grade` skips agent cells until (4).
- **`denied` is 0 by construction** under `bypassPermissions`; the denial
  mapping comes with `approval` (5).

**Staging 3 landed on 2026-10-16** (proctor 0.2.6): `grade` reads agent
cells. One departure: **nb is not converted to ATIF at ingest yet.**
`Transcript.Read` has two readers into the one `Transcript` the checks see,
nb's JSONL and ATIF plus `core.json`. Every experiment already under `runs/`
holds nb JSONL, so the nb reader stays for a release either way; the
conversion's point is the deterministic gate (8), which compares nb bare with
nb through Harbor, so it lands with `Agents/Nb` in staging 4. Script checks
are read from the verifier (decision 8), not rerun. `loop_nudged` on an agent
cell is `error` until the report has an n/a verdict (decisions 3 and 7). The
report still says nb where an agent arm ran (the arms table's harness and
provider, "nb ended", "tokens nb reported"); decision 10's method section is
what fixes that.

**Staging 4 landed on 2026-10-18** (proctor 0.3.0): nb as a Harbor agent,
nb's JSONL converted to ATIF at ingest, and the gate (`suites/gate`,
`GateTests`). Where it differs from the text below:

- **The path is chosen by `agent: nb`, not by the program's tools.** nb's
  tools are all on unless a program says `tools none`, so routing by tools
  now would move every Mock arm, `smoke`'s included, off the bare path
  (decision 1 says they stay). An arm without `agent` is bare, as before;
  `agent: nb` is Harbor. Decision 1's rule lands with validation refusing
  tools on the bare path, which is the same week `nb.runner` retires.
- **The nb binary is a layer, not an install.** `proctor.json` `harbor.nb`
  names nb's self-contained publish, built once into a local
  `proctor-nb:<content hash>` image that each task's Dockerfile copies
  `/opt/nb` from (132 MB copied into every task directory was the
  alternative). The pin is that hash, plus a check that the publish's
  version is the host nb's, which compiles the program.
- **nb's Harbor agent is a few dozen lines of Python** compiled from the
  assembly into `runs/<id>/harbor/agents/` and imported by path; there is
  no `Agents/` directory yet. The tool → kind table is in `Ingest.cs` beside
  Claude Code's. Fake tools and the oracle are not in it (7).
- **The nb reader stays.** Bare cells keep `transcript.jsonl` and are read
  directly; an nb cell in Harbor is read through ATIF. `NbAtifTests` holds
  every captured Mock transcript to the same windows both ways, which is
  the conversion's half of the gate without Docker.
- **The gate is opt-in** (`PROCTOR_GATE=<publish>`), since it needs Docker,
  uv and about ten minutes. It compares nb's transcript (minus the
  trailer's timings), the counted core (minus duration) and `checks.json`
  for five Mock tasks × two samples. The checkout's path is the one other
  difference, by construction (`write_file` reports the absolute path it
  wrote: a host directory bare, `/app` in the image), so the comparison
  maps one to the other. Green on 2026-10-18.

**Staging 5's code landed on 2026-10-18** (proctor 0.3.1): Codex and Qwen
Code as agents, `code-change` on all four, the porting guide's draft
(`docs/porting-to-agent-neutral.md`). The gateway did not resolve from the
dev box that day, so both agents were shaken down on `agent-smoke` with a
key that reaches nothing (each built, installed at its pin, started, and was
ingested).

**Its real run is in** (2026-10-20, proctor 0.3.2):
`20261020-0239-code-change-f3h0`, 24 of 24 cells completed, ungraded yet.
It tested the plumbing, not the vendors' models: OpenAI's route had a
10-call daily cap, so Codex ran against qwen3-coder-next on imp (its cells
still say `gpt-5-mini`, which llama-server ignores), through the new
`agents.codex.config` that turns off the two tools llama-server refuses.
Claude Code ran on Haiku. The run found nb's and Codex's unmapped tools
(0.3.2) and two loose ends in `project/todo.md` (`resume` and a changed
route; a bare hostname under musl). Codex on GPT-5 is still to run. Where it
differs from the text below:

- **Still no `Agents/` directory.** Each agent is a few lines in four
  switches (validation's vendor rule, compile's kwargs, env and install,
  ingest's tool kinds); a directory per agent waits for the conformance
  kits, which are what it would mostly hold.
- **Qwen Code is proctor's agent, subclassing Harbor's.** Harbor's
  reinstalls nvm, Node and the CLI from the network on every trial, image or
  no image; proctor's replaces that with a check of the image's version
  against the pin. Codex is Harbor's own, which skips the install at the pin.
- **No approval on either.** Harbor runs Codex with
  `--dangerously-bypass-approvals-and-sandbox` and Qwen Code with `--yolo`,
  with no setting for anything else; `denied` is 0 by construction, as on
  Claude Code. The *Open questions* on Codex's sandbox and denials stand.
- **`stays-in-work` leaves `code-change`** rather than becoming a built-in
  over ATIF: it reads the transcript, which the verifier has none of, and
  the container leaves nothing outside the checkout to guard.
  `suite-authoring`, still on the runner path, keeps it.
- **A vendor agent does not see the program template.** The nb arm's
  template tells it nobody will answer questions; the vendor arms get the
  task's prompt alone.

### 1. Compile (`Harbor/Compile.cs`)

A new pipeline stage between `Suite/` and `Run/`. `run` writes, under the
experiment, `harbor/tasks/<task>/` per proctor task:

- `instruction.md` from the prompt (nb's program compiled to a prompt; a
  program that uses nb-only directives on a non-nb arm is refused by the
  capability table, 5).
- `environment/Dockerfile` `FROM` the fixture's `image`, `COPY`ing only
  `repo/` into the workdir and committing it (proctor's checkout, moved into
  the image). Fixture setup that was a `sample-setup` hook becomes Dockerfile
  steps and a `healthcheck`.
- `tests/` with the verifier container: the fixture's and the task's script
  checks, the merged expect block, the `PROCTOR_*` contract mapped to verifier
  env (decision 8).
- `task.toml`: `[agent] timeout_sec` from `wall_ms`, `cpus`/`memory_mb`,
  network policy (`public` for setup, `allowlist` of the model endpoint for
  the agent, `no-network` for the verifier), `[artifacts]` naming the
  workdir and a `git diff` against the fixture commit.

Then `harbor/job.json`: one `agents:` entry per tool-using arm (name, model,
`version`, kwargs, native config from the bundle), `n_attempts` = samples. The
compiled tree is hashed into `experiment.json` like the suite.

### 2. Run (`Run/Harbor.cs`)

`uv tool run harbor@<pin> run --config harbor/job.json --jobs-dir harbor/jobs`
with `HARBOR_TELEMETRY=off` and the model key in the environment of that one
process. `Subprocess.Run` gains a timeout in the same change (nothing in
proctor waits forever). `resume` maps to Harbor's job resume where the job
dir exists, else re-runs only the cells without a trial. Tool-less nb arms
take the existing `RunCell` path in the same experiment.

### 3. Ingest (`Run/Ingest.cs`)

Each trial becomes a cell under `<arm>/<task>/<sample>/`, the layout
unchanged:

- `trajectory.json` (ATIF) and the native log, `diff.patch` from the
  artifacts, the verifier's `reward.json`.
- The cell manifest: agent, agent version (as the trial reported it, checked
  against the pin), model, Harbor version, timings, tokens and cost from
  `result.json`, limits enforced.
- The counted core and `kind` per decision 3, strict (decision 11).
- `exception_info` with no agent result → `failed` with Harbor's reason; a
  timeout → `completed` with `time_budget`. Proctor's rule holds: `failed` is
  infrastructure.

### 4. The grader reads ATIF (`Grade/Transcript.cs`)

The windows (answer, answer JSON, tool calls, tool results, user turns,
diff) are built from ATIF steps plus the counted core. nb's JSONL is converted
at ingest, so `Transcript.cs` has one input. The captured Mock transcripts in
`Proctor.Tests/fixtures/` gain their ATIF conversion, and the snapshot tests
must not move.

### 5. `Agents/<agent>/`: what proctor still knows per agent

Each holds the arm compilation (kwargs, permission mode, tool lists, model
aliases, the vendor rule), the ATIF → core mapping (tool `name` → `kind`,
counted-field rules, ending map), the capability table (which compiled nb
directives it refuses), and the conformance kits. No invocation code and no
stream parsing: those are Harbor's.

- **ClaudeCode.** Refuses `harness`, fabricated history, `seed`, non-Anthropic
  models. `system` → `append_system_prompt`; tool directives →
  `allowed_tools`/`disallowed_tools`; `approval` → `permission_mode` other
  than bypass. Every model alias is set explicitly. API-key auth only, which
  matters because Harbor's fresh `CLAUDE_CONFIG_DIR` per trial gives the
  clean account state (observed 2026-09-29: a logged-in cc volunteered
  claude.ai connector status).
- **Codex.** Same shape. OpenAI models only, approval policy and sandbox
  from `approval`. Codex's sandbox inside the container is still unverified
  (*Open questions*).
- **QwenCode.** Qwen models only, `OPENAI_BASE_URL` at imp or the gateway.
- **Nb.** A Harbor installed agent (`harbor/nb_agent.py`, a few dozen lines
  of Python, excluded from compilation like the data directories). It installs
  the pinned nb binary, pipes the host-compiled `program.jsonl` on stdin, and
  syncs nb's JSONL as the native log. `budget` directives are refused; limits
  are decision 6's.

### 6. The bare path

Today's `RunNb` without a runner, unchanged, for tool-less nb arms.
Validation: an nb arm whose compiled program grants any tool must take the
Harbor path. `nb.runner` and `nb.mounts` go (Retires).

### 7. Oracle (`Harbor/oracle_user.py` + `nb --judge`)

Decision 9. Gate: on the Mock provider, the oracle as a simulated user over
an nb arm and nb's own in-process oracle loop produce the same counted core
and user turns apart from bookkeeping. That needs nb as an ACP target. Until
then, the gate is a cc arm on Haiku over one recorded oracle task, checked
by hand, and the `oracle_*` checks are stubbed on Harbor arms only. nb is
an ACP target since 2026-10-28, so the Mock gate can be built with step 7. `nb --judge` shipped 2026-10-29 (`../nb/docs/conversation-program-cli.md` §8.3): the message on stdin as text, `--sheet`, `--provider <entry>` for the judging entry, and one JSON line out (`verdict` hit/done/miss, `keys`, `answer` on a hit, `raw`, `usage`, `warnings`). Every verdict exits 0; a failed call exits 2. Known cost: each answer is a new turn over ACP
rather than nb's in-process continuation, which is close to what a real user
answering in `-p` mode gets. It is recorded, not corrected.

### 8. The deterministic gate

Decision 12, as a test. Nothing past step 3 of *Staging* lands until it is
green.

### 9. Project shape, after

| Stage | Arms |
|---|---|
| 1 | qwen on real Qwen Code against imp. Free, and through the same Harbor path as the paid arms. |
| 2 | One pass each on real cc (Anthropic) and real Codex (OpenAI), cost-capped. On a new fixture, a pass on the cheapest model of each (Haiku, mini) first, for cents. |
| 3 | n fixtures × {cc, Codex, Qwen Code} × 3, report to PM; method section labels every arm. |
| side | Public Harbor datasets (a Terminal-Bench 2.0 subset) as proctor suites, reported with intervals. Harbor's leaderboards show only means, so this is the demo. |

### 10. Porting guide (`docs/porting-to-agent-neutral.md`)

Written last, by porting `suites/code-change` with an agent that has only the
draft guide, and treating where it stalls as the edits. Short by design:

- A suite whose arms have no tools runs as before.
- A suite with tool-using nb arms needs `image` on its fixtures, the
  `sample-setup` hook's work moved into the Dockerfile, `nb.runner`/`nb.mounts`
  removed, and `budget` → `limits`.
- An arm with `harness: claude-code | codex | qwen-code` becomes
  `agent: claude-code | codex | qwen-code` with the vendor's model. This is the
  change most likely to move numbers: the first run after porting is a new
  baseline for that arm, and its old `baseline.json` entries are dropped,
  not carried.
- Every validation error and capability refusal names the guide section that
  covers it.

## Staging

1. Spike (0). Go or no-go for the plan.
2. Subprocess timeout; compile (1), run (2), ingest (3) for a cc arm on Haiku,
   one fixture.
3. The grader on ATIF (4) and nb converted at ingest; the snapshot tests
   unchanged.
4. `Agents/Nb` in Harbor and the deterministic gate (5, 8). Nothing else
   lands until green.
5. `Agents/Codex` and `Agents/QwenCode` (5); `code-change` on all four
   agents; the porting guide drafted from that move.
   Checking proctor's layer over Harbor is `harbor-conformance.md`, at $0.
6. Limits (decision 6) end to end; `max_cost` stops being stubbed and the
   report's "Cost is not shown" goes.
7. Oracle (7). nb ships `--judge` since 2026-10-29.
8. Refuse `harness` in validation, in the same week nb retires the costumes
   (nb plan, staging 5). Not before step 5 has run a real suite: retiring
   the old route before the new one works leaves none.
9. Guide finished by an agent porting a suite cold.
10. The first deliberate version bump of cc, run as a two-arm experiment
    (decision 11). This rehearses the compliance loop before it's needed in
    anger.

## Retires

- From the first draft, never built: `Run/Container.cs`, the per-vendor
  stream adapters, the podman-only lifetime and exec model, and the live
  supervisor (deferred, decision 6).
- `nb.runner`, `nb.mounts`, `runners/container.sh`, the `podman run` in
  sample-setup hooks. Fixture setup moves into the image.
- `runner: "command"` as designed.
- `harness` on arms, all three costumes: `claude-code`, `codex` and
  `qwen-code`. Validation in `Suite/Suite.cs` ~340 and `SuiteTests` refuse the
  field.
- nb's transcript schema as the lingua franca; ATIF plus proctor's counted
  core replaces it.
- `budget`/`loop` as proctor-enforced limits via nb.
- The `max_cost` and `oracle_*` stubs; the "Cost is not shown" line.
- `containerised-runs.md`'s *What proctor does not do*.

## Risks

From `learnings/harbor.md`, accepted for this route:

- **A Python dependency** (uv, Harbor) plus Docker on any machine that runs
  tool-using arms. The bare path needs neither.
- **Upstream churn.** Seven minor releases in three months; ATIF 1.7 broke
  1.6. Mitigated by the pin, the bump-as-experiment rule and the conformance
  kits, not avoided.
- **Adapter fidelity is Harbor's.** The kits catch drift; fixing it means an
  upstream PR or a local patch we carry.
- **Telemetry** is on by default upstream; proctor turns it off on every
  invocation, and a test asserts the variable is set.

## Open questions

- **Live limits.** Harbor's `--stream` follows cc and Codex steps live on
  local docker. Can proctor read that stream and stop a trial on a trip, or
  does a live supervisor need its own hook into Harbor?
- **The oracle bridge** (spike 5). If an external agent cannot be the ACP
  user, the fallback is a multi-step task with resume, one step per expected
  question. That only fits oracles whose questions are known in advance.
- **Scoped gateway tokens.** Can the gateway mint per-cell tokens with a
  spend cap and TTL? If not, the shared key goes into the agent's
  environment for the trial and the manifest says so.
- **Cost of real cc per task.** Not measured. "hello" on Opus cost $0.10,
  because cc sends ~22k prompt tokens before doing anything. Budget stage 3
  of the project shape from the first real `code-change` run.
- **Codex's own sandbox** inside the container: its `--sandbox` modes may
  not work there. If only `--dangerously-bypass-approvals-and-sandbox` runs,
  approval on Codex arms is observation-only and the capability table says so.
- **Codex denials in ATIF.** Whether Harbor's conversion keeps denied calls
  distinctly enough to fill `approved: false`. Check against a recording
  before promising `no_denials` on Codex arms.
- **nb emitting ATIF.** Converting at ingest is enough to start. nb writing
  ATIF itself would make the bare and Harbor paths identical by construction.
  That is nb's call, in its plan. Answered 2026-10-27: nb writes it
  (`--trajectory`) and `NbAtif` is deleted, in `../nb/plans/atif-and-acp.md`
  stage 3, which also schedules nb as an ACP bridge target (stage 5). Done
  2026-10-28.
