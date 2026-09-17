# Research: what proctor should borrow (and refuse) from unit-testing frameworks

Reading-only pass, 2026-09-17. Context: proctor = experiment manager / grader / reporter for LLM-agent evals
sitting beside `nb` (one JSONL transcript per run, result trailer). N samples x M arms x K cases; deterministic
checks first, then a separately re-runnable per-criterion model judge; deterministic tabular reports with CIs.
Evals live in the project under test like a `test/` directory. Probably .NET.

Legend: **BORROW** = adopt roughly as-is; **ADAPT** = the idea transfers, the mechanics do not; **REJECT** = bad fit.

---

## 0. The core mismatch (read this first)

A unit test is **deterministic, binary, and individually meaningful**: one execution yields pass/fail, and one
failure is a bug. An eval run is **a sample from a distribution**: one execution yields a score, one failure is a
data point, and the unit of meaning is the *rate* (with a confidence interval) over N samples, compared across arms.
Every unit-test convention below is built on the first model, so each has to be checked against the second.

Consequences that fall out of that:

| Unit-test assumption | Eval reality | What breaks |
|---|---|---|
| A test's result is one of pass/fail/skip | A case x arm is a rate + CI; a single run is a score vector | JUnit XML has no cell for "0.73 +/- 0.08" |
| Re-running a test should give the same result; if not, the test is *flaky* (a defect) | Re-running is *the method*; variance is signal, not defect | Retry/flaky tooling (rerun-until-green) actively destroys the data you want |
| The suite is green or red; CI blocks on red | A regression is a *statistically significant drop* vs a baseline arm | `assert score >= 0.7` on a single sample is a coin flip near the threshold (DeepEval admits this) |
| Snapshot = exact expected output | Expected output is a *criterion*, not bytes | Byte-diff snapshot loops don't apply to the output; they DO apply to grader verdicts and reports |
| Setup/teardown per test; cost is ms | A sample costs money and minutes; the MCP server / sandbox is per-arm | Per-test fixture lifecycle is too fine; assembly/session scope is the right analogue |
| Fail-fast saves time | Fail-fast destroys the sample size you need for a CI | `--fail-fast` only makes sense for *infrastructure* errors, not for graded failures |
| Test identity = code location | Case identity = dataset row + arm + sample index | Method names aren't the key; a (case_id, arm, seed/sample) triple is |

How the straddling tools handle it, and where they break:

- **Inspect (UK AISI)** — the most honest. A `@task` is a function returning `Task(dataset, solver, scorer)`;
  the unit of repetition is **epochs** (`--epochs 5`) with **reducers** (`mean`, `max`, `pass_at_k`, `at_least_k`)
  to fold epoch scores into one per-sample score before metrics (accuracy, stderr) are computed.
  https://inspect.aisi.org.uk/tasks.html , https://inspect.aisi.org.uk/reference/inspect_ai.scorer.html ,
  https://inspect.aisi.org.uk/metrics.html . Logs are `.eval` files; `inspect score log.eval` re-scores an
  existing log without re-running the model (the "separate re-runnable judge phase" proctor wants). `--sample-id`
  and `--limit` filter. Where it breaks: it is a *runner*, not a *comparator* — cross-arm comparison and CI on
  the difference are left to notebooks; no CI-consumable interchange format; no notion of "baseline arm".
- **DeepEval** — pytest-native (`assert_test(test_case, [metric])`, `deepeval test run` with `-n` parallel,
  `-r` repeat, `-c` cache, `-i` identifier). It takes the unit-test model literally: each test case is pass/fail
  against a threshold. It then has to bolt on `flaky=True` on cases and metrics, which turns failures into
  warnings, and the docs concede "some test cases will sit right on a metric's threshold and flip between passing
  and failing across runs". https://deepeval.com/docs/evaluation-unit-testing-in-ci-cd . That is exactly the
  mismatch: pass/fail per sample forces you to either accept flaky CI or mute the signal.
