---
source: three studies of where programmers' time goes, listed under "What was read"
read: 2026-11-14, text extracted with pdftotext. Ko et al.: abstract, method, division of labour, search and navigation results, limitations, model. Minelli et al. and Xia et al.: abstract, the headline table, how understanding time is inferred, threats. Figures read as text only
take: (Claude's; the user has not weighed in on these) skilled people on unfamiliar code spend a third or more of their time finding and re-finding things, and the literature treats that as the normal cost of the work and a property of the environment's cues. It gives us a human baseline for the friction share and a model, information scent, for the searches
---

# Where programmers' time goes

Raised on 2026-11-14 with Suchman, as the literature about the
practitioner and not the process.

## What was read

- Ko, Myers, Coblenz, Aung. "An Exploratory Study of How Developers Seek,
  Relate, and Collect Relevant Information during Software Maintenance
  Tasks." IEEE TSE 32(12), 2006.
  https://faculty.washington.edu/ajko/papers/Ko2006SeekRelateCollect.pdf
- Minelli, Mocci, Lanza. "I Know What You Did Last Summer: An
  Investigation of How Developers Spend Their Time." ICPC 2015.
  https://sonar.ch/documents/328164/files/Mine2015b.pdf
- Xia, Bao, Lo, Xing, Hassan, Li. "Measuring Program Comprehension: A
  Large-Scale Field Study with Professionals." IEEE TSE 44(10), 2018.
  https://xin-xia.github.io/publication/TSE17.pdf

## Ko et al. 2006: the lab study

**Setup.** 31 Java developers, analysis on the 10 most experienced. An
unfamiliar 508-line paint program with no documentation or comments, five
tasks (two bugs, three enhancements), 70 minutes, in Eclipse, with
scripted interruptions. Screen video coded by hand. A lab study was
chosen deliberately: on identical tasks, differences are differences in
strategy.

**Where the time went**, of time not spent on interruptions: "about a
fifth of their non-interrupted time reading code, a fifth of their time
editing code, a quarter of their time performing textual searches and
navigating dependencies, and a tenth of their time testing".

**The headline.** "developers spent, on average, 35 percent of their time
with the mechanics of redundant but necessary navigations between
relevant code fragments": 19 minutes each recovering context, iterating
search results, returning from navigations.

**Searches mostly failed.** "An average of 88 percent (± 11) of
developers' searches led to nothing of later use in the task", and
"about 36 percent" of their time went on "inspecting irrelevant code".
The cause is the cues: eight of nine developers on one bug saw a painting
problem, searched for "paint", and landed on a method "which was not
responsible for the bug". Half first opened `PencilPaint` when the
relevant file was "the generically named PaintWindow".

**They re-found what they had found.** Developers returned to relevant
code a mean of 18 times on one task and 12 on another, and "lost track
of relevant code" as tabs and scroll positions were reused.

**They avoided the precise tool.** Every developer used Eclipse's
dependency navigation at least once, but "only two developers used the
tools more than once". The rest used find-and-replace, because the
precise tool "required filling in many details and iterating through the
search results" and left tabs to clean up.

**The model.** Program understanding as "searching, relating, and
collecting relevant information, all by forming perceptions of relevance
from cues in the programming environment", grounded in information
foraging: "information scent: the imperfect 'perception of the value,
cost, or access path of information sources obtained from proximal
cues.'" Two things decide success: "the environment must provide clear
and representative cues", and "a reliable way to collect the information
the developer deems relevant. If an environment does not provide good
cues, it may lead to fruitless investigations; if an environment does not
provide an effective way to collect information, the developer will have
to retrace his steps".

**Limits they state.** Subjective coding of video; a small program;
unfamiliar code; one language and IDE; time pressure. They note
"developers who use command-line tools" might differ.

## The two field studies

**Minelli et al. 2015.** IDE interaction logs: 18 developers, 738
sessions, about 200 hours, 5 million events, in the Pharo IDE.
Understanding about 70%, UI interaction about 14%, outside the IDE about
8%, editing about 5%, navigation about 4%. The 70% is largely a
residual: "small periods of idleness (inter- and intra-activities) are
mapped to program understanding is an explicit assumption that we made,
but indeed they could be mini interruptions". More than half the
sessions come from one developer. Understanding ranges from 41% upward
across developers.

**Xia et al. 2018.** 78 professionals, seven projects, 3,148 working
hours, logged across applications and not just the IDE. Comprehension
58% on average. I did not read the overall split for navigation and
editing; the one row I checked (58.26%, 18.38%, 15.25%, 8.15% other) is
a single developer in the table that validates the tool against manual
labels. "senior developers
spend significantly less percentages of time on program comprehension
than junior developers". Nine root causes of long comprehension sessions
from interviews, including missing comments and searching for relevant
code. The same inference from idle time as Minelli.

## Worth taking

- **A human baseline for the friction share.** Experienced developers on
  unfamiliar code spent 35% of their time on navigation overhead. Our
  Sonnet runs on an unfamiliar tool spent 32 to 35% of spend on friction.
  The measures differ (their time, our dollars; their definition, ours),
  so this is a sanity check and no more: a third is the going rate for
  skilled work on an unfamiliar surface, and it says the friction share
  is not evidence of a weak agent.
- **Friction is charged to the environment's cues.** Ko's developers were
  not at fault for searching "paint"; the name misled them. That is the
  framing for the quiet regime: a failed search is a finding about what
  the docs and names signal. Our topic counts in `regimes.md` (check
  names in 10 runs, the `system` line in 10) are a list of bad cues.
- **Failed-search rate as a measure.** "88 percent of searches led to
  nothing of later use" is a number we can compute: a search is useful
  if something it returned is read, edited or cited later in the run.
  It is rule-made and it scores the surface, not the agent.
- **Re-finding is its own cost.** Returns to the same code are the
  ledger's `reread` line, and Ko ties it to a missing way to collect
  what was found. For an agent that is context: a re-read after a long
  detour may be the cost of the detour.
- **Skilled people choose the crude tool when the precise one costs
  more.** The find dialog over Java Search is `grep` over `--help`. A
  work-around is evidence about the cost of the intended path, which
  fits intended friction: if the intended path is avoided, measure what
  it costs.
- **Seniority reduces the share.** A stronger model should show a
  smaller friction share on the same surface, which is checkable across
  arms, and is why the weaker agent is the better instrument.
- **Compare on identical tasks.** Ko's reason for the lab is proctor's
  reason for samples.

## Not to take

- **Idle time as understanding.** Both field studies get their headline
  from a residual. We have the opposite problem and a better position:
  no idle time, and thinking tokens recorded where they exist.
- **The percentages as targets.** Three studies give 35%, 58% and 70%
  for overlapping things, by different definitions.
- **Tool suggestions for IDEs.** Not our problem.

## Not read

- LaToza, Venolia, DeLine on developers' mental models; Lawrance et al.
  on information foraging in debugging; Pirolli and Card's original
  paper.
