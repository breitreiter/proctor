# Agent arms on Modal

`harbor.engine: modal` runs each agent cell's containers as sandboxes on
your [Modal](https://modal.com) account, so the machine running proctor
needs no Docker and no podman: proctor, nb and uv are enough. The suite
does not change. You run it with a second machine file in place of
`suites/proctor.json`:

```bash
cp suites/proctor.modal.example.json suites/proctor.modal.json   # then edit
proctor run code-change --config suites/proctor.modal.json
```

The usual way to use it: try a suite on your own machine (local models,
your container engine), then run it on Modal once it is right, so someone
without your setup can run it too. Arms without an agent (bare nb) still
run on the machine you start proctor on, so a suite can keep a bare arm on
a local model while its agent arms run on Modal.

Checked on 2026-11-10 with Harbor 0.24.0 (Modal SDK 1.5 and later):
a `code-change` task through Harbor's verifier, the allowlist from inside
a sandbox, and the gate (`PROCTOR_GATE_ENGINE=modal`).

## 1. A Modal login

```bash
uvx modal token new
```

This prints a link (or opens your browser), and writes a token to
`~/.modal.toml`. Proctor never reads the token: Harbor's process inherits
proctor's environment, and Modal's SDK finds the file itself. In CI, set
`MODAL_TOKEN_ID` and `MODAL_TOKEN_SECRET` instead. `run` checks for one or
the other before anything starts.

## 2. The machine file

`suites/proctor.modal.example.json` is the starting point. It differs from
`proctor.json` in two ways:

- `harbor.engine` is `modal`.
- Every agent's model is on the internet: the vendors' own APIs, with the
  keys from your environment or `~/.config/proctor/env`. A sandbox on
  Modal reaches only public hosts.

`suites/proctor.*.json` is gitignored, as `proctor.json` is.

## What cannot run on Modal

`run` refuses these before it makes anything, and says which arm or
fixture and why:

- **A model on your network.** A base URL whose host is `localhost`, a
  private or link-local address, a name without a domain (`imp`,
  `library`) or a `.local`, `.lan`, `.internal` or `.home` name. Bare arms
  are not affected, since they run on your machine.
- **A fixture with `image.services`.** Harbor runs a task with services as
  docker-in-docker on Modal, which cannot limit the agent's network to its
  model, so Harbor refuses the task rather than run it unconfined. Run
  these suites on docker or podman. The stub model that conformance uses
  is one, so conformance stays a local check.

An `agent: nb` arm runs on Modal if its provider's endpoint in nb's config
is public. nb's publish (`harbor.nb`) is copied into each nb task's image,
since Modal cannot see an image built on your machine.

## What it costs

Modal bills sandboxes by the second for the CPU and memory they hold. On
2026-11-08 that was about $0.14 an hour per core (a core is two vCPUs)
and $0.024 an hour per GiB, so a ten-minute cell on a small sandbox is a
few cents. The model calls cost far more. New accounts get $1 of credit,
and $30 a month once a payment method is added.

Every sandbox proctor starts ends itself: its lifetime is the cell's wall
limit plus the time Harbor allows for the build and the checks, and it
stops after ten minutes with nothing running. A run you kill leaves
nothing billing for longer than that. Harbor's own default would keep a
sandbox for a day.

## Resume

An experiment keeps the engine it started on. Resume it with the same
file:

```bash
proctor resume <id> --config suites/proctor.modal.json
```
