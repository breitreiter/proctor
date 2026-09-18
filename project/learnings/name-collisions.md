---
type: learnings
title: The name "proctor" — collision check
created: 2026-09-17
---

# The name "proctor" — collision check

Checked 2026-09-17, before the first remote existed. Decision: keep the name.
Do not relitigate unless something below changes.

## What exists

Nothing established in evals or AI observability uses it. Two small 2026
projects reached for the same metaphor independently, which confirms the name
fits and that nobody owns it:

- arXiv 2609.02246 (Sept 2026) names its Teacher-grades-Student agent
  architecture PROCTOR. Single author, no code.
- `dachent/model_proctor`: control plane for coding agents with an eval
  harness. Tiny.

Bigger "proctor"s are outside our domain: Indeed's archived Java A/B testing
framework (465 stars, archived 2024-07), Proctor AI (commercial essay grader),
and the whole exam-surveillance category (Proctorio, edx-proctoring, dozens of
webcam projects). Web search for "proctor" will land on those first.

## Registries

| Registry  | Bare `proctor`                                 |
|-----------|------------------------------------------------|
| nuget.org | Taken. `Proctor` 1.0.0, Initify, 2013, dead.   |
| PyPI      | Taken. Old unittest runner.                    |
| npm       | Taken. File-watcher.                           |
| crates.io | Free.                                          |

NuGet is not namespaced; IDs are flat and `Company.Product` is convention only.
Prefix reservation exists but does not free the bare name. If we ever publish,
use `Dreamlands.Proctor` or `Proctor.Cli`. The dotnet tool command name can
still be `proctor`; that is per-package metadata, not a global ID.

## Cost accepted

Discoverability by name is poor and the bare package IDs are gone. Fine for a
peer app to nb. Revisit only if the tool needs to be found by strangers.
