---
type: plan
title: Modal runs — promote a suite from this machine to Modal, unchanged
created: 2026-11-08
status: built 2026-11-10 (steps 2–4 and 6); conformance stays local (spike 2)
---

# Modal runs

## Why

A suite is shaken down here: local models on imp, Docker or podman, the
gateway on the LAN. Once it is trusted it should run somewhere that needs
none of that, for a colleague who will not install podman and for CI.
Modal is that somewhere. Its Starter plan gives $30 of compute a month and
a workspace budget cap; a cell costs about $0.04 of sandbox, which is noise
next to its model calls (pricing as read on 2026-11-08: sandboxes
$0.00003942 per core-second, a core being 2 vCPU, and $0.00000667 per
GiB-second).

Modal does not replace the local path. It is the second machine a suite can
be promoted to, and the cost of running there is an AI provider's key.
Local models stay the shakedown path.

## The seam is already there

The suite is the question; `suites/proctor.json` is the machine. Arms name
an agent or an nb provider; the machine's file says where that agent's
model calls go (`agents.<agent>.base_url`), where nb's config is (and in it
each provider's `Endpoint`), and which engine Harbor drives
(`harbor.engine`). `experiment.json` records the engine and `resume`
refuses a changed one.

So promoting a suite is running it with a different machine file:

```bash
proctor run code-change                                  # suites/proctor.json: podman, the gateway, imp
proctor run code-change --config suites/proctor.modal.json   # modal, public endpoints, keys
```

`proctor.modal.json` sets `harbor.engine: "modal"`, points `agents.*` at
the vendors' public APIs, and points `nb.config` at an `nb.modal.json`
whose providers are public. One file is one machine, as now; there are no
profiles inside a file. Both are gitignored, with
`proctor.modal.example.json` committed beside the others.

Bare arms run on the host whatever the engine. A suite with a bare `local`
arm and Harbor arms still runs its `local` arm on imp when the Harbor cells
go to Modal; only a Harbor arm whose model is local is out.

## What changes

1. **`--config <file>`** on `list`, `run` and `resume`: the machine file,
   default `suites/proctor.json`. `grade` takes it too, for judges.
   Relative paths inside it resolve against its own directory, as now.
2. **`modal` joins `HarborConfig.Engines`.** `Compile.Job` already writes
   the engine as Harbor's `environment.type`.
3. **Reachability before anything starts.** Under a remote engine, `run`
   refuses every Harbor arm whose route cannot be reached from the
   internet: an `agents.<agent>.base_url` or an nb provider `Endpoint`
   whose host is loopback, a private or link-local address, a single-label
   name (`imp`, `library`) or `.local`/`.lan`. The message names the arm,
   the host and the file to change. This is what turns "local models are
   disqualified" into a clear refusal rather than a cell that times out on
   Modal's clock. The same check refuses an agent arm on a task whose
   fixture has `image.services` (spike 2: no allowlist under compose on
   Modal), naming the fixture.
4. **nb's layer leaves the local image store.** Today `HarborRun.NbImage`
   builds `proctor-nb:<hash>` with the local engine and each nb task does
   `COPY --from` it; Modal builds remotely and cannot see it. Copy the
   publish into each nb task's build context and `COPY` it from there,
   for every engine, so there is one way. Cost: the publish (tens of MB)
   is in each nb task's context; compile is once per (arm, task), and the
   spike measures whether Modal caches the layer across tasks. A registry
   is the alternative if it does not; prefer not to need one.
5. **A sandbox ends itself.** Harbor's Modal environment (0.24.0) defaults
   to `sandbox_timeout_secs` of 24 hours and no idle timeout, so a trial
   Harbor never closed bills for a day. Compile writes both into the job's
   environment kwargs: the lifetime as the cell's wall limit plus
   `HarborRun.Overhead`, and an idle timeout. Then `HarborRun.TearDown`
   on Modal is a courtesy, not the guard: Harbor tags every sandbox
   (`harbor.*` labels as Modal tags, all under the app `__harbor__`), so
   it can stop a killed trial's sandboxes through the SDK by tag, or
   leave them to their timeout. The guide still says to set a workspace
   budget.
6. **Harbor with its Modal extra.** `modal` is an optional dependency
   (`harbor[modal]`, `modal>=1.5.4`), so `HarborRun.UvArgs` runs
   `harbor[modal]@<pin>` when the engine is `modal`, under the same
   cooldown.
