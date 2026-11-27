---
type: learnings
title: Adding or keeping an engine proctor does not support — what the networking work knows
created: 2026-11-27
---

# An engine of your own: a brain dump

Written 2026-11-27 by the session that built the relay and measured the
network boundary, on the day the core was cut to rootless podman
(`plans/remix.md`). It is for another agent asked to make Docker work
again, bring Modal back, or try an engine nobody has run (macOS, a
hosted sandbox). Raw material for a user-facing recipe, not the recipe.
Everything is as of Harbor 0.26.0 and proctor at 2feb83b. Each claim
says **run**, **read** or **guess**. The longer record, dated, is
`harbor.md` from "A gateway on the same machine as the containers" to
the end.

## The short version

- Proctor does not talk to an engine. It writes a Harbor task and a
  Harbor job file and starts `uv tool run harbor@<pin> run --config`.
  The engine is one string in the job (`environment.type`). So "adding
  an engine" is mostly: does Harbor run this task there, and are the
  six things below still true there.
- Most of what went wrong on every engine was the network, not the
  containers. Budget for that.
- You can find out nearly everything for free, without proctor and
  without a model, with the probe (`netprobe/`, below). Run it first.
  It took about two minutes a trial and settled questions that reading
  Harbor's source did not.

## What proctor needs to be true on an engine

These are the assumptions compile and ingest make. Each has a cheap way
to check it.

1. **Harbor runs a compose task there.** A fixture with services, the
   relay, and the `NET_RAW` drop all live in
   `environment/docker-compose.yaml`. Without compose you have plain
   fixtures and remote models only. (Run: Docker, podman, and Modal's
   VM runtime since 0.26.0.)
2. **A service that declares no network is at the agent's `localhost`.**
   True on Docker and podman at 0.26.0 because Harbor gives such a
   service `network_mode: service:harbor-docker-egress-control-sidecar`
   (run, both). **False on Modal**: each service has its own namespace
   and a name, and needs `network_mode: service:main` to be at
   `localhost` (run). **Will be false on Docker** if Harbor's pull
   request 4427 lands: a sidecar and namespace per service, reached by
   name, the agent's sidecar named with a hash; podman unchanged (read,
   never run). A service that declares `network_mode` itself is left as
   written everywhere, and joining the sidecar by name outright works on
   both local engines (run). So the durable form is for compile to
   write the join line per engine. That was designed and not built: see
   `HarborCompile.Relayed` and `InServices`, where the line would go,
   and the table in `harbor.md`, "The stub fixture's service on Modal".
3. **The environment can carry the agent's network policy.** Harbor
   applies `[agent]`'s policy only during the agent's phase. Proctor
   compiles `[environment]` to the same policy (`HarborCompile.Network`)
   so a process the agent leaves running never gets the internet. That
   needs the image build not to be subject to the policy. True on
   Docker and podman, where the engine builds (run). **False on Modal**:
   the build runs inside the sandbox under the environment's policy, an
   allowlisted environment cannot pull its base image, and with a public
   environment a left-running process got HTTP 200 from the second the
   agent's phase ended (run). That is why proctor refused services on
   Modal, and an engine that builds in-sandbox will have the same
   choice to make: accept the leak and say so on the report, or refuse.
4. **A unix socket on the host can be mounted into a container.** The
   relay is `HostRelay` listening on
   `$XDG_RUNTIME_DIR/proctor-relay/<hash>/<port>.sock` and a compose
   service `proctor-relay` (Python, `Harbor/relay.py`) that mounts the
   directory and serves the port at `127.0.0.1`. Run on rootless podman
   and rootless Docker. **Guess: it does not work through a macOS
   engine's VM** (Docker Desktop, Colima, OrbStack, podman machine);
   file sharing there generally does not carry a live unix socket.
   Nobody tried. If it fails, the fallback is a TCP hop the VM can
   reach, which is a new design with its own exposure, not a tweak.
   Not possible at all on a remote engine: there is no host beside the
   sandbox. `Runner.RequireReachable` refuses `image.host_ports` and a
   loopback model there.
5. **The engine can see nb's layer.** For `agent: nb`, locally proctor
   builds `proctor-nb:<hash>` once and tasks `COPY --from` it; podman
   needs the tag spelled `localhost/proctor-nb:…` or it tries a
   registry (run). Remotely there is no shared image store, so compile
   copies the publish into each task's build context
   (`environment/proctor-nb/`). That branch is keyed on
   `HarborConfig.Remote` and is generic.
