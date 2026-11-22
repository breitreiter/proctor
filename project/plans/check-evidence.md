---
type: plan
title: Checks that explain themselves — why a check exists, what it does, and what it points at
created: 2026-11-22
status: proposed 2026-11-22 from auspex's stage page design, not agreed. Lines marked OPEN are mine. Depends on bank step 4 (stages) only for stage ids; C1 and C2 can be built now
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
