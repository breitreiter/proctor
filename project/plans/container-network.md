---
type: plan
title: What an agent under test can reach — a research plan
created: 2026-11-25
status: a research plan, written at the user's request 2026-11-25; nothing is built. The user answered its four open decisions the same day ("Decided"). Golem's legs were run 2026-11-25: A on rootless Docker (A1 to A5 answered; the fix for A4 is built, see the last section) and B1 with part of B3 (the relay carries all three vendor agents); the results are in `learnings/harbor.md`, its last three sections. Left: podman and rootful Docker for both, B2, B4 to B6, C, D, E, F. The stance and the candidate mechanisms are still mine and not agreed
---

# What an agent under test can reach

## Why

An agent under test should reach what it would reach in use, its model
above all, and nothing else: not the open internet, not the other
services on the machine that runs it. An agent that can is not being
malicious. It is solving its task with whatever answers, and a result
got that way measures the network and not the variant.

Today that holds in one arrangement (a vendor agent through Harbor, its
model at a public name) and is unknown, awkward or absent in the others.
The todo "The container-engine guides" (2026-11-25) cannot be finished
until we know what to say. A new user meets this on their first run with
a real model, usually as a connection that times out with no reason
given, and the fixes that come to hand first are the dangerous ones.

**The stance being tested**, mine, not agreed: the agent reaches its
model through one opening, and proctor makes that opening. Advice that
has a person reconfigure their engine's network is where
`docs/podman.md` §5 went wrong.

## Decided (the user, 2026-11-25)

- **Tools that run at the vendor are off by default** (C2): "almost
  certainly off-by-default. just too much risk that your fixtures model
  something real or shipping and the model finds the live docs/schema".
  An exam that wants one on will have to say so; how is C2's to design.
- **Modal's sandbox time is agreed** (D): it "pales in comparison to
  model spend for a run".
- **Root is the user's hands** (B5), if it comes to that.
- **Machines.** Golem is rootless Docker and stays so. Both podman
  machines are away from the user until 2026-11-26 at the earliest;
  podman legs wait for then. No machine has rootful Docker; see
  "Order", 2.

## What is known

Run means seen on a machine; read means from Harbor 0.24.0's source in
uv's cache, not run.

