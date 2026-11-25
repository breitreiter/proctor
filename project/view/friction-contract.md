---
date: 2026-11-14
status: first pass built and measured, rules (0.9, ported to proctor) and a reader stage (Haiku, decided); the file format is the thing to depend on, the detector behind it is replaceable. Implements the file contract in `loop.md` and reads the declaration in `surface.md`
---

# friction.json: the contract, and the first detector behind it

## What to depend on

One `friction.json` per run, beside `checks.json` once proctor writes it.
The viewer, the review workflow and proctor's features read this file and
nothing about how it was made. The detector today is a Python script
with rules only (`trials/friction.py`); a port to proctor, and a model
reader for what rules cannot see, write the same file.

524 real files exist to build against, under `trials/out/`, all made by
detector 0.9: the 15 Sonnet and 9 Qwen `agent-onboarding` runs with a
surface declared, 6 `nb-changes` runs with none, 54 `tern-docs` runs
with a surface and a way round it, and 440 Terminal-Bench 2.0 runs with
none. The `agent-onboarding` Sonnet and `nb-changes` files carry the
Haiku reader's points as well. Four more, from the private `nb-changes`
runs, are under `datasets/proctor-private/derived/` with their labels.

## The file

```json
{
  "schema": "friction/0",
  "run": {"experiment": "20261111-0120-agent-onboarding-dd5u", "arm": "cc-sonnet", "task": "exact-answers", "sample": "4"},
  "agent": "claude-code",
  "atif": "ATIF-v1.7",
  "detector": {"name": "auspex/trials/friction.py", "version": "0.9", "labels": ["..."], "surface": "18b0cb35de10"},
  "summary": {
    "steps": 18, "calls": 18, "points": 7, "steps_in_a_point": 11,
    "by_label": {"off-surface": 3, "help": 1},
    "surface": {
      "reached": true,
      "first_reach": {"step": "4", "reached": "surface"},
      "used": true,
      "worked": true,
      "surface_calls": 5,
      "surface_calls_failed": 0,
      "used_substitute": false,
      "parts": {"on": {"actions": 8, "cost_usd": 0.03}, "about": {}, "around": {}, "substitute": {}, "background": {}}
    }
  },
  "points": [
    {
      "id": "p4",
      "label": "off-surface",
      "steps": ["8", "9"],
      "calls": ["s8.1", "s9.1"],
      "evidence": [{"step": "8", "call": "s8.1", "quote": "../proctor/project/plans/on-disk-layout.md", "where": "command"}],
      "rules": ["internals"],
      "detail": ["/opt/proctor/project/plans/on-disk-layout.md"],
      "cost_usd": 0.048416,
      "source": {"kind": "rule", "detector": "0.9"}
    }
  ]
}
```

- **A point is a stretch of adjacent steps with one label.** `steps` is
  the first and last ATIF step id, inclusive. Two labels on the same
  steps are two points.
- **`calls`** are `s<step>.<n>`, the nth tool call of that step, from 1.
- **`evidence`**: each quote is in that call's command or result with all
  whitespace removed, proctor's judge rule. A point with no quote that
  passes is not written. At most six per point.
- **`rules`** says which rule fired; **`detail`** is what it fired on,
  normalised so that the same thing in two runs is the same string. It
  is what a count across runs groups by.
- **`cost_usd`** is the cost of the point's calls, whole, where the run
  records cost; otherwise null. A call in two points is counted in both.
- **`source.kind`** is `rule` today. A reader's point will carry `model`
  and which model.
- **`summary.surface`** is present only when a surface was declared.
  `worked` is true when at least one surface call did not fail. A
  surface call failed when its result has a 4xx or 5xx status line or
  matches a `fails` pattern in the declaration.
  "Went round" is `reached` and not `used`; whether the run also passed
  is proctor's to join.
- **`detector.surface`** is a hash of the declaration, so a file says
  which declaration made it.

The labels, fixed (renamed 2026-11-22 so each reads bare, written that way since proctor's `friction/2`, 2026-11-23; new names in brackets, reasons in proctor's `check-evidence.md`, C5.1; the measurements here use the old names):

