---
date: 2026-11-16
status: corrected by the user on 2026-11-16; their notes are the quoted lines and settle the guesses. Step 1 of the design process (journeys, then `inventory.md`, then pages). Drawn from `regimes.md`, `loop.md` and `model.md`
---

# Journeys: where the person is when they open auspex

No screens here. Each journey says where the person has just come from,
what they hold, what they want to walk away with, and what that forces
on the design.

## Who

A practitioner who owns something an agent uses: a tool, its docs, a
skill, a prompt. They can write a proctor exam with help from a coding
session. They are not an evals specialist, and they read transcripts
only when made to. They usually have a coding agent session open beside
auspex, and that session can read the same files.

**Guess:** one person at a time on their own machine. Nobody shares a
running auspex; what gets shared is a report or a link in a PR.

> correct for now. if this ever turns into a business, the value will be in facilitating these multi-user conversations. but for the current scope of auspex, one user, one environment. report or link shared.

## 1. Will it run at all

- **Comes from:** a new exam, or a new fixture, run for the first time
  on a cheap model.
- **Holds:** a handful of attempts, most failed.
- **Wants:** to know whether the failure is theirs (fixture, container,
  network, a check) or the agent's, and where the first attempt died.
- **Forces:**
  - The first thing shown is what broke before the agent had a fair
    go, not a pass rate.
  - Invalid attempts must be visibly kept out of every later number.
  - `regimes.md` says a coding session already does most of this well.
    auspex may only need to point at the step and stay out of the way.
    
> agree. most of this is proctor producing agent-friendly reports. i would not expect anyone to run stuff from proctor by hand, but to instead stage by asking claude code to create a proctor exam. we just need a nudge somewhere in there to say "hey an exam is basically code. you can check that it compiles, but you also need to check to make sure it doesn't crash when it runs."

## 2. It passed, and I do not believe it

- **Comes from:** a batch on a frontier model, everything green.
- **Holds:** attempts that pass, friction points proctor coded on them.
- **Wants:** to see where effort was wasted or the surface under test
  was avoided, and to decide for each whether it matters.
- **Forces:**
  - This is the main journey; a green batch is the normal case now.
  - The entry point is what recurs across attempts, not one attempt.
    One attempt's oddity is noise until it shows up in its siblings.
  - Every point must open on its step with the quote marked, because
    the person will not trust a label they cannot check in one move.
  - There will be many points and most will be left alone. Leaving one
    alone has to cost nothing.
    
> this is also a step where the user is most likely to dig into the actual conversations and final state of the fixtures. basically they're trying to see if the agent actually did complete the task, that the output is of expected quality, and that the agent didn't cheat. this is sort of a user alignment step. sometimes the user will express a goal, then only on seeing the output will understand that they didn't express the goal clearly or that that their thinking was off. this is why we did paper prototypes back in the old days. sometimes it's hard to understand something until you actually see it.

### Four classes of run, and why friction exists (2026-11-22)

From the user, 2026-11-22: runs broadly fall into four classes.

| Class | Checks | Friction | What friction tells you | What the person does |
|---|---|---|---|---|
| 1. Any model, task impossible, fails | red | searching that finds nothing (`wasted-read`, `bad-instruction`) | there was nothing to find | fix the exam, or record the gap in the surface as the finding |
| 2. Frontier model, task possible, tools fight the model, succeeds | green | resistance (`harness-refused`, `inline-script`, `used-substitute`, `fell-back`, `machine-refused`) | the only signal there is | fix the tools; the pass hides the problem |
| 3. Weaker model, task possible, tools fight the model, fails | red | the same resistance | why it failed | the same fix as class 2 |
| 4. Any model, task possible, tools well-suited, succeeds | green | little | nothing to see | leave quickly |

> the 2-vs-4 thing is the whole reason we're on friction. opus 5.5 is reliably able (given as bash tool) to power through vague goals, sketchy data, and unclear tools. it doesn't feel right to grade that experience "100% full success"

What follows:

