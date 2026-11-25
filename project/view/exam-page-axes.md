---
date: 2026-11-24
status: rewritten 2026-11-24 after proctor's `axes-build.md` slice 3c, and built the same day as a first cut for the user to react to (`View/AxesPages.cs`). It replaces the proposal of the morning, two of whose points no longer held ("the grid leads", "the exam as given"). Short on purpose. The earlier `exam-page.md` (2026-11-22, setups against live) still describes an exam without axes
---

# The pages of an exam with axes

## What changed since the morning

- **Proctor does no decision support** (proctor's `axes.md`, decision
  17). The pages are for inspecting evidence and for opening a claim
  an agent made. A judgement is the agent's, in the conversation.
- **An axis is not narrow.** A dozen axes is ordinary, so there is no
  grid of every combination. A page lists the configurations somebody
  named or ran.
- **No section of agent-written findings** (the user). The mock's "Your
  agent's analysis" is gone. An agent's mark on a step stays, on the
  stage page, since it points at evidence.
- **A value is not shown as a diff.** It can be a database or a whole
  site. A page gives the author's sentence, what the value carries and
  its hash, each marked as whose it is.

## The pages

Every address below is one `proctor compare` or `proctor coverage`
writes into its answer, so a number an agent quotes can be opened.

| Address | Shows |
|---|---|
| `/<exam>` | The axes with each value's tries; the configurations named or run, each with its tries, the newest and what is known about it; what is live across the axes with no live value. `/<exam>/coverage?as_of=` is the same as of a time |
| `/<exam>/configuration/<key>` | One configuration: per axis the value, the author's note, what it carries, its hash; whether it is its values put together; its tries |
| `/<exam>/compare?change=…&against=…&within=…&as_of=…` | One change against another: proctor's sentence, its cautions, each setting with every try's count linked to the try, the counts per check, the reading as one, what so few tries could show |
| `/<exam>/try/<id>/<stage>` | The stage page, as built. `?check=` or `?point=` selects a verdict or a friction point |

## Rules the pages keep

1. **Every number is proctor's.** The viewer calls proctor's code
   (`View/Measured.cs`) and renders what it gets. It computes nothing.
2. **A number's address says as of when.** Opened later, the page
   shows the number as it stood then and, when tries were made since,
   the sentence as it is now beside it.
3. **Not run is a row.** A named configuration with no tries, a setting
   with one side missing, a value with no tries: each is on the page as
   itself.
4. **Whose words.** The author's note is labelled the author's.
   Proctor's sentence is proctor's. Nothing on a page is an agent's.
5. **Each page says what to ask an agent,** with the command the agent
   would run. It is the one place a page points away from itself.

## Left for the user

- **How it looks.** Tables, the existing type and colours, no chart.
  Nothing here was drawn on the canvas first.
- **What the "ask your agent" box says.** A first wording is on the
  exam page and the comparison page.
- **The stage-by-try grid, friction per kind and "every try"** of
  `exam-page.md` are not on the new exam page. They read proctor's
  report (`stats.json`), which the pages do not use yet; a
  configuration's page lists its tries as links only.
- **`exam-page.md` and `where-we-are.md`** still speak of setups, and
  are not revised.
