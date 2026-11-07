# playground

The first suite to run. Nothing in it needs a model or a key: the one arm
is nb's Mock provider, which reads what to say from the task's prompt. The
point is to see the whole loop once, `list`, `run`, `grade`, `report`, and
to read a report about a real repository before writing a suite of your
own.

```bash
proctor list playground
proctor run playground          # prints an experiment id
proctor grade <id>
proctor report <id>             # then open reports/data/<id>/report.html
```

| | |
|---|---|
| fixture | `fixtures/jotcli`: a tiny TypeScript to-do CLI with its own tests |
| arm | `mock`: nb on Mock, 2 samples |
| tasks | `hello`: the model answers in one turn. `runs-the-cli`: the model builds the CLI and runs it once with bash |
| pass | the run ended cleanly, the answer contains what the task expects, and the repository still compiles and passes its tests |

The last two pass checks come from the fixture, not this suite: every
task on `jotcli` carries them. They run for real on the checkout after
each sample, which is why the report's check reasons say `tsc: 0 errors`
and `9 of 9 tests pass` even though no model touched anything.

When the report reads, the next suite is `cli-for-agents`, where a real
model is asked to change this same CLI.
