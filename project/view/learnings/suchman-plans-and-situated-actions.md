---
source: https://www.cs.colby.edu/courses/J16/cs267/papers/Suchman-PlansAndSituatedActions.pdf (Xerox PARC technical report ISL-6, February 1985; the thesis that became the 1987 book)
read: 2026-11-14, text extracted with pdftotext from a 163-page scan. Read: the abstract, the preface's navigation passage, the passages on plans in chapters 3 and 4, the study's setup and the two trouble types in chapter 7, and the conclusion. Chapters 2, 5 and 6 (interactive artifacts, communication) were not read. The transcripts did not survive extraction well
take: the user's (2026-11-14): we may be applying a structure built for line workers to work that is closer to a technician's, programmer's or artisan's, open to variance in the practitioner's approach. Claude's: this is the source for that position, and it also names the two cells of our surprise table we could not fill
---

# Plans and Situated Actions

Lucy A. Suchman. "Plans and Situated Actions: The problem of
human-machine communication." Xerox PARC ISL-6, 1985. An
anthropologist's study of people using a photocopier whose help system
was built on a plan-recognition model of the user.

## The argument

Two views of purposeful action. The first "views the organization and
significance of action as derived from plans, which are prerequisite to
and prescribe action". The second "treats plans as derivative from
situated action. Situated action as such comprises necessarily ad hoc
responses to the actions of others and to the contingencies of particular
situations" (abstract).

The opening image, quoted from Berreman, is two navigators. The European
"begins with a plan-a course" and works to stay on it. The Trukese
navigator "begins with an objective rather than a plan. He sets off
toward the objective and responds to conditions as they arise in an ad
hoc fashion ... If asked, he can point to his objective at any moment,
but he cannot describe his course". Suchman's claim: "the essential
nature of situated action, however planned or unplanned, is Trukese."

What a plan is, on this view:

- "Stated in advance, plans are necessarily vague, insofar as they are
  designed to accomodate the unforseeable contingencies of actual
  situations of action. Reconstructed retrospectively, plans
  systematically ignore the necessary ad hoc ness of situated action in
  favor of an account of the action as in accord with the plan."
- Plans "are resources for people's practical deliberations about
  action", "not as the generative mechanism of action".
- The vagueness is not a defect: it "is ideally suited to the fact that
  the detail of intent and action must be contingent on the
  circumstantial and interactional particulars of actual situations"
  (conclusion).

On instructions, citing Amerine and Bilmes: "Successfully following
instructions can be described as constructing a course of action such
that, having done this course of action, the instructions will serve as
a descriptive account of what has been done." Instructions "filter out
of the retrospective account of the action, or treat as 'noise,'
everything that was actually done that the instructions fail to mention".

## The study

"a corpus of videotapes of first-time users of the 'expert help system'"
on a copier. First-time users because the system was meant to be
self-explanatory, and because their troubles "disclose work required to
understand the system's behavior that ... is masked by the proficient
user." People worked in pairs where possible, since each then says aloud
what she thinks is going on.

The transcripts are laid out in four columns: the user's actions not
available to the machine, the user's actions available to the machine,
the machine's behaviour available to the user, and the design rationale.

The system's design "predicts some intent on the part of the user" and
ties instructions to it. The result is "an insensitivity to actual
circumstances that is both the system's central resource, and its
fundamental problem."

Two kinds of trouble (7.5):

- **The false alarm.** "a misconception on the user's part produces
  evidence of an error in her actions where none exists". The user does
  the right thing, expects a different effect, and concludes she went
  wrong.
- **The garden path.** "a misconception on the user's part produces an
  error in her action, the presence of which is masked." The user "takes
  an action other than that prescribed by the design, but one that
  satisfies the procedural requirement", the system mistakes it for the
  correct action, and both carry on.

"In both cases, the user's trouble is unavailable to the system." The
conclusion puts the cause as "the asymmetrical access of user and machine
to the situation of action": the machine "is mapping a restricted set of
effects, left by the user's actions, onto a prescribed template of
possible meanings."

## Worth taking

- **The position itself.** A reference run is one navigator's course, not
  the route. The alignment test (`process-mining.md`) found two runs of
  one task share a third of their actions in order, which is what this
  predicts. Compare runs on the objective and on what each met along the
  way, not on the course.
- **An account written afterwards will agree with the plan.** "Plans
  systematically ignore the necessary ad hoc ness" is a caution for
  `direction.md`: a model's summary of a run, and the agent's own closing
  message, are retrospective accounts and will tidy the run into its
  plan. The detours are what such an account treats as noise, and they
  are what we are looking for. This is an argument for the symptoms being
  rule-made from the steps, with the prose account on top.
- **The false alarm and the garden path are the two empty cells.** In the
  surprise table (`surprise.md`), "expected result, agent reorients" is
  the false alarm, and "odd result, agent carries on" is close to the
  garden path. Her definition of the second is sharper than ours: the
  action is wrong for the design, satisfies what the system checks, and
  is taken as correct by both sides. That is a pass that left the surface
  under test (`off-path.md`), and a vacuous check.
- **The four columns.** What the agent thought, what it did, what came
  back, and what the surface's designer intended at that point. The first
  three are in an ATIF step. The fourth is the written intent we do not
  have, and her layout shows what a run looks like once it exists.
- **First-time users disclose the work.** The published version of the
  Qwen shakedown: a weaker agent shows the work a stronger one masks.
- **Our checks have the machine's blind spot.** A check maps "a
  restricted set of effects" onto "a prescribed template". The trouble
  between the effects is unavailable to it, which is the case for reading
  trajectories at all.

## Not to take

- **The method.** Conversation analysis of video by hand, a few
  sessions, no counts. It establishes kinds of trouble, not rates.
- **The conclusion for design.** Her remedy is richer mutual access
  between user and machine. Ours is a person fixing the surface between
  runs, a different loop.
- **A reading that plans do nothing.** She says they are resources. A
  suite's stated intents and a tool's usage rules are still worth
  writing and checking; what fails is treating the sequence of steps as
  the thing to match.

## Not read

- The 1987 book and the 2007 second edition, which has her replies to
  critics. Chapters 2, 5 and 6 of this report.
