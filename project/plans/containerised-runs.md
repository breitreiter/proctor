---
type: plan
title: Containerised runs — one container per cell, nothing inside it but the checkout
created: 2026-09-21
status: done (2026-09-22); coordinated with nb (`../nb/plans/container-runs.md`)
---

# Containerised runs

The normal way to run nb is inside a container: nb's own guidance is "one
container, nb inside it, one filesystem" (`nb/CLAUDE.md`, *nb does not
confine the tools it runs*). Today proctor can do this, because its whole
contract with nb is one subprocess call and `nb.path` can already name a
wrapper script, but a consumer who tries discovers the leaks one at a time:
the program tells the model to work at a host path, the program file and
the eval sit in a directory the wrapper must mount, the config carries keys,
the manifest records the nb version as `unknown`, and the fixture's checks
assume the toolchain is on the host. Each has a workaround; together they
are an afternoon of reverse-engineering the runner.

This plan is the pit of success for that case: the obvious way to run an
eval in a container is the way that works, and it leaves nothing in the
container the model should not read. It stays inside the brief's posture
(*the test environment is not our problem*): proctor does not compose
`podman run`. It defines what the thing that runs nb is handed and what it
must return, ships a worked example that honours it, and makes its own side
of the contract container-clean. The container is the consumer's script,
and the script is ten lines.

It is coordinated with nb, which owes three things its cookbook plan listed
and never built: `--compile`, so a program with includes can travel over
stdin with nothing resting on disk; a Containerfile that publishes nb as a
layer; and the runbook for running nb in a container well. Those are filed
as `nb/plans/container-runs.md`. Nothing here depends on nb changing its
wire format or its CLI beyond that one flag.

## The worked example

The code-change eval, run in a podman container per cell.

```
evals/
  proctor.json                 { "nb": { "path": "../../nb/bin/Debug/net10.0/nb",
                                         "config": "nb.json",
                                         "runner": "runners/podman.sh",
                                         "mounts": { "work": "/work", "bundle": "/bundle" } } }
  nb.json                      endpoints only; keys as ${IMP_API_KEY}-style references
  runners/
    podman.sh                  the runner: `podman exec` into the cell's container
    Containerfile              FROM dotnet/sdk, COPY --from=nb /opt/nb /opt/nb
  code-change/
    eval.json                  hooks.sample.setup / teardown own the container
    hooks/
      sample.setup.sh          create the container, stage, start fakes
      sample.teardown.sh       collect, remove
    program.nb                 unchanged; {{work}} now resolves to /work
    ...
```

`runners/podman.sh`, in full:

```bash
#!/usr/bin/env bash
# The runner: nb inside the cell's container, the checkout at /work, the program on stdin, the transcript on stdout.
set -euo pipefail
exec podman exec -i -w /work -e NO_COLOR=1 "$PROCTOR_CONTAINER" /opt/nb/nb --output jsonl --config /nb/config.json -
```

The container is not the runner's. It exists before nb starts and after nb
ends, because the consumer usually has work to do at both ends: staging
files the model should find, starting a fake service inside, joining a
pod, pulling a service's log out afterwards. That work goes where hooks
already run. The sample setup hook creates the container and the sample
teardown hook removes it; the runner only execs into it.

```bash
#!/usr/bin/env bash
# hooks/sample.setup.sh: the cell's container, named for the cell, alive until teardown removes it.
set -euo pipefail
image="proctor-$(jq -r .stack "$PROCTOR_FIXTURE/fixture.json")"     # one image per fixture stack
podman rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true              # a resumed cell starts clean
args=(-d --name "$PROCTOR_CONTAINER" --userns=keep-id
      -v "$PROCTOR_WORK:$PROCTOR_WORK_MOUNT"
      -v "$PROCTOR_NB_CONFIG:/nb/config.json:ro"
      -v proctor-nuget:/home/agent/.nuget/packages
      -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 -e IMP_API_KEY)
[ -n "$PROCTOR_BUNDLE" ] && args+=(-v "$PROCTOR_BUNDLE:$PROCTOR_BUNDLE_MOUNT:ro")
podman run "${args[@]}" "$image" sleep infinity >/dev/null
# Anything the cell needs before nb starts goes here: podman cp a seed file, start a fake, join a pod.
```

