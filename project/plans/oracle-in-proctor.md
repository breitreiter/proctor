# The oracle in proctor

Status: accepted 2026-10-29. Step 7 of `agent-neutral-runs.md`, decision 9.
`learnings/scope.md` (*The consequence: the oracle moves up*) asked for this plan
before building, and its four questions are settled below.

## Where things stand

Proctor has no oracle. No suite field names a sheet. No arm attaches one. The
`oracle_hit`, `oracle_misses` and `oracle_turns` checks are in `Checks.NotYet`.
nb has had an oracle since `plans/oracle-resolver.md`:
- `oracle @sheet.md` services a halt in-process;
- `nb --acp` makes nb a bridge target (2026-10-28);
- `nb --judge` makes the judgement on its own (2026-10-29, `../nb/docs/conversation-program-cli.md` §8.3).

Facts this plan rests on, each checked against Harbor 0.24 source or by running it:

- **A user agent is configured like an agent.** `UserAgentConfig` extends
  `AgentConfig`: it takes `import_path` and `kwargs`, plus `bridge {kind: acp}` and
  `user_prompt_template_path`. It runs in the Harbor process on the host, so it
  can read host files and run the host's nb. Its logs land in the trial's
  `agent/user-agent/`.
- **The user agent's turns are `acpx prompt` calls in the container.** With
  `--format json`, `acpx` prints the whole JSON-RPC exchange, including the
  prompt's answer, for example
  `{"stopReason":"end_turn","usage":…,"_meta":{"nb":{"exit_reason":"ok"}}}`. I ran this
  locally against `nb --acp` on Mock. The bridge's own config says `format: quiet`,
  which would give only the text, so the user agent overrides it per call.
- **In bridge mode, Harbor runs `populate_context_post_run` on the user agent,
  not the target** (`trial.py` `_populate_agent_context`). So a bridged Claude
  Code or Codex run produces no ATIF from its native log; the only record is
  the raw ACP export. nb is unaffected: it writes its own trajectory on every
  prompt.
- **nb already marks oracle turns in ATIF.** On the bare path `AtifWriter` writes an
  oracle turn as a `user` step with `extra.source: "oracle"`, plus its `keys` and `verdict`.
  `Transcript` reads `extra.source` into `UserTurn.Source`.

## The four questions

1. **Whose is the sheet?** The task's. `oracle` on a task names a markdown file in
   the suite, beside the task and outside every fixture's `repo/`. It can't reach
   the container by construction. The format is nb's `AnswerSheet`.
2. **Who judges?** `nb --judge` on the host. Proctor's own judge client is
   rejected for three reasons:
   - the gate would then compare two judges rather than two transports;
   - the Mock's scripted verdict (`MOCK:oracle=`) only works through nb;
   - `../nb/evals/oracle-bench` tunes the prompt nb uses, and a copy would drift.

   The judging entry is an nb config entry, named the same way on both paths.
3. **What does a turn look like in ATIF?** The same as on the bare path. Ingest
   writes `extra: {source: "oracle", keys, verdict}` on the bridged trajectory's
   answer steps, from the user agent's log. `Transcript` and the checks then read
   one shape from both paths.
4. **What about agents that can't be a target?** They're refused at validation,
   with a "not yet" naming the reason:
   - Qwen Code isn't a bridge target.
   - Claude Code and Codex are, but bridged they leave no ATIF (above).

   Lifting the refusal needs an ACP-export-to-ATIF reader, or the user agent calling
   the target's own conversion. Either is its own piece of work. The oracle is
   nb-only, and the refusal stays (decided 2026-10-29). A cruder route, an injected
   ask tool, is in `todo.md`, for when the gap disqualifies a real use case.

## Design

### Suite

- `oracle` on a task: a path relative to the task file, to an answer sheet. A missing
  file, or a sheet with no headed entry, is a validation problem.
- `limits.oracle_turns`: how many answers a run may get. It defaults to nb's 8 and
  applies on both paths.
- `oracle_judge` on the suite: the nb config entry that judges. Absent, the arm's own
  `provider` judges, as nb's in-process oracle does.
- Validation refuses:
  - a task with an `oracle` on a vendor arm (question 4);
  - an `oracle` task whose program template doesn't end in exactly one `run`
    (needed by the bridge below);
  - the three oracle checks on a suite with no oracle task. Today's "not yet" becomes
    this, so a check that can't fire is still an error.

### Bare path (tool-less nb arm)

The resolved program gets the oracle prepended:
- `oracle @<host sheet path>`;
- `oracle provider <entry>`, when the suite names a judge;
- `budget oracle_turns <n>`.

Directives apply to every `run` after them, so prepending works whatever the template
says. nb does the rest in-process, as it does today when you run it alone.

### Harbor path (tool-using nb arm)

The prompt can't reach nb as the program's `run`, because the user agent sends it.
- **Program.** It's compiled on the host as now, then the final `run` event is
  dropped from the JSONL. Validation guarantees there is one, and that it's last.
  The prompt is already the task's `instruction.md`, which Harbor hands to the user
  agent. The program carries no `oracle` directive, so the sheet stays on the host.
- **Job.** `HarborCompile.Job` adds a `user_agent` to an `oracle` cell:
  - `import_path: oracle_user:OracleUser`;
  - `bridge: {kind: acp}`;
  - a compiled `user_prompt_template_path`, which keeps the task text separable from
    the bridge's instructions behind a sentinel line, as the spike did;
  - kwargs: the host sheet path, the host nb binary and config (`experiment.Nb`), the
    judging entry, and `oracle_turns`.

  A cell with no oracle runs direct, as today.
