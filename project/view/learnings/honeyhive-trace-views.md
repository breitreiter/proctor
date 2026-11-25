---
source: https://docs.honeyhive.ai/v2/tracing/trajectory-view (and tree, thread, timeline, graph, ui-flows)
read: 2026-11-11, docs only, no product use
take: weak views, but someone designed and built them; look at the secondary and inspect views
---

# HoneyHive's trace views

## What they have

A session opens in a side panel with five tabs over the same spans:

| view | a row or mark is | what it is for | navigation |
|---|---|---|---|
| Tree (default) | a span, indented under its parent; icon by type, duration, green or red status | finding one step | click → right panel: inputs, outputs, metadata, evaluator scores |
| Timeline | a Gantt bar, width = duration, colour = type | where the time went: long, sequential, gaps, overlap | none documented |
| Graph | a span *name*, repeats merged; edges carry call count and duration | the shape of the run; filters "Errors", "Bottlenecks", "Critical Path"; sidebar "Common Agent Paths" with counts | none documented |
| Trajectory | a bubble per step: y = type and name, x = step order; size *and* colour = a chosen metric (duration, cost, an evaluator score) | where in the run the cost or the bad score sits | click → that span in Tree |
| Thread / Conversation | turns with a divider summarising counts, duration and tokens; levels "Turns / Events / Details" | reading it as a conversation; multi-agent handoffs named | "view event" → Tree |

The Tree's right panel heads with a "Session Summary" (children, model
events, success rate, duration, tokens, cost) and evaluator scores grouped
with count, average, min and max. Human feedback is a rating and comment on
the whole session; bulk review is "Annotation Queues" (not read).

## Worth taking

- **Tree is the hub.** Every other view is an overview that jumps back to
  one step in the same place. One detail panel, many entry points. For
  auspex: the step view is the hub; every overview links into it.
- **Merge repeats into a graph node, count the edges.** The same tool
  called forty times is one node with "×40". Close to our
  count-within-the-frame instinct.
- **Levels of detail as one control** (turns / events / details) rather
  than separate views.
- **Filter presets named by the question** ("Errors", "Bottlenecks",
  "Critical Path") rather than by field.
- **Common paths with counts** is their only cross-run aggregate, and it is
  within one trace. The right instinct pointed at the wrong unit.

## Not to take

- One metric on two channels (bubble size and colour), with the colour's
  direction flipped by a small indicator.
- Several names for one thing: "Model Events", "Completions", "Agent
  Steps"; "Thread" versus "Thread view" versus "Conversation view".
- A metric that blanks silently when its field is missing (cost needs
  `metadata.cost`). Say "not recorded".
- Hidden, remembered toggles that change what a tab shows.
- No comparison of two runs anywhere, and no annotation below session
  level: you cannot mark a step.

## Against what we need

Built for latency and cost in production traces, one session at a time.
Nothing here addresses the questions auspex starts from: what did many runs
of one task do, where do they differ, and which step is the problem.
