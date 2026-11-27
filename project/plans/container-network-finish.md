---
type: plan
title: What an agent under test can reach — finishing it
created: 2026-11-26
status: under way since 2026-11-26. The user set its goals, gave it the laptop's session and said to push as it goes; the design of the relay and the stages are the agent's and have not been gone through line by line. Rewritten the same day around the user's four goals, after a survey of Harbor since our pin and of other tools. It finishes `container-network.md`. Nothing here is built. What the survey says of Harbor is read from its docs, changelog, issues and pull requests, none of it run
---

# Finishing "what an agent under test can reach"

## The goals (the user, 2026-11-26)

1. Manage the agent's network access selectively, so it reaches the
   services that cannot sensibly be simulated.
2. Make it feasible, ideally easy, to run network-service fixtures
   mildly isolated, so the agent cannot cheat by reading their source
   or configuration.
3. Exams and fixtures somewhat portable: built and tried on a macOS
   laptop, moved to a Linux server.
4. Standardise on Harbor, to inherit its fixes.

By building, by documenting, or by adopting something that exists.

## What the survey changed

We are pinned at Harbor 0.24.0. The latest is 0.27.0 (2026-11-21), and
most of what the first draft of this plan would have built is in it,
merged after it, or open as a pull request:

| Ours, planned or built | Harbor |
|---|---|
| Refusing a fixture with services on Modal | 0.26.0 runs compose tasks on Modal's VM runtime with the allowlist and per-phase policy. The refusal is out of date |
| Vendor-side tools off by default (C2) | on main, release not identified: a policy other than public turns off supported agents' web tools, `disable_web_search` overrides |
| The relay that holds the key | merged 2026-11-25 for Modal only, unreleased: sidecars with their own allowed hosts and secrets, a default proxy, a dummy key for the agent. For Docker and podman a pull request is open (3538), nothing merged |
| A port in the allowlist | nobody has proposed it. Ours to ask for |
| What our probe found leaking (DNS and ping under a policy) | issue 4167, fix open (4376) |
| Fixture services in the agent's namespace | issue 3992; the open fix (4427) gives each service its own namespace on Docker and leaves podman shared |

Also open there: connections that bypass the hostname check (3503),
and on macOS the allowlist depends on the engine VM's kernel (2527,
2593).

So the first move is to **adopt**: move the pin. One thing is still
ours to build, the relay, below.

## Each goal, by means

