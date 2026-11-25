---
type: plan
title: The workbench: problems, changes and the rerun, with the reading tool split off and deferred
created: 2026-11-11
status: proposed; nothing built. Step 1 waits on nothing; step 2 is `lab.md` step 4 plus bundles on agent arms; the reading tool waits on beats
moved: 2026-11-12 from proctor's `project/plans/` (never committed there). Paths like `todo.md`, `lab.md` and `project/lab/` are proctor's. Superseded in part by `direction.md`: the reading tool no longer waits on beats (beats are a derived column), so step 4 and the status's last clause are out of date. Bundles on agent arms landed host-side only (proctor #11, 93b8927); in-container delivery is still open
---

# The workbench

## The loop it serves

1. Write a fixture and a suite.
2. Run the suite.
3. Read the runs, mark what went wrong and where, and group the marks into
   problems: each a guess about what in the surface caused them.
4. Submit a change to the surface (a bundle revision) meant to fix one or
   more problems, and say beforehand what the rerun should show.
5. Rerun. Proctor says whether each problem got rarer, by a count anyone
   can reproduce with no model.

Steps 1, 2 and 5 exist. Steps 3 and 4 happen today in a session's prose and
in loose files. This is view 2 of "Three things a reader does with
trajectories" (`todo.md`, 2026-11-11): the person codes failures from the
reading view, says which ones the treatment is meant to correct, and the
treatment's report leads with those named counts.

## Decided

- **Two things, not one.** Reading trajectories (summarise, show, flag) is
  a general job over ATIF that anyone with Harbor or vendor-agent
  trajectories has. It becomes its own tool, grown from
  `project/lab/observe.py`. The loop from a problem to a rerun is proctor's,
  because proctor owns checks, arms, bundles and the paired statistics.
- **The seam is a rule.** A flag in the reading tool is a rule over raw ATIF
  fields (tool name, an argument pattern, a result pattern, a host). It
  enters proctor through the script-check contract that exists today
  (`<tool> rule <name> "$PROCTOR_TRAJECTORY"`, exit 0, 1 or 2), as a
  guardrail. Proctor does not parse the rule language.
- **Beats belong to the reading tool, and nothing is measured with them.**
  A beat's edges are a judgement under active research (`todo.md`, "Beats,
  second cut" and "The cut read blind"). A check or a reported number that
  depended on them would change when the cut changes, and would break
  pairing against older experiments. Rules and checks read the raw record
  or the per-call event table, never kinds or beats. If proctor's report
  ever shows a beat, it is a label beside a step range (the step range is
  the anchor), taken from a file the tool wrote, with the tool's version
  recorded like a judge's.
- **A problem is measured by a check.** No new mechanism decides "fixed". A
  problem names a deterministic check that counts its occurrence; the
  existing paired statistics say whether a change moved it. A model check
  may not measure a problem.
