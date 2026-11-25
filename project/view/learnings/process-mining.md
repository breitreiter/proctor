---
source: four papers, listed under "What was read"
read: 2026-11-14, text extracted from the PDFs with pdftotext; the two agent papers in full, the handbook chapter in its definitions, applications and challenges sections, the performance spectrum paper in its abstract and introduction only. Figures were read as extracted text, not as images
take: the user's (2026-11-14): this is a structure built for line work, applied to work that is closer to a technician's, programmer's or artisan's, where the practitioner's approach legitimately varies. Claude's: the alignment test bears that out. What survives is what does not assume one path: rule checking, the split picture on coarse standings, and the sample-size bound
---

# Process mining

A field that reads event logs from business systems (an order, a claim, a
loan application) to find out how a process actually runs and where it
departs from how it was meant to run. Raised on 2026-11-14 as the human
process literature closest to an event table plus a reference run.

## What was read

- Carmona, van Dongen, Weidlich. "Conformance Checking: Foundations,
  Milestones and Challenges." In *Process Mining Handbook*, LNBIP 448,
  Springer 2022, pp. 155–190. Open access.
  https://link.springer.com/content/pdf/10.1007/978-3-031-08848-3_5.pdf
- Dorsch, Henselmann, Harth (Fraunhofer IIS). "COMPASS: A Process
  Mining-based Methodology for Prompt Analysis of Large Language Model
  Agents." HybridAIMS and CAI workshops at CAiSE 2025.
  https://ceur-ws.org/Vol-3996/paper-5.pdf
- Fournier, Limonad, David (IBM Research). "Agentic AI Process
  Observability: Discovering Behavioral Variability." PMAI 2025.
  https://arxiv.org/abs/2505.20127
- Denisov, Fahland, van der Aalst. "Unbiased, Fine-Grained Description of
  Processes Performance from Event Data." The copy read does not state
  its venue. https://www.vdaalst.com/publications/p1027.pdf

## The frame

An event log is rows with "at least a case identifier (uniquely
identifying a process instance), an activity name (describing what
happened), and a timestamp" (COMPASS, section 2). Three things are done
with it: discovery (draw the process from the log), conformance checking
(compare the log with a model of what should happen), and enhancement
(put times and counts on a model).

For us the case is a run and the timestamp is the step. The activity is
the open question, below.

## Conformance checking

The handbook chapter gives three ways to compare a log with a model.

- **Rule checking.** "exploit rules that are satisfied by all the
  execution sequences of a process model". Its examples are order and
  count constraints: "An application can be accepted (Aa) at most once";
  "An application must never be finalised (Fa), if the respective offer
  has been declined (Do) already."
- **Token replay.** Replay each trace through the model and count the
  tokens that were missing or left over. Older, and the chapter treats
  alignments as its successor.
- **Alignments.** "An alignment connects a trace of the event log with an
  execution sequence of the process model", written as a two-row matrix.
  Each column is a move of one of three types:
  - a synchronous move, where the event and the model's task agree;
  - a model move, "When a task should have been executed according to the
    model, but there is no related event in the trace": a skipped step;
  - a log move, "a superfluous execution of an activity": an extra step.

  "optimal alignments, i.e., alignments with a minimal number of model or
  log moves, are preferred", and a cost function can weight moves,
  "enabling the categorization of deviations in terms of their severity".

Against a general model this is expensive: "as complex as reachability
in Petri nets which, for general Petri nets, is undecidable", solved in
practice with A* over a product of model and trace.

