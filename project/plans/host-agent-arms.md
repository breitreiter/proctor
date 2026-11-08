---
type: plan
title: Host agent arms — the user's own Claude Code or Codex, on their login, no container
created: 2026-11-08
status: dropped 2026-11-08; kept as the record of why
---

> **Dropped.** A host CLI on the user's login meets the leakage and guardrail
> problems that took months to fight on Harbor, as soon as a task is complex
> enough to want Claude Code or Codex; the same reason nb dropped costumes.
> Onboarding's second step is nb with an API key on a task that is not a
> coding task. Users who do not want the local stack, and CI, are a question
> for cloud-hosted runs.

# Host agent arms

## Why

Getting started needs a second step that runs a real coding agent, and the
one every reader already has is their own Claude Code or Codex, logged in.
Today a real agent means Harbor: Docker or podman, uv, an API key routed
through `proctor.json`, a fixture image. That is right for a measurement and
wrong for a first hour. nb bare is no longer a good general-purpose coding
agent, so it is not the step-2 answer either.

A host agent arm runs the vendor CLI the user has installed, on the host, in
the cell's work directory, on whatever login that CLI already holds. It is
the bare path's shape (proctor's checkout, hooks, diff, host-side script
checks) with the vendor CLI where nb was, and the vendor's own sandbox where
the container was.

It is a convenience path, not the measurement path. The report must say so
(below), and the guide must say when to move to Harbor: a pinned version, a
clean configuration, an isolated network.

## What the vendors offer (checked 2026-11-08)

Claude Code 2.1.319 was checked against its `--help` on the dev box; Codex is
not installed there, so its column is from OpenAI's docs
(`learn.chatgpt.com/codex/{non-interactive-mode,sandboxing,config-reference}`,
which `developers.openai.com/codex/*` now redirects to). Verify Codex's flags
against a real install before coding against them.

| | Claude Code | Codex |
|---|---|---|
| headless | `claude -p <prompt> --output-format stream-json --verbose` | `codex exec --json <prompt>` (JSONL: `thread.started`, `turn.*`, `item.*`, `error`) |
| the user's login | used by `-p`; **not** by `--bare` (API key or `apiKeyHelper` only) | "reuses saved CLI authentication by default"; `CODEX_API_KEY` overrides |
| writes | working directory, `$TMPDIR`, `--add-dir`; protected paths (`.claude`, `.git/hooks`, …) denied even inside | `--sandbox workspace-write`: the workspace (plus `sandbox_workspace_write.writable_roots`); exec's default is read-only |
| reads | **the whole machine by default** | **the whole machine by default** |
| confining reads | file tools: `permissions.blockReadsOutsideWorkingDirectories: true` (holds in every mode, `bypassPermissions` included). Bash: `sandbox.enabled`, `sandbox.filesystem.denyRead: ["~/"]`, `allowRead: ["."]`. Two layers, both needed: the sandbox covers shell commands only, the permission rule covers the built-in tools only | a permission profile: `permissions.<name>.filesystem.<path-or-glob> = "deny"`, `default_permissions = "<name>"` (not combined with `sandbox_mode`), `:workspace_roots` for relative rules |
| fail closed | `sandbox.failIfUnavailable: true` and `sandbox.allowUnsandboxedCommands: false`; without them a machine missing bwrap runs commands **unsandboxed, silently** | unverified; find out what exec does when bwrap is missing |
| per-run config | `--settings '<json>'`, `--setting-sources`, `--strict-mcp-config`, `--add-dir`, `--permission-mode`, `--model` | `-c key=value` overrides, `--profile`; `--skip-git-repo-check`; `--ephemeral` (no rollout file, so do not pass it: the rollout is the trajectory) |
| mechanism | Seatbelt (macOS), bubblewrap + socat (Linux, WSL2); **none on native Windows** | Seatbelt (macOS), bubblewrap (Linux, WSL2) |
| network | sandboxed commands go through a local proxy, `sandbox.network.allowedDomains` starts empty | `sandbox_workspace_write.network_access`, off by default (verify) |

## Shape

**The arm.** An agent arm with `"host": true`:

```json
{ "id": "my-claude", "agent": "claude-code", "host": true, "model": "claude-sonnet-5-5" }
```

`agent` is `claude-code` or `codex` (Qwen Code later, if asked for; nb is
refused, since bare nb already is the host path). `model` is optional: the
CLI's own default when absent, recorded either way. `agent_version` is
optional; when given, `run` refuses an installed version that differs. The
nb fields and `runner` are refused as on Harbor arms. Validation refuses a
host arm on native Windows (no sandbox) and an oracle task beside one (as for
vendor arms on Harbor). Decide in review whether `host` is a boolean or a
`where: "host" | "harbor"`; the boolean is the smaller change.

**The run.** `Runner.RunCell` takes the bare path through checkout, setup
hook, teardown hook and diff, and replaces `RunNb` with `RunHostAgent`:

1. Resolve the binary (`claude` / `codex` on `PATH`; a `proctor.json`
   `agents.<agent>.path` override) and its version, once per experiment,
   into `experiment.json` and every cell manifest, as `versions.nb` is.
2. Write the per-cell settings (below) into the cell directory, never into
   the work directory, where the agent would see and could edit them.
3. Start the CLI in the work directory with the task's prompt as its only
   instruction (no program template, as on Harbor), stdin closed, the
   cell's wall limit as the timeout. The environment is the user's, minus
   every `PROCTOR_*` variable, so the expectations stay out of reach as on
   the bare nb path.
4. Keep stdout (`agent.jsonl`) and stderr, then convert to ATIF
   (`trajectory.json`) and count `core.json` (below).

