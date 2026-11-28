---
type: plan
title: The site's IA — pages, sections, and what each is made of
created: 2026-11-27
status: outline, for review. No copy yet. Feeds the layout work in Design (site.md)
---

# The site's IA

What is on auspex.bot, page by page and section by section. Each
section has a job (one sentence), the kinds of element it is made of, a
rough length, where its facts come from, and whether it can be written
now. This is enough for the layout to be drawn before the copy exists,
and for the copy to be written into shapes the layout already holds.

Words are counted for the copy that will be on the page, not for this
outline.

## Pages

| page | address | for | status |
|---|---|---|---|
| Front door | `/` (`docs/index.html`) | a stranger deciding whether this is for them | below |
| For your agent | `/agent.md`, linked as a page and also served raw | the coding agent the user hands auspex to, and the user reading over its shoulder | below |
| Providers | `/providers.html` (anchors fixed) | someone `auspex setup` sent to get a key | exists, restyled |
| Remix | `/remix/` (`docs/remix/`) | someone building on auspex | waits on `remix.md` steps 1–2 |
| Hosted reports | `app.auspex.bot` | outside the site: linked, never framed | `report-hosting.md` |

The guides already in `docs/` (`getting-started.md`, `cli.md`,
`configuration.md`, `podman.md`, …) are served as they are and linked from
the front door and the agent page. The site does not rewrite them.

Nav, on every page: auspex (mark and name), Reports, What you'll need,
For your agent, Remix, GitHub. Six items is enough. The sections between
those are reached by scrolling, not by the nav.

## Front door

About 1,200–1,500 words in all. That is a long scroll with short sections,
not a wall of text. The sections in order:

### 1. Hero: what it is

- **Job**: in ten seconds, what auspex does and for whom.
- **Elements**: a headline (under ten words), a sub-line of two
  sentences, two buttons ("See a report", "What you'll need"), and one
  picture: a still of a real viewer page (the exam page of `tern-docs`),
  cropped and linked to the live one.
- **Length**: about 50 words.
- **Facts**: the README's opening. Runs real agents in sandboxes, against
  your tools, docs or prompt. Grades every attempt. Reports what changed,
  with the uncertainty stated.
- **Status**: can be written now. The still waits on a public snapshot.

### 2. The problem, and who it is for

- **Job**: make "my product has a new user, and I have no idea how it's
  doing" land, then say who gets value from this.
