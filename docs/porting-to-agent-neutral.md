# Running an exam through Harbor

What an exam, its fixture and the machine need for a coding agent to do
the work in a container. A value that names an `agent` (`claude-code`,
`codex`, `qwen-code`) or says `"route": "harbor"` (nb itself) runs
through Harbor in the fixture's image; a value that names only a
provider and a model is nb run bare on the host. Written from moving
`exams/code-change`; the design is `project/plans/agent-neutral-runs.md`.

## 1. An exam with nothing through Harbor

Nothing here applies, and validation refuses `harbor` and `limits` on
it.

## 2. The machine: an engine, uv, and proctor.json

A container engine and uv on the machine that runs it; the bare path
needs neither. The engine that is supported is rootless podman, whose
setup is [`podman.md`](podman.md). In `exams/proctor.json`:

- `harbor.engine`: `podman`, or `docker` (the default, not maintained).
- `agents.<agent>`: `base_url` and `api_key` for each vendor agent the
  exam names (`claude-code` an Anthropic route, `codex` and `qwen-code`
  an OpenAI-dialect one). The base URL's host is the only name the
  agent's container may reach, matched exactly as spelled.
- `harbor.nb`: nb's self-contained linux-x64 publish, for nb through
  Harbor. It must be the same nb as `nb.path` (`run` refuses another
  version), since the host compiles the program and the image runs it.

## 3. The fixture: an image

Every fixture the exam runs in needs `image`:

```json
"image": {
  "from": "mcr.microsoft.com/dotnet/sdk:10.0",
  "env": { "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1" },
  "run": ["find . -name '*.csproj' -exec dotnet restore -v q {} \;"]
}
```

`run` steps run in the checkout at image build, which is the only phase
with public network: the agent reaches only its model's host, and the
verifier reaches nothing. So anything a build or a test needs from the
network (packages, tools) is fetched here. The fixture's `repo/` is
copied in, with the files the configuration's values lay over it, and
committed; nothing else of the fixture enters the image the agent runs
in. A text parameter is a build argument for these steps to act on.

## 4. The exam: Harbor, limits, and no runner

- Add `"harbor": {"version": "0.26.0"}` (pinned) and
  `"limits": {"wall_ms": …}` (Harbor's agent timeout, for each stage; a
  stage that hits it is completed with `time_budget`, not failed).
- Remove the `nb` block (`runner`, `mounts`). The image is the container
  now, and `{{work}}` is `/app` for nb.
- Remove `hooks.try`. It runs on the host, which the agent's container
  never sees, so it is refused; what its setup did (build an image, warm
  a package cache, start a container) is the fixture's image.
  `hooks.configuration` still runs on the host, once per configuration:
  `code-change` keeps `ensure-model.sh`, which puts the local model on
  the model box.

## 5. Checks

- **A script check runs in Harbor's verifier**, a fresh copy of the
  image with the stage's diff applied, offline. It sees the container's
  paths (`PROCTOR_WORK` is `/app`, the exam at `/tests/exam`, the fixture
  at `/tests/fixture`) and keeps the 0/1/2 exit contract. The stage's
  directory and number, nb and the runner are empty there.
- **There is no transcript in the verifier.** A script check that reads
  `PROCTOR_TRANSCRIPT` cannot run; it is `error` there. The fixture's
  checks and the expectations are in the verifier only, by construction,
  so a check that the agent stayed out of them has nothing to guard.
- **Built-in and model checks are unchanged**: `grade` reads the ATIF
  trajectory and proctor's own count (`core.json`) into the same
  windows. A budget check reads proctor's count, never the agent's.
- **What only nb does** has nothing to read on a vendor agent's stage:
  `loop_nudged` is `error` there, and `denied_calls` is 0 by
  construction, since Harbor runs every vendor agent with approvals
  bypassed.

## 6. Who does the work

A value that has made tries cannot be edited into another under the
same name, so a value moved to Harbor takes a new name.

- **nb** keeps `provider` and `model` and gains `"route": "harbor"`. Its
  provider entry in nb's config must name an `Endpoint` whose host the
  container may reach; `${VAR}`s in it go to the container by value and
  the key by name. The program template still applies.
- **A vendor's agent** is `agent`, a pinned `agent_version`, and a bare
  `model` of that vendor: Anthropic for Claude Code, OpenAI for Codex,
  Qwen for Qwen Code, with no provider prefix. `provider` is refused.
- **The program template does not reach a vendor's agent.** Its
  instruction is the stage's prompt, nothing else: the template's
  `system`, `approval`, `loop` and `budget` are nb's, and each vendor
  agent runs with its own system prompt and its own tools.
- **A stage with an answer sheet keeps it on nb, in a try of its own,
  and cannot run on a vendor's agent.** Through Harbor the oracle is a
  simulated user that drives nb over Harbor's ACP bridge, so the
  template must end in exactly `run {{prompt}}`, which the user sends,
  and the try must be that one stage. Three things differ from the bare
  run, and the numbers can show them:
  - **`budget tool_calls` and the loop detector count per answer.** Each
    answer is a new ACP prompt, not a continuation of one `run`.
  - **The judge's tokens are not in the stage's.** Bare, nb charges them
    to the stage. Bridged, they're the manifest's `oracle`
    (`judge_input_tokens`, `judge_output_tokens`).
  - **The agent can see the bridge's `.acpxrc.json` in its checkout.**
    The diff leaves it out.

## 7. Bare against Harbor

The deterministic gate (`exams/gate`) shows nb bare and nb through
Harbor read the same on a scripted model, stage for stage. To see it on
a real one, make the route an axis, as `exams/oracle-change` does, and
compare along it.