- **Classes 2 and 4 look the same in checks.** Only friction separates
  them. So a pass is never shown without its flagged steps beside it.
  The pass stays a pass: friction never tints or qualifies the result
  (proctor's `check-evidence.md`, C5.2: no severity, no lesser pass).
  It sits next to the result in its own colour, as a count. There is no
  "heavy" threshold: one try is placed against its siblings ("5 flagged
  steps; the other tries had 1 to 2"), not judged. The exam page is
  where class 2 has to be caught: nobody opens a green try without a
  reason.
- **Classes 2 and 3 are one problem seen through two models.** The fix
  is the same and the friction is at the same steps. Showing that
  needs no transcripts side by side (still out of scope), just a count
  across two banks: "where the frontier model met resistance is where
  the weaker one failed". Proctor writes that count; auspex shows it.
- **Classes 1 and 3 both fail with friction.** The kind of friction may
  tell them apart (searching that finds nothing against tools that
  fight back). That's a hypothesis for proctor to test on `datasets/`,
  not a rule. The strong evidence that a task is possible is a passing
  run (a reference or oracle attempt, or any setup that passed), which
  proctor already has the means for (`validity` checks, the gate
  oracle).
- **Class 1 has two causes the transcript can't tell apart:** a broken
  exam (a bug) and a surface missing the information (the finding;
  `clear-wait` in the mocks is this kind). The page points the person
  at the exam or the surface, not the agent: absent evidence plus a
  `wasted-read` point.
- **Class 4 is most tries and must cost nothing:** one line on the exam
  page, no reason to open it.
- **No class label from auspex.** If a try is ever tagged with a class,
  proctor writes the tag, or the session says it in prose with its
  counts from a query. auspex shows the result and the friction beside
  it. This is `regimes.md`'s quiet regime and `off-path.md`'s
  hypothesis, seen from the person's side.

### Three levels, three questions (2026-11-22)

From the user, thinking out loud:

> at the setup level, i'm trying to figure out if my treatment improved
> or worsened outcomes/behavior ... failed checks is one; it's a simple
> integer or proportion. friction could be another, if we had a
> similarly simple way to summarize it
>
> at the try level, i'm honestly just looking for variance. are we
> hitting the same issues or is every run doing something different
>
> at the stage level, i'm debugging and trying to understand the why
> behind the friction. this is the point where i need the transcript

| Level | Question | Measure | Where |
|---|---|---|---|
| Setup | did my treatment make things better or worse? | headline checks held per try and flagged steps per try, each as a spread across tries, against live; both confirmed over tries | top of the exam page |
| Across tries | is this happening all the time or rarely, and which costs most? | one row per kind of friction: in k of n tries, min / median / max flagged steps when it happens, flagged steps per try overall | exam page, under the outcome grid |
| Stage | why did this friction happen? | the transcript, with each point's `because` | the stage page (`stage-page.md`) |

- **Flagged steps per try is the simple friction summary.** It's an
  integer per try, like failed checks.
- **The spread answers "is it real?"** Each setup shows its range
  across tries next to live's. If the ranges don't overlap, the
  treatment did something, and no statistic needs explaining.
  Proctor's verdict word (better, worse, no detectable difference)
  sits beside it. This also makes the try the unit for confirming a
  difference, not the stage (proctor's `check-evidence.md`, C5.4, the
  open question on the unit).
- **Friction renders per kind, not per try** (the user, 2026-11-22):
  "all tries should represent the same thing, same environment, same
  engine", so a column per try is noise. A kind is a label and what the
  rule fired on ("jot: command not found"); a reader's points group by
  label alone. Each row answers two questions:
  - **constant or rare**: in k of n tries;
  - **how bad when it happens**: min / median / max flagged steps over
    the tries that had it, so a rare but costly kind (one try sank 9
    steps into an inline script) stands apart from a constant cheap one;
  - **which first**: flagged steps per try overall, which folds both
    into one number; rows sort by it.
- **No per-try friction anywhere else.** The outcome grid stays (results
  do vary between tries), but its cells carry no friction count, the
  try rows have no flagged-steps column, and the stage page doesn't
  place a stage against the other tries. The setup comparison keeps its
  spread of flagged steps per try: that's a distribution, not a
  profile.
- **A single try has no page of its own** beyond the frame around its
  stages. "Try" as a level means comparing tries, on the exam page.

## 3. Some passed and some failed

- **Comes from:** a mixed batch.
- **Holds:** siblings on the same task with different outcomes.
- **Wants:** what the failures did that the passes did not, and whether
  it is rare bad luck worth chasing.
- **Forces:**
  - Comparison is between siblings on one task, by variant and outcome.
  - A failure's signature (which check, what reason) must separate a
    budget cap, a hard check and a mis-tuned judge.
  - The price to chase (`regimes.md`) is a number the person needs
    before spending anything.

## 4. Turning a friction point into a check

- **Comes from:** journey 2 or 3, with one point they have decided
  matters.
- **Holds:** the point, its step, the attempt.
- **Wants:** a proctor check that fails on this attempt for this
  reason, tried before it is trusted.
- **Forces:**
  - The check is written in proctor, by the person or their session.
    auspex hands over enough for the session to write it: the point,
    the step, the quote, the paths.
  - auspex records that this point now has a check, and which.
  - Needs `grade --try` in proctor, which is not built.

## 5. I changed something; did it help

- **Comes from:** a second variant run against the live one, with a
  claim about which checks should flip.
- **Holds:** two variants' attempts on the same tasks, and the claim.
- **Wants:** did the claimed thing move, and what else moved that
  nobody predicted, including new friction.
- **Forces:**
  - The reference is the live variant, always.
  - Proctor's report gives the verdict on the claim. auspex's part is
    the unpredicted: friction that is new in the change, or gone.
  - That needs a rule for "the same friction point in another attempt",
    which `loop.md` lists as new and unproven.
  - Claims are not designed in proctor yet.

## 6. Showing someone else

- **Comes from:** any of the above, with a PR to write.
- **Holds:** a pick or a change, and what they found.
- **Wants:** evidence a reviewer who knows less than they do will
  accept: "it basically works and I looked" in the first mode; "it
  helps and breaks nothing" in the second.
- **Forces:**
  - The reviewer will not run auspex. Whatever is shown has to stand as
    a static page or a paragraph with links.
  - **Guess:** this belongs to proctor's report, with auspex supplying
    links to steps. If the reviewer should be able to open a step,
    that is the static demo in `demo.md`.
    
> it may be (or become) the case that no one ever reads the evidence, and they just trust the robot. that's fine. the important part is that if anyone pushes back on a prompt/doc/tool change, the proposer (1) has receipts and (2) has a path to run more reps if someone complains "oh that's like 3 trials, we need to see at least 27 to prove this doesn't introduce regressions."

## 7. Coming back later

- **Comes from:** a week away, or a new batch on an exam already
  reviewed.
- **Holds:** earlier dispositions and checks.
- **Wants:** not to review the same friction again.
- **Forces:**
  - Dispositions outlive a batch, so they are keyed to something
    steadier than an attempt.
  - Same unproven rule as journey 5.
  
> a realistic version of this is user-feedback. users don't always complain immediately. often we'll bump into someone at a conference or user group meeting and they'll say "omg you changed X six months ago and I hate it every day." so when we hear something like that, it will be nice to have evidence for "yup, that really did change and it's really been like that, and this new thing fixes that."

## What I am least sure of

1. Whether journey 1 is auspex's at all, or stays with the coding
   session.
   
> probably coding session
   
2. Whether a person compares more than two variants side by side
   (the first mode), or picks by the report and opens auspex only on
   the pick.
   
> I'm going to make a command decision for "no" here. we're already eating an enormous amount of complexity for "explain to me how all 10 runs went." I don't want to buy the extra complexity of "now explain how that trajectory has changed over the last few changes."
   
3. How much the person does in auspex and how much they tell their
   session to do while auspex is the thing both are looking at.
   
> I'd lean toward coding agent interactions first. Just because your agent is more flexible at fixing crap. 
   
4. Whether a second reader (journey 6) ever needs more than a page.

> I think at some point you just locally host auspex if you have enough people using it as a centerpiece for collaboration. Start local, expand out when you need it.
