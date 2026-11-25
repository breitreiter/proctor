---
date: 2026-11-12, the quiet regime revised 2026-11-14
status: proposed; nothing built. Says which problems the tools are for. Where the tools live is settled by `loop.md`; what the quiet regime is for was revised on 2026-11-14 (two kinds of friction), and the numbers under it stand as measured
---

# Regimes: what a person needs after an experiment

## Where the person arrives

From the user, after about a year of running these. At the end of an
experiment the person is in one of six states:

1. **Passed, and the checks are trusted.** Nothing to do.
2. **Passed, and the checks are not trusted.** Go and look, to be sure
   nothing was missed.
3. **Mixed.** Why did some runs fail?
4. **Passed with smells.** Close to the budgets; feels wrong.
5. **Obvious failure.** Usually clear what broke, sometimes not what the fix
   is. The fix may be in a check, the fixture or Harbor's network. Checks
   are mostly sound these days; bad fixtures and network trouble are the
   standing nuisance.
6. **A treatment made it worse.** Something was changed to fix something,
   and the result is net worse.

How the looking is done today: Claude reads the transcript and checks it
against the person's high-level intents, interactively, then is asked to
probe for shortcuts the person suspects and cannot prove. Before that the
person read the transcript and looked for behaviour nobody had expected,
which is why it had no check. Two losses are admitted:

- **Asking loses the surprise.** A review against stated intents finds only
  violations of what was stated.
- **The probes are not kept.** Each is asked of one run in one session.

Vendor agents have also made a step harder to read: a line of vague prose
in terms the agent invented, a long inline script, an exit code.

## Three references

A run is odd compared with something. There are three things to compare
it with:

- **Its siblings**: the other runs of the same task, by arm and outcome.
- **The experiment before**: the same suite before a treatment.
- **The person's intents**: what a good run looks like, which today is
  unwritten.

States 3 and 4 use the first, state 6 the second, and states 2 and 5 have
only the third.

## Three regimes

The user's classes of problem, sorted by how they show up. The regime, not
the class, decides the instrument.

| Regime | Classes | Shows as | Found by |
|---|---|---|---|
| **Loud** | a plain fixture or container bug; a plain failure of the thing under test | an error in every run | one cheap run, read by a stronger model |
| **Rare** | a flaky fixture or container bug; a flaky tool bug | an error in a few runs | a count across experiments |
| **Quiet** | no bug: a tool badly described, output that needs work, many calls to get one fact | cost in every run, no error | a scent in the steps, which ends as a check or as a practitioner's call |

The quiet class looks like a plain failure to a weak model. A frontier
model reasons through it, and then it is hard to tell from "the problem is
hard, so it costs".

## Loud: Claude Code, and little from auspex

A coding agent session slicing the ATIF finds where a run died. The
user's shakedown run on local Qwen is the instrument, for two reasons the
user gives: the session's model fully understands what Qwen is doing, and
Qwen does not work around a fixture mistake, it repeats itself until the
token cap.

What is left for tooling:

- **A mark that a run is invalid**, so it stays out of every later count.
  A proctor record, not a view.
- **Where the weak model stalled**, carried forward. The bug that is loud
  on Qwen is quiet on a frontier model, which works around it and passes.
  The stall is a place to check the frontier runs for a detour.

One limit, measured (`trials/subsample.py`): on `agent-onboarding` the Qwen
runs show the Sonnet runs' stumbles 3, 0, 0, 0 and 1 times of 9. The weak
model stalls at the first obstacle and hides what is behind it.

## Rare: a decision to chase or not

The person decides whether it is worth chasing. That depends on the use
case, the tolerance for a run that sometimes fails, and the risk of the
treatment. Three things support the decision, none of them a picture.

**A signature.** Which check failed, and its reason. A budget cap, a hard
check and a mis-tuned judge then look different from each other.

**The price to chase.** After a fix, how many runs in a row without the
failure are needed before "it is gone" beats luck. If the failure occurs at
rate p, n clean runs happen by chance with probability (1 − p)ⁿ; the price
is the smallest n that puts that under 5% (`trials/price.py`):

