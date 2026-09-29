---
type: plan
title: Agent-neutral runs — podman always, any harness an arm, limits and the oracle in proctor
created: 2026-09-29
status: draft; coordinated with nb (`../nb/plans/agent-neutral-runs.md`)
---

# Agent-neutral runs

nb's half is `../nb/plans/agent-neutral-runs.md`: the locked transcript
schema, a per-turn usage event, `nb --judge`, `nb --serve-fake-tools`. This
file holds the design and the rest of the work.

## Why this plan exists

Evals increasingly need bash, and once bash is open nb's own controls stop
controlling anything; the guardrails already moved outward into podman.
Proctor's arms today are nb plus costumes of other harnesses
(`harness: claude-code`, `harness: codex`). Simulating Claude Code gets harder
every release as it entangles with Anthropic's other products; the honest way
to ask "does this ship in Claude Code?" is to run Claude Code.

Proctor's reports talk about runs, arms, checks and tasks, and never say nb.
That freedom is what this plan spends: the machinery under an arm changes,
the report does not, and the numbers must not move for machinery reasons.

What proctor lacks today (survey 2026-09-29), all of which this needs:

- No timeout or kill: `Run/Subprocess.cs:48` waits forever.
- No container limits; container composition is a suite convention
  (`hooks/sample-setup.sh`, `runners/container.sh`), not proctor code.
- Keys enter the container (`-e LLM_GATEWAY_KEY`).
- `podman exec` output arrives at the end; nothing reads it early.
- `runner: "command"` designed, not built (`Suite/Suite.cs` ~342).
- `max_cost` stubbed (`Grade/Checks.cs:90`) — though nb's trailer now
  carries `cost` when the entry declares a price.
- Limits live in `program.nb` (`budget`, `loop`) and are enforced by nb, so
  they vanish on any arm that is not nb.

## Decisions

1. **Podman is mandatory.** Every cell runs in a container proctor composes.
   There is no host run and no runner-script indirection. Agents avoid
   containers unless nagged and every project ends up needing one, so the
   default is the only path. This retires the brief's *the test environment
   is not our problem* for the container specifically, and
   `containerised-runs.md`'s *What proctor does not do*.

2. **An arm names an agent, and the agent fixes the vendor.**
   `"agent": "nb" | "claude-code" | "codex"`, absent meaning `nb`, so classic
   suites run unchanged. `claude-code` takes Anthropic models only, `codex`
   OpenAI only; validation refuses the rest and no flag overrides it. A real
   harness with another vendor's brain answers a question nobody asked. Only
   `nb` takes any provider, and `harness` is valid only on an `nb` arm, as
   `nb` or `qwen-code`. `runner: "command"` is retired before it ships.

   **One route per harness.** `harness: claude-code` and `harness: codex` are
   refused with a pointer to `agent:`, and nb retires both costumes
   (its plan, item 5). A consumer agent reading proctor's docs and nb's
   finds exactly one way to test in Claude Code: run Claude Code.

3. **Every agent's output is normalised to nb's transcript schema by an
   adapter in proctor, read from stdout.** stdout is out of the model's reach
   once proctor holds it; a file inside the container is not. The grader
   (`Grade/Transcript.cs`) does not change. It is the definition of the
   minimum the schema doc locks.

4. **One home per agent: `Agents/<Agent>/`.** Each holds the invocation, the
   stream adapter, the capability table (which compiled directives it
   refuses) and the version pin. Vendor-specific knowledge lives nowhere else.

5. **Limits are run policy in `suite.json`, enforced by a supervisor for every
   arm.** `tool_calls`, `wall_ms`, `tokens`, `cost_usd`. The supervisor reads
   the normalised stream, and on a trip kills the container and writes the
   trailer with the shared exit reason. Same enforcer, same exit reasons, for
   every agent. `budget` in a suite's program is refused with a pointer to the
   porting guide.

6. **`approval` and `loop` stay agent behaviour, not limits.** `approval` is a
   refusal the model sees; each agent gets its own mechanism, configured from
   the compiled directives (nb natively, cc `--allowedTools` with
   `--permission-prompts none`, Codex its approval policy). `loop` is an nb
   nudge; other agents have nothing equivalent, and `not_nudged` reports n/a
   on them rather than pass.

7. **Proctor drives the oracle loop, for every arm, from outside the
   container.** The answer sheet stays on the host. See *Oracle*.

8. **Machinery changes are proven deterministic before they are trusted.** A
   statistical A/A on `code-change` (3 tasks × 3 samples) cannot detect
   anything. The gate is a Mock-provider run through old and new machinery
   producing identical transcripts apart from timestamps and durations, plus
   one real smoke run.

9. **The report's method section records agent, agent version and model per
   arm**, and for an nb arm its harness (`nb` or `qwen-code`). With the
   cc and Codex costumes gone, a report can no longer compare a costume with
   the harness it imitates, so the labelling rule the brief needed for that
   (~:277–325) has nothing left to guard.

## Work

### 1. The container runner (`Run/Container.cs`)

