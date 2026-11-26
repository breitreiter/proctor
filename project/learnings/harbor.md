---
type: learnings
title: Harbor — the runner proctor was about to build, and the layer it lacks
created: 2026-10-12
sources: harbor-framework/harbor at 4778ceb (2026-10-12), v0.24.0; harborframework.com docs; tbench.ai
status: desk research, then the spike run on 2026-10-15 (all five steps pass; see *The spike, run*). Adopted as the route in plans/agent-neutral-runs.md (revised 2026-10-12)
---

# Harbor

Harbor is the Terminal-Bench team's framework for running agents in
containers and scoring them. It is the official harness for Terminal-Bench
2.0, Apache-2.0, Python (`uv tool install harbor`), led by Stanford and the
Laude Institute with outside contributors. On 2026-10-12: ~6,000 stars,
~2,000 forks, a release every two to four weeks (0.17 in July, 0.24 on
2026-10-05), and adapters for about 80 public benchmarks (SWE-bench and its
variants, Aider Polyglot, GAIA, OSWorld, …).

The claim this note tests: **proctor sits a level above Harbor**. Harbor runs
trials and produces a reward per trial; proctor decides what a suite means,
grades it under named roles, and says with an interval whether one arm beat
another. Mostly true. The overlap is in grading, and that is the one design
decision this note leaves open.

## What Harbor is, in proctor's words

| Harbor | proctor | fit |
|---|---|---|
| task (`instruction.md`, `task.toml`, `environment/`, `tests/`, `solution/`) | task + fixture | Harbor folds the fixture into the task. Reuse across tasks is a shared `docker_image` or generated task dirs. |
| dataset | suite | A dataset is a list of tasks, with no shared checklist and no business goal. |
| agent + model (+ `--ak` kwargs, `version`) | arm | Close. An arm's bundle maps to a skills dir, `--append-system-prompt`, or native config. |
| trial | sample / cell | `n_attempts` is the sample count. |
| job | experiment | `jobs/<job>/config.json`, a lock file, one dir per trial. |
| verifier (`tests/test.sh` → `/logs/verifier/reward.json`) | the checks | Overlaps. Harbor's reward is numbers keyed by name; proctor's check is pass/fail/error/needs-judge, with a role. |
| RewardKit judge (TOML rubric, LiteLLM or an agent judge) | `judge` / `decide` | Overlaps, and theirs is weaker on evidence (below). |
| ATIF `trajectory.json` | nb's JSONL, the planned lingua franca | Overlaps; ATIF already exists and 30+ agents emit it. |
| `solution/solve.sh`, the `oracle` agent | reference solutions in `code-change` | Same idea. Their `oracle` runs the solution; proctor's oracle is an answer sheet, a different thing with the same name. |
| metrics: mean, min, max, sum, or a uv script | `Stats.cs` | No intervals, no pairing, no baseline. This is the gap. |
| `harbor view` (local web viewer) | `report.html` | A trial browser, not a readout. |

## What Harbor gives proctor

### It already is most of `plans/agent-neutral-runs.md`

Item by item against that plan's *Work*:

- **1. Container runner.** Docker and podman locally (plus ~25 cloud
  sandboxes), `cpus`/`memory_mb`/`storage_mb`, agent and verifier timeouts,
  healthchecks, multi-container compose, and network policy per phase:
  `public` while the agent installs, `allowlist` (only the model API) while it
  runs, `no-network` while the verifier runs. A task whose policy the
  environment cannot enforce is refused, not run weaker. That last line is
  exactly proctor's "not yet" instead of silent skipping.
- **5. `Agents/ClaudeCode`.** Done, ~2,000 lines. Fresh `CLAUDE_CONFIG_DIR`
  per trial (the clean account state the plan asked for), the version
  pinned through `--ak version=…` and verified after install, `--settings` from a
  native config file, `--allowedTools`/`--disallowedTools`, `--max-turns`,
  `--max-budget-usd`, `--append-system-prompt`, skills and MCP registration,
  stream-json → ATIF with subagents, `ANTHROPIC_BASE_URL` honoured (so the
  gateway works), resume across steps.
- **6. `Agents/Codex`.** Same depth, `OPENAI_BASE_URL` honoured. Also
  `qwen-coder` (the real Qwen Code CLI, `OPENAI_BASE_URL` → the imp box is a
  config line), `gemini-cli`, `opencode`, `copilot-cli`, `cursor-cli`, and
  ~35 more.
- **Resume.** Multi-step tasks (steps with their own instruction and tests,
  `min_reward` early stop) with `--resume-trajectory` on 15 agents.
- **Regrade.** `harbor job regrade` reruns an updated verifier over recorded
  artifacts with no agent and no agent key, when the task uses a separate
  verifier container.

What it does **not** give, and stays proctor's: the supervisor's uniform
limits (Harbor has a timeout everywhere, but turns and dollars only where the
agent's own CLI has a flag, e.g. cc's `--max-turns`/`--max-budget-usd`), the
answer-sheet oracle (Harbor's nearest thing is a simulated user played by a
second agent over ACP, which is a model improvising), and the strict
adapters with conformance kits (decision 10). Harbor's conversion is
theirs; a vendor format change would show up as a Harbor bug, not as
`adapter_unmapped`.

### Reflected credibility

"Run on Harbor, the Terminal-Bench 2.0 harness" answers the "hacked-together
evals" objection faster than any amount of our own documentation.
It also opens two doors proctor has no other way through:

- **Public benchmarks as suites.** Any Harbor dataset (TB2, SWE-bench
  Verified) can be a proctor suite, and proctor's report adds what Harbor's
  leaderboards lack: per-arm Wilson intervals, Newcombe paired differences,
  a baseline verdict. "cc vs Codex on a TB2 subset, with intervals" is a
  demo nobody else ships.
- **Proctor suites as Harbor tasks.** A suite compiled to Harbor task dirs
  can be run by anyone with Harbor, without proctor.

## What proctor keeps, and why it is the higher layer

From `learnings/prior-art.md` and the brief, none of which Harbor does:

- **The suite as one business goal with one checklist**, checks declared at
  suite, fixture and task level with roles: `pass`, `validity` (excludes a
  sample with its reason), guardrail rate.
- **Three headline outcomes** (pass, fail, undecided), and `failed`
  meaning infrastructure, never a bad transcript.
- **Statistics.** Each task scored as its mean over counted, decided
  samples; Wilson per arm; Newcombe method 10 for paired differences; MDE.
  Harbor reports the mean reward and stops.
- **Baselines and `--against`**: pinned cells, re-scored under the
  current lists, `held`/`improved`/`regressed`, an exit code.
- **The judge contract.** Every quote verified against the window, split
  or `unknown` → `needs-judge`, verdict files as cache and record, an
  off-family judge. RewardKit asks for binary, Likert or numeric answers
  and records them; it does not verify quoted evidence, and a Likert score
  is the kind of model-authored number proctor's report keeps out.
- **The report**, rendered twice from one block list, every number from
  `stats.json`.

So the split is: **Harbor runs a trial and returns its evidence; proctor
decides what that evidence means across a suite.**

## Where nb still fits

Agreed with the premise, and the plan already retires the costumes
(decision 2), so "nb minus costumes" is the plan as written:

- **Custom harnesses.** nb as the harness that is not a vendor CLI:
  scripted tool surfaces, fabricated history, `seed`, fake tools, any
  provider. Inside Harbor it would be one more installed agent
  (`BaseInstalledAgent`, ~100 lines of Python): install the pinned binary,
  pipe `program.jsonl` in, and emit ATIF or have proctor convert nb's JSONL.
- **Small tool-less tests** (parsing, extraction, classification). A
  container, an agent install and a verifier container cost seconds to
  minutes per trial for a test that is one model call. These keep today's
  bare path: nb on the host, ~2 s per cell. That also keeps every Mock-driven
  test in `Proctor.Tests` off Docker.

