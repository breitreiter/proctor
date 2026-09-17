# Statistics for small-n agent evals: research findings for proctor

Scope: 2-6 arms, 5-50 samples per arm, mostly binary outcomes, arms usually share cases (paired). Reports must be deterministic tables, PM-legible, statistician-defensible.

## 1. Sources surveyed

### Anthropic, "Adding Error Bars to Evals" (Miller 2024, arXiv 2411.00640)
- https://arxiv.org/abs/2411.00640 ; blog: https://www.anthropic.com/research/statistical-approach-to-model-evals
- Five recommendations:
  1. **CLT SEM.** `SE = sqrt(Var(s)/n)`, with `Var(s) = (1/(n-1)) sum (s_i - mean)^2`. For strictly binary scores `SE = sqrt(p(1-p)/n)`; the paper flags Llama 3 for using the Bernoulli form on fractional scores. 95% CI = mean +/- 1.96 SE.
  2. **Clustered SE** when questions share a source (passage, seed, scenario): `SE_clustered^2 = SE_CLT^2 + (1/n^2) sum_c sum_{i != j in c} (s_i - mean)(s_j - mean)`. Observed 3x inflation on reading-comprehension evals. Inspect exposes this as `stderr(cluster="metadata_key")`.
  3. **Variance reduction by resampling.** Run each question K times, score = per-question mean; within-question variance shrinks by 1/K. Do NOT lower temperature to reduce variance. Under a uniform-difficulty prior K=2 cuts variance by 1/3, K=4 by 1/2.
  4. **Paired differences** when two models see the same questions: compute `d_i = s_A,i - s_B,i`, then `SE_paired = sqrt(Var(d)/n) = sqrt(SE_A^2 + SE_B^2 - 2 SE_A SE_B corr(s_A, s_B))`. Report mean difference, its SE/CI, and the correlation.
  5. **Power analysis.** `n = (z_{alpha/2} + z_beta)^2 * (omega^2 + sigma_A^2/K_A + sigma_B^2/K_B) / delta^2`, where omega^2 is the variance of per-question (paired) conditional means and sigma^2 the within-question variance. Worked example: delta = 3 pts at 80% power / 5% alpha needs ~969 questions.
- Reporting format suggested: point estimate with SE in parentheses under it, e.g. `65.5% (0.7%)`, plus n, plus number of clusters; pairwise table with differences, SEs, and correlations.

### "Don't Use the CLT in LLM Evals With Fewer Than a Few Hundred Datapoints" (arXiv 2503.01747)
- https://arxiv.org/abs/2503.01747
- Direct rebuttal of recommendation 1 at small n. CLT intervals collapse to zero width when p is 0 or 1 and under-cover at N = 3, 10, 30, even 100 in many settings. They deliberately give no hard N*.
- Recommends: **Wilson score** or a **Beta-Bernoulli Bayesian credible interval** for a single arm; a **paired Bayesian** model for two arms on shared questions; Bayesian hierarchical (Beta-Binomial) for clustered data; Bayesian for nonlinear metrics (F1). Bootstrap and delta-method both do badly at small n on nonlinear metrics.

### Binomial proportion intervals (Brown, Cai & DasGupta 2001, Statistical Science 16:101-133)
- Summaries: https://search.r-project.org/CRAN/refmans/DescTools/html/BinomCI.html , https://metricgate.com/blogs/wilson-vs-agresti-coull-vs-clopper-pearson/ , https://casrai.org/guides/confidence-interval-for-a-proportion
- Wald: avoid below n ~ 40 and near 0/1; degenerate (zero width) at k = 0 or k = n.
- Clopper-Pearson: exact = guaranteed >= nominal coverage, but conservative (too wide). Use only when a regulator demands "never overstate precision".
- Wilson: recommended default for small n by Brown et al. and most modern texts; closed form; stays inside [0,1]; non-degenerate at 0/n and n/n. Formula:
  `centre = (p + z^2/(2n)) / (1 + z^2/n)`, `half = z * sqrt(p(1-p)/n + z^2/(4n^2)) / (1 + z^2/n)`.
- Jeffreys: Beta(k+1/2, n-k+1/2) quantiles; equal-tailed, slightly narrower than Wilson at the edges; also recommended for small n.
- Agresti-Coull: Wilson centre with Wald half-width ("add 2 successes and 2 failures" for 95%); recommended for n >= 40 for simplicity of explanation; a bit wide at very small n.
- Inspect AI's metrics docs concur: use `ci_wilson()` for binary outcomes; it "always stays within [0,1] and remains well calibrated for small samples and proportions near 0 or 1" (https://inspect.aisi.org.uk/metrics.html).

