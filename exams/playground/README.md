# playground

The first exam to run. Nothing in it needs a model or a key: the one variant
is nb's Mock provider, which reads what to say from the task's prompt. The
point is to see the whole loop once, `list`, `run`, `grade`, `report`, and
to read a report about a real repository before writing an exam of your
own.

```bash
proctor list playground
proctor run playground          # prints a batch id
proctor grade <id>
proctor report <id>             # then open reports/data/<id>/report.html
```

| | |
|---|---|
| fixture | `fixtures/jotcli`: a tiny TypeScript to-do CLI with its own tests |
| variant | `mock`: nb on Mock, 2 attempts |
| tasks | `hello`: the model answers in one turn. `runs-the-cli`: the model builds the CLI and runs it once with bash |
| pass | the attempt ended cleanly, the answer contains what the task expects, and the repository still compiles and passes its tests |

The last two pass checks come from the fixture, not this exam: every
task on `jotcli` carries them. They run for real on the checkout after
each attempt, which is why the report's check reasons say `tsc: 0 errors`
and `9 of 9 tests pass` even though no model touched anything.

When the report reads, the next exam is `readme-answers`: a real model,
on a free tier, answering questions about this same CLI from two versions
of its README. It needs a key (`proctor setup`) and no sandbox.
