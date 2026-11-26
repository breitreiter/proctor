---
type: snapshot
title: Where proctor stands, for whoever picks it up next
created: 2026-11-25
status: written at the end of two days on a travel laptop, for a session starting on another machine with none of that conversation. It is a pointer, not a record: the record is `plans/axes-build.md`. Retire or rewrite it once it is stale
---

# Where proctor stands (2026-11-25)

Branch `exam-variant-attempt`, pushed. Read `plans/axes-build.md` for
what was built and decided, slice by slice; this page says where to
start and what is not in git.

## What was built on the 24th and 25th

Axes, slices 3a to 6 of `plans/axes-build.md`, each with a "BUILT"
note and what was decided while building:

| Slice | What |
|---|---|
| 3a | `run` makes the configurations an exam names, or one value alone; never the cross product |
| 3b | `proctor build`: what each value changes, built alone; a configuration held against its values |
| 3c | `proctor compare` and `coverage`, answered as JSON; a configuration against another |
| 3d | The viewer moved in as `View/`; pages for an exam with axes, a first cut |
| 4 | A configuration through Harbor; `tern-docs` and `gate-oracle` on axes; a one-stage try with an oracle |
| 5 | Grades kept under their standard; a check's `when`; not yet graded; "the standard moved" |
| 6 | Predictions |

## Decisions that shape everything else

- **Proctor does no decision support** (`plans/axes.md`, decision 17).
  It is a source of truth, a layer for inspecting evidence, and a data
  access layer for an agent. The agent makes the recommendation.
- **The words.** A **prediction** is a statement written before the
  tries: run this configuration, and this will change. It replaced
  "claim". "Candidate" and "treatment" are not words here. A **try** is
  one configuration run once through the stages; an **attempt** is one
  stage of a try.
- **Nothing records that a person reviewed anything.** A record of a
  review was built and removed the same day.
- **Opening the host's loopback to containers is not advice.** It was
  done on the travel laptop only, for narrow testing, and is never to
  be set on a machine that matters or written into a guide
  (`learnings/harbor.md`, "A gateway on the same machine as the
  containers").

## In flight

**The paid run of `tern-docs`.** Six tries exist, two of each named
configuration, Claude Code on Sonnet through Harbor:

| `docs` | Headline checks held, each try | Flagged steps | Tool calls, 12 stages |
|---|---|---|---|
| `as-shipped` (live) | 18/18, 18/18 | 7, 11 | 42 |
| `restructure` | 18/18, 18/18 | 10, 8 | 45 |
| `none` | 13/18, 13/18 | 29, 32 | 88 |

**Finished 2026-11-26, on the desktop (rootless podman, the gateway on
another machine):** four tries a side. No docs against as shipped is
worse on headline checks held, confirmed (p = 0.029), and both of its
predictions held; the restructure is the same as as shipped, and none
of its three predictions came out. What follows is how it stood on
the 25th.

Two a side cannot confirm anything. The plan was four a side: raise
`attempts` from 2 to 4 in `exams/tern-docs/exam.json` and run the other
six tries. A try with docs is about 25 requests and $0.50; one without
is about 60 requests. **Those six tries are in `bank/`, which is not in
git** (see below): without them the other machine starts from none.

**The viewer's pages** work and are not designed. The user's first
look: the parts make sense, the presentation is muddy, the layout has
Claude's habits in it, and it has not been held against the design
canvas. A day of its own, starting from the canvas
(`project/view/where-we-are.md` has the link).

**Another session's work on container networking** was uncommitted on
the laptop when this was written: `plans/container-network.md`,
`learnings/netprobe/`, and edits to `Harbor/Compile.cs`,
`docs/configuration.md`, `CLAUDE.md`, `CHANGELOG.md` and `todo.md`. If
it is not in git, it is on the laptop.

## Owed, small

Done on 2026-11-26, from the desktop: the vocabulary table says
"prediction", the changelog has slice 6, issue #13 is closed, and
`docs/podman.md` section 5 no longer advises opening the host's
loopback. Left:

- A Docker guide does not exist; the Modal guide predates axes
  (`todo.md`, "The container-engine guides").
- What `docs/podman.md` section 5 should offer for a gateway on the
  same machine waits for `plans/container-network.md`.

## Next in the plan

- Slice 6a, what the exam remembers (retired values, why a value is
  there, the moves of live, where users are): each still to confirm.
- Slice 7, port the remaining exams to stages and axes and delete the
  old path. `tern-docs` and `gate-oracle` are done.
- Not built and wanted: which tries are worth paying for, and how many
  a question needs. What is known is in `skills/proctor-answers/`.

## What is not in git

| What | Where on the laptop | Worth carrying? |
|---|---|---|
| The six paid tries | `bank/tern-docs/`, with `log/*tern-docs*` and `exams-seen/` | Yes, 37 MB: they are $3.40 and the first half of the comparison. Copy all three directories to the same places under the other machine's repository root |
| The machine's config | `exams/proctor.json`, `exams/nb.json` | No: each machine has its own |
| The other session's work | the repository's working tree | Only if it was not committed |
| The old viewer checkout | `/srv/aiwork/auspex`: `project/trials/` (7 MB) and `datasets/` (1.9 GB, partly private) | Only if that machine has no copy. Neither goes to GitHub |
| Built images, `.proctor/` | Docker, the repository | No: rebuilt on demand, about 20 minutes for `tern-docs` on a slow line |