**Where the work directory lives.** Not under the repository's `.proctor/`:
a confined agent should have nothing interesting nearby even if a rule is
missed. Put host cells' work directories under the user's temp directory
(`$TMPDIR/proctor/<experiment>/<cell>`), recorded in the manifest. Check
what this breaks: `PROCTOR_WORK` is "never deleted"; regrade's restore; the
docs that say `.proctor/`.

## Confinement

Generated per cell by proctor, not left to the user's settings.

**Claude Code**, passed with `--settings`:

```json
{
  "permissions": {
    "blockReadsOutsideWorkingDirectories": true,
    "defaultMode": "bypassPermissions"
  },
  "sandbox": {
    "enabled": true,
    "failIfUnavailable": true,
    "allowUnsandboxedCommands": false,
    "filesystem": { "denyRead": ["~/", "<proctor repo root>"], "allowRead": ["<work dir>"] },
    "network": { "allowedDomains": ["<the task's package registries, if any>"] }
  }
}
```

Open points: whether `bypassPermissions` or `dontAsk` with an allow list is
the right headless mode (the docs warn bypass is for containers; with reads
blocked, writes confined and Bash sandboxed it may be acceptable, and it is
what Harbor runs); whether `denyRead: ["~/"]` breaks the CLI's own reads of
its credentials and config (the sandbox applies to commands Claude runs, not
to the CLI process, but test it); and whether npm and the like need network
for the fixture's build (jotcli does not once `node_modules` is in the
checkout, but `fixture.json` excludes `node_modules` and `dist`, and its
`builds` check runs `npm ci` on the host when they are missing; an agent that
wants to build or test needs `registry.npmjs.org` in `allowedDomains`, or a
setup hook that runs `npm ci` into the checkout first, which is the better
default: the agent then needs no network beyond its model).

**Codex**, by `-c` overrides or a generated `--profile`: a permission profile
extending `:workspace` with `~` denied and the work directory as the only
workspace root. Unverified end to end; the first task of the Codex half is
to prove a read of `~/.ssh` and of the proctor repo fails from inside
`codex exec`.

**A confinement check is part of the feature.** A conformance-style test
(opt-in, like `PROCTOR_CONFORMANCE`, since it needs the CLI and a login)
that runs each host agent on a task whose prompt asks it to read a file
outside the work directory and to write one outside it, and asserts both
failed. A machine where the sandbox is unavailable must fail the cell, not
run it open.

## The user's configuration bleeds in

On the user's login the CLI also loads their global instructions, skills,
MCP servers, hooks and memory. That is the honest "your agent as you use it",
and it is also unreproducible and can contaminate (a global CLAUDE.md that
mentions proctor, an MCP server that reaches the network).

- Claude Code: `--setting-sources project --strict-mcp-config` skips user
  settings and MCP servers. Unknown: whether the user's `~/.claude/CLAUDE.md`
  and skills still load under it. Find out. `--bare` is clean but refuses
  OAuth, so it is the clean mode only for a user with an API key.
- Codex: find the equivalent (`--profile` with a minimal config, or
  `CODEX_HOME` pointed at a generated directory holding only `auth.json`,
  which would also keep its rollouts out of the user's history).

Default to the cleanest mode the login allows, and record which in the
manifest. Offer `"config": "user"` on the arm for someone who wants their
real setup measured.

## Counting

Proctor counts from ATIF, strictly (`Ingest.Count`, `Ingest.Kinds`), and
never takes an agent's numbers. Harbor already converts both CLIs' logs to
ATIF inside its agents (Python). Options:

1. Port the conversion to C# for the two formats (`stream-json` events, or
   Claude Code's session JSONL under `~/.claude/projects/`; Codex's rollout
   file or its `--json` stream). Most work, no Python on the host path.
2. Call Harbor's converters on the host through uv. Keeps one definition of
   the mapping, but pulls Harbor into the path this feature exists to avoid.
3. Count directly from the native stream into `core.json` and write a
   minimal ATIF for grading's windows.

Recommendation: start with 1 for Claude Code's `stream-json` (it is the
majority of customers) and hold every tool name to `Ingest.Kinds` as now, so
an unknown tool fails the cell rather than going uncounted. Capture real
streams from each CLI into `Proctor.Tests/fixtures/` first and build the
converter against them, as `fixtures/nb-run/` was built.

## The report

A host arm's description says, in the arms table and the reproducibility
section: host, the installed version, the login kind (subscription or key,
without the identity), and the config mode (clean or user). The method
paragraph says host runs are not isolated from the network the way Harbor
runs are. No new verdict logic.

## Before the guide sends strangers here

Check Anthropic's and OpenAI's terms for driving a subscription login from
an automated tool. A user running their own CLI on their own machine looks
fine, but the guide should not tell anyone to do something the terms forbid;
if subscriptions are out, the step says "with an API key".

## Work, in order

1. Prove confinement by hand for Claude Code: the settings above, a prompt
   that tries to read `~/.ssh` and `../../suites`, a missing-bwrap machine.
2. Capture `stream-json` from a few real runs into test fixtures; write the
   converter to ATIF and `core.json` against them.
3. Arm schema and validation; `RunHostAgent`; the temp work directory;
   manifest fields; report wording.
4. `cli-for-agents` gains a `claude-code` host arm beside (or instead of)
   `local`; `reference/shakedown.sh` is unchanged, since the checks are.
5. The opt-in confinement test.
6. Codex: steps 1, 2 and 4 again.
7. Docs: `docs/configuration.md` "Host agent arms"; getting started's step 2;
   CLAUDE.md's conventions entry.

Spend: the host arm runs on the user's own subscription or key, so the
shakedown is a handful of real runs on the developer's login. Agree it first,
as for any hosted run.

## Not in scope

Qwen Code on the host; Windows native; pinning or installing the CLI
(that is Harbor's job); an oracle for host arms.
