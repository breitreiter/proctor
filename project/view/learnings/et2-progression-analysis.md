---
source: https://vaclab.unc.edu/publication/tvcg_2019_guo/tvcg_2019_guo.pdf (ET², IEEE TVCG 2019)
read: 2026-11-12, the whole PDF, page images
take: the problem statement is ours and the views are worth cribbing; the algorithm assumes what agent runs are not (one-way, homogeneous) and rests on embeddings, already ruled out for beats
---

# ET² (EventThread 2)

Shunan Guo, Zhuochen Jin, David Gotz, Fan Du, Hongyuan Zha, Nan Cao.
"Visual Progression Analysis of Event Sequence Data." IEEE TVCG 2019.
Successor to EventThread (Guo et al., TVCG 24(1), 2018), which cut
sequences into fixed-width time windows and so could not line up the same
stage reached at different speeds; ET² exists to fix that.

## The problem, in their words

Prior work analyses "*hard patterns*": "exactly matching orderings of
events", which "makes them poor match tasks where detailed differences in
event order as less important than understanding higher-level progression
patterns across stages." Design requirement R3, "Time-varying stages to
reflect non-linear speed of progression", is the beats finding: Sonnet's 17
calls and Qwen's 32 both come to 4 to 6 beats.

## The method

Three stated assumptions (4.1): the sequences "should have a similar
progression process" (so the data is pre-filtered by query), they "may vary
in number of events, duration, and pattern of progression", and "the
progression process is irreversible."

1. **Event representation.** Skip-gram over event types, each training pair
   weighted by exp(-|tᵢ - tⱼ|/θ) within a span T: events close in time count
   as related.
2. **Alignment.** An iterative mean sequence: start from the longest
   sequence, merge each other sequence into it by DTW (FastDTW) on event
   vectors, positions treated as equidistant. O(M·n²).
3. **Segmentation.** Content Vector Segmentation (text segmentation from
   word embeddings) over the mean sequence: greedy splits, each segment's
   context vector the sign of the sum of its events' vectors, stopping when
   the best split gains less than a threshold. The stages are then unpacked
   onto each sequence through the alignment.

The user can merge, split (rerun on one stage) or fold stages afterwards.

## The views

Seven coordinated views; the ones that matter:

- **Sequence view.** One row per sequence, one column per stage; each
  segment drawn as its first and last event joined by a duration bar
  coloured by the commonest category, or expanded to a timeline. A stage a
  sequence never entered is a dashed line across the column.
- **Cluster view.** Within each stage, segments clustered (Mean Shift) into
  nodes sized by count, showing start/end event treemaps and frequent
  sub-patterns (VMSP); edges between stages by flow. Spanning transitions get
  invisible intermediate nodes so a skipped stage does not tangle the layout.
- **Stage transition view.** One treemap per stage, edges by how many
  entities move between stages: the highest-level summary.
- Also: a milestone query, a t-SNE of event vectors (the experts asked to
  drop it), an entity list, a thread view inherited from EventThread.

## Evaluation

Two case studies: 145 MIMIC cardiac patients (data set of ~11,000 event
types), seven stages, which two clinicians grouped into five phases; 39
professors' careers over 23 years (10 event types), 13 stages. Three expert
interviews. Readability scores of both main views fall sharply past 300 to
400 sequences; alignment is the cost (DTW), ~300 s at 18k sequences of
length 100 on a 2018 iMac. Accuracy is not measured: "the lack of ground
truth measurements for stage progression analysis makes the accuracy
difficult to quantify." No code or demo URL in the paper.

## Worth taking

- **The views, fed by rule-cut stages instead of learned ones.** A
  sequence view whose columns are orient | iteration 1…n | wrap, a dashed
  line for a skipped stage (a run that never oriented, a write never
  tried), and a transition view of how an arm's runs move through the
  frame, split by pass and fail. ET²'s display without its algorithm.
- **Alignment at the beat level, later.** DTW over 4 to 12 beats per run
  is cheap and can be shown; the alignment of one arm's runs on one task is
  a defensible thing to draw, unlike an embedding cluster.
- **The citation.** "Hard patterns" versus latent stages, and R3, are the
  published form of why beats exist.

## Not to take

- **The algorithm.** Agent runs are not irreversible: they loop write, try,
  fail, repair, and the second cut is built on those iterations; a DTW
  mean sequence averages the loops into one line.
- **Pre-filtering to one progression.** The comparison auspex wants is
  across arms and outcomes, which is exactly the heterogeneity the method
  filters out; split by arm and outcome, a group is 9 to 15 runs.
- **Embeddings as the unit of meaning.** Already ruled out for beats
  (unstable at 90 points, group by syntax, a cluster cannot be defended or
  reproduced). ET²'s stages inherit embedding training's randomness and a
  greedy threshold, with no accuracy figure.
- **Time as similarity.** In an agent run the gap between events is mostly
  model and tool latency; position is the clock.

## Open

- Does the sequence view still read when the columns are a variable number
  of iterations rather than K shared stages? Probably needs iterations
  aligned by intent (advance, rework, repair) rather than by index.
- See `inconlens.md`: shared milestones per task are another way to get
  columns that mean the same thing in every run.