| | | |
|---|---|---|
| The allowlist admits the named host and refuses another name and a bare IP | run, Docker and podman | `learnings/harbor.md` |
| It matches a name exactly as spelled | run | same |
| It is by host, never by port | run | same |
| A fixture's compose service is in the agent's network namespace; loopback never meets the allowlist | run | same, "Conformance at $0" |
| All three vendor agents take a model at `http://localhost:<port>` | run | conformance, on the stub |
| Rootless Docker closes the host's loopback; rootless podman reaches it only with a pasta option; either way it is all of loopback | run | same, and 2026-11-25 |
| A host's LAN address can fall inside Docker's bridge subnet | run, golem | same |
| A container with no network reaches one host service through a mounted unix socket and an in-container relay, and nothing else | run, golem, plain `docker run`, as root and as uid 1000 | this session, not through Harbor |
| On Modal the allowlist holds without compose; with compose Harbor refuses | run | `archive/modal-runs.md`, spikes 2 and 3 |
| An allowlist entry may be a domain, a wildcard, an IP, a CIDR or a range; there is no port | read | `bin/network-policy` |
| The sidecar is gost's transparent handler, sniffing `Host` and SNI, `sniffing.fallback: true` | read | `gost.yaml` |
| Under a policy the sidecar lets through ICMP and DNS to the resolvers in `resolv.conf`, and rejects every other non-TCP packet | read | `bin/network-policy` |
| Harbor's Modal environment replaces the image's entrypoint with `sleep infinity`; an environment argument `keepalive` (a command, or none to keep the image's) changes that | read | `environments/modal.py` |
| The bare path's runner containers have the engine's default network, so the open internet | read | `exams/exam-authoring/hooks/attempt-setup.sh` |

## The questions

Each has a method, a cost, and what its answer decides. Everything is
$0 unless it says otherwise: the stub model or no model at all.

### A. What the boundary is today

One probe task, the 2026-10-15 `netprobe` grown up: Harbor's `oracle`
agent runs a script in the agent phase and writes what answered. Run on
rootless Docker, rootful Docker and rootless podman.

- **A1. Can the allowlist be talked past?** HTTP to an address that is
  not allowed with `Host:` set to the allowed name; TLS likewise with
  the SNI. The answer turns on whether gost dials the name it sniffed
  or the address the packet was going to. *Decides:* whether "allowlist"
  can be called a boundary in the docs at all, or only a habit. If it
  can be talked past, it is an issue for Harbor and a sentence for us.
- **A2. What of the allowed host is open?** HTTP on a second port;
  something that is neither HTTP nor TLS (ssh's banner) on a third,
  with the host allowed by name and then by IP, since `fallback` may
  treat the two differently. *Decides:* what "the gateway must be alone
  on its host" has to mean, and whether an IP entry is looser than a
  name.
- **A3. What leaves that is not TCP?** A lookup of an arbitrary public
  name; of a name only the LAN's resolver knows; a ping; IPv6. *Decides:*
  what the docs admit to. Not a path an agent solves a task by, but the
  page should not say "nothing" if a name resolves.
- **A4. Is there a window outside the policy?** Read 2026-11-25
  (`trial/trial.py`, `trial/network_policy.py`): yes, two. Our
  `task.toml` sets the environment `public` and the agent `allowlist`;
  Harbor applies the agent's policy only around the agent's own phase
  and puts the environment's back after it. So (1) the agent's setup,
  the ACP bridge's and the simulated user's run under `public`, which
  is harmless while no model has been asked anything; and (2) **after
  the agent's phase the container is `public` again**, and between the
  stages of a staged try too. A process the agent left running has the
  internet from then on, and on a staged try the next stage finds what
  it fetched. To do: a probe that leaves a background fetch running
  past its phase, to see it; then whether compile can set the
  environment itself to the allowlist (the 2026-10-15 spike ran "every
  phase allowlisted" with the agent pinned in the image, which is how
  we compile now). *Decides:* a change to `HarborCompile.TaskToml`,
  likely. Also read there: a trial takes `extra_allowed_hosts` at run
  time, merged into the task's, which is how a route could be allowed
  without recompiling.
- **A5. What else is beside it?** From inside a trial: the compose
  network's gateway, a second trial running at the same time, the
  engine's socket. On rootful Docker the gateway is the host, with
  everything bound to `0.0.0.0`. *Decides:* whether rootful Docker
  needs its own warning.

### B. A model gateway on the same machine

The candidate is the socket relay: the host end a unix socket, its
directory mounted into the task, a relay in the agent's namespace
serving it on `localhost`, the agent phase `no-network`.

- **B1. Does it go through Harbor?** Hand-edit one compiled conformance
  task: add the relay as a compose service with the bind mount, point
  the agent at it, and put the stub *on the host's loopback* in place of
  the gateway. All three vendor agents, tasks `ok` and `hang`.
  *Decides:* build it, or fall to B5.
- **B2. Does it hold on each engine?** Rootful Docker (who owns the
  socket), rootless podman (`keep-id`, an SELinux label on the mount),
  an agent that is not root in its container. *Decides:* whether it is
  one mechanism or three.
- **B3. Does it carry real traffic?** A streamed answer of several
  minutes, eight attempts at once on one host socket, a connection the
  agent holds open across turns, the gateway restarted mid-run.
  *Decides:* what the relay is. Candidates: socat from a pinned image,
  a few lines of Python in an image we already pull, a static binary in
  nb's layer. Whatever it is comes under the seven-day cooldown like
  everything else Harbor resolves.
- **B4. What does proctor own?** Who starts the host end and where the
  socket lives (the log entry's directory is the obvious place); what a
  crashed run leaves; what `WhyNotContinue` must compare; how the route
  is written in `proctor.json` and recorded, which is also the open todo
  about a changed agent route going unnoticed (`todo.md`). A design
  question, answered on paper once B1 to B3 are in.
- **B5. The fallback, only if B1 fails:** one private address on the
  host that the gateway also listens on, allowed by IP, with a firewall
  rule for the one port (A2 says whether the rule is needed). Needs
  root twice, so it is the user's hands on a machine of theirs.
- **B6. Is the relay also right for a gateway on another machine?** It
  would turn "that host" into "that host and port" and remove the
  bare-name lookup todo. Against it: one more moving part where the
  allowlist already works. *Decides* after A2: if a second port on the
  allowed host answers, yes.

### C. The model endpoint is itself a way out

- **C1. Which tools run at the vendor?** For each vendor agent at its
  pin: every tool it offers (conformance's stub already records the
  list), which run on the vendor's side through the model's own
  endpoint (web search), which fetch from the container and so die at
  the allowlist, and the setting that turns each off. From the vendors'
  documentation, then one stub run per agent with the setting on to see
  the tool leave the list. *Decides:* what compile sets by default, and
  what `Ingest.Kinds` should name as reaching the web.
- **C2. Off by default** (decided, above). What is left to design:
  where an exam that wants one on says so (the variant, since it
  changes what is under test, and so its hash), that the report then
  says the agent had the web, and a check that counts such calls
  either way, so "off" is seen to hold and not assumed.
- **C3. What can the agent's key do?** The agent holds it. At a vendor,
  it is the whole account's API; at a gateway, every route the gateway
  serves. Advice only (a key for the one route, with a cap), unless
  `setup --check` can cheaply say "this key lists N models". *Decides:*
  one paragraph, perhaps one line of output.

### D. Modal

A few cents of sandbox time in all; spend to be agreed before D1.

- **D1. The same probe as A,** on Modal, where the allowlist is Modal's
  own and not gost. A1 to A3 only.
- **D2. A fixture's service inside the agent's sandbox.** Can a job
  file pass `keepalive`, does a service started by the image's
  entrypoint answer on `localhost`, and does the same hold in the
  separate verifier. Then: which user the agent runs as, and whether it
  can read the service's files or stop it. On the stub fixture.
  *Decides:* whether proctor compiles services into the image on a
  remote engine, and the rule for which fixtures may (the stub: yes;
  `tern-shop`'s docs service, whose content is the thing under test:
  probably not).
- **D3. A second sandbox,** from Modal's documentation only: is there a
  private path between two sandboxes. Read to rule it out properly, not
  to build it.

### E. The bare path

- **E1.** Decision 1 of `agent-neutral-runs.md` (Harbor exactly when the
  program has tools) would make this moot: a bare variant has no tools
  and nothing to reach with. Until it is built, the shipped runner
  hooks give a tool-using nb the internet. Either the hooks take
  `--network none` and B's relay, or the docs say plainly that a runner
  is not containment. *Decides* after B1; costs nothing to find out
  beyond it.

### F. Where a new user falls

The questions above are ours. These find the ones we have not thought
of.

- **F1. Clean-machine walks.** A fresh VM per engine (rootful Docker,
  rootless Docker, rootless podman), following only the docs, to a first
  agent attempt with the model in each of three places: a vendor's API,
  another machine, the same machine. Nine walks; every stumble written
  down with the message the user saw. The stub stands in for the model
  in all nine. *Decides:* the order of the guides and the preflight
  list in F2.
- **F2. What `run` can refuse before anything is made.** Each is a
  candidate until F1 or A to D shows the foot gun is real:
  - a model host that is loopback or private on a local engine, said
    with where to go (today: a timeout);
  - the host's address inside the engine's bridge subnet;
  - a model host with no dot, for a musl agent (the open todo);
  - **an engine whose host loopback is open to containers** (rootless
    Docker's flag, pasta's `--map-host-loopback`): a warning on every
    run, since it is the one setting that undoes everything else;
  - a route that changed under compiled tasks (the open todo).
- **F3. A self-test a user can run.** The A probe as something shipped
  (`setup --probe`, or a tiny exam beside `playground`): one trial, no
  model, prints what an agent on this machine can reach. *Decides*
  after A: worth it if the probe runs in a minute or two and its output
  is something a person can act on. It would also be how we keep the
  claims in the guides true across Harbor pins, as conformance does for
  ingest.

## Order

1. **Read** (half a day, no machine): A4, C1's documentation half, D3,
   and gost's handler for A1, so the probe is written knowing what to
   expect.
2. **The probe** (A, then F3's question): golem for rootless Docker
   now; podman from 2026-11-26. Rootful Docker has no machine and golem
   is not to be reconfigured for it. Proposed, mine: its leg runs in a
   VM on golem, the first of F1's nine, so it costs no machine and
   comes last.
3. **The relay through Harbor** (B1 to B3), same machines. B4 on paper
   after.
4. **Modal** (D1, D2), once spend is agreed.
5. **The walks** (F1), last, against the docs as redrafted from 1 to 4,
   so they test the advice and not today's gaps.

Stages 2 and 3 do not wait on each other. A1 failing changes the
wording everywhere but stops nothing. B1 failing sends stage 3 to B5,
which waits on the user.

## What it ends in

Not decided here; this is the shape the answers fill.

| Where the model is | Advice | Tooling |
|---|---|---|
| A vendor's API | the default; what C says about tools and keys | C1's defaults in compile |
| Another machine | what A2 and A5 require of that host | F2's refusals; perhaps the relay (B6) |
| The same machine | never loopback opened, never a LAN bind | the relay, or B5 by hand |
| Modal | public endpoints only; which fixtures may run | D2's compile, or the refusal stays |
| Bare, with a runner | E1's sentence | perhaps the relay |

And the files: one page on the boundary that every engine guide points
at (what an agent can reach, why, how to check), `docs/docker.md`
written, `docs/podman.md` §5 replaced, `docs/modal.md` brought up to
stages and axes, `Setup/SandboxPlan.cs` and the `proctor-exam` skill
pointing at them. Each guide dated with the versions it was checked on.

## Rules for the work

- Findings go into `project/learnings/harbor.md` as they are made, with
  run or read said; a bug found is a GitHub issue; a defect in Harbor
  is an issue there, written up for the user to file.
- No hosted model. Modal's sandbox time is the only spend, and is agreed.
- Host loopback is opened to containers on no machine for this work.
  Golem has it open already, for an unrelated test; a probe there that
  shows loopback reachable is showing that, and says so.
- Nothing that needs root is done by the agent's account. B5 and any
  firewall rule are written out for the user.

## Not in this plan

Defending against an agent that means to get out; what an exam does
when its question *is* an agent with the web; network policy for the
judge and the reader, which run on the host; the viewer.

## The fix for A4 (built 2026-11-25)

`HarborCompile.TaskToml` gives `[environment]` the agent's policy, a
bridged task excepted: Harbor installs acpx from npm in the container
on every trial, so with the environment closed the oracle gate failed
in a minute, and passed without the change. A task with an answer
sheet therefore still has the window after its agent's phase, until
the bridge is carried in the image as Qwen Code's agent is.

Verified on golem (rootless Docker), $0: `GateTests` and
`OracleGateTests` twice, the second time on the head it was committed
to, and both `ConformanceTests` (48 minutes). A first run the same day
failed all four, three of them on image builds that could not reach
`mcr.microsoft.com` over a hotel's network, the fourth on the bridge.
Not run on podman or Modal.