Computed 95% intervals (percent), for the n values proctor cares about:

| k/n   | Wald        | Wilson     | Agresti-Coull | Clopper-Pearson | Jeffreys   |
|-------|-------------|------------|---------------|-----------------|------------|
| 5/5   | [100, 100]  | [57, 100]  | [51, 100]     | [48, 100]       | [62, 100]  |
| 4/5   | [45, 100]   | [38, 96]   | [36, 98]      | [28, 99]        | [37, 98]   |
| 3/5   | [17, 100]   | [23, 88]   | [23, 88]      | [15, 95]        | [21, 91]   |
| 0/5   | [0, 0]      | [0, 43]    | [0, 49]       | [0, 52]         | [0, 38]    |
| 20/20 | [100, 100]  | [84, 100]  | [81, 100]     | [83, 100]       | [88, 100]  |
| 18/20 | [77, 100]   | [70, 97]   | [69, 98]      | [68, 99]        | [72, 98]   |
| 12/20 | [39, 81]    | [39, 78]   | [39, 78]      | [36, 81]        | [38, 79]   |
| 50/50 | [100, 100]  | [93, 100]  | [91, 100]     | [93, 100]       | [95, 100]  |
| 45/50 | [82, 98]    | [79, 96]   | [78, 96]      | [78, 97]        | [79, 96]   |
| 31/50 | [49, 75]    | [48, 74]   | [48, 74]      | [47, 75]        | [48, 74]   |

Takeaways: Wald is unusable at n=5 and n=20 (zero width at 5/5, 20/20; overshoots 100). Wilson, Agresti-Coull and Jeffreys agree to within a few points at n >= 20. At n = 5 Wilson is the least surprising to a reader (5/5 -> "at least 57%"). Clopper-Pearson is 5-10 points wider at the bottom.

### Bootstrap intervals at small n
- rdoodles simulation, "Bootstrap CIs when sample size is really small": https://rdoodles.rbind.io/2020/06/bootstrap-confidence-intervals-when-sample-size-is-really-small/ . Coverage of nominal-95% BCa: ~81-83% at n=5, 87-90% at n=10, 91-92% at n=20, near nominal by n=40. Percentile slightly worse. Distribution shape mattered less than n. Conclusion: do not rely on bootstrap CIs below n ~ 20.
- Indeed Engineering, "Bootstrap CIs for LLM evaluation" (2026): https://engineering.indeedblog.com/blog/2026/07/bootstrap-confidence-intervals-for-llm-evaluation/ . Cluster bootstrap: resample inputs (cases) with replacement, carrying all k runs of each chosen case. For paired comparisons, resample cases and keep both arms' results together. Use BCa for nonlinear statistics; note BCa's acceleration estimate is noisiest exactly at small N / high skew. Start with k = 3-5 runs per input; more unique inputs beats more reruns. Distinguish exploratory (descriptive CIs) from confirmatory (pre-registered k and multiplicity adjustment).
- Percentile primer (Rousselet, Pernet, Wilcox 2021): https://journals.sagepub.com/doi/full/10.1177/2515245920911881 ; BCa explainer: https://blogs.sas.com/content/iml/2017/07/12/bootstrap-bca-interval.html . Percentile is transformation-respecting but not bias/skew-corrected; BCa is second-order accurate and the usual default when B >= 2000.
- Implication: for count/duration means at n = 5-20, a t-interval on the mean (or on log-duration) is as defensible as, and more transparent than, a bootstrap; at n >= 20-30 a BCa bootstrap on the mean with a fixed seed is fine. Either way, print the median and IQR as the primary descriptive figures for skewed metrics.

