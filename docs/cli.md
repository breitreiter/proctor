# Commands

Every command reads and writes files under the repository that holds your
exams: `exams/` (yours, in git), `attempts/` (raw output, gitignored) and
`reports/` (derived, written by `proctor report`).

## After a crash, run it again

If the machine died, the network dropped or you stopped a batch half made,
the command is the one you started with. There is no resume verb:

```
$ proctor run tern-docs
batch 20261113-0240-tern-docs-icih was interrupted: 21 of 36 attempts finished, 1 was running
  continuing it: 15 attempts to make
  ...
15 made: 15 completed
```

`run` continues an interrupted batch in place while the conditions it was
made under still hold: the runner script, the Harbor version, the Harbor
tasks it compiled, the container engine and nb's image. If one changed, it
says which and makes what is missing in a new batch instead:

```
batch 20261113-0240-tern-docs-icih was interrupted: 21 of 36 attempts finished
  not continuing it: Harbor is 0.25.0 now, 0.24.0 then
batch 20261113-0612-tern-docs-p2xv: 15 attempts to make
```

An attempt is made again only if it never finished: `pending`, `running`
when the batch stopped, or `failed` (the machine's failure: a hook, nb not
starting, Harbor not starting). An attempt that `completed`, whatever the
agent did in it, is a result and is graded. `proctor list <exam>` names an
interrupted batch and the command that continues it.

## Every command

```bash
proctor setup                 # providers and keys, one job at a time (interactive)
proctor setup --check         # what is set up, in plain lines for you or your coding agent; --probe repeats each key check
proctor list [exam]           # validate; per variant, its current attempts against those wanted; stale attempts; an interrupted batch
proctor list --label kind=bugfix   # only the exams carrying that label
proctor run <exam>            # make the attempts the exam lacks, in a new batch under attempts/<batch>/; continues an interrupted batch first
proctor run <exam> --variant a [--tasks x,y]   # only what those variants and tasks lack
proctor run <exam> --config exams/proctor.modal.json   # another machine's config, such as Modal's (docs/modal.md); also list, grade
proctor grade <exam>          # checks over every current, completed attempt -> checks.json; model verdicts cached in verdicts/
proctor grade <exam> --rejudge   # call the judges again instead of reusing the attempts' verdict files
proctor grade <exam> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <exam> --batch <id>     # only the attempts that batch made
proctor grade <exam> --variant a [--tasks x,y]   # only those attempts (--variant repeatable); the rest keep their checks.json, so a paid judge pass can be staged
proctor friction <exam>       # friction.json beside each current, completed attempt's trajectory: where the attempt paid a cost the task did not ask for; rules only, no model, no key
proctor friction <exam> --variant a [--tasks x,y] [--batch <id>]   # only those attempts
proctor friction <exam> --reader haiku   # a chat judge from proctor.json reads each attempt too: repair, lost, reread, dead-end, workaround; one model call per attempt, kept in the attempt and reused (--rejudge asks again)
proctor report <exam>         # reports/<exam>/explore/{results.jsonl,stats.json,report.html,summary.md}, stamped with what they were built from
proctor report <exam> --batch <id> | --variant a | --tasks x,y   # a report over only those attempts
proctor baseline <batch> [--variant a] [--tasks x,y]   # pin the variant's analysed attempts in that batch as exams/<exam>/baseline.json
proctor report <exam> --tolerance 10 --fail-on regression   # guard mode: exit 1 if a variant fell further than that
proctor report <exam> --against <batch>   # each variant against the same variant in an earlier batch, in place of the baseline
proctor --version
```

## What an exam's attempts are

An exam wants `attempts` of each task from every variant (`exam.json`). Its
attempts are every attempt of every batch of it under `attempts/`, whichever
`run` made them. An attempt is **current** while the hash of its variant and
the hash of its task are what they are today; one made before either changed
is **stale**. `run` makes, per variant and task, the exam's `attempts` less
its current, completed ones, numbering each new attempt after the ones
already on disk, so `variant/task/n` names one attempt. Run it again with
nothing missing and it says so and makes nothing:

```
$ proctor run tern-docs
tern-docs: every variant has its 2 attempts of each task; nothing to make
```

So adding a variant makes only its attempts, raising `attempts` makes only
the extra ones, and changing a task makes that task's attempts again; the
rest stand. A changed variant under an id that has made attempts is refused:
give it a new id. Retiring a variant is deleting its file; its attempts stay
on disk, stale.

`grade`, `friction` and `report` take the exam and read its current
attempts; `--batch`, `--variant` and `--tasks` narrow them. Stale attempts
are listed with the hash that moved and never graded, read or counted.
Grading is separate from running, and every model verdict is cached beside
the evidence, so a new check or a different judge is a regrade, not a
rerun (a new check changes the task's hash, though, so the attempts it
would grade are stale: run the exam again).

## Explore and guard

There are two ways of working.

In **explore** mode the report compares several variants with each
other. The verdict card compares each variant with the
first, the reference: the first file under the exam's `variants/` by name.

In **guard** mode one variant runs against a baseline: pinned attempts per task,
committed with the exam, written by `proctor baseline` from a batch
you trust. The verdict card then compares every variant with the baseline by
the same paired method, and each task card shows every variant against that
task's pinned score. The verdict is held, improved or regressed on the
point estimate against `--tolerance`, and `--fail-on regression` reads
only that, so it can gate a CI job. A verdict is *confirmed* only when the
whole interval lies beyond the tolerance on the same side (or within it,
for held), so a small task count cannot pass off a guess as a finding.

`--against <batch>` stands in for the baseline for one report: each
variant is compared with the variant of the same id in that earlier batch,
and the page says "earlier run" where it said "baseline".

While the pinned batch is still under `attempts/`, its attempts are re-read,
so a regrade flows through to the comparison; once it is archived the
pinned scores stand, and the report says which.

## The report

`proctor report <exam>` writes `reports/<exam>/explore/`. A report there
from before is moved into a directory of its own beside it, named by when
it was built (`reports/<exam>/explore/20261115-183450/`), so a report is
never overwritten. Each report says what it was built from: `stats.json`
keeps it as `built_from` (when; the batches; each variant's and task's
hash; how many current attempts it read and how many stale ones it left
out, each with why; the narrowing asked for), and the page's
reproducibility section shows it. When one variant's attempts came from
batches made under different conditions (proctor's, nb's or Harbor's
version, the runner, the engine), the page says so above the numbers.

`report.html` is one file that loads nothing from outside: styles, a few
lines of script for the section rail, and the logo are inline. Open it
from disk or attach it to a message; the page reads the same without the
script. `summary.md` has the same content as plain markdown, the verdict
card as a list and a heading per task.

Every rate carries a 95% interval and its counts. Comparisons between variants
are paired on shared tasks with their own interval and a fixed verdict
word. The minimum detectable effect is stated, so an underpowered result
cannot read as "no difference". The methods are named in the report's
last paragraph. No model writes any part of the report; a judge's role
ends at a label and a quote.