7. **Preflight.** Modal's login is the user's (`modal token new` writes
   `~/.modal.toml`, or `MODAL_TOKEN_ID`/`MODAL_TOKEN_SECRET`); Harbor's
   process inherits proctor's environment, so nothing is passed. `run`
   checks one of them exists before the first cell, as `CheckPodman` does
   for podman.
8. **Report.** The engine is already recorded; the reproducibility
   section says where the cells ran. No other change.

## Work, in order

1. **Spike, $0 model spend, cents of Modal:** the gate (`suites/gate`, nb on
   Mock) and conformance's stub cells on `modal`, with steps 2 and 4 done
   by hand. Answers: does Harbor's Modal environment run our compose tasks
   (the stub service, the egress sidecar), does the allowlist hold, how
   long a cold image build takes, whether the nb layer is cached. Agree the
   spend first; it fits inside the free credit.
   **Spike 1, 2026-11-08: passed.** `code-change/rename-module` as
   compiled for `cc-haiku` (allowlist host changed from `library` to
   `api.anthropic.com`), its reference solution as `solution/`, Harbor
   0.24.0's `oracle` agent, `environment.type: modal` with
   `kwargs: {sandbox_timeout_secs: 3600, sandbox_idle_timeout_secs: 600}`
   (Harbor passes `environment.kwargs` to the environment's constructor).
   50 s end to end, 34 s of it environment setup including a cold build
   of the .NET SDK image with Claude Code installed; `acceptance`,
   `builds` and `tests-pass` all 1 from the separate offline verifier;
   `diff.patch` back under `artifacts/logs/artifacts/`, as locally. No
   container left afterwards; the app `__harbor__` stays deployed with
   no tasks. Not shown: that the agent-phase allowlist is enforced (the
   oracle agent makes no network call), compose services, nb's layer.
   **Spike 2, 2026-11-08: compose and an allowlist do not mix on Modal.**
   Conformance's `claude-code/ok` (the stub as a compose service) was
   refused before any sandbox started: "network_mode='allowlist' is not
   supported by EnvironmentType.MODAL environment". Harbor's Modal
   environment runs a task with a `docker-compose.yaml` as docker-in-docker
   with host networking, and declares no allowlist and no
   `disable_internet` in that mode ("DinD mode requires host networking —
   cannot enforce network isolation"). Daytona's environment declares the
   same (it keeps `disable_internet`, not the allowlist). Every agent task
   proctor compiles has an allowlist, so **a fixture with
   `image.services` cannot run on Modal**; the stub is such a fixture,
   so conformance and the oracle gate stay local checks, which is what
   they are for (proctor driving Harbor and reading it back, not the
   engine).

   **Spike 3, 2026-11-08: the allowlist holds without compose.** The
   spike 1 task with a `solve.sh` that probes from the agent phase,
   allowlist `api.anthropic.com`: that host answered (404), while
   `example.com` and `registry.npmjs.org` had their connections reset.
   So the isolation a Harbor cell has locally holds on Modal for a task
   with no services.
2. `--config`, the engine, the reachability check, preflight; unit tests on
   the host classification.
3. The nb layer in the build context, for every engine; the gate on docker
   and podman again to show nothing moved.
4. Teardown for Modal.
5. `PROCTOR_GATE_ENGINE=modal` and `PROCTOR_CONFORMANCE` on Modal, opt-in
   like the others.
6. Docs: `docs/modal.md` (account, token, budget, the example machine
   file), `docs/configuration.md` (`--config`, `engine: modal`), the
   CLAUDE.md conventions entry.

## Open

- The free credit, seen at signup on 2026-11-08: $1 on joining, the
  other $29 a month only with a payment method. Enough for the spike;
  a new user who wants more than a handful of cells adds a card, and the
  guide says so up front.
- Harbor's Modal environment runs compose as docker-in-docker; what size
  sandbox that needs, and so the real per-cell cost.
- Whether the oracle's simulated user works when the agent is on Modal:
  it runs on the host and reaches the agent over Harbor's ACP bridge.

## Not in scope

Other cloud environments (the engine list is the only place they would
go, but each needs its teardown and a conformance pass before it is
named); tunnelling a local model to Modal; running proctor itself in the
cloud. CI is its own plan, after #6, and runs proctor on the CI runner with
either engine.
