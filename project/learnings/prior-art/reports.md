# Report/readout conventions from adjacent fields — research for proctor

Scope: what A/B readouts, benchmark tools, clinical trial tables, test reporters and model-eval tables do to
serve "one table a manager reads, one an expert checks". Everything below is meant to be producible
deterministically (counts, tables, templated sentences). Sources are listed at the end of each section.

---

## 0. Cross-cutting lessons (the short version)

1. **Separate the decision table from the evidence tables.** Every mature format has a tiny headline
   (Optimizely/Spotify scorecard, benchstat's one-line-per-benchmark, Criterion's `change:` line,
   CONSORT's primary-outcome row) and pushes everything else into fixed, named sections.
2. **Express uncertainty as an interval next to the point estimate, not as a p-value alone.** benchstat
   prints `1.423µ ± 1%`; Criterion prints `[lower estimate upper]`; hyperfine prints `mean ± σ` and
   `1.04 ± 0.04 times faster`; CONSORT item 17a/26 requires "effect size and its precision (such as 95%
   confidence interval)"; Spotify Confidence shows point estimate + CI "on a relative scale".
   Kohavi's rules of thumb: "report the point estimate along with its confidence interval rather than
   relying solely on p-values."
3. **Have an explicit "no detectable difference" glyph/phrase.** benchstat prints `~`; Criterion says
   "No change in performance detected" / "Change within noise threshold"; Spotify says "Not significant:
   lack statistical evidence for a change"; Airbnb greys out cells "for which there is not yet sufficient
   confidence". None of them print "no difference" — they print "not detected".
4. **Account for every unit before you show outcomes.** CONSORT's flow diagram exists precisely because
   readers cannot trust an outcome table without knowing who was dropped and why. The ExP "post-experiment"
   patterns put the Sample Ratio Mismatch check *before* any metric is read.
