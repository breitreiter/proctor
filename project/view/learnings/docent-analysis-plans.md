---
source: https://docs.transluce.org/analysis/analysis-plans (and rubrics, reading steps, DQL steps, agent run)
read: 2026-11-11, docs only; the search, clustering and judge pages returned 404
take: model summarisation in the 2024 manner, but a possible lever; the deterministic rubric is still being built in proctor's lab, let it cook
---

# Docent's analysis plans

## What it is

An analysis plan is a Python script (Docent SDK) that the user's coding agent
writes; the UI only shows it. Two kinds of step, run as a lazy graph,
content-addressed and cached so a rerun only does new steps:

- **DQL**: read-only SQL over agent runs, transcripts, metadata and earlier
  results. "Fast and deterministic." It decides which rows exist.
- **Reading**: one model call per row of a DQL result, with a prompt
  ("write a 1–2 sentence summary…"). The only model step. May need the
  user's approval before it spends.

Everything else is a pattern of these two: clustering is a Reading step over
the array of earlier summaries ("cluster these into 5–10 categories");
search is a DQL filter then a Reading per run; pairwise comparison likewise.
Hand-written behaviour rubrics are legacy: "We no longer recommend authoring
behavior rubrics by hand."

Data model: an `AgentRun` holds one or more `Transcript`s (one per agent in a
multi-agent run) in a tree of `TranscriptGroup`s, with free metadata.
Context for a model step is cut by message index
(`transcript_slice(id, start, end)`, original indices kept) and by globs over
metadata, hidden by default. Input is SDK objects; no file format spec was
found.

Citations: a schema field may require them; they point at message and block
indices. **No check that a cited passage exists or says what is claimed**
was found in the docs.

## Worth taking

- **The deterministic query decides the population; the model only reads
  what the query handed it.** That split is ours: rules and the event table
  pick the steps; a model, if any, labels from a fixed vocabulary.
- **The plan is a file the user's agent writes**, cached by content and
  rerun cheaply. Matches the proctor finding that the strongest reader is
  the user's own session, and proctor's planned `grade --try`.
- **Slice by original index**, so whatever a model or a person says points
  back at the raw record by step. Our step ids.
- **Approval before a model step spends.**

## Not to take

- Model-made clusters as the findings: grouping by embeddings or by a
  model was already ruled out for beats.
- Free-text summaries whose numbers come from the model. Revised
  2026-11-12 (`../direction.md`): free text is allowed in an analysis when
  every number in it is a query that can be rerun and every quote
  resolves to a step. What stays out is a count nobody can reproduce.
- Citations by index with no verification. Proctor's judge verifies every
  quote as a substring; auspex should do the same for anything a model
  says.

## The lever

Docent's shape is the one to copy: a deterministic query chooses rows and
a model reads only what it was handed. For proctor's report the model's
part stays a label from a fixed vocabulary with a verified quote. For an
auspex analysis (2026-11-12, `../direction.md`) the model writes the prose,
and the tools carry the weight: every number a kept query, every quote
checked.
