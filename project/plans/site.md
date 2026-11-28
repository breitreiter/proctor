---
type: plan
title: The site — auspex at auspex.bot
created: 2026-11-27
status: started 2026-11-27. Nothing built. The sections and what each waits on; the copy comes next
---

# The site

The public site for the tool, at auspex.bot. We sell nothing, so it has
one job: a stranger finds out what this is, whether it is for them, what
they need to start, and sees finished reports as evidence.

## Decided (the user, 2026-11-27)

- **The name is auspex, at auspex.bot.** The site is written in the new
  name, though the binary, the variables and the skills keep "proctor"
  until stage 2 of `rename-to-auspex.md`. So the site does not launch
  before that rename, or every command it shows is wrong on the day.
- **The hat is retired.** It needs a new logo (`todo.md`, "A new logo").
  Nothing on the site uses the hat, so its CC BY credit goes too.
- **Report hosting is the one cloud part.** The old page's "Not a cloud
  service" no longer holds as written. Say what is hosted (reports, to
  share) and what never is (exams, the bank, the runs, the keys).
- **The look is the viewer's**: Web Awesome and its theme tokens from
  `View/assets`, with no hand-rolled type sizes
  (`component-library-over-hand-rolled`). The old page's purple goes.
- **One set of agent docs, in markdown**, unless it turns out to need
  two.

## Sections

| section | source of truth | borrow from the old `docs/index.html` | waits on |
|---|---|---|---|
| What it is | the brief, the README's opening | the hero's sub-line: real agents, sandboxed, graded, reported | nothing |
| What sets it apart | `CLAUDE.md` conventions: the real harness through Harbor, checks with verified quotes, paired intervals, counts taken from the trajectory, no model-written report, axes and predictions | "How it grades", "The report", the as-of-November comparison, "What it is not" (reworded for hosting) | nothing |
| Who it's for, and the problem | the brief | "The problem" with its citations (Vercel, Microsoft, Stripe) and the agent-experience naming. Check every citation again before reuse | nothing |
| Evidence: shared reports | hosted auspex pages on `app.auspex.bot` | the report-card pattern (kicker, question, facts, open) | `report-hosting.md`, and pages a stranger can open without an invite (below); runs of `tern-docs` and the exam on our own docs, scrubbed of host, gateway and machine names |
| Remix, license, repo | `remix.md`, `docs/remix/` | the license line | `docs/remix/seams.md` and `promises.md`, which do not exist yet |
| What you'll need | setup's jobs (`SetupState.JobOrder`, `Setup/providers.json`): a coding agent to drive it, podman, keys for the harnesses under test, a frontier judge, a decider | "Get started" | nothing; the remix pointer for "if you can't meet these" waits on remix |
| Agent setup and steering | `skills/proctor-exam/`, `docs/getting-started.md`, `setup --check`, `sandbox-plan.md` | nothing | the rename, for names; measured with `agent-onboarding` before launch |
| Providers | `docs/providers.html` | the whole page, restyled | nothing |

## Fixed before launch

- **`providers.html` and its anchors are permanent.** `proctor setup`
  prints `<SiteBase>providers.html#<kind>`. `Catalogue.SiteBase` is
  still `breitreiter.github.io/proctor/`. Move it to auspex.bot with
  `SetupTests` before setup is released, and keep every anchor.
- **Every claim is runnable from the README** (the old todo's rule): read
  the page against the repository, line by line, before it goes up.
- **Public words**: evals, measure, grade, and never an "AI testing
  product". "Evals" in the general sense is welcome. Name no employer,
  and anonymise the precedent the brief leans on.
- A weekly link check (lychee) over the vendor links, once it is live.
- The old `docs/reports/*/report.html` pages are from the HTML report
  dropped on 2026-11-22. They do not go on the new site.

## Order

1. A shell: one page on the viewer's tokens, a placeholder mark where
   the logo goes, the nav, and `providers.html` ported with its anchors.
   It settles the look before the copy goes in.
2. Copy for the sections that wait on nothing: what it is, what sets it
   apart, who it's for, what you'll need.
3. The agent docs: one markdown set, measured with `agent-onboarding`.
4. Evidence, once report hosting serves real pages.
5. Remix, once `docs/remix/` exists.
6. Launch after the rename: the line-by-line read, `SiteBase`, the link
   check.

## Where it is served (2026-11-27)

- **The front door is GitHub Pages** at auspex.bot (a custom domain on
  the repository's Pages), from `docs/`, where `providers.html` already
  is and `SetupTests` reads it. `docs/` also holds the markdown guides.
  Pages renders those too, which is fine as long as the guides link to
  each other relatively.
- **Hosted reports are `app.auspex.bot`**, on Azure, behind a Google
  login. That is its own plan, `report-hosting.md`. The site links to
  it and does not depend on it, apart from the evidence.

## Evidence (2026-11-27)

- **`tern-docs`**: clean, on axes, and the exam to plant failure modes
  in, so the report has something to show.
- **The exam on our own docs**: can a coding agent build a working exam for an app,
  given only auspex's docs and skill? That is `agent-onboarding` (and
  `exam-authoring` before it). It faces inward first. On the site it
  answers the obvious sceptic's question: do your own docs work?