| Rate | Clean runs needed |
|---|---|
| 1 in 2 | 5 |
| 1 in 3 | 8 |
| 1 in 4 | 11 |
| 1 in 5 | 14 |
| 1 in 10 | 29 |
| 1 in 20 | 59 |

The rate is itself an estimate, so the price is a range. A failure seen in
3 of 15 runs has a true rate plausibly between 4% and 48%, which is 5 to
68 clean runs; at $0.26 a run (Sonnet on `agent-onboarding`), $1.30 to
$17.68. One sighting in 18 runs (nb's timing-dependent test, proctor
`todo.md`) cannot be priced from above at all. The range is the risk of
hill-climbing, stated before any money is spent.

**The options that cost nothing**, in this order:

1. **Regrade.** A failure blamed on a check or a judge is settled over the
   cells already paid for. No new run.
2. **Watch.** A kept query counts the failure on every later experiment.
   It is chased only if it grows.
3. **Chase.** The only option with a price.

## Quiet: a scent that ends as a check, and a judgement about the rest

**Two kinds of friction** (the user, 2026-11-14). This replaces the
earlier framing, in which the ledger was the main instrument and a
picture of it the goal.

1. **Something that should be a failing check.** Nobody expected an agent
   to try it, or nobody knew the surface under test would fail that way.
   A very capable agent powers through, so the macro checks (final state,
   acceptance) still pass. It starts as a scent and ends as a check. This
   is the case being solved for.
2. **Everything else.** Too fuzzy to code in a durable, production-ready
   way. Less friction is better, returns diminish, and a practitioner,
   human or otherwise, makes the call against their own goals.

A hard requirement is a check. So there is no separate notion of intended
friction, and the moment a practitioner puts a number on the second kind
("no more than five calls spent learning the tool") it becomes a check
and moves to the first.

**What the first kind looks like.** From `agent-onboarding` (Sonnet, 15
runs, 11 of which passed the suite's grading; counts from
`trials/subsample.py`). No check fails on any of these today:

| What the runs did | Runs | Where it ends |
|---|---|---|
| Hit the docs' dead end ("ask the user to run `proctor setup`") and carried on | 15 | a check: the docs never send a run to an instruction it cannot follow |
| Read the internal plan `on-disk-layout.md` | 6 | a check: no read outside the published docs |
| Read `Grade/Checks.cs` to learn what a check does | 3 | a check: no read of the tool's source |
| Found `python3` missing | 4 | a fix to the fixture, not a check on the agent |

A scent can end as a fixture fix as well as a check. Either way it is
tried over the runs already on disk before it is kept (`loop.md`, step
4), which costs nothing.

**Finding the scent.** What has been tried, all on the same 15 runs:

- **Symptoms in the next action** find most of it by rule: 84% of the
  steps two readers call friction, about three flags in four right
  (`surprise.md`, second test). A reader is about 15 points better.
- **The rules carry a suite's names in them**, and do not transfer as
  they are (`surprise.md`, cross-check). The suite has to declare its
  surface and the substitutes for it (`surface.md`). With that, "the
  surface was called and then a substitute was used" and "the run passed
  and never touched the surface" are the rule-made signature of powering
  through.
- **Search is not friction by itself.** Skilled people choose many cheap
  checks over a few reasoned ones, and it is rational by their criterion
  (`learnings/rasmussen-trouble-shooting.md`). A third of time on
  finding and re-finding is also the human rate on unfamiliar code
  (`learnings/programmer-time-studies.md`). So a search is a scent only
  against a declared surface, and its cost is the question, not its
  presence.

**The second kind: the ledger, as an aid.** Where each run's budget went,
a row per run, in three standings: the work, learning the surface as
intended, and friction. It is shown to the practitioner and is not a
gate. What it is still for:

- **The size of the prize**: what friction costs per run, which bounds
  what a treatment can save.
