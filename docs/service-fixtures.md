# A fixture with services

Some stages need something to talk to: an API the code under change
calls, a documentation site, a scripted model. A fixture can bring such
services with it. They run beside the agent, the agent reaches them on
the network, and it cannot read their source or their configuration.
That last part is the point: a planted behaviour stays planted when the
agent can only ask.

Checked on 2026-11-27 with Harbor 0.26.0 on rootless podman 5.7.0 and
rootless Docker 29.1.3, both on Linux. Nothing here was run on macOS.

Two fixtures in this repository have services, and are the examples to
copy: `fixtures/stub` (one service, a scripted model) and
`fixtures/tern-shop` (an API and a docs site).

## The layout

```
fixtures/<id>/
  fixture.json
  repo/                      the only thing the agent's container holds
  services/
    docker-compose.yaml
    api.py                   whatever the services run
  checks/
```

```json
"image": { "from": "python:3.13", "services": "services" }
```

`image.services` names the directory. Proctor copies it into each
task's build context beside the image, never into `repo/`, and Harbor
starts its services with the agent's container.

## The compose file

```yaml
services:
  api:
    image: python:3.13-alpine
    command: ["python", "-u", "/srv/api.py"]
    volumes:
      - ./api.py:/srv/api.py:ro
```

- **Declare no `networks`, no `network_mode` and no `ports`.** Harbor
  puts a service that declares neither of the first two in the agent's
  network namespace, so the agent reaches it at `localhost:<port>` and
  your machine listens on nothing. A service that declares one of them
  is left as written and is then somewhere else.
- **The agent uses `localhost`, not the service's name.** Service names
  do not resolve from the agent's container. Write `localhost` wherever
  the repository's settings or the prompt say where the service is.
- **Every service shares one set of ports** with the agent and with each
  other. Pick ports that do not collide, and not ones the agent's own
  work is likely to bind (a dev server's default, say).
- **Mount what the service runs, read-only, by relative path.** Paths
  are relative to the services directory.
- **Use an image the engine can pull by its full name.** Images are
  pulled by the engine, so the agent's network policy does not stop
  them; a short name needs podman's search registry
  ([`podman.md`](podman.md), section 3).
- Leave `main` out. It is the agent's container and Harbor's to define.
  Proctor adds one line for it (below); if you declare `main` yourself,
  yours is kept and proctor's is not added.

One directory appears beside the compose file for a service to mount:

- `./params/<NAME>`: each tree parameter the fixture declares, empty
  when the configuration sets none. A docs service mounts one and serves
  it, so the agent reads the docs over HTTP and cannot search them on
  disk. `fixtures/tern-shop`'s docs site serves `./params/DOCS`.

## What the agent can and cannot see

Measured from inside a trial, the agent running as root:

| Of the service | podman | Docker |
|---|---|---|
| Its ports at `localhost` | reached | reached |
| Which ports are listening | seen | seen |
| Its process | not visible | not visible |
| Its files, source and environment | not reachable | not reachable |
| Its traffic on the shared loopback | not readable | not readable, because proctor drops `NET_RAW` |

On Docker a container holds the `NET_RAW` capability by default, and
with it an agent could capture everything a service says on the loopback
they share: one service calling another, a header with a secret in it.
Proctor writes `main: { cap_drop: [NET_RAW] }` into the task's compose
file, and the capture is then refused. The cost is `ping` for the agent
on Docker. (A task has a compose file when its fixture has services or
a port of your machine is relayed to it, so those are the tasks with no
ping there.)

So:

- **What a service holds and does not send is out of the agent's
  reach.** What it answers to a request is not. If the API will hand its
  planted behaviour to anyone who asks the right URL, the agent may ask.
- **The agent can use the service as a client would, as often as it
  likes.** It can probe it, and it can change its state. A check that
  depends on the service's state should not trust the copy the agent
  talked to (below).
- **The agent cannot take over a port a service holds**, though it can
  bind any other.

## The checks get no services

Checks run after the agent, in a separate container with no network and
without the fixture's services. A check that needs one starts its own
from the fixture's copy, which the checks' container does hold:

```bash
python -u "$AUSPEX_FIXTURE/services/tern_api.py" &
```

`exams/tern-docs/checks/acceptance.sh` does this, on a spare port when
the usual one is taken. It is also the better test: the service the
checks ask is one the agent never touched.

## A model that is a service

`fixtures/stub` runs a scripted model as a service, and the agents' base
URLs point at `http://localhost:8765`. Since a base URL at `localhost`
otherwise means the machine that runs proctor, the fixture says the port
is its own:

```json
"image": { "from": "…", "services": "services", "serves": [8765] }
```

Only a port a model endpoint uses needs listing. `tern-shop` lists none.

## A service you cannot bring

- **A real service on the internet** that cannot sensibly be simulated:
  name its host in `image.allowed_hosts`. Every web port of that host is
  then open to the agent ([`network.md`](network.md)).
- **Something running on your machine**, early in an exam's life: name
  its port in `image.host_ports`, and it is relayed to the agent's
  `localhost`. The exam then depends on something it does not hold, so
  no one else can run it and no later run is the same. Move it into
  `services/` before the results matter.

## Changing a service

The job's hash covers the fixture's `repo/` and its `image` block, not
the files under `services/`. Edit a service and the tries already
made stay current, though they met a different service. Treat it as
[`configuration.md`](configuration.md), "When an exam gets a copy",
says of any change to the world: copy the fixture and the exam to new
ids.

## Proving it for free

Before any model runs: a `reference/` script that applies a known-good
change and runs the checks, with a failing side
(`exams/tern-docs/reference/shakedown.sh`). That a machine runs
services beside an agent at all is what conformance shows, with the stub
as the model and no cost ([`podman.md`](podman.md), section 7).

## Moving between machines

`fixtures/stub` runs unchanged on rootless podman and rootless Docker.
Use images published for both Intel and Apple processors if the exam
will move between an Apple laptop and a Linux server; the `python:*-alpine` images
do. Harbor is changing how services are placed on Docker (its pull
request 4427, open on 2026-11-26, gives each service its own namespace,
reached by name). The exam's `harbor.version` pin is what keeps a
fixture working as written until proctor follows.
