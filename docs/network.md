# What an agent can reach

A coding agent runs in a container, through
[Harbor](https://github.com/harbor-framework/harbor), and proctor
compiles what that container may reach on the network. This page says
what that is, what it is not, and how to see it for yourself. The engine
guides ([`docker.md`](docker.md), [`podman.md`](podman.md)) say how to set a machine up; they point here for
the rules.

Checked on 2026-11-27 with Harbor 0.26.0, on rootless podman 5.7.0 and
rootless Docker 29.1.3, both on Linux. Not run: rootful Docker, Docker Desktop, any engine on macOS or
Windows.

None of this applies to nb run bare. Bare nb runs on your
machine, or in whatever the exam's `nb.runner` starts, with that
process's network.

## The rule

An agent reaches its model, what its fixture provides, and nothing else.

| Where the model is | What the agent's container reaches |
|---|---|
| A vendor's API, or any other machine | that host, by the name in the base URL |
| The machine that runs proctor (`localhost`, a `127.` address) | that one port, relayed to its own `localhost`; no network at all |
| A service of the fixture (the stub model) | the fixture's services at its own `localhost`; no network at all |
| Nowhere (nb on its Mock provider) | no network at all |

Beside the model:

- **A fixture's services** answer at the agent's `localhost`
  ([`service-fixtures.md`](service-fixtures.md)).
- **Hosts a fixture names** in `image.allowed_hosts` join the model's
  host, for a service that cannot sensibly be simulated.
- **Ports of your machine a fixture names** in `image.host_ports` are
  relayed like a model on it: a dev server you started by hand, say.

[`configuration.md`](configuration.md), "Fixtures", is the reference for the three
fields.

The checks do not run in the agent's container. Harbor runs them
afterwards in a separate one, with no network.

## The policy holds for the container's whole life

Harbor applies an agent's policy only while the agent is working. Proctor
therefore gives the task's environment the same policy as the agent, so
a process the agent leaves running has no more network after the agent
has finished than it had before, nor between the stages of a try.

Two exceptions:

- **A stage with an answer sheet** (an oracle) is compiled with the
  environment open, because Harbor installs its bridge to the simulated
  user from npm inside the container on every trial. The agent's own
  phase is still held to the policy.

## What an allowlist is, and is not

When the model is on another host, the agent's container is on Harbor's
allowlist, with that host on it. Measured from inside a trial, on
rootless Docker by name, by address and with the network open as the
control, and on rootless podman by name, where every answer was the
same:

- **It is by host, never by port.** Every HTTP and TLS port of an allowed
  host is open to the agent: a second web service on your gateway's
  machine, whatever its port. A protocol where the server speaks first,
  ssh for one, is not.
- **It cannot be talked past.** A request to another address that names
  the allowed host in `Host` or in the TLS server name is answered by
  the allowed host, not by the address it was sent to.
- **A name matches as spelled.** Write the base URL the way the agent
  will use it. `*.example.com` is accepted for the subdomains (read in
  Harbor's source, not measured).
- **An address is looser than a name.** An allowed IP address is open on
  every port and to every protocol. Prefer a name.
- **Names still resolve, and a ping may still leave.** Nothing can be
  fetched that way, but "the agent can touch nothing else" is not true
  as a sentence. Ping is gone on Docker wherever proctor drops the
  `NET_RAW` capability from the agent's container, which is every task
  with a fixture's services or a relayed port
  ([`service-fixtures.md`](service-fixtures.md) says why). A fixture
  with neither, its model on another host, keeps it.

So a gateway on another machine of your network works, and opens that
machine's other web services to the agent. If that matters, run the
gateway where proctor runs and let the relay carry the one port (below).

One thing can stop a host on your network from answering at all: an
address inside the range the engine gives its own container networks
(Docker's default bridge takes `172.17.0.0/16`, and its further networks
`172.18` to `172.31`; podman's take `10.88` and `10.89`). A container
then looks for the host among its neighbours. `run` prints a warning when
it sees this, and the attempt times out. Use a name or address outside
the range, or move the engine's range.

## A port on this machine

A base URL at `localhost` means the machine that runs proctor. `run`
carries that port, and only that port, to the agent's own `localhost`:
it listens on a unix socket in your runtime directory and mounts the
directory into a small service beside the agent. Nothing is bound to
the network, nothing else on the machine can be reached that way, and
the agent's phase has no network at all. `run` refuses to start when
nothing answers on the port. It is the same on Docker and podman.

The sockets are under `$XDG_RUNTIME_DIR/auspex-relay/`, one directory
per run, removed when the run ends. A session with no runtime directory
(`su` with no login session, say) gets them under the system's temporary
directory instead. The service
beside the agent is named `auspex-relay`, and runs on
`python:3.13-alpine`, which the engine pulls once.

**Never open the host's loopback to containers** to get the same effect
(pasta's `--map-host-loopback`, rootless Docker's host-loopback switch,
a gateway bound to every interface). Each hands every loopback service
on the machine to every trial.

The relay has not been tried on Docker
Desktop or any macOS engine, where a host socket may not mount through
the engine's virtual machine.

## The agent holds its model's key

The key is in the agent's container, since the agent's own CLI makes the
calls. An agent can therefore call your gateway outside its harness: any
route and any model that key serves, with no record in the trajectory.
The relay carries bytes and holds no key, so it does not change this.
Give the agents a key that serves only the routes the exam needs, if
your gateway can issue one.

## Vendor web tools are off

Under any policy other than an open network, Harbor starts Claude Code
without its web search and web fetch tools, and Codex without web
search: those run on the vendor's side, where no policy on the container
reaches them. Qwen Code's fetch tool runs in the container, under the
policy.

## What this is not

It is a boundary for an agent doing its task, not a defence against one
that means to get out. The agent is root in its container. Known gaps in
Harbor's allowlist, open there on 2026-11-26: names resolve and ICMP
passes under a policy (issue 4167), and reports of connections that skip
the hostname check (3503). On macOS the allowlist depends on the engine
VM's kernel (2527, 2593).

## Seeing it for yourself

- A run's compiled tasks are under
  `log/<id>/harbor/tasks/<configuration>/<exam>/`. `task.toml` holds the policy
  (`network_mode` and `allowed_hosts`, the same under `[agent]` and
  `[environment]`), and `environment/docker-compose.yaml` the services
  beside the agent, the relay among them.
- The log entry (`log/<id>/entry.json`) records the ports relayed, under
  `harbor.relay`.
- `project/learnings/netprobe/` is the task the measurements above were
  made with: an agent that is a script, two targets, no model. `run.sh`
  there runs it on your engine in about two minutes, and its output
  files in that directory are what it printed on ours.
