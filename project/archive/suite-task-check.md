---
type: plan
title: Suite, task, sample, check — the vocabulary drawn as bright lines, and the criteria moved onto the task
created: 2026-09-23
status: built 2026-09-23 as written; readers accept the old spellings for one release; the suite-authoring bundle rev and its checked-in report still predate the rename
---

# Suite, task, sample, check

The first live consumer of proctor outside this repository is the company's
design-system site. Asked to "write an eval" for it, a coding agent wrote one
eval called "can coding agents use the design system" and wedged every use
case it could think of into it as cases. Nothing in proctor stopped it, and
nothing in proctor's words told it that it had built the wrong thing. The
word "eval" invited a single test; the model then let cases differ in every
way except the one that matters, the criteria, which a case cannot carry.

This plan does two things that turn out to be one thing. It fixes the
vocabulary so the concepts are mutually exclusive and jointly cover what
proctor does, and it moves the success criteria onto the task, because the
word "task" is a lie without them.

The reference point is Anthropic's
[Demystifying evals for AI agents](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents),
read on 2026-09-23. Its taxonomy maps onto proctor almost one to one; where
it does not, the article is right.

## The vocabulary

| word | one sentence | the article's word | was |
|---|---|---|---|
| **suite** | A collection of related tasks with one business goal, graded by one shared checklist so a pass rate across them means one thing. | evaluation suite | eval |
| **task** | One input and one desired outcome: a prompt against a fixture, with the checks that say whether the outcome was reached. | task | case |
| **sample** | One measurement of a stochastic system: one attempt at one task by one arm. | trial | sample |
| **check** | One yes-or-no item on the checklist, asked of a finished sample. Its role in the headline is set by where it is listed. | grader | check |
| **arm** | What is under test: a harness, a provider, a model and a bundle, run over every task. | agent harness + configuration | arm |
| **fixture** | The repository a task runs against and what "done" looks like in it; reused across suites. | environment | fixture |
| **experiment** | One execution of one suite across every arm, task and sample. | eval run | experiment |
| **cell** | One arm, one task, one sample: the directory a sample's evidence lives in. | (transcript + outcome) | cell |
| **baseline** | Pinned cells from an earlier experiment, compared with as a virtual arm. | regression eval | baseline |

Three words are deliberately not the article's:

- **suite**, not eval, because the collection is what the directory holds.
  "Can an agent use our documentation" is not one test; it is many tasks
  with one goal. The plural word sets the expectation that there will be
  several tasks and that they share something.
- **sample**, not trial, because sample says why there are several: the
  system is stochastic and this is one measurement of it. Every interval
  in the report is over samples, and the word should say so.
- **check**, not grader, because a grader suggests a system that weighs
  many things and hands back one grade. Proctor's report is a checklist:
  which items were met, how often, by which arm. There is no composite
  score, and the word should not promise one.

The report keeps **run** for a sample on the page, as
[report-structure.md](report-structure.md) chose for a reader who was not
there; a run is defined there as one sample. Whether the page should say
sample instead is an open question at the end.

## The bright lines

The concepts are only useful if an author, human or agent, can decide which
one they are holding. These are the tests.

**One nb process per cell.** Every sample is its own checkout of the task's
fixture, its own setup hook, its own transcript. Nothing is shared between
tasks at run time. A suite is, mechanically, a loop over tasks and arms
that runs nb once per sample and grades each transcript with the checklist.

**A lens is a check, never a suite.** If the same transcripts can answer a
question, that question is a check. Splitting a suite to ask a second
question of the same tasks pays for every transcript twice. "Does the agent
refuse out-of-scope requests" is a check when in-scope and out-of-scope
prompts sit in one task set; it is a `stance` check with `expect: "@expect"`
in the pass list, and a balanced problem set as the article asks for.

**A suite splits when the inputs differ or the pass list lies.** If probing
a behaviour needs its own prompt set, that set is its own suite and costs
its own transcripts. If a task cannot be honestly graded by the shared pass
list, the suite is two suites. The tell is in the checks: a script that
branches on `PROCTOR_CASE`, a check that passes vacuously on half the tasks,
or a `pass` list that means something different per task. The task count
is never the tell. The three tasks of `evals/code-change` differ in fixture,
prompt and acceptance tests and are one suite, because "it builds, its
tests pass, the acceptance tests pass, the diff stayed in scope" is honest
on all three.

**The prompt is what the user wants; the checks are how it is measured.**
"Book me a flight from AUS to DEN next Tuesday morning, aisle seat, minimal
layovers" is the prompt, in the words a user would use. Departure before
noon, an aisle assignment, at most one stop, a booking that exists in the
environment: those are the task's checks, parameterised from its `expect`.
Rewriting the prompt as a spec would change what is under test, which is
the article's warning against over-specification. The prompt may be fuzzy;
the checks may not. "Two domain experts would reach the same verdict" is a
standard on the checks, and it can only be met if the task states them.