- **Before and after**: whether a treatment reduced the share, with no
  threshold. The shares have to stay comparable across two experiments.
- **Who pays**: every arm, or the weaker ones.
- **What the treatment costs**: a longer skill is paid for in every run.

The measurements below say how far to trust it: the three standings are
stable between readers, and nothing finer is.

**Lines carry context, not only a kind** (the user, 2026-11-12). Read,
write and search say too little. What matters is the activity against what
the task makes normal:

- Wrote a `.cs` file for an nb patch: normal. Wrote an awk script:
  concerning.
- Searched for a library's CVEs: normal. Searched for Win32 header
  documentation: perhaps concerning.

Where that context comes from was open here and is now `surface.md`: the
suite declares the surface and its substitutes, and a call sorts by rule
into on, about and around the surface, and background. An earlier guess
at standings (the work, scaffolding, elsewhere) is dropped in favour of
the three the readers used.

**What the first trial found** (`trials/quiet.py`, `trials/quiet2.py`,
Sonnet `agent-onboarding-dd5u`, 15 runs):

- **The agent's own questions are the best signal.** Every run searched
  proctor's tree. By topic (grouped by reading, counted by regex): check
  names 10 runs, the program's `system` line 10, provider config 7,
  `PROCTOR_*` variables 5, hooks 4. No error is involved and most of these
  runs passed.
- **Every run had to locate the install**: `readlink -f` on the binary in
  15 of 15, `find /` in 11. `--help` in 12.
- **Retries were silent.** Apart from the setup dead end, proctor's errors
  cleared in one round in 14 of 15 runs.
- **Post-processing misfired.** `proctor grade | tail` in 13 runs is copied
  from the reference `shakedown.sh`. What the surface's own examples do has
  to be subtracted before a count means anything.
- **Nothing separates runs by cost.** Runs that read internal notes or
  source (10) against runs that did not (5): median $0.26 against $0.23,
  16.5 calls against 17. All 4 failures are in the 10, which chance gives
  about one time in seven. Quiet friction is paid by every run, so it
  shows only across arms or across a treatment, never within an arm.

**The ledger, built by hand once** (`trials/ledger.py`,
`trials/ledger/vocabulary.md`, labels under `trials/ledger/labels/`; the
same 15 runs, 254 calls, $3.66 of call cost). Seventeen lines in three
standings (work, learning the surface as intended, friction). Two subagent
readers, one on Sonnet and one on Haiku, labelled every call with one to
three lines and a quote for any friction line; two runs were labelled in
the session as a reference. A call's cost is split evenly over its lines.

- **The standings are stable.** Share of dollars by Haiku's labels and by
  Sonnet's: work 43% and 43%, learning 22% and 24%, friction 35% and 32%.
  Per run the two differ by a median of 2 to 5 points, 16 at worst.
- **About a third of the arm's spend is friction.** Per run the share runs
  from about a fifth to nearly a half (22% to 48% by Haiku, 16% to 44% by
  Sonnet).
- **The prize can be sized, roughly.** The largest friction lines, as the
  two readers put them: searching the tree $0.49 and $0.30, locating the
  install $0.22 and $0.27, repair $0.26 and $0.24. A docs change aimed at
  the first two addresses at most 19% or 16% of the spend.
- **Lines are less stable than standings.** The readers gave the same
  first line on 218 of 254 calls (86%) and the same set on 158 (62%). The
  search line is the loosest: a grep over the docs is a search to one
  reader and a read to the other. Per-run agreement is weak for
  `docs-read` (rank correlation 0.39) and `locate` (0.45), strong for
  `repair` (0.92) and `env-probe` (0.93). Good enough to size a line over
  an arm; not good enough to compare two runs on one line.
- **Attribution is rough where calls are compound.** Half to two thirds of
  calls carry more than one line (128 by Haiku, 169 by Sonnet), and a
  third or more mix standings (83 and 110). The even split is arbitrary.
