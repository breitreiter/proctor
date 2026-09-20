---
type: learnings
title: Coupling to nb — what it would cost to leave
created: 2026-09-20
---

# Coupling to nb — what it would cost to leave

A survey, not a plan. Nothing here proposes a change; the point is to have a
number for a risk we have so far only gestured at.

## The worry

`claude -p` and `codex exec` are converging on what nb does: a scriptable,
headless unit of agent work with a machine-readable event stream. nb's own
plan calls nb "a turbo-powered `claude -p`" (`nb/plans/composable-cli-reorientation.md`),
which is an accurate description of the niche and also of the collision course.
If the vendors ship close analogues, nb stops being an asset to proctor and
becomes two liabilities:

- **Chasing.** Every harness nb emulates is a surface nb has to track as the
  vendor changes it, forever, with no vendor cooperation.
- **Cost.** We are not a frontier lab and cannot subsidise model usage inside
  nb. A user who already pays for a Claude or Codex subscription pays again, in
  API tokens, to run the same work through nb. That is a direct, visible,
  per-run cost for using our tool instead of theirs.

So: if we had to make the harness optional, or turn nb into an orchestrator of
the vendor CLIs, how much of proctor would we be rewriting?

## Measured: 1,924 lines of proctor, ~110 of them nb-shaped

Counted by reading every site, 2026-09-20.

| tier | where | lines | what breaks if nb goes |
|---|---|---|---|
| process invocation | `Runner.RunNb` (19), `ResolveNb` (13), `FindOnPath` (4), `Versions` (8), the `ResolvedNb` record | ~47 of 308 | argv shape `--output jsonl [--config X] program.nb`, cwd, `NO_COLOR`, stdout→`transcript.jsonl`, version read from `nb.dll` |
| wire format | `Transcript.Parse`'s event switch | 31 of 149 | the event type names and field names, nothing else |
| definition schema | `Eval.ValidateDef`'s `case "nb":`, the `NbConfig` record, the harness enum in an error string | ~12 of 252 | `runner`, `provider`, `harness` validation |
| provenance and copy | `experiment.json`'s `nb` block, `nb_exit_code`, two report sentences about nb's trailer, one provenance row | ~8 | cosmetic |
| **total** | | **~110 (6%)** | |

The other 94% — `Checks.cs`, `Grade.cs`, `Stats.cs`, `Results.cs`, `Report.cs`,
`Layout.cs`, the whole verb surface — never mentions nb. It reads the *windows*
`Transcript` exposes (trailer, answer, answer JSON, tool calls, tool results,
user turns, diff) and knows nothing about where they came from. That is the
part that carries the intellectual content and the gravitas, and it is already
portable.

Two seams were designed in and left stubbed, which is better luck than we
deserved:

- `arm.runner` is already a field with a `"command"` value reserved and
  rejected with "not yet implemented".
- `TranscriptRef.Format` is already a discriminator on the cell manifest,
  written as `"nb-jsonl"`. Nothing reads it yet; a second format would.

## Route A — make the harness modular

Proctor grows a second and third runner; nb becomes one option.

What it actually costs:

1. **A runner seam, kept small.** A switch on `arm.runner` in `RunCell`, plus
   per-runner argv construction. Not a plugin system, not an `IRunner` — three
   in-tree cases, the way nb's own `HarnessRegistry` does it. **~60 lines.**
2. **A parser per format.** `claude -p --output-format stream-json` and
   `codex exec --json` both emit JSONL with a different vocabulary; each lowers
   into the same window records. **~80–150 lines each**, and they are the kind
   of code that is written once against captured fixtures and then stable.
3. **The program file.** This is the expensive one, and it is not in the C#.
   `program.nb` is not a prompt — it is nb's *language*: `provider`, `harness`,
   `approval` rules, `loop`, `budget`, `system`, `run`. Look at
   `evals/code-change/program.nb`: eleven approval rules, a loop bound and two
   budgets, none of which has a `claude -p` equivalent with the same semantics.
   Two options, neither cheap:
   - *Per-runner program files* (`program.nb`, `program.claude`, …) authored by
     the eval writer. Cheap for us, and it pushes the cost onto every eval
     author, forever, and makes two arms textually incomparable.
   - *A neutral program schema* that each runner lowers into its harness's
     form. Expensive, and lossy: nb's approval rungs have no vendor analogue,
     and `--allowedTools` / `--sandbox` have no nb analogue.