### Paired binary comparisons (arms share cases)
- McNemar variants. Fagerland, Lydersen & Laake 2013, BMC Med Res Methodol 13:91, https://link.springer.com/article/10.1186/1471-2288-13-91 : the asymptotic McNemar test and the **McNemar mid-p** test are recommended; the exact conditional test is too conservative; mid-p did not violate nominal level in any of 9595 scenarios and is nearly as powerful as the exact unconditional test. Rule of thumb from older texts: >= 10 discordant pairs for the asymptotic chi-square; below that use mid-p (or exact).
- Fagerland, Lydersen & Laake 2014, Stat Med 33:2850-2875, https://onlinelibrary.wiley.com/doi/10.1002/sim.6148 : evaluated 24 methods. Tests: asymptotic McNemar and McNemar mid-p. Intervals for the paired difference: two closed-form adjusted Wald intervals (Bonett-Price Laplace adjustment; Agresti-Min pseudo-frequency adjustment) and the Tango asymptotic score interval. A CI excluding zero is equivalent to a significant test.
- Newcombe 1998 (Stat Med 17:2635) "square-and-add" paired interval (method 10): take Wilson limits (L1,U1), (L2,U2) for each arm, then
  `lower = d - sqrt((p1-L1)^2 + (U2-p2)^2 - 2 phi (p1-L1)(U2-p2))`, `upper = d + sqrt((U1-p1)^2 + (p2-L2)^2 - 2 phi (U1-p1)(p2-L2))`, phi = estimated phi-coefficient of the 2x2 case table (set to 0 if any margin is 0). Summary: https://fangya.medium.com/newcombe-wilson-confidence-interval-1939dc8fa8d7 , https://www.researchgate.net/publication/13449032
- Paired bootstrap on the difference (resample cases, keep both arms together) and the sign test are also valid; the sign test on discordant pairs IS the exact McNemar test.
- Miller 2024 formula for the paired SE (above) is the CLT version; it degenerates when arms agree on every case (Var(d) = 0), which is exactly when small-n evals produce "all discordant in one direction" and a mid-p McNemar or Newcombe/Tango interval still gives a sensible answer.
- "Measuring all the noises of LLM Evals" (arXiv 2512.21326): paired prediction noise (rerun variability) typically exceeds paired data noise; averaging repeated runs per case raises power; use an all-pairs paired method for multi-arm comparisons.

### Unpaired binary comparisons (arms do NOT share cases)
- Newcombe 1998 (Stat Med 17:873), eleven-method comparison: recommends the **hybrid score interval (method 10)**, i.e. Wilson limits for each arm combined square-and-add with phi = 0. Fagerland, Lydersen & Laake 2011 ("Recommended confidence intervals for two independent binomial proportions") also recommends Newcombe hybrid score and the Miettinen-Nurminen asymptotic score; https://www.ms.uky.edu/~mai/sta635/FagerlandLydersenLaake2011---RecommendedCIsForTwoIndependent....pdf ; https://metricgate.com/docs/newcombe-hybrid-score-ci/ .
- Formula (95%): `lower = (p1-p2) - sqrt((p1-L1)^2 + (U2-p2)^2)`, `upper = (p1-p2) + sqrt((U1-p1)^2 + (p2-L2)^2)`.
- Wald difference interval fails for the same reasons as single-arm Wald. Fisher's exact test is conservative; a Newcombe interval plus (optionally) Fisher or Boschloo p is defensible.
- Caveat from Newcombe: coverage is "acceptable" from ~40 per arm; below that it is still the best simple choice but is somewhat conservative. Say so in a footnote.