- **Elements**:
  - one short paragraph
  - three findings from outside, each a claim, a number, and the source
    linked (Vercel's skill that agents never opened, Microsoft's script
    that ran in no runs, Stripe's agents reporting success after a 400),
    as three cards or a tight list
  - a "who it's for" list: about four kinds of builder, one line each,
    each line naming what is under test
  - one "not for" line: leaderboards, single-turn prompts
- **Length**: about 250 words.
- **Who**, as a first draft: you ship an MCP server, CLI, API or docs that
  agents use; you maintain a skill, a plugin or a CLAUDE.md; you run an
  in-product assistant or a headless agent job; you pick which agent and
  model your team uses, and want to know whether a change helped.
- **Facts**: the old page's "The problem" (check every citation again),
  the brief.
- **Status**: can be written now.

### 3. What sets it apart

- **Job**: what an expert would check before taking it seriously, each
  point answered in a line.
- **Elements**: a grid of six claims. Each is a bold claim, one sentence
  of how, and a link to where it is documented. No icons that need a
  designer to invent them.
- **Length**: about 300 words.
- **The six**, as a first draft:
  1. **The real harness.** Claude Code, Codex and Qwen Code at pinned
     versions, through Harbor, one container per attempt, with the network
     limited to the model's host. Models are tuned to their harness, so
     anything else measures what your users never see.
  2. **Checks that don't trust the agent.** Deterministic wherever they
     can be: scripts, acceptance tests in a separate offline verifier, and
     counts taken from the trajectory, never from the agent's own summary.
     Where a model must judge, every quote it gives is checked against the
     transcript, several votes must agree, and a split is "undecided",
     never a default.
  3. **Numbers that say how sure they are.** Every rate has a 95% interval
     and its counts, and comparisons are paired by task. The report says
     "could not tell" when the sample cannot tell.
  4. **One thing changed at a time.** An exam has axes (docs version,
     agent, model, …), and a comparison holds every other axis still.
     A prediction, written before the attempts, is held against them, with
     a miss reported as plainly as a hit.
  5. **Results that accumulate.** Every attempt is kept in the bank. A new
     check regrades what is already there and runs nothing again. When the
     standard moves, the report says so, because a number can move with
     nothing re-run.
  6. **It can play the user.** For assistants that should ask before
     acting, an answer sheet holds what the user knows but did not say,
     and the transcript records whether the model asked or guessed.
- **Plus one line under the grid**: no model writes any of the report. A
  judge's part ends at a label and a quote.
- **Facts**: `CLAUDE.md` conventions, `docs/configuration.md`.
- **Status**: can be written now. Each claim gets checked against the code
  before launch.

### 4. How it works

- **Job**: the loop a user goes through, so "an afternoon with my agent"
  feels plausible.
- **Elements**: five numbered steps, each a bold verb phrase and one or
  two sentences. Optionally one simple diagram: exam → attempts → checks →
  report, with "change one thing" looping back.
- **Length**: about 200 words.
- **Steps**, as a first draft: plan the exam with your coding agent (the
  skill does the interviewing, and you approve a plan); prove the checks
  for free (shakedown scripts, nb's Mock); try it on a cheap or local model;
  run the real agents; read the report, then the transcripts; change one
  thing, run again, compare.
- **Facts**: the old "How you use it", `skills/proctor-exam/`.
- **Status**: can be written now.

### 5. Reports (the evidence)

- **Job**: show a finished report before anyone reads a config file.
- **Elements**: two or three report cards. Each has a kicker (exam ·
  date), the question in a sentence, a facts line (configurations, tasks,
  attempts, judge), the finding in one line, and "Open the report →" to
  its stable public URL. Under the cards, one line: "these are small
  exams, and the reports say what they cannot tell."
- **Length**: about 150 words.
- **Cards**: `tern-docs` (with planted failure modes), and the exam on our
  own docs (can a coding agent build a working exam from auspex's docs
  alone? `agent-onboarding`).
- **Facts**: the reports themselves.
- **Status**: waits on public snapshots (`report-hosting.md`) and on runs
  worth showing.

### 6. What you'll need

- **Job**: an honest checklist, so nobody installs for an hour and then
  finds a key they cannot get.
- **Elements**: a table with three columns: what, what it's for, and
  whether you can do without it. Then a two-line escape hatch: start with
  `playground`, which needs nothing, and if the rest can't be met, see
  Remix.
- **Rows**, as a first draft:

  | what | for | without it |
  |---|---|---|
  | a coding agent you already use | it writes and runs exams with you, from the skill and the agent page | possible, by hand; the docs are written for an agent |
  | Linux with rootless podman, and uv | the sandbox the agents under test run in | only bare nb runs, no coding agents |
  | the .NET 10 SDK and git | building auspex and nb from source, until binaries ship | — |
  | keys for the agents you want to measure | Anthropic for Claude Code, OpenAI for Codex, and so on | measure nb on a local or free model |
  | a key for a frontier judge, from a family you are not measuring | checks only a model can grade. A judge from the same family as an agent under test grades its own kind, so it sits off-distribution: GLM or Kimi through OpenRouter or z.ai, beside Claude and GPT | use deterministic checks only |
  | a key for a decider (a System One endpoint, such as Jev) | classifier checks | skip them |

  Below the table, the usual set spelled out, since "four keys" is the
  surprise: **a normal sweep of Claude Code and Codex needs an Anthropic
  key, an OpenAI key, a key for the judge's provider (OpenRouter, z.ai, …),
  and a key for the decider's (OpenRouter, Typesafe, …).** Then the
  escape hatch: fewer agents need fewer keys, a deterministic exam needs
  no judge, and `playground` needs none at all.

- **Length**: about 200 words with the table.
- **Facts**: setup's jobs (`SetupState.JobOrder`, `Setup/providers.json`),
  the README's Install, `docs/podman.md`. The supported platform is
  rootless podman on Arch and Ubuntu, and the page says only that.
- **Status**: can be written now. The rows follow setup's jobs, so that
  the two cannot drift.

### 7. Where your data lives

- **Job**: replace "not a cloud service" with the true version, in two
  lines.
- **Elements**: two short columns. On your machine: exams, the bank,
  transcripts, keys; the only traffic out is the agents' model calls and
  the judge's. Hosted, only when you publish: reports, at app.auspex.bot,
  invite-only alpha, plus public shares.
- **Length**: about 80 words.
- **Status**: can be written now. The hosted half's words follow
  `report-hosting.md` once it is built.

### 8. Remix, the license, the repository

- **Job**: say it is built to be built on, and that it is free.
- **Elements**: a paragraph (other people's code stands beside auspex: it
  reads the files and calls the commands, and what is promised is written
  down), a link to `/remix/`, then the license (MIT) and the GitHub link.
- **Length**: about 100 words.
- **Facts**: `remix.md` ("Two ways to remix, and only one is promised").
- **Status**: the paragraph can be written now. The link waits on
  `docs/remix/`.

### Footer

The name, MIT, the repository, the credit for the new logo if it needs
one. No hat.

## For your agent (`/agent.md`)

- **Job**: one document a user hands their coding agent, which gets it
  from nothing to a planned exam. The user's whole instruction is "read
  auspex.bot/agent.md and help me set up auspex".
- **Form**: markdown, served raw so an agent can fetch it, and as a page
  so a person can read it. Linked from `llms.txt`.
- **Sections**, as an outline:
  1. What auspex is, in three sentences: enough for the agent to explain
     it back.
  2. Ground rules for the agent: keys go in at the user's own secret
     prompt, never through the agent; no hosted model is run without the
     spend agreed first; `setup --check` before anything else.
  3. Install, and check the machine: the commands, then
     `exams/sandbox-plan.md`, which setup writes as a brief for the agent.
  4. Setup: what the user does in `auspex setup` (interactive), and what
     the agent reads back with `setup --check`.
  5. The first report: `playground`, with no model and no key.
  6. Planning an exam: install `skills/proctor-exam/` (it is the steering)
     and follow it. The plan is approved by the user before anything is
     built.
  7. Where to look next: `docs/configuration.md`, `docs/cli.md`, the
     example exams.
- **Length**: short, about 800 words. The skill and the guides carry the
  rest, and this page points at them.
- **Open**: whether this is a new document or `getting-started.md`
  rewritten for an agent. One set is the goal (`site.md`), so if
  `getting-started.md` can serve both readers, that is better.

## Open

- The hero picture: a still of the viewer, or a short loop of it?
- Whether "What sets it apart" holds six claims or four. Six is the
  honest count. Four would read faster.
- Whether the guides in `docs/` get the site's style or stay as plain
  rendered markdown.