6. **What Harbor writes back is what ingest reads.** `Ingest` is strict
   on purpose (unknown tool, wrong version, no trajectory: the attempt
   fails). This did not differ by engine in anything run. It does
   differ by Harbor version, so move the pin and the engine separately.

## Where the engine shows in the code

Small, which is the good news (read, by grep, 2026-11-27):

| where | what |
|---|---|
| `Exam/Exam.cs` `HarborConfig` | the list of engines, `Remote` (today: is it Modal) |
| `Run/Harbor.cs` | `UvArgs` (the `harbor[modal]` extra), nb's image tag and whether it is built, `Preflight` (podman's three checks, Modal's login), `SubnetWarning` (each engine's default container range), `TearDown` (removes a killed trial's leftovers by compose project label, with the engine's CLI) |
| `Harbor/Compile.cs` `Job` | `environment.type`, Modal's sandbox lifetime and idle limit |
| `Run/Runner.cs` | `RequireReachable` on a remote engine; the relay only on a local one; `WhyNotContinue` refuses to continue a run on another engine |
| `Setup/` | what setup observes and offers |

A local engine that speaks Docker's CLI and compose is nearly free:
podman needed the tag prefix, three preflight checks and two lines of
user config. Everything else podman cost was the machine's (below).

## Docker, as it stood

Run on one laptop: rootless Docker 29.1.3, Ubuntu, kernel 7.0, Harbor
0.24.0 and 0.26.0. Both gates (450 s), conformance (1192 s), the relay
with all three vendor agents, a run interrupted and continued.
`docs/docker.md` is the guide and was walked against that machine.
Rootful Docker was never run.

Things that cost time, in the order they were met:

- **buildx missing**: every agent attempt fails building Harbor's
  sidecar with `unknown flag: --file`. Looks like the exam's fault.
- **`DOCKER_HOST`**: Harbor runs under uv and its client did not find
  the rootless daemon from the CLI context alone on that machine; set
  it in proctor's env file. (Not re-tested with the context only.)
- **The model on the same machine.** Rootless Docker closes the host's
  loopback; `host.docker.internal` and the bridge gateway are not the
  host there. Opening the loopback works and hands every loopback
  service to every trial: the user ruled that out, as advice too. The
  relay is the answer.
- **The bridge's range.** Wi-Fi at `172.17.1.0/24` inside Docker's
  `172.17.0.0/16`: containers could not reach any host on the LAN, and
  it presents as a model timeout. `HarborRun.SubnetWarning` exists for
  this. Moving `default-address-pools` was not tried.
- **`NET_RAW`.** Docker grants it by default, and with it a root agent
  read a fixture service's loopback traffic, header and all (run).
  `main: { cap_drop: [NET_RAW] }` in the task's compose file closes it;
  Harbor merges its own `main` over ours and keeps the drop (run).
  Costs the agent `ping`. Podman's default set has no `NET_RAW`.
- **An interrupted run** left the trial's compose project: sidecar
  running, containers exited, network, two images. Only proctor was
  signalled; a terminal Ctrl-C was not tried. Not reproduced on podman.

What would break Docker next, most likely: Harbor's pull request 4427
(point 2 above). When the pin moves past it, the stub fixture, the
`tern-shop` services and the relay all stop answering at `localhost` on
Docker. Three trials were written to settle it and never run, since the
pull request needed a dependency inside uv's seven-day cooldown and
then the core was cut: the probe as it is; `JOIN=<the agent's sidecar
name>`; `JOIN=main`. `run.sh` takes `FROM='git+https://…@<sha>'` for an
unreleased Harbor. The sidecar's name under that pull request was read
as `harbor-docker-egress-control-main-8e62e8c65106` at commit 12a50b9;
check it against whatever lands.

## Modal, as it stood

Built in three spikes (`archive/modal-runs.md`), then probed again at
0.26.0 for a few cents. Experimental throughout: no hosted model's
agent ever ran there.

- Login is Modal's own (`~/.modal.toml` or `MODAL_TOKEN_*`), inherited
  by Harbor's process. Proctor only checks one exists.
- Harbor's default sandbox lifetime is a day with no idle limit. Set
  both per job (`HarborCompile.ModalIdle`) or a killed run bills.
- The image's entrypoint is replaced by `sleep infinity` unless the
  environment is given `keepalive` (read).
- Harbor picks its compose strategy by itself (`_ModalDinD`,
  `vm_runtime: True`) for a task with a compose file (run).