- **promptfoo** — YAML test cases with `assert:` lists; each assertion has `threshold` (0..1) and `weight`;
  `evaluateOptions.repeat` runs each case N times; `outputPath` accepts JSON/CSV/HTML/XML/**JUnit**.
  https://www.promptfoo.dev/docs/configuration/reference/ ,
  https://www.promptfoo.dev/docs/configuration/expected-outputs/model-graded/llm-rubric/ ,
  https://www.promptfoo.dev/docs/integrations/ci-cd/ . Where it breaks: the JUnit export flattens each
  case x provider to one pass/fail, so "repeat" results are averaged away or listed as duplicates; the docs
  themselves recommend computing a pass-rate from the JSON and failing the build on a *rate* threshold, i.e. they
  bypass their own JUnit output for the thing that matters. Model-graded assertions are "non-deterministic, so
  demanding a perfect 100% every run gives you flaky builds".

Takeaway: the straddlers all rediscover that **the per-sample pass/fail must be aggregated to a rate before any
gate is applied**, and that JUnit XML is a lossy lowest-common-denominator. Proctor should make the rate (with CI
and a comparison to a baseline arm) the *primary* artifact and emit JUnit/CTRF as a lossy *view* of it.

---

## 1. Discovery and naming

### What the frameworks do

- **xUnit.net v3** — discovery is reflection over `[Fact]`/`[Theory]` in the assembly; display name defaults to
  `Namespace.Class.Method` (configurable to method-only, with `TestMethodDisplayOptions` to replace `_` with
  spaces, etc.). v3 adds a `TestDisplayName` property on every data attribute and `TheoryDataRow<T>` rows with
  metadata (display name, explicit, skip, timeout, traits). Test assembly unique IDs are computed from the file
  path and overridable with a `.uniqueid` file beside the assembly — an explicit acknowledgement that identity
  needs to survive moves. https://xunit.net/docs/getting-started/v3/whats-new ,
  https://github.com/xunit/xunit/discussions/2532 , https://api.xunit.net/v3/3.0.0/Xunit.v3.DataAttribute.html
- **NUnit** — full name `Namespace.Class.Method(arg1,arg2)`; `TestCaseData.SetName(...)` / `TestName=` override;
  the docs warn "the order of the test cases is undefined" when multiple data sources combine.
  https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcase.html
- **MSTest** — `[TestMethod]` + `[DataRow]` / `[DynamicData]`, `DisplayName=` per row; now on Microsoft.Testing.
  Platform (MTP), where the filter is by **test node UID** rather than name.
  https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-cli-options
- **pytest** — node ID `path/to/test_mod.py::Class::test_fn[param-id]`; parametrised ids default to a `-`-joined
  repr of the values, overridable with `ids=` (string list or callable) or `pytest.param(..., id="...")`.
  Collection is a separate phase (`--collect-only`). https://docs.pytest.org/en/stable/example/parametrize.html ,
  https://github.com/pytest-dev/pytest/issues/1360
- **JUnit 5** — two separate things: a **UniqueId** (`[engine:junit-jupiter]/[class:com.x.FooTests]/
  [test-template:bar(int)]/[test-template-invocation:#2]`) used for selection/re-run, and a **display name**
  (`@DisplayName`, `@DisplayNameGeneration`, `@ParameterizedTest(name = "[{index}] {argumentsWithNames}")`) used for
  humans. Placeholders: `{displayName}`, `{index}`, `{arguments}`, `{argumentsWithNames}`, `{argumentSetName}`,
  `{0}`..`{n}`. The invocation index is positional, so re-ordering data changes identity — which is why
  `argumentSetName` was added. https://junit.org/junit5/docs/current/api/org.junit.jupiter.params/org/junit/jupiter/params/ParameterizedTest.html
- **Go** — `TestXxx` functions found by the toolchain; subtests via `t.Run(name, ...)` get a `/`-separated path
  `TestFoo/case_name`; spaces become `_`; `-run` takes a `/`-separated list of regexes matched per level.
  https://go.dev/blog/subtests
- **Rust** — the libtest harness lists `path::to::module::test_name`; `cargo test <substring>` / `-- --exact`;
  nextest requires harnesses to support `--list --format terse` for discovery.
  https://doc.rust-lang.org/cargo/commands/cargo-test.html , https://nexte.st/docs/selecting/ ,
  https://nexte.st/book/custom-test-harnesses.html

### "One file = one thing under test"

Universal convention, enforced by nobody: `FooTests.cs`/`test_foo.py`/`foo_test.go`/`mod tests` mirror the
source unit. Rust and Go enforce co-location by *package* (Go `_test.go` files sit in the package; Rust `#[cfg(test)]`
modules sit in the source file). .NET puts tests in a sibling project. The thing that transfers to proctor is
**one eval file = one behaviour under test** (e.g. `evals/tool-retry.eval.yaml` for "does the agent retry a failed
tool call"), holding its own cases and criteria, with arms defined once at the suite level.

### Verdict

- **BORROW (JUnit 5)**: separate *stable ID* from *display name*. Proctor's ID should be a path of explicit
  segments — `suite/eval-file/case_id/arm/sample#` — where `case_id` is a *declared* key in the case data, never a
  positional index and never a hash of the prompt text (renames of the display string must not orphan history).
- **BORROW (xUnit v3 `.uniqueid`, Inspect `Sample.id`)**: require `id:` on every case; generate and *write back*
  a stable one if missing rather than deriving it at runtime.
- **BORROW (Go subtest paths / pytest node ids)**: make the ID a slash path so `--filter` can match per level.
- **ADAPT**: discovery = glob for `*.eval.*` under a conventional directory (like `test/`), not reflection. A
  separate `list` phase is essential (see 7) because listing is free and running is not.
- **REJECT**: positional invocation indices (`[test-template-invocation:#2]`, `[{index}]`) as identity.

---

## 2. Parameterisation and data-driven tests

- xUnit `[Theory]` + `[InlineData]` / `[MemberData]` / `[ClassData]`; v3 `TheoryDataRow` with `TestDisplayName`,
  `Skip`, `Explicit`, `Timeout`, `Traits` per row. Each row becomes a test case; `MemberData` rows that are not
  serialisable collapse into one un-enumerable theory in v2 (a well-known wart; v3 improves it).
- NUnit `[TestCaseSource]` → `TestCaseData` with `.SetName`, `.SetCategory`, `.Returns`, `.Explicit`, `.Ignore`.
- pytest `@pytest.mark.parametrize("a,b", [...], ids=[...])`; stacked parametrize decorators produce the
  **cartesian product** with ids joined by `-`: `test_x[case1-armA]`. https://docs.pytest.org/en/stable/example/parametrize.html
- JUnit 5 `@ParameterizedTest` + `@CsvFileSource(resources="cases.csv", numLinesToSkip=1)`, `@MethodSource`,
  `@ArgumentsSource`; `arguments(named("..", v))` and `argumentSet("name", ...)` give rows names.

### Mapping onto N cases x M arms x K samples

pytest's stacked-parametrize product is the closest existing model: `cases x arms` is a product with a
two-segment id (`[case_id-arm]`), and the K samples are the dimension **no framework has** — the nearest are
NUnit `[Repeat(K)]`, pytest-repeat's `--count K` (which appends `[1-K]` to the id) and Go `-count=K` (which
does not rename anything, just re-runs). In every one of these the K runs are reported as K independent tests,
which is precisely wrong for an eval.

### Verdict

- **BORROW**: rows-with-metadata (xUnit v3 `TheoryDataRow`, NUnit `TestCaseData`): a case row carries `id`,
  `display`, `tags`, `skip`, `timeout`, and its own expected/criteria. Cases in CSV/JSONL/YAML files beside the eval
  (JUnit `@CsvFileSource` style) rather than inline in code.
- **BORROW (pytest)**: cartesian product of cases x arms with a compound id.
- **ADAPT**: samples are an *inner* dimension of a single result, not K test cases. Report `case x arm` as one
  row containing K observations; never emit K rows to the CI format (or if you must, emit K `<testcase>`s under a
  `<testsuite name="case/arm">` and put the rate in `<properties>` — see 6).
- **REJECT**: `[Repeat]`/`--count` semantics (run-and-report-each).

---

## 3. Fixtures and lifecycle

- **xUnit**: constructor/`Dispose` per test; `IClassFixture<T>` per class; `ICollectionFixture<T>` via
  `[Collection("name")]` shared across classes (and serialises those classes); v3 adds `[assembly: AssemblyFixture]`
  and `ITestPipelineStartup` (`StartAsync`/`StopAsync` around the whole run). `IAsyncLifetime`
  (`InitializeAsync`/`DisposeAsync`) works at every level in v3. Gotcha: in v3 MTP mode a fixture is created and
  initialised even when every test using it is skipped. https://xunit.net/docs/shared-context ,
  https://github.com/xunit/xunit/issues/3371
- **NUnit**: `[SetUp]/[TearDown]` per test, `[OneTimeSetUp]/[OneTimeTearDown]` per fixture, `[SetUpFixture]`
  per namespace/assembly.
- **MSTest**: `[TestInitialize]`, `[ClassInitialize]`, `[AssemblyInitialize]`.
- **pytest**: fixtures with `scope=function|class|module|package|session`, `autouse`, `yield` teardown in reverse
  order; only one instance of a parametrised fixture is cached at a time, so scope can be violated by param
  ordering. Session fixtures are **per xdist worker**, not truly global — the classic footgun for "start the server
  once". https://docs.pytest.org/en/stable/how-to/fixtures.html
- **JUnit 5**: `@BeforeEach/@AfterEach`, `@BeforeAll/@AfterAll`, `@TestInstance(PER_CLASS)`, extensions via
  `@ExtendWith` / `@RegisterExtension`, `ExtensionContext.Store` with `CloseableResource` for cross-class sharing.
- **Go**: `TestMain(m *testing.M)` for per-package setup; `t.Cleanup`.
- **Inspect**: `sandbox=` on a `Task` (docker/local), one sandbox environment per sample by default, with
  per-task config — i.e. lifecycle is tied to the *sample*, not the suite.

### Analogue of "start the MCP server once for the whole arm"

The arm is the natural *collection*: everything in an arm shares model, provider, tool config, and (probably) the
MCP server process. So:

- **BORROW**: xUnit collection/assembly fixture + `IAsyncLifetime` shape: `ArmFixture.InitializeAsync()` starts
  servers, warms caches, records versions into the run manifest; `DisposeAsync()` tears down. Serialising within
  the collection (xUnit's behaviour) maps to "concurrency limit per arm" (a rate-limit concern, not a correctness
  one).
- **BORROW (pytest yield ordering, JUnit Store)**: teardown in reverse order, and a per-run "store" that
  fixtures write into and the report reads out of (provider, model version, server commit hash → belongs in the
  result manifest so reports are reproducible).
- **ADAPT**: lifecycle levels for proctor are `run > arm > case > sample`, not `assembly > collection > class >
  test`. Per-sample state must be fresh (new conversation, clean sandbox) or the samples aren't independent and
  the CI is wrong. Inspect's per-sample sandbox is the right default; per-arm server is the optimisation.
- **REJECT**: xUnit's "initialise the fixture even if everything is skipped" (costly for an eval) and pytest's
  per-worker session scope ambiguity — be explicit that arm fixtures run once per arm per run.

---

## 4. Skip, expected failure, flaky, retry, quarantine

- **pytest**: `@pytest.mark.skip(reason)`, `skipif`, `@pytest.mark.xfail(reason, strict=True/False, raises=)`.
  Non-strict xfail that passes is reported as XPASS (informational); `strict=True` makes XPASS a failure so stale
  markers get removed. `-rxXs` prints reasons. https://docs.pytest.org/en/stable/how-to/skipping.html
- **JUnit 5**: `@Disabled("reason")`, `@DisabledIf`, `@EnabledOnOs`, etc. No built-in xfail.
- **xUnit v3**: `Skip="reason"`, `SkipWhen`/`SkipUnless` (static bool), `SkipType`, runtime `Assert.Skip(...)`,
  `Explicit=true` (run only on request). https://xunit.net/docs/getting-started/v3/whats-new
- **NUnit**: `[Ignore("reason")]`, `[Explicit]`, `[Retry(n)]`, `[Repeat(n)]`.
  https://docs.nunit.org/articles/nunit/writing-tests/attributes/retry.html
- **Retry plugins**: pytest-rerunfailures (`--reruns N --reruns-delay S`, `@pytest.mark.flaky(reruns=)`); JUnit
  Pioneer `@RetryingTest`; MTP `--retry-failed-tests N --retry-failed-tests-max-percentage P
  --retry-failed-tests-max-tests M` (refuses to retry if too much failed — a sane guard).
  https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-retry
- **How "passed on retry" is reported**:
  - pytest's JUnit XML records only the last attempt — reruns are invisible
    (https://github.com/pytest-dev/pytest-rerunfailures/issues/97).
  - Maven Surefire adds `<flakyFailure>` / `<flakyError>` (failed then passed) and `<rerunFailure>` /
    `<rerunError>` (failed then failed again) elements inside `<testcase>`, each with `stackTrace`,
    `system-out`, `system-err`. This is the de-facto extension of JUnit XML for retries; Jenkins and the
    EnricoMi GitHub action understand it. https://maven.apache.org/surefire/maven-surefire-plugin/examples/rerun-failing-tests.html
  - CTRF (JSON) has first-class `retries: n` and `flaky: bool`; "a test is considered flaky only if its final
    status is passed and it experienced one or more failed attempts". https://ctrf.io/docs/full-schema
  - Playwright classifies failed-then-passed as **flaky**, a third outcome distinct from passed.
  - Gradle Develocity: within-build FLAKY outcome when a test fails then passes in one task; cross-build
    detection compares outcomes across builds with the same input fingerprint; `failOnPassedAfterRetry` option.
    https://docs.develocity.ai/2026.2/using-develocity/flaky-test-detection/
  - Buildkite Test Engine detects, labels, notifies and can **quarantine** flaky tests (run but don't block).
  - Google (2016): ~1.5% of tests flaky; a test is designated flaky after failing 3 times in a row across runs;
    reruns are only used for tests already marked flaky or on explicit request; quarantined tests keep running
    but don't block submission. https://testing.googleblog.com/2016/05/flaky-tests-at-google-and-how-we.html

### Verdict

- **BORROW**: `skip` with mandatory reason, `explicit` (opt-in expensive cases), and `xfail(strict)` — the latter
  is genuinely useful for evals as **"known-bad baseline"**: an arm/case pair expected to score below threshold;
  strict mode flags when it unexpectedly improves so the expectation gets updated. Store as a rate expectation
  (`expect: {max_pass_rate: 0.3}`), not a binary.
- **BORROW**: the three-outcome vocabulary (passed / flaky / failed) and Surefire's principle that *every attempt
  is retained in the report*. For proctor this generalises: every sample is retained; there is no "last attempt".
- **BORROW (MTP)**: retry guard rails — cap retries as a percentage and absolute count so systemic outages don't
  get papered over.
- **ADAPT**: *retry* in proctor means only "re-run samples that ended in an **infrastructure** exit_reason"
  (provider 5xx, timeout, tool server crash) — those are excluded from the denominator and counted separately.
  A graded failure is never retried; it is a data point.
- **ADAPT**: quarantine ≙ "informational arm/case" that is run and reported but excluded from the gate. Google's
  "3 in a row" rule maps to "significant drop on 3 consecutive runs" before a regression is escalated.
- **REJECT**: rerun-until-green. Also reject xfail-as-binary.

---

## 5. Assertions, failure messages, snapshot/approval testing

### Good failure messages

Consensus (xUnit `Assert.Equal` diff, FluentAssertions, Shouldly, pytest assertion rewriting, Go `got/want`):
show **expected** and **actual** side by side, name the *subject* (Shouldly rewrites the source expression:
`result.Count should be 3 but was 2`), include *because* context (FluentAssertions `.Should().Be(x, "because ...")`),
and for collections/strings show a **diff with position markers**, truncated around the first difference. pytest
rewrites `assert a == b` into a detailed diff with introspected intermediate values.

For an LLM eval, the "assertion" is a criterion; the message should be: criterion id, verdict, the judge's quoted
evidence span from the transcript (with turn/tool-call index), the rubric text, and a link to the transcript at
that offset. This is what promptfoo's `llm-rubric` returns (score + reason) and what Inspect's `Score` carries
(`value`, `answer`, `explanation`, `metadata`).

### Approval / snapshot testing — the most relevant pattern

- **ApprovalTests** (original): `received` vs `approved` files, opens a diff tool on mismatch.
- **Verify (.NET)**: `{Dir}/{Class}.{Method}_{Params}_{UniqueFor...}.verified.{ext}` beside the test;
  `.received.` written on mismatch; accept via diff tool, Rider plugin, `dotnet verify accept`, or CI-safe
  scripted acceptance; `UniqueForRuntime/OS/Architecture` for env-specific snapshots; scrubbers for volatile
  values (dates, GUIDs, machine names); `UseDirectory`/`UseFileName`/`DerivePathInfo` for layout control;
  verified files are unscoped across TFMs, received files are scoped to avoid parallel lock contention.
  https://github.com/VerifyTests/Verify/blob/main/docs/naming.md , https://github.com/verifytests/verify
- **insta (Rust)**: `.snap` (accepted) vs `.snap.new` (pending); `INSTA_UPDATE=auto` writes `.snap.new` only when
  not on CI; `cargo insta review` is an interactive TUI: `a` accept, `r` reject, `s` skip, `d` toggle diff;
  inline snapshots (`assert_snapshot!(x, @"...")`) get rewritten in source. https://insta.rs/docs/cli/ ,
  https://docs.rs/insta
- **pytest-snapshot / syrupy**: `--snapshot-update` flag; syrupy stores `.ambr` per module with named entries.
- **Jest**: `__snapshots__/` dir, `-u` to update, obsolete-snapshot detection.

The loop is always: **record → review diff → approve → commit the approved artifact beside the test**, with
scrubbing for volatile fields and an explicit env dimension for legitimately-different expectations.

### Verdict

- **BORROW, but for the *grader*, not the *output***: byte-exact snapshots of model output are a REJECT
  (stochastic). But three things in proctor are deterministic given inputs and deserve the record/review/approve
  loop:
  1. **Judge verdicts as calibration snapshots** — for a fixed transcript, the judge's per-criterion verdict is
     re-runnable. Store `cases/<id>/<arm>/<sample>.judged.verified.json`; when the judge prompt/model changes,
     re-judge, diff, and approve. This turns "did my rubric change break grading?" into a snapshot test.
  2. **Deterministic-check expected values** (exit_reason, tool-call sequence, file diffs) — plain Verify-style.
  3. **Reports themselves** — the tabular report is deterministic given the result set; snapshot it so report
     formatting regressions are caught.
- **BORROW (Verify)**: naming scheme with parameters and `UniqueFor` axes (here: judge model, rubric version);
  scrubbers (timestamps, run ids, token counts if not under test); `.received`/`.verified` pair with a CI-safe
  non-interactive accept command; and a **review UI** that shows the diff with accept/reject per item (insta's TUI
  is the model — proctor's "judge calibration review" is exactly this).
- **BORROW (Shouldly/pytest)**: failure text names the criterion, shows verdict, evidence span, and rubric.
- **REJECT**: inline snapshots of transcripts; snapshotting anything with sampling in it.

---

## 6. Reporting formats

- **JUnit XML** — `<testsuites><testsuite name tests failures errors skipped time><testcase classname name time>
  <failure message type>text</failure> | <error> | <skipped message> | <system-out>`; `<properties>` per suite
  (and per testcase in newer consumers). No schema authority; Surefire's extension adds `flakyFailure` etc.
  Universally consumed: GitHub actions `mikepenz/action-junit-report`, `dorny/test-reporter`,
  `EnricoMi/publish-unit-test-result-action`, Jenkins, GitLab, Azure DevOps, Buildkite, Datadog.
- **TRX** — MSTest/VSTest XML; Azure DevOps native; `Microsoft.Testing.Extensions.TrxReport` on MTP. Windows-centric
  consumer base; `dorny/test-reporter` reads it.
- **MTP 2.3+** — `--report-trx`, `--report-junit`, `--report-html`, `--report-ctrf`, plus
  `Microsoft.Testing.Extensions.GitHubActionsReport` / `--report-gh`: per-assembly log groups, `::error`
  annotations placed on the PR diff when a source location resolves, and a Markdown job summary appended to
  `$GITHUB_STEP_SUMMARY`. MTP 2.4 consolidates report artifacts across modules. Exit codes: 8 = zero tests,
  9 = `--minimum-expected-tests` not met. https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports ,
  https://devblogs.microsoft.com/dotnet/microsoft-testing-platform-reporting/ ,
  https://github.com/microsoft/testfx/issues/9003
- **CTRF** (JSON) — `results.tool`, `results.summary{tests,passed,failed,skipped,pending,other,start,stop}`,
  `results.tests[]{name,status,duration,retries,flaky,suite,filePath,line,message,trace,tags,extra}`; `extra` is an
  open object. Consumers: GitHub CTRF actions, Gaffer, MTP. https://ctrf.io/docs/full-schema ,
  https://github.com/ctrf-io/ctrf/blob/main/spec/ctrf.md
- **pytest-json-report** — full session JSON incl. per-phase (setup/call/teardown) outcomes and user
  `metadata`; niche consumers.
- **Go `-json` / test2json** — newline-delimited events `{Time, Action: run|pause|cont|pass|fail|skip|output|bench,
  Package, Test, Elapsed, Output}`; streaming, one event per line, subtest names as `/` paths. This is the best
  streaming *runner* protocol (gotestsum builds JUnit from it). https://pkg.go.dev/cmd/test2json
- **Allure** — per-test JSON results dir → static HTML with history, retries, categories, attachments; heavy.
- **GitHub job summary / annotations** — `$GITHUB_STEP_SUMMARY` Markdown (up to 1 MiB), `::error file=,line=::`
  workflow commands (limited to 10 per step for annotations of each type via commands; Check Run API for more).

### Which format for an eval?

None of them can express "rate +/- CI vs baseline". Recommendation:

- **Primary**: proctor's own JSON (one file per run: manifest + per case x arm rows with K observations, per
  criterion verdicts, summary with CI and deltas). This is the source of truth; everything else is a projection.
- **Streaming**: Go test2json-style JSONL events during the run (`run`, `sample-done`, `judged`, `arm-done`) so a
  UI/CI tail can follow progress; it also gives crash-resumability for free.
- **CI interchange**: emit **JUnit XML** as a projection where the *test case* is `case x arm` (not per sample);
  `<failure>` iff the rate gate fails; the rate, CI, n and delta go into `<properties>` and into the failure
  message text. Also emit **CTRF** (its `extra` object can carry the full stats, `retries` carries infra retries,
  `tags` carries arm/case tags) — it is the format MTP and GitHub actions increasingly speak. Skip TRX unless an
  Azure DevOps user asks.
- **Human**: Markdown job summary (table with rate, CI, delta, sparkline-free) written to `$GITHUB_STEP_SUMMARY`,
  with `::error` annotations pointing at the eval file line for a regressed case (MTP's `--report-gh` is the
  reference implementation).

---

## 7. Runner CLI shape

| Concern | Unit-test precedent | Proctor mapping |
|---|---|---|
| Filtering | `dotnet test --filter "FullyQualifiedName~X&Category=Y"`; pytest `-k expr`, `-m mark`, node ids; Go `-run 'TestFoo/case_.*'`; JUnit `--include-tag`; nextest filtersets | **BORROW** slash-path filter + tag expression: `proctor run --filter tool-retry/case-1* --arm glm* --tag cheap` |
| List without running | MTP `--list-tests`, pytest `--collect-only`/`-q`, Go `-list`, Rust `--list --format terse` (nextest depends on it) | **BORROW, essential**: `proctor list` prints ids + estimated cost; free and needed for CI matrix/sharding |
| Parallelism | xUnit `maxParallelThreads`/`parallelizeTestCollections`; pytest-xdist `-n`; Go `-parallel`; nextest per-process | **ADAPT**: concurrency per arm (provider rate limit) and global cap; not per-test |
| Fail-fast | `--fail-fast`, pytest `-x`/`--maxfail`, Go `-failfast`, MTP `--maximum-failed-tests` | **ADAPT**: only for infrastructure failures (`--max-infra-failures N`), never for graded failures |
| Timeouts | xUnit `Timeout=` (cooperative), MTP `--timeout`, pytest-timeout, Go `-timeout`, `--hangdump` | **BORROW**: per-sample timeout mapped to nb's exit_reason; whole-run timeout |
| Repeat | Go `-count=N`, NUnit `[Repeat]`, pytest-repeat `--count`, promptfoo `repeat`, Inspect `--epochs`, deepeval `-r` | **ADAPT**: `--samples K` is the eval's N; also `--until-ci-width` (adaptive sampling) which has no unit-test analogue |
| Rerun failed | pytest `--lf`/`--ff`, MTP `--retry-failed-tests` | **ADAPT**: `--resume <run>` fills missing samples; `--rejudge <run>` re-runs only the judge phase (Inspect `inspect score`) |
| Expected count guard | MTP `--minimum-expected-tests` exit code 9 | **BORROW**: zero-cases-discovered is an error, not a green run |
| Seeds | Hypothesis `--hypothesis-seed`, FsCheck `Replay`, Go `-shuffle=SEED`, pytest-randomly | **BORROW**: record provider seed/temperature per sample where the provider supports it; `--seed` for case ordering/shuffle |

---

## 8. Categorisation

- xUnit `[Trait("Category","Slow")]` (and v3 traits on `TheoryDataRow`), NUnit `[Category]`, MSTest
  `[TestCategory]`, pytest `@pytest.mark.slow` + `-m "not slow"` with markers registered in config
  (`--strict-markers`), JUnit `@Tag("integration")` with `--include-tag`/`--exclude-tag`, Go build tags /
  `testing.Short()`, Rust `#[ignore]` as a poor man's "slow".
  https://docs.pytest.org/en/stable/example/markers.html
- CI usage: two jobs (fast on every push, slow nightly/on-label); `dotnet test --filter Category!=Slow`.

**Verdict — BORROW**: tags on cases and on criteria. Two built-in axes: `cost` (`deterministic` | `judged`) and
`speed`. The eval file declares which criteria need a judge, so `proctor run --tag deterministic` (or `--no-judge`)
is a pure grading pass and can run on every PR, while the judged phase runs nightly against the same stored
transcripts. Register tags in the suite config (pytest `--strict-markers`) so typos don't silently drop cases.

---

## 9. Property-based / golden-master testing

- **Hypothesis**: `@given` strategies; on failure it **shrinks** to a minimal example, saves it to a local
  `.hypothesis/` example database, replays it first next run, and prints `@reproduce_failure('version', b'blob')`
  (opaque, version-pinned) when `print_blob=True` (default in the `ci` profile); `@seed` / `--hypothesis-seed`;
  `@example` for pinned regression inputs. https://hypothesis.readthedocs.io/en/latest/reproducing.html ,
  https://hypothesis.readthedocs.io/en/latest/tutorial/replaying-failures.html
- **FsCheck**: prints the seed on failure; `Config.Replay` / `[Property(Replay="…")]` reruns it; shrinks via
  `Arbitrary.Shrinker`. https://fscheck.github.io/FsCheck/TipsAndTricks.html
- **QuickCheck**: same lineage; `replay` with `QCGen` + size.
- **Golden master**: capture output of a legacy system over many inputs, then diff — essentially Verify at scale.

### Analogue for "this arm failed on this case, give me the smallest repro"

Shrinking needs a *deterministic* predicate over a *structured* input; an eval has neither cleanly (the model is
the noise, the input is a prompt). But two pieces transfer:

- **BORROW: the failure database + replay**. Every failing sample already *is* a repro artifact: nb's JSONL
  transcript + the exact request parameters. Proctor should (a) keep a per-eval `.proctor/failures/` index of the
  last failing (case, arm, sample) with its transcript path, (b) run those first on the next invocation
  (Hypothesis's "replay saved examples first" policy), and (c) print a one-line replay command
  (`proctor replay <run>/<case>/<arm>/<sample>`) the way Hypothesis prints `@reproduce_failure`.
- **ADAPT: shrinking → transcript truncation**. The one shrink that makes sense for an agent transcript is
  *prefix minimisation*: find the shortest transcript prefix after which the judge's verdict is already "fail"
  (binary search over turns, re-judging the prefix — cheap because it's judge-only, no model sampling). This gives
  "the failure is decided by turn 7" rather than a 40-turn dump. It is deterministic given a fixed judge.
- **ADAPT: pinned examples (`@example`)** → "regression cases": when a real-world failure is found, add it as a
  case with a stable id; this is how a case corpus should grow.
- **REJECT**: input shrinking of prompts/tools (predicate is stochastic; the "minimal prompt that fails 60% of the
  time" is not a well-defined object without a sample budget per probe).

---

## Summary table

| # | Pattern | Verdict | Source |
|---|---|---|---|
| 1 | Stable ID separate from display name; slash-path IDs; declared `id` per case | BORROW | JUnit 5 UniqueId, xUnit v3 `.uniqueid`, Go subtests, Inspect `Sample.id` |
| 1 | One eval file = one behaviour; cases in data files beside it | BORROW | universal; JUnit `@CsvFileSource` |
| 1 | Positional invocation index as identity | REJECT | JUnit `{index}`, pytest default ids |
| 2 | Rows with metadata (id, display, tags, skip, timeout) | BORROW | xUnit v3 `TheoryDataRow`, NUnit `TestCaseData` |
| 2 | cases x arms cartesian product with compound id | BORROW | pytest stacked parametrize |
| 2 | Samples reported as K separate tests | REJECT | `[Repeat]`, `-count`, pytest-repeat |
| 3 | Arm-scoped async fixture with reverse-order teardown; per-sample fresh state | BORROW/ADAPT | xUnit collection/assembly fixture + `IAsyncLifetime`; Inspect per-sample sandbox |
| 4 | skip(reason), explicit, xfail(strict) as *rate expectation* | BORROW/ADAPT | pytest, xUnit v3 |
| 4 | Three outcomes; keep every attempt | BORROW | Surefire `flakyFailure`, CTRF `flaky`/`retries`, Develocity |
| 4 | Retry only infra failures, with percentage/absolute caps | ADAPT | MTP `--retry-failed-tests-max-*` |
| 4 | Rerun-until-green | REJECT | pytest-rerunfailures et al. |
| 5 | Record/review/approve loop for judge verdicts, deterministic checks, reports; scrubbers; UniqueFor axes; TUI review | BORROW | Verify, insta |
| 5 | Snapshotting model output | REJECT | — |
| 5 | Failure message = criterion + verdict + evidence span + rubric | BORROW | Shouldly/pytest/promptfoo reason |
| 6 | Own JSON as truth; JSONL streaming events; JUnit+CTRF projections at case x arm; Markdown job summary + annotations | BORROW/ADAPT | test2json, MTP `--report-gh`, CTRF |
| 7 | `list`, filter expression, tags, min-expected guard, `--resume`, `--rejudge` | BORROW/ADAPT | MTP, pytest, Inspect `inspect score` |
| 7 | `--fail-fast` on graded failures | REJECT | — |
| 8 | Tags with registered names; deterministic vs judged axis | BORROW | pytest markers, xUnit traits |
| 9 | Failure DB, replay-first, printed replay command; prefix minimisation via re-judging | BORROW/ADAPT | Hypothesis, FsCheck |
| 9 | Input shrinking | REJECT | — |

## Source list

- xUnit v3 what's new: https://xunit.net/docs/getting-started/v3/whats-new
- xUnit shared context / fixtures: https://xunit.net/docs/shared-context
- xUnit v3 skipped-fixture issue: https://github.com/xunit/xunit/issues/3371
- xUnit theory display names: https://github.com/xunit/xunit/discussions/2532
- xUnit on MTP: https://xunit.net/docs/getting-started/v3/microsoft-testing-platform
- NUnit TestCase / Repeat / Retry: https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcase.html , https://docs.nunit.org/articles/nunit/writing-tests/attributes/retry.html
- MTP CLI options: https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-cli-options
- MTP retry: https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-retry
- MTP reports: https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports , https://devblogs.microsoft.com/dotnet/microsoft-testing-platform-reporting/ , https://github.com/microsoft/testfx/issues/9003
- pytest parametrize: https://docs.pytest.org/en/stable/example/parametrize.html
- pytest fixtures: https://docs.pytest.org/en/stable/how-to/fixtures.html
- pytest skip/xfail: https://docs.pytest.org/en/stable/how-to/skipping.html
- pytest markers: https://docs.pytest.org/en/stable/example/markers.html
- pytest-rerunfailures + JUnit: https://github.com/pytest-dev/pytest-rerunfailures/issues/97
- JUnit 5 ParameterizedTest: https://junit.org/junit5/docs/current/api/org.junit.jupiter.params/org/junit/jupiter/params/ParameterizedTest.html
- Go subtests: https://go.dev/blog/subtests ; test2json: https://pkg.go.dev/cmd/test2json
- Rust cargo test: https://doc.rust-lang.org/cargo/commands/cargo-test.html ; nextest: https://nexte.st/docs/selecting/ , https://nexte.st/book/custom-test-harnesses.html
- Surefire rerun / flakyFailure: https://maven.apache.org/surefire/maven-surefire-plugin/examples/rerun-failing-tests.html
- CTRF schema: https://ctrf.io/docs/full-schema , https://github.com/ctrf-io/ctrf/blob/main/spec/ctrf.md
- Develocity flaky detection: https://docs.develocity.ai/2026.2/using-develocity/flaky-test-detection/
- Google flaky tests: https://testing.googleblog.com/2016/05/flaky-tests-at-google-and-how-we.html
- Verify naming: https://github.com/VerifyTests/Verify/blob/main/docs/naming.md ; repo: https://github.com/verifytests/verify
- insta CLI: https://insta.rs/docs/cli/ ; https://docs.rs/insta
- Hypothesis reproducing: https://hypothesis.readthedocs.io/en/latest/reproducing.html , https://hypothesis.readthedocs.io/en/latest/tutorial/replaying-failures.html
- FsCheck tips: https://fscheck.github.io/FsCheck/TipsAndTricks.html
- Inspect tasks / scorer / metrics: https://inspect.aisi.org.uk/tasks.html , https://inspect.aisi.org.uk/reference/inspect_ai.scorer.html , https://inspect.aisi.org.uk/metrics.html
- DeepEval CI: https://deepeval.com/docs/evaluation-unit-testing-in-ci-cd
- promptfoo reference / rubric / CI: https://www.promptfoo.dev/docs/configuration/reference/ , https://www.promptfoo.dev/docs/configuration/expected-outputs/model-graded/llm-rubric/ , https://www.promptfoo.dev/docs/integrations/ci-cd/
