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