- **Authored and derived stay apart.** What a person decides (a problem's
  wording and check, a change's bundle and prediction) is written down. What
  happened (the rates before and after, whether the prediction held) is
  computed by `grade` and `report` every time and never stored in an
  authored record.
- **Proctor gains no server.** It stays a CLI that writes a static report.
  Any interactive page belongs to the reading tool.

## Deferred, and why

**The reading tool**, its page and its notes wait until beats are stable
enough to build a run view on. The run view ("a run as beats, then
steps") is its centre; built around raw steps now, it would be redesigned
when the cut settles, and that is when packaging mistakes get expensive.
Research stays in `observe.py` and `project/lab/trials/`, throwaway and
quick to change.

**Beats count as stable enough** when all three hold on the `nb-changes`
and `agent-onboarding` runs:

1. A blinded reader calls at least 80% of cuts right (the bar in
   `debugging-suite.md`; 73% on 2026-11-11).
2. Two readers agree with each other on "one piece of work" well enough to
   put a ceiling under (1). Not yet measured.
3. Small changes to the rules move few beat edges on the runs already read,
   and the episode level (an advance and the rounds that follow it) either
   has a rule or is dropped from the view.

**The canvas** being specified in a parallel session (a React front end, a
free canvas, SQLite tracking revisions, problems, tests and changes;
`todo.md`, end of "Three things a reader does") is the reading tool's
interface under this plan, not proctor's. Two consequences for it:

- What proctor needs to compute step 5 of the loop (which check measures a
  problem, which bundle revision a change introduces, what it predicts) must
  be files in the suite that proctor reads without the canvas. The canvas
  may index them, show them and write them; it is not their only copy.
- The packaging rules below go with it.

## Step 1: run the loop by hand, before any verb (now, $0)

On an experiment that already exists, with hand-written files, to learn
whether the loop is used and what its records must hold before code fixes
them in place. Mock or the local model only.

1. Read a run with `observe.py` and write two or three notes by hand in
   `suites/<suite>/notes/<experiment>.jsonl` (format below).
2. Write one problem in `suites/<suite>/problems/<id>.json`, measured by a
   script check, proved over the existing cells with `observe.py count`
   (the seed of `grade --try`).
3. Write one change in `suites/<suite>/changes/<id>.json`: a bundle revision
   and a prediction.
4. Rerun with the changed bundle; compare with `report --against <before>`.
5. Write down in `project/todo.md` what was missing, awkward or never
   looked at.

A bare nb arm can carry a bundle today, so step 1 does not wait for agent
arms. `readme-answers` (README bundles, free tier, regex checks) or
`playground` (Mock) are the cheapest hosts; `nb-changes` on the local
model, once its runs exist, has the richest failures.

Done when: one loop has been run end to end and the formats below are
confirmed or corrected from it.

### The records, as a first guess

Continuing `lab-report-worked-example.md` (docs `main` against `pr-412`,
experiment `20270114-0932-otel-setup-k3vd`); everything here is invented.

A note anchors to a step range, never a beat, and carries its quote, since
runs are gitignored and archived and the note must still read when its cell
is gone:

```json
{"id":"n-7k2q","cell":"codex.pr-412/aspnet/1","steps":[41,41],"tag":"auth-401","quote":"missing X-Beacon-Dataset header","text":"First export rejected; the agent had not seen the header anywhere.","by":"session","at":"2027-01-14T11:02:10Z"}
```

A problem blames the surface, not the agent, and names its check:

```json
{
  "id": "header-not-on-quickstart",
  "title": "pr-412 moved X-Beacon-Dataset off /quickstart; agents meet it as a 401",
  "blames": "docs: /quickstart, /quickstart/<language>",
  "check": "beacon-401",
  "notes": [{"experiment": "20270114-0932-otel-setup-k3vd", "ids": ["n-7k2q", "n-7k2r"]}],
  "status": "open"
}
```

A change names its bundle revision and predicts per problem:

```json
{
  "id": "header-back-on-quickstart",
  "fixes": ["header-not-on-quickstart"],
  "bundle": {"from": {"git": "docs", "rev": "7be2f15"}, "to": {"git": "docs", "rev": "c03e9a1"}},
  "predict": [{"problem": "header-not-on-quickstart", "at_most": 1, "of": 8}],
  "before": "20270114-0932-otel-setup-k3vd"
}
```

What the report should eventually say (step 3), set beside the prediction,
never judging the change:

> | Change | Problem | Before | After | Predicted | |
> |---|---|---|---|---|---|
> | `header-back-on-quickstart` | `header-not-on-quickstart` (`beacon-401`) | 6 of 8 | 1 of 8 | at most 1 of 8 | **Held** |
>
> Paired by agent and task: 5 runs changed, 0 the other way (exact p =
> 0.06, unconfirmed).

Questions step 1 should answer:

| # | Question | First guess |
|---|---|---|
| 1 | The words | **note**, **problem**, **change**. "Change" overlaps "bundle revision"; **fix** is the alternative. Into `CLAUDE.md`'s table only when code uses them |
| 2 | Who owns notes | The reading tool, once it exists; until then a file in the suite. Proctor never reads a note's content, only that a problem points at notes |
| 3 | One file per problem and per change | Yes: they merge cleanly. Notes one file per experiment, append-only |
| 4 | Is a problem's check required? | Not to open one; required before a change can name it |
| 5 | What a prediction is | A direction and a size per problem against its check |
| 6 | Does anything mark the rerun as testing a change? | `run` notices an arm whose bundle is a change's `to` and records it in `experiment.json`; or the person names the change with a flag. Step 1 will show which is less error-prone |
| 7 | Can a problem's check be a rule with no reading tool yet? | Yes: a script check written by hand. The tool's rule language only makes writing them easier |

## Step 2: the steps-only foundation in proctor

Needed by `lab.md` regardless, and nothing in it rests on beats:

- **Bundles on agent arms** (`todo.md`, "An agent arm cannot carry a
  bundle"). Without it a change on a vendor agent is a fixture edit, with no
  bundle hash and no pairing.
- **`events.jsonl`** per cell, one row per tool call with raw fields and a
  stable step id; notes and rules anchor to it.
- **`proctor steps <cell> <range>`.**
- **`proctor grade --try <check spec>`**: verdicts over existing cells,
  nothing written. How a problem's check is proved before a change names it.

Done when: `lab.md` step 4's own criterion holds, and step 1's notes can be
rewritten against `events.jsonl` step ids without loss.

## Step 3: proctor's side of the loop

Only after step 1 has confirmed the records:

- Records and loading for problems and changes (`Suite/Problems.cs`),
  reported by `list` like any suite problem: a problem naming a missing
  check, a change naming a missing problem, a problem measured by a model
  check or a `pass` check.
- Verbs, plain lines, no Spectre: `problem new|check|close`,
  `change new`. Notes stay files until the reading tool owns them.
- `run` records which change an experiment tests; `Stats.cs` computes before,
  after and the pairing per problem; `Report.Blocks` renders "Changes
  tested". Snapshot-tested.
- An investigation skill beside `skills/proctor-suite`, ending in notes and
  a problem with a proved check, not in prose.

Done when: the step 1 loop runs again through the verbs and the report
section comes out of a real rerun.

## Step 4: the reading tool (when beats pass)

Its own repository and plan, written then. What this plan fixes for it now:

- Input is ATIF, plus an optional labels file beside the trajectories (arm,
  task, sample, bundle) that proctor writes, so runs can be grouped without
  the tool knowing proctor.
- Output proctor consumes: rule verdicts through the script-check contract,
  and optionally a beats file per cell for labels in the report.
- Its own kinds of call, separate from proctor's `Ingest.Kinds`, which
  exists for strict counting and conformance, not for reading. Where the
  two must agree, the event table is the meeting point.
- The language is open: Python fits Harbor's ecosystem and `uvx`, and
  `observe.py` already is it.

## Packaging, for whatever page is built

This has gone wrong before: development on a dev server (React and Vite,
hot reload), production shipping a different package from the one the
tests ran against, so the first person to see a broken page was a user.
These rules hold for the reading tool's page and the canvas, whichever
stack they use.

**The bytes the tests fetch are the bytes a user gets, through the same
code path.**

1. **One way an asset reaches the browser.** Every page, script and
   stylesheet ships inside the released artefact and is served by one
   handler. No lookup relative to the working directory or a content root:
   those resolve differently under a dev run, a test run and an installed
   copy, which is the class of bug to rule out.
2. **No mode switch.** No development flag or environment variable that
   changes what is served or where it comes from. A switch is two products,
   and only one gets tested.
3. **One build.** If a JavaScript build exists, the project's normal build
   or package step runs it, and only its output is served. A Vite dev
   server, if used at all, proxies the real server for data and is never
   what a test hits or a document describes.
4. **The tests run the real server.** Start it from the built artefact on
   port 0, crawl every route linked from the index, fetch every asset each
   page names, and fail on any non-200, any named asset that is not shipped,
   and any shipped asset nothing names. Write one record through the page
   and assert the file equals what the CLI writes.
5. **The installed form is gated too.** An opt-in test builds the package a
   user installs (a wheel run by `uvx`, a `dotnet publish`, whatever it is)
   and repeats the crawl against it.

The first thing built for any page is a skeleton that serves one page and
one asset and passes 4 and 5. Nothing is designed before that.

A server is loopback only, with a per-start token in the URL it prints and
`Origin` checked on every write, since it writes files and cells can hold
keys in their config snapshots.

## Not in this plan

A hosted, multi-user workbench; a database as proctor's store; model-written
notes, problem titles or beat labels; human verdicts on specimens (`todo.md`,
"Specimens for human review"); aligning trajectories across agents.

## Order and cost

Step 1 costs nothing and can change everything after it. Step 2 is shared
with `lab.md`. Step 3 is small once step 1 has settled the records. Step 4
waits on the beat research, which `nb-changes` is feeding now. All of it is
tested at $0: Mock cells, captured trajectories, the gate's suites.
