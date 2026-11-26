# readme-answers

The first exam with a real model, and the second to run after
`playground`. It asks a question nearly every project has: does the
documentation tell a reader what they need? Here the reader is a small,
cheap model and the documentation is one README. There is no code to write
and no tool to call, so it needs no sandbox: only a provider and a key.

```bash
proctor setup                     # add Gemini for the built-in harness; name it gemini
proctor run readme-answers        # run it again after an interruption: it continues
proctor grade readme-answers
proctor report readme-answers     # then open reports/readme-answers/explore/summary.md
```

| | |
|---|---|
| fixture | `fixtures/empty`: nothing; the model's whole context is the README |
| axes | `docs`: `as-shipped`, jot's README as it ships, which is live; `for-scripts`, the same README with a section on using jot from a script. `assistant`: Gemini Flash-Lite (`gemini-flash-lite-latest`). The two configurations are named after their `docs` value |
| attempts | 4 tries of each configuration, set in `exam.json`. A try is all six stages, once; four a side is the fewest that can confirm a difference |
| stages | six questions about jot (`fixtures/jotcli`), each asked in a conversation of its own. Three the README as shipped answers (how to add a task, an exit code, where the tasks live); three only the section for scripts does (what exit code `clear` gives with no terminal, whether `echo y \| jot clear` works, how long it waits) |
| pass | `correct`: the `ANSWER:` line matches the stage's expected answer |
| validity | `answered`: the stage ended cleanly. A stage the provider cut short (a rate limit, say) loses its whole try: it is left out rather than counted wrong, and `run` makes another |

What to expect: both configurations get the first three right. On the last three,
`as-shipped` can only guess, and the report shows how often a guess is
wrong. That gap is the point: a change to the documentation, measured
try against try, as how many of a try's six answers were right. The README as it ships is the live
value of `docs`, so the report gives `for-scripts` against it from the first run.

**Cost.** 48 requests, each a few thousand tokens. On Gemini's free tier
that is free; free-tier requests may be used by Google to improve its
models. A free tier allows only a few requests a minute, and nb waits and
retries when it is throttled, so a run can take a few minutes.

**A different provider.** The `assistant` axis's one value names the provider
`gemini`, the name `proctor setup` suggests. If you named it something else, or
want another model, change `provider` and `model` there in `exam.json` before
the first run; after it, a changed value needs a new name.
`proctor setup --check` lists the models your key can reach.

**How it is built.** The `docs` value sets the exam's `README` parameter, a
directory under `values/docs/`. `program.nb` gives that README to the model as
the system message (`system @{{param.README}}/README.md`) with no tools, then
asks the stage's question and for the two-line reply the checks read.
Every check is deterministic: `correct` is a regular expression per stage,
in the stage's `expect`. `reference/shakedown.sh` proves the checks on
nb's Mock provider, every stage passing with scripted right answers and none
with `--wrong`, at no cost.
