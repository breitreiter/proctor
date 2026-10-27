# Porting a suite to agent arms

Draft, written from porting `suites/code-change` (proctor 0.3.1). An arm with
`agent` runs through Harbor in the fixture's image; an arm without one is nb
run bare on the host, as before. This guide is what changes when a suite
moves its arms to the first kind. The design is
`project/plans/agent-neutral-runs.md`.

## 1. A suite with no agent arms

Runs as before. Nothing here applies, and validation refuses `harbor` and
`limits` on it, since nothing runs through Harbor.

## 2. The machine: Docker or podman, uv, and proctor.json

An agent arm needs a container engine and uv on the machine that runs it;
the bare path needs neither. The engine is Docker (with the Compose and
buildx plugins) or rootless podman, whose setup is
[`podman.md`](podman.md). In `suites/proctor.json`:

- `harbor.engine`: `docker` (the default) or `podman`. An experiment keeps
  the engine it started on; `resume` refuses another.

- `agents.<agent>`: `base_url` and `api_key` for each vendor agent the suite
  names (`claude-code` an Anthropic route, `codex` and `qwen-code` an
  OpenAI-dialect one). The base URL's host is the only name the agent's
  container may reach, matched exactly as spelled.
- `harbor.nb`: nb's self-contained linux-x64 publish, for an `agent: nb` arm.
  It must be the same nb as `nb.path` (`run` refuses another version), since
  the host compiles the program and the image runs it.

## 3. The fixture: an image

Every fixture a task of the suite names needs `image`:

```json
"image": {
  "from": "mcr.microsoft.com/dotnet/sdk:10.0",
  "env": { "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1" },
  "run": ["find . -name '*.csproj' -exec dotnet restore -v q {} \\;"]
}
```

`run` steps run in the checkout at image build, which is the only phase with
public network: the agent reaches only its model's host, and the verifier
reaches nothing. So anything a build or a test needs from the network
(packages, tools) is fetched here. The fixture's `repo/` is copied in and
committed; nothing else of the fixture enters the image the agent runs in.

## 4. The suite: Harbor, limits, and no runner

- Add `"harbor": {"version": "0.24.0"}` (pinned; `resume` refuses a change)
  and `"limits": {"wall_ms": …}` (Harbor's agent timeout; a run that hits it
  is completed with `time_budget`, not failed).
- Remove the `nb` block (`runner`, `mounts`). The image is the container now,
  and `{{work}}` is `/app` for an nb arm.
- Remove the sample hooks. A sample hook runs on the host, which the agent's
  container never sees, so it is refused; what the setup hook did (build an
  image, warm a package cache, start a container) is the fixture's image.
  Arm hooks still run on the host, once per arm: `code-change` keeps
  `ensure-model.sh`, which puts the local model on the model box.
- A file another suite borrows from this one (`code-change`'s teardown hook
  and `stays-in-work.sh` were `suite-authoring`'s too) moves to the suite that
  still uses it, rather than going with the port.

## 5. Checks

- **A script check runs in Harbor's verifier**, a fresh copy of the image
  with the run's diff applied, offline. It sees the container's paths
  (`PROCTOR_WORK` is `/app`, the suite at `/tests/suite`, the fixture at
  `/tests/fixture`) and keeps the 0/1/2 exit contract. The cell, the sample,
  nb and the runner are empty there.
- **There is no transcript in the verifier.** A script check that reads
  `PROCTOR_TRANSCRIPT` cannot run; it is `error` on every agent cell.
  `code-change`'s `stays-in-work` was one, and it was dropped rather than
  ported: its point was that the repository root held the suite, the tasks
  and the checker, and in the agent's container none of them exist. The
  fixture's checks and the expectations are in the verifier only, by
  construction.
- **Built-in and model checks are unchanged**: `grade` reads an agent cell's
  ATIF trajectory and proctor's own count (`core.json`) into the same
  windows. A budget check reads proctor's count, never the agent's.
- **What only nb does** has nothing to read on a vendor agent's cell:
  `loop_nudged` is `error` there, and `denied_calls` is 0 by construction,
  since Harbor runs every vendor agent with approvals bypassed.

## 6. Arms

- **An nb arm** keeps `harness`, `provider` and `model`, drops `runner`, and
  gains `"agent": "nb"`. Its provider entry in nb's config must name an
  `Endpoint` whose host the container may reach; `${VAR}`s in it go to the
  container by value and the key by name. The program template still applies.
- **A vendor arm** is `agent` (`claude-code`, `codex`, `qwen-code`), a pinned
  `agent_version`, and a bare `model` of that vendor: Anthropic for Claude
  Code, OpenAI for Codex, Qwen for Qwen Code, with no provider prefix. The
  nb fields (`runner`, `harness`, `provider`) are refused.
- **The program template does not reach a vendor agent.** Its instruction is
  the task's prompt, nothing else: the template's `system`, `approval`, `loop`
  and `budget` are nb's, and each vendor agent runs with its own system
  prompt and its own tools. `code-change`'s template still tells the nb arm
  that nobody will answer questions; a vendor arm in `-p` mode is not told.
- An arm that was `harness: claude-code | codex | qwen-code` (nb in a
  costume) becomes `agent: claude-code | codex | qwen-code` with the vendor's
  model. This is the change most likely to move numbers: its first run is a
  new baseline for that arm.

## 7. Baselines

A baseline pins cells by arm id. An arm whose route changed is a new
measurement: drop its entries from `baseline.json` rather than compare across
the move. `code-change` keeps its `floor` entries, because the floor is the
same nb on the same model and the deterministic gate (`suites/gate`) shows nb
bare and nb through Harbor read the same; its first run after the port says
whether that held on a real model.