This reverses one decision in `agent-neutral-runs.md`: decision 1 made a
container mandatory. Under this note there are two paths. An agent arm runs
through Harbor in a container. A tool-less nb arm runs bare. Validation
refuses tools on a bare arm (once a tool is granted, bash follows, and the
guardrails belong in the container, which is the plan's original reason).

## The shape of an integration

Proctor stays C# and talks to Harbor the way it talks to nb, through a CLI
and files:

1. **Compile.** `run` writes one Harbor task dir per proctor task under the
   experiment: `instruction.md` from the prompt, `environment/Dockerfile`
   `COPY`ing the fixture's `repo/` (never its checks), `task.toml` from the
   suite's limits and network policy, and `[artifacts]` naming the
   workspace and a `git diff`. Arms become `agents:` entries in a job
   config. The compiled dirs are hashed like everything else.
2. **Run.** `harbor run --config job.json --jobs-dir <experiment>/harbor`,
   with `HARBOR_TELEMETRY=off`.
3. **Ingest.** Each trial becomes a cell: `trajectory.json` (ATIF) beside
   the raw native log, `diff.patch` from the artifacts, a manifest with agent,
   agent version, model, timings, tokens and cost from Harbor's `result.json`.
   A trial with `exception_info` and no agent result is `failed` with that
   reason, matching proctor's rule.
4. **Grade** as today, against ATIF windows instead of nb JSONL.

**Open: where deterministic checks run.** Two options:

- **(a)** Proctor grades on the host after the trial, from collected
  artifacts. One grader, `checks.json` unchanged, regrade free, but script
  checks that need the fixture's toolchain need a container again.
- **(b)** Proctor compiles its script checks into Harbor's verifier, one
  `reward.json` key per check (1/0, with error as a missing key), and reads
  them back. The checks run in the task's own image and Harbor's
  separate-verifier and regrade come with it. Proctor then grades only the model checks
  on the host.

Leaning to (b) for script checks and (a) for model checks and the built-ins.
The fixture's toolchain lives in its image either way, and model checks must
stay out of any container the agent touched.

**The lingua franca becomes ATIF.** The plan's lingua franca was nb's
transcript schema; ATIF is the same idea, already versioned (v1.7), with
subagents, per-step metrics and an `extra` slot where `ext.<agent>.*` would
go. `Grade/Transcript.cs` reads ATIF; nb either emits ATIF or proctor
converts nb's JSONL at ingest. That is a change to nb's half of the plan
(`../nb/plans/agent-neutral-runs.md`, item 1) and needs saying there.

## Costs and risks

- **A Python dependency** for a .NET tool: Harbor, uv, and Docker on any
  machine that runs agent arms. The tool-less path needs none of it.
- **Churn.** Seven minor releases in three months, and ATIF 1.7 broke 1.6
  refs. Pin the Harbor version per experiment, record it in
  `versions.harbor`, and treat a bump like an agent bump (decision 10): an
  experiment, not an upgrade.
- **Telemetry is on by default** (PostHog: agents, models, token counts,
  cost, reward, per job). No prompts or paths, but at work it must be
  `HARBOR_TELEMETRY=off`, set by proctor, not left to the user.
- **Defaults chosen for benchmarks, not product questions.** cc runs as
  `bypassPermissions` with `IS_SANDBOX=1`, the agent is installed at trial
  time from the network unless the image has it, and a cc arm on a custom
  base URL maps every model alias to one model. Proctor sets each of these
  explicitly rather than inheriting them. That includes refusing
  cross-vendor arms (plan decision 2), which Harbor will happily run.
- **Docker allowlists need nftables** (Harbor's egress sidecar). The dev box
  has docker; whether its kernel supports the sidecar is untested.
- **Adapter fidelity is trusted, not verified.** Proctor's conformance kits
  become a check on Harbor's conversion: record a native stream, convert
  it, compare. Cheaper than writing the adapter, and it still catches drift.
- **Vocabulary collision.** Harbor's `oracle` agent and proctor's oracle
  sheet are different things. Harbor's "task" bundles what proctor splits
  into task and fixture. The report never says Harbor's words.

## Unverified, and the spike that would settle it

Everything above is from the source and the docs; nothing was run. A
half-day spike:

1. `uv tool install harbor`; `harbor run -t hello-world/hello-world -a
   claude-code -m anthropic/<haiku>` on local docker with
   `ANTHROPIC_BASE_URL` at the gateway. Cost and wall time per trial.
2. Hand-write one `code-change` task as a Harbor task dir; run it on cc and on
   `qwen-coder` against imp. Look at `trajectory.json` against what
   `Grade/Transcript.cs` needs: answer, tool calls, results, user turns.
3. `network_mode = "allowlist"` with only the gateway host, on this box.
4. Check whether a pinned `version` plus a prebuilt image runs with no
   network at install time.

If those hold, `agent-neutral-runs.md` items 1, 5 and 6 become "compile to
Harbor and ingest". Items 3 (supervisor), 7 (oracle) and 10 (conformance)
narrow but remain.

## The spike, run (2026-10-15)

Harbor 0.24.0 on this box (Docker 29.1, kernel 7.0), the gateway (minrouter,
now `http://library:8086`) for every model call, cc 2.1.301 on Haiku 5.5 and
Qwen Code 0.25.2 on qwen3-coder-next (imp). The task files are in
`harbor-spike/`. **Go: steps 1 to 5 all pass.** About two cents of Haiku in
total over five cc trials, 18 gateway requests.

| step | what ran | result | wall | agent setup | agent work |
|---|---|---|---|---|---|
| 1 | `hello-world`, cc | reward 1, $0.003 | 124 s | 101 s | 2 s |
| 2 | `fix-null-deref`, oracle and nop agents | oracle 1/1/1, nop fails `tests_pass` | 35 s | | |
| 2 | `fix-null-deref`, Qwen Code | `builds`, `tests_pass`, `diff_in_scope` all 1 | 123 s | 12 s | 73 s |
| 2 | `fix-null-deref`, cc, agent allowlisted | all 1, $0.004 | 147 s | 92 s | 12 s |
| 3 | `netprobe` (oracle curls five URLs in the agent phase) | only the allowlisted name answers | | | |
| 3 | `fix-null-deref`, Qwen Code, agent allowlisted | all 1 | 149 s | | |
| 4 | cc pinned in the image, every phase allowlisted to the gateway | all 1, $0.002 | 80 s | 0.16 s | 10 s |
| 5 | scripted user (`oracle_user.py`) over the ACP bridge to cc | two turns, all 1 | 168 s | | |

### What the spike taught

- **Prerequisites beyond the note.** Harbor needs the Docker Compose *and*
  buildx CLI plugins; this box had neither (`docker compose` and `docker
  build --file` both failed). Both install per user under
  `~/.docker/cli-plugins/`, as does uv (`pipx install uv`). No sudo anywhere:
  the egress sidecar works with docker group membership alone. A
  `no-network` or `allowlist` phase builds the sidecar even when the agent
  phase is public.
- **Podman is an environment type, not a compatibility shim.** Harbor
  0.24.0's `podman` environment subclasses `docker` with the podman CLI:
  `podman compose` (docker-compose over the podman socket, `up --wait`
  off, since it hangs rootless) or `podman-compose --in-pod=false`, and the
  same egress sidecar (its docs list `allowlist` and per-phase policy for
  podman, nftables permitting). Proctor selects it with `harbor.engine`;
  as of 2026-10-20 no gate or conformance run has been on podman. On
  2026-10-29 both gates ran green on podman (`GateTests` in 8.7 minutes,
  `OracleGateTests`, with the ACP bridge, in 3.5); conformance still hasn't.
- **Model names go bare.** With `ANTHROPIC_BASE_URL` set, Harbor passes `-m`
  through verbatim, so `anthropic/claude-haiku-5-5` reaches the gateway with
  its prefix and 404s. Compile must write `claude-haiku-5-5`.
- **The agent install is most of a cc trial** (90–100 s of npm and
  bootstrap per trial). With the pinned version already in the image,
  `install()` is skipped after one `claude --version`, setup takes 0.16 s,
  and the trial needs no public network in any phase. So the compiled
  Dockerfile installs the arm's pinned agent; the agent-install network is
  needed at image build only.
- **The allowlist matches names exactly.** `library` passes; `library.lan`
  (the same machine), imp's IP and public hosts get nothing. The suite must
  name the gateway host exactly as the agent's base URL spells it.
- **The separate verifier fits proctor's checkout.** A `[[verifier.collect]]`
  command writes `git diff --cached --binary HEAD` to `/logs/artifacts/`,
  which Harbor collects by convention (an explicit `[[artifacts]]` entry for
  it is redundant and logged as skipped) and re-materialises in a fresh copy
  of the task image, where the verifier applies it to the clean commit.
  That is `diff.patch` and the regrade restore, done by Harbor. With
  `no-network`, NuGet must be restored at image build. A multi-key
  `reward.json` (one key per script check, 1/0) works and Harbor's summary
  shows each key's mean.
- **ATIF carries the windows.** Answer: the last `agent` step's `message`.
  Tool calls: `tool_calls[]` with `function_name` and `arguments`. Results:
  `observation.results[]` keyed by `source_call_id`. User turns: `source:
  user`. Tokens: `final_metrics` (cost only where the agent knows a price;
  Qwen Code's is null). Two gaps: a result has only `content`, so a tool
  error is text unless the agent's `extra` says more (cc puts
  `tool_result_metadata.tool_use_result` with stdout there; Qwen Code puts
  nothing); and no exit reason, which ingest takes from `result.json`
  (`exception_info`, timings). Denials were not exercised.
- **The oracle bridge works, narrower than hoped.** A custom `BaseAgent` is
  accepted as `--user-agent module:Class` with the module on `PYTHONPATH`;
  it gets one rendered prompt, and a template with a separator gives back the
  task text. It talks with `environment.exec("acpx prompt …")`, which blocks
  until the target's turn ends and returns the reply. But: only cc, Codex,
  opencode and gemini-cli can be a target (Qwen Code cannot), and in bridge
  mode the target's record is `agent/bridge-trajectory.json`, acpx's raw
  ACP JSON-RPC history (135 messages here), not ATIF, with no token usage in
  `agent_result`. An oracle task needs an ACP reader at ingest, or a
  conversion upstream.
- **Telemetry** stayed off by `HARBOR_TELEMETRY=off` on every invocation.

### Staging 2, run (2026-10-16)

`suites/agent-smoke` through proctor 0.2.5: cc 2.1.301 on Haiku 5.5,
`fix-null-deref`, compiled by `Harbor/Compile.cs` rather than by hand.
Completed with `builds` and `tests-pass` both 1; 86 s with the image built
from scratch, 58 s cached; $0.0046, 5 tool calls over 4 turns by proctor's
count. One thing the spike did not write down: a collected container path is
mirrored under the trial's `artifacts/`, so `/logs/artifacts/diff.patch`
lands at `artifacts/logs/artifacts/diff.patch` (the manifest says so), not
at `artifacts/diff.patch`. Ingest first read the wrong path and fell back to
an empty diff; it now fails the cell when the diff is missing.

### Staging 5, shaken down (2026-10-18)

Codex 0.163.0 and Qwen Code 0.25.2 through proctor 0.3.1 on `agent-smoke`,
with the gateway unresolvable from the box (so: everything but the model).

- **Harbor's Qwen Code agent always installs.** Its `install()` runs the nvm
  installer and `npm install -g` on every trial, with no check of what the
  image holds, so it needs public network in the agent phase. Claude Code's
  and Codex's check the pin first and skip.
- **nvm under `RUN` needs `NVM_DIR`.** Docker's `RUN` is `/bin/sh`; sourced
  there, `nvm.sh` cannot find its own directory and puts Node under
  `/usr/bin/versions/`. Harbor's agents look in `~/.nvm`, so export
  `NVM_DIR="$HOME/.nvm"` first, as Harbor's own snippet does.
- **Codex tries a WebSocket first.** It opens `ws://<base>/responses`, and
  falls back to HTTPS after five failures. A gateway that does not proxy
  WebSockets costs a few seconds of retries per run, not the run.
- **Qwen Code's API error is a Harbor exception** (`UnknownApiError`), so
  an unreachable endpoint fails the cell as infrastructure. Codex's goes on
  retrying until the agent timeout, which Harbor reports as a timeout with
  no turns; ingest fails a vendor run with no turn, so an outage never
  reads as a run.


### Conformance at $0 (2026-10-20)

`project/archive/harbor-conformance.md`: each vendor agent at its pin against a
scripted model (`fixtures/stub`), and Harbor's `oracle` agent over
`code-change`'s reference solutions. No hosted model, judge or gateway.

- **A task's compose services share the agent's network namespace.** With
  egress control on, Harbor attaches a service from
  `environment/docker-compose.yaml` that declares no networking to the
  sidecar's namespace, as it does `main`. The stub serves at `localhost:8765`.
  Loopback is exempt from the sidecar's redirect (`fib daddr type local`), so
  the allowlist never sees it, and the host has no port.
- **All three agents run on a stub that is stateless and keys on the prompt.**
  Claude Code (Anthropic Messages), Codex (Responses, after the WebSocket 404)
  and Qwen Code (Chat Completions) each took one shell call, ran it, accepted
  a final text, and Harbor's ATIF ingested strictly. A 500 on every request
  is `failed` on all three. A tool call followed by silence is `completed`
  with `time_budget`. One run is nine trials, about 18 minutes once the
  images are cached.
- **The offered tool list is the conformance test for `Ingest.Kinds`.** The
  first run found 16 tools the three agents offer at their pins that ingest
  would have refused the first time a model called one. Among them are
  Claude Code's `Cron*`, `*Worktree`, `SendMessage`, `Workflow` and
  `ReportFindings`; Codex's `multi_agent_v1` and `web_search`; Qwen Code's
  `notebook_edit`, `tool_search` and `tool_call`. The night before, real
  models had found two such tools (`get_goal`, nb's `todo_write`) at the cost
  of a run each.
- **The oracle runs the task's `solution/solve.sh` in the task's image.** So
  a reference solution may use only what the fixture's image has. The `.NET`
  SDK image has `git` and no `python3`. The first oracle pass found
  `add-retry-flag`'s reference broken twice over: it needed python, and the
  2026-09-23 vocabulary rename had turned its C# `case` labels into `task`,
  so its edits matched nothing. It is a `git apply` of a patch now. A patch
  fails loudly when the fixture moves; an edit that matches nothing passes
  silently.

### Podman, checked (2026-10-27)

Issue #5's findings, repeated on another machine: rootless podman 5.7.0 on
Ubuntu with its `docker-compose-v2` package (2.40.3), Harbor 0.24.0, the
oracle agent on a probe task with an `allowlist` phase, no model.

- **The sidecar needs short names to resolve.** Its Dockerfile is
  `FROM gogost/gost:…@sha256:…`, and Ubuntu's `/etc/containers/registries.conf`
  defines no search registry, so the sidecar build fails before the trial
  starts. `unqualified-search-registries = ["docker.io"]` in
  `~/.config/containers/registries.conf` fixes it: reward 1.0 in 25 s. The
  podman API service reads that file too, which is where Harbor's builds
  happen.
- **docker-compose v2 through `podman compose` works; podman-compose
  cannot.** Harbor prefers `podman compose` whenever `podman compose up
  --help` answers (`environments/docker/runtime.py`) and probes it with
  `podman compose ls` in preflight, which podman-compose lacks. With
  podman-compose alone Harbor calls it directly, and the allowlist phase's
  `compose exec --no-TTY` (`docker.py`) is refused (seen on podman 6.1.3
  and podman-compose 1.6.0; here read from the code, not run). `run`
  checks both before an experiment is made (`HarborRun.CheckPodman`).
- **The allowlist holds:** the allowed host answered, a name and a bare IP
  outside it got nothing.
- **The host's loopback is open by host, not by port.** A rootless trial
  reaches a gateway on `127.0.0.1` as `host.containers.internal` only with
  `pasta_options = ["--map-host-loopback", "169.254.1.2"]` under `[network]`
  in `~/.config/containers/containers.conf`; then a second loopback port
  answered as well. Every loopback service on the machine is open to the
  trial.
- **Teardown leaks the sidecar here.** Every trial with a sidecar ended
  with `compose down` failing on it (`rootless netns: kill network process:
  permission denied`), leaving an exited sidecar container and the
  project's network; the trial's reward was unaffected and `podman rm -f`
  removed them afterwards. Explained on 2026-10-29 (#7): Ubuntu 26.04's
  AppArmor profile for pasta allows it no signals, and the kernel log shows
  `operation="signal" profile="pasta" signal=term peer="podman"` denied.
  No denial was visible at first only because `dmesg_restrict=1` hides the
  kernel log from a non-root account. The sandbox played no part. The fix
  is an AppArmor rule (`docs/podman.md` §6), plus an include line, because
  pasta's packaged profile, unlike most of Ubuntu's, never reads
  `local/usr.bin.pasta`; a rule there alone does nothing. Verified
  2026-10-30: with both, `podman unshare --rootless-netns true` succeeds
  and a container on its own network tears down leaving nothing.

### Conformance on podman (2026-10-27)

Both conformance tests on the same machine, Harbor run through a sandboxed
`uv` (`docs/podman.md` §8); `PROCTOR_CONFORMANCE` now takes the
`proctor.json` whose `harbor` block to run on.

- **The oracle pass was green at once** (3.5 min): every `code-change`
  task's script checks are 1 with its reference solution.
- **The vendor agents failed on the compose banner.** `podman compose`
  prints `>>>> Executing external compose provider … <<<<` on stderr before
  every call, and Harbor reads `compose exec` output with stderr in it. So
  `codex --version` read as banner plus version: Harbor's Codex agent took
  its pin for missing and failed reinstalling (`apt-get`), and proctor's
  Qwen Code agent refused the image as the wrong version. Claude Code and
  the oracle never compare such output, which is why the #5 probes (oracle
  only) missed it. `compose_warning_logs = false` under `[engine]` in
  `~/.config/containers/containers.conf` silences it; `run` now refuses
  podman while it is on. With it off, all three agents came back as the
  stub scripted them (17 min).
- **The sidecar leak (#7) is per environment, not per trial.** The
  separate verifier is `no-network`, so it has a sidecar too: each cell
  left two exited sidecars and two networks (`…__env_default`,
  `…__verifier__trial_default`), 18 of each per run. The cause is
  AppArmor, not the sandbox (above).

## A gateway on the same machine as the containers (2026-11-25)

Seen on a laptop where the model gateway listens on the host's loopback
and Docker is rootless. An agent run through Harbor is in a container,
and could not reach the gateway at all. What was tried, and what each
showed:

- **`localhost` in the container is the container.** Expected, and the
  reason for the rest.
- **Rootless Docker ships with the host's loopback closed**
  (`rootlesskit --disable-host-loopback`): `10.0.2.2` is unreachable.
- **`host.docker.internal` and the bridge's gateway** are the rootless
  network's own, not the host: connection refused.
- **The host's own LAN address was unreachable too**, for an accident
  of the network: the Wi-Fi was `172.17.1.0/24` and Docker's default
  bridge is `172.17.0.0/16`, so a container took the host for a
  neighbour on the bridge. A gateway bound to the LAN address would
  also be open to whatever network the machine is on.
- **A relay cannot be a container**: it would sit in the same network.
- The gateway took the agent's key in `x-api-key`, which is how Claude
  Code sends one, so auth was not a second problem.

Two things are kept apart here on purpose (the user, 2026-11-25).

**What was done on that laptop, for narrow testing only.** The host's
loopback was opened to its rootless Docker
(`DOCKERD_ROOTLESS_ROOTLESSKIT_DISABLE_HOST_LOOPBACK=false`), so a
container reaches the gateway at `10.0.2.2`. This opens every loopback
service on the host to every container, an agent under test included;
Harbor's allowlist names a host, and the host is then the whole
loopback. **It is not advice.** It is not to go into the docs, the
skill or setup's sandbox plan, and it is not to be set on a machine
that matters.

**What to suggest for normal runs is not settled.** A proposal, mine
and untested: give the host one private address that is neither
loopback nor on any network (an alias on `lo`), have the gateway
listen there as well, and name that address in `agents.<agent>.base_url`.
A container then reaches one address and one port, nothing else on the
host, and nothing is open to the network. It needs root on the host
twice (the address, and the gateway's listen list), and whether
rootless Docker lets such an address through was not tried.

## When Harbor applies a network policy (read 2026-11-25)

Read in Harbor 0.24.0's source for `plans/container-network.md`, not
run.

- **The agent's policy is on only during the agent's phase.**
  `Trial._phase_network_policy` sets it on entering the phase and sets
  the environment's baseline back on leaving. Proctor compiles the
  environment `public`, so the agent's setup, the bridge's and the
  simulated user's run with the internet, and so does the container
  after the agent has finished, and between the steps of a job with
  several. A process the agent left running outlives its allowlist.
- **An allowlist entry is a domain, a wildcard, an IP, a CIDR or a
  range, never a port** (`bin/network-policy`). A trial also takes
  `extra_allowed_hosts`, merged into the task's unless the policy is
  public.
- **Under a policy the sidecar passes ICMP and DNS** to the resolvers
  in the container's `resolv.conf`, and rejects all other traffic that
  is not TCP. TCP goes to gost's transparent handler, which sniffs
  `Host` and SNI and, failing that, falls back
  (`sniffing.fallback: true`); what it falls back to was not read.
- **On Modal the image's entrypoint is replaced** by `sleep infinity`
  unless the environment is given `keepalive` (a command, or none to
  keep the image's own).

## What an agent's phase can reach, probed (2026-11-25)

Leg A of `plans/container-network.md`, on golem: rootless Docker 29.1.3,
Harbor 0.24.0, kernel 7.0. The task is `learnings/netprobe/` (Harbor's
`oracle` agent, no model, about two minutes a trial): two targets as
compose services that declare `networks`, so each has an address of its
own and a connection to either crosses the sidecar. Each answers HTTP on
two ports, TLS on a third and, on a fourth, speaks first as ssh does.
Run under an allowlist of the one name, of that target's IP, and public
as the control; the outputs are beside the task.

- **The allowlist cannot be talked past.** HTTP to the other target's
  address with `Host:` naming the allowed one, and TLS likewise with
  the SNI, were both answered *by the allowed target*: gost dials the
  name it sniffed, never the address the packet was going to. The
  reverse (the allowed target's address, the other's name) gets nothing.
- **Allowed by name is every HTTP and TLS port of that host, and
  nothing else of it.** A second HTTP port answered. A protocol where
  the server speaks first got nothing, and so did the host's own IP
  with no name. So ssh on a gateway's host is closed to the agent;
  another web service on it is open.
- **Allowed by IP is the reverse, and looser.** The address answers
  HTTP only when `Host` is the address too (so a base URL must spell
  the IP), nothing by name, nothing over TLS with a name in the SNI.
  But the server-speaks-first port answered: with nothing to sniff,
  gost falls back to the address, so every port and protocol of an
  allowed IP is open.
- **Names resolve and ping leaves.** Under either allowlist a public
  name resolved, and `ping 1.1.1.1` was answered. UDP to an outside
  resolver was refused. Not a way to fetch anything, but "the agent
  reaches nothing else" is not true as a sentence.
- **Golem's open host loopback is shut by the allowlist and open
  without it.** `10.0.2.2:<a loopback port>` answered under `public`
  and not under either allowlist. Which makes the next point matter
  more on such a machine.
- **A process the agent leaves running gets the internet when its
  phase ends.** A loop started in the agent's phase, fetching
  `http://example.com` once a second, got nothing for the whole phase
  and `200` from the second after it, through collect, until teardown.
  This is the window read from the source above, seen. On a machine
  with the host's loopback open it reaches that too.
- **With the environment itself allowlisted, it never does.** The same
  task with `[environment]` given the agent's `network_mode` and
  `allowed_hosts`: the loop got nothing from start to teardown, and the
  trial finished with its reward. The image still built (a build is the
  engine's, not the sidecar's). Not yet tried with a vendor agent's
  setup under it.
- **The container's gateway is not the host under rootless Docker**
  (connection refused on every port, in `public`). On rootful Docker it
  is; not run.
- **The agent is root in its container** (Harbor's `oracle`; the vendor
  agents were not looked at).

## A model on the host's loopback, through a socket (2026-11-25)

Leg B of `plans/container-network.md`, on golem (rootless Docker
29.1.3, Harbor 0.24.0). The stub model ran **on the host**, bound to
`127.0.0.1`; `socat UNIX-LISTEN:<dir>/model.sock,fork TCP:127.0.0.1:<port>`
was its host end; the stub fixture's compose file held, in place of the
stub, a relay (`learnings/netprobe/relay/`, forty lines of Python in
`python:3.13-alpine`) with `<dir>` bind-mounted, serving the socket at
`localhost:8765`. Nothing else changed: `proctor run conformance` in a
scratch root, the agents' base URLs `http://localhost:8765` as
conformance has them.

- **All three vendor agents came back as scripted.** Claude Code, Codex
  and Qwen Code, tasks `ok` and `hang`: `ok` passed with `made-file`
  1, `hang` was `completed` with `time_budget`. The host stub logged
  the ten requests. A relay that declares no network is in the agent's
  namespace like any fixture service, and a bind mount of an absolute
  host path beside the sidecar is no trouble.
- **It needs no network in any phase.** Claude Code's `ok` again with
  `[agent]` and `[environment]` both `no-network`: reward 1, the same
  trajectory. So with a relay the leftover-process window above closes
  too, and the agent's setup (a version check, the agent being in the
  image) is content with nothing.
- **A socket's path is at most 108 bytes.** The first try put it under
  the scratch root and socat refused it (110). A socket cannot live
  under `log/<id>/…` of a repository at any depth; it wants a short
  directory (`$XDG_RUNTIME_DIR`), or to be reached through a symlink,
  which is what was done here.
- **Eight streamed answers at once pass.** Outside Harbor, a container
  with `--network none`: eight clients, each a sixty-second
  `text/event-stream` of one event a second through one host socket.
  Every client got its sixty events, the first within 20 ms, so nothing
  buffers.
- Not tried: podman (`keep-id`, a label on the mount), rootful Docker
  (the socket is then root's on the host side of the mount), an agent
  that is not root, the host end restarted during a run, nb as the
  agent, the simulated user.

## The same probe on rootless podman (2026-11-26)

Leg A of `plans/container-network.md` on sanjuro: rootless podman 5.7.0
(netavark), docker-compose 2.40.3 behind `podman compose`, Harbor
0.24.0. One trial of `learnings/netprobe/`, allowed by name, with
`[environment]` given the agent's policy as compile writes it since
ddd04fe. The output is beside the task
(`sanjuro-rootless-podman.name.txt`, and `.late.txt` for the process
left running).

- **Every answer is the one rootless Docker gave.** The named host
  answers and the other does not; `Host` and SNI cannot talk past it;
  a second HTTP port of the allowed host answers and a port where the
  server speaks first does not; a public name resolves and a ping
  leaves; UDP to an outside resolver and IPv6 get nothing; the
  container's gateway answers nothing; the agent is root.
- **A process the agent leaves running never gets the internet.** Its
  fetch of `http://example.com` once a second got nothing from the
  start of the agent's phase to teardown: with the environment
  allowlisted, the window is shut on podman too.
- **The resolver is the network's gateway**, not Docker's `127.0.0.11`,
  and `host.docker.internal` resolves under podman (to nothing that
  answered under the allowlist).
- `GateTests` and `OracleGateTests` pass on podman at f693887 (8 m 38 s
  for both), so the fix for A4 holds for nb as a Harbor agent and for
  the bridged task there.
- Not run on podman: allowed by IP, `public` as the control, the relay
  (leg B).

**A gateway on another machine, as on sanjuro.** The allowlist names
that machine, so every HTTP and TLS service on it is open to the agent,
whatever port (here a second, unrelated web service), and the agent's
key reaches every route the gateway serves that key, a web search
upstream among them. What narrows the second is the gateway's (a key
scoped to the one upstream). Nothing proctor has narrows the first:
Harbor's allowlist has no port. The relay of leg B is the one mechanism
seen that would make it "that host, that port" (B6).

## The probe again: golem with its loopback shut, podman on 0.26.0 (2026-11-26)

For `plans/container-network-finish.md`. Run, both.

**Golem, rootless Docker 29.1.3, Harbor 0.24.0, the host's loopback
shut** (the user removed
`DOCKERD_ROOTLESS_ROOTLESSKIT_DISABLE_HOST_LOOPBACK=false` that day).
Run by golem's own session, the three policies at once, the environment
given the agent's policy. The outputs are on golem beside the earlier
ones (`golem-rootless-docker.<policy>.2026-11-26.txt`, not yet in the
repository).

- **One line of the 2026-11-25 results was the open loopback's, and
  only one.** Under `public`, `10.0.2.2:<port>` answered then and is
  `Network unreachable` now. Every other line of all three files reads
  the same, so what was written above about the allowlist stands for a
  machine set up as shipped.
- **A default container holds `NET_RAW` on Docker** (`capsh` in
  `alpine`). Podman's defaults leave it out (read, not run here). So on
  Docker an agent can capture packets on a loopback it shares with a
  fixture's service. Not yet tried from inside a trial.
- With the environment `public`, the process left running had the
  internet before, during and after the agent's phase; with it
  allowlisted, never. As on 2026-11-25.
- The probe's script wrote no agent policy at all when `ENVPOL` was
  unset (the write sat inside the `if`), so a `public` trial ran with
  the placeholder in its `task.toml`. Fixed. The script was committed
  with that fault, so how the earlier `public` file on golem was made
  is not known; its answers are a public network's, and today's agree
  with them but for the loopback line.

**Sanjuro, rootless podman 5.7.0, Harbor 0.26.0**, allowed by name:
the same answers as on 0.24.0, line for line but for the address a
public name resolved to, and the process left running got nothing
(`sanjuro-rootless-podman.name.harbor-0.26.0.txt` and `.late.txt`).
`run.sh` takes `HARBOR=<version>`.

## What an agent sees of a fixture's service beside it (2026-11-26)

For goal 2 of `plans/container-network-finish.md`. Run, Harbor 0.26.0,
allowed by name: sanjuro (rootless podman 5.7.0) by this session, golem
(rootless Docker 29.1.3) by its own. The probe's compose file has a
service, `beside`, that declares no network and so shares the agent's,
as a fixture's service does when compile lays it out. It holds a file,
an environment variable, and a header on a request it makes of itself
on the loopback once a second. The agent is root.

| | podman | Docker |
|---|---|---|
| The service at `127.0.0.1` | answers | answers |
| Its process in `/proc` | not there | not there |
| Its file, its environment, its source, anywhere on disk | nowhere | nowhere |
| Its listening port in `/proc/net/tcp` | seen | seen |
| Its port taken on another address | refused, in use | refused, in use |
| The agent's capabilities, of `net_raw net_admin sys_ptrace sys_admin dac_read_search` | none | `net_raw` |
| Its loopback traffic captured (a packet socket on `lo`) | refused | **heard, the header with it** |

- **On Docker an agent overhears whatever a fixture's service says on
  the loopback they share**: another service calling it, its own calls,
  a verifier's. What the service holds and does not send is safe on
  both engines.
- **The capability is Docker's default, not Harbor's.** `docker
  inspect` during the trial: the agent's container and the service have
  no capability added or dropped; only Harbor's sidecar adds any
  (`NET_ADMIN`, `NET_RAW`), in every version in golem's uv cache.
- **Dropping it in the task's compose file closes it.**
  `main: { cap_drop: [NET_RAW] }` beside the task's services: Harbor
  merges its own `main` over it, the container shows
  `CapDrop=[CAP_NET_RAW]` in the agent's phase and in the verifier's,
  and the capture is refused. The trial gets its reward on both
  engines.
- **It costs ping on Docker**: `ping: socket: Operation not permitted`
  there with the capability gone. On podman ping still answers, with or
  without the drop.
- `run.sh` takes `DROP=<capability>`. Golem's outputs are on golem
  (`golem-rootless-docker.name.harbor-0.26.0*.txt`).

## Harbor 0.26.0, first runs (2026-11-26)

Run. The pin in the repository is still 0.24.0.

- The probe's answers are the same as at 0.24.0 under all three
  policies on rootless Docker, and by name on rootless podman. After
  the agent's phase the trial moves on sooner (golem saw the process
  left running write for 6 s after it, against 16 at 0.24.0).
- `GateTests` and `OracleGateTests` pass with the two gate exams at
  0.26.0 and nothing else changed: 8 m 51 s on podman, 7 m 26 s on
  rootless Docker.

## The relay again, on Harbor 0.26.0 and on podman (2026-11-26)

Run, by hand as on 2026-11-25 ("A model on the host's loopback, through
a socket"): the stub model on the host bound to `127.0.0.1`, socat from
a unix socket in `$XDG_RUNTIME_DIR/proctor-relay/` to it, and in a
scratch root the stub fixture's service replaced by
`learnings/netprobe/relay/` with that directory mounted. Proctor at
0e979c6, so the compiled compose file also drops `NET_RAW` from the
agent's container.

- **Rootless podman 5.7.0 (sanjuro), the engine it had not been tried
  on**: `proctor run conformance --tasks ok` made all three vendor
  agents' attempts, each completed and passed its four checks, and the
  host's stub logged their requests. Then Claude Code's `ok` with
  `[agent]` and `[environment]` both `no-network`, Harbor run on the
  compiled task directly: reward 1, two requests at the host. The
  socket's directory mounts as it is; nothing about ownership or labels
  had to be set (Ubuntu, so no SELinux).
- **Rootless Docker 29.1.3 (golem), its host loopback shut**, by its
  own session: Claude Code's `ok` through `proctor run` passed, and
  again with no network in any phase, reward 1; the host's stub saw two
  requests each time. Nothing had to differ from 2026-11-25.
- **On Docker, an agent whose fixture has no services still holds
  `NET_RAW`** (golem's `docker inspect` of the gate's and
  `code-change`'s containers: nothing dropped). There is no service's
  traffic for it to overhear there. Once the relay is compiled into a
  task it is a service, so the drop will come with it.
- Not tried on either engine: `hang` through the relay at 0.26.0, an
  agent that is not root, nb as the agent, the host end restarted
  during a trial, two ports at once, and Harbor's pull request 4427.


## Harbor's pull request 4427, read, and a service that says whose namespace it joins (2026-11-26)

Read, not run: the source diff of pull request 4427 at 12a50b9
(`egress_control.py`, `docker.py`, `podman.py`, the sidecar's scripts
and the page it adds to the docs), open against Harbor's main from a
fork. It answers issue 3992.

- **Released (0.26.0), both engines**: one sidecar,
  `harbor-docker-egress-control-sidecar`, and Harbor gives every service
  that declares neither `networks` nor `network_mode` the line
  `network_mode: service:<that sidecar>`. That is why a fixture's
  service answers at the agent's `localhost`.
- **Under the pull request, on Docker**: every such service gets a
  sidecar and a namespace of its own, the sidecar carrying the
  service's name as an alias on the project's network. The agent's
  sidecar is `harbor-docker-egress-control-main-8e62e8c65106` (the
  prefix, the service's name, twelve hex digits of the SHA-256 of
  both). Traffic between services on the project's subnet is let past
  the policy, the gateway is not, so under `no-network` a service is
  reached by its name. **Nothing answers at `localhost` any more**:
  not the relay, and not the stub model (`localhost:8765`) or
  `tern-shop`'s two services either. `ports` and `links` on such a
  service are refused.
- **Under the pull request, on podman**: unchanged, one shared sidecar.
  So the same task would answer at `localhost` on podman and at a name
  on Docker.
- A service that declares `networks` or `network_mode` is left as it is
  written, released and under the pull request alike.

Run, on rootless podman 5.7.0 (sanjuro) at 0.26.0, the probe with no
network in either phase (`run.sh`, which now takes `JOIN=<service>` and
`FROM=<what uv installs from>`):

| `beside` declares | at `127.0.0.1:9090` | its name |
|---|---|---|
| nothing | answers | does not resolve |
| `network_mode: service:harbor-docker-egress-control-sidecar` and a `depends_on` it | answers | does not resolve |

So a service's name is no address on the released Harbor, and a service
can say outright which sidecar it joins and be where it was. Whether
that also holds under the pull request (joined to
`harbor-docker-egress-control-main-8e62e8c65106`) is **not run**: it
means running a fork's unmerged commit, and this session's permission
check refused handing that to golem's.

The same two trials on rootless Docker 29.1.3 (golem, its own session,
0.26.0): the same answers. Joined outright the service answers at
`127.0.0.1:9090`, Harbor starts the environment with no complaint, and
the name does not resolve either way.

## The stub fixture's service on Modal (2026-11-26)

Run, by hand: Claude Code's `ok` task of conformance as proctor
compiles it at 0.26.0 (compiled on podman in a scratch root, since
`run` refuses a fixture with services on Modal), handed to
`harbor[modal]@0.26.0` with proctor's Modal job settings. Three trials,
a few cents. Harbor picked its compose strategy on Modal's VM runtime
by itself (`Selected strategy: _ModalDinD`, `Using vm_runtime: True`).

| The task | What happened |
|---|---|
| as compiled: `[environment]` and `[agent]` both the allowlist `["localhost"]` | **the image is never built** (19 s): the build runs inside the sandbox under the environment's policy, and the pull of the base image is refused (`mcr.microsoft.com ... connect: connection refused`) |
| `[environment]` public | built and started, then the agent's phase timed out: Claude Code retried its model nine times and never reached `localhost:8765`. On the VM runtime the services are on an ordinary compose network, each with a namespace of its own |
| `[environment]` public, and the stub with `network_mode: "service:main"` | **reward 1** in two minutes; the stub's tool list is the one seen on podman |

So on Modal a fixture's service works, and three things differ from a
local engine: the service must say it joins `main` (on the released
local engines `main` is itself joined to Harbor's sidecar, and the
sidecar is what to name); the environment's phase cannot be the
agent's allowlist, since the build needs its registries; and there is
no egress sidecar, the policy being Modal's own on the sandbox. Not
measured: whether the agent's allowlist held against a host outside the
task in that trial, and what a process left running can reach after the
agent's phase when the environment is public.

**Where a fixture's service answers, then, is not one thing:**

| Harbor and engine | with nothing declared | to stay at `localhost`, the service declares |
|---|---|---|
| 0.26.0, Docker and podman | the agent's `localhost` | nothing, or `network_mode: service:harbor-docker-egress-control-sidecar` (run on both) |
| 0.26.0, Modal's VM runtime | its own namespace; the agent does not find it at `localhost` | `network_mode: service:main` (run) |
| pull request 4427, Docker | its own namespace, reached by name (read) | perhaps `network_mode: service:harbor-docker-egress-control-main-8e62e8c65106` (not run) |
| pull request 4427, podman | the agent's `localhost` (read) | nothing |

## Vendor agents' web tools under a policy, at 0.26.0 (2026-11-26)

Read and seen. Harbor 0.26.0 has the default this project decided for
itself (`agents/base.py`: under a rollout policy that is not public it
sets `disable_web_search` on an agent that has native web tools, unless
the job set that option itself). Claude Code is then started with
`--disallowedTools WebSearch,WebFetch` (the trial log on Modal shows the
command line), and the stub's record of the tools offered under the
allowlist has neither for Claude Code and no `web_search` for Codex,
which offered it at 0.24.0 (the first conformance run, above). Qwen
Code has no such switch in Harbor and proctor brings its own agent
class; its list has no web tool, and what it loads on demand
(`web_fetch`) runs in the container, under the policy. Not done: a
0.24.0 list for Claude Code to set beside it, and a call counted.
