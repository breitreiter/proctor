# Agent variants on Docker

Docker is the default engine (`harbor.engine: docker`, or no
`harbor.engine` at all). It needs less setting up than podman, and
rootless Docker is not nothing.

Checked on 2026-11-27 with rootless Docker 29.1.3 on Linux (kernel 7.0)
and Harbor 0.26.0: both gates (about seven and a half minutes),
conformance (every vendor agent against the stub, about twenty minutes)
and a model on the machine's loopback through the relay. **Not run:
rootful Docker, Docker Desktop, and any engine on macOS or Windows.**
What this page says of those is from Docker's and Harbor's own
documentation and issues, and says so.

What an agent's container can reach, on any engine, is
[`network.md`](network.md).

## 1. What Harbor needs

- **Docker with the Compose v2 and buildx plugins.** Ubuntu's `docker.io`
  package brings neither: install `docker-compose-v2` and
  `docker-buildx` beside it. Without buildx every agent attempt fails
  building Harbor's egress sidecar with `unknown flag: --file`, while
  bare attempts are untouched, which makes it look like the exam's fault.
- [uv](https://docs.astral.sh/uv/), which runs the pinned Harbor. It
  needs no root.

## 2. Rootless Docker: say where the socket is

```
# ~/.config/proctor/env
DOCKER_HOST=unix:///run/user/<uid>/docker.sock
```

Harbor runs under uv, and a Docker client there may not read the CLI
context your shell uses. Proctor loads its env file into every process
it starts. `id -u` prints the number.

## 3. A model on the same machine

Leave `agents.<agent>.base_url` as you would use it yourself,
`http://localhost:8086` say. `run` relays that one port to the agent's
own `localhost` and gives the agent's phase no network
([`network.md`](network.md), "A port on this machine"). It needs
nothing of Docker.

Rootless Docker ships with the host's loopback closed to containers,
and `host.docker.internal` there is not your machine. Leave both as they
are. Opening the loopback
(`DOCKERD_ROOTLESS_ROOTLESSKIT_DISABLE_HOST_LOOPBACK=false`) hands every
loopback service on the machine to every trial, an agent under test
included.

On rootful Docker a container's gateway is the host (read, not run), so
a service on your machine that listens on every interface may be in a
container's reach where the policy allows the host. A gateway that
listens on loopback only, reached through the relay, avoids the
question.

## 4. A model on your network: check the bridge's range

Docker's default bridge is `172.17.0.0/16`, and the networks Compose
makes take `172.18` to `172.31`. If the machine's own network is inside
one of those (a Wi-Fi network at `172.17.1.0/24` was the case seen), a
container looks for a host of that network among its neighbours on the
bridge, and never finds it. Every model call times out.

`run` warns when a host it compiles into the allowlist is inside these
ranges. To see it yourself, compare `ip -4 addr` on the machine with
`docker network inspect bridge`. The ways out: reach the model by an
address outside the range, run it on the same machine (section 3), or
give Docker other ranges (`default-address-pools` in the daemon's
config, which is the machine owner's to change).

## 5. What a trial leaves behind

Harbor ends every trial with `compose down --rmi local --volumes
--remove-orphans`, for the agent's environment and again for the
checks': a finished attempt leaves no container, no network and no image
of its own.

Two things skip that:

- **A Harbor that proctor kills**, when an attempt outruns its wall limit
  and the overhead allowed for building. Proctor then removes the trial's
  containers, networks and images itself, by the compose project's
  labels, and the attempt's failure says what was removed.
- **An interrupted run.** A `run` that was sent an interrupt in the
  middle of an attempt left that trial's compose project behind: the
  sidecar still running, the other containers exited, and the network.
  The continued run was not hindered. Whether a Ctrl-C at the terminal
  leaves the same is not yet known.

To clear what is left, when no run is in progress:

```bash
docker ps -a --format '{{.Names}}' | grep -E '__env-|__verifier__trial-' | xargs -r docker rm -f
docker network ls --format '{{.Name}}' | grep -E '__env_default$|__trial_default$' | xargs -r -n1 docker network rm
docker images --format '{{.Repository}}' | grep -E '__env-main$|__verifier__trial-main$' | xargs -r docker image rm
```

Networks matter more than their size suggests: each holds a subnet from
a finite pool.

## 6. Checking a machine: conformance

```bash
PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests
```

Every vendor agent at its pin against a scripted model, and each
`code-change` task's reference solution through Harbor's checks. About
twenty minutes, no model, $0. It takes `harbor.uv` and `harbor.engine`
from `exams/proctor.json`, or from a file you name in place of `1`.

## 7. macOS and Windows (not run)

Nobody has run proctor's agent variants on Docker Desktop, OrbStack,
Colima or podman machine. What is known from outside:

- Each runs containers in a Linux virtual machine. Harbor's allowlist
  depends on that VM's kernel, and Harbor has open reports of it failing
  on macOS engines (its issues 2527 and 2593, open on 2026-11-26).
- The relay mounts a unix socket from your machine into a container. A
  host socket is not known to pass through any of these VMs, so a model
  on the same machine may not be reachable by the relay there. A model
  on a vendor's API or on another machine does not need it.
- Docker Desktop's licence may matter to your employer.

An exam and its fixtures do not change between machines: the engine is
the machine's (`exams/proctor.json`), never the exam's. If the laptop
cannot run agent variants, the exam can still be written and its checks
proved there (bare nb, the reference scripts), and its agent variants
run on a Linux machine or on Modal ([`modal.md`](modal.md)).

## 8. Harbor without your credentials in reach

[`podman.md`](podman.md), section 8, wraps uv in a sandbox that hides
your home directory from Harbor's dependencies. The same reasoning
holds on Docker. The wrapper there is written for podman's sockets and
has not been adapted or run for Docker's.