| Goal | Adopt | Build | Document |
|---|---|---|---|
| 1 | the newer pin: web tools off, the verifier's own hosts | a port on this machine at the agent's `localhost` (below); a fixture or exam names further hosts the agent may reach (today compile allows the model's host only), in the task's hash | what an allowlist is and is not: by host, every web port, names resolve |
| 2 | compose services as Harbor has them | nothing until measured | how to write a service fixture, and what the agent can and cannot see of it |
| 3 | compose on Modal | the Modal refusal narrowed to what Harbor still refuses; a warning, never a refusal, for a private model host and for a bridge-subnet collision, saying why it may time out and where to go | a guide per engine, each dated; macOS from Harbor's issues and the engines' documentation, and marked not run (there is no Mac) |
| 4 | everything above | as little as possible | issues for Harbor, written for the user to file |

## A port on this machine (the user, 2026-11-26)

Two things, one case. A model gateway or a local model on the machine
that runs proctor is ordinary: two of the three machines that run
proctor today have one. And early in an exam's life its author may
start a working repository's dev server (vite, say) for the agent to
reach, which is poor isolation, not reproducible, will happen, and is
not to be forbidden or blocked. The user wants one strategy for both,
so there are fewer special cases.

Proposed, mine: **a port on this machine appears at the agent's
`localhost`, and only that port.** It is the socket relay of
`container-network.md`, leg B, as a byte pipe and nothing more (no key
handling; that stays parked):

- `run` holds the host end: for each port, a listener on a unix socket
  in a short directory, forwarding to `127.0.0.1:<port>`.
- Compile adds one relay service to the task, the directory mounted,
  serving each socket at `localhost:<port>`. A service in the task is
  Harbor's own mechanism; the host end is the only part beside it.
- **The model needs no new configuration**: a base URL (or an nb
  provider's endpoint) whose host is loopback means this machine's, and
  is relayed. A fixture names any other port it wants (the dev server),
  and the report says the try reached a service that is not the
  exam's.
- The host's loopback stays shut, nothing is bound to the network, and
  with no other host allowed the agent's phase is `no-network`.
- Not on Modal, which has no such machine: the existing refusal of a
  loopback host there stands.

Measured for it, on rootless Docker at 0.24.0 only: all three vendor
agents through it, no network in any phase, eight streams at once.
**The risk is Harbor's own direction**: the open fix for issue 3992
gives each service its own namespace on Docker, and the relay would
then answer at a service name, not `localhost`, under whatever policy
that connection meets. So it is measured again on the new pin before it
is built, and against that pull request's branch if it has not merged.

## Stages

All $0 (stub, Mock, no model) but a few cents of Modal, agreed
2026-11-25. About six days.

### 1. Move the pin (a day, mostly waiting)

To 0.26.0 now; to 0.27.0 from 2026-11-28, when it clears the seven-day
cooldown. Then the unit tests, both gates and conformance on podman
(sanjuro), and the probe again on the new pin. Expect breakage: the
web-tools default is marked breaking, and ingest reads Harbor's files
strictly. **Stop and talk** if the move costs more than a day of
fixes.

### 2. Measure what the goals rest on (a day, beside stage 1)

| What | Where | Who |
|---|---|---|
| What a root agent sees of a fixture service: its files, its processes, its traffic on the shared loopback. Docker grants the capability to capture packets by default and podman does not, so this may differ by engine | sanjuro, the laptop | agent |
| Conformance's stub fixture, which has a service, on Modal | Modal | agent |
| The probe on rootful Docker | sanjuro's system Docker | the user, three commands |
| Web tools gone from each vendor agent's list under a policy | sanjuro, the stub | agent |
| The hand-made relay on the new pin: on podman (the mount, an agent that is not root), nb as the agent, the host end restarted mid-trial, two ports at once; and against the branch of Harbor's pull request 4427 | sanjuro, the laptop | agent |

### 3. Build (two to three days)

The relay first: the host end, compile, the ports in the log entry,
`WhyNotContinue`, what a crashed run leaves. Then the three in the
table above: further allowed hosts declared, the Modal refusal
narrowed, the two warnings (a loopback model host is no longer one of
them: it is relayed). Whatever stage 2 shows of a
service's exposure is fixed in compile only if it is a line of compose;
otherwise it is a sentence in the guide and an issue for Harbor.

### 4. Write it down (a day)

The boundary page; the service-fixture guide; `docs/docker.md`;
`docs/podman.md` and `docs/modal.md` finished; `Setup/SandboxPlan.cs`
and the `proctor-exam` skill pointing at them; the issues for Harbor
drafted (a port in the allowlist, and anything stage 2 finds); the
todos closed; both plans archived.

## Parked

| What | Why | Until |
|---|---|---|
| The key out of the agent's reach | a gap in theory; no model has been seen to build its own harness on its credentials (the user, 2026-11-26). Harbor's sidecar proxy does it on Modal once released | that release, then adopt it there; the todo stays |
| The relay on a macOS engine | a host socket is not known to mount through any of their VMs, and there is no Mac | someone has one |
| The bare path's runners | not Harbor | decision 1 of `agent-neutral-runs.md` |
| An agent told to get out | for after the core and the viewer work (the user, 2026-11-26) | `todo.md`, "An agent told to get out" |
| The nine clean-machine walks | | one walk on the laptop, made by the agent over ssh from the guides alone, after stage 4 |

## The user's attention

As first written this plan would have stopped for the user eight or
nine times. It is arranged for two sittings and nothing between.

**Sitting one is done (2026-11-26), without ssh.** The laptop (golem,
rootless Docker 29.1.3) has a Claude Code session of its own, reached
from sanjuro's over Remote Control: sanjuro's session sends it tasks
and it answers with what it ran. It runs docker, uv, dotnet and git
unattended, and anything that would change that machine waits for the
user there. Code travels by the branch on GitHub. Golem's host loopback
was shut by the user the same day, so its earlier probe results are the
record of a machine with it open, and the probe is run there again.

Still optional, and the user's: one script on sanjuro's rootful Docker,
about six minutes, proved on podman first. Skipped, the Docker guide
says rootful was not run.

**Standing answers, so that no stage asks:**

- Commits go on `exam-variant-attempt` as each piece passes its tests,
  the agent's own files only (another session commits here too),
  and are **pushed to that branch as they are made** (the user,
  2026-11-26), since that is how they reach golem.
- Names in files and flags are the agent's choice, written in "Decided
  alone" below. Nothing is released before sitting two, so each is a
  rename.
- Modal's cents are agreed. No hosted model, no judge.
- Where this plan said stop and talk, it now says what happens instead:
  - the pin move costs more than a day of fixes: stay on the highest
    pin that passes everything, go on, and list what broke;
  - the relay fails on podman: build it for Docker, refuse it on podman
    with the reason, go on;
  - the relay does not answer at `localhost` on the branch of Harbor's
    pull request 4427: build it for what is released, and write down
    what it would need there;
  - a measurement contradicts this plan's design: the smaller build,
    and the finding.

**Never without the user, all held for sitting two:** a push to any other branch; an issue
or a comment on GitHub, ours or Harbor's; anything that needs root; a
paid model; the host's loopback opened anywhere.

**Sitting two, about half an hour, when the agent says it is done.**
One document to read, `project/plans/container-network-finish.md` with
its two sections filled in ("Decided alone", "Not done, and why"),
then: the names, kept or changed; the issues drafted for Harbor, to
file or not; whether the owed half of the paid `tern-docs`
run goes through the relay.

**What can still halt the work:** the session ending. It does not
carry itself across days, and 0.27.0 cannot be resolved before
2026-11-28. One word starts it again, and this file says where it was.

## Decided alone

- **`NET_RAW` is dropped from the agent's container whenever the
  fixture has services, on both engines, with no switch** (2026-11-26).
  The cost is `ping` for the agent on Docker. An agent overhearing a
  fixture's service was judged the worse of the two. A compose file
  that declares `main` itself is left alone, which is the way out for
  an exam that needs the capability.
- **`tern-docs` stays on Harbor 0.24.0** while every other shipped exam
  moves to 0.26.0 (2026-11-26): its paid run is half made, and a bank
  made under two Harbor versions is a report with mixed conditions.
  Moving it is one line, for when the owed half is run or after it.
- **The pin goes to 0.26.0 first, not straight to 0.27.0**, which the
  seven-day cooldown does not resolve until 2026-11-28.
- **Compile will name the namespace a fixture's service joins**, by
  engine (Harbor's sidecar on Docker and podman, `main` on Modal), in
  place of leaning on what Harbor does with a service that declares
  nothing (2026-11-26). It ties compile to the sidecar's name in the
  pinned Harbor, which fails loudly if Harbor renames it. Not built
  yet.
- **A fixture says which ports are whose** (2026-11-27):
  `image.serves`, the ports its own services answer on, and
  `image.host_ports`, ports of the machine that runs proctor. A
  loopback model on a served port is the fixture's (the stub's 8765);
  on any other it is the machine's and relayed. The names are mine.
- **`run` refuses when a relayed port does not answer** on the machine,
  before anything is made (2026-11-27): a model that is not running is
  otherwise found by every attempt timing out. It is a refusal of a run
  that could not work, not of the arrangement.
- **The relay's host end in C#, its container end a short Python
  script** on the image the stub already uses, no socat on either side
  (2026-11-26, said to the user, not objected to).

## Progress

- 2026-11-26. Stage 1 at 0.26.0: the probe, both gates and conformance
  pass on rootless podman (sanjuro) and rootless Docker (golem) with no
  change to proctor. Stage 2: what an agent sees of a fixture's service,
  measured on both; the one leak (loopback capture on Docker) closed in
  compile. All of it is in `learnings/harbor.md`, the last three
  sections. The hand-made relay passes on 0.26.0 on both engines, podman for
  the first time, with no network in any phase. Next: 0.27.0 from
  2026-11-28; the relay against Harbor's pull request 4427, with nb as
  the agent and with its host end restarted; web tools under a policy;
  the stub fixture on Modal; then the relay's build.
- 2026-11-26, later. Three more of stage 2, in `learnings/harbor.md`,
  the last three sections. **Web tools under a policy are off at
  0.26.0** for Claude Code and Codex, by Harbor: nothing to build.
  **The stub fixture's service runs on Modal** (reward 1), once it says
  it joins `main` and the environment's phase is public. **Harbor's
  pull request 4427 was read**: on Docker it would move every fixture's
  service off the agent's `localhost`, the stub and `tern-shop`'s as
  much as the relay. A service that names outright whose namespace it
  joins stays at `localhost` on both local engines at 0.26.0 and on
  Modal, so compile will write that line for every fixture service,
  chosen by engine. Next: the relay's remaining legs (nb as the agent,
  the host end restarted, two ports), then its build; 0.27.0 from
  2026-11-28.

- 2026-11-27. **The relay is built** (`Run/Relay.cs`, `Harbor/relay.py`,
  compile, `run`, the log entry's `harbor.relay`), with unit tests, and
  documented in `docs/podman.md` section 5 and `docs/configuration.md`.
  Run on rootless podman at 0.26.0 in a scratch root, the stub on the
  host's loopback: Claude Code, Codex, Qwen Code and nb each completed
  `ok` through it with `no-network` in the agent's phase; the three
  vendor agents' four checks pass. Not yet: Docker (golem), two ports
  at once (`image.host_ports`), a continued run, `hang`, the sentence
  in the report that a try reached a port not the exam's. Then:
  further allowed hosts, the join line per engine, the Modal refusal,
  the two warnings.
- 2026-11-27, later. The built relay passes on rootless Docker too
  (golem): the refusal, the three vendor agents, and a run interrupted
  and continued. Still not run: `hang`, an agent that is not root,
  long streams side by side.
- 2026-11-27, later still. Built: a fixture's further allowed hosts
  (`image.allowed_hosts`) and the bridge-subnet warning. Unit tests
  only; neither has met an engine. Left of stage 3: the join line per
  engine, the Modal refusal narrowed.
- 2026-11-27, last. The probe on Modal: the refusal of service
  fixtures there stays, for a reason that is now true (below). The
  join line per engine has no use until that is lifted or pull request
  4427 lands, so it waits for the 28th. **Stage 3 is done but for
  that.** Next: stage 4, the guides; then 0.27.0 and the pull
  request's trials on the 28th.

- 2026-11-27, stage 4. Written: `docs/network.md` (the boundary page),
  `docs/service-fixtures.md`, `docs/docker.md`; `docs/podman.md` and
  `docs/modal.md` brought to 0.26.0; the sandbox plan, the skill, the
  README and `docs/configuration.md` point at them; four drafts for
  Harbor in `project/harbor-issue-drafts.md` (one waits for the 28th);
  the two todos rewritten to what is left. Found on the way: a
  fixture's services are in no hash (`todo.md`). Left of stage 4: the
  walk through the guides on golem, and archiving both plans, which
  waits for 0.27.0 and the pull request's trials since this file says
  where they stand.

- 2026-11-27, after. Golem's session walked the three guides against
  its machine, read-only, and its findings are in them (how rootless
  Docker is got, the daemon file that is the user's there, that an
  interrupted trial leaves its images too, when ping is gone). `hang`
  went through the relay on podman as scripted. Still not run: an
  agent that is not root, long streams side by side.

- 2026-11-27, the user: no issues in other projects' repositories
  under their name. `project/harbor-issue-drafts.md` is a record of
  findings, not a queue; it is off the list for sitting two, and the
  comment on pull request 4427 will not be written.

- 2026-11-27, the user: proctor is in the middle of invasive changes
  by another session and is not to be counted on as working. **Held
  until the user says it is stable**: everything that runs proctor (the
  two relay runs left, the gates and conformance at 0.27.0, the join
  line in compile). Not held, since it runs Harbor alone through
  `learnings/netprobe/run.sh`: the probe at 0.27.0 and golem's three
  trials against pull request 4427, from 2026-11-28 01:24 UTC.

- 2026-11-27, **the plan is cut to rootless podman** (the user, and
  `plans/remix.md`, "What the core supports"). Goals 1 and 2 stand, on
  podman: the relay, the allowlist, a service fixture the agent cannot
  read. Goal 3 is dropped as the core's work, and goal 4 means Harbor
  on podman. What that removes from what was ahead:
  - **Harbor's pull request 4427 is not run**, and the join line by
    engine is not built. The pull request changes Docker only and
    leaves podman as it is, so nothing supported depends on it.
  - **No more tasks for golem.** Its results stand as dated record.
    The untracked probe files there are still the user's to keep or not.
  - **The Modal refusal is no longer a question for sitting two.**
    Modal leaves the core under `remix.md`, and the refusal goes with
    it. The probe's findings stay in the learnings for whoever writes
    Modal against the seam.
  - **`docs/docker.md` is dated and marked not maintained**;
    `docs/modal.md` moves with Modal's code, in that plan's step, not
    here. Nothing more is written for macOS.
  - The interrupted trial's leftovers (`todo.md`) were seen on Docker;
    worth a look only if podman shows the same.

  What is left, all on podman and all held until proctor is stable:
  the pin to 0.27.0 with the probe, the gates and conformance; the two
  relay runs (an agent that is not root, long streams side by side);
  then this plan and `container-network.md` archived.

## Not done, and why

- **Harbor's pull request 4427 will not be run** (see Progress, the cut
  to podman); what follows is how it stood before. It was only read. The user
  said to run it (2026-11-26); uv's seven-day cooldown would not
  resolve a package its Harbor needs, and the user decided against an
  exemption. Golem's session runs the three trials after 2026-11-28
  01:24 UTC, the day 0.27.0 resolves too. The question it answers is
  whether a service joined to `harbor-docker-egress-control-main-8e62e8c65106`
  answers at the agent's `localhost` there.
- **The guides were not walked on a clean machine**, and nothing in
  them was run on macOS, Docker Desktop or rootful Docker. `docs/docker.md`
  marks each such part.
- **Both plans are not archived yet**: 0.27.0 and the pull request's
  trials are still ahead, and this file is where they are tracked.
- **No warning for a private model host.** The plan listed one. With a
  loopback host relayed there is nothing left to warn of: a model on
  another machine of the network works, and the one way it fails is the
  subnet collision, which has its warning.
- **Further allowed hosts are a fixture's only**, not an exam's. A
  fixture's image is already in each task's hash; an exam-level list
  would have to be put there, and nothing asks for it yet.
- **The Modal refusal is kept, not narrowed**, with its reason
  corrected. Harbor 0.26.0 does run a fixture's services on Modal, and
  the stub passed there. But the image is built inside the sandbox, so
  the environment's phase must be public, and the probe showed a
  process the agent left running reaching the internet the second its
  phase ended. That is the leak compile closes on every other engine.
  **For the user at sitting two**: accept that for exams that want
  Modal (then the refusal becomes a line in the report), or leave it
  refused until Harbor builds before the policy applies.
