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