**Either side may be wrong.** The chapter has model repair ("Assuming
that an event log constitutes a correct representation of a process'
behaviour, deviations ... are a starting point for model repair") and log
repair (assuming the model is right). A repeated extra step can mean the
model needs a loop, not that the trace is at fault.

Its open challenges include showing deviations in a way that ranks them
by what matters, and sampling: "Coverage error occurs when the recorded
behaviour is not a representative sample of the underlying process".

## Process mining applied to agents

### COMPASS

Five phases: plan, extract trajectories, turn them into event logs,
explore and check, write guidelines into the prompt. Tools: PM4Py and
PMTK.

- **The agent.** A ReAct agent on Llama 3 70B with three tools (`search`,
  `describe`, `query`) that writes SPARQL over a knowledge graph. 69
  questions, each run 10 times, "690 trajectories per optimization
  iteration".
- **The activity is the tool name.** The paper allows an event to be "an
  action, thought, or observation" and names aggregation, enrichment and
  slicing as optional steps, but the case study's maps have only the
  three tools plus success and fail.
- **The picture.** One flow map for successful runs and one for
  unsuccessful, the same nodes, each edge labelled with a percentage
  (Fig. 3). The finding read off it: "in unsuccessful runs, the agent
  more often (14% instead of 9%) attempted to generate SPARQL queries
  immediately after receiving search results".
- **The treatment and its measure.** A behavioural model written into the
  prompt, then the same two-map comparison for old and new prompt
  (Fig. 4): "premature query generation dropped from 10% to 5%". A second
  iteration used the variant explorer on logs split four ways by
  self-assessment and an F1 threshold.
- **The result is mixed.** F1 rose for one, two and three-hop questions
  and fell for four-hop, from 0.20 to 0.16 and 0.15 (Fig. 5).
- **Weak spots.** Success in the first iteration is the agent's own
  self-assessment. No interval is given for 14% against 9%. Stated
  limits: needs expertise in both fields, "patterns discovered are often
  domain-specific", and "effective PM demands data volume".

### Agentic AI Process Observability

A toy: a three-agent CrewAI calculator, 290 runs of one input,
`1 + 2 − 3 ∗ 4/5`.

- **The log.** "each row represents a single, timestamped invocation of a
  tool by an agent"; "the concatenation of the agent and tool columns as
  the activity type identifier and the run number as the trace
  identifier."
- **Discovery, two views.** A heuristics-miner flow map for rare paths,
  and a causal graph inferred from timestamps for dependencies, with
  split points drawn as gateways.
- **Intended or not.** Their distinction is the useful one: a split in
  the flow is a decision point if the specification asks for it, and a
  variation point if it "accidentally arises during execution". They
  decide which by giving an LLM a rule describing the split and the
  agent's source, and asking whether the source implies the rule.
- **Findings.** A parentheses tool called with no parentheses in the
  input; a manager agent calling tools it was told to delegate. Fixed by
  editing the specification and redrawing the graph.
- **How many runs.** Two formulas. For a branch proportion,
  n = Z²·p·(1−p)/E². For a rare branch, "to be 95% confident that a
  branch with true prevalence p is observed at least once, the number of
  required runs n must also satisfy (1 − p)ⁿ < 0.05": 298 runs for a 1%
  branch. The second is the price to chase in `regimes.md`, arrived at
  independently.
- **Weak spots.** One input, one toy. The intended-or-not call is a
  model's reading with no check on it. "further empirical validation on
  real-world applications ... is needed".

## The performance spectrum

A picture, not a method. Flow maps annotate each edge with one aggregate,
"average or maximum waiting times between two process steps", which
assumes the process does not change over time and that cases do not
affect each other. The performance spectrum "maps all observed flows
between two process steps together regarding their performance over
time", one line per case from when it left step A to when it reached
step B, with no aggregation first. Patterns such as batching and
overtaking show as shapes in the lines and vanish in an average. The
dotted chart is the older relative: "plots all events per case (y-axis)
over" time.

## Worth taking

- **Alignment against the reference run.** Our model is not a Petri net;
  it is one run, the cheapest pass (`direction.md`, run primitives).
  Aligning a run with a single sequence is plain edit distance, cheap at
  our sizes. Log moves are steps the reference did not need, which is a
  rule-made candidate for friction with no vocabulary of symptoms. Model
  moves are steps the run skipped, which is the candidate for a vacuous
  pass. Tested on 2026-11-14 and weak as it stands; see "The alignment
  test" below.
- **Rule checking is the form our intents take.** "Tool query must not be
  used before Tool search" (COMPASS, on compliance metrics) is a sequence
  operator and a proctor check. The field's name for a written-down
  intent is a declarative constraint, and it checks them per trace
  without a full model.
- **The split flow map.** The same nodes drawn twice, pass beside fail or
  before beside after, with edge percentages. It is the two-ledger
  comparison of `regimes.md` drawn as flow, and COMPASS shows a reader
  can take a finding off it.
- **Do not aggregate before looking.** At 15 runs every run can be drawn.
  The performance spectrum's argument against averages applies to a
  ledger's totals as well.
- **Either side may be wrong.** Model repair and log repair are the
  field's version of "the fix may be in the check, the fixture or the
  surface".
- **Decision point against variation point.** A name for intended and
  unintended variability, and so for intended friction: a split the suite
  declares is a decision point.
- **The citation for the price to chase.** Fournier et al. state the same
  rare-branch bound.

## The alignment test (2026-11-14)

`trials/align.py`, the 15 Sonnet `agent-onboarding` runs, at $0. The
reference for each task is its cheapest pass. The other 12 runs are
aligned with it action by action (observe's kind and target, suite and
task names blanked) by longest common subsequence. A step is flagged when
at least half its actions are extra. Scored against the readers' friction
labels, Haiku labels then Sonnet labels.

| Flag | Steps of 208 | Flag is friction | Friction steps found | Starts found |
|---|---|---|---|---|
| Every step (base rate) | 208 | 52%, 54% | | |
| Extra against the reference | 139 | 64%, 66% | 82%, 81% | 75%, 79% |
| Action absent from the reference anywhere | 88 | 73%, 73% | 59%, 57% | 42%, 46% |
| A symptom (`surprise.md`, without thinking) | 114 | 79%, 82% | 83%, 82% | 78%, 77% |
| Extra and a symptom | 90 | 86%, 88% | 71%, 70% | 65%, 69% |
| Extra or a symptom | 163 | 63%, 65% | 94%, 94% | 88%, 87% |

- **Alignment alone is barely better than flagging everything.** It marks
  two thirds of all steps, 12 points above the base rate. The symptom
  rules do better on every column with fewer flags.
- **Two runs of one task share about a third of their actions in order.**
  65% of a run's actions are extra and 54% of the reference's are
  skipped. Agents order the same work differently (one writes the
  shakedown before the suite, one after), and an order-strict alignment
  calls the displaced half extra. This is the beats problem again.
- **Dropping order helps precision and costs coverage.** An action the
  reference never took at all is friction 73% of the time, and finds
  under 60% of it.
- **It is useful as a second opinion.** A step both flagged extra and
  showing a symptom is friction 86 to 88% of the time, and the union
  finds 94% of friction steps. That gives a short sure list and a long
  safe list from two rule-made signals.
- **Skipped steps say nothing here.** The four failed runs skip 20, 22,
  25 and 42 reference actions; the passes skip 20 to 40.

The alphabet is the limit, as "Not to take" says of the tool name. The
same test is worth one more try on a coarser alphabet, and alignment by
set or by window instead of strict order.

## Not to take

- **Discovery algorithms.** They need volume (290 and 690 runs here) and
  a small activity alphabet. We have 15 runs and bash.
- **The tool name as the activity.** It works with three tools. With one
  shell tool carrying compound commands, every run is its own variant
  and the flow map is one node. The field calls the missing step event
  abstraction and COMPASS lists it as optional. For us it is the whole
  job, and it is what the ledger lines and the event table's normalised
  actions are for.
- **Petri nets and optimal alignment against a general model.** The cost
  is for a problem we do not have.
- **A model deciding intended from unintended with no check.** Against
  "no count from a model" as it stands; the suite should declare it.
- **Self-assessed success** as the outcome to split on.

## Not read

- van der Aalst's critique of directly-follows graphs, which I believed
  was at the URL that turned out to be the performance spectrum paper.
  The claim that flow maps mislead under frequency filtering is from
  memory and should be read before the split flow map is built.
- van der Aalst et al., "Visualizing Token Flows Using Interactive
  Performance Spectra" (fetched, header only).
- The handbook's sections on precision and generalisation, and anything
  on variant explorers as such.
