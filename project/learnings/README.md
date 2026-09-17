---
type: learnings
title: What we learned building this
created: 2026-09-17
---

# What we learned building this

Written after the fact, recording what the runs said rather than what the plans
hoped. Negative results belong here as much as wins: a technique that did not
survive contact with a real nb log is worth more to the next reader than one
that did.

| document | what it covers |
|---|---|
| [prior-art.md](prior-art.md) | The research pass. What eval frameworks, trackers, the judge literature, small-N statistics and readout conventions already solved, the four things none of them did, and the three decisions that follow. Raw findings with URLs under `prior-art/`. |
| [test-framework-patterns.md](test-framework-patterns.md) | What xUnit, pytest, JUnit, snapshot testers and flaky-test conventions lend an eval runner: ids, fixtures, three outcomes, record-review-approve. And the one mismatch none of them can paper over. |
| [ci-distribution.md](ci-distribution.md) | How other repos pull proctor and nb into GitHub Actions: nuget.org tool packages pinned in a manifest, a thin composite action, report-don't-gate. And what nb needs first. |
| [where-runs-live.md](where-runs-live.md) | Commit the definitions and the report, archive transcripts to a bucket in the on-disk layout, never LFS. The `archive` and `fetch` verbs. |