- **`Harbor/oracle_user.py`.** It's embedded and compiled into `harbor/agents/` like
  `nb_agent.py`. Its loop follows nb's `ResolveWithOracleAsync`, so the two paths
  agree by construction:
  1. Send the task with `acpx --format json prompt`. Read the prompt's answer and its
     last message: the `agent_message_chunk` text after the prompt's last tool
     update.
  2. The judge runs only after `ok`. If the answer isn't `end_turn` with
     `_meta.nb.exit_reason == "ok"`, or it's an error, stop. The run's own ending
     stands.
  3. Otherwise `nb --judge --sheet … --config … [--provider …]`, with the last message
     on stdin.
     - **done** ends the run, which ends `ok`.
     - **miss** ends it as `oracle_miss`.
     - **hit** with answers already spent on `oracle_turns` ends it as `oracle_budget`.
     - **hit** otherwise sends `answer` as the next prompt, then goes back to step 2.
  4. A judge exit other than 0 ends the run as `judge_error`.

  It writes `agent/user-agent/oracle-user.json` with its turns (`keys`, `verdict`,
  `answer`, the judge's `usage`), the ending, and every verdict. It makes no model call
  of its own.

### Ingest

For a cell with `oracle-user.json`:

- **The trajectory's user steps.** After the first one, they must be the answers in
  order, compared by text. Each gets `extra {source: "oracle", keys, verdict}`. A
  count or text mismatch fails the cell as `adapter_unmapped`, by the strict rule.
- **The exit reason.** The log's ending replaces the trajectory's reason when the
  ending is `oracle_miss` or `oracle_budget`. On the bare path nb sets these reasons
  itself.
- **A `judge_error` ending** fails the cell as infrastructure. A run whose judge broke
  doesn't measure the agent.
- **The judge's usage** is recorded in the manifest beside the agent's. It isn't added
  to the counted core (see *Known differences*).

### Checks

The three checks leave `NotYet`. They read the `oracle_turns` window: `UserTurns`
whose `Source == "oracle"`, plus the exit reason.
- `oracle_hit`: the listed keys are among the keys hit.
- `oracle_misses {max}`: counts `oracle_miss` endings, so it's 0 or 1 per cell.
- `oracle_turns {max}`.

Each is negatable, like the other built-ins.

### The gate

Decision 9 as a test, beside `GateTests` and on the same terms: Mock, an engine, $0.
- **Suite.** One task asks twice, and its sheet's first entry scripts the second ask
  (`MOCK:response=… MOCK:oracle=…`, as nb's `evals/fixtures/oracle-sheet.md` chains
  them).
- **Arms.** Two nb arms on that task:
  - bare and tool-less, with the in-process oracle;
  - on Harbor and bridged, with `OracleUser`. It needs a granted tool to take the
    Harbor path.
- **Assertion.** Equal counted cores and equal oracle user turns (text, keys,
  verdict), apart from bookkeeping.
- **Endings.** Three variants pin the three endings, `ok`, `oracle_miss` and
  `oracle_budget`. On Mock each is a different `MOCK:oracle=` script.

### Known differences, recorded rather than corrected

- **Tokens.** On the bare path nb charges the judge's calls to the run's usage, as
  `SideCallAsync` does. On Harbor the judge is a separate process. So the gate
  compares tokens only after taking the judge's out of the bare side. That's
  reproducible on Mock, where every call costs 15, and the report states it.
  Changing nb to report the judge's usage separately would make this exact, and
  it's a small nb change if the gate needs it.
- **Continuation.** Each answer is a new prompt over ACP, not nb's in-process
  continuation. The model sees the same history either way. This is the known cost
  decision 9 already names.
- **The tool-call budget and the loop detector.** They're per prompt under `--acp`,
  per `run` bare. An oracle continuation is inside one `run` in-process but is a new
  prompt over the bridge, so `limits.tool_calls` counts per answer on Harbor. The
  gate's task makes no tool calls. The porting guide says so for real suites.

## Work, in order

Each is a commit with its tests, and `main` stays green after each.

1. Suite schema and validation: `oracle`, `limits.oracle_turns`, `oracle_judge`, the
   refusals, and the checks' new error. No run changes.
2. Bare path: the prepended directives and the three checks. This is end to end on
   the bare runner, Mock, with a `RunnerTests` case per ending.
3. `oracle_user.py`, the run-less program, and the job's `user_agent`. These are unit
   tests on the compile and on the Python loop's parsing (canned `acpx` output).
4. Ingest: marking, endings, and the judge's usage in the manifest. These are
   `HarborTests` on a recorded trial.
5. The gate, then the plan, docs and CHANGELOG.

## Interop with nb

Nothing here changes nb. Proctor depends on things nb already has:
- `nb --judge`'s JSON and exit codes;
- `--acp`'s `_meta.nb.exit_reason`;
- the oracle's ATIF shape (`extra.source`/`keys`/`verdict`);
- `MOCK:oracle=`.

`../nb/nb.Tests/ProctorContractTests.cs` should gain those as tripwires, so a change
on nb's side names this file. The one optional nb change is the judge's usage on the
trailer (*Known differences*).

## Open questions

- **Vendor targets.** The ideal is upstream: Harbor recording a bridged target's ATIF
  itself, so multi-turn over ACP works for every agent and proctor carries nothing. Short
  of that, which way to lift the refusal: read the ACP export into ATIF
  (proctor owns a reader, kept in step with ACP), or have the user agent call the
  target's own `populate_context_post_run` (Harbor's conversion, reached around
  Harbor). Not needed for nb, and not decided here.
- **Whether `limits.tool_calls` should become per run on the bridge** by having the
  user agent track it. It probably shouldn't: per prompt is what a real user's turns
  get.