Replaces `hooks/sample-setup.sh` + `runners/container.sh` + `nb.runner` +
`nb.mounts`.

- **Image.** The suite or fixture names one (`"image"`), built `FROM`
  proctor's base: `suites/runners/Containerfile` with pinned nb (the
  `COPY --from` layer), cc and Codex. The manifest records all three versions.
- **Lifetime.** One container per sample, created at sample setup
  (`sleep infinity`), torn down after grading. Each agent turn is a
  `podman exec` into it, which is what makes resume work (the agent's session
  state persists in the container's `HOME` between execs).
- **Mounts.** Work dir at `/work`, bundle read-only at `/bundle`, NuGet cache
  volume. Nothing else.
- **Limits.** `--memory`, `--cpus`, `--pids-limit` from the suite with
  defaults; read-only root with tmpfs `/tmp` and a writable `HOME`.
- **Network.** Default bridge plus `--add-host` for the gateway, as today.
  A pod with one egress is a follow-on, not this plan.
- **Keys.** One env var into the exec, never into the image. If the gateway
  can mint per-cell tokens with a spend cap and TTL, proctor mints and revokes
  them; if not, the shared key and a manifest line saying so. (Unverified:
  the gateway is Cloudflare-shaped, `/cf/compat/v1`.)
- **Streaming.** stdout read per line as it arrives and written through to
  `transcript.raw.jsonl` and the adapter. Try `podman exec` without buffering
  first, then a pty, then a FIFO on a host mount.
- **Kill.** `podman kill` on a supervisor trip; teardown still collects
  `diff.patch`.
- **Hooks and checks run in the container too** (`PROCTOR_EXEC`), resolving
  `containerised-runs.md`'s open question: fixtures stop assuming a host
  toolchain.

`Subprocess.Run` gains a timeout in the same change. Nothing in proctor waits
forever.

### 2. The deterministic gate

`suites/smoke` on the Mock provider, through the current path and through
step 1. Transcripts must match apart from timestamps and durations. Then one
real `code-change` floor sample on the new path. Nothing else lands until
this is green; it becomes a test.

### 3. The supervisor (`Run/Supervisor.cs`)

Reads the normalised stream across every exec in a sample (limits are
per sample, not per process; oracle resumes accumulate). Counts tool calls,
tokens and cost from `usage` events (nb's new per-turn event; cc's per
message; Codex's per turn), wall clock from its own clock excluding oracle
judge time. On a trip: kill, write the `result` trailer with
`max_tool_calls` / `time_budget` / `token_budget` / `cost_budget`.

`suite.json`:

```json
"limits": { "tool_calls": 60, "wall_ms": 1800000, "tokens": null, "cost_usd": 2.00 }
```

`max_cost` stops being stubbed; the report's "Cost is not shown" goes.

### 4. `Agents/Nb`

- Compile on the host (`nb --compile`), refuse `budget` directives.
- Invocation: `podman exec -i … /opt/nb/nb --output jsonl --config … -`.
- Adapter: identity (nb writes the schema), plus `agent`/`agent_version`
  in the trailer.
- MCP config gains `nb --serve-fake-tools` when the fixture has
  `fake-tools.yaml`.

### 5. `Agents/ClaudeCode`

- **Capability table.** Reads the compiled program and refuses: `harness`,
  fabricated history, `seed`, `budget`, non-Anthropic `provider`/`model`.
  Maps `system` to `--append-system-prompt`, tool-surface directives to
  `--allowedTools`/`--disallowedTools`, `approval` to allow rules plus
  `--permission-prompts none` (anything unlisted is denied and lands in
  `permission_denials`).
- **Invocation.** `claude -p --output-format stream-json --verbose --model …`.
  First turn with the prompt; oracle turns with `--resume <session_id>` and
  the answer.
- **Clean account state.** API-key auth only, fresh `HOME`, no user or
  project settings beyond what proctor writes (`--setting-sources` limited to
  proctor's `--settings` file), `--strict-mcp-config` with proctor's
  `--mcp-config`. Observed 2026-09-29: cc on a logged-in account
  volunteered claude.ai connector status in a reply to "hello there". An arm's
  result must not depend on whose credentials built the image. Manifest
  records auth mode and cc version.
- **Adapter** (`StreamJsonAdapter.cs`): `assistant` text blocks →
  `assistant_text`; `tool_use` → `tool_call`; `user` `tool_result` →
  `tool_result`; per-message `usage` → `usage`; `result` → trailer
  (`num_turns`, `total_cost_usd`, `permission_denials` → `denied` and the
  denied calls' `approved: false`, `session_id` kept for resume). Unit-tested
  against recorded stream-json under `Proctor.Tests/fixtures/claude-code/`.
- **No mod.** An earlier draft kept one to capture `prompt.compose` for
  calibrating nb's `claude-code` costume; the costume is retired, so it has
  no job. Mods stay a known tool if a suite ever needs something stream-json
  cannot show.

### 6. `Agents/Codex`

Same shape. `codex exec --json`, resume with `codex exec resume <id>`, OpenAI
models only, approval policy and sandbox mode from the compiled `approval`
directives, clean `HOME` and `CODEX_HOME`. Adapter unit-tested against
recorded output under `Proctor.Tests/fixtures/codex/`.

### 7. Oracle (`Run/Oracle.cs`)

When the compiled program carries an `oracle` sheet:

1. The agent's exec ends at the end of its turn.
2. Proctor runs `nb --judge --sheet <host path>` on the transcript so far.
3. `hit` → the answer is the next user turn: cc `--resume`, Codex
   `exec resume`, nb re-run seeded with the transcript so far. The adapter
   marks it `user{source: "oracle", keys}`, as nb does in-process.
4. `done` ends the sample; `miss` records a miss and ends it (as nb does).
5. Stops at the suite's `oracle_turns` limit with `oracle_budget`.

The sheet never enters the container. Today `--compile` embeds it in the
program nb reads inside the box; under this plan proctor strips the `oracle`
event before handing the program in.

Gate: on the Mock provider, the outside loop over an nb arm and nb's own
in-process loop produce the same transcript apart from bookkeeping. The
`oracle_*` checks stop being stubbed once this passes.

Known cost: each answer is a new process. A cc resume reloads the session
(prompt cache limits the price) and may re-inject reminders a continuous
session would not. Close to what a real user answering in `-p` mode gets;
recorded, not corrected.

### 8. Project shape, after

| Stage | Arms |
|---|---|
| 1 | qwen on nb. The only free arm, and the only one where harness and model are decoupled. |
| 2 | One pass each on real cc (Anthropic) and real Codex (OpenAI), cost-capped. On a new fixture, a pass on the cheapest model of each (Haiku, mini) first, for cents. |
| 3 | n fixtures × {cc, Codex, qwen} × 3, report to PM; method section labels every arm. |

Stage 2 used to be the costumes (~$1–2 each). It becomes the real harnesses
at about the same price: the costumes ran the same vendor models, so the
tokens were never the free part.

### 9. Porting guide (`docs/porting-to-agent-neutral.md`)

Written last, by porting `suites/code-change` with an agent that has only the
draft guide and treating where it stalls as the edits. Short by design:

- A suite with no `agent` field runs as before *once its container fields are
  filled*; the mandatory changes are podman (`image`, removing
  `nb.runner`/`nb.mounts`/the setup hook's `podman run`) and `budget` →
  `limits`.
- An arm with `harness: claude-code` or `harness: codex` must become
  `agent: claude-code` / `agent: codex` with the vendor's model. This is the
  third mandatory change, and the one most likely to move numbers: the report
  of the first run after porting is a new baseline for that arm, not a
  continuation of the old one. The guide says so, and `baseline.json`
  entries for the old arm id are dropped rather than carried.
- Adding a real-harness arm to a suite that had none is optional and is its
  own section.
- Every validation error and every capability refusal names the guide
  section that covers it. The guide is mostly the index those messages point
  into.

## Staging

1. Container runner + subprocess timeout (1).
2. Deterministic gate (2). Nothing else lands until green.
3. Supervisor (3), after nb ships per-turn usage.
4. `Agents/Nb` (4); code-change moves to the new path; porting guide drafted
   from that move.
5. `Agents/ClaudeCode` (5) on Haiku, one fixture.
6. `Agents/Codex` (6) on the cheapest OpenAI model.
7. Oracle (7), after nb ships `--judge`.
8. Refuse `harness: claude-code|codex` in validation, in the same week nb
   retires the costumes (nb plan, staging 5). Not before steps 5 and 6 have
   run a real suite: retiring the old route before the new one works leaves
   none.
9. Guide finished by an agent porting a suite cold.

## Retires

- `nb.runner`, `nb.mounts`, `runners/container.sh`, the `podman run` in
  sample-setup hooks. Hooks remain for fixture setup, run inside the
  container.
- `runner: "command"` as designed.
- `harness: claude-code` and `harness: codex` on nb arms (validation in
  `Suite/Suite.cs` ~340 and `SuiteTests` narrow to `nb | qwen-code`); the
  brief's open question of real CLI vs costume per arm is closed: real.
- `budget`/`loop` as proctor-enforced limits via nb.
- The `max_cost` and `oracle_*` stubs; the "Cost is not shown" line.
- `containerised-runs.md`'s *What proctor does not do*.

## Open questions

- Scoped gateway tokens: can the gateway mint them? If not, add it or accept
  the shared key for now.
- Streaming out of `podman exec`: which of unbuffered / pty / FIFO works.
- Whether judge time should count against `wall_ms`. This plan says no;
  revisit if a suite leans on the oracle hard enough for it to matter.
- Codex's own sandbox inside a rootless podman container: its `--sandbox`
  modes may not work there, and its help names
  `--dangerously-bypass-approvals-and-sandbox` for externally sandboxed
  environments, which ours is. If that is the only mode that runs, approval
  on Codex arms is observation-only and the capability table says so.
- Codex's `--json` stream: whether it reports denied calls distinctly enough
  to fill `approved: false`. Check against a recording before promising
  `no_denials` on Codex arms.