### Multiple comparisons
- Holm dominates Bonferroni at equal FWER guarantee (Holm 1979; https://en.wikipedia.org/wiki/Holm%E2%80%93Bonferroni_method , https://pubmed.ncbi.nlm.nih.gov/8629727/ ). A handful of pre-planned comparisons rarely needs more than Holm.
- Gelman, Hill & Yajima 2012, "Why we (usually) don't have to worry about multiple comparisons": https://arxiv.org/abs/0907.2478 . Argues corrections are the wrong frame for small exploratory studies; hierarchical partial pooling is better. Practical reading for proctor: at 2-6 arms and n <= 50 the experiment is exploratory; report all pairwise CIs unadjusted, and if you must label "significant", use Holm across the family of comparisons actually printed.
- evalci (arXiv 2607.04429, https://arxiv.org/abs/2607.04429 ) found 3 of 8 adjacent MMLU rank gaps stop being significant after correcting for the 36 pairwise comparisons a 9-model ranking implies. Its one-line claim format is a good template: `Model A beats Model B, delta = 3.1 pts, 95% CI [1.2, 5.0], paired permutation p = 0.002, n = 1,319`.
- evalstats (https://github.com/ianarawjo/evalstats ): "auto" CI method choice by data type and n; refuses to output below 15 samples; Holm as the default correction; outputs forest plots and critical-difference diagrams; multi-run "noise plots" for per-input instability.

### Presenting uncertainty
- Overlapping-CI fallacy: two 95% CIs can overlap while the difference is significant at p = 0.03; non-overlap implies significance but overlap implies nothing. https://medium.com/data-science/why-overlapping-confidence-intervals-mean-nothing-about-statistical-significance-48360559900a , https://vyasenov.github.io/blog/overlapping-conf-intervals.html , https://imaging.mrc-cbu.cam.ac.uk/statswiki/FAQ/cis . Always print a CI on the difference. (Inspect's own docs suggest comparing whether intervals overlap; this is the mistake to avoid.)
- Greenland et al. 2016, "Statistical tests, P values, confidence intervals, and power: a guide to misinterpretations": https://pmc.ncbi.nlm.nih.gov/articles/PMC4877414/ . CI is "the range of effect sizes reasonably compatible with the data"; "not significant" is not "no effect"; a p-value is not the probability the null is true.
- MDE reporting (Bloom 1995, "Minimum detectable effects: a simple way to report the statistical power of experimental designs"; Analytics-Toolkit posts): https://blog.analytics-toolkit.com/2024/what-if-the-observed-effect-is-smaller-than-the-mde/ , https://docs.geteppo.com/statistics/sample-size-calculator/mde/ . When a study fails to reject, report the MDE alongside the CI so readers can distinguish "no effect" from "underpowered". Plain phrasing: "This experiment could reliably detect a difference of about X points; the observed difference was smaller, so we cannot say whether a real difference exists. Differences larger than the CI's upper bound are ruled out."
- Hamel Husain (evals FAQ, https://hamel.dev/blog/posts/evals-faq/ ): prefer binary pass/fail over Likert (adjacent-point differences are subjective and need larger n); track CIs on production metrics and act when the lower bound crosses a threshold; plan 30-50 pass and 30-50 fail examples per set for judge validation. Eugene Yan (https://eugeneyan.com/writing/evals/ ) is pragmatic and gives no formal CI guidance.
- Cameron Wolfe, "Applying Statistics to LLM Evaluations" (https://cameronrwolfe.substack.com/p/stats-llm-evals ): restates Miller; suggested table cell `mean = X% (SE = Y%) 95% CI = (A%, B%) n = Z`; flags that below ~100 items CLT intervals are too narrow and that MDE should be checked before running.

## 2. Answers

### Q1. Per-arm pass rate at small n
- Method: **Wilson score interval**, 95%, no continuity correction. Non-degenerate at 0/n and n/n, stays in [0,1], closed form, deterministic, the Brown-Cai-DasGupta and Inspect default. Jeffreys is an acceptable alternative if a Bayesian framing is wanted; do not use Wald at any n proctor sees; do not default to Clopper-Pearson.
- If a case has K repeated runs, score each case as its mean pass rate and use the CLT SE on those per-case means (Miller 2024) with a t critical value for n < 30; the binary Wilson interval only applies when each case contributes a single 0/1.
- If cases cluster (same scenario / seed / source repo), cluster the SE on that key; with < 10 clusters, say so and treat the interval as optimistic.
- Print: `62% [45, 77]  (13/21)` or two columns `pass  62% (13/21)` and `95% CI  [45, 77]`. Whole-number percentages (n <= 50 means the resolution is >= 2 points; decimals imply false precision). Always print k/n. Footnote once: "95% Wilson score intervals."

### Q2. Two arms on shared cases (paired)
- Primary number: **paired difference in pass rate** `d = pA - pB = (b - c)/n`, where b = cases A passed and B failed, c = the reverse.
- Interval: **Newcombe 1998 method 10 for paired data** (Wilson square-and-add with phi correction). Closed-form, deterministic, handles b or c = 0, agrees with Tango at moderate n. Tango's asymptotic score interval is the alternative if an iterative solver is acceptable (Fagerland 2014).
- Test (secondary, for the appendix): **McNemar mid-p** on (b, c). Report the discordant counts themselves; PMs understand "A won 6 cases B lost; B won 1 case A lost" better than any statistic.
- Alternative that needs no special-case code: paired cluster bootstrap on d with a fixed seed and B = 2000-10000, percentile interval; only trustworthy at n >= 20.
- Print: `+24 pts [+6, +41]  (A won 6, B won 1, tied 14/21)` . Sign always shown. Footnote: "Paired difference; 95% Newcombe interval; McNemar mid-p in appendix."

### Q3. Two arms on different cases (unpaired)
- Primary: difference in pass rates, interval: **Newcombe hybrid score (method 10, independent)** = Wilson limits of each arm combined square-and-add. Secondary: Fisher exact or Boschloo p in the appendix.
- Flag in the table that the comparison is unpaired ("different cases") because the interval will be visibly wider than a paired one at the same n, and the reader should know why.
- Print: `+24 pts [-3, +46]  (unpaired)`.

### Q4. Count / duration metrics (tool calls, tokens, wall time)
- Descriptives: **median and IQR** as the headline (skewed, heavy-tailed, occasional timeouts), plus mean and n. Show min/max when n <= 10.
- Interval on the mean: n < 20: t-interval `mean +/- t_{n-1,0.975} * s/sqrt(n)`, computed on log(duration) for wall time (back-transform to a ratio). n >= 20: BCa bootstrap on the mean with fixed seed and B = 10000; percentile bootstrap if BCa is unstable (acceleration undefined when all values equal). Do not print any bootstrap interval for n < 10; print `n too small for interval`.
- Comparison of arms: paired: per-case differences (or log-ratios) with a t-interval, or paired bootstrap at n >= 20; also show the per-case win count. Unpaired: Welch t-interval on the difference, or Mann-Whitney in the appendix. evalstats defaults to Welch for independent samples.
- Print: `median 14 (IQR 9-22), mean 17 [12, 25], n=21`.

### Q5. Power / "how many samples" helper
Inputs: baseline pass rate p0, target MDE delta (absolute points), paired or not, alpha = 0.05, power = 0.80, optional K runs per case, optional correlation rho between arms (default 0.3 for paired agents, 0 for unpaired).
- Unpaired binary: `n per arm = (z_{0.975} + z_{0.80})^2 * [p0(1-p0) + p1(1-p1)] / delta^2`, p1 = p0 + delta.
- Paired binary (Miller 2024): `n = (z_{0.975} + z_{0.80})^2 * Var(d) / delta^2` with `Var(d) = p0(1-p0) + p1(1-p1) - 2 rho sqrt(p0(1-p0) p1(1-p1))`; with K reruns per case replace the within-case variance terms by /K.
- Also compute the inverse: given the n you have, print the MDE: `MDE = (z_{0.975} + z_{0.80}) * sqrt(Var(d)/n)`, i.e. 2.80 * SE.
- Worked values (paired, sd of per-case difference 0.5): delta 30 pts needs n = 22; 20 pts needs 49; 10 pts needs 196. Given n, MDE at 80% power: n=5 -> ~63 pts, n=10 -> ~44, n=20 -> ~31, n=50 -> ~20. Unpaired at p = 0.5 is worse: n=20 -> 44 pts, n=50 -> 28 pts.
- Output one sentence per experiment: "With 21 paired cases this experiment can reliably detect a difference of about 31 points. To detect 10 points you need about 200 cases (or 100 cases x 3 reruns)."
- Practical extra (Indeed, Miller): more unique cases beats more reruns; suggest K = 3-5 only when per-case rerun variance is large.

### Q6. Presentation rules
1. Every estimate has n beside it and a 95% interval in brackets: `62% [45, 77] (13/21)`.
2. Comparisons are their own rows/columns with a signed difference and its own interval; never ask the reader to eyeball two per-arm intervals.
3. No p-values in headline tables. If a decision label is wanted, derive it from the CI: "A > B" only when the difference CI excludes 0; otherwise "no clear difference (could detect >= X pts)". P-values (McNemar mid-p, Fisher) go in an appendix table.
4. Every "no difference" cell carries the MDE so the reader can tell "nothing there" from "too few cases".
5. Whole-number percentage points; counts alongside. Use "pts" for absolute differences to avoid "%" vs "percentage point" confusion.
6. Interval degeneracy is visible, not hidden: at 5/5 print `100% [57, 100]`, never `100% [100, 100]`.
7. Deterministic: closed-form intervals by default; any bootstrap uses a fixed seed and fixed B, both stated in the footnote.
8. One footnote block per report naming methods: "Per-arm: Wilson 95%. Paired differences: Newcombe 95%, McNemar mid-p. Unpaired: Newcombe hybrid score. Durations: median/IQR, t-interval on log scale. No multiplicity adjustment; 6 comparisons shown." With > 3 arms, add Holm-adjusted significance in the appendix, not the headline.
9. State the pairing and clustering: "21 shared cases, paired" or "different cases, unpaired"; "cases cluster by scenario (4 clusters), SEs clustered".
10. Word the summary as compatibility, not proof: "compatible with anything from a 6-point to a 41-point improvement".

## 3. Three presentation mistakes to avoid
1. Judging a comparison by whether two per-arm intervals overlap, instead of printing the interval on the difference.
2. Printing Wald / CLT intervals at small n, which collapse to zero width at 0% or 100% and overshoot [0,1].
3. Reporting "no significant difference" without the minimum detectable effect, so an underpowered null reads as evidence of equivalence.
