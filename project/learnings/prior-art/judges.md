# LLM-as-judge literature and practice: findings for proctor

Reading-only survey, 2026-09-17. Scope: what the judge literature and practitioner guidance say that bears on grading nb agent transcripts across experimental arms, under the rule "deterministic checks first, model judge only where unavoidable; judge output constrained to a fixed label vocabulary plus quoted evidence".

Sources are cited inline by short name; full URL list at the end.

---

## 1. Judge output formats: what is most reliable

### Binary pass/fail beats Likert
- **Hamel Husain, "Creating a LLM-as-a-Judge That Drives Business Results"** (hamel.dev/blog/posts/llm-judge): rejects multi-point scales because "a 3 versus a 4? Nobody knows, and different evaluators often interpret these scales differently." Binary judgments are "easy to interpret and act upon". His listed mistake #4 is "straying from binary judgments when starting out".
- **Husain & Shankar, AI Evals FAQ** (hamel.dev/blog/posts/evals-faq): with binary you only need to check "true aligns with your trues and false with your falses"; Likert requires checking every level aligns, adjacent-point differences are subjective, larger samples are needed to detect differences, and annotators default to the middle value.
- **Eugene Yan, "Evaluating the Effectiveness of LLM-Evaluators"** (eugeneyan.com/writing/llm-evaluators): "Where possible, I have my evaluators return binary outputs. This improves model performance while making it easier to apply classification metrics" (precision/recall/kappa rather than Spearman/Kendall).
- **G-Eval** (arXiv 2303.16634): Likert output from an LLM judge shows score clustering / low variance; the paper had to add probability-weighted scoring to recover discrimination. This is the empirical basis for "Likert from a judge is not a usable scalar".
- **OpenAI evaluation best practices** (developers.openai.com/api/docs/guides/evaluation-best-practices): use "pairwise comparison or pass/fail for more reliability" rather than open-ended scoring; "reformat questions into multiple choice formats" so grading can be automated.
- **Anthropic, "Define success criteria and build evaluations"** (platform.claude.com/docs/en/test-and-evaluate/develop-tests): make rubrics "empirical or specific", e.g. instruct the LLM to output only 'correct' or 'incorrect'; the cookbook grader emits reasoning in `<thinking>` then a fixed label in `<result>`, and the reasoning is discarded after parsing. "Asking the LLM to reason first before producing an evaluation score, and then discarding the reasoning, increases evaluation performance."

### Pairwise vs single-answer
- **Zheng et al. 2023, MT-Bench** (arXiv 2306.05685): pairwise GPT-4 judge reached ~85% agreement with humans excluding ties; human-human agreement 81%. Single-answer grading was also viable and scales better (no O(n^2) comparisons) but is less stable for subjective quality; reference-guided single-answer grading cut math failure from 70% to 15%.
- **Eugene Yan**: "studies show that pairwise comparisons lead to more stable results" for subjective quality; direct scoring is the right tool for objective checks ("faithfulness to a source text or detecting policy violations").
- **The Coin Flip Judge** (arXiv 2606.13685): pairwise judges "frequently choose a winner even when their own scalar scores provide little evidence of a meaningful quality difference" (pointwise gap 0.19-0.36 on a 10-point scale, aggregate difference not significant, yet pairwise declares winners). Pairwise manufactures preference; pointwise ICC only 0.575-0.774. Mean pairwise flip rate across reruns 13.6%; 28% of questions flip >20% of the time; single-trial evaluation only 86.6% consistent with a 50-trial consensus.
- **Prometheus 2** (arXiv 2405.01535): trained for both direct assessment (Likert, rubric-conditioned) and pairwise; Pearson 0.6-0.7 with GPT-4 on 5-point scales, 72-85% pairwise agreement with humans. Confirms Likert correlation with humans tops out around 0.6-0.7 even for a purpose-trained judge.