```bash
#!/usr/bin/env bash
# hooks/sample.teardown.sh: collect what the container knows, then remove it. Runs whenever setup ran, so it is idempotent.
set -uo pipefail
podman logs "$PROCTOR_CONTAINER" > "$PROCTOR_CELL/hooks/container.log" 2>&1 || true
podman rm -f "$PROCTOR_CONTAINER" >/dev/null 2>&1 || true
```

`PROCTOR_CONTAINER` is nothing more than a name proctor derives from the
cell coordinates (`proctor-<experiment>-<arm>-<case>-<sample>`) so that the
hooks, the runner and a human at `podman ps` agree on it without passing a
file around. Proctor never uses it itself.

What the model can reach inside that container: the checkout at `/work`,
the bundle at `/bundle` if the arm has one, nb's binary and providers under
`/opt/nb`, and a config file with endpoints and `${VAR}` placeholders. The
program arrived on stdin and is not on disk. The eval, the cases, the
fixture's checker, the reference solutions and the other cells are on the
host, five directories above a path that does not exist in the container.
The key is in the environment, which is nb's documented posture ("keys via
env, not file"); a model that prints `env` reads it, and the transcript
records that it did.

An eval with no work to do at either end can collapse the three scripts
into one runner that does `podman run --rm -i …` itself; the contract is
the same. The split form is the one to reach for the moment the eval needs
anything beyond a checkout, and it is the one the example ships, because
the collapse is obvious and the split is not.

A cell then runs in the order it runs today: fixture checkout, sample setup
hook, the runner, sample teardown hook, diff. The container lives from the
second step to the fourth. The runner is handed the compiled program on
stdin and the cell environment; it writes the transcript to stdout and the
chrome to stderr, both captured into the cell by proctor as now. The
manifest records that a runner ran, which one, and its hash, beside the nb
version proctor read from the host binary.

```
runs/20260921-1410-code-change-k3pq/floor/ledger/1/
  manifest.json      "runner": { "script": "runners/podman.sh", "hash": "sha256:…" }, "nb": { "version": "0.9.3" }
  program.nb         the resolved source, for reading
  program.jsonl      what went down stdin: includes expanded on the host by `nb --compile`
  transcript.jsonl   tool calls name /work/…, and the stays-in-work check knows to expect that
  stderr.txt
  diff.patch
  checks.json
```

Grading is untouched. Fixture checks run on the host against `PROCTOR_WORK`
as now, which is right for code-change because the host has the .NET SDK. A
fixture whose toolchain exists only in the image writes its check as a
`podman run` against the same image, and the example fixture shows that
form once so nobody derives it.

## The diff to the model

Five changes in proctor, all in the runner and its config; one in a check.

### 1. `nb.runner`: the command that runs nb, defaulting to nb itself

`evals/proctor.json` gains `nb.runner`, a script path relative to `evals/`
like `nb.config`. When set, proctor runs it instead of `nb.path` for the run
step. `nb.path` stays the host binary proctor uses for `--compile`,
`--version` and, later, `--validate`. `--runner <script>` overrides on the
command line and `--runner none` runs bare, which is how a shakedown checks
the eval before anyone builds an image. The runner in effect is in the
environment as `PROCTOR_RUNNER` (empty when bare), so hooks that own a
container can skip it on a bare run rather than build one nb never enters.

The runner contract, which goes in the README and is the whole interface:

| proctor gives | the runner must |
|---|---|
| stdin: the program as JSONL, includes already expanded | pass it to nb's stdin unchanged (`nb … -`) |
| cwd: the work directory on the host | start nb with the checkout as its working directory, wherever that is inside |
| the cell environment (`PROCTOR_*`), plus `PROCTOR_NB` (the host binary), `PROCTOR_NB_CONFIG`, `PROCTOR_WORK_MOUNT`, `PROCTOR_BUNDLE_MOUNT`, `PROCTOR_CONTAINER`, `NO_COLOR` | run nb where the checkout, bundle and config are at the paths those name |
| nothing on argv | run `nb --output jsonl [--config <config>] -` |
| stdout, stderr captured to the cell | put only nb's stdout on stdout |
| | exit with nb's exit code |