- **There is no egress sidecar.** The policy is Modal's, on what leaves
  the sandbox. Inside the task nothing is policed: a service the
  allowlist did not name answered on every port (run). For a fixture's
  own services that is what you want.
- In the agent's phase the internet is shut, names still resolve (run).
- The agent holds `NET_RAW` there too (run).
- The stub fixture passed there with two hand edits: `[environment]`
  public, and the stub joined to `main` (run, reward 1 in two minutes).
  So conformance on Modal is two lines of compile away, plus the
  decision in point 3.
- A model must be on the internet. `HarborRun.Unreachable` is the list
  of hosts a sandbox cannot reach (loopback, private ranges, dotless
  and `.local`-style names).

If Modal is rewritten outside the core, its real requirements are: no
local image build, a login to check, lifetimes on the job, a set of
unreachable hosts, a join line of its own, and a stated answer to the
post-phase leak. Harbor has merged, unreleased as of 2026-11-26, a
sidecar proxy for Modal with its own allowed hosts and secrets; read
that before designing anything about keys.

## macOS and anything else unrun

Nobody has run an agent variant on a Mac. Known from outside only:
Harbor's allowlist depends on the engine VM's kernel and has open
reports of failing there (its issues 2527, 2593); the relay's socket
mount is a guess (point 4). The order to find out, cheapest first: the
probe with `ENGINE` set, by name; then the same with a hand-made relay
(`netprobe/relay/`); then the gate; then conformance.

## How to check an engine, for free

1. **The probe**, `project/learnings/netprobe/run.sh <name> '<policy
   lines>'`, Harbor's `oracle` agent, no model. Two targets with HTTP on
   two ports, TLS, and a server-speaks-first port; a service `beside`
   that declares no network; a process left running that fetches
   `example.com` every second. Knobs: `ENGINE`, `HARBOR`, `FROM`,
   `ENVPOL` (the environment's policy), `JOIN`, `DROP`, `NET` (subnet,
   so trials run side by side). The committed `*.txt` files are what it
   printed on each engine; diff yours against them. One early fault to
   know: the script once wrote no agent policy when `ENVPOL` was unset,
   fixed since.
2. **The gates**: `PROCTOR_GATE=<nb publish> PROCTOR_GATE_ENGINE=<engine>
   dotnet test --no-build --filter GateTests`. nb on Mock bare and
   through Harbor must agree. About eight minutes locally.
3. **Conformance**: `PROCTOR_CONFORMANCE=<a proctor.json naming the
   engine> dotnet test --no-build --filter ConformanceTests`. Every
   vendor agent against the stub, which is a fixture with a service, so
   it is also the test of point 2. About twenty minutes.
4. **The relay**, in a scratch root, never against live routes: copy
   `exams/conformance` and `fixtures/stub`, run a copy of `stub.py` on
   the host's `127.0.0.1` on another port, point the agents' base URLs
   at it, `proctor run conformance --tasks ok`. The compiled task should
   show `no-network` for agent and environment and a `proctor-relay`
   service. Stop the host stub and `run` should refuse before making
   anything.

Never run `proctor run conformance` in the real root: its machine file
names paid routes.

## Traps that are not about the engine

- **uv's cooldown** (`--exclude-newer "7 days"`) applies to Harbor's
  dependencies too. A Harbor release can be unresolvable for a week
  after it ships because of one dependency. It is deliberate (a
  dependency of Harbor's once shipped a credential stealer) and the
  user declined an exemption. Plan pin moves around it.
- **Podman's machine setup** is most of `docs/podman.md`: the API
  socket, docker-compose v2 and not podman-compose, a search registry,
  the compose banner off, and on Ubuntu 26.04 an AppArmor rule so podman
  can stop pasta. None of it is proctor's, all of it presents as
  attempts failing.
- **A bridged task** (an answer sheet) is compiled with a public
  environment because Harbor installs acpx from npm in the container on
  every trial. Any engine without the internet at that moment cannot
  run oracle tasks through Harbor.
- **Vendor web tools**: at 0.26.0 Harbor turns them off itself under
  any non-public policy. Before that, Codex offered `web_search` under
  an allowlist, served on the vendor's side where no policy reaches.
- **The allowlist is by host.** No port. An allowed IP is every port
  and protocol. `docs/network.md` is the measured statement.
- **A fixture's `services/` files are in no hash** (`todo.md`). Change a
  service while porting and old attempts still read as current.
