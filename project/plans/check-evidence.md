---
type: plan
title: Checks that explain themselves — why a check exists, what it does, and what it points at
created: 2026-11-22
status: proposed 2026-11-22 from auspex's stage page design, not agreed. Lines marked OPEN are mine. Depends on bank step 4 (stages) only for stage ids; C1 and C2 can be built now. C5 extended the same day with friction's legibility and roll-up (C5.1 to C5.4), also proposed
---

# Checks that explain themselves

## Why

auspex is designing the page for one stage of one attempt (exam, setup,
try, stage). On a failed stage the person first wants to understand
the failure, and only then maybe read the transcript. The user said
what that takes (2026-11-22): mostly the check's reason, plus **what the
check does, mechanically, and why the author thought it worth checking**.
The agent's own marks matter only when the person suspects an accurate
answer that didn't fit the expected form.

So a failure card in auspex reads, in order:

1. the reason (what happened: "answered 60; expected 30");
2. what the check does (from its definition);
3. why the check exists (the author's words);
4. the evidence: a step, a file, the record of a search that found
   nothing, or none.

Proctor already holds (1) and most of (2). It has nothing for (3) or (4).
auspex reads what proctor writes and keeps no second copy of proctor's
logic, so all four have to be in files proctor writes.

## What exists

- `checks.json` per attempt: check name → `{result, reason}` (`Verdict`).
- `description`: optional on every check, required on a script check,
  validated as a sentence. It says *what* the check tests.
- `Checks.Describe(spec)`: the description, or a sentence derived from
  the built-in fields ("uses save_data", "changes touch all of
  note.txt"). It is used by the report and the Harbor TOML. It is not
  written next to a verdict.
- Built-in kinds already name what they look for: `tools_used` lists
  tools, `files_touched` lists paths, `answer_contains` and
  `result_matches` give a string or pattern. So a check like "must call
  save_data" is `tools_used: ["save_data"]`, and no new kind is needed
  for it.

## C1. `why` on every check

An optional key beside `description`. Like `description`, it is not a
check field. It holds one sentence on why the author thinks this is
worth checking.

```json
"no_denials": {
  "denied_calls": { "max": 0 },
  "why": "a denied call means the agent tried something the setup forbids, which a user would see as a stuck session"
}
```

- Validated the way `description` is: a sentence, never an empty string.
- Not required. A check without one shows "no stated reason". The
  report and auspex both say so rather than leaving a blank, so the gap
  stays visible.
- OPEN: should `proctor check` (or lint) list checks that have no `why`?
  A list, not an error.
- The exam-writing skill and `docs/configuration.md` gain a line asking
  for a `why` on each check (B9's audience: the author is usually a
  coding agent).
- The guardrail checks that recur in every exam (`exit_ok`,
  `no_denials`, `not_nudged`, `under_budget`) want one sentence each,
  written once. OPEN: as a default `why` proctor supplies when the
  check is copied in unchanged, or written into each exam.

## C2. The verdict carries the check as it was graded

Each entry in `checks.json` gains the explanation of the definition that
produced it:

```json
"touches-note": {
  "result": "fail",
  "reason": "note.txt was not changed",
  "describes": "changes touch all of note.txt",
  "why": "the task is to record the decision in note.txt",
  "evidence": [ { "absent": { "files": ["note.txt"], "in": "diff" } } ]
}
```

- `describes` is `Checks.Describe(spec)` at grade time; `why` is copied
  from the spec. Snapshotting them ties the explanation to the
  definition that produced the verdict, so an exam edited later doesn't
  re-explain an old verdict with new words. An `outdated` verdict
  already exists for that case.
- A script check's `describes` is its `description`. OPEN: also the
  script's path, so a person can open it.
- A `decide` or `judge` check's `describes` is `Judge.Describe`: the
  question asked, the window read, and the options.
- auspex generates nothing. It shows these fields.

## C3. Evidence on a verdict

`evidence` is an optional list. Each item is one of:

| Item | Meaning |
|---|---|
| `{stage, step}` | an ATIF step by its original id (the stage id once stages exist) |
| `{file, lines?}` | a path in the attempt's diff, with an optional line range |
| `{absent: {tools \| files \| text \| pattern, in}}` | what was searched for and where (`transcript`, `answer`, `results`, `diff`) and found nowhere |
| none | the check is about the attempt or stage as a whole |

The same check points at different things depending on its result.
`tools_used: ["save_data"]` points at the call when it passes and at
`absent` when it fails. Evidence belongs to the verdict, not to the
check.

What each built-in kind would write:

| Kind | On pass | On fail |
|---|---|---|
| `tools_used`, `tools_used_any` | the steps of the calls | `absent: tools` (the missing ones) |
| `tool_args` | the matching call | the calls to that tool, or `absent: tools` |
| `tool_sequence` | the calls in order | the calls found, plus `absent` for the first missing |
| `answer_*` | the answer step | the answer step, plus `absent: text/pattern` for contains and regex |
| `result_matches` | the step whose result matched | `absent: pattern, in: results` |
| `denied_calls`, `tool_errors` | none | the offending steps |
| `files_touched` | the files | `at_least`: `absent: files`; `at_most`/exact: the extra files |
| `oracle_*` | the oracle turns | the oracle turns |
| `exit_reason`, `loop_nudged`, `max_tokens`, `max_duration_ms` | none | none |
| `max_tool_calls` | none | the call that went over. OPEN: worth it? |
| `decide`, `judge` | steps of the quotes the judge cited, found by the judge's own quote rule | same |
| `script` | whatever the script reports | same |

- Script checks: OPEN. The script contract could take optional evidence
  lines in the script's output. Until then a script verdict has none.
- Step ids are ATIF's original ids, the ones auspex's addresses use
  (`exam/setup/try/stage#step`).

## C4. Evidence and B8

Under B8, a check on the workspace runs once, at the end of the attempt,
and sees what the last stage left. So a `{file}` item belongs to the
attempt, not to a stage. A check on the transcript is per stage for
free. auspex will show workspace checks at the attempt level, or on a
stage only when the check names it. OPEN: should a check be able to name
its stage (`"stage": "tags"`) for display, given that the author already
arranges for a stage's output to survive later stages?

## C5. Friction on the same footing

auspex's stage page shows friction points beside the checks: on a
green stage they are the way in. The user's rule (2026-11-22):
**proctor scaffolds friction, auspex only renders it.** Detection,
grouping and interpretation all live here, so auspex doesn't become a
second proctor.

The user's ask (2026-11-22) is that friction be legible at three
levels, and that it roll up to a direction a person can act on: "this
got better", or "with your new fixture things got worse". It has
to be explainable to that person, so there is one measure and no
second: **a count of flagged steps**, each step counted once. There is
no severity, no weight, no score and no tokens (see "Not taken").

### C5.1 A point says why it was marked

Today a rule's point carries a label, rule ids, a normalised `detail`
and a quote: `workaround` / `inline-script-or-scratch` / `"python3 -c"`.
Only a reader's point has a sentence (`notes`). `rules` and `detail`
are lists on the point and `evidence` stops at six, so nothing says
which rule fired on which call.

- **`rule` and `because` on each evidence item.** `because` is one
  sentence the rule writes when it fires, from what it already holds.
  It is a template per rule, so the same trajectory still gives the same
  file. A reader's `why` goes in the same field, and `notes` stays for
  whoever reads it today.

  ```json
  { "step": "9", "call": "s9.1", "quote": "localhost:8081", "where": "command",
    "rule": "substitute-after-failed-surface",
    "because": "used localhost:8081, a declared substitute, one step after the call to localhost:8080 at s8.1 failed (HTTP 404)" }
  ```

- **The sentences.** Words in angle brackets are filled from the call.

  | Rule | `because` |
  |---|---|
  | `internals` | read `<path>`, which is the surface's own source or notes, not its published side |
  | `locating` | went looking for where `<command>` is installed |
  | `substitute-command`, `-tool`, `-url` | used `<what>`, a declared substitute for the surface |
  | `substitute-after-failed-surface` | used `<what>`, a declared substitute, `<n>` steps after the call to `<surface>` at `<call>` failed (`<match>`) |
  | `docs-search` | searched the surface's docs (`<path>`) |
  | `surface-help`, `help` | asked `<command>` for its help |
  | `inline-script-or-scratch` | ran a script inline or wrote to a scratch file (`<match>`) |
  | `machine-refuses` | the machine refused: `<line>` |
  | `tool-refuses` | the agent's own tool refused: `<line>` |

- **Each label carries its sentence in the file.** `detector.labels`
  and `reader.labels` become objects: the name and `describes` (the "a step
  where" sentence of `docs/configuration.md`). A viewer words nothing
  itself.
- **Addresses.** Under stages a friction point's `steps` span names its
  stage, as evidence does in C3, so a point and a check verdict point
  at steps the same way.

### C5.2 The surface verdict is a field

auspex today turns `summary.surface`'s flags into words
(`SurfaceWords`). The rule moves here as `summary.surface.verdict`, one
of `never-reached`, `went-round`, `used-all-failed`, `used`, with
`abandoned` and `used_substitute` beside it as they are. It describes
the attempt. It is not a severity and it qualifies no result: a pass
is a pass. An author who wants an attempt that went round not to count
writes a check for it and lists it under `grading.validity`.

OPEN (asked from auspex, 2026-11-22): the verdict should carry its
sentence too, as each label carries `describes` (C5.1), since a viewer
words nothing itself.

### C5.3 One attempt (one stage, once stages exist)

`summary` gains `flagged_steps`, which is today's `steps_in_a_point`
under a name that says what it is: the steps inside any point, each
once, whatever the label.

- **A count, not a share**, since a share falls when an attempt does
  more ordinary work.
- **Every label counts the same** (the user, 2026-11-22). On
  `tern-docs`, leaving out the cheap labels (`help`, `surface-search`,
  `reread`, `tool-error`, `workaround`) gives the same verdicts, and
  hides about 20 steps an attempt of Qwen probing by inline script
  when it had no docs, which is detour.
- `by_label` stays, as the points per label.

### C5.4 Across attempts: better, worse, or no detectable difference

One file per exam, `bank/<exam>/friction.json`, written by `proctor
friction` after the attempts' files, from them alone. It is derived
from the attempts, so it sits beside them. **`proctor report` does not
render it** (the user, 2026-11-22): the static report is provisional
and reporting is expected to move into auspex, which reads this file
and shows it. So "no report reads friction" stands, and the Friction
section `exam-variant-attempt.md` plans for the page is auspex's.

Per variant:

- **Flagged steps per attempt**, as each task's mean over its attempts,
  so the unit is the task, as in `Stats.cs`.
- **By label**: the attempts with it, k of n, and its steps.
- **What recurs**: points grouped by label and `detail`, in how many
  attempts ("`file`: command not found, 12 of 15"). This is what
  `Pages.Across` does in auspex today; it moves here. A reader's points
  have no `detail` and group by label alone. Each group lists **which**
  attempts it was in, not just how many: auspex draws it as a grid of
  issues by try, to show whether runs hit the same issues or each one
  wanders differently.
- **Per attempt** (asked from auspex, 2026-11-22): each attempt's
  flagged steps, and the share of its stages right (its passing checks,
  until stages exist). auspex shows a variant's spread across tries
  ("3 to 6 flagged steps per try, median 4") next to live's, so a
  person sees whether a difference between variants is bigger than
  the variation from try to try.

**The three levels** (the user, 2026-11-22), and what each reads:

| Level | The question | What it reads |
|---|---|---|
| Variant (auspex: setup) | did the treatment make things better or worse? | stages right and flagged steps per try, as a spread against live, plus the verdict below |
| Across attempts (auspex: tries) | the same issues every run, or every run different? | what recurs, by attempt |
| One stage | why did this friction happen? | the transcript, with each point's `because` |

Against the live variant, for every other variant, in the report's own
three words:

- **Direction** is the sign of the difference in flagged steps per
  attempt, task by task: so many tasks worse, the same, better.
- **Confirmed** when the tasks agree: the exact sign test over the
  tasks that differ, two-sided at 95% (2026-11-22, my recommendation,
  not objected to). Six tasks pass only when all six move one way. It
  is one sentence to a person ("worse on all six tasks") and needs no
  assumption about how step counts are spread. Five tasks or fewer can
  never confirm, and the file says so: "worse on 5 of 5, too few tasks
  to confirm".
- **What moved** is listed under the verdict: the labels and details
  whose attempt counts changed most. It explains the verdict and is
  never part of it. Label by label the direction can be wrong: without
  docs `workaround` fell from 12 attempts to 2, because the agent never
  got as far as scripting.

**OPEN: the unit under stages** (raised from auspex, 2026-11-22). The
sign test above treats tasks as independent, which holds when each task
has its own attempts in a fresh workspace. Under the bank's stages
(B8), a try runs every stage in one container whose workspace is never
reset, so stages share causes. A `jot` missing from the PATH fires in
every stage, a variant that fixes it moves all six, and the test reads
6 of 6, confirmed, from one cause. And an exam of one long stage would
have a single unit, so it could never confirm. Recommended from the
auspex side: **confirm over tries**, which are independent runs. Tries
from two variants don't pair up, so the test is unpaired: Mann–Whitney,
said as "a try of the variant had fewer flagged steps than a try of
live in 23 of 25 pairings". Stage-by-stage direction stays as "what
moved", explaining the verdict, never part of it, as labels do now.
Until stages exist, tasks are independent and the sign test as written
holds.

**One attempt** is placed, not judged: "5 flagged steps; the other
attempts of this task had 1 to 2". One attempt confirms no direction.

**Measured** on the 54 `tern-docs` attempts copied to auspex
(`project/trials/out/friction-tern`, rules 0.9, no reader), six tasks,
task means, each against the docs as shipped:

| Model | Variant | Flagged steps | Other steps | Tasks worse / same / better |
|---|---|---|---|---|
| Sonnet | no docs | 1.8 → 4.8 | 2.2 → 2.1 | 6 / 0 / 0 |
| Sonnet | docs restructured | 1.8 → 1.8 | 2.2 → 2.1 | 2 / 2 / 2 |
| Qwen | no docs | 2.7 → 65.3 | 16.0 → 17.2 | 6 / 0 / 0 |
| Qwen | docs restructured | 2.7 → 2.3 | 16.0 → 12.7 | 2 / 0 / 4 |

Without docs the other steps stay the same size and every added step
is inside a point: on this exam the count is the detour and nothing
else. The restructure reads "no detectable difference", which twelve
attempts support.

**Limits.**

- One exam. Where friction is inside the work (`repair`, `lost`), only
  the reader sees it: on `nb-changes` the rules alone found 8% of what
  two readers called friction. Measure there, with reader points,
  before the count is trusted on a coding exam.
- Two variants compare only under one `RulesVersion` and one surface
  hash, and a reader's labels count only when every attempt on both
  sides was read by the same reader and prompt. Otherwise the file says
  which comparison it left out and why.
- **A changed fixture.** "With your new fixture things got worse"
  compares attempts made under two task hashes. The older ones are
  stale, and `friction` reads only current attempts. Variant against
  variant needs nothing new. Before and after a fixture change waits on
  the change report (stage 3 of `exam-variant-attempt.md`); `friction`
  gets no flag of its own to name the stale side, which would be a
  second, partial way to compare across hashes built just ahead of the
  real one (2026-11-22, my recommendation, not objected to).

### Not taken

- **Tokens or dollars as the cost.** Dollars are on about 1% of steps
  in the trajectories kept in auspex; tokens are on nearly all, but
  charging them to a call is a choice (the step's own output, the result
  it added to context, or every later step that re-read it), and a
  choice is a lever to explain. A point keeps `cost_usd` where the agent
  reports it.
- **Weights per label, or one score.** A weight is a guess at which
  friction sinks some other attempt. Enough attempts answer that with
  failures.
- **Severity, as a tier per label** (`outcome`, `cost`, `note`; drafted
  here and dropped by the user, 2026-11-22). Success remains success,
  so no label makes a pass a lesser one. Assigning labels to tiers is
  fiddly and every assignment is "not quite right in this one place":
  `environment` fired in 15 of 15 `agent-onboarding` attempts and
  `off-surface` in 15 of 15, so the word saturated whichever way they
  were placed. And it is harder to explain than a bare count.
- **Share of steps in a point.** See C5.3.

## Not in this plan

- auspex's layout (its `project/` notes and the design canvas).
- A "closest step" for an absent call (say, the `write_file` nearest to
  a missing `save_data`). That's a judgment. If it is ever wanted, it
  comes from a model check that cites steps, not from a rule passed off
  as one.
- Graders checking after each stage (B8 stands).

## Order

1. C1 and C2: small, no layout change, testable now on existing exams.
2. C3 for the transcript kinds and `files_touched`, with step ids as
   they are today.
3. Stage ids in evidence, with bank step 4.
4. Script evidence, once the contract is settled.

Friction (C5) is apart from the checks and can go in any time:

1. C5.1 and C5.2: `rule` and `because` on evidence, labels as objects
   with their sentence, the surface verdict. What the rules flag does not
   change, but labels turning from strings into objects breaks a reader
   of `detector.labels`, so the schema is `friction/2`. auspex is the
   only reader and moves to the bank anyway.
2. C5.3: `flagged_steps`.
3. The same measurement as C5.4's on `agent-onboarding` and
   `nb-changes`, reader points included, before step 4.
4. C5.4: the exam's file and the comparison with the live variant.