Without a runner, proctor composes exactly that argv itself and pipes the
same stdin, so the bare path and the runner path are one code path with a
different executable. The subprocess helper grows a stdin argument.

### 2. `nb.mounts`: where the model will see the checkout and the bundle

`{{work}}` and `{{bundle}}` are baked into the program before nb starts,
so a container needs them to resolve to the paths inside it.
`nb.mounts.work` and `nb.mounts.bundle` are those paths; when absent, the
placeholders resolve to the host paths as now. Proctor passes both to the
runner and to checks as `PROCTOR_WORK_MOUNT` and `PROCTOR_BUNDLE_MOUNT`,
set to the host path when no mount is configured, so a script can always
read "the path the model was told" from one variable. `PROCTOR_WORK` and
`PROCTOR_BUNDLE` stay the host paths, because hooks and checks run on the
host and need to find the files.

### 3. The program travels on stdin, compiled on the host

Proctor keeps writing `program.nb` to the cell for reading. Before the run
it calls `nb --compile program.nb` on the host binary, writes the result as
`program.jsonl`, and pipes that to the runner. Includes (`@file`, an oracle
sheet, a costume's instruction file) are therefore resolved against the
eval on the host, and the container never holds a path to them. The
program hash in the manifest stays the hash of the source. Until nb ships
`--compile`, proctor pipes the source text, which is identical for a
program without includes; the two forms are indistinguishable to nb, which
sniffs JSONL.

### 4. The manifest says how nb was run

`Versions` reads `nb --version` from the host binary instead of the DLL
beside it, so a wrapper no longer turns the version into `unknown`. The
manifest and `experiment.json` record the runner as `{ script, hash }` when
one is set, hashed like a program. The image's identity is the consumer's:
the example arm setup hook logs `podman image inspect --format '{{.Digest}}'`
so it lands in `hooks/arm.setup.log`. `resume` refuses a changed runner as
it refuses a changed eval or bundle.

### 5. The container's lifetime is the hooks'

Two small changes make the split form safe. `PROCTOR_CONTAINER` joins the
cell environment: a name derived from the experiment, arm, case and sample,
which proctor never uses and every script may. And the sample teardown hook
runs whenever the sample setup hook ran, whether or not setup succeeded;
today a failed setup skips teardown, which would leave a half-made
container behind for the resumed cell to trip over. Teardown is therefore
required to be idempotent, and the contract says so. The arm level is
unchanged: image builds, warmed caches and fakes shared across cells belong
there already.

### 6. `stays-in-work` compares against the mounted path

The check reads `PROCTOR_WORK_MOUNT` in place of `PROCTOR_WORK`, and the
allowlist of system paths gains `/opt/nb` and `/nb`. In a container the
check is structurally true, since there is nothing else to reach; it stays
as a cheap tripwire and as the check that keeps meaning when the eval is
run bare.

## What proctor does not do

- **Compose the container command.** Podman, Docker, bwrap, a devcontainer
  and a CI job container differ in flags, networking and UID mapping, and
  the fakes an eval needs differ per eval. The runner is the consumer's
  script, as hooks are.
- **Run the fixture's checks inside the container.** Checks run on the
  host after the container is gone, as now. A check that needs the image
  says so itself.
- **Narrow the network.** With the model served from another box the
  container needs egress to it; `--network none` is not available, and a
  curated network is the consumer's runbook. nb's own guidance on sharing a
  network namespace with the model applies unchanged.
- **Hide the key.** nb reads keys from the environment; the model shares
  that environment. The runbook says so; the transcript shows any read.

## What nb owes

Filed in `nb/plans/container-runs.md`, in this order:

1. **`--compile`**: parse, resolve includes, emit JSONL, run nothing. The
   size of `--resolve`. Proctor's step 3 uses it the day it exists.
2. **A Containerfile** that publishes nb self-contained for linux-x64 into an
   image with `/opt/nb/nb`, providers beside it, no `appsettings.json`,
   `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. Its use is as a layer:
   `COPY --from=nb /opt/nb /opt/nb` onto whatever base the fixture needs.
   Whether it is published to a registry is a distribution decision for
   later; a local build is enough for this plan.
3. **The runbook**, `docs/containers.md`: the cookbook's "basic container
   case, and podman well" row, written once. Image contents, `podman run
   -i` with stdin as the program and stdout as the transcript, rootless UID
   mapping and `--userns=keep-id`, where config and keys enter, `--network`
   choices, and the artefact inventory: what nb leaves in the model's reach
   and what each leaks. Proctor's README links it rather than repeating it.

The one interface point between the repos is that nb keeps accepting a
JSONL program on stdin and keeps `--config` as the way a deployed binary
finds its configuration. Both are documented today.

## Build order

Five sessions, each ending in a commit and a status line here. The nb
session runs alongside the first two; the live run waits for it; the
runbook is written last, from what the live run actually did.

1. **Proctor: the runner contract.** `nb.runner`, `--runner`, stdin in the
   subprocess helper, the cell environment plus `PROCTOR_CONTAINER` and
   `PROCTOR_RUNNER` passed to the run step, teardown after any setup that
   ran, the runner in the manifests, `resume` refusing a change. Done when
   the smoke eval run through a runner script that is nothing but
   `exec "$PROCTOR_NB" --output jsonl --config "$PROCTOR_NB_CONFIG" -`
   produces a cell identical to a bare run.
   *Status 2026-09-21: done.* Two things the session settled that the
   contract above did not say: the runner also gets `PROCTOR_NB`, the host
   binary, so a passthrough runner needs no path of its own; and the bare
   path keeps its environment to `NO_COLOR` only, because `PROCTOR_EXPECT`
   in nb's environment is the answer in the model's reach. The same warning
   applies to a runner that forwards its environment into the container
   wholesale; the example runner forwards nothing. The manifest records the
   whole `nb` block (`path`, `config`, `runner: {script, hash}`) rather than
   the runner alone.
2. **Proctor: mounts and identity.** `nb.mounts`, the placeholder
   resolution, the two `_MOUNT` variables, `stays-in-work` reading the
   mount path, `nb --version` through the host binary, and the runner
   contract table in the README. Done when a test resolves `{{work}}` to
   `/work` and a hook sees both the host path and the mount.

   *Status 2026-09-21: done.* One rule the section above left open: the
   mounts are only in effect with a runner. A bare run resolves the host
   paths whatever `nb.mounts` says, so `--runner none` stays a shakedown of
   the eval on the host rather than a run that tells the model about a
   `/work` that is not there. The experiment and every manifest record the
   mounts in effect under `nb.mounts` (absent when bare). Mounts must be
   absolute; `LoadConfig` reports a relative one as a problem.
3. **nb: `--compile` and the Containerfile** (`nb/plans/container-runs.md`,
   items 1 and 2). Independent of steps 1 and 2. Done when a program with
   an include and an oracle sheet compiles to JSONL that runs identically
   under Mock, and `podman build` yields an image with `/opt/nb` and
   nothing `docs/distribution.md` says must not ship.

   *Status 2026-09-21: done* (nb 96eb757, 19a8e38). Beyond the contract
   above: `--compile` also folds in `--seed` and runs nb's directive-shape
   check, so a program nb would refuse fails on the host, not in the
   container. The image's final base is `runtime-deps:10.0`, so it runs on
   its own for a smoke test as well as serving as a layer; its `ENV
   DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` does not travel with `COPY
   --from`, so the fixture Containerfile in step 4 sets it (the setup hook's
   `-e` already does). Building the image found that `dotnet publish
   nb.csproj` had never worked from a clean checkout; fixed in nb's publish
   target, so step 4's image build needs no prior restore. Verified with
   docker, not podman: this box has no podman, so step 4 is the first
   podman build.
4. **Proctor: the live example.** `evals/runners/podman.sh`, the sample
   setup and teardown hooks, the `Containerfile` for the .NET stack on nb's
   layer, the NuGet volume warmed and the image digest logged in the arm
   setup hook. Then the code-change eval on imp through it, compared to the
   bare baseline. Done when the guard report says held. This is where UID
   mapping, the package cache and reaching imp from inside the container
   get settled, so it is its own session.

   *Status 2026-09-22: done* (proctor 3d2d56e). The runner is
   `evals/runners/container.sh`, not `podman.sh`: this box has docker and
   no podman, so every script picks the engine with `command -v podman`,
   and the only line that differs is `--userns=keep-id` (podman only,
   untested here). The container is entered by `--runner` on the command
   line rather than `nb.runner` in `evals/proctor.json`, because that file
   is shared with `smoke`, whose hooks make no container (open question
   below). Hooks gained `PROCTOR_NB` and `PROCTOR_NB_CONFIG` so the setup
   hook can mount the config the host binary was resolved with. The image
   is identified in `arm.setup.log` by id (docker's legacy builder has no
   digest for a local image), on nb's image id. imp is reached by
   `--add-host` from `/etc/hosts`; the key alone crosses, by `-e`. The SDK
   image already has a uid 1000, so `useradd -o`; the gid is a build arg
   too, or the files come out as the caller's uid and a stranger's gid.
   Nine cells in nine containers (`20260922-1150-code-change-6y0s`)
   completed and passed; the bare run the same afternoon
   (`20260922-1205-code-change-1itz`) is pinned as the baseline, and the
   guard report says `held`. Container cells were no slower than bare (66 s
   median against 75 s). `docker exec` buffers stdout, so the transcript
   lands whole at the end of a cell; nothing in proctor reads it early.
5. **nb and proctor: the runbook and the compile switch.**
   `docs/containers.md` written from step 4, not before it; proctor's
   stdin switching from source to `program.jsonl`; the README linking the
   runbook and saying that a bare run is for shakedown and a container is
   for anything you would not run on your own machine. Done when a reader
   can go from a fresh checkout to a containerised run without reading
   either runner.

   *Status 2026-09-22: done.* `RunCell` compiles after writing `program.nb`
   and before the checkout: `nb --compile [--config]` on the host binary,
   in the eval directory, the JSONL written as `program.jsonl` and piped to
   bare nb and runner alike. `program_hash` is still the source's. A
   program nb refuses (a missing include, an unknown harness) fails the
   cell there, with nb's first stderr line, and no hook runs for it.
   `docs/containers.md` in nb is written from session 4's run, with the
   artefact inventory as a table; the README links it and says what bare
   and container are each for.
   Acceptance: `20260922-1231-code-change-5ksh`, nine cells in nine
   containers with the compiled program on stdin, 9 of 9 completed and
   passed, `held` against the bare baseline, no container left behind.

## Open questions

- **Where `nb.runner` belongs.** It is in `evals/proctor.json`, which is
  shared by every eval, and the runner only works with the hooks of an
  eval that make its container, so setting it there breaks `smoke`. The
  runner and the mounts are one unit with those hooks, which argues for
  `eval.json`; against that, whether a run is bare or in a container is
  the machine's choice as much as the eval's, which is what `--runner` on
  the command line expresses. The lean is an `nb` block in `eval.json` as
  the eval's default, overridden by the flag, with `proctor.json` keeping
  only the host binary and its config. Not built: one eval with hooks is
  not evidence, and the flag covers the case today.
- **Per-fixture images.** The example picks the image from the fixture's
  `stack`, which `fixture.json` already declares. If that holds up, an
  `image` field on the fixture is the obvious next step; it is not in this
  plan because one stack is not evidence.
- **Checks in the container.** If a second fixture needs its toolchain
  only in the image, the `podman run` form in every check script becomes a
  third copy of the runner. That is the point to consider a `PROCTOR_EXEC`
  the checks can call, and not before.
- **Fake services per sample.** The office bench's shape is a pod per cell
  with the fakes inside it. The split form expresses it with no new
  feature: sample setup creates the pod and starts the fakes in it, the
  container joins it, teardown pulls the fakes' logs and removes the pod.
  It wants a second worked example once there is an eval that needs one.
