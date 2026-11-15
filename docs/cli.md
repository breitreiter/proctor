# Commands

Every command reads and writes files under the repository that holds your
exams: `exams/` (yours, in git), `attempts/` (raw output, gitignored) and
`reports/` (derived, written by `proctor report`).

```bash
proctor setup                 # providers and keys, one job at a time (interactive)
proctor setup --check         # what is set up, in plain lines for you or your coding agent; --probe repeats each key check
proctor list [exam]           # validate; print the labels and the attempts that would run
proctor list --label kind=bugfix   # only the exams carrying that label
proctor run <exam>            # make every attempt into attempts/<batch>/; prints "batch <id>"
proctor resume <id>           # make again the attempts that did not complete
proctor run <exam> --config exams/proctor.modal.json   # another machine's config, such as Modal's (docs/modal.md); also list, resume, grade
proctor grade <id>            # checks over every completed attempt -> checks.json; model verdicts cached in verdicts/
proctor grade <id> --rejudge  # call the judges again instead of reusing the attempts' verdict files
proctor grade <id> --judge glm=k2   # compare k2 against glm on the checks glm grades; checks.json is untouched
proctor grade <id> --variant a [--tasks x,y]   # only those attempts (--variant repeatable); the rest keep their checks.json, so a paid judge pass can be staged
proctor friction <id>         # friction.json beside each completed attempt's trajectory: where the attempt paid a cost the task did not ask for; rules only, no model, no key
proctor friction <id> --variant a [--tasks x,y]   # only those attempts
proctor friction <id> --reader haiku   # a chat judge from proctor.json reads each attempt too: repair, lost, reread, dead-end, workaround; one model call per attempt, kept in the attempt and reused (--rejudge asks again)
proctor report <id>           # reports/data/<id>/{results.jsonl,stats.json,report.html,summary.md}
proctor baseline <id> [--variant a] [--tasks x,y]   # pin the variant's analysed attempts as exams/<exam>/baseline.json
proctor report <id> --tolerance 10 --fail-on regression   # guard mode: exit 1 if a variant fell further than that
proctor report <id> --against <earlier>   # each variant against the same variant in an earlier batch, in place of the baseline
proctor --version
```

A batch is a matrix: every variant over every task, several attempts each. `run`
prints the batch's id; `grade` and `report` take it. Grading is
separate from running, and every model verdict is cached beside the
evidence, so a new check or a different judge is a regrade, not a rerun.

## Explore and guard

There are two ways of working.

In **explore** mode several variants run in one batch and the report
compares them with each other. The verdict card compares each variant with the
first, the reference.

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
