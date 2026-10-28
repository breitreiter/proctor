---
type: learnings
title: Microsoft's Scope, read against proctor
created: 2026-10-27
---

# Microsoft's Scope, read against proctor

Microsoft Developer Relations open-sourced Scope on 2026-10-19: "an
open-source agentic experience evaluation platform", MIT, a research preview
(repo created 2026-07-28, 66 stars on 2026-10-27). Read from the
announcement, the docs and the repo (`docs/architecture/overview.md`,
`criteria-provider.md`, `post-processing.md`, `config/criteria/`). Nothing
was run.

Four things come out of it for proctor: check dependencies, ATIF as the one
format, cost with provenance, and the oracle as proctor's own once agent
arms go through Harbor. Personas are not one of them.

## What it is

The question it answers is a product team's: *how do coding agents use my
SDK, CLI, MCP server or skill?* That is the same question as the office
bench, not proctor's "which arm is better". The showcase is Cosmos DB: eight
scenarios in five languages, bare models against the team's Agent Kit, 20
kit issues and 27 doc issues found, hierarchical partition-key adoption 47%
→ 100% after one kit change. Those are Microsoft's own numbers, which nobody
else has reproduced.

It is a platform rather than a tool: a TypeScript monorepo with a React
portal, Express API, CLI, scheduler, queue-fed workers scaled by KEDA, a
Judge service, a post-processor and a Rust TLS-intercepting gateway. It runs
on AKS with Cosmos, Redis, Blob and Key Vault, or on Docker Compose locally.
It says of itself that it is "not a secure sandbox" (the local worker mounts
the Docker socket).

| Scope | proctor | note |
|---|---|---|
| worker (`coder-acp-claude-code`, `coder-acp-copilot`, VS Code + Copilot) | `arm.agent` | over ACP; no Codex, no open or local models |
| profile (worker, model, agent version, skills, MCP servers) | arm | versioned and reusable, like a pinned arm |
| scenario (prompt, starting codebase) | task on a fixture | |
| criterion `{id, prompt, depends_on, gates}` | check | every criterion is a sentence the LLM judge decides |
| `depends_on` | none | see below |
| gate (`Select → Build → Test → Run → Deploy`) | none | phases with their own prompt and criteria, stop on failure, one workspace |
| persona (traits, verbosity) | none | see below |
| HAR → ATIF v1.7 via `atifact` | Harbor's ATIF, `NbAtif` | same format and the same version |
| "8 of 10", cost per success | Wilson, Newcombe, MDE, baseline verdicts | Scope has no interval anywhere in the repo |

The judge reads three kinds of evidence: the workspace snapshot, the tool
outputs across the whole run (`list_tool_calls`, `search_tool_outputs`), and
the agent's answer. A build or test check is a criterion the judge decides
by looking. Their docs advise keeping judge results apart from deterministic
ones and having a human review borderline verdicts; the code does not
enforce either.

## Take: check dependencies

A criterion names its prerequisites, and the set must stay acyclic; Scope
enforces that on write. Proctor today reports a broken build several times:
`builds` fails, then `tests_pass` fails, then every judged check of the code
fails or spends a judge call on code that does not compile. The Failures
section lists them all as separate failures of the arm. Only one of them is
the arm's failure. The rest are consequences of it.

A shape that fits proctor's rules:

- A check spec gains `after: [names]`, resolved against the cell's merged
  set (`Suite.ChecksFor`). It names a check that must reach the same cells.
  A cycle, an unknown name, or a prerequisite that does not reach every cell
  the dependent reaches is a `Problem`.
- When a prerequisite fails, errors or is undecided, the dependent is **not
  reached**: a fourth verdict that is never evaluated, never sent to a
  judge, and never run in the verifier (`test.sh` can skip it in check
  order).
- The headline does not change: when a reached `pass` check fails, the run
  fails. Being unreached does not fail a run by itself. Only the
  prerequisite's own outcome fails it, as it already does.
- A check's rate is over the cells that reached it, with the unreached count
  printed beside it, the way undecided runs are now. Failures lists the root
  and says "and N checks after it not reached".

Two questions are open. The first is what a `validity` check after a `pass`
check should mean (probably a problem: validity decides whether the sample
counts, so it cannot wait on the outcome). The second is whether the
conditional rate ("tests pass, given it builds") is the number readers want,
or whether they want the unconditional one beside it. Scope's gates (a phase
per prompt) are a separate idea, closer to the oracle than to dependencies.

## Take: ATIF is the standard; be on it

When nb's JSONL was written there was no common trajectory format. ATIF is
that format now. Harbor writes it for every agent. Scope converts its HAR
captures into it rather than invent one, and third-party tooling exists
(`atifact`). Proctor is halfway there:

- `Transcript.Read` has two readers: `Parse` for nb's JSONL on the bare path
  and `FromAtif` for agent cells. Two readers means windows that can drift.
- `NbAtif` already converts nb's JSONL to ATIF v1.7 for nb arms under
  Harbor, and `NbAtifTests` holds every captured Mock transcript to reading
  the same both ways.

The work to absorb:

1. **One reader.** The bare path writes `trajectory.json` from
   `transcript.jsonl` at the end of the cell, through `NbAtif`, and `grade`
   reads ATIF only. `Parse` survives as the converter's input, not as a
   window source. The proof that this is safe is `NbAtifTests`, which
   already exists.
2. **Step metrics.** ATIF carries usage per agent step; nb reports usage
   only in its trailer. nb's per-turn `usage` event
   (`nb/plans/agent-neutral-runs.md` §2) lets the conversion fill each step.
3. **nb writes ATIF itself** (its open question). nb's JSONL is also its
   program format (`--seed` reads what `--output jsonl` writes), so it does
   not go away. ATIF is the output format, and the program stays nb's. Once
   nb emits ATIF, `NbAtif` moves into nb or is deleted.
4. **Pin the version as for agents.** v1.7 is recorded; a newer
   `schema_version` is an ingest failure until it has been read, as an
   unknown tool is now.

Reading a Scope trajectory is not a goal. Sharing the format makes that
possible later.

## Take: cost, with provenance

It makes better blog content than a token count, and a reader asks for it
first. It is also the number easiest to get wrong. What exists:
`core.json` already records `CostUsd` from Harbor (`agent_result.cost_usd`
or ATIF `final_metrics.total_cost_usd`), which is filled where the agent
knows a price (cc) and null where it does not (Qwen Code). The report says
"Cost is not shown: nb does not report it", `max_cost` is "not yet", and
pricing nb's providers is filed in nb (`todo.md`).

The caution, as rules:

- **Every figure says where it came from**: reported by the agent, computed
  from a price table of a named date, or the gateway's charge. If none of
  these exists, there is no figure. Proctor never estimates silently, which
  is the line `estimated` tokens already draw.
- **A local arm is "local", not $0.** Qwen on the Strix boxes costs nothing
  per token and is not free. Printing $0 beside a hosted arm misleads the
  reader in the direction a blog post would like.
