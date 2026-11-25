---
source: https://arxiv.org/abs/2603.28106 (InconLens, cs.HC, v1 2026-03-30)
read: 2026-11-11, HTML; the first ~40% verbatim, the rest through a model's summary, marked [unverified] below
take: a student project, very closely aligned; the UI is a workbench disaster but the method is legible; crib from it, with credit
---

# InconLens

Shuo Yan, Jiaming Mi (East China Normal University), Xiaolin Wen (NTU),
Shaolun Ruan (SMU), Yanjie Zhang, Yushi Sun, Huamin Qu, Rui Sheng (HKUST).
"Interactive Visual Diagnosis of Behavioral Inconsistencies in LLM-based
Agentic Systems."

## The problem, in their words

"the same agent may succeed in one execution but fail in another under
identical inputs." Logs are "lengthy, unstructured, and difficult to compare
across runs"; existing tools "primarily focus on inspecting single
executions." Scope: orchestrator-and-workers multi-agent systems
(MagenticOne), one case-study task (portfolio rebalancing).

## The method

The unit is an **information node**: "a task-relevant informational
milestone", defined by "a change in the system's informational state," not
by an action.

1. Keep the orchestrator's messages; drop worker traces as "low-level
   execution noise".
2. Segment each run where the embedding similarity of adjacent messages
   falls below a threshold.
3. A model summarises segments into candidate node descriptions, which
   "serve as candidate descriptions, not final nodes"; obvious overlaps are
   merged.
4. A developer edits, merges, splits, adds or removes nodes. Only the
   confirmed set is used.

[unverified] Then a model proposes dependencies between nodes (editable), a
model judges per run whether each node was reached, and runs are drawn as a
Sankey of node transitions, links coloured success / unrecovered failure /
recovered failure and thick by run count. Views: task summary, node Sankey,
aligned actions per run with model error analysis, raw log.

Evaluation: a six-month formative study with five developers, and five
expert interviews with NASA-TLX [unverified figures, all low load]. No
agreement numbers, no task-performance study, one task.

## Worth taking (with credit)

- **The unit is a change in what is known, not an action.** The closest
  published idea to a beat, and it names what proctor's blinded read found
  missing: the reader joins fix chains into "one piece of work", the
  *episode*, and an episode is close to "a change in informational state".
  Worth testing as the definition of an episode.
- **Nodes are shared across runs.** A run is a path through milestones
  common to the task, so runs compare by which milestones they reached and
  in what order, without aligning steps. That is the "typical run, with n
  of N against each beat" view proctor's todo wants.
- **A person confirms the node set once per task, not per run.** The cost
  of judgement is paid per task; every run is then read against it.
- **Recovered failure as its own outcome**, distinct from success and from
  failure.

## Not to take

- Embedding-threshold segmentation. Already ruled out for beats: groups by
  syntax, unstable, cannot be defended.
- A model deciding whether each run reached a node, with no verification
  and no agreement figure. If auspex has milestones, reaching one should be
  a rule over the event table (a file written, a command that passed, a
  page read), declared when the person confirms the node.
- Dropping the workers. For a coding agent the "worker" steps are the work.
- The UI.

## Open

- Can a task's milestones be declared by rule (proctor already has the
  material: the files acceptance touches, the verify command, the surface's
  pages), so the human step is writing a short list rather than confirming
  a model's?
- Does a milestone set survive across agents, or does each agent need its
  own (proctor's "do different agents need different affordances")?
