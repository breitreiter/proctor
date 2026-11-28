# nb-changes

From a bug report or a written contract, can a shipped coding agent make a change to nb, a .NET codebase of
about 13,000 lines that it cannot read whole? Five stages report a defect in nb at a pinned revision (four of
them planted, one of those two defects at once; the fifth reports documented behaviour as a bug); one asks for
a feature. The plan, with why each has the shape it has, is `project/archive/debugging-suite.md` (written when
they were tasks).

What is planted is an axis, `planted`, whose values each name a fixture; a stage belongs to the value it
reports, so a try is the one or two stages of one repository. Read the results per kind: a stage carries no
label, so the kind is in this table and not in the files.

| Stage | Kind | The agent is given | nb's own tests |
|---|---|---|---|
| `red-test` | defect | "the build is red" | 5 fail |
| `report-only` | defect | a user's report with a program and what it compiles to | all pass |
| `two-causes` | defect | a user's report; a second defect shows once the first is fixed | 10 fail, then 1 |
| `far-cause` | defect | a report that some transcripts replay as seeds with a message out of place | all pass |
| `not-a-bug` | defect | a report of documented behaviour | all pass |
| `ask-user-question` | feature | an eight-point contract for a new tool | all pass |

## Fixtures

Each planted defect has its own fixture (`fixtures/nb-<value>`), so no stage's defect is in another's
repository. `not-a-bug` and `ask-user-question` share `fixtures/nb-pinned`, nb unchanged (`planted = none`). A fixture's source is
nb's repository at the pin; the first `image.run` line applies `plant.patch` (carried in the line as base64)
and makes the repository again, so the plant is in the only commit. Change a plant by editing `plant.patch`
and putting its base64 in that line; `planted-present` fails every try if the two disagree.

## Checks

`builds`, `acceptance` and `tests-green` are the correctness checks, with `exit_ok`. `acceptance/<stage>/` holds
xunit files dropped into nb's test project for the one stage, and `acceptance/<stage>.sh` a script against the
built nb on its Mock provider; the verifier is the only place either exists. `planted-present` is integrity.
`tests-untouched`, `diff-in-scope` and `named-cause` are observations.

`not-a-bug` passes on an empty diff, which is also what giving up looks like. Its `named-cause` (the closing
message names the page that documents the behaviour) is what tells the two apart, and it is an observation, not
part of correctness.

## Proving the checks without a model

```bash
NB_REPO=../nb exams/nb-changes/reference/shakedown.sh             # every check passes
NB_REPO=../nb exams/nb-changes/reference/shakedown.sh --unsolved  # acceptance fails where something is planted or asked for
NB_REPO=../nb exams/nb-changes/reference/shakedown.sh --half      # a half fix, a wrong fix, a deleted test
```

About ten minutes each. Without `NB_REPO` it clones nb from the fixture's URL.
