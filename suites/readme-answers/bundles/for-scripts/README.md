# jot

A tiny to-do list for the terminal. Tasks live in `.jot.json` in the
current directory (or wherever `JOT_FILE` points).

```
npm ci && npm test
node dist/src/cli.js add "buy milk"
node dist/src/cli.js list
node dist/src/cli.js done 1
node dist/src/cli.js clear
```

| command | does |
|---|---|
| `add <text>` | adds a task and prints its number |
| `list` | prints every task, done ones ticked |
| `done <n>` | ticks task `n` |
| `clear` | deletes every task, after asking |

Exit codes: 0 on success, 1 when a task is not found, 2 for a usage error.

## From a script or an agent

`clear` asks `Delete N tasks? [y/N]` and reads the answer from the terminal
itself (`/dev/tty`), never from stdin, so a stray redirect cannot say yes for
you. There is no `--yes` flag. For a caller without a person at a keyboard:

- `echo y | jot clear` and `yes | jot clear` do not delete anything: the
  piped `y` is never read.
- With no terminal attached (a script, CI, an agent's shell tool), `clear`
  waits up to 30 seconds for one (`JOT_CONFIRM_TIMEOUT_MS` changes that),
  then prints `Aborted.` and exits **0** without deleting anything. Exit 0
  does not mean the list was cleared; read the output.
- To empty the list from a script, delete the store file instead:
  `rm -f "${JOT_FILE:-.jot.json}"`.
