# In-house prior art: what we kept rebuilding

## nb `evals/oracle-bench/` (Sept 2026)

Shape: `run.sh --provider <judge> --n 5 --case GLOB --variant LABEL`. Per case
(`cases/<name>.md`, frontmatter `sheet:`/`expect:` then `---` then the message),
generates an nb program into `out/<stamp>-<model>-<variant>/runs/<case>.nb`,
runs it N times, writes `<case>.<i>.jsonl` + `.stderr`.

Extraction, all `jq` on the JSONL: `exit_reason` from the trailer, oracle `keys`
from the `user` event with `source=="oracle"`, `usage.output`, `oracle_verdict`.
Verdict is a 4-way classification (hit/miss/done/error) compared to `expect`;
hits compare as id sets; `|` alternatives.

Outputs: `results.jsonl` (one row per sample, every field the run produced
plus `judge`, `model`, `variant` so runs across invocations can be joined),
`table.tsv` (one row per case), `summary.md` built by awk from the tsv: a
per-case markdown table plus two headline numbers ("overall pass" and
"done-on-waiting", the dangerous one) plus median time and mean tokens.
Hermetic `config.json` (0600, may carry keys) and empty `mcp.json` written
beside results.

Lessons:
- The variant label riding on every row is what makes A/B joins possible.
- The summary names the ONE number that is dangerous (done-on-waiting) and
  explains why in the same line. That is a report convention worth keeping.
- All extraction is a handful of jq one-liners on known fields; nobody loads
  the whole log.
- The config snapshot beside results is the reproducibility record, but the
  nb binary version and the prompt code version (`OracleResolver.BuildPrompt`
  is code) are NOT recorded; the `--variant` label is a human promise.

## weaver `evals/` (July 2026, design in `weaver/project/plans/agent-evals.md`)

Shape: harness × model matrix over 10 "rungs" of ramping difficulty. Each rung
is a dir with `prompt.md` + `grade.sh` (+ `rubric.md` + `pass.jq` for judged
rungs). `run.sh --harness claude|codex|nb --model X --rungs 1,2 -n 3`.

Runner: composes `orientation.md` + rung prompt; drives the harness in a
throwaway sandbox (bwrap read-jail for nb and codex, tool allowlist for
claude); captures the transcript as text; extracts the LAST fenced JSON block
as the answer (`valid_answer` recorded separately from correctness); runs the
deterministic grader; grader exit code 0=pass, 1=fail, 2=needs-judge.

Result row per run: `{rung, harness, model, run, valid_answer, status,
pass|null, wall_secs, reason, answer, transcript_path}` appended to
`runs/results-<stamp>.jsonl`. Transcript file named
`<rung>__<harness>__<model>__run<N>.txt`.

Judge: separate phase (`judge-pass.sh results.jsonl` -> `.judged.jsonl`).
Off-distribution model (GLM-5.2 on Cloudflare, no GLM in the worker pool).
Judge prompt: fixed system prompt; per-rung rubric of named criteria; judge
returns ONLY `{criteria: {key: bool}, notes: "<one sentence>"}`; a
deterministic `pass.jq` applies the threshold. Judge sees the answer JSON in
full and only the tail 40 KB of the transcript. Retries with hard timeout;
rows that error become `judge-error` and are re-judged on the next pass.

Lessons (from the plan's Design principles and Decisions):
- "The interesting output is highest rung reliably passed, not a raw count."
  A gradient is more legible than a score.
- Same prompt for every harness; floor differs only in model ability.
- Format compliance (`valid_answer`) recorded separately from correctness.
- Deterministic first; hybrid gate-then-judge only where prose must be judged
  (4 of 10). Judge emits booleans, never the verdict; the pass bar is code.
- Judge is off-distribution from every worker (conflict of interest).
- Judge never sees ground truth; rubric is self-contained.
- N=3 per pair, pass at 2/3. Cheap; nondeterminism acknowledged.
- "Eval runner is thin bash, not a framework": inspect-ai/promptfoo rejected
  because the hard part (per-harness sandbox invocation) is custom either way
  and a framework adds a viewer plus an install step.
- Everything committed, isolation at runtime, so graders and rubrics can live
  in the public tree beside the code (the sidecar shape).
- Judge daily cap became a scheduling constraint (36 calls > 20/day).
- Calibration pass against the live CLI pinned expected values BEFORE the
  matrix ran and found two prompt/CLI mismatches. Expected values are data.

What both rebuilt by hand, i.e. the framework proctor is:
1. an arm loop (N × cases × arms) that names outputs by their coordinates;
2. a results.jsonl row schema with the coordinates on every row;
3. jq extraction of a few trailer/answer fields;
4. a pass/fail classification with a `needs-judge` third state;
5. a separate, re-runnable, constrained judge phase;
6. an awk/jq summary table with one or two named headline numbers;
7. a hermetic config snapshot beside the results.

What neither has: any interval or uncertainty on the pass rates; a
cross-arm comparison table (weaver's summary is per-rung status strings; the
oracle bench is single-arm per invocation and joins are left to the reader);
code-version capture; a stable name for the experiment as a whole; resume of
a partially failed matrix (oracle bench: none; weaver: judge phase only).