**Implication for proctor**: arms should not be compared by asking a judge "which arm is better". Grade each run in isolation with binary/categorical labels, then compare arms with counts. Pairwise is only warranted for genuinely subjective, reference-free quality questions, and then it needs AB+BA swapping and tie handling.

### Reasoning-first vs label-first
- Anthropic cookbook, OpenAI guidance ("Add reasoning and chain-of-thought as reasoning before scoring improves eval performance"), G-Eval, Constitutional AI (cited by Yan): reasoning before the label improves accuracy. No study found showing label-first is better.
- Counterpoint: **Quantifying and Mitigating Self-Preference Bias** (arXiv 2604.22891): CoT "produced inconsistent results, sometimes increasing SPB". **CALM** (arXiv 2410.02736) lists "Chain-of-Thought bias" (evaluations vary with explicit reasoning steps) as one of 12 bias types. So: reasoning is scratch space, never a report artifact.
- **Datadog hallucination-detection judge** (datadoghq.com/blog/ai/llm-hallucination-detection): constrained decoding directly on the reasoning model hurt accuracy; they use two stages: unrestricted reasoning, then constrained reformatting into the schema by a smaller model. Decomposed guided steps beat single-shot reasoning.
- Structured-output surveys (futureagi.com 2026; tianpan.co 2026): JSON mode without constrained decoding fails schema 4-13% of the time; constrained decoding brings that under 0.1% but "can cause a 10% to 30% accuracy degradation on complex reasoning tasks" when the model must commit to fields before reasoning.

### Quoted evidence
- **Datadog**: requiring the judge to quote spans from both context and answer "forces the LLM-as-a-judge generation to remain grounded in the text"; also enables UI highlighting. Reached F1 0.810 on RAGTruth.
- Legal-domain judge work (arXiv 2607.03325, 2606.00898): quoted spans are verified deterministically by verbatim match against source; unmatched quotes are dropped as hallucinated. Judges that were not forced to quote "repeatedly award high scores to responses with hallucinated facts".
- **Husain critique shadowing**: human critiques must be "detailed enough so that you can use it in a few-shot prompt"; the same discipline (evidence + verdict) is what makes judge output auditable.

**Design pattern that emerges**: judge emits `{label: <enum>, evidence: [verbatim quotes]}`; proctor verifies every quote is a substring of the transcript and rejects the verdict (or marks it `UNVERIFIED`) if any quote does not match. This turns the judge's prose into something a deterministic check can gate.

---

## 2. Biases that matter for grading agent transcripts, and cheap mitigations

### Catalogue (CALM, arXiv 2410.02736, 12 types)
Position, verbosity, compassion-fade (model name visible vs anonymous), bandwagon, distraction (irrelevant detail), fallacy-oversight (ignore logic errors if final answer looks right), authority (citations look credible), sentiment, diversity, chain-of-thought, self-enhancement, refinement-aware. Most severe in their data: position (robustness 0.60-0.82) and sentiment; self-enhancement significant.

