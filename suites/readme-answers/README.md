# readme-answers

The first suite with a real model, and the second to run after
`playground`. It asks a question nearly every project has: does the
documentation tell a reader what they need? Here the reader is a small,
cheap model and the documentation is one README. There is no code to write
and no tool to call, so it needs no sandbox: only a provider and a key.

```bash
proctor setup                     # add Gemini for the built-in harness; name it gemini
proctor run readme-answers        # prints an experiment id
proctor grade <id>
proctor report <id>               # then open reports/data/<id>/report.html
```

| | |
|---|---|
| fixture | `fixtures/empty`: nothing; the model's whole context is the README |
| arms | `as-shipped`: jot's README as it ships. `for-scripts`: the same README with a section on using jot from a script. Both on Gemini Flash-Lite (`gemini-flash-lite-latest`), 3 samples |
| tasks | six questions about jot (`fixtures/jotcli`). Three the README as shipped answers (how to add a task, an exit code, where the tasks live); three only the section for scripts does (what exit code `clear` gives with no terminal, whether `echo y \| jot clear` works, how long it waits) |
| pass | `correct`: the `ANSWER:` line matches the task's expected answer |
| validity | `answered`: the run ended cleanly. A run cut short by the provider (a rate limit, say) is left out rather than counted wrong |

What to expect: both arms get the first three right. On the last three,
`as-shipped` can only guess, and the report shows how often a guess is
wrong. That gap is the point: a change to the documentation, measured,
with an interval around it.

**Cost.** 36 requests, each a few thousand tokens. On Gemini's free tier
that is free; free-tier requests may be used by Google to improve its
models. A free tier allows only a few requests a minute, and nb waits and
retries when it is throttled, so a run can take a few minutes.

**A different provider.** The arms name the provider `gemini`, the name
`proctor setup` suggests. If you named it something else, or want another
model, change `provider` and `model` in both arms of `suite.json`.
`proctor setup --check` lists the models your key can reach.

**How it is built.** `program.nb` gives the arm's README to the model as
the system message (`system @{{bundle}}/README.md`) with no tools, then
asks the task's question and for the two-line reply the checks read.
Every check is deterministic: `correct` is a regular expression per task,
in the task's `expect`. `reference/shakedown.sh` proves the checks on
nb's Mock provider, every run passing with scripted right answers and none
with `--wrong`, at no cost.