| Label | A step where | Needs a declaration |
|---|---|---|
| `off-surface` (now `read-internals`) | the run read the surface's internals, or hunted for where it is installed | yes |
| `substitute` (now `used-substitute`) | a declared substitute was used (a command, a tool or a host) | yes |
| `abandoned` (now `fell-back`) | a substitute was used within two steps of a surface call that failed | yes |
| `surface-search` (now `searched-docs`) | the run searched the surface's docs (recursive grep, `find -name`) | yes |
| `help` (now `ran-help`) | `--help`, `-h` or `man` | no |
| `workaround` (now `inline-script`) | a script run inline, or a file written to `/tmp` | yes |
| `environment` (now `machine-refused`) | the machine refused: command not found, permission, timeout, network | no |
| `tool-error` (now `harness-refused`) | the agent's own tool refused: output too large, a rejected call, an edit that did not apply | no |

Eight. `rework`, `repeated-failure` and a rule-made `repair` were built
and dropped: on a coding task they fire on the work. `repair` is the
reader's label now (below). The scores in the next section are from
version 0.4, which still had the rule-made `repair`.

## How good the first detector is

Scored step by step against two readers' labels, Haiku labels then
Sonnet labels. "Right" is the share of flagged steps a reader calls
friction; "found" is the share of the reader's friction steps flagged.

| Runs | Declaration | Steps | Reader says friction | Flagged | Right | Found |
|---|---|---|---|---|---|---|
| `agent-onboarding`, Sonnet, 15 | yes | 246 | 126, 132 | 109 | 78%, 83% | 67%, 69% |
| `nb-changes`, Sonnet, 4 (private) | none | 125 | 25, 20 | 16 | 75%, 75% | 48%, 60% |
| `nb-changes`, Haiku, 6 | none | 120 | 12, 12 | 2 | 50%, 50% | 8%, 8% |

For scale, the two readers against each other on `agent-onboarding`:
90 to 94% right, 90 to 94% found.