4. **Checks that only nb can answer.** `denied_calls` and the approval rungs,
   `loop_nudged`, the reserved `oracle_*` family, `max_tokens` where the vendor
   reports differently. These need a fourth verdict — *unsupported by this
   runner* — which must not fold into `fail` and must not fold into `error`
   either, because a cross-runner comparison would then look like a bug.
   Once that exists, a cross-runner `grading.pass` is only honest over the
   *intersection* of supported checks, and the report has to say so. **This is
   a statistics-and-presentation change, not a plumbing change**, and it is the
   part most likely to embarrass us in front of the audience the report exists
   to convince.

Engineering estimate: **the code is a few days; the eval corpus and the
comparability semantics are the real bill.**

## Route B — nb becomes an orchestrator for the vendor CLIs

nb keeps its program language, its approval model and its transcript schema,
and `harness claude-code` stops meaning "nb wearing Claude Code's costume" and
starts meaning "shell out to `claude -p`".

For proctor this is **nearly free**: the argv, the JSONL and the windows are
unchanged, and ~110 lines stay exactly as they are. Every cost lands in nb.

nb has already thought about the mechanism — `nb/plans/harness-emulation.md`
tables `claude -p --output-format stream-json`, `codex exec` and
`cursor-agent -p` as *controls* to validate costumes against. Turning a control
into an execution path is a smaller step than inventing one.

The difficulty is that nb's model does not survive the trip intact. nb's
approval rungs, its `loop` nudge, its budgets and its tool surface are enforced
*inside* nb's own tool loop; delegating the loop to `claude -p` delegates all of
it. What comes back is the vendor's sandbox policy and the vendor's usage
accounting, mapped onto nb's trailer as best it can be. Several of proctor's
checks would then be answering about a different machine than they claim.

## The finding that matters more than the line count

**Today an arm that says `harness: claude-code` is not measuring Claude Code.**
nb's harnesses are costumes: nb's own tools wearing another agent's tool names,
under "an nb-authored facsimile written from vendor documentation and observed
behaviour" (`nb.Core/Harness/ClaudeCodeHarness.cs`). That is a defensible thing
for nb to do and nb's own docs are scrupulous about saying so.

It is proctor that publishes the number. Our audience is ~60% Claude Code and
~20% Codex users, and a report that puts `claude-code` in an arm label is
making a claim about the tool they use every day. The moment `claude -p` is a
credible direct control, the costume is no longer the best available proxy — it
is a worse measurement that we chose. **The exposure is a validity problem in
the report, not a porting problem in the runner**, and it arrives before any
of the engineering above becomes urgent.

Second-order, and cheaper to fix: **proctor's entire test suite depends on nb's
Mock provider.** Nine captured fixtures under `Proctor.Tests/fixtures/` are nb
JSONL, and `RunnerTests` spawns a real nb per cell. If nb went away, proctor
would need a deterministic fake harness before it could be tested at all. That
is a dependency on nb's *existence*, not on its design, and no amount of runner
modularity removes it.

## Cheap insurance, if we ever want it

Deliberately nothing that changes the shape of the code now:

- Keep `TranscriptRef.Format` written and start *reading* it in the grader, so
  a second format is a data question rather than an assumption.
- Keep `Checks.cs` reading only the window records. It already does; the rule
  is to notice if a check ever reaches for `experiment.Nb`.
- When the unsupported-check verdict is needed, add it as a verdict, not as an
  error — `Verdict` already has the shape for a fourth constant.
- Record a real `claude -p` control run against the same fixtures before
  publishing any cross-harness comparison, so we know the size of the costume
  gap before someone else measures it for us.

## The verdict

The coupling is **shallow in code and deep in corpus**. Six percent of proctor
is nb-shaped and the parts that matter are already harness-neutral, largely by
accident of having put a `runner` field and a transcript `format` field in early.
What is not portable is `program.nb` — the language every eval is written in —
and what is not fixable by porting is the claim an arm label makes. Route B is
nearly free for proctor and expensive for nb; route A is a few days of code and
an unbounded argument about what a cross-runner pass rate means.

Neither is urgent. The thing to watch is not the vendor CLIs shipping features;
it is the first time someone asks whether our `claude-code` arm is Claude Code.
