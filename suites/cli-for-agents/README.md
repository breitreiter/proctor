# cli-for-agents

Can an agent make a small command-line tool usable from a script or by
another agent? The second suite a new user runs, and the first with a real
model in it. Its fixture is the same tiny TypeScript CLI as `playground`;
its one task is the change that CLI is asking for.

```bash
proctor list cli-for-agents
proctor run cli-for-agents      # 3 samples on the local model; prints an experiment id
proctor grade <id>
proctor report <id>
```

| | |
|---|---|
| fixture | `fixtures/jotcli`: `jot clear` waits for a keypress on the terminal, with no `--yes`, and exits 0 when it gives up |
| arm | `local`: nb bare on the local qwen3-coder model, 3 samples. Add an arm for any model nb's config names |
| task | `clear-for-scripts`: add `--yes`, refuse without a terminal instead of waiting, exit non-zero whenever nothing was cleared |
| pass | the run ended cleanly, `acceptance/clear-for-scripts.sh` passes against the changed checkout, and the repository still compiles and passes its own tests |
| guardrails | no denied tool calls, no loop nudges, under 60 tool calls and 20 minutes, changes confined to `src/`, `tests/` and `README.md` |

The acceptance script runs the built CLI with stdin closed and no
controlling terminal under a ten-second limit, so the original
thirty-second wait fails on time rather than on behaviour. `reference/`
holds a patch that passes it; `reference/shakedown.sh` applies it (or
nothing, with `--unsolved`) and runs every check, which is how to prove
the suite without spending a model call.

The suite is bare nb on purpose: no container, the model's bash runs on
this machine in the checkout. Run it somewhere you would run `npm test`.
For an agent arm (Claude Code, Codex, Qwen Code, or nb itself) through
Harbor, see `code-change`.
