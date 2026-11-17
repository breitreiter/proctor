# Porting an exam to agent variants

Draft, written from porting `exams/code-change` (proctor 0.3.1). A variant with
`agent` runs through Harbor in the fixture's image; a variant without one is nb
run bare on the host, as before. This guide is what changes when an exam
moves its variants to the first kind. The design is
`project/plans/agent-neutral-runs.md`.

## 1. An exam with no agent variants

Runs as before. Nothing here applies, and validation refuses `harbor` and
`limits` on it, since nothing runs through Harbor.

## 2. The machine: Docker or podman, uv, and proctor.json

An agent variant needs a container engine and uv on the machine that runs it;
the bare path needs neither. The engine is Docker (with the Compose and
buildx plugins) or rootless podman, whose setup is
[`podman.md`](podman.md). In `exams/proctor.json`:

- `harbor.engine`: `docker` (the default) or `podman`. An interrupted run keeps
  the engine it started on; `run` continues it only on that engine.

- `agents.<agent>`: `base_url` and `api_key` for each vendor agent the exam
  names (`claude-code` an Anthropic route, `codex` and `qwen-code` an
  OpenAI-dialect one). The base URL's host is the only name the agent's
  container may reach, matched exactly as spelled.
- `harbor.nb`: nb's self-contained linux-x64 publish, for an `agent: nb` variant.
  It must be the same nb as `nb.path` (`run` refuses another version), since
  the host compiles the program and the image runs it.

## 3. The fixture: an image

Every fixture a task of the exam names needs `image`:

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

## 4. The exam: Harbor, limits, and no runner

- Add `"harbor": {"version": "0.24.0"}` (pinned; `run` does not continue an interrupted run started under another version)
  and `"limits": {"wall_ms": …}` (Harbor's agent timeout; an attempt that hits it
  is completed with `time_budget`, not failed).
- Remove the `nb` block (`runner`, `mounts`). The image is the container now,
  and `{{work}}` is `/app` for an nb variant.
- Remove the attempt hooks. An attempt hook runs on the host, which the agent's
  container never sees, so it is refused; what the setup hook did (build an
  image, warm a package cache, start a container) is the fixture's image.
  Variant hooks still run on the host, once per variant: `code-change` keeps
  `ensure-model.sh`, which puts the local model on the model box.
- A file another exam borrows from this one (`code-change`'s teardown hook
  and `stays-in-work.sh` were `exam-authoring`'s too) moves to the exam that
  still uses it, rather than going with the port.

## 5. Checks

- **A script check runs in Harbor's verifier**, a fresh copy of the image
  with the attempt's diff applied, offline. It sees the container's paths
  (`PROCTOR_WORK` is `/app`, the exam at `/tests/exam`, the fixture at
  `/tests/fixture`) and keeps the 0/1/2 exit contract. The attempt's directory and
  number, nb and the runner are empty there.
- **There is no transcript in the verifier.** A script check that reads
  `PROCTOR_TRANSCRIPT` cannot run; it is `error` on every agent attempt.
  `code-change`'s `stays-in-work` was one, and it was dropped rather than
  ported: its point was that the repository root held the exam, the tasks
  and the checker, and in the agent's container none of them exist. The
  fixture's checks and the expectations are in the verifier only, by
  construction.
- **Built-in and model checks are unchanged**: `grade` reads an agent attempt's
  ATIF trajectory and proctor's own count (`core.json`) into the same
  windows. A budget check reads proctor's count, never the agent's.
- **What only nb does** has nothing to read on a vendor agent's attempt:
  `loop_nudged` is `error` there, and `denied_calls` is 0 by construction,
  since Harbor runs every vendor agent with approvals bypassed.

## 6. Variants

Each variant is its own file, `variants/<id>.json`, and porting one edits it,
which makes it a new variant: once the old one has made attempts, `run`
refuses the edit under the same id, so the ported variant takes a new id
(and a new file).

- **An nb variant** keeps `harness`, `provider` and `model`, drops `runner`, and
  gains `"agent": "nb"`. Its provider entry in nb's config must name an
  `Endpoint` whose host the container may reach; `${VAR}`s in it go to the
  container by value and the key by name. The program template still applies.
- **A vendor variant** is `agent` (`claude-code`, `codex`, `qwen-code`), a pinned
  `agent_version`, and a bare `model` of that vendor: Anthropic for Claude
  Code, OpenAI for Codex, Qwen for Qwen Code, with no provider prefix. The
  nb fields (`runner`, `harness`, `provider`) are refused.
- **The program template does not reach a vendor agent.** Its instruction is
  the task's prompt, nothing else: the template's `system`, `approval`, `loop`
  and `budget` are nb's, and each vendor agent runs with its own system
  prompt and its own tools. `code-change`'s template still tells the nb variant
  that nobody will answer questions; a vendor variant in `-p` mode is not told.
- A variant that was `harness: claude-code | codex | qwen-code` (nb in a
  costume) becomes `agent: claude-code | codex | qwen-code` with the vendor's
  model. This is the change most likely to move numbers: its first attempts are a
  new measurement of that variant. It is not optional: nb refuses the costume names
  since 2026-10-28, so an unported variant fails at compile with the same redirect.
- **An oracle task keeps its sheet on an nb variant, and loses its vendor variants.**
  On `agent: nb` the oracle is a simulated user that drives nb over Harbor's
  ACP bridge, so the template must end in exactly `run {{prompt}}`, which the
  user sends. A vendor variant beside an oracle task is refused. Three things
  differ from the bare run, and a ported exam's numbers can show them:
  - **`budget tool_calls` and the loop detector count per answer.** Each
    answer is a new ACP prompt, not a continuation of one `run`, so the
    budget starts again on every answer.
  - **The judge's tokens are not in the attempt's.** Bare, nb charges them to the
    attempt. Bridged, they're the manifest's `oracle` (`judge_input_tokens`,
    `judge_output_tokens`), so an oracle variant's `input_tokens` drops by them.
  - **The agent can see the bridge's `.acpxrc.json` in its checkout.** The
    diff leaves it out.

## 7. The live variant

A ported variant is a changed variant, so it takes a new id (a changed
variant under an id that made attempts is refused), and its attempts are
made again. If the old one was live and you retire its file, `live.json`
names a variant with no file and every verb says so; mark the ported one live with `proctor live`
once you trust it, rather than compare across the move. The deterministic
gate (`exams/gate`) shows nb bare and nb through Harbor read the same, so an
nb variant moved to `agent: nb` is the closest to the one it replaced; its
first run after the port says whether that held on a real model.