- **A rule and a reader disagree on about a quarter of searches.** Of 42
  calls the grep rule flags, the readers labelled 34 and 31 as searches,
  and labelled 13 and 11 that the rule did not flag. Lines a rule can
  decide (search, locate, help, skill) should come from the rule; a reader
  is needed for repair, scaffolding and rereading.
- **Quotes resolved**: 133 of 133 (Sonnet), 126 of 128 (Haiku; the two
  misses quote the rendering's heredoc placeholder, which is not in the
  record). The checker also rejected two of the session's own reference
  quotes, which cited a neighbouring step.
- **The weaker reader was enough.** Against the reference Haiku matched
  the first line on 31 of 35 calls and Sonnet on 32.

Not counted: an early detour also inflates the context of every later
call, so friction near the start costs more than its own calls show.

## How many runs

Measured by enumerating every subsample of the 15 Sonnet runs
(`trials/subsample.py`). The chance a subsample shows a known finding at
least once / at least twice:

| Finding | Runs | Any 3 | Any 5 | Any 7 |
|---|---|---|---|---|
| `proctor setup` dead end | 15 of 15 | 100% / 100% | 100% / 100% | 100% / 100% |
| Read `on-disk-layout.md` | 6 of 15 | 82% / 34% | 96% / 71% | 99% / 92% |
| `python3` missing | 4 of 15 | 64% / 15% | 85% / 41% | 95% / 66% |
| No model at `localhost:8080` | 4 of 15 | 64% / 15% | 85% / 41% | 95% / 66% |
| Read `Grade/Checks.cs` | 3 of 15 | 52% / 8% | 74% / 24% | 88% / 45% |

Five runs meet most findings once and few of them twice. So:

- **Finding problems needs few runs**, and yields candidates.
- **Confirming a fix on its own stumble needs few runs** when the stumble
  was common, and the price table when it was rare.
- **Moving a pass rate needs many**, and says least.
- **Each treatment in a batch names the stumble it should remove.** One
  rerun then speaks to each treatment separately. Treatments can still
  interact.

## What cannot be read must be chosen

More samples means more transcripts than a person reads. A reading list
with a reason against each entry: one run per failure signature, the
cheapest pass as the reference, the outliers on cost, any run that did
what no sibling did. The last is a rule-made list of surprises, and the
only partial answer so far to "asking loses the surprise".

## What this makes of auspex

Where each piece lives is `loop.md` (2026-11-14): proctor codes a run,
including the friction points, signatures and kept queries; auspex
renders them and holds the person's review.

- **Its centre is the review that turns a scent into a check.** One run,
  its friction points with the steps around each, and for each a
  disposition: left alone, or the check written for it. Success is a
  candidate the person accepts that fails on runs already on disk.
- **The ledger is shown beside it**, three standings per run and per arm,
  before and after a treatment, for the practitioner's call on the
  second kind of friction.
- **For the rare regime** it shows a signature and a price.
- **For the loud regime** almost nothing.
- **What it hands proctor** is a check, and a treatment with the checks
  it should flip.

## Open

- Cost attribution inside a compound call. The even split held up at the
  level of standings and not below it; splitting a call into its actions
  by rule is the likely fix, untried.
- What a detour costs downstream, in the context of every later call.
  Rasmussen's criterion makes this the measure that decides whether a
  cheap search was cheap.
- Whether the three standings stay comparable across two experiments
  when a reader assigns them, which before-and-after depends on.
- Whether a scent can be recognised again in a later run, so a point
  left alone is not raised afresh each time (`loop.md`, "Acceptable").
- The copied data is small: of 175 non-scripted ATIF runs the median is 16
  tool calls and the longest 93. The motivating runs are hundreds of calls.
  One public Terminal-Bench 2.0 job (440 runs, median 29 calls, longest
  506) is in `datasets/public/`.
- `trials/` imports `observe.py` from proctor's lab for command splitting.

Settled since first written: where a line's standing comes from
(`surface.md`); whether the lines carry to another suite (they do not
without a declaration); whether intents become a written file (they
become the surface declaration and checks); whose the invalid-run mark
and the price are (proctor's, by `loop.md`).
