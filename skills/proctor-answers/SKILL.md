---
name: proctor-answers
description: Answer a person's questions about a proctor exam with axes from what proctor measured — is a change better, where is there no evidence, what could be shipped — quoting proctor's numbers with their scope and a link to the evidence, and never working out a number of your own.
---

# Answering from a proctor exam

A proctor exam with axes varies something (a prompt, the docs, the
agent) one value at a time and keeps every try. The person asking is
usually a product manager or someone building alone, looking at an exam
they did not write or wrote months ago. They want a judgement. Proctor
does not make judgements: it measures, and you make the recommendation.
This skill is how to do that without saying more than was measured.

## The two queries

Both print JSON and call no model. Run them from the repository root,
or pass `--root <dir>` for a repository somewhere else. Each answer ends
with `methods`, which says what its fields mean.

```bash
proctor coverage <exam>                     # where there are tries, and where there are none
proctor compare <exam> <axis>=<value>       # that value against its axis's live value
proctor compare <exam> <axis>=<value> --against <value> --within <axis>=<value>
proctor compare <exam> --configuration <name>               # everything it changes, together, against what is live
proctor compare <exam> --configuration <name> --against <name>
proctor list <exam>                         # the axes, their values and what each is for
```

Start with `list` and `coverage`. Ask `compare` once for each thing the
person is asking about, and only for those.

"What does X change, and how much evidence is there for it?" is one
`compare`. For one value, ask about the value. For something that could
ship, ask about the configuration: it is compared as everything it
changes, taken together. That answer does not say which of its values
made the difference; ask about each value alone for that.

## Rules

1. **Every number you give is one proctor gave you.** Do not average,
   pool, subtract or re-test anything yourself, and do not read the
   bank's files to compute a rate. If the number you want is not in an
   answer, say that proctor has not measured it.
2. **A comparison is true only inside its scope.** Each `compare` answer
   says which settings of the other axes it covered (`cells`,
   `sentence`). Say the scope whenever you say the result. "Better" in
   one setting is not "better".
3. **Not run is not "no difference".** A cell whose state is not
   `compared`, a configuration with no tries, and a value with no tries
   are all gaps. Name them as gaps.
4. **`better_here_worse_there` has no summary.** Say where it was better
   and where it was worse. Do not net them out.
5. **"Not confirmed" and "the same" are not "no effect".** Read `power`.
   With few tries nothing but a landslide can be confirmed, and then the
   answer says nothing either way.
6. **Pass on every caution that bears on what you say** (`cautions`,
   `moved_with`, `timing`). If two axes only ever moved together, you
   cannot say which one did it. If a configuration is "more than its
   values put together", what each value does alone does not predict it.
7. **Link the evidence.** Each answer has an `address`, and each try has
   an `address` and a `path`. Put the address beside the claim it
   supports, so the person can check it. The address includes `as_of`:
   the numbers are as they stood then.
8. **Ask for what proctor cannot know.** How many users are on each
   setting, how far a bad release reaches, how easily it is undone, what
   a bad outcome costs. A recommendation to ship, to ship behind a flag
   or to run more tries rests on those, and they are the person's.
9. **Say what is yours.** Proctor's findings and your recommendation
   are different things. Mark which is which.

## What to make of common answers

- A value confirmed better across every setting that has tries, with no
  cautions: evidence for that value in those settings.
- Better in some settings and worse in others: the difference depends on
  the other axis. Find out from the person how many users are in each.
- Good where run, with settings in `live_span` that have no tries: the
  users in those settings would be the first to try it.
- Nothing confirmed and `power.could_confirm` false: the exam cannot
  answer yet. Say how many tries a side there are, and that more are
  needed before anything can be said.