By label on `agent-onboarding`: `help` 100% right, `workaround` 100%,
`off-surface` 89% and 98%, `surface-search` 94% and 89%, `repair` 78%
and 100% (9 steps), `environment` 70% and 73%, `tool-error` 0% (7
steps: the readers' vocabulary there had no line for it). On the private
`nb-changes` runs `environment` and `tool-error` are right every time
(12 steps) and `repair` one time in five.

Against the plan's bar for the day:

- **Contract frozen, 25 runs of real files.** Met: 474.
- **80% found, three in four right, on the labelled suite.** Half met.
  Right, yes. Found is 67 to 69%. An earlier setting found 76 to 80% and
  was right 71 to 77%; the difference is one rule, whether `type proctor`
  and listing the install folder count as hunting. They were ruled not
  to, which is what the readers said and what the skill tells an agent
  to do.
- **The known scents.** Six of seven: the install hunt in 15 runs,
  internals in 15, tool source in 4, private notes in 4, `file` missing
  in 12, `python3` missing in 4, the shell timeout in 9. Not found: the
  docs' dead end, which all 15 runs read and stepped past. Nothing in a
  step's command or error marks it.
- **A second suite with no code change.** Not met as stated, and this is
  the day's main finding. With no declaration the detector has only
  `help`, `repair`, `environment` and `tool-error`. It finds machine
  trouble well and nothing else. On the Haiku runs the readers' friction
  is mostly searching a wrong area and repairing a wrong change, and no
  rule here sees either.
- **Every point checkable, same output twice.** Met: 1,570-odd points,
  none without a passing quote; two runs give identical files.
- **Does not fall over.** Met: the 440 Terminal-Bench runs in 28
  seconds.

## The reader stage (added 2026-11-14)

`trials/friction_reader.py`. A model reads one run and names the friction
rules cannot see. It writes into the same file: points with
`"source": {"kind": "model", "model": "...", "prompt": "<hash>"}`, an
empty `rules` and `detail`, and a `notes` list holding its one-line
reason, which is prose and is never counted. The file gains a top-level
`reader` object naming the model, the prompt hash and the reader's
labels.

The reader's labels, fixed (renamed 2026-11-22 so each reads bare, written that way since proctor's `friction/2`, 2026-11-23, while the prompt still asks in the old words; new names in brackets, reasons in proctor's `check-evidence.md`, C5.1; the measurements here use the old names):

| Label | A call that |
|---|---|
| `repair` (now `fixed-own-work`) | fixes its own earlier work after something it ran failed |
| `lost` (now `wasted-read`) | searches or reads to nothing of later use |
| `reread` | reads again what an earlier call showed |
| `dead-end` (now `bad-instruction`) | follows an instruction or a path that could not work as given |
| `workaround` | builds a way round something instead of using it |

It is told not to label missing commands, timeouts or tool refusals,
which the rules find. An answer is dropped unless the call exists, the
label is in the list, and the quote is in that call's command or result.

**First run**: Haiku through subagents in the session, on the 25
labelled runs. 60 calls labelled, 57 kept; the three dropped all quoted
something not in the call (two the rendering's `<<HEREDOC` placeholder).

Rules and reader together, scored as before (Haiku labels, Sonnet
labels):

| Runs | Flagged | Right | Found | Found by rules alone |
|---|---|---|---|---|
| `agent-onboarding`, Sonnet, 15 | 126 of 246 | 79%, 86% | 79%, 82% | 67%, 69% |
| `nb-changes`, Sonnet, 4 (private) | 26 of 125 | 81%, 65% | 84%, 85% | 48%, 60% |
| `nb-changes`, Haiku, 6 | 8 of 120 | 88%, 75% | 58%, 50% | 8%, 8% |

- **The reader's own points are right more often than the rules'** on
  `agent-onboarding`: 86% and 100% of 37 steps, against 78% and 83%.
- **It adds what was missing**: on the Haiku `nb-changes` runs, found
  goes from 8% to 50 to 58%, almost all from `lost` and `repair`.
- **Its `repair` beats the rule's.** The reader's is right on 30 of 32
  steps by the Sonnet labels across the three sets; the rule's on 11 of
  16. The rule-made `repair` was dropped in version 0.5. With it gone,
  rules and reader together find the same share and are right more
  often: `agent-onboarding` unchanged (126 flagged, 79% and 86% right);
  private `nb-changes` 22 flagged, 95% and 77% right, 84% and 85% found;
  public `nb-changes` 7 flagged, 100% and 86% right, 58% and 50% found.
- **Two readers disagree most on `workaround`**: on the private runs the
  reader's five are right by one set of labels and one in five by the
  other. Scripted edits through a heredoc are a work-around to one
  reader and the change itself to another.
- **It found a dead end, and not the known one.** In four runs it
  flagged the agent guessing that proctor's suites sit beside its
  launcher in `/usr/local`, which they do not. The docs' "ask the user
  to run `proctor setup`" dead end is still found by nothing.

Caution on these numbers: the stage's reader is Haiku, and one of the
two label sets it is scored against was also made by Haiku, with a
different prompt and vocabulary. The Sonnet column is the more
independent one.

### Local models as the reader (2026-11-14)

The same 25 prompts, unchanged, through the gateway's free routes to the
local box (`friction_reader.py ask --key-env`). Scored as above; "found"
is rules and reader together, Haiku labels then Sonnet labels.

| Reader | Calls labelled | Passed the quote check | Reader steps right, `agent-onboarding` | Found, `agent-onboarding` | Found, `nb-changes` Sonnet agent | Found, `nb-changes` Haiku agent |
|---|---|---|---|---|---|---|
| Rules alone | | | | 67%, 69% | 48%, 60% | 8%, 8% |
| Haiku (hosted) | 60 | 57 | 86%, 100% of 37 | 79%, 82% | 84%, 85% | 58%, 50% |
| Qwen3-Coder-Next | 40 | 21 | 67%, 75% of 12 | 70%, 72% | 48%, 60% | 8%, 8% |
| Qwen3 30B instruct | 25 | 15 | 60%, 70% of 10 | 71%, 73% | 48%, 60% | 8%, 17% |
| Mistral Small 4 | 51 | 29 | 45%, 55% of 22 | 70%, 73% | 56%, 65% | 8%, 8% |

- **No local model tried does the reading.** Each adds one to eight
  points of "found" where Haiku adds 12 to 50, and its points are right
  about as often as a coin on the suites with no declaration.
- **They label less and quote worse.** Four to five in ten answers fail
  the quote check, against one in twenty for Haiku. Of Qwen3-Coder's 19
  failures: 6 used an ellipsis, 4 the heredoc placeholder, 6 quoted
  another call, 3 quoted nothing in the run. The check did its job;
  what survives is still weak, so stricter quoting would not rescue it.
- **Not tried.** The large GLM chat profile: the box refused to load it
  (about 110 GiB needed, 108 usable), and that refusal was left alone.
  Gemma 4 31B: one prompt exceeded its 32,768-token context and the
  other 24 timed out at the gateway. Mistral timed out on two runs. No
  prompt was tuned for a local model, and none read a run in pieces.

So the stage is not free today. **Decided with the user (2026-11-14):
the reader is Haiku**, a hosted model inside proctor, which is where
`loop.md` puts model work. The local-model question is closed unless
free reading becomes a requirement.

### Off-family readers (2026-11-22 and 23)

Why: proctor's judges are meant to be off-family from the agents they
grade, and the reader never was. A Haiku reader comparing Claude Code
with Qwen is the case the rule exists for (the user, 2026-11-22). So:
can a cheap reader from another family stand in for Haiku, and does
Haiku treat its own family's transcripts differently?

Readers were picked with the user from OpenRouter's list: not Anthropic
or OpenAI (both are often under test), not Qwen (an `agent-onboarding`
agent), not DeepSeek (the user's call). Run on the 21 public prompts
(`agent-onboarding` Sonnet 15, `nb-changes` Haiku 6) through minrouter's
OpenRouter route, the same prompts as above, scored the same way (Haiku
labels, then Sonnet labels). The private runs were left out, since a
hosted model would read them.

| Reader | $ per M in/out | `agent-onboarding`: found | right | reader steps right | `nb-changes`: found | right | answers with nothing |
|---|---|---|---|---|---|---|---|
| Rules alone | | 63%, 64% | | | 0%, 0% | | |
| Haiku (in-session subagents) | | 78%, 81% | 78%, 86% | 86%, 100% of 37 | 58%, 50% | 100%, 86% | |
| GLM 5.2 | 0.62 / 2.42 | 78%, 80% | 79%, 85% | 90%, 97% of 29 | 33%, 33% | 80%, 80% | 5 of 21 (4 on `nb-changes`), 1 runaway |
| GLM 5.3 Flash | 0.04 / 0.08 | 82%, 84% | 78%, 84% | 84%, 93% of 43 | 25%, 25% | 60%, 60% | 3 of 21, 2 unparseable (all `nb-changes`) |
| Gemini 2.5 Flash-Lite | 0.10 / 0.40 | 67%, 67% | 76%, 81% | 50%, 50% of 12 | 0%, 0% | 0% of 4 | 2 of 21; 5 quotes failed |

- **On `agent-onboarding` both GLMs match Haiku.** GLM 5.3 Flash, at
  about a twentieth of GLM 5.2's price, finds the most and is right as
  often. Gemini Flash-Lite barely adds to the rules and its points are
  right half the time: out.
- **On `nb-changes` no off-family reader holds up,** and that is where
  the reader is all of it (the rules find nothing). The GLMs mostly
  answer `{}` there, or run out of tokens, on the coding transcripts
  where Haiku found `lost` and `repair` the labels agree with. Six runs
  is few, but the failure is answering nothing, not answering wrong.
- **GLM 5.2 is erratic:** one answer was a 332k-character loop ("I'm
  done. Final. FIN."), and with a 12,000-token cap it ran out while
  reasoning on 2 of 9 Qwen runs. `friction_reader.py ask` now takes
  `--max-tokens` and records an empty reply with its finish reason.

**Does Haiku favour its own family?** Haiku (three in-session subagents)
and GLM 5.2 also read the 9 Qwen `agent-onboarding` runs, which have no
labels, so reader against reader. Reader-flagged steps per run:

| Runs | Haiku | GLM 5.2 | flagged by both |
|---|---|---|---|
| Claude Code on Sonnet, 15 | 2.5 | 1.9 | GLM agrees with 62% of Haiku's, Haiku with 79% of GLM's |
| Qwen, 7 (2 left out: GLM ran out of tokens) | 4.0 | 3.9 | 75% and 78% |

- **No sign of leniency.** On Qwen's runs the two agree in amount and
  in which steps; on Claude's, Haiku flags more than GLM. A family
  effect, if any, makes Haiku stricter on its own, not softer. 15 and 7
  runs support "no evidence Haiku favours Claude", not "unbiased".
- **Both readers see about twice the friction in Qwen's runs.** The
  difference between the agents survives a change of reader.
- **Haiku as separate subagents is not consistent with itself:** one
  labelled reads that returned nothing `lost`, another left them alone.

**Cost:** $1.94 on OpenRouter over both days, most of it GLM 5.2 (its
runaway and its reasoning); the Haiku reading ran in-session.

**Where this leaves the reader:** Haiku stays (the user, 2026-11-23),
its same-family caveat now measured on one exam rather than assumed.
The user's reading: friction reading may be less like adversarial
judging than anticipated (`learnings/off-family-friction-reader.md`). GLM 5.3 Flash is the
off-family candidate on agentic-but-not-coding transcripts; it is not
fit for coding transcripts as prompted. Two things would move this:
labels for the Qwen runs (the comparison above has no ground truth),
and a prompt change for the GLMs' empty answers on long coding runs,
tried before any reader is ruled out on six runs.

## What this says about the design

- **The declared-surface labels are the strong ones**, and they are the
  ones the first kind of friction needs (`regimes.md`): going round,
  hunting, reading internals. They are right about nine times in ten.
- **Machine trouble is the other strong one**, and needs no declaration.
  Across the 440 Terminal-Bench runs it lists the missing tools by run
  count without being told what to look for: `file` in 51 runs,
  `python3` in 46, `xxd` in 32, `python` in 31, `ps` in 27. That is the
  fixture list.
- **Friction inside the work needs a reader.** Lost searches, repairs
  and re-reads are judgements about what a call was for. `repair` by
  rule is the weakest label and fires 744 times on Terminal-Bench, where
  editing and re-running is the task. It should move to the reader or
  go.
- **Substitute, abandoned and went round are tested on constructed runs
  only** (`trials/friction_test.py`, six cases). The rules do what they
  say. Whether agents do it is not shown by any run we have.

## Known limits

- The split of a call into actions is by shell rules and will be wrong
  on unusual quoting. A pipeline is named by its first command.
- `cd` is followed by the script; a run whose steps record no working
  directory starts at `/`.
- Cost is per call, not per action, and a point's cost includes
  everything else its calls did.
- Rules read the first and last 200,000 characters of a result.
- The readers saw results cut to 800 characters; the rules read all of
  it. Some "wrong" flags are things a reader could not see.
- The vocabulary used to label `nb-changes`
  (`trials/friction/vocabulary-generic.md`) was written the same day and
  the readers reported seven ambiguities in it.

## What is safe to build on

- **The file and its fields.** A reader's points and a ported detector
  add to it; nothing built on points, step ranges, labels, quotes and
  the surface summary needs to change.
- **The review of a point**: its steps, its quote, a disposition.
- **Counting `detail` across runs** to rank scents.

What is not settled, and should stay loosely coupled: the label list
(expect `repair` to change and reader labels to be added), and any
number that depends on "share of steps in a point".

## The docs' dead end, as a check (2026-11-14)

Neither rules nor the reader find a run reading "ask the user to run
`proctor setup`" and stepping past it. Once a person names it, it is a
pattern over tool results, and `trials/try_check.py` tries that on the
runs on disk: `--no-result 'REGEX'` passes a run when no result matches.
On the Sonnet `agent-onboarding` runs 0 of 15 pass; on the Qwen runs 6
of 9; on `nb-changes` 6 of 6, as it should.

- Proctor has no check kind that reads tool results. This one would be
  new (`result_matches`, with a `max`).
- A check that every run fails is a finding about the fixture. The
  treatment is the docs line; the check then guards it.
- The detector's part is smaller than hoped: it cannot discover a dead
  end the agent steps past cleanly. A person reading one run does.

## The public MCP set, opened (2026-11-14)

`obaydata/mcp-agent-trajectory-benchmark` (Apache-2.0), copied to
`datasets/public/mcp-agent-trajectory-benchmark/`. It does not have
substitutes. The 38 single-pass runs are short scripted question-and-
answer sessions (11 to 18 steps, 282 calls) in which the agent has the
MCP server's tools and nothing else, so there is nothing to go round
with. Of the 11 multi-session runs, 3 are ATIF and the same shape, and
the rest are other formats. A suite with real substitutes has to be made.

## A suite with a way round: `tern-docs` (2026-11-14)

Proctor has run one since the datasets were copied: `tern-docs`, 36
Sonnet runs and 18 Qwen runs in `/srv/aiwork/proctor/runs/`, read in
place. The surface is the docs site; the way round is probing the
carrier's sandbox API directly. One arm, `no-docs`, serves a 404 for
every page, so it is the control: every run there has to go round.

The declaration is four lines (`trials/surface/tern-docs.json`). It
needed one new field, `hosts`, on the surface and on `substitutes`: an
action or a tool call whose arguments name the host. Detector 0.6.
Outputs in `trials/out/friction-tern/`.

| Arm | Runs | Passed | `abandoned` flagged | More than two API probes | Probes per run, median (max) |
|---|---|---|---|---|---|
| Sonnet, docs as shipped | 12 | 12 | 3 | 0 | 0 (2) |
| Sonnet, docs restructured | 12 | 12 | 3 | 0 | 0 (2) |
| Sonnet, no docs | 12 | 2 | 12 | 10 | 4 (7) |
| Qwen, docs as shipped | 6 | 6 | 2 | 1 | 1 (3) |
| Qwen, docs restructured | 6 | 6 | 4 | 2 | 1 (6) |
| Qwen, no docs | 6 | 1 | 6 | 6 | 43 (110) |

- **The split finds the way round on real runs.** All 18 control runs
  are flagged, and the count of substitute actions separates the control
  from the arms with docs with little overlap.
- **Three runs powered through**: no docs, heavy probing, passed (two
  Sonnet, one Qwen). This is the case the first kind of friction is
  about, on real data for the first time.
- **`abandoned` by adjacency over-fires.** With working docs, 12 of 36
  runs are flagged. The three Sonnet as-shipped ones were read: each
  reads the docs, then makes one call to the API to confirm what the
  docs said. That is a cheap check (`learnings/rasmussen-trouble-
  shooting.md`), and it is the docs working. The label needed a sign that
  the surface call failed; it has one now (below).
- **`used` does not mean it worked.** Every control run "used" the
  surface, with more docs actions than the arms that had docs (median
  8.5 against 2), because it kept asking a site that answered 404. A
  surface call's own failure is in the file as of 0.7 (below).
- **The restructure does not show** in Sonnet's numbers here and shows
  faintly in Qwen's (4 flagged against 2, six runs each). Too few runs to
  say.

So the synthetic suite is not needed for the contract. What it would
still add is a tool, not docs, as the surface.

### A failed surface call (detector 0.7, same day)

The declaration gained `fails`: patterns that mark a surface call's
result as a failure, beside a built-in 4xx or 5xx status line. For
`tern-docs` it is the 404 page's title and a bare `404`. `abandoned` now
needs the surface call to have failed, and `summary.surface` says
`worked`, with counts of surface calls and failed ones.

| Arm | Runs | Surface worked | Surface calls, failed | `abandoned` |
|---|---|---|---|---|
| Sonnet, docs as shipped | 12 | 12 | 24, 0 | 0 |
| Sonnet, docs restructured | 12 | 12 | 24, 0 | 0 |
| Sonnet, no docs | 12 | 1 | 69, 68 | 12 |
| Qwen, docs as shipped | 6 | 6 | 15, 0 | 0 |
| Qwen, docs restructured | 6 | 6 | 19, 0 | 0 |
| Qwen, no docs | 6 | 0 | 28, 28 | 6 |

`abandoned` is now every control run and no other. The one control run
marked "worked" made a single call whose output showed neither pattern.

Cautions. The patterns were written knowing which arm was the control.
A call's result is judged whole, so in a compound call a failure of
something else can be charged to the surface. `agent-onboarding`
declares no `fails`, so `worked` there means only "no error status
line".

## The port to proctor (2026-11-14)

The rules are in proctor, uncommitted in its working tree:
`proctor friction <experiment>` writes `friction.json` beside each
completed cell's trajectory, and `suite.json` takes a `surface` block
(`Friction/Friction.cs`, `Friction/Shell.cs`, `Suite/Suite.cs`; the
user's guide is proctor's `docs/configuration.md`, "The surface under
test"). `suites/tern-docs` declares its surface.

Three changes were made to the Python first, so there was one exact
thing to port (rules 0.9):

- It no longer imports proctor's lab script. `workaround` is restated as
  a pattern on the command (a script run inline, or a file written to
  `/tmp`); scores moved by one step (125 flagged on `agent-onboarding`,
  78% and 86% right, 78% and 81% found).
- It has its own command splitter. The library one dropped a whole
  command that began with a comment line, which hid most of the Qwen
  runs' probing (median probes per no-docs run 53, was 43).
- Non-text results and arguments are rendered as compact JSON.

**Held to identical output.** Both detectors were run over 702 runs (the
524 above, plus every other copied proctor experiment with a
declaration that does not fit it) and compared field by field: 702
identical, costs within a millionth of a dollar. Proctor's own tests
(22 new) cover the constructed cases, the splitter, and the verb end to
end.

What differs in proctor's files: `detector.name` is `proctor friction`,
and `detector.surface` is a hash of proctor's own rendering of the
declaration, so the two tools' hashes do not match each other.

A rule change is made in proctor from here. `trials/friction.py` stays
as the record of how the numbers above were made, and
`trials/regen.sh` rebuilds them.

One limit showed up: the verb walks the cells of the suite as it is
now, so an experiment whose arms have since been renamed (the Qwen
`tern-docs` run) is not read. `grade` has the same limit.

## The reader and a check kind, in proctor (2026-11-14)

Uncommitted in proctor's working tree, on top of the committed port:

- **`proctor friction <experiment> --reader <judge>`** (`Friction/Reader.cs`).
  A chat judge from `proctor.json` reads each run once. Same
  instructions, byte for byte (the hash `626fe82757e6` is pinned in a
  test), same five labels, same three checks on an answer. What was
  sent and answered is kept in the cell and reused; `--rejudge` asks
  again; the command prints tokens used.
- **Replayed at $0.** The 25 stored Haiku answers were put through
  proctor's code: 60 labelled, 57 kept, 44 points, the same as the
  Python merge. The rendered run differs from the Python rendering on
  137 of 11,445 lines, all the spacing of JSON arguments.
- **Not yet run against a model.** The Haiku figures above came from
  subagents in a session, not from an API call with this prompt. The
  first live run is the test of that, and is a spend to agree: the 25
  labelled runs are about 750,000 characters of prompt, roughly 200,000
  tokens in.
- **`result_matches`**, a built-in check: a regular expression over every
  tool result, usually negated. It is what the docs' dead end becomes.

A reader's point is ordered after the rules' points that start on the
same step, and the rules' points keep their order. The Python merge
sorted by label name there, so point ids can differ between the two.

## The live reader run (2026-11-14)

`proctor friction <experiment> --reader haiku` on the 25 labelled runs,
Haiku through the gateway's Anthropic route, spend agreed with the user
first. 25 runs asked, 316,597 tokens in and 34,905 out, $0.49. 47 of 52
labelled calls kept; all five drops quoted something not in the call,
four of them the heredoc placeholder.

Rules and reader together, Haiku labels then Sonnet labels, beside the
subagent run of the same prompt:

| Runs | Reader | Flagged | Right | Found |
|---|---|---|---|---|
| `agent-onboarding`, Sonnet, 15 | live | 124 | 78%, 84% | 77%, 79% |
| | subagents | 125 | 78%, 86% | 78%, 81% |
| `nb-changes`, Sonnet, 4 (private) | live | 15 | 93%, 93% | 56%, 70% |
| | subagents | 22 | 95%, 77% | 84%, 85% |
| `nb-changes`, Haiku, 6 | live | 4 | 100%, 100% | 33%, 33% |
| | subagents | 7 | 100%, 86% | 58%, 50% |

- **On the suite with a declaration the live reader matches** the
  subagent run to within two points.
- **On `nb-changes` it is more careful and finds less.** It labelled 11
  calls where the subagents labelled 21, and all but one of its steps
  are right by both label sets. What it left out is taken apart under
  "What the live reader leaves out" below.
- **One run each here; three passes are below.** The model takes no
  temperature setting, so two live runs can differ. The subagent readers
  also saw the prompt inside a session with its own instructions.

Three things the run turned up, fixed in proctor:

- Haiku 5.5 refuses a `temperature` setting outright, so the reader
  sends none. Proctor's `judge` check still sends one and would be
  refused by this model.
- The verb walked the cells of the suite as it is now. It walks the
  experiment's own record of its arms and tasks, so the labelled runs
  (and the Qwen `tern-docs` run) are read.
- `suites/agent-onboarding` declares its surface.

The live files are in the cells under proctor's `runs/`; the private
ones under `datasets/proctor-private/derived/friction-live/`.

### Run to run (three passes)

The ten `nb-changes` runs read twice more with `--rejudge`, $0.47 for the
two passes. The three fixes above are committed in proctor (`3ec0ef8`).

| Runs | Pass | Reader's calls kept | Flagged | Right | Found |
|---|---|---|---|---|---|
| Sonnet, 4 (private) | 1 | 7 | 15 | 93%, 93% | 56%, 70% |
| | 2 | 7 | 16 | 100%, 100% | 64%, 80% |
| | 3 | 8 | 16 | 94%, 94% | 60%, 75% |
| | subagents | | 22 | 95%, 77% | 84%, 85% |
| Haiku, 6 | 1 | 4 | 4 | 100%, 100% | 33%, 33% |
| | 2 | 3 | 3 | 100%, 100% | 25%, 25% |
| | 3 | 4 | 4 | 100%, 100% | 33%, 33% |
| | subagents | | 7 | 100%, 86% | 58%, 50% |

- **The live reader is steady.** One step either way between passes, and
  found moves 8 to 10 points on runs this few.
- **So the gap to the subagent run is real.** No pass comes near its 22
  and 7 flagged. The live reader finds the `repair` every time (4 and 2
  to 3 points) and little else.
- **It is the careful end of the trade.** Right is 93% to 100% by both
  label sets, above the subagents' 77% and 86% by the Sonnet labels.

Passes 2 and 3 are under `trials/out/friction-live-spread/` and
`datasets/proctor-private/derived/friction-live-pass{2,3}/`. The cells in
proctor's `runs/` hold pass 1.

### What the live reader leaves out

Counted from the label files and the three passes (2026-11-15). An
earlier line here said "mostly `lost`"; that holds for the public runs
only.

- **`lost` is five steps in the ten runs.** Four are one stretch in one
  public run (`far-cause` sample 1, steps 5 to 8: four greps of the
  provider projects with changing terms, after the method was found at
  step 4). Both label sets call all four lost. The fifth is a private
  step, lost by the Sonnet labels only.
- **The live reader marks one call of that stretch**: step 7 in pass 1,
  none in pass 2, step 8 in pass 3. The subagent run marked all four.
  Scoring is by step, so this is the whole public gap.
- **On the private runs the gap is not `lost`.** Steps the subagent run
  flagged and live pass 1 did not: 5 `workaround` and 2 `repair` by the
  Haiku labels, 3 `repair` by the Sonnet labels, which have no
  `workaround` line on these runs.
- **The pattern is one call marked where a stretch was meant.** A person
  reviewing the point lands in the right place either way, so the
  step-level "found" understates what the reader gives a reviewer.

## Next

1. Left alone for now: the reader's prompt. If it is taken up, the
   change is "label every call of a stretch", or the viewer treats a
   reader point as a stretch. About $0.25 a pass on these ten runs.
2. auspex's first view over `friction.json`: proctor now writes real
   ones, with reader points.
3. Later, if wanted: a synthetic suite whose surface is a tool
   (`off-path.md`: raw SQL in place of the CLI). The Qwen Code arm runs
   on the local box at $0.