- **Cost per passed run** (Scope's metric) is total cost over decided passes,
  computed in `Stats.cs` like every other number, with median and p90 per
  run beside it. An arm with no passes has no figure.
- **Subscription is not API.** A cc arm run on a subscription has a reported
  price nobody paid. The method section says which.
- Prices move. A report's cost is as of the experiment, and `--against` an
  older experiment compares tokens, never re-prices.

## Leave: personas

A persona is a system-prompt fragment with a name. Where we want one, it is
seeding: a `system` directive in the program for an nb arm, the prompt's own
wording for a vendor agent (which never sees the program template), and the
oracle sheet's answers for how a user replies. None of this needs a new
concept. The rest of Scope that we leave is the platform itself (cluster,
database, portal), the TLS gateway (Harbor gives ATIF without one), and
judging deterministic outcomes by asking a model.

## The consequence: the oracle moves up

What Scope's personas and gates point at is a run that does not end at the
first stop: the agent asks, someone answers, the work goes on. In nb that is
the oracle: `oracle @sheet.md` is an answer sheet serviced in-process when
the model halts on a question. The more of proctor goes through Harbor, the
less of it can use nb's oracle. A vendor agent never runs nb, and an nb arm
under Harbor has its sheet on the wrong side of the container wall.

Where things stand (`plans/agent-neutral-runs.md` decision 9,
`learnings/harbor.md` staging 1):

- Harbor runs a second agent as the user over ACP (`--user-agent`,
  `--bridge acp`). A custom `BaseAgent` is accepted, gets the rendered task,
  and talks to the target with `acpx prompt`, blocking per turn. That part
  works.
- Only cc, Codex, opencode and gemini-cli can be a target. Qwen Code cannot,
  and neither can nb until it speaks ACP.
- In bridge mode the record is `agent/bridge-trajectory.json`: raw ACP
  JSON-RPC, not ATIF, with no token usage. Ingest would need an ACP-to-ATIF
  reader, which runs into the ATIF take above.
- The `oracle_hit`, `oracle_misses` and `oracle_turns` checks are "not yet".

Whether to lift the oracle into proctor turns on four questions:

1. **Whose is the sheet?** Proctor's shape says the task's, beside `expect`
   and outside `repo/`, so it is unreachable from the container by
   construction. The format is nb's `AnswerSheet` today.
2. **Who resolves it?** Either `nb --judge --sheet` from the user agent (nb
   as a host tool, as `--compile` is), or proctor's own judge client under
   the judge rules: endpoint from `proctor.json`, verified quotes, verdict
   file as record. The second makes the oracle a judge, with the same
   auditing.
3. **What does a turn look like in ATIF?** `user` steps marked
   `extra.proctor.source: oracle` with the keys hit, written by ingest from
   the bridge record, so `oracle_turns` reads ATIF like every other window.
4. **What about agents that cannot be a target?** Refuse the arm as
   validation does now ("not yet"), rather than run it single-turn and call
   the result comparable.

That is a plan, not a learning. Before building, write a short
`plans/oracle-in-proctor.md` that settles 1 and 2. The bare nb path keeps
nb's in-process oracle either way.

### What ACP in nb would cost (napkin, 2026-10-27)

Now planned in `../nb/plans/atif-and-acp.md`. Harbor 0.24's bridge takes any
agent implementing `ACPAgentMixin`, so risk 1 below is settled in source; a $0
spike still confirms it. Sizes there supersede these.

A tool-using nb arm under Harbor can take the outside oracle only if nb is a
bridge target, which means speaking ACP. Sized from nb's code, not built:
about **3–4 days** for nb as a bridge target, **5–6** with live streaming,
roughly 800–1,200 lines with tests. A one-day Harbor spike comes first and
may make the rest moot.

ACP is JSON-RPC 2.0 over stdio, one message per line. An agent needs
`initialize`, `session/new`, `session/prompt` and `session/cancel`, plus
`session/update` notifications. Asking the client for permission and using
its filesystem or terminal are optional, and nb needs neither: its approval
is a declarative allow-list that never asks, and its tools run in the
container. Loading a saved session is optional too. No .NET SDK was looked
for; by hand on System.Text.Json the protocol is small.

| piece | how it fits nb | size |
|---|---|---|
| stdio JSON-RPC loop, message records, `nb --acp` | new file; the `--output jsonl` seam already sends everything else to stderr | ~300 lines, 1 day |
| session mapping | `session/new` calls `NbRuntime.BuildAsync`; the program file sets up the session (provider, model, approval, budget, `system`) with no `run`, and prompts arrive over ACP. `session/prompt` calls `AppendHistory` then `ConversationManager.RunAsync`, with `session/cancel` on its token. `exit_reason` maps to `stopReason` | ~150 lines, 0.5–1 day |
| updates, cheap | after each turn, send the turn's new history as one set of updates; enough for the bridge, since `acpx prompt` blocks until the turn ends | ~100 lines, 0.5 day |
| updates, live | an event sink through `ConversationManager`'s streaming loop and tool dispatch in place of `MarkdownRenderer`; a 1,340-line file that writes to the console directly | +1–2 days |
| transcript beside the protocol | nb still writes its JSONL to a file, so ingest reads `nb.jsonl` through `NbAtif` as now, not the bridge's ACP record | ~0.5 day |
| tests | Mock provider over in-memory pipes: two prompts in one session, cancel, stop reasons | ~1 day |

Mapping the `mcpServers` a client passes in `session/new` onto `McpManager`
is extra and can wait, since the bridge does not need it.

Risks, largest first:

1. **Harbor may not take nb as a target.** Staging 1 found only cc, Codex,
   opencode and gemini-cli, and nobody has checked whether that list is
   hard-coded in Harbor or whether `acpx` takes a custom agent command.
   Settle it first, with a 20-line Python stub that speaks ACP standing in
   for nb.
2. **Stray stdout.** A `Console.Write` outside the redirect seam corrupts
   the protocol, and Spectre is used throughout. A test that checks every
   stdout line parses as JSON-RPC catches it.
3. **Several prompts per session is new to nb**, which has run one program
   per process since the REPL was retired. Budgets (`oracle_turns`, wall
   time, tokens) would have to apply across a session's prompts, not reset
   on each one.

It would buy the outside oracle on nb arms, the gate that the outside and
in-process oracle loops agree (it needs the Mock provider, so nb), and nb in
ACP clients such as Zed. Aside for the cost take:
`ConversationManager.SetPriceLookup` already exists, so part of the pricing
may already be in nb.

## Where proctor stands

The obvious incumbent reports raw fractions, judges everything with a model,
supports two vendors, and needs a cluster. Proctor's case, stated against
it: intervals on every rate and difference, deterministic checks where a
deterministic answer exists, judge evidence that is verified rather than
trusted, any agent including local ones, and $0 to check the plumbing. That
case gets stronger with Scope out, not weaker: a large vendor shipping a
platform for this question says the question matters.

Sources: [announcement](https://opensource.microsoft.com/blog/2026/10/19/introducing-scope-open-source-evaluation-for-ai-coding-agents/),
[repo](https://github.com/microsoft/scope), [docs](https://microsoft.github.io/scope/),
[AiCybr's setup guide](https://aicybr.com/blog/microsoft-scope-ai-coding-agent-evaluation-self-hosted-guide).
