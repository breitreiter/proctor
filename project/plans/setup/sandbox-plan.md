<!--
Mockup of suites/sandbox-plan.md, which setup writes beside setup.json when
the user picks "On this machine" at step 2. Gitignored with it: it describes
this machine.

Design: this is a brief for a strong model, not a checklist. Machines differ
too much (OS, distro, Docker Desktop licensing, rootless podman, WSL, a
corporate proxy, a work laptop where installing anything needs a ticket) for
a static file to know the steps. Setup contributes what it can observe and
what proctor requires; the agent contributes judgment, in conversation with
the user, at the time it is used. Everything below the comment is the file.
-->

# Setting up a sandbox for proctor on this machine

You are helping the user make this machine able to run proctor's agent arms
(Claude Code, Codex, Qwen Code, nb with tools). Those run through Harbor, in
containers, so the agent under test cannot touch the user's files, keys or
settings. `proctor setup` wrote this file on 2026-11-08 from what it could
see; it does not know this user's constraints. Find them out before acting.

## What the user said

- Sandbox: on this machine (step 2 of `proctor setup`).
- Wants, eventually: Claude Code arms. Not Codex or Qwen Code.

## What setup observed

Facts at the time of writing, not a diagnosis. Re-check anything you rely on.

| | |
|---|---|
| OS | Ubuntu 26.04, x86_64 (Linux 7.0.0) |
| uv | 0.9.4 |
| docker | not found |
| podman | 5.6.1, rootless; `podman.socket` not running |
| `podman compose` | not found (no compose provider) |
| `/etc/containers/registries.conf` | no `unqualified-search-registries` |
| `~/.config/containers/containers.conf` | not present |

## What proctor needs

The requirement, not the route. Any route that ends here is fine.

- A container engine Harbor drives: Docker with the Compose v2 and buildx
  plugins, or rootless podman with its API socket running and docker-compose
  v2 behind `podman compose`.
- For podman: an unqualified-search registry (the egress sidecar is
  `FROM gogost/gost`) and `compose_warning_logs = false` (Harbor reads the
  compose banner as agent output).
- uv on PATH; it runs the pinned Harbor.
- `harbor.engine` in `suites/proctor.json` naming the engine (`docker` is the
  default; setup wrote `podman` here, from what it found).

Why each of these exists, and the known traps (Ubuntu's pasta AppArmor
profile among them), are in `docs/podman.md` for podman and
`docs/configuration.md`, "Agent arms", for both.

## How to proceed

- Ask before you change anything. Learn what the user may do on this
  machine: whether it is a managed work laptop, whether installing packages
  or system services needs approval, whether Docker Desktop's licence is an
  issue for their employer, whether there is a proxy or a private registry
  mirror. The right route depends on the answers, and a route that needs a
  ticket the user cannot get is not a route.
- Prefer what is already installed. Here podman is present, so finishing
  podman is likely cheaper than adding Docker, unless the user says otherwise.
- Propose the steps as a short plan and let the user approve it. Run nothing
  that needs root without saying so first. Do not put any API key on a
  command line or in a file other than the user's proctor env file.
- If this machine cannot be made to work within the user's constraints, say
  so plainly. Suites with no agent (the built-in harness) need no sandbox, and
  a hosted sandbox is planned; `proctor setup` records either choice.

## Done means

`proctor setup --check` reports `job sandbox: ready`. The full proof, which
runs every vendor agent against a scripted stub model at no cost, is:

    PROCTOR_CONFORMANCE=1 dotnet test --no-build --filter ConformanceTests

It takes about half an hour; offer it, do not run it unasked. Until then,
`--check` reports `job sandbox: planned (suites/sandbox-plan.md)`.
