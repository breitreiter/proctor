---
type: learnings
title: What unit-test frameworks lend an eval runner, and what they do not
created: 2026-09-17
---

# What unit-test frameworks lend an eval runner, and what they do not

A reading pass over xUnit v3, NUnit, MSTest, pytest, JUnit 5, Go and Rust test
harnesses, the snapshot-testing tools (Verify, ApprovalTests, insta), the
flaky-test conventions (Surefire, Develocity, Buildkite, Google), and the
property-based testers (FsCheck, Hypothesis). Raw findings with URLs in
`prior-art/unittest.md`. The eval tools that straddle both worlds (Inspect,
DeepEval, promptfoo) were re-read for how they cope.

## The mismatch, stated once

A unit test is deterministic, binary, and individually meaningful. An eval run
is one sample from a distribution, and the unit of meaning is a rate with an
interval, compared across arms. Every unit-test convention assumes the first
model. The tools that try to be both rediscover the same fact: per-sample
pass/fail has to be aggregated to a rate before any gate is applied, and the
CI interchange formats cannot carry a rate.

- Inspect is the most honest about it. Epochs plus a reducer fold repeats into
  one per-sample score before metrics, and `inspect score` re-scores a stored
  log without re-sampling, which is exactly the separate judge phase in our
  brief. It stops at cross-arm comparison and leaves that to notebooks.
- DeepEval takes pytest literally, asserts a threshold per metric, and then has
  to add a flaky flag to turn failures into warnings. Its own docs admit cases
  near the threshold flip between runs.
- promptfoo has repeat and JUnit output, but the JUnit projection flattens
  repeats to one pass/fail per case and arm, and its own CI docs tell you to
  compute the rate from the JSON instead.

So: borrow the shape of the runner, the identity scheme, the lifecycle, and
the review loop. Do not borrow the pass/fail semantics.

## Borrow

**A stable id separate from the display name, as a slash path.** JUnit 5's
UniqueId, Go's subtest paths, and pytest node ids all converge on
`file/case/arm/sample`. Every case declares its own `id`; xUnit v3 went as far
as a `.uniqueid` file to make identity survive a move. Reject positional
invocation indices, which change when a row is inserted above.

**One eval file is one behaviour, with cases in a data file beside it.**
JUnit's `@CsvFileSource` is the model. Rows carry metadata: id, display, tags,
skip, timeout. Cases by arms is a cartesian product with a compound id, the
way pytest stacks parametrize decorators.

**Arm-scoped async fixtures with reverse-order teardown.** xUnit's collection
fixture with `IAsyncLifetime` and pytest's yield fixtures are the model for
"start the MCP server once per arm". The lifecycle levels are run, arm, case,
sample. Per-sample state must be fresh, or samples are not independent. Be
explicit that an arm fixture runs once per arm per run; xUnit v3 initialises a
fixture even when every test is skipped and pytest's session scope is
ambiguous under parallel workers, and both caused real confusion.

**Skip, explicit, and expected-failure, reinterpreted.** `skip(reason)` and
explicit opt-in for expensive cases transfer directly. `xfail(strict)` becomes
a rate expectation: a known-bad baseline, where strict flags an unexpected
improvement as loudly as a regression.

**Three outcomes and every attempt kept.** Surefire's `flakyFailure` and
`rerunFailure`, the CTRF `flaky` and `retries` fields, Develocity's FLAKY
outcome, and Playwright all report "passed on retry" as its own thing. Retry
only infrastructure exit reasons (provider error, rate limit), with a cap on
retries per run. Google's rule: reruns only for tests already marked flaky, and
quarantine means run but do not gate.

**Record, review, approve, applied to the deterministic parts.** This is the
most useful borrow. Verify and insta store a `.received` beside a `.verified`
file, show a diff, and accept from a terminal. For proctor the things worth
snapshotting are not model outputs but: judge verdicts per stored transcript,
as calibration snapshots that get re-judged and diffed when the rubric or
judge model changes; the deterministic-check expectations; and the report
output itself. Verify's naming, with a unique-for axis, maps onto judge model
and rubric version. A failure message is criterion, verdict, evidence span by
turn and tool index, and the rubric text.

**Reporting as projections from one source of truth.** Proctor's own JSON is
authoritative. Go's test2json-style event stream is the model for streaming
and resume. JUnit XML and CTRF are projections where the test case is a case
by arm pair and the rate, interval, n, and delta ride in properties. Markdown
job summary and `::error` annotations at the eval-file line for regressions;
Microsoft.Testing.Platform's `--report-gh` is the reference. Skip TRX unless
someone asks.

**Runner CLI.** `list` without running, with a cost estimate. Slash-path
filter plus a tag expression. Per-arm concurrency. Per-sample and whole-run
timeouts. A zero-cases guard, like MTP's minimum expected tests. `--resume
<run>` and `--rejudge <run>`. Seeds and temperature recorded per sample.

**Tags with registered names**, like pytest's strict markers, and a built-in
`deterministic` versus `judged` axis so a judge-free pass runs on every pull
request and the judged pass runs nightly against the same transcripts.

**From property-based testing.** The failure database, replay-first ordering,
and a printed one-line replay command transfer directly. The one shrink that
transfers is prefix minimisation of a transcript by re-judging: binary search
for the earliest turn at which the verdict is already fail, which is
deterministic given a fixed judge. That is a cheap and useful diagnostic.

## Reject

- Samples reported as K separate test cases. `[Repeat]`, Go's `-count` and
  pytest-repeat all do this and it is the wrong unit.
- Rerun until green. pytest's JUnit output records only the last attempt,
  which is the same as lying.
- Fail-fast on graded failures. Only on infrastructure failures.
- Byte-exact snapshots of model output, or inline snapshots of transcripts.
- Input shrinking. The predicate is stochastic; "the smallest prompt that fails
  sixty percent of the time" is not well defined without a sample budget per
  probe.

## What this adds to the design

The grader contract and report skeleton in `prior-art.md` stand. This pass
adds a runner shape: stable slash-path ids, a data file of cases beside each
eval, arm-scoped fixtures, a three-outcome vocabulary with retries only on
infrastructure failures, and a record-review-approve loop for judge
calibration and for the report itself. The `deterministic`/`judged` tag axis
is what makes the PR-versus-nightly split in `ci-distribution.md` possible.
