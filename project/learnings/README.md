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
| [name-collisions.md](name-collisions.md) | The name "proctor" checked against evals, o11y and package registries on 2026-09-17. Kept. Bare `Proctor` is taken on nuget.org, PyPI and npm; publish as `Dreamlands.Proctor` if ever. |
| [nb-coupling.md](nb-coupling.md) | How much of proctor is nb-shaped, measured: ~110 of 1,924 lines. What a modular harness or an nb-as-orchestrator would cost, why `program.nb` is the expensive part, and why an arm labelled `claude-code` is the real exposure. |
| [jev-judges.md](jev-judges.md) | First pass on TypeSafe's Jev and the System One shape as proctor's judge: what the independent evaluations actually say, why it cannot quote evidence and what to do instead, the MIT clones that run it locally, and Cloudflare's `typesafe/jev` binding. Open line in [../plans/jev-judge-research.md](../plans/jev-judge-research.md). |
| [two-modes-and-baselines.md](two-modes-and-baselines.md) | An interview with a working agent bench in industry, anonymised: a baseline is pinned cells not pinned numbers, explore and guard are two different modes, grade the final state and not the transcript, expectations live with the fixture, and isolation is verified rather than enforced. |