5. **Guardrails are a separate block with a different question.** Success metrics ask "did it improve?";
   guardrails ask "is there evidence it got worse?" (Spotify: "as long as the metric has not significantly
   moved in the wrong direction, there is no evidence of harm").
6. **Disclose n, duration and the pre-committed design.** Evan Miller: fix the sample size in advance;
   otherwise "all the reported significance levels become meaningless". benchstat prints `n=10` on every
   row so the reader sees the surviving sample count.
7. **No p-values in the baseline table** (the "Table 1 fallacy") — a config/setup table describes arms; it
   does not test them.

---

## 1. Headline table

### What the sources do

**A/B scorecards (Optimizely, Spotify Confidence, Airbnb ERF, Microsoft ExP).** One row per (metric,
variant) or per metric with variant columns. Standard columns: metric name, metric role (primary /
secondary / guardrail), control value, treatment value, relative lift (%), absolute difference, confidence
interval, significance flag / status label. Airbnb ERF: "red and green cells signify metrics that are
statistically significant (red for bad and green for good). Uncolored cells with grey text represent
metrics for which there is not yet sufficient confidence." Spotify: status is one of *Significant* /
*Not significant*, and the experiment gets a recommendation label (*Ship* / *Don't ship* / *Continue* /
*Abort*). ExP additionally reports a scorecard-level "treatment effect assessment" p-value (BH-adjusted)
because many metrics are compared at once.

**benchstat** (Go): one row per benchmark, one column-group per arm (`old.txt`, `new.txt`), each showing
`median ± CI%`, then a `vs base` column with `-17.20% (p=0.000 n=10)` or `~ (p=0.446 n=10)`. Last row is
`geomean`. The `±` is "95% confidence interval for the median". `~` means not significant at α=0.05.
Everything that would make a reader argue (p, n) is printed inline but small.

```
                      │   old.txt   │               new.txt               │
                      │   sec/op    │   sec/op     vs base                │
Encode/format=json-48   1.718µ ± 1%   1.423µ ± 1%  -17.20% (p=0.000 n=10)
Encode/format=gob-48    3.066µ ± 0%   3.070µ ± 2%        ~ (p=0.446 n=10)
geomean                 2.295µ        2.090µ        -8.94%
```

**Criterion.rs**: `time: [lower est upper]`, `change: [lower est upper] (p = 0.00 < 0.05)`, then exactly one
verdict sentence from a fixed set: *Performance has improved.* / *Performance has regressed.* /
*Change within noise threshold.* / *No change in performance detected.* Default noise threshold ±2%,
95% CI. The verdict combines statistical significance AND a practical-significance threshold.

**hyperfine**: `Time (mean ± σ)`, `Range (min … max)`, `N runs`; summary line
`X ran 1.04 ± 0.04 times faster than Y`. Markdown export columns: `Command | Mean [s] | Min [s] | Max [s] | Relative`.

**Clinical results tables (CONSORT item 17/26)**: for each outcome: n per group, result per group
(count/total and %), estimated effect size and its precision (95% CI). For binary outcomes "presentation of
both absolute and relative effect sizes is recommended".

**Model-card / leaderboard tables**: rows = benchmarks, columns = models; best value bold; footnotes give
n trials ("average of 5 trials"), sampling settings, grader. Anthropic's "Adding Error Bars to Evals"
(Miller 2024): report SE / 95% CI (±1.96·SE) on every score; when comparing two models on the same
questions use the *paired* difference and its SE, not two independent CIs; resample each question K times
and average to reduce variance.

### Proposed headline table for proctor

Columns, in this order (left to right = what a manager reads first, what an expert checks last):

| Arm | Runs (graded/attempted) | Pass rate | 95% CI | Δ vs control | Δ 95% CI | Verdict |
|---|---|---|---|---|---|---|
| A (control) | 50/50 | 72% (36/50) | 58–83% | — | — | baseline |
| B | 49/50 | **86%** (42/49) | 73–93% | +14 pts | −2 to +30 | ~ no detectable difference |
| C | 50/50 | 60% (30/50) | 46–73% | −12 pts | −30 to +6 | ~ no detectable difference |

Design choices, each drawn from a source:

- **Count next to rate** (`72% (36/50)`) — CONSORT and test reporters both do it; it lets the reader see
  n without a separate column and makes small-n obvious.
- **Wilson interval** for the per-arm rate (Brown/Cai/DasGupta 2001 recommend Wilson or Jeffreys for small
  n; Wald can go below 0 / above 1). Newcombe/Wilson for the difference of two proportions.
- **Δ in percentage points, with its own CI** — mirrors benchstat's `vs base` and CONSORT's absolute effect.
  Optionally also relative lift (`+19%`) to satisfy CONSORT 17b, but points first: PMs mis-read relative
  lifts.
- **Verdict column uses a glyph + fixed phrase**, never a p-value. Options: `▲ better`, `▼ worse`,
  `~ no detectable difference` (benchstat `~`; Criterion sentences). Rule: ▲/▼ only if the Δ CI excludes 0
  AND |Δ| ≥ a declared practical threshold (Criterion's noise threshold idea); else `~`.
- **Winner marking**: bold the best point estimate (leaderboard convention) but only attach ▲ when the
  interval supports it. Bold-without-▲ reads as "highest observed, not proven".
- **Ordering**: control first, then arms in the order the experiment declared them (not sorted by score —
  sorting by score encourages reading noise as ranking; CONSORT/scorecards keep declared order).
- **Expert details** (p-value, test used, α, correction for multiple arms) go in a one-line footnote under the
  table, benchstat-style: `Two-sided Newcombe interval; α=0.05; Holm-adjusted across 2 comparisons; n shown
  is graded runs.`
- **Runs column** shows `graded/attempted` so the reader sees exclusions at a glance and is pushed to the
  accounting table.

Sources:
- benchstat: https://pkg.go.dev/golang.org/x/perf/cmd/benchstat
- Criterion.rs output: https://bheisler.github.io/criterion.rs/book/user_guide/command_line_output.html ;
  false-positive FAQ: https://github.com/bheisler/criterion.rs/blob/master/book/src/faq.md
- hyperfine: https://github.com/sharkdp/hyperfine
- Airbnb ERF (red/green/grey cells, p-value sparklines): https://medium.com/airbnb-engineering/experiment-reporting-framework-4e3fcd29e6c0
  and https://medium.com/airbnb-engineering/https-medium-com-jonathan-parks-scaling-erf-23fd17c91166
- Spotify Confidence analysis docs: https://confidence.spotify.com/docs/experiments/analyze-results ;
  https://confidence.spotify.com/blog/experiment-analysis
- Microsoft ExP post-experiment patterns: https://www.microsoft.com/en-us/research/group/experimentation-platform-exp/articles/patterns-of-trustworthy-experimentation-post-experiment-stage/
- ExP treatment-effect assessment (scorecard-level p): https://www.microsoft.com/en-us/research/articles/treatment-effect-assessment-at-scale-accounting-for-correlated-metrics-and-metric-relevance-in-modern-experimentation/
- Optimizely scorecard: https://support.optimizely.com/hc/en-us/articles/34053132157965-Understand-your-Experiment-Scorecard
- Kohavi et al., Seven Rules of Thumb: https://exp-platform.com/Documents/2014%20experimentersRulesOfThumb.pdf ;
  book: https://www.cambridge.org/core/books/trustworthy-online-controlled-experiments ;
  Intuition Busters (KDD'22): https://exp-platform.com/abtestingintuitionbusters/
- Netflix on CIs vs p-values: https://netflixtechblog.com/interpreting-a-b-test-results-false-positives-and-statistical-significance-c1522d0db27a
- Evan Miller, Adding Error Bars to Evals: https://arxiv.org/abs/2411.00640
- Wilson interval background: https://arxiv.org/pdf/2207.03199 ; https://metricgate.com/docs/wilson-score-interval/
- Anthropic model cards (n-trial footnotes): https://assets.anthropic.com/m/64823ba7485345a7/Claude-Opus-4-5-System-Card.pdf

---

## 2. Accounting / flow table (CONSORT-style)

### What CONSORT does and why it matters

The CONSORT flow diagram (2010 item 13a/b; 2025 item 22a/b) "accounts for every participant ... across four
stages — enrolment, allocation, follow-up and analysis — giving a count in each box and a reason for every
departure." Standard box labels:

- Assessed for eligibility (n=) → Excluded (n=): not meeting inclusion criteria (n=), declined (n=), other (n=)
- Randomised (n=)
- Allocated to arm X (n=): received allocated intervention (n=), did not receive (give reasons) (n=)
- Lost to follow-up (give reasons) (n=); Discontinued intervention (give reasons) (n=)
- Analysed (n=); Excluded from analysis (give reasons) (n=)

Why it earns trust: outcome tables can be gamed (or accidentally biased) by silently dropping units;
the diagram makes attrition and per-arm differential attrition visible *before* the results. Reviewers
routinely complain that diagrams "lack ... reasons for exclusion" — the reasons are the point, not the counts.
The ExP equivalent is the Sample Ratio Mismatch check: if arms don't have the expected counts, do not read
the metrics.

Companion convention: **"Table 1"** — baseline characteristics per arm, *no p-values* (the "Table 1
fallacy"; CONSORT explicitly says significance tests on baseline are inappropriate). For proctor this is the
arm configuration table.

### Proposed accounting table for proctor

Markdown can't draw the diagram well, so use a stage×arm table with a reasons sub-table:

| Stage | A (control) | B | C | Notes |
|---|---|---|---|---|
| Planned | 50 | 50 | 50 | per config `runs_per_arm: 50` |
| Attempted (launched) | 50 | 50 | 50 | |
| Completed (harness exited) | 50 | 49 | 50 | B: 1 harness crash |
| Graded | 50 | 49 | 48 | C: 2 grader errors |
| **Analysed** | **50** | **49** | **48** | denominators used in §1 |

Exclusions with reasons, per arm (CONSORT "give reasons"):

| Excluded at | Reason | A | B | C |
|---|---|---|---|---|
| Completion | harness crashed / no exit | 0 | 1 | 0 |
| Grading | grader error (timeout) | 0 | 0 | 2 |
| Analysis | manually voided (see run ids) | 0 | 0 | 0 |

Plus a **ratio-mismatch flag**: if analysed counts differ across arms by more than what the planned design
allows, print a warning line above the headline table ("Arm C analysed 48/50; results may be biased if
excluded runs are not random"). This is the SRM idea translated.

Arm setup table (the "Table 1"): arm name, model, harness, prompt hash, temperature, max turns, tool set —
descriptive only, no tests.

Sources:
- CONSORT 2010 explanation (BMJ): https://www.bmj.com/content/340/bmj.c869 ; flow-diagram description:
  https://casrai.org/dictionary/term/consort-flow-diagram
- CONSORT 2025 statement: https://www.thelancet.com/journals/lancet/article/PIIS0140-6736(25)00672-5/fulltext ;
  https://www.nature.com/articles/s41591-025-03635-5 ; site: https://www.consort-spirit.org/
- Table 1 fallacy: https://journals.sagepub.com/doi/full/10.1177/1741826711421688 ;
  https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12677328/
- ExP SRM-first pattern: (post-experiment patterns link above); SRM at scale: https://arxiv.org/pdf/2208.07766

---

## 3. Failure breakdown by categorical reason

### What the sources do

- **pytest / JUnit**: final line `N passed, M failed, K skipped, J xfailed, R rerun in T s`; then a "short test
  summary info" listing FAILED/ERROR/RERUN entries one per line. pytest-rerunfailures shows per-attempt
  status (`RRF`) and counts reruns separately so retries cannot hide instability.
- **Flaky-test dashboards (Gradle Develocity, Buildkite Test Engine)**: a test is *flaky* when the same
  commit yields both PASSED and FAILED; dashboards show passed / failed / flaky / skipped counts and rank by
  reliability %. Reports of "passed on retry" are kept distinct from "passed first time".
- **Clinical harms tables (CONSORT item 19/28)**: adverse events by category, count per arm, with total n
  per arm as denominator.
- **Spotify guardrails**: separate block; verdict is "no evidence of deterioration" not "improved".

### Proposed failure breakdown for proctor

Rows = exit_reason (categorical), columns = arms, cells = count (share of analysed runs). Sorted by total
count descending so the biggest problem is first; "pass" row kept at top for orientation.

| exit_reason | A (control) | B | C | Total |
|---|---|---|---|---|
| pass | 36 (72%) | 42 (86%) | 30 (63%) | 108 |
| wrong_answer | 9 (18%) | 4 (8%) | 6 (13%) | 19 |
| max_turns | 3 (6%) | 2 (4%) | 9 (19%) | 14 |
| tool_error | 2 (4%) | 1 (2%) | 3 (6%) | 6 |
| timeout | 0 | 0 | 0 | 0 |
| **analysed** | 50 | 49 | 48 | 147 |

Conventions:
- Denominator is *analysed* runs, and it is printed as the last row so percentages are checkable.
- Show rows with zero counts only if the category exists in the schema (keeps the table stable across reports).
- Optional "guardrail" verdict per non-pass row: flag ▼ if a failure category is detectably *higher* in a
  treatment arm than control (one-sided; Spotify's guardrail semantics). Never flag ▲ for fewer failures —
  that's already in the headline.
- If runs were retried, add a `first-try pass` vs `pass after retry` split (flaky-test convention), or a
  per-task consistency figure (pass^k, Anthropic agent-eval guidance) when each task is run multiple times.
- For each failure row, link to the per-run list filtered by that reason (appendix), so "failure grouped by
  reason" leads to evidence.

Sources:
- pytest summary flags: https://docs.pytest.org/en/stable/how-to/output.html (`-r` short test summary)
- pytest-rerunfailures: https://github.com/pytest-dev/pytest-rerunfailures
- Gradle flaky definition: https://gradle.com/blog/flaky-tests/ ; retry plugin: https://github.com/gradle/test-retry-gradle-plugin
- Buildkite Test Engine: https://buildkite.com/docs/test-engine/flaky-test-management
- Anthropic agent evals (pass@k / pass^k, read transcripts): https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents
- Spotify guardrail semantics: https://confidence.spotify.com/docs/experiments/analyze-results

---

## 4. Distribution metrics (tokens, duration, tool calls)

### What the sources do

- **benchstat**: median ± 95% CI on the median, per arm; delta on medians; `n=` surviving after outlier removal.
- **Criterion**: point + CI for slope/mean/median, plus std dev and MAD; separate outlier count line
  ("Found 8 outliers among 100 measurements (8.00%): 4 high mild, 4 high severe").
- **hyperfine**: mean ± σ, min … max, runs; warns "Statistical outliers were detected" and "The first
  benchmarking run for this command was significantly slower" (warm-up effect).
- **pytest-benchmark**: Min, Max, Mean, StdDev, Median, IQR, Outliers, OPS, Rounds, Iterations; compare mode
  shows `(NOW vs baseline)` percentages; `--benchmark-compare-fail=min:5%`.
- **Netflix streaming experiments**: for skewed latency-type metrics they compare *quantile functions*
  with confidence bands rather than means, because means hide tail changes and are dominated by outliers.
- **CONSORT**: continuous outcomes as mean (SD) or median (IQR) per group + difference with CI.

### Proposed distribution table for proctor

Token/duration/tool-call distributions from agent runs are right-skewed with occasional blow-ups
(max_turns runs). Follow benchstat/Netflix: **median as the headline, p90 for the tail, mean only for cost
arithmetic.**

| Metric | Arm | n | median | p90 | mean | min–max | Δ median vs control |
|---|---|---|---|---|---|---|---|
| duration (s) | A | 50 | 84 | 190 | 101 | 22–412 | — |
| duration (s) | B | 49 | 71 | 150 | 82 | 19–301 | −13 s (−15%) [−31, +4] ~ |
| tokens (total) | A | 50 | 41k | 97k | 52k | 9k–210k | — |
| tokens (total) | B | 49 | 38k | 80k | 44k | 8k–160k | −3k (−7%) [−12k, +6k] ~ |
| tool calls | A | 50 | 14 | 31 | 17 | 3–60 | — |

Rules:
- Quantiles yes (median, p90; p50/p90/p99 if n ≥ 100). IQR optional. Standard deviation is not useful for
  these distributions; prefer min–max plus p90.
- CI on the median difference via bootstrap (Criterion/benchstat both bootstrap); print as `[lo, hi]` and
  reuse the `~` glyph.
- Report **pass-only and all-runs** variants where the failure mode inflates the metric (a max_turns arm
  will have a long duration for the wrong reason). Say which one the table uses.
- Outlier line, Criterion-style: `Outliers: A 3/50 (>3×IQR), B 1/49` — do not remove them silently.
- Sparklines/histograms: earn their place only when the shape matters (bimodal duration = "solved fast or
  wandered until max_turns"). In markdown, a compact text histogram or a 10-bucket `▁▂▅▇▃▁` sparkline per arm
  is enough; in HTML, a small strip/dot plot per arm on a shared axis. Netflix's argument: a table of summary
  stats can't show *where* in the distribution the shift happened; a quantile plot can.

Sources:
- benchstat, Criterion, hyperfine (links above)
- pytest-benchmark comparing: https://pytest-benchmark.readthedocs.io/en/latest/comparing.html
- Netflix quantile visualisation: https://medium.com/netflix-techblog/streaming-video-experimentation-at-netflix-visualizing-practical-and-statistical-significance-7117420f4e9a
- Netflix sequential testing / continuous data: https://netflixtechblog.com/sequential-a-b-testing-keeps-the-world-streaming-netflix-part-1-continuous-data-cba6c7ed49df

---

## 5. Templated narrative

### Fixed sentences observed in the wild

- benchstat: no prose; the `~` glyph and `(p=… n=…)`.
- Criterion: "Performance has improved." / "Performance has regressed." / "Change within noise threshold."
  / "No change in performance detected." Always after a `change: [lo est hi] (p = … < 0.05)` line.
- hyperfine: "`X` ran 1.04 ± 0.04 times faster than `Y`".
- pytest: "3 failed, 45 passed, 2 rerun in 12.3s".
- Spotify Confidence: "Significant: statistical evidence for a change/increase/decrease due to the
  treatment"; "Not significant: lack statistical evidence for a change". Recommendation: Ship / Don't ship.
- Clinical abstracts (CONSORT abstract item): "Results — Numbers analysed: N in each group; the primary
  outcome occurred in 42/49 (86%) vs 36/50 (72%); difference 14 percentage points (95% CI −2 to 30)."
- Kohavi/ExP "ship email" structure: what was tested, what happened (OEC + guardrails), decision.
- Miller (error bars): report "difference ± SE" on paired data; state the number of resamples per question.

### Proposed templates for proctor (all fillable from counts)

Summary block ("what was tested / what happened / what we recommend" — ExP ship-email structure):

- *Tested:* `{k} arms × {n} runs each on {task_set} ({n_tasks} tasks); control = {A}. Run on {date}.`
- *Result, detectable difference:*
  `{B} passed {pB}% ({xB}/{nB}) vs {A} {pA}% ({xA}/{nA}): +{d} points (95% CI {lo} to {hi}). Detectable improvement.`
- *Result, none detectable:*
  `{B} passed {pB}% vs {A} {pA}%: {d:+} points (95% CI {lo} to {hi}). No detectable difference at n={n} per arm;
  a difference smaller than about {mde} points would not have been detected.`
  (The MDE sentence is Evan Miller's "report how large an effect can be detected given the current sample size".)
- *Guardrails:* `No arm showed a detectable increase in any failure category.` or
  `{C} had more max_turns exits than {A} (19% vs 6%, +13 points, 95% CI +1 to +25).`
- *Accounting:* `{e} of {N} runs excluded before analysis: {k1} {reason1} ({arms}), {k2} {reason2}.` or
  `All {N} runs completed and were graded.`
- *Cost:* `{B} used {x}% fewer tokens (median {mB} vs {mA}) and finished {y}% faster (median {tB} vs {tA}); both
  differences {within/outside} the interval of noise.`

Wording rules:
- "No detectable difference" or "not detected", never "no difference" or "equivalent" (benchstat/Criterion/
  Spotify all avoid asserting the null).
- Always pair a delta with its interval in the same sentence.
- Always name the denominator once per paragraph.
- Never rank arms in prose unless the intervals separate them; otherwise say "highest observed".
- Multiple arms: state the correction once ("intervals adjusted for {k−1} comparisons") in the footnote, not in
  every sentence.

Sources: links in §1 plus Evan Miller, How Not To Run an A/B Test: https://www.evanmiller.org/how-not-to-run-an-ab-test.html

---

## 6. Appendix / detail conventions

- **Per-run table** (pytest "short test summary" + CONSORT "give reasons"): one row per run:
  `run_id | arm | task | outcome | exit_reason | duration | tokens | tool_calls | link`. Sorted by arm then
  task. Failed/excluded runs listed first in a separate short list (pytest lists failures, not passes).
- **Evidence links**: every count that appears in a table should be traceable — run id links to the
  transcript/log directory; failure-reason rows link to the filtered run list. Anthropic's agent-eval
  guidance: "we do not take eval scores at face value until someone digs into the details of the eval and
  reads some transcripts" — the report's job is to make that one click away.
- **Reproducibility block** (model cards + benchmark tools + pre-registration): experiment id; date/time;
  proctor version; per-arm model id + provider + version/date; harness version; prompt file hash; config file
  hash and path; task set version/hash; grader version; sampling settings (temperature, max turns);
  command line to regenerate; random seed if any; stats method, α, CI method, correction.
  Model cards put this as footnotes ("average of 5 trials, 64k thinking budget, default sampling").
- **Analysis-method note** (benchstat prints "need >= 6 samples" style warnings when tests are underpowered;
  Criterion prints the outlier breakdown): a fixed short paragraph naming the test/CI method and any warning
  triggered (low n, arm imbalance, high outlier count, retries used).
- **Data export**: hyperfine and pytest-benchmark export CSV/JSON/markdown of the same numbers; ship a
  machine-readable JSON next to the markdown so experts can recompute.

Sources: hyperfine export (link above); Anthropic system cards (link above); Anthropic agent evals (link above).

---

## 7. Proposed report skeleton for proctor

1. **Title + one-paragraph summary** — templated sentences from §5 (tested / happened / recommend).
2. **Headline table** (§1) — one row per arm; pass rate (x/n), 95% CI, Δ vs control with CI, verdict glyph.
   Footnote: methods, α, correction. Warning banner above it if accounting shows arm imbalance.
3. **Accounting** (§2) — stage×arm counts (planned → attempted → completed → graded → analysed) + exclusions
   with reasons.
4. **Failure breakdown** (§3) — exit_reason × arm counts with shares; guardrail flags.
5. **Cost & effort** (§4) — duration / tokens / tool calls: n, median, p90, mean, min–max, Δ median with CI;
   outlier line; optional sparkline.
6. **Arm setup ("Table 1")** — model, harness, prompt hash, settings per arm; no statistics.
7. **Appendix A: per-run table** with links to transcripts; failures first.
8. **Appendix B: reproducibility** — versions, hashes, command line, stats methods, JSON export path.
