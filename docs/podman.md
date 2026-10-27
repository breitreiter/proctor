# Agent arms on rootless podman

What a machine needs before `harbor.engine: podman` works, and why each
step is there. Checked on 2026-10-27 with rootless podman 5.7.0 on Ubuntu,
docker-compose 2.40.3 and Harbor 0.24.0: conformance (every vendor agent
against the stub) and the oracle pass over `code-change` both run green.
Earlier findings came from podman 6.1.3. Docker needs none of this.

`run` and `resume` check the compose frontend, the compose banner and the
search registry before an experiment starts, and say which step is
missing. The other steps cannot be checked from outside a trial.

## 1. Packages, once, as root

```bash
sudo apt install podman passt uidmap docker-compose-v2
sudo loginctl enable-linger "$USER"
```

- `passt` provides pasta, rootless podman's network. `uidmap` provides the
  helpers that map a container's users onto the account's subordinate
  range. `/etc/subuid` and `/etc/subgid` must give the account a range;
  most distributions add one when the account is created
  (`grep "^$USER:" /etc/subuid` to see).
- Linger keeps the user's systemd running without a login session, so
  the API socket below survives logging out.
- **docker-compose v2 is the only compose frontend that works with Harbor
  0.24.0.** `podman-compose` has no `compose ls`, which Harbor's preflight
  runs, and refuses `exec --no-TTY`, which the allowlist phase runs. With
  both installed, set `PODMAN_COMPOSE_PROVIDER` to docker-compose's path
  (`/usr/libexec/docker/cli-plugins/docker-compose` on Ubuntu); proctor
  passes its environment through to Harbor.