**Checks live at three levels, and a sample carries the union.**

| level | declared in | applies to | for |
|---|---|---|---|
| suite | `grading.checks` | every task | what the whole checklist shares: exit ok, no denials, stance, the global bad-behaviour judge |
| fixture | `fixture.json` `checks` | every task on that fixture | what the repository knows: how to build it, how to run its tests |
| task | the task file's `checks` | that task | the desired outcome: acceptance, the specific tool call, the rubric for this prompt |

A name declared at two levels is a problem, as fixture-versus-suite already
is. A check's rate is over the samples that carry it, which `Stats.cs`
already does by name; a task-level check therefore reports over one task,
and a check two tasks both need is declared on both or lifted to the suite
with `@expect`.

**The headline is the suite's.** `pass` and `validity` name checks that
every task carries, from whichever level. The existing rule, a pass check
the suite does not declare must come from every task's fixture, extends
to "or from every task's own block". A task-only check that is not in
`pass` is a guardrail rate over that task, which is what a per-task
outcome check that is not gating looks like.

## The model changes

Four changes to the model, in the order they should land. The first two are
the substance; the last two are the words.

### 1. A task carries checks

The task file gains a `checks` block with the same shape as a fixture's.
Its scripts resolve against the suite directory, like the suite's own,
since a task has no directory of its own. `Eval.ChecksFor` becomes suite,
then fixture, then task; `CheckNames`, `CheckDescriptions` and `Judged`
read the task level too; validation reports a clash at any two levels and
extends the pass-list rule.

```json
{
  "id": "make-bat-implement-ifoo",
  "description": "Adapt an existing class to a new interface without changing its callers",
  "fixture": "bat-service",
  "prompt": "Make `Bat` implement `IFoo`. Its existing callers must not change.",
  "expect": {
    "files_touched": { "paths": ["src/Bat.cs", "tests/**"], "mode": "at_most" }
  },
  "checks": {
    "callers-untouched": {
      "not_files_touched": { "paths": ["src/Consumers/**"], "mode": "at_least" },
      "description": "no file under src/Consumers was changed"
    },
    "implements-ifoo": {
      "judge": {
        "ask": "Does the diff make Bat implement every member of IFoo, rather than a subset or a wrapper type?",
        "window": "prompt+diff",
        "samples": 3,
        "with": "glm"
      },
      "description": "Bat itself implements the whole interface"
    }
  }
}
```

The `acceptance.sh` pattern, one suite script that finds
`acceptance/$PROCTOR_CASE`, stays valid: it is a suite check whose input
is per task. A task block is for the check that only this task can state.

### 2. `@expect` reaches the ask, and is keyed by check

Two limits found on 2026-09-23:

- For a model check, only `expect` can come from the task; the `ask`
  cannot. A per-task rubric under a shared check name has no expression.
- For a built-in, `@expect` reads `expect.<field>`, so two checks over the
  same field cannot be parameterised differently in one suite.

Both resolve the same way: `expect.<check>` is looked up first, `expect.<field>`
second. For a model check, `expect.<check>` may be a string (the expectation,
as today) or an object whose keys are the fields that said `"@expect"`:

```json
"grading": {
  "checks": {
    "correct": {
      "judge": { "ask": "@expect", "window": "prompt+answer", "samples": 3, "with": "glm" },
      "description": "the reply is correct for what this task asked"
    }
  }
}
```

```json
"expect": {
  "correct": { "ask": "Does the reply name the three required fields of a Widget and nothing else?" }
}
```

The verdict hash already covers the compiled request, so a per-task ask
gets a per-task verdict file. A task without the value stays `error`, not
"does not apply": with a task-level block available, absence is an
authoring mistake and should say so.

### 3. Rename on disk

| was | becomes |
|---|---|
| `evals/<id>/` | `suites/<id>/` |
| `eval.json` | `suite.json` |
| `cases/` | `tasks/` |
| `grading.checks` etc. | unchanged |
| `eval_hash`, `eval_id` in manifests and results | `suite_hash`, `suite_id` |
| `PROCTOR_EVAL_DIR`, `PROCTOR_CASE`, `PROCTOR_CASE_JSON` | `PROCTOR_SUITE_DIR`, `PROCTOR_TASK`, `PROCTOR_TASK_JSON` |
| `{{case}}` | `{{task}}` |
| `Layout.EvalsDir`, `EvalFile`, `CasesDir` | `SuitesDir`, `SuiteFile`, `TasksDir` |

Readers accept both spellings for one release, since `report` and
`baseline` re-read pinned cells under `runs/` and a baseline pins cells
that were written with the old keys. Writers write the new ones. Hooks
and scripts get both environment names for the same release, then the old
ones go. `proctor.json` moves with the directory it sits in.

