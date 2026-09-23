# Changelog

Proctor's version is `<Version>` in `Proctor.csproj`; `proctor --version`
prints it and every `experiment.json` records it under `versions.proctor`.
Before 1.0 a minor bump means a breaking change to the layout, the file
formats or the vocabulary; a patch bump does not.

## 0.2.0 — 2026-09-23

The vocabulary and the layout, renamed to suite / task / sample / check
(`project/plans/suite-task-check.md`). Breaking:

- `evals/` is `suites/`, `eval.json` is `suite.json`, `cases/` is `tasks/`;
  `evals/eval-authoring` is `suites/suite-authoring`.
- `experiment.json`: `eval`, `eval_hash`, `eval_def`, `cases` are `suite`,
  `suite_hash`, `suite_def`, `tasks`. `manifest.json`: `case` and `eval_hash`
  are `task` and `suite_hash`. `baseline.json`: `eval_hash` and `cases` are
  `suite_hash` and `tasks`. `results.jsonl` rows say `task`; `stats.json`
  says `suite`, `tasks`, `n_tasks` and gains `task_details`.
- Hooks and script checks get `PROCTOR_SUITE_DIR`, `PROCTOR_TASK` and
  `PROCTOR_TASK_JSON`; the program template placeholder is `{{task}}`;
  `baseline --cases` is `--tasks`.
- Every reader accepts the 0.1 spellings for this release only: the
  directory and file names, the JSON keys under `runs/` and in a baseline,
  the old environment names beside the new, and `{{case}}`. Writers use
  the new ones. 0.3 drops the old ones.

Added:

- A task file may declare `checks`, its own beyond the suite's and its
  fixture's; a cell carries the union, and a name at two levels is a problem.
- `@expect` on a built-in field reads `expect.<check>.<field>` before
  `expect.<field>`; a model check's `ask` may be `"@expect"`, read from
  `expect.<check>.ask`.
- The report's Tasks table shows each task's fixture and its own checks;
  the Checks table says which tasks carry each check.

## 0.1.0 — 2026-09-22

The first slice: `list`, `run`, `resume`, `grade`, `report`, `baseline`;
fixtures, arm bundles and baselines; containerised runs; the `decide` and
`judge` model checks; the report rebuilt for a reader who was not there.
