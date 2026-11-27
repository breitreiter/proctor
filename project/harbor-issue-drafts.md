# Issues for Harbor, drafted

Drafts for the user to read and file, or not (2026-11-27). Nothing here
has been posted. Each was measured on Harbor 0.26.0 unless it says
otherwise; the evidence is in `learnings/harbor.md` under the heading
named. Before filing any of them, search Harbor's tracker again: the
survey of open issues was made on 2026-11-26, and 0.27.0 has not been
run here yet.

| | Draft | State |
|---|---|---|
| 1 | A port in an allowlist entry | ready |
| 2 | The agent's container holds `NET_RAW` on Docker and can read a service's loopback traffic | ready |
| 3 | On Modal a compose task's image is built under the environment's policy | ready; to check against 0.27.0 first |
| 4 | A comment on pull request 4427: services reached at `localhost` | **not ready**: waits for the trials of 2026-11-28 |

Not drafted, because not yet reproduced: what an interrupted trial
leaves in the engine (`todo.md`).

---

## 1. Feature: a port in an allowlist entry

**Title:** Allowlist entries cannot name a port, so an allowed host is every web service on it

An `allowed_hosts` entry is a domain, a wildcard, an IP, a CIDR or a
range. There is no way to say "this host, this port".

Measured from inside a trial on 0.26.0 (rootless Docker 29 and rootless
podman 5.7, the agent on an allowlist of one name): a second HTTP port
on the allowed host answered, and so did a TLS port. For an entry that
is an IP address, every port and protocol answered, including one where
the server speaks first.

Where it matters: a model gateway usually shares its machine with other
things. Allowing an agent its gateway at `gateway.example:8086` also
allows it whatever else serves HTTP on `gateway.example`, on any port.
The task author knows the one port and has nowhere to write it.

**Asked for:** an entry form with a port, `host:port` (and `ip:port`),
enforced in the sidecar, with a bare host keeping today's meaning.

**What we do meanwhile:** for a service on the machine that runs the
job, we give the agent's phase `no-network` and carry the one port into
the task over a unix socket ourselves. That does not help for a host
elsewhere.

---

## 2. The agent's container holds `NET_RAW` on Docker

**Title:** On Docker the agent can capture a compose service's traffic on the shared loopback (default `NET_RAW`)

With egress control on, a compose service that declares no `networks`
or `network_mode` is put in the main container's network namespace, so
the agent reaches it at `localhost`. On Docker a container holds
`NET_RAW` by default, and Harbor neither adds nor drops capabilities on
`main` (only the sidecar adds `NET_ADMIN` and `NET_RAW`). So a root
agent can open a packet socket on `lo` and read whatever the service
says there.

Measured on 0.26.0, rootless Docker 29.1.3, the agent on an allowlist:
a service made a request of itself on the loopback once a second with a
header; a packet socket in the agent's container read the header. The
service's files, environment and process were not visible, only its
traffic. On rootless podman 5.7 the same capture was refused, since
podman's default capability set has no `NET_RAW`.

It is a way for an agent to read what a task's service was meant to
keep from it (a credential a service sends upstream, a call between two
services, a verifier's request).

**What closes it:** `cap_drop: [NET_RAW]` on `main`. We add
`main: { cap_drop: [NET_RAW] }` to the task's compose file; Harbor
merges its own `main` over it, the container shows
`CapDrop=[CAP_NET_RAW]` in the agent's phase and the verifier's, and the
capture is refused. The cost is `ping` in the agent's container on
Docker.

**Asked for:** drop `NET_RAW` on `main` by default when a policy is in
force, or say in the docs for compose tasks that a service sharing the
agent's namespace is audible to it on Docker.

(If pull request 4427 lands, services on Docker get namespaces of their
own and this goes away there. It would remain wherever a service is
joined to `main` by `network_mode`, as on Modal's VM runtime, where the
agent also holds `NET_RAW`.)

---

## 3. Modal: a compose task's image is built under the environment's policy

**Title:** Modal (VM runtime): a compose task cannot have a closed environment, so a process outlives the agent's policy

On Modal's VM runtime (`_ModalDinD`), a compose task's image is built
inside the sandbox, and the build is subject to the task's
`[environment]` network policy.

Measured on `harbor[modal]` 0.26.0:

- With `[environment]` on an allowlist (the same as `[agent]`), the
  image is never built: the pull of the base image is refused
  (`connect: connection refused` to the registry), and the trial fails
  in about twenty seconds.
- With `[environment]` public the task builds and runs. But the agent's
  policy is applied only for the agent's phase, so a process the agent
  left running (a loop fetching `http://example.com` once a second) was
  refused throughout the phase and answered 200 from the second it
  ended, until teardown.

On Docker and podman the build is the engine's and is not policed, so a
task can give `[environment]` the agent's policy, and the same loop gets
nothing from start to teardown. On Modal there is no setting that both
builds the image and keeps the container closed after the agent.

**Asked for:** build (and pull) before the environment's policy
applies, or a policy for the build of its own, so `[environment]` can
be as closed on Modal as elsewhere.

Related, same runtime, and perhaps only a line for the docs: a compose
service that declares no network gets a namespace of its own there, so
a task whose agent reaches a service at `localhost` on Docker or podman
does not find it on Modal unless the service declares
`network_mode: service:main`.

---

## 4. Comment on pull request 4427 (not ready)

To write after the trials of 2026-11-28. What reading the diff says,
which the trials are to confirm or correct:

- A task whose agent reaches a compose service at `localhost` today
  (which is what the released behaviour gives it) stops working on
  Docker under the pull request, and goes on working on podman. The
  same task then needs two addresses by engine.
- The question for its author: is there a supported way for a service
  to stay in the agent's namespace on Docker (a `network_mode` naming
  the agent's sidecar is left as written, but the sidecar's name now
  carries a hash), or should a task move to service names, and if so
  will podman get the same?
