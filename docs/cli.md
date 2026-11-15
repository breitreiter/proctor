# Commands

Every command reads and writes files under the repository that holds your
suites: `suites/` (yours, in git), `runs/` (raw output, gitignored) and
`reports/` (derived, written by `proctor report`).

```bash
proctor setup                 # providers and keys, one job at a time (interactive)
proctor setup --check         # what is set up, in plain lines for you or your coding agent; --live repeats each key check
proctor list [suite]          # validate; print the labels and the cells that would run
proctor list --label kind=bugfix   # only the suites carrying that label
proctor run <suite>           # run every cell into runs/<id>/; prints the id
proctor resume <id>           # rerun cells that did not complete
proctor run <suite> --config suites/proctor.modal.json   # another machine's config, such as Modal's (docs/modal.md); also list, resume, grade
proctor grade <id>            # checks over every completed cell -> checks.json; model verdicts cached in verdicts/
proctor grade <id> --rejudge  # call the judges again instead of reusing the cells' verdict files
proctor grade <id> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <id> --arm a [--tasks x,y]   # only those cells (--arm repeatable); the rest keep their checks.json, so a paid judge pass can be staged
proctor friction <id>         # friction.json beside each completed cell's trajectory: where the run paid a cost the task did not ask for; rules only, no model, no key
proctor friction <id> --arm a [--tasks x,y]   # only those cells
proctor friction <id> --reader haiku   # a chat judge from proctor.json reads each run too: repair, lost, reread, dead-end, workaround; one model call per run, kept in the cell and reused (--rejudge asks again)
proctor report <id>           # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--arm a] [--tasks x,y]   # pin the arm's analysed cells as suites/<suite>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if an arm fell further than that
proctor report <id> --against <earlier>   # each arm against the same arm in an earlier experiment, in place of the baseline
proctor --version
```

A run is a matrix: every arm over every task, several samples each. `run`
prints the experiment's id; `grade` and `report` take it. Grading is
separate from running, and every model verdict is cached beside the
evidence, so a new check or a different judge is a regrade, not a rerun.

## Explore and guard

There are two ways of working.

In **explore** mode several arms run in one experiment and the report
compares them with each other. The verdict card compares each arm with the
first, the reference.

In **guard** mode one arm runs against a baseline: pinned cells per task,
committed with the suite, written by `proctor baseline` from an experiment
you trust. The verdict card then compares every arm with the baseline by
the same paired method, and each task card shows every arm against that
task's pinned score. The verdict is held, improved or regressed on the
point estimate against `--tolerance`, and `--fail-on regression` reads
only that, so it can gate a CI job. A verdict is *confirmed* only when the
whole interval lies beyond the tolerance on the same side (or within it,
for held), so a small task count cannot pass off a guess as a finding.

`--against <experiment>` stands in for the baseline for one report: each
arm is compared with the arm of the same id in that earlier experiment,
and the page says "earlier run" where it said "baseline".

While the pinned experiment is still under `runs/`, its cells are re-read,
so a regrade flows through to the comparison; once it is archived the
pinned scores stand, and the report says which.

## The report

`report.html` is one file that loads nothing from outside: styles, a few
lines of script for the section rail, and the logo are inline. Open it
from disk or attach it to a message; the page reads the same without the
script. `summary.md` has the same content as plain markdown, the verdict
card as a list and a heading per task.

Every rate carries a 95% interval and its counts. Comparisons between arms
are paired on shared tasks with their own interval and a fixed verdict
word. The minimum detectable effect is stated, so an underpowered result
cannot read as "no difference". The methods are named in the report's
last paragraph. No model writes any part of the report; a judge's role
ends at a label and a quote.