Counts on 2026-09-23, for scale: eval, case and sample each appear about
300 times in code, 400 in `project/` and the READMEs, 200 in tests. This
is a mechanical pass, not a design one, and it should be its own commit
after 1 and 2 so the diff that changes behaviour is readable.

### 4. Rename in the page and the prose

`report-structure.md`'s four terms become suite (introduced once, in the
about table and the opening sentence), task, run and check. The brief,
`CLAUDE.md`, the README and `on-disk-layout.md` get the vocabulary table
above with the article's column, so an agent reading the repository sees
"a suite is a collection of tasks; a task states its own success criteria;
the suite's checks are the ones every task shares" before it writes one.

The authoring guidance that goes with it, in the README where someone
writing their first suite will read it:

> A suite is one business goal probed by many tasks. Every task in it is
> graded by the same pass list. If you find yourself wanting a different
> pass list for one task, or a check that only makes sense on some tasks
> and passes vacuously on the rest, you have two suites. If you want to
> ask a second question of the same transcripts, you have a new check,
> not a new suite.

## The design-system suite, redrawn

The monolith had one description, "can coding agents use the design
system", and tasks of at least three kinds: find the right page for a
component, build a component from the docs, answer a question about a
token. Under the lines above:

- **build-a-component** is one suite. Tasks differ in component and
  fixture; the pass list is the same: it builds, it uses tokens rather
  than raw values, the rendered result matches the reference, the diff
  stayed in the component's directory. Each task's `checks` block holds
  the reference comparison for that component.
- **answer-a-question** is another suite, single-turn, graded by a
  `correct` judge whose ask comes from the task. Its transcripts are
  cheap and its pass list has nothing to do with building.
- **find-the-page** is a check, not a suite: a `tool_args: "@expect"`
  over the build-a-component transcripts, since the agent has to find
  the page on the way to building. If a dedicated navigation task set is
  wanted later, that is a third suite.

Three reports, each with a goal a reader can hold, each held or regressed
against its own baseline. "Is the feature ready" is the list of three,
which is the cross-suite view that does not exist yet (below).

## Out of scope, and why it is listed

- **A per-label slice of the report.** Tasks carry labels and every result
  row records them, but `Stats.cs` never groups by one. A per-label rate
  within a suite would let closely related task kinds stay in one suite
  and still read apart. It is the cheaper alternative to a split when the
  pass list is honest across both, and it is one grouping over the case
  means that already exist. Its own plan.
- **pass@k and pass^k.** A task is scored as its mean over samples, the
  expected per-sample pass, which is neither of the article's two. Both
  are computable from `results.jsonl`; pass^k is what a "ready to ship"
  gate wants. Its own plan, in `Stats.cs`.
- **A page across suites.** A feature is several suites sharing arms and
  fixtures; only `list` sees across them. Its own plan.
- **Command arms.** An arm whose harness is the customer's agent rather
  than nb. The transcript windows are defined over nb's JSONL, so this
  needs an adapter as well as a launcher. Validation says "not yet" and
  keeps saying so.

## Order of work

1. Task-level `checks`: `Eval.cs` (record, `ChecksFor`, `CheckNames`,
   `CheckDescriptions`, `Judged`, validation), `Grade.cs` (evaluate the
   union, scripts against the suite directory), tests over a task with a
   check the others lack, snapshot the report with a one-task check row.
2. `@expect` by check name and into the ask: `Checks.cs`, `Judge.cs`,
   `Window.cs` if the ask compiles there; tests for a shared judge with two
   different asks and for two built-ins over one field.
3. The on-disk rename with dual readers; the smoke, code-change and
   eval-authoring suites and their fixtures move; the checked-in
   eval-authoring report regenerates.
4. The prose: `report-structure.md`'s term table, the brief, `CLAUDE.md`,
   README, `on-disk-layout.md`, and this plan's status.

## Open questions

- **Sample or run on the page.** The report says run so that a reader
  who was not there has one word for one attempt. Sample is the word
  that says why there are several. One of them should go from the page,
  and the choice is the reader's, not the code's.
- **Should `expect` fold into `checks`?** With a task-level block, `expect`
  is the parameters for shared checks and `checks` is the task's own. Two
  blocks with one purpose, "what this task expects", may be one too many.
  Left as two here because `expect` is also what hooks and scripts read
  as `PROCTOR_EXPECT`, and a merge changes that contract.
- **Does a task-level script need its own directory?** Today it resolves
  against the suite directory, which is where `acceptance/<task>/`
  already is. A `tasks/<id>/` directory per task would hold the script
  and the acceptance tests beside the task file, at the cost of one more
  layout change. Decide when the first task-level script is written.
