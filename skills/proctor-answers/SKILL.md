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

## How many tries a question needs

Proctor does not work this out yet. You carry it. A person who has just
seen one good run wants to ship; your job is to say what that run can
and cannot show.

What a number of tries can show is fixed by the test proctor uses, and
needs no data. With the same number on each side:

| Tries a side | What it takes to confirm a difference |
|---|---|
| 1, 2 or 3 | Nothing can. No result confirms, however one-sided |
| 4 | Every try on one side must beat every try on the other |
| 5 | At most 2 of the 25 pairings may go the other way |
| 6 | At most 5 of 36 |
| 8 | At most 13 of 64 |

Unequal sides: 3 against 5 can confirm, and only with every pairing one
way. An answer's `power` says the same thing for the tries it has.

Use it like this:

- **Before tries are paid for,** say how many a side the question needs
  and what that costs. Four is the least that can confirm anything, and
  only a difference so large the two sides do not overlap. A small
  change needs eight or more. If the person will only pay for two, say
  that two shows a direction and settles nothing.
- **After one good run,** say it is one run. "Went the way we hoped,
  once" is the whole finding. Do not let it stand as "better".
- **Fix the number first.** Making tries until the result looks right,
  then stopping, finds a difference that is not there. If more tries
  are added after looking, say so when you report them.
- **A size is not asked for.** Nobody can say beforehand how much a
  change will move a check, and proctor's predictions carry a direction
  only. Do not invent one.

## Predictions

A prediction is one statement, written before the tries are made: "if
this configuration is run, this is what will change." It is a file,
`exams/<exam>/predictions/<name>.json`, naming one configuration the
exam names and the changes expected: each a check or a metric (`held`,
`flagged_steps`, `tool_calls`, `tokens`, `duration`) with a direction,
`more` or `less`. It says when it was made, in `written`, a date or a
time in UTC: required, and its author's to set.

```bash
proctor predict <exam>          # pin the checks each prediction was made under, and say how each stands
```

- **Write the prediction before the run,** and suggest one when a person
  proposes a change: it is the reason the tries are being paid for.
  Set `written` to the time you write it. One written after tries of
  its configuration were made is labelled post hoc, and you say so.
- **The report and `compare` say how each change came out:** it went
  that way, it did not happen, it went the other way, or it is not run
  yet. A miss is a finding. Report it as plainly as a hit.
- **A prediction is about one full configuration.** "This value,
  whatever the other axes are" is not a prediction; it is every
  combination, which nobody is going to pay for.
- **If the checks changed after the prediction was pinned,** the answer
  says the standard changed. Pass that on with the outcome.

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
