# Commands

Every command reads and writes files under the repository that holds your
exams: `exams/` and `fixtures/` (yours, in git), `bank/` (every try made,
gitignored), `log/` and `exams-seen/` (what each `proctor run` did and the
exam as it stood, gitignored) and `reports/` (derived, written by
`proctor report`). The words are in
[`configuration.md`](configuration.md#layout-and-vocabulary).

## Every command

```bash
proctor setup                 # providers and keys, one job at a time (interactive)
proctor setup --check         # what is set up, in plain lines for you or your coding agent; --probe repeats each key check
proctor list [exam]           # validate; the exam's axes, and per configuration it names or has tries of, its current tries against those wanted
proctor list --label kind=bugfix   # only the exams carrying that label
proctor run <exam>            # make the tries the exam lacks, of the configurations it names
proctor run <exam> --configuration <name>   # only that named configuration
proctor run <exam> --value docs=rewritten   # that value, with every other axis at its live value or reference
proctor run <exam> --config exams/auspex.other.json   # another machine file in place of exams/auspex.json; also list, grade
proctor build <exam>          # nothing run, no model: what each value changes in the starting workspace, and each configuration against its values alone
proctor build <exam> --value docs=rewritten   # one value's change, with its text
proctor grade <exam>          # the checks over every stage of every current try -> checks.json; model verdicts cached in verdicts/
proctor grade <exam> --rejudge   # call the judges again instead of reusing the verdict files
proctor grade <exam> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <exam> --configuration <name>   # only those tries (also --value); the rest keep their grades, so a paid judge pass can be staged
proctor friction <exam>       # friction.json beside each stage's trajectory: where it paid a cost the stage did not ask for; rules only, no model, no key
proctor friction <exam> --reader haiku   # a chat judge from auspex.json reads each stage too; one model call per stage, kept beside it and reused (--rejudge asks again)
proctor report <exam>         # reports/<exam>/explore/{results.jsonl,stats.json,friction.json,summary.md}, stamped with what they were built from
proctor compare <exam> docs=rewritten   # JSON: that value against its axis's live value (--against <value>), every other axis held
proctor compare <exam> --configuration <name>   # JSON: everything that configuration changes, together, against what is live (--against <name>)
proctor coverage <exam>       # JSON: the tries of each value and configuration, and where there are none
proctor predict <exam> [name] # pin a prediction: record the checks it was made under
proctor retire <exam> <configuration> --reason "..."   # retire a named configuration (exams/<exam>/retired.json; commit it). Nothing is deleted; run makes no new tries of it
proctor retire <exam> <configuration> --bring-back     # undo that
proctor retire <exam>         # which configurations are retired, since when and why; --json on any form answers as JSON (retire/1)
proctor --version
```

`--value` and `--configuration` narrow `grade`, `friction` and `report`
to the configurations the exam carries that hold them. `compare` and
`coverage` also take `--as-of <time>`, the answer as it stood then, and
`compare` takes `--within axis=value`, only the settings of the other
axes that hold that value.

## What an exam's tries are

An exam says how many tries it wants of each configuration it names
(`tries` in `exam.json`). Its tries are its bank: every try under `bank/<exam>/`,
whenever it was made. One made last week counts as one made today. A try
is **current** while its configuration's values and the exam's stages
are what they were when it was made; one made before either changed is
**stale**. `run` first says where each configuration stands, and what it
is about to make:

```
$ proctor run tern-docs
tern-docs: one job of 6 stages, 4 attempts wanted
  docs=as-shipped,assistant=sonnet   4 of 4 attempts
  docs=restructure,assistant=sonnet  2 of 4 attempts   making 2
  docs=none,assistant=sonnet         4 of 4 attempts
run 20261118-0912-tern-docs-k20g: 2 attempts to make
  docs=restructure,assistant=sonnet/3/quote-at-checkout  completed  ok
  ...
  docs=restructure,assistant=sonnet/3  whole  412.6s
2 attempts made: 2 whole
```

Run it again with nothing missing and it ends `nothing to make`.

A try is every stage that applies in its configuration, once, in one
workspace. It is kept under an id of its own,
`bank/<exam>/<try id>/stages/<i>-<stage>/`, and its manifests say which
configuration it ran as; its number (`/3/` above) is its place among
that configuration's tries. A try comes out one of three ways:

- **whole**: every stage completed, whatever the agent did in it.
- **broke** (a chained exam only): a stage was stopped by a limit, so
  the stages after it were not reached. It still counts.
- **lost**: a stage did not run (a hook failed, nb or Harbor did not
  start, the provider failed). None of it counts, and it is in no
  number.

**After a crash, run it again.** A try is never taken up part way, since
its workspace is half way through: one that was lost or never finished
is made again, whole, under the next number, by the same command.

So naming a configuration makes only its tries, raising `tries`
makes only the extra ones, and changing a stage makes every try again.
A changed value under a name that has made tries is refused: give it a
new name. A configuration no longer wanted is retired (below); its
tries stay.

`grade`, `friction` and `report` read the current tries. Stale ones are
listed with what moved and never graded, read or counted. Grading is
separate from running, and every model verdict is cached beside the
evidence, so a new check or a different judge is a regrade, not a
rerun. One exception: through Harbor a script check runs in Harbor's
verifier when the try is made, so its verdict is fixed then. Change the
script, a file beside it, its expected values, or add a script check,
and `grade` marks those verdicts `outdated`: out of every number, never
a fail, named in a line after the grade summary. They stay outdated
until the tries are made again: move their directories out of
`bank/<exam>/` (`grade` names them) and `proctor run` the exam.

After the stages, `grade` says per check how many of them it has a pass
or fail for, of those the check applies to:

```
  completes    graded 24 of 24
  cites-docs   graded 24 of 24   8 new
  acceptance   graded 16 of 24   8 cannot be regraded from what was kept
  tone         graded 0 of 24   24 need a judge
```

`new` counts the stages the check had no pass or fail for before this
grade: a check just added reads all new. `cannot be regraded from what
was kept` is the outdated script check, above.

## What is live

What is live is declared on an axis in `exam.json`, `"live":
"<value>"`: the value in use now. There is no command for it; it is
part of the exam, committed with it. The configuration at the live
value of every axis that has one is the live configuration, and `list`
marks it.

Every other value of an axis with a live value is compared with the
live one, with every other axis held: try against try, on the
correctness checks each try held and on its flagged steps, by the
Mann–Whitney test with an exact p. **Plan for at least 4 tries of each
configuration**, or 3 against 5: fewer can show a difference and never
confirm one, and `run` says so. One try is fine as a shakedown, for
setup problems and plain failures.

Where the same comparison can be made in several settings of the other
axes (in each repository, say), each is shown, and they are also read
as one, a try ranked only against tries in its own setting. Where they
point different ways the answer is "better here, worse there" and they
are not read as one.

With nothing live, nothing is compared: the report lists the
configurations and their numbers.

## Asking a question

`compare` and `coverage` answer as JSON (`"schema": "answer/1"`), for an
agent that is answering a person. `compare` says what was found, in
which settings, what was not run, when the tries were made, what so few
tries could have shown, and any caution that goes with the answer;
`coverage` says where the exam has tries and where it has none. Neither
recommends anything. The skill for an agent that uses them is
[`skills/proctor-answers/`](../skills/proctor-answers/SKILL.md).

A comparison that would read a try not yet graded under the checks as
they are now is refused, so both sides are always under one standard.

## Retiring a configuration

The named configurations are what a reader looks at first: what is
shipping, what might replace it, how that is going. One nobody is
looking at any more can be retired, with the reason:

```
$ proctor retire tern-docs restructure --reason "we kept the docs as shipped"
exams/tern-docs/retired.json: restructure is retired. Its tries, predictions and comparisons stay; `run` makes no new tries of it. `proctor retire tern-docs restructure --bring-back` undoes this
```

```json
{
  "schema": "retired/1",
  "configurations": {
    "restructure": { "reason": "we kept the docs as shipped", "since": "2026-11-27" }
  }
}
```

Commit the file with the exam. Retiring deletes nothing: the tries stay in
the bank, the predictions keep their outcomes, and `compare` still answers
about the configuration. What changes is that `proctor run` makes no new
tries of it (and refuses one asked for with `--configuration` or
`--value`), `list` and the report mark it, and the viewer lists it under
"retired" with its reason, out of the main lists.

The live configuration cannot be retired: make another value live first.
`--bring-back` removes the mark, and the next `run` makes whatever tries
it lacks.

With `--json`, each form answers in JSON in place of the sentence: what it
did (`listed`, `retired`, `reason_replaced` or `brought_back`), the mark
that was there before as `was` (when there was one), and every
configuration retired now, `named` saying whether `exam.json` still names
it.

```
$ proctor retire tern-docs restructure --reason "we kept the docs as shipped" --json
{
  "schema": "retire/1",
  "exam": "tern-docs",
  "file": "exams/tern-docs/retired.json",
  "did": "retired",
  "configuration": "restructure",
  "retired": {
    "restructure": { "reason": "we kept the docs as shipped", "since": "2026-11-27", "named": true }
  }
}
```

It exits 0 when it did what was asked, and 1 with the reason on stderr and
nothing changed when it is refused.

## The report

`proctor report <exam>` writes `reports/<exam>/explore/`. A report there
from before is moved into a directory of its own beside it, named by when
it was built (`reports/<exam>/explore/20261115-183450/`), so a report is
never overwritten.

- `stats.json` (`"schema": "configurations/1"`): the axes, the stages,
  each configuration the exam names or has tries of with its tries, each
  value against its axis's live one, the tries that were lost, how each
  prediction came out, the judges whose verdicts the tries hold, and
  `built_from`: when it was built, the runs that made its tries, how
  many current stages it read and how many stale ones it left out, each
  with why, and any narrowing asked for.
- `results.jsonl`: a row per stage of every try read.
- `friction.json`: what recurs across each configuration's tries.
- `summary.md`: the same as plain markdown, for a person or an agent.

When one configuration's tries came from runs made under different
conditions (proctor's, nb's or Harbor's version, the runner, the
engine), `report` says so.

Each try gives two numbers: the correctness checks it held, of all that
apply in its configuration (a stage a chained try did not reach holds
nothing), and its flagged steps. A configuration with no counted try is
not run, never "no change". A try with a stage that failed an integrity
check is listed and not counted. The method is named in the report's
last paragraph. No model writes any part of it; a judge's role ends at a
label and a quote. To read tries, transcripts and comparisons on a page,
start the viewer (`dotnet View/bin/Debug/net10.0/auspex-view.dll <root>`, which prints the address to open).

In those files, and in a try's `manifest.json` and a run's `entry.json`,
a configuration's key is the field `configuration` and a stage is
`stage`; in a manifest `values` is the value on each axis and `place` is
where the stage comes in its try. Tries written before 2026-11-28, which
said `variant` and `task`, are not read: `list` counts them and the
viewer lists them as earlier attempts.
