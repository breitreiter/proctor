# playground

The first exam to run. Nothing in it needs a model or a key: its one
configuration is nb on its Mock provider, which reads what to say from the
stage's prompt. The
point is to see the whole loop once, `list`, `run`, `grade`, `report`, and
to read a report about a real repository before writing an exam of your
own.

```bash
proctor list playground
proctor run playground          # makes its tries; run it again and it has nothing to do
proctor grade playground
proctor report playground       # then open reports/playground/explore/summary.md
```

| | |
|---|---|
| fixture | `fixtures/jotcli`: a tiny TypeScript to-do CLI with its own tests |
| axis | `assistant`, with one value, `mock`: nb on Mock. The configuration named `mock` is that value |
| tries | 2, set as `attempts` in `exam.json`; a try is both stages, once |
| stages | `hello`: the model answers in one turn. `runs-the-cli`: the model builds the CLI and runs it once with bash |
| correctness | the stage ended cleanly, the answer contains what the stage expects, and the repository still compiles and passes its tests |

The last two correctness checks come from the fixture, not this exam: every
stage on `jotcli` carries them. They run for real on the checkout after
each stage, which is why the report's check reasons say `tsc: 0 errors`
and `9 of 9 tests pass` even though no model touched anything.

When the report reads, the next exam is `readme-answers`: a real model,
on a free tier, answering questions about this same CLI from two versions
of its README. It needs a key (`proctor setup`) and no sandbox.