- [uv](https://docs.astral.sh/uv/) runs the pinned Harbor. It needs no root.

## 2. The API socket

```bash
systemctl --user enable --now podman.socket
```

docker-compose talks to podman through this socket, not the CLI. Without
it, Harbor's preflight fails on `podman compose ls`.

## 3. Short image names

```toml
# ~/.config/containers/registries.conf
unqualified-search-registries = ["docker.io"]
```

Harbor's egress sidecar is built `FROM gogost/gost:…`, a short name.
Docker assumes docker.io; podman refuses unless a search registry is set,
and Ubuntu's `/etc/containers/registries.conf` sets none. Every trial with
an `allowlist` or `no-network` phase builds the sidecar, and the separate
verifier is `no-network`, so without this line every agent cell fails
before its trial starts.

## 4. The compose banner

```toml
# ~/.config/containers/containers.conf
[engine]
compose_warning_logs = false
```

`podman compose` prints `>>>> Executing external compose provider … <<<<`
on stderr before every call. Harbor reads `compose exec` output with stderr
included, so the banner becomes part of every version an agent's setup
asks the image for: Harbor's Codex agent decides its pin is missing and
fails reinstalling it, and proctor's Qwen Code agent refuses the image.
Claude Code and the oracle happen not to notice, which is why a check with
only those passes.

## 5. A gateway on the host's loopback (only if yours is one)

A rootless container reaches the host as `host.containers.internal`, which
is the host's routable address, not `127.0.0.1`. For a model gateway that
listens only on loopback:

```toml
# ~/.config/containers/containers.conf
[network]
pasta_options = ["--map-host-loopback", "169.254.1.2"]
```

and use `host.containers.internal` as the host in `proctor.json`'s
`agents.<agent>.base_url`. **The allowlist is by host, not port**: once
mapped, every service on the host's loopback is open to every trial, not
just the gateway. Prefer a gateway on a LAN address the allowlist can name
on its own.

## 6. Leftover sidecars (issue #7)

Every environment with a sidecar, the trial's and the verifier's, ends
with Harbor's `compose down` failing on it (`rootless netns: kill network
process: permission denied`) and leaves an exited sidecar container and
the project's network behind: two of each per cell. The cell is
unaffected. Until #7 is resolved, clear them after an experiment:

```bash
podman ps -aq --filter name=egress-control-sidecar | xargs -r podman rm -f
podman network ls --format '{{.Name}}' | grep -E '__env_default$|__trial_default$' | xargs -r podman network rm
```

So far this has been seen only with Harbor in the sandbox below; whether
it happens without one is #7's open question.

## 7. Checking a machine: conformance

```bash
PROCTOR_CONFORMANCE=<proctor.json> dotnet test --no-build --filter ConformanceTests
```

`PROCTOR_CONFORMANCE=1` takes `harbor.uv` and `harbor.engine` from the
repository's `suites/proctor.json`; a path takes them from that file
instead, so a machine can be checked on podman without changing its
config. A file holding only `{"harbor": {"engine": "podman"}}` is enough.
About twenty minutes (seventeen for the vendor agents, three and a half
for the oracle pass), no model, $0.

## 8. Harbor without your credentials in reach (optional)

Harbor pins its own version but not its dependencies', and one of them
(litellm 1.107.0, 2026-10-21) shipped a credential stealer that ran at
every Python startup. Proctor resolves Harbor's dependencies under a
seven-day cooldown, which keeps out a poisoned release pulled within the
week. If the account that runs agent arms also holds keys worth stealing
(`~/.ssh`, a GitHub token, an AI client's credentials), `harbor.uv` can
point at a wrapper that runs uv in a bubblewrap sandbox with the home
directory hidden:

```bash
#!/bin/bash
# harbor.uv: uv with $HOME, the runtime directory's other sockets, /srv and
# the rest of /tmp hidden, and the environment cleared but for what proctor
# hands Harbor. Bind in your repository's runs/ (or wherever experiments
# live) if it is under a hidden path.
set -euo pipefail
H=$HOME R=$XDG_RUNTIME_DIR UV=$(command -v uv)
mkdir -p "$H/.cache/uv" "$H/.local/share/uv"
pass=()
for v in HARBOR_TELEMETRY NO_COLOR ANTHROPIC_API_KEY OPENAI_API_KEY PYTHONPATH PODMAN_COMPOSE_PROVIDER; do
  [ -n "${!v:-}" ] && pass+=(--setenv "$v" "${!v}")
done
exec bwrap \
  --ro-bind / / --dev /dev --proc /proc \
  --tmpfs "$H" \
  --ro-bind "$UV" "$UV" \
  --bind "$H/.cache/uv" "$H/.cache/uv" \
  --bind "$H/.local/share/uv" "$H/.local/share/uv" \
  --bind "$H/.local/share/containers" "$H/.local/share/containers" \
  --ro-bind "$H/.config/containers" "$H/.config/containers" \
  --tmpfs "$R" \
  --bind "$R/containers" "$R/containers" \
  --bind "$R/libpod" "$R/libpod" \
  --bind "$R/podman" "$R/podman" \
  --tmpfs /tmp --bind-try /tmp/proctor-tests /tmp/proctor-tests \
  --chdir "$PWD" \
  --clearenv \
  --setenv HOME "$H" --setenv USER "$USER" --setenv XDG_RUNTIME_DIR "$R" \
  --setenv CONTAINER_HOST "unix://$R/podman/podman.sock" \
  --setenv PATH "$(dirname "$UV"):/usr/local/bin:/usr/bin:/bin" \
  "${pass[@]}" \
  --die-with-parent \
  "$UV" "$@"
```

- Inside bubblewrap's user namespace the podman CLI cannot join podman's
  own; `CONTAINER_HOST` makes it a client of the API socket instead, which
  is how Harbor's compose calls reach podman anyway.
- Harbor's working directory (the cell, under `runs/`) must be visible
  and writable inside; add a `--bind` for it if it lies under `$HOME`,
  `/tmp` or another hidden path.
- An `agent: nb` arm also passes its provider endpoint's `${VAR}`s by value;
  add them to the list.
- This stops a dependency that reads credential files. It does not stop
  one that drives the podman socket, which can start a container mounting
  anything the account can read.