### Numbers
- **Zheng 2023**: position bias: GPT-3.5 inconsistent ~50% of the time, Claude-v1 ~70% (Yan's summary). Verbosity "repetitive list" attack fooled Claude-v1 and GPT-3.5 ~91%, GPT-4 8.7%. Self-enhancement: GPT-4 +10% win rate on own outputs, Claude-v1 +25%.
- **Judging the Judges: position bias** (arXiv 2406.07791, AACL-IJCNLP 2025): 15 judges, 150k instances. Position bias "is not due to random chance and varies significantly across judges and tasks"; driven strongly by quality gap between candidates (small gap -> bias dominates), weakly by length. Metrics to adopt: repetition stability, position consistency, preference fairness.
- **Reliability without Validity** (arXiv 2606.19544, 21 judges): position bias 0.002 (Gemini 2.5 Pro) to 0.192 (Qwen3 8B); a judge with test-retest alpha 0.992 still had 0.192 position bias, i.e. consistency masks invalidity. Verbosity correlation <0.011 for all 21 judges under their single pairwise template (they caution this is not a universal claim).
- **Self-preference** (arXiv 2410.21819): driven by perplexity/familiarity, not identity: judges score lower-perplexity text higher regardless of author. GPT-4 strongest (0.52). Same-family judges inherit the same stylistic preference. (arXiv 2604.22891: 8 of 20 models positive self-preference, 9 negative; family effects not disentangled.)
- **Fallacy-oversight and authority** matter specifically for agent transcripts: a confident final answer with a broken tool trajectory gets passed (AgentRewardBench over-leniency, Catching One in Five).

### Cheap mitigations, with source
| Mitigation | Source | Note |
|---|---|---|
| Swap order and rerun; treat inconsistent verdicts as tie/unknown | Zheng 2023; Reliability without Validity ("measure position bias via paired AB+BA") | Only relevant if pairwise is used at all |
| Never show arm identity, model name, or run ordering to the judge | CALM compassion-fade; Zheng self-enhancement | Grade one run at a time, anonymised |
| Length normalisation / control response length | OpenAI best practices ("Control for response lengths as LLMs bias towards longer responses"); Zheng verbosity | For proctor: report length as a deterministic column, never let the judge weigh it |
| Multiple samples at t=0, majority or flag disagreement | Coin Flip Judge: >=10 trials minimum, 11 for 95% consensus reliability, 15+ for high-variance items; Reliability without Validity: >=3 runs at t=0 with caching disabled | Cheap for a binary label on a short evidence window |
| Judge from a different provider/family than any arm | Coin Flip Judge high-stakes recommendation ("use judges from different providers"); self-preference papers | Husain FAQ counters: same model is acceptable if TPR/TNR against human labels is high |
| Decompose into per-dimension forced-choice checks, one judge call per dimension | Anthropic "Demystifying evals" ("grade each dimension with an isolated LLM-as-judge"); arXiv 2604.22891 (31.5% average reduction in self-preference); Catching One in Five (per-check judges beat holistic) | This is the single most-recommended structural fix |
| Reference-guided grading where a reference exists | Zheng 2023 (70% -> 15% math failure) | For proctor: expected answer / expected tool set is a reference |
| Give the LLM an "Unknown"/"insufficient evidence" label | Anthropic Demystifying evals | Prevents forced verdicts |
| Restrict judge to prefix/relevant window, not full downstream trace, when attributing blame to a step | arXiv 2608.22960 (full-trace judges shift blame later by 0.537 normalised position: collider bias) | Relevant if proctor ever asks "where did it go wrong" |

---

## 3. Validating a judge before trusting it

### Calibration set size
- **Husain (llm-judge post)**: ~30 examples to discover failure modes; for validating a judge "about 100 examples per failure mode, with enough Pass and Fail examples to measure both classes. Below 60 examples, the confidence intervals are often too wide."
- **Husain & Shankar FAQ**: 100-200 labelled examples per failure-mode judge; split train 10-20% / dev 40-45% / test 40-45%; aim for 30-50 Pass and 30-50 Fail in both dev and test.
- **Arize, "How to measure human-LLM judge alignment"**: 30-50 for early rubric iteration; ~100 treat as directional; for a stable benchmark size by CI width and expected count per class (about 100 examples for +/-10pp at 95%).
- Labeller: one "principal domain expert" whose judgment defines acceptable (Husain). For proctor that is the experimenter.

### Metrics and thresholds
- **Husain**: treat the judge as a binary classifier; report TPR and TNR separately, "agreement can be misleading when failures are rare".
- **Yan**: binary -> precision/recall/Cohen's kappa; Likert -> Spearman/Kendall. Observed human-LLM kappa "fair", 0.3-0.5; human-human kappa is often only 0.2-0.3; the benefit of a judge is scale, not superiority.
- **Reliability without Validity**: "a judge reporting 85% agreement on MT-Bench has kappa about 0.48"; exact-match exceeds kappa by 34-41 points purely from label distribution. Recommend: report kappa or Krippendorff's alpha as the headline number; >=3 runs at t=0; paired AB+BA; >=2 datasets with contrasting label distributions; "when test-retest exceeds 0.95, verify position bias is below 0.10 before claiming reliability".
- **Arize metric table**: 2 raters categorical -> Cohen's kappa; ordered labels -> weighted kappa; 3+ raters -> Fleiss; varying raters or missing data -> Krippendorff's alpha. Always show raw agreement alongside.
- **Krippendorff's convention** (Wikipedia, casrai, Appen, PMC11636850): alpha >= 0.800 reliable; 0.667-0.800 tentative; < 0.667 discard. Landis & Koch kappa bands (0.21-0.40 fair, 0.41-0.60 moderate, 0.61-0.80 substantial, >0.80 almost perfect) are looser and widely used in eval blog posts.
- **Arize and OneUpTime (2026-08-31 kappa post)**: both refuse a universal threshold: "There is no context-free kappa cutoff that makes a judge safe"; compare against human-human agreement on the same items and weigh asymmetric costs ("five false passes may be much worse than five false failures"). Pitfalls: prevalence masking (kappa low despite high agreement when one class dominates), test-set contamination from repeated tuning, excluding parse errors/refusals from the denominator.

### Cadence
- **Husain FAQ**: re-run error analysis on any material change (new feature, prompt, model switch, major bug fix); 100+ fresh traces per cycle, typically every 2-4 weeks; 10-20 traces weekly between cycles, emphasising outliers.
- **OneUpTime**: freeze the judge (model + prompt + config) as a version; periodically rerun as traffic changes; collect fresh labels for the next iteration instead of re-tuning against the same held-out set.
- **Anthropic Demystifying evals**: "You won't know if your graders are working well unless you read the transcripts and grades from many trials."

---

## 4. Pre-digesting long transcripts before the judge sees them

- **TRAIL** (arXiv 2505.08638): 148 traces, 841 annotated errors; best model (Gemini 2.5 Pro) reaches only 11% joint accuracy at locating and categorising errors; context-handling errors F1 0.00 for most models. Mean trace consumes 28-63% of context; max traces exceed context 2x; 3 of 8 models could not process full traces; performance correlates negatively with input length (Spearman -0.508 for location accuracy). Recommendations: structured, standardised trace formats (OpenTelemetry-like) rather than raw logs; focus on rare high-impact errors.
- **AgentRewardBench** (arXiv 2504.08942): 1,302 web-agent trajectories, expert labels on success / side-effects / repetition. Judge results (precision/recall/F1): simplified GPT-4o judge with accessibility tree 69.8/83.1/75.9; with screenshots 68.1/80.3/73.7; AER 67.7/71.9/69.7; NNetNav (judge sees LLM-written change summaries only) 52.5/82.4/64.1; rule-based 83.8/55.9/67.1. "Including both accessibility trees and screenshots yields a lower performance than including only the screenshot, indicating that more information distracts rather than assists." LLM judges "overestimate the success rate of every agent"; rule-based checks "consistently underestimate it". Summary-only input (NNetNav) had the worst precision, i.e. summarising before judging inflates false passes.
- **Husain FAQ**: "Give each judge only the parts of the trace it needs for its failure mode"; prioritise the "first upstream failure" because errors compound; progressive disclosure.
- **Datadog**: decomposition into guided extraction steps beat single-shot reasoning over the whole context.
- **Catching One in Five** (arXiv 2606.10315): holistic judges over multi-turn transaction transcripts miss ~20% of real errors, especially state-tracking, implicit-requirement, and attempted-but-not-completed task failures; decomposition and per-check judges recovered much of it.
- **DigiData** (arXiv 2511.07413): fine-tuned judges on goal + step summaries with binary ground truth; works only because they trained the judge on that representation.

**Synthesis**: no source supports handing an LLM-written summary to a general judge in place of the trace. What works is *deterministic* extraction of a narrow, structured window relevant to one check (final assistant text, tool-call list with names/args/exit codes, the result trailer, a specific turn), with the judge answering one question about that window. Summaries lose the evidence the judge must quote and increase leniency.

---

## 5. Process (trajectory/tool-use) vs outcome grading

- **Anthropic Demystifying evals**: grade both transcript and outcome ("the agent says 'your flight has been booked' ... but the outcome is whether a reservation exists in the database"), but "it's often better to grade what the agent produced, not the path it took" because agents find valid paths the rubric author did not anticipate. Prefer deterministic graders: string/regex match, tests, "tool call verification, transcript analysis"; model graders only "where necessary"; use pass@k vs pass^k depending on whether one success or every success matters.
- **CUARewardBench** (arXiv 2510.18596, ICML 2026): best outcome reward model 82.9% precision / 80.1% accuracy; step-level (process) reward models do worse. Human step-level annotation focuses on "key actions" and ignores redundant-but-harmless steps.
- **What Process Evaluation of Coding Agents Actually Measures** (arXiv 2608.22960): action, task and step levels are different questions. Step-level causal effects are undetectable at feasible replay cost (5.3% significant, matching chance, none survive correction). Task-level uncertainty is instance-level (64% of replay variance), not step-level. Full-trace judges show collider bias: blame shifts later when downstream steps are visible. Recommendation: don't treat step-level judge signals as causal; control the judge's information set.
- **Agent-as-a-Judge survey** (arXiv 2508.02994) and the DeepEval guide: trajectory metrics (task completion, step efficiency, plan adherence) and component metrics (tool correctness, argument correctness against a gold tool set) are the standard decomposition; tool correctness is a deterministic comparison against an expected tool set, which catches "right answer via wrong tool".
- **AgentRewardBench**: expert labels on process properties (side effects, repetition loops) are binary and were reliable to annotate; these are exactly the kind of thing that can be detected deterministically (repeated identical tool calls, destructive commands) or with a narrow judge.
- **GroundEval** (arXiv 2606.22737): argues for replacing LLM judges with deterministic execution-trace, state-transition and consistency checks for stateful agents, keeping a model only for task interpretation and residual semantic cases.

**Synthesis**: outcome grading is more reliable and less brittle; process grading is valuable for a small set of *nameable* properties (wrong tool, loop, side effect, forbidden action, gave up early) that should be defined as binary checks, most of them computable without a model. Do not ask a judge to score "trajectory quality" holistically or to attribute blame to a step.

---

## 6. Judge prose failures, and designs that don't depend on prose

Observed failure modes:
- Judges declare winners with no underlying evidence of difference (Coin Flip Judge); reruns flip 13.6% on average.
- Constrained JSON output degrades reasoning 10-30% when the schema forces early commitment (structured-output surveys; Datadog).
- Free JSON output fails to parse 4-13% of the time (futureagi, tianpan); OneUpTime warns that dropping parse failures from the denominator hides judge unreliability.
- Justifications hallucinate citations or reward surface features (legal-domain judge studies, arXiv 2505.17267, 2606.00898).
- CoT itself is a bias axis (CALM; arXiv 2604.22891).
- Same-model or same-family judges score by familiarity (perplexity), so their prose reads as plausible even when wrong (arXiv 2410.21819).

Designs that avoid depending on prose:
1. **Fixed label enum + discard reasoning** (Anthropic cookbook `<thinking>`/`<result>` pattern; OpenAI multiple-choice reformatting). The report shows the label only.
2. **Evidence as verbatim quotes, verified by substring match** (Datadog; legal citation-grounding). Unverifiable quote -> verdict rejected.
3. **Two-stage: free reasoning, then a separate constrained formatting pass** (Datadog) so constrained decoding never touches the reasoning.
4. **One narrow question per call, per dimension** (Anthropic Demystifying; Catching One in Five; arXiv 2604.22891).
5. **An explicit `UNKNOWN`/insufficient-evidence label** (Anthropic Demystifying) so the judge never has to invent a verdict.
6. **Multiple samples at t=0; unanimous -> label, split -> `DISPUTED`** (Coin Flip Judge; Reliability without Validity).
7. **Validate the judge as a classifier on a human-labelled set, report kappa/TPR/TNR, freeze and version it** (Husain; Reliability without Validity; OneUpTime).
8. **Deterministic first**: rule-based checks are high-precision/low-recall (AgentRewardBench 83.8% precision); LLM judges are the opposite. Use rules for everything that has a rule, and use the judge only to raise recall on the residual, so a judge failure can never flip a deterministic result.

---

## Concrete rules for a proctor grader design

1. Grade each run in isolation; compare arms only by counting labels. (Zheng pairwise/position findings; Coin Flip Judge pairwise paradox.)
2. Every judge check is a binary or small-enum label, never a Likert score or free score. (Husain; Yan; G-Eval clustering; OpenAI.)
3. One judge call per dimension, each seeing only the deterministically extracted window it needs (final answer, tool-call table, one turn). (Anthropic Demystifying; Husain FAQ; Catching One in Five; TRAIL length effect.)
4. Never feed an LLM-written summary to the judge in place of the transcript window. (AgentRewardBench NNetNav precision 52.5%.)
5. Judge output schema: `{label, evidence:[verbatim quotes]}`; proctor verifies each quote against the transcript and downgrades to `UNVERIFIED` on mismatch. (Datadog; citation-grounding work.)
6. Let the judge reason before the label, but never store or report the reasoning; if constrained decoding is used, do it in a second formatting pass. (Anthropic cookbook; Datadog.)
7. Include an `UNKNOWN` label. (Anthropic Demystifying.)
8. Sample the judge >=3 times at temperature 0 (10+ for anything reported as a headline); disagreement becomes `DISPUTED`, not a majority vote silently. (Reliability without Validity; Coin Flip Judge.)
9. Hide arm identity, model names, and run order from the judge; grade in randomised order. (CALM compassion-fade; Zheng self-enhancement.)
10. Prefer a judge model from a different family than any arm, or at minimum validate that same-family judging doesn't inflate that arm. (arXiv 2410.21819; Coin Flip Judge.)
11. Length, token usage, tool-call count, exit_reason come from the trailer as deterministic columns; the judge is never asked anything length-related. (OpenAI length control; Zheng verbosity.)
12. Before trusting a judge check: 100-200 human-labelled runs per check with 30-50 of each class; report TPR, TNR, and Cohen's kappa alongside raw agreement; require kappa at least comparable to human-human on the same items, ideally Krippendorff alpha >= 0.667 (tentative) / 0.8 (reliable). Freeze and version the prompt+model; re-validate on any model or prompt change and on a fresh sample every few weeks. (Husain; Arize; OneUpTime; Krippendorff.)
13. Outcome checks first (answer matches reference, expected artefact exists, expected tool was called); process checks only for nameable binary properties (loop, forbidden tool, destructive command, premature stop), most of which are deterministic. Never ask for holistic "trajectory quality" or step-level blame. (Anthropic Demystifying; CUARewardBench; arXiv 2608.22960; GroundEval.)
14. Parse failures and refusals count as judge failures in the denominator, and are reported. (OneUpTime.)

## Three most common mistakes
1. Likert/score outputs from a judge, then averaging them across arms as if they were measurements (G-Eval clustering; Coin Flip ICC; Husain).
2. Pairwise "which arm is better" judging without swap-and-rerun and without a tie option, letting position bias and manufactured preferences decide the result (Zheng; arXiv 2406.07791; Coin Flip Judge).
3. Trusting a single judge call over the whole long transcript (or an LLM summary of it) and treating raw percent agreement as validation, ignoring chance-corrected kappa, class imbalance, and parse failures (TRAIL; AgentRewardBench; Reliability without Validity; OneUpTime).

---

## URLs
- Zheng et al. 2023, Judging LLM-as-a-Judge with MT-Bench and Chatbot Arena: https://arxiv.org/abs/2306.05685 (numbers summarised at https://huggingface.co/datasets/rl-llm-wiki/knowledge-base/discussions/34)
- G-Eval: https://arxiv.org/abs/2303.16634
- Prometheus 2: https://arxiv.org/abs/2405.01535
- Judging the Judges: position bias (AACL-IJCNLP 2025): https://arxiv.org/abs/2406.07791
- Justice or Prejudice? CALM, 12 biases: https://arxiv.org/abs/2410.02736
- Self-Preference Bias in LLM-as-a-Judge (perplexity): https://arxiv.org/abs/2410.21819
- Quantifying and Mitigating Self-Preference Bias of LLM Judges: https://arxiv.org/html/2604.22891v2
- The Coin Flip Judge? Reliability and Bias: https://arxiv.org/abs/2606.13685
- Reliability without Validity (21 judges, kappa deflation): https://arxiv.org/abs/2606.19544
- Catching One in Five: judge blind spots on multi-turn transaction agents: https://arxiv.org/abs/2606.10315
- AgentRewardBench: https://arxiv.org/abs/2504.08942
- TRAIL: Trace Reasoning and Agentic Issue Localization: https://arxiv.org/abs/2505.08638
- CUARewardBench (ORM vs PRM): https://arxiv.org/abs/2510.18596
- What Process Evaluation of Coding Agents Actually Measures: https://arxiv.org/abs/2608.22960
- GroundEval: deterministic replacement for LLM-as-judge: https://arxiv.org/abs/2606.22737
- Agent-as-a-Judge survey: https://arxiv.org/abs/2508.02994
- DigiData (fine-tuned trajectory judges): https://arxiv.org/abs/2511.07413
- Hamel Husain, Creating a LLM-as-a-Judge That Drives Business Results: https://hamel.dev/blog/posts/llm-judge/
- Husain & Shankar, AI Evals FAQ: https://hamel.dev/blog/posts/evals-faq/
- Eugene Yan, Evaluating the Effectiveness of LLM-Evaluators: https://eugeneyan.com/writing/llm-evaluators/
- Anthropic, Demystifying evals for AI agents: https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents
- Anthropic, Define success criteria and build evaluations: https://platform.claude.com/docs/en/test-and-evaluate/develop-tests
- Anthropic cookbook, Building evals: https://platform.claude.com/cookbook/misc-building-evals
- OpenAI, Evaluation best practices: https://developers.openai.com/api/docs/guides/evaluation-best-practices
- Datadog, Detecting hallucinations with LLM-as-a-judge: https://www.datadoghq.com/blog/ai/llm-hallucination-detection/
- Arize, How to measure human-LLM judge alignment: https://arize.com/blog/measuring-human-llm-judge-alignment/
- OneUpTime, Calibrate an LLM judge with Cohen's kappa: https://oneuptime.com/blog/post/2026-08-31-calibrate-llm-judge-cohens-kappa/view
- DeepEval, LLM-as-a-judge guide: https://deepeval.com/blog/llm-as-a-judge
- Krippendorff's alpha thresholds: https://en.wikipedia.org/wiki/Krippendorff's_alpha ; https://casrai.org/guides/krippendorffs-alpha ; https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11636850/
- Structured-output parse failure and accuracy trade-off: https://futureagi.com/blog/evaluating-llm-structured-output-modes-2026/ ; https://tianpan.co/blog/2026/04/12/structured-outputs-constrained-decoding-eliminating-parsing-failures-production-llm
- Legal citation-grounding judges: https://arxiv.org/abs/2607.03325 ; https://arxiv.org/abs/2606.00898 ; https://arxiv.org/abs/2505.17267
