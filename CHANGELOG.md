# Changelog

Proctor's version is `<Version>` in `Proctor.csproj`; `proctor --version`
prints it and every log entry (`log/<id>/entry.json`) records it under `versions.proctor`.
Before 1.0 a minor bump means a breaking change to the layout, the file
formats or the vocabulary; a patch bump does not.

## Unreleased

- **`proctor retire --json`** answers as JSON (`retire/1`): what it did,
  the mark that was there before, and every configuration retired now.
- **Breaking: an exam is stages and axes, and nothing else.** The
  earlier shape is gone: `tasks/*.json`, `variants/*.json`, a variant's
  `bundle`, `live.json` and `proctor live`, `--variant` and `--tasks`,
  and the report that set variants side by side. An exam still written
  that way is refused with what each part becomes: a task is a stage of
  `exam.json`, a variant's fields are a value of an axis, a bundle is a
  value's `files` or a tree parameter it sets, and what is live is
  `"live"` on an axis. Every exam in the repository is ported.
  `docs/porting-from-suites.md` is the map.
- **A value's `files`, and a tree parameter, may come from a git
  revision**: `{"git": url, "rev": sha, "paths": [...]}`, cloned once
  under `.proctor/files/`. A path the revision lacks stops the run.
- **`exam-authoring` lost `continue-the-batch`**, its workspace
  (`fixtures/authoring-interrupted`) and its two checks: there is no
  run to continue. Both authoring exams now ask for, and grade, exams
  of stages and axes.
- **Breaking: attempts made under tasks and variants are no longer
  read.** They stay in `bank/<exam>/` where they were made; `list` says
  how many, and the viewer lists them on an exam's page ("earlier
  attempts") so their transcripts can still be opened. They are in no
  number: what was under test is named and hashed another way now.
- **`run` no longer continues an interrupted run.** A try is never taken
  up part way, since its workspace is half way through: one that was
  lost or never finished is made again, whole, under the next number.
- **`PROCTOR_CONTAINER` is the try's**, `proctor-<exam>-<try id>`, the
  same for every stage. On an exam with axes it changed with the stage
  and held `=` and `,`, which no engine takes as a name.
- **The outdated summary names the try to move**, by the id it is kept
  under; it named a directory that is not where a try is.
- **`summary.md` and `stats.json` name the judges** whose verdicts the
  tries hold, each with the checks it graded (`judges`).
- **Breaking: the word variant is gone from what an exam's scripts and
  files say.** `PROCTOR_VARIANT` is `PROCTOR_CONFIGURATION` (the
  configuration's key, as before; each axis's value is still
  `PROCTOR_AXIS_<NAME>`). `{{variant}}` in a program template is
  `{{configuration}}`. `hooks.variant` in `exam.json` is
  `hooks.configuration` and `hooks.attempt` is `hooks.try`; each old
  key is refused with its new name, and their logs are
  `<key>.setup.log` in the run's log entry and `try.setup.log` in the
  try. `PROCTOR_TASK`, and the fields `variant` and `task` in
  `manifest.json`, `entry.json`, `results.jsonl` and `stats.json`, are
  unchanged: they are renamed once, with the binary.
- `PROCTOR_BUNDLE`, `PROCTOR_BUNDLE_MOUNT`, `{{bundle}}` and
  `nb.mounts.bundle` went with the bundle. A value's files are laid over
  the checkout, so they are under `PROCTOR_WORK`.
- **Breaking: the modal engine is gone from proctor.** `harbor.engine:
  modal` is refused with a sentence saying where it went: the code is
  at the git tag `modal-in-core`, and its guide and example machine
  file are in `project/archive/`. Rootless podman on Arch and Ubuntu
  is the engine that is supported; Docker still works and is not
  maintained. `--config <file>` stays. `proctor setup` records Modal
  as a hosted service that is wanted, like the others.
- **An exam's or a fixture's own `README.md` no longer outdates a
  script verdict** on an agent variant when it is edited: it is for
  people. Any other file a script could read still does.
- **`coverage` says which configurations are retired** (`retired` on a
  row, with the reason and date). Their tries are still counted.
- **The viewer's interactive parts are Web Awesome components**
  under a theme that reads the six type roles: a transcript step and
  the fold-outs are its details, a step pill its button, and what a
  tag means its tooltip. A link to a step opens that step.
- **The viewer's type is six roles** (title, heading, lead, body,
  small, code) in place of fourteen sizes, set once in its stylesheet;
  no page sets a font size of its own.
- **Every page of the viewer is in the designed style.** An exam of
  variants, a try of one, coverage and the comparison across every
  setting now use the same stylesheet and parts as the five pages
  drawn on the canvas, and the stage page is one page for every try.
  Search on a stage and selecting a check went with the first
  pass's script.
- **The viewer's mark beside a configuration is about that
  configuration's own tries.** On an exam where another axis varies
  too, the mark used proctor's verdict pooled over every setting of
  that axis, so it could rest on tries of another model. It now reads
  the one setting that is the configuration itself, as the comparison
  page does.
- **A named configuration can be retired**, on an exam with axes.
  `proctor retire <exam> <configuration> --reason "..."` records it in
  `exams/<exam>/retired.json` with the reason and the date (commit it;
  it is in no hash); `--bring-back` undoes it, and with no configuration
  the verb lists the retired. Nothing is deleted: the tries, the
  predictions and the comparisons stay, and `compare` still answers
  about it. `run` makes no new tries of a retired configuration and
  refuses one asked for by name or by its values. The live
  configuration cannot be retired. `list` and the report mark it,
  `stats.json` carries `retired` (`reason`, `since`) on its row, and
  the viewer leaves it out of the exam's and the index's lists, under
  a count that opens the retired ones with their reasons.
- **Tool calls, tokens read and time are compared like the rest.** A
  comparison's cells (`stats.json`, `compare`) carry `tool_calls`,
  `tokens` and `duration` beside `held` and `flagged`, try against
  try, fewer being better; each configuration's tries carry the
  spread of each, and a try in an answer its own three numbers. One
  is null when a try on either side lacks the number. The verdict is
  still made on correctness checks held alone.
  Every measure of one cell also says how far: each side's median
  (`median`, `other_median`) and the first as a fraction above or
  below the second (`change`).
- **The viewer's exam page is the one designed on the canvas**, for an
  exam with axes: each named configuration against what is live on
  checks held, flagged steps, tool calls, tokens read and time; its
  predictions with each change's outcome; its axes, stages and checks
  with their roles. The earlier page is still at `/<exam>/coverage`.
- **A stage and a check across a configuration's tries, as plain
  statistics** (`stats.json`, each configuration's `tries`). A stage
  gives its count of correctness checks, the share of them a try held
  as lowest, median, highest and average (a share, so stages with
  different numbers of checks read alike), the tries it is over, and
  each correctness check that did not hold with in how many tries and
  the first reason. `checks` gives each check's role and how often it
  held of the times it was asked, whatever the stage; an integrity
  check's is over every graded try. A stage of a try carries the
  checks it held, of how many.
- **The viewer's configuration page is the one designed on the
  canvas**: the same five measures against live, its predictions,
  stages, checks, friction and tries, each try's stages as squares
  that open that stage.
- **A comparison's cell goes check by check and stage by stage**
  (`compare`): `by_check`, each check's rate on both sides and the two
  compared on the stages of a try in which it held; `by_stage`, each
  stage on both sides as a configuration has it, compared on the share
  of its correctness checks a try held; `friction`, flagged steps a
  try by label, which `friction.json` now gives per configuration too
  (`labels`).
- **The viewer's comparison page is the one designed on the canvas**,
  for a comparison in one setting: "X vs Y", every number describing
  X. A comparison across several settings keeps the earlier page. The
  token redirect keeps a page's own query.
- **A verdict records its check's role** (`checks.json`, `role`), as
  it was when graded, beside `describes` and `why`. Grade an exam
  again to add it to verdicts already on file.
- **The viewer's page for a stage of a try is the one designed on the
  canvas**, on an exam with axes: the try's stages as tabs, each right
  or wrong; verdicts by role, with the steps their evidence points at
  as pills; friction points likewise; the transcript, a step a row
  that opens; the prompt and the files as the same address asked with
  `?show=`. It needs no script. `?check=<name>` is gone there: a
  verdict's address is `#check-<name>`.
- **The viewer's index of exams is the one designed on the canvas**:
  a row an exam, the one tried most recently first; an exam with axes
  shows its named configurations with their marks against live, its
  predictions with their counts, its tries and when the last was made.
- **A predicted change's outcome is one of five words** (breaking, in
  `stats.json` and `compare`): `supported` (was `held`),
  `contradicted` (was `opposite_confirmed`), `no_effect` (was
  `no_difference`), `inconclusive` (was `leaning` and `opposite`: a
  difference either way that is not confirmed; the measure says which
  way) and `not_run`. The sentences are unchanged.
- **Four roles for a check, named in the exam** (breaking). `grading.pass`
  is `grading.correctness` and `grading.validity` is
  `grading.integrity`; a new list, `grading.quality`, names checks
  that are preferred and not required, and a check in no list is an
  observation (it was a guardrail). An `exam.json` with the earlier
  names is refused with the new one: rename the two keys. A check
  that asks a model (`judge`, `decide`) is refused under correctness
  and belongs under quality. The report says correctness, integrity,
  quality and observation where it said headline, validity and
  guardrail; `stats.json` says `correctness_checks`,
  `integrity_checks` and `quality_checks`, and each stage of an exam
  with stages lists its checks with their roles. Every stage's
  standard moves with the rename, so tries on exams with axes are not
  yet graded until `proctor grade <exam>`; nothing is run again.
  `tern-docs` names `tests-untouched` as integrity and
  `current-endpoint` as quality.
- **A prediction says when it was made, and must** (breaking). `written`
  in `exams/<exam>/predictions/<name>.json` is its author's: a date
  (`2026-11-24`, read as the end of that day) or a time in UTC
  (`2026-11-24T21:05Z`), required, and never set by proctor. A file
  without one is a problem naming the field; add the date it was
  written. `proctor predict` and `run` still pin the standard it was
  made under, once. With the date always there, "Not pinned" is gone,
  and a prediction written after tries of its configuration were made
  is **post hoc**, not "late": in the report, `list`, `predict` and
  `compare`, and as `post_hoc` where `stats.json` and an answer said
  `late`.
- **Three guides**: `docs/network.md` (what an agent's container can
  reach on any engine, and what an allowlist is and is not),
  `docs/service-fixtures.md` (a fixture with services, and what the
  agent can and cannot see of one) and `docs/docker.md`.
- **A fixture names further hosts its agent may reach**
  (`image.allowed_hosts`), beside the model's, for a service that
  cannot sensibly be simulated. By host, as Harbor's allowlist is:
  no port, and every web port of a named host is open. Part of each
  task's hash on the fixture.
- **`run` warns when a host the agent is to reach sits inside the
  range the engine gives its container networks by default**
  (172.17 to 172.31 on Docker, 10.88 and 10.89 on podman): a trial
  whose network takes that address never reaches the host, and the
  attempt times out with nothing to say why. A warning, never a
  refusal.
- **A model on the machine that runs proctor is relayed to the agent.**
  An agent variant whose base URL (or nb provider's endpoint) is at
  `localhost` or a `127.` address used to reach nothing: a container
  cannot see the host's loopback, and opening it hands every loopback
  service to every trial. `run` now holds a unix socket per such port
  in the user's runtime directory and compiles a small service into
  the task (`proctor-relay`, `Harbor/relay.py`) that serves the port at
  the agent's own `localhost`. Only that port crosses, the agent's
  phase is `no-network`, and `run` refuses before anything is made when
  nothing answers there. A fixture names further ports of the machine
  in `image.host_ports` (a dev server), and the ports its own services
  answer on in `image.serves`, which is how the stub's
  `localhost:8765` stays the fixture's. The log entry records the
  ports as `harbor.relay`. Local engines only; run on rootless podman.
- **The shipped exams run on Harbor 0.26.0** (0.24.0 before), all but
  `tern-docs`, whose paid run is half made and stays on the pin it was
  started under. Both gates and conformance pass on it on rootless
  podman and rootless Docker with nothing else changed, and the network
  probe (`project/learnings/netprobe/`) answers as it did. An exam's pin
  is its own (`harbor.version`); an interrupted run started under
  another version is not continued.
- **An agent beside a fixture's services can no longer capture what
  they say.** A service that declares no network shares the agent's, and
  under Docker's default capabilities the agent's container could open a
  packet socket on that loopback: on rootless Docker a probe heard a
  header a service sent itself. Compile now drops `NET_RAW` from the
  agent's container (`main: { cap_drop: [NET_RAW] }` in the compiled
  compose file) whenever the fixture has services. Podman's defaults
  never held the capability. The cost is `ping` in the agent's container
  on Docker. A compose file that declares `main` itself is left as
  written. An interrupted run whose compiled tasks have services is not
  continued, and what is missing is made by a new run.
- **A try of several stages through Harbor no longer dies at its second
  stage on podman.** Harbor uploads a stage's `setup.sh` into the
  agent's directory, and podman's Docker API gives that directory the
  host's owner and mode as it does; git then refused the repository
  ("dubious ownership") for the script that puts the workspace back,
  and would have for the agent after it. The script now gives the
  directory back to the repository's owner first. Docker was never
  affected.
- **A tool's cache is no part of a fixture, a bundle or a check.**
  `__pycache__`, `.pytest_cache`, `.mypy_cache` and `.ruff_cache` are
  skipped wherever proctor hashes, copies or compares a directory. Git
  ignores them, so they were on one machine and not another: a fixture
  whose tests had been run where it lies had another hash, which made
  every attempt of its tasks stale, and an exam whose acceptance tests
  had been run had them compiled into each Harbor task, so the script
  checks of attempts made there were `outdated` on any other machine. A
  bank now reads the same on the machine it is carried to. A fixture
  hashed with such a cache in it hashes without it now, so attempts made
  under that hash are stale once; an interrupted run whose compiled
  tasks hold one is not continued, and what is missing is made by a new
  run.
- **A prediction written after the tries leaves their script verdicts
  alone.** `predictions/*.json` and `combinations.json` are files
  proctor reads itself, like `exam.json`; they counted as a change to
  what a script check reads, so pinning a prediction made every script
  verdict of an agent's attempts `outdated` at the next `grade`.
- **`grade` says why a check did not pass.** Each attempt's line
  printed `check=result` for a check that failed, errored or could not
  decide; it now adds the first line of the reason as `checks.json`
  holds it, cut at 100 characters, so an errored check can be read
  from the output (#12).

- **An agent's container keeps its network policy for its whole life.**
  The compiled `task.toml` gave the agent's phase its allowlist (the
  model's host, or nothing) and left the environment `public`; Harbor
  applies a phase's policy only while the phase runs, so a process the
  agent left running had the open internet from the moment its phase
  ended, and between the stages of a try. The environment now carries
  the agent's policy. Nothing about an exam changes, and attempts in
  the bank stay current. A run interrupted before this change, when
  continued, still makes its attempts from the tasks it compiled then,
  with the public environment: `run` compares those with what the log
  entry recorded, not with what compile writes now. Attempts made
  before the change on a staged exam had that window between stages.
  A task with an answer sheet through Harbor keeps a public
  environment: Harbor installs its ACP bridge from the network in the
  container on every trial, so the window stays open there after the
  agent's phase. Not run on Modal or podman yet.
- **A one-stage try with an answer sheet runs through Harbor**
  (`axes-build.md`, slice 4; owed since `bank.md` step 4). It is
  compiled as a plain Harbor task, which is what Harbor's simulated
  user can answer, and kept where any try is. An exam of stages with an
  oracle was refused for every agent variant; now only a try of
  several stages with a sheet among them is. `gate-oracle` is on
  stages and axes: each ending is one stage on one value of `ending`,
  and `route` runs it bare and through Harbor. Its `variants/` and
  `tasks/` are gone; it had no bank kept in the repository.
- **The viewer shows an oracle's turns as the oracle's.** A user step
  the oracle wrote is labelled `oracle`, with the sheet entries it
  answered from; the try's header counts its answers; and a
  conversation that ended unanswered (`oracle_miss`, `oracle_budget`)
  says so at the end of the transcript, since an answer that was not
  given leaves no turn.
- **Predictions** (`axes-build.md`, slice 6). On an exam with axes, a
  prediction is one statement written before the tries are made: run
  this configuration, and this will change. It is a file,
  `exams/<exam>/predictions/<name>.json`: the configuration it is about,
  by the name the exam gives it, what it is compared with (`against`,
  or what is live), and its `changes`, each a check or a metric
  (`held`, `flagged_steps`, `tool_calls`, `tokens`, `duration`) with a
  direction, `more` or `less`, and optionally why. New verb
  `proctor predict <exam> [name]` pins it once, with the time and each
  stage's standard; `run` pins one whose configuration it is about to
  make tries of. `list`, the report and `compare` say how each change
  came out in a sentence, a miss as plainly as a hit; one pinned after
  tries of its configuration existed is labelled late, and one whose
  checks changed since says the standard moved. A prediction gates
  nothing and is in no hash. `tern-docs` carries two. The word
  replaces "claim" in the vocabulary.
- **Grades over time** (`axes-build.md`, slice 5). `grade` keeps each
  grade in the attempt as `grades/<standard>.json`, beside
  `checks.json`: the verdicts, the hash of the checks they were made
  under, the exam's hash and the time. A regrade under changed checks
  adds a file; the old grade stays. A check may carry `"when"` (`axis
  = value`, `!=`, joined by `and`, on an exam with axes): where it does
  not hold the verdict is `not-applicable`, which is neither pass, fail
  nor undecided and no part of that configuration's headline. On an
  exam with axes a try with no grade under the checks as they are now
  is not yet graded: `report` leaves it out of every number and says
  how many (`not_graded`), `coverage` counts it, and `compare` refuses
  a comparison that would read it. `grade`, the report and each
  comparison say where the standard moved since an earlier grade: a
  check narrowed, added or removed, with the verdicts concerned. Before
  a judged `grade` asks anything it prints, per judge, the verdicts it
  would ask and those already on file. **A bank graded before this
  reads as not yet graded on an exam with axes until `proctor grade`
  is run once**; verdicts on file are reused, so it asks no judge.
- **Axes through Harbor** (`axes-build.md`, slice 4). A configuration
  whose assistant value names a vendor's agent, or `route: harbor`,
  runs through Harbor. Its values' files are in the repository the
  image is built from; its text parameters are build arguments (`ARG
  NAME` before the image's own steps, limited to `image.params` when
  the fixture lists them) and never in the agent's environment; a tree
  parameter is `environment/params/<NAME>/` for a compose service to
  mount; the job's steps are the stages that apply; the verifier's
  checks get `PROCTOR_AXIS_*` and `PROCTOR_PARAM_*`. `proctor build`
  compares what such a configuration's image build is handed and
  builds no image. A try whose image cannot be built is lost with the
  build's own error as its reason.
- **`tern-docs` is on axes.** Its three variants are the `docs` axis
  (`as-shipped` live, `restructure`, `none`) and an `assistant` axis of
  one value; the configurations keep the variants' names. The docs
  pages moved from `bundles/` to `values/docs/`, and `fixtures/tern-shop`
  takes them as the tree parameter `DOCS`, mounted from
  `./params/DOCS` where it was `./bundle`. It had no bank, so nothing
  is stale.
- **The viewer lives here** (`rename-to-auspex.md`, stage 1). auspex
  is `View/`, its own project in the same solution; `dotnet build`
  builds both and `dotnet test` runs both suites. It is started as
  before, `auspex <proctor root>`.
- **Axes: the pages** (`axes-build.md`, slice 3d). The viewer reads a
  bank laid out by try id and serves the addresses `compare` and
  `coverage` give: `/<exam>` (the configurations named or run, and
  what live spans), `/<exam>/coverage?as_of=`,
  `/<exam>/configuration/<key>`,
  `/<exam>/compare?change=…&against=…&as_of=…` (or
  `?configuration=<name>`), and `/<exam>/try/<id>/<stage>`. A
  comparison's page shows proctor's own numbers, worked out by
  proctor's code, as they stood at `as_of` and as they are now when
  tries were made since. A stage page takes `?check=<name>` to select
  a verdict.
- **Axes: two queries, answered as JSON** (`axes-build.md`, slice 3c).
  `proctor compare <exam> <axis>=<value>` (with `--against <value>`,
  `--within <axis>=<value>` and `--as-of <time>`) answers one value
  against another with every other axis held, from the report's own
  code: each setting with its tries, what was not run, the settings
  read as one, what so few tries could have shown, when each side was
  made, which other axis only ever moved with this one, how many other
  comparisons the report holds, and an address for the comparison,
  each configuration and each try. `proctor coverage <exam>` answers
  the tries of each value and configuration and of everything live
  spans. Both are schema `answer/1`, call no model and recommend
  nothing. `compare` is refused while a try in it is not graded.
  `skills/proctor-answers/` is the skill for an agent answering a
  person from them. `proctor compare <exam> --configuration <name>
  [--against <name>]` asks about a configuration: the axes on which it
  differs from what is live (or from the other) are compared as one
  change, in every setting where the exam carries it, and the answer
  says it is of those values together. A comparison's address is
  `/<exam>/compare?change=…&against=…&as_of=…` for both forms.
- **Axes: better here, worse there.** A comparison whose settings point
  different ways says so, in the report and in `compare`, and is not
  read as one: opposite differences cancelled into "the same". Each
  comparison in `stats.json` gains `verdict`.
- **Axes: a value built alone** (`axes-build.md`, slice 3b). New verb
  `proctor build <exam>`, for an exam with axes; it runs nothing and
  calls no model. It builds the starting workspace (the checkout, the
  values' files, the setup hooks) of the reference configuration
  twice, then of each value with every other axis at its reference,
  and says what each value changed: who does the work, the parameters
  it sets, the paths that differ and, with `--value`, their text, for
  whoever asked to read against the value's note. A path two builds of
  the reference disagree on is left out of every change. Each
  configuration the exam carries that is more than one value off the
  reference is held against its values built alone: it is their sum,
  more than their sum (some script reads more than one axis), or
  cannot be told (two values change the same lines). That result is
  kept in `exams/<exam>/combinations.json` (new, committed, in no
  hash), and `list`, the report's rows and every comparison that reads
  such a configuration say it. Nothing records whether anyone read
  what a value changes. **On an exam with axes, `note` on every value
  and `about` on every axis are now required.**
- **A staged try starts from what its setup hook left.** On the bare
  path the checkout is committed after the attempt setup hook, where it
  was committed before it. A stage's `diff.patch` now holds only what
  the agent changed, so `files_touched` no longer counts a file the
  hook wrote; the hook's own changes are `setup.patch` in the try, and
  a regrade puts them back before the stage's diff. Each stage's
  manifest gains `start_tree`. A try made before this still grades:
  its diff holds the hook's changes, and it has no patch.
- **Axes: `run` makes what is named** (`axes-build.md`, slice 3a).
  This changes the two entries below, which were never released.
  Nobody spans the cross product of an exam's axes any more. `proctor
  run <exam>` makes the configurations the exam names under
  `configurations`, and says to name one when there are none;
  `--configuration name` makes one of them; `--value axis=value` makes
  that value with every other axis at its reference, where it used to
  narrow the cross product. An axis's reference is its live value, or
  the value it names with the new `"reference"`, or its only value; an
  axis with none is named in the refusal. A configuration made this
  way and not named is carried from then on: `list`, `grade`,
  `friction` and `report` read it, and a plain `run` does not top it
  up. `list` shows a row per configuration named or made, and the
  exclusions as lines; the grid of two axes is gone. In `stats.json`
  (`configurations/1`) the rows are those configurations, the
  `excluded` state is gone and `excluded` is a list of the values
  declared impossible together; a comparison looks only where the
  value was named or made.
- **Axes, reported** (`axes-build.md`, slice 3, proctor's half).
  `proctor report` on an exam with axes writes `stats.json` as
  `configurations/1`: every configuration with its state (a
  configuration nobody ran is `not_run`, never left out), and each
  value of an axis against that axis's live value, cell by cell with
  the other axes held and read as one across them (the van Elteren
  test). Each comparison's sentence says which configurations it
  covered and which were not run.
- **Axes, run bare** (`axes-build.md`, slice 2). `proctor run` makes
  every configuration of an exam with axes that runs nb bare; `--value
  axis=value` and `--configuration name` narrow `run`, `grade` and
  `friction`. A try is kept at `bank/<exam>/<try id>/`, the id its time
  and four random characters, and its manifests name the value on each
  axis and a hash of the workspace each stage left. A value's files are
  laid over the checkout and committed before the first stage; declared
  parameters reach the program as `{{param.NAME}}` and hooks and checks
  as `PROCTOR_PARAM_<NAME>`. A value changed under a name that has made
  attempts is refused. Through Harbor, `report` and git-sourced files
  are not built for axes yet.
- **Axes, declared** (`axes-build.md`, slice 1; the model is
  `axes.md`). An exam of stages can declare `axes` in `exam.json` in
  place of `variants/`: each axis a named thing with values, one value
  optionally live, a configuration one value from each. A value sets
  parameters the base declares (`params` in `fixture.json`, or in
  `exam.json` for the program template), lays files over the checkout,
  says who does the work, or names a whole fixture for an axis of
  worlds; a stage may carry `when`. Refused at load, by field: an
  undeclared parameter, two axes setting one thing or writing one path,
  an overlay leaving the checkout, a condition naming something
  undeclared, a configuration with no stage. `proctor list` shows the
  axes and the configurations. Nothing runs a configuration yet.
- **The first real exams of stages, and how to port a suite.**
  `readme-answers` and `tern-docs` are staged: their `tasks/` are
  `stages` in `exam.json`, and both `reference/shakedown.sh` scripts
  read them there. `readme-answers` asks for 4 tries a variant, the
  fewest that can confirm a difference. The other exams stay tasks
  until each is next run. `docs/porting-from-suites.md` is the way from
  a 0.3 suite to an exam of stages, and the exam-writing skill
  (`skills/proctor-exam`) now writes stages.
- **Stages, defined** (`bank.md`, step 4, slice 1). An exam can be one
  job of stages: `fixture` and `stages` in `exam.json` in place of
  `tasks/`, each stage with an `id`, a `prompt` and optionally `about`,
  `expect`, `checks` and `oracle`. The job has one hash. `proctor list`
  shows the stages and names ids that will not read as a label.
- **Stages, run bare** (`bank.md`, step 4, slice 2). A bare nb variant
  runs a staged exam: one try is every stage in order in one workspace,
  nb started once per stage, kept at
  `bank/<exam>/<variant>/<n>/stages/<i>-<id>/`. A stage that did not run
  loses the try; `"chained": true` ends a try at a stopped stage and
  still counts it. A log entry of a staged exam carries `stages`, and a
  stage's manifest its `stage`.
- **Stages, graded** (slice 3). `proctor grade` writes a `checks.json`
  per stage. Transcript checks are each stage's; a workspace check is
  graded once against the try's diff, the exam's and the fixture's on
  the last stage and a stage's own on that stage.
- **Stages: every check is a stage's, and a stage starts fresh.** A
  workspace check (`files_touched`, a script) now runs for each stage
  that carries it and sees the workspace as that stage left it; each
  stage has its own `diff.patch`, and the try has none. Unless the exam
  says `"chained": true`, each stage starts from the workspace as the
  try began. This replaces the rule that a workspace check belongs to
  the last stage, on both runners.
- **Stages, through Harbor** (slice 6). An agent variant runs a staged
  exam as one Harbor task whose steps are the stages; one trial is one
  try, each step ingested as its stage. **Breaking for a staged exam
  written since slice 3:** a check that reads the workspace on a stage
  before the last is refused; declare it on the exam or the last stage.
- **Stages, reported by try** (slice 5). On a staged exam
  `proctor report` writes `stats.json` as `tries/1` (each variant's
  tries; headline checks held and flagged steps per try; each variant
  against live by Mann–Whitney, with a sentence and what moved most), a
  roll-up of what recurs in friction as `friction.json` beside it
  (`check-evidence.md`, C5.4), `results.jsonl` a row per stage and
  `summary.md`. `proctor run` notes when the attempts wanted are too few
  to confirm a difference between variants.
- **Stages, read for friction** (slice 4). `proctor friction` writes a
  `friction.json` per stage, from that stage's own steps; its `attempt`
  block carries `stage`. A stage not reached is skipped.
- **Breaking: `friction/2`, labels that read bare** (`check-evidence.md`,
  C5.1 and C5.3). The rules' labels are renamed (rules 0.10):
  `off-surface` to `read-internals`, `substitute` to `used-substitute`,
  `abandoned` to `fell-back`, `surface-search` to `searched-docs`,
  `help` to `ran-help`, `workaround` to `inline-script`, `environment` to
  `machine-refused`, `tool-error` to `harness-refused`. The reader's are
  written as `fixed-own-work` (was `repair`), `wasted-read` (`lost`),
  `bad-instruction` (`dead-end`), with `reread` and `workaround` kept; its
  prompt is unchanged, so every kept reading stays valid. Labels are
  objects with their sentence (`detector.labels`, `reader.labels`), each
  evidence item has `rule` and `because`, and the summary's
  `steps_in_a_point` is `flagged_steps`. Rerun `proctor friction` to
  rewrite existing files: the rules are free, a kept reading is reused.
- **Evidence on verdicts** (`check-evidence.md`, C3). A built-in check's
  verdict in `checks.json` carries `evidence`: the trajectory steps or
  diff paths where it found what it looks for, whatever the result, so a
  negated check that fails points at what it found. A check that found
  nothing, or is about the run as a whole, has none. Script and model
  checks have none yet.
- **Grading reads the trajectory.** An attempt with `trajectory.json` is
  graded from it, bare nb attempts included, as Harbor attempts already
  were; `transcript.jsonl` is read only when there is no trajectory or it
  doesn't parse. nb keeps under `extra` what its JSONL carries: the two
  read the same on every captured pair and on 1,164 of 1,165 real
  attempts. The other had an empty JSONL, and its trajectory held nb's
  result (`rate_limited`). A bare attempt's trailer is nb's own, read
  from the trajectory's `extra.result`. Every call now comes with its
  ATIF step id, which verdict evidence will point at (C3).
- **A check's `why`, and verdicts that explain themselves**
  (`project/plans/check-evidence.md`, C1 and C2). A check takes `why`
  beside `description`: one sentence on why it is worth checking.
  `proctor list` names checks without one; four guardrails (exit reason
  `ok`, no denied calls, no loop nudge, a budget) get proctor's own.
  `grade` writes `describes`, `why` and, for a script check, `script`
  (its path from proctor's root) into each verdict in `checks.json`.
  Verdict equality is still result and reason.
- **Breaking: no `report.html`.** `proctor report` writes `results.jsonl`,
  `stats.json` and `summary.md`; the HTML page, its chart, styles, script
  and embedded logo are gone. auspex is the viewer, and the page would
  otherwise have had to be refactored for stages. `summary.md` keeps the
  same blocks and wording, less one sentence about hovering and clicking.
- `run` and `grade` say where the bank stands (`project/plans/bank.md`,
  step 2). `run` opens with each variant's current, completed attempts of
  those the exam wants and how many it is making; with nothing missing it
  ends `nothing to make`, where it printed `<exam>: every variant has its
  N attempts of each task; nothing to make`. `grade` ends with a line per
  check: how many attempts it has a pass or fail for, how many of those are
  new with this grade, and how many it could not decide and why.
- **Breaking: the bank. There is no batch** (`project/plans/bank.md`, step
  1). Attempts live at `bank/<exam>/<variant>/<task>/<n>/`, whenever they
  were made. What one `proctor run` did is a log entry,
  `log/<id>/entry.json` (with its `status.json`, the variant hooks' output
  under `hooks/` and the compiled Harbor tasks under `harbor/`), and the
  exam's definition as it stood is kept once per hash under `exams-seen/`.
  Nothing under `attempts/` is read; `project/migrate/attempts-to-bank.py`
  moves it.

  | before | now |
  |---|---|
  | `attempts/<batch>/<variant>/<task>/<n>/` | `bank/<exam>/<variant>/<task>/<n>/` |
  | `attempts/<batch>/batch.json` | `log/<id>/entry.json`; its `exam_def` in `exams-seen/sha256-<hash>.json` |
  | `attempts/<batch>/<variant>/hooks/variant.setup.log` | `log/<id>/hooks/<variant>.setup.log` |
  | manifest `batch` | manifest `log` |
  | `--batch <id>` on `grade`, `friction`, `report` | gone |
  | `PROCTOR_BATCH` | gone; `PROCTOR_EXAM` is the exam's id |
  | `PROCTOR_CONTAINER` `proctor-<batch>-…` | `proctor-<exam>-<variant>-<task>-<attempt>` |
  | `results.jsonl` `batch`, `friction.json` `attempt.batch` | gone |
  | `stats.json` `built_from.batches`, `outdated_checks[].batches` | `built_from.log`; gone |

  `run` no longer continues an interrupted run for attempts whose compiled
  script checks are not the checks as they are now, so outdated attempts
  are made again by moving their directories out of the bank.

- **Breaking: the vocabulary is exam, variant, attempt and batch**
  (`project/plans/exam-variant-attempt.md`, step 1 of
  `project/plans/exam-variant-attempt-removal.md`). The words change and
  nothing else does; no old spelling is read, and existing runs are not
  migrated (make them again). The name map:

  | before | now |
  |---|---|
  | `suites/`, `suite.json`, `skills/proctor-suite/` | `exams/`, `exam.json`, `skills/proctor-exam/` |
  | `runs/<experiment>/`, `experiment.json` | `attempts/<batch>/`, `batch.json` |
  | `experiment.json` `suite`, `suite_hash`, `suite_def` | `batch.json` `exam`, `exam_hash`, `exam_def` (and `variants`, `tasks`: below) |
  | `arms` in `suite.json`, an arm's `samples` | `variants/<id>.json` beside `exam.json`, the exam's `attempts` (below) |
  | manifest `experiment`, `arm`, `sample`, `run_id`, `attempts` (retries), `suite_hash` | `batch`, `variant {id, hash}`, `attempt`, `id`, `tries`, `task {id, hash}` (below) |
  | `results.jsonl` `run_id`, `arm`, `sample`; `stats.json` `arms`, `k_cells`, `n_cells`, `attempted`, `runs` | `id`, `variant`, `attempt`; `variants`, `k_attempts`, `n_attempts`, `started`, `attempts` |
  | `friction.json` `friction/0`, `run {experiment, arm, task, sample}` | `friction/1`, `attempt {exam, batch, variant, task, attempt}` |
  | `PROCTOR_SUITE_DIR`, `PROCTOR_ARM`, `PROCTOR_SAMPLE`, `PROCTOR_EXPERIMENT`, `PROCTOR_CELL` | `PROCTOR_EXAM_DIR`, `PROCTOR_VARIANT`, `PROCTOR_ATTEMPT`, `PROCTOR_BATCH`, `PROCTOR_ATTEMPT_DIR` |
  | `{{arm}}`, `{{sample}}` | `{{variant}}`, `{{attempt}}` |
  | `hooks.arm`, `hooks.sample` (`arm.setup.log`, `sample.setup.log`) | `hooks.variant`, `hooks.attempt` (`variant.setup.log`, `attempt.setup.log`) |
  | `--arm`, `setup --check --live` | `--variant`, `setup --check --probe` |
  | a judge check's `samples` | `votes` |
  | the report's run, Run summary, Every run, run id | attempt, Attempt summary, Every attempt, batch |

  The spellings before suite/task (`evals/`, `eval.json`, `cases/`,
  `PROCTOR_EVAL_DIR`, `PROCTOR_CASE`, `PROCTOR_CASE_JSON`, `{{case}}`) are
  no longer read. `suite-authoring` is `exam-authoring`; `nb-changes`'
  `suite-green` check is `tests-green`. `project/gate/old-words.sh` fails
  on an old word outside its allow-list.

- **Breaking: variants are files, and a variant and a task each have a
  hash** (step 2 of `project/plans/exam-variant-attempt-removal.md`).
  Each variant is `exams/<exam>/variants/<id>.json`, declared data only:
  agent or runner, harness, provider, model, bundle, and a free-text
  `note` where a variant had `description`. `attempts` moves from each
  variant to `exam.json`, for every variant alike. An `exam.json` that
  still lists `variants`, or a variant with `attempts` or `description`,
  is refused with where it goes; every exam here was converted (where
  variants asked for different counts, the exam takes the smallest:
  `code-change` is 1). The exam's hash no longer covers variants. A
  variant's hash covers what it declares but its id and note, its bundle
  by content; a task's covers what the agent meets (the prompt, an
  oracle's answer sheet, the program template, the hooks and the fixture
  with its source), not the checks or expected values, so a changed check
  is a regrade and never a rerun. Manifests record `exam`,
  `variant {id, hash}` and `task {id, hash}` in place of `exam_hash`;
  `batch.json` keeps `exam_hash` and `exam_def` and adds `variants`
  (`{id, hash, def}`) and `tasks` (`{id, hash}`). `run` refuses a changed
  variant under an id that has made attempts, saying what changed: give
  it a new id. `resume` refuses a changed variant (its bundle included)
  as it refuses a changed exam. A git bundle may name `paths`, the part
  of its revision to hand over. Variants load in file-name order, so the
  reference (the first variant) is the first file by name. A `batch.json`
  from before this is refused, not read: make it again.

- **Breaking: an exam's attempts are a pool, and `run` fills its gaps**
  (step 3 of `project/plans/exam-variant-attempt-removal.md`). An exam's
  attempts are every attempt of every batch of it under `attempts/`; one is
  current while its variant's and its task's hashes are today's, and stale
  after. `proctor run <exam>` makes, per variant and task, the exam's
  `attempts` less the current, completed ones, in a new batch, numbering
  each after the attempts already on disk; with nothing missing it says so
  and makes nothing. Adding a variant makes only its attempts; changing a
  task makes only that task's again. **`proctor resume` is gone, with no
  alias:** after a crash, `proctor run <exam>` continues the interrupted
  batch in place (attempts `pending`, `running` or `failed`) while its
  runner, Harbor version, compiled Harbor tasks, engine and nb image still
  hold, says so, and otherwise says which changed and makes what is missing
  in a new batch. `proctor list <exam>` shows each variant's current
  attempts against those wanted, the stale ones and an interrupted batch.
  `grade`, `friction` and `report` take the exam, not a batch id, and read
  its current attempts; `--batch <id>`, `--variant` and `--tasks` narrow
  them (and `--variant`, `--tasks` narrow `run`). Stale attempts are listed
  with the hash that moved and never graded, read or counted; friction no
  longer reads attempts of a variant whose file is gone. `report` writes
  `reports/<exam>/explore/` in place of `reports/data/<batch>/`, moves an
  earlier report there into a directory named by when it was built, and no
  longer copies `batch.json`; `stats.json` drops `batch` for `built_from`
  (when, the batches, each variant's and task's hash, current and stale
  counts with each stale attempt's reason, the narrowing, and per variant
  the conditions that differed between its batches), which the page's
  reproducibility section shows, with a warning when one variant's
  attempts were made under mixed conditions. A report leaves out a variant
  with no current attempt. `results.jsonl` rows carry `batch`.
  `batch.json` lists the attempts it set out to make (`attempts`, each
  `{variant, task, attempt}`) in place of `planned`, and its `variants` and
  `tasks` are only those it made attempts of; a `batch.json` without
  `attempts` is refused, not read. `proctor baseline <batch>` and
  `report --against <batch>` still take a batch id until `live` replaces
  them.

- **Breaking: one variant may be live, and it is the report's reference;
  the baseline is gone** (step 4 of
  `project/plans/exam-variant-attempt-removal.md`). `proctor live <exam>
  <variant>` writes `exams/<exam>/live.json`, `{"variant": {"id", "hash"},
  "since"}`, to commit with the exam; `proctor live <exam>` says which is
  live. With a variant live, the report compares every other variant with
  it; with nothing live it compares none, and the verdict card shows the
  variants side by side ("Two variants, side by side", "None is live
  yet."). The first variant file by name is no longer the reference. A mark
  naming a variant with no file, or whose file has changed since it was
  marked, is a warning from `list`, `run`, `report` and `proctor live`, and
  nothing is live until a variant is marked again; delete the file to have
  none. `stats.json` drops `baseline` for `live` (`variant`, `since`, and
  `not_reference` when the mark does not hold or the variant has no
  attempt in the report); `comparisons` are against the live variant only.
  The page says "live" where it said "the reference" and drops the baseline
  block, the task cards' baseline columns and the chart's baseline mark.
  **Removed, with no replacement:** `proctor baseline`, `baseline.json`
  (`exams/code-change/baseline.json` is deleted; a leftover file is
  ignored), `report --against`, `--tolerance` and `--fail-on regression`.
  `agent-onboarding`'s reference variant `live` is `shipped`, and its
  trial's version key with it.

- **A script check on an agent variant that changed since the attempt is
  `outdated`, not silently the old verdict.** Its script ran in Harbor's
  verifier when the attempt was made, and `grade` only reads what it
  wrote, so an edited script returned the old script's verdict and a new
  one was `error` ("the verifier did not run this check"), failing every
  agent attempt when it was a headline check. Now `grade` compares the
  batch's compiled copy (the check's line in `test.sh`, the files of the
  exam and fixture copies but their definition files and other checks'
  scripts, `PROCTOR_EXPECT`) with the check as it is, and a difference, a
  check the batch never compiled, or no compiled copy is a new outcome in
  `checks.json`, `outdated`, whose reason says which and ends "make the
  attempt again". It is out of every rate like an undecided verdict,
  never a fail: an outdated headline check makes the attempt undecided.
  `grade` prints a line naming the attempts, the checks and the batches to
  move out of `attempts/` before `proctor run`; `results.jsonl` rows carry
  `outdated`, `stats.json` adds `outdated_checks` and a check rate's
  `outdated` count, and the report warns at the top, shows "· N outdated"
  beside a check's rate and "outdated" for such an attempt. Script checks
  on nb variants, built-in and judged checks are regraded as before.

Entries below this line, the unreleased ones above 0.3.6 included, use the
earlier words: suite (exam), arm (variant), experiment (batch), sample,
cell or run (attempt).

- `proctor friction <experiment> --reader <judge>`: a chat judge reads
  each run and adds the points rules cannot see (`repair`, `lost`,
  `reread`, `dead-end`, `workaround`), each with a quote checked against
  the call it cites and a model source. One call per run, kept in the
  cell and reused.
- A `result_matches` check: a regular expression over every tool result
  of a run, so `not_result_matches` fails a run that was shown something
  it should never be shown, whatever it did next.
- `proctor friction <experiment>` writes `friction.json` beside each
  completed cell's trajectory: the points where the run paid a cost the
  task did not ask for, each with a quote from the call it cites, and a
  summary. Rules only, no model and no key. A suite may declare a
  `surface` (what is under test, its docs and internals, the ways round
  it, what a failed call to it looks like), which the rules sort each
  run's actions by; `docs/configuration.md`, "The surface under test".
  `suites/tern-docs` declares one.
- An agent arm may name a `bundle`. It is the host's: hashed when the
  experiment starts, recorded in every cell's manifest, handed to arm
  hooks as `PROCTOR_BUNDLE`, and a changed one refuses `resume`, as on a
  bare arm. Nothing of it enters the agent's container; an `agent: nb`
  arm with a bundle cannot use `{{bundle}}` in its template (#11).
- An agent arm's bundle reaches a fixture's services: when the fixture has
  `image.services`, proctor copies the arm's bundle beside them as
  `bundle/` (empty for an arm without one), so a compose service can
  mount `./bundle` and serve it on the agent's `localhost`. The agent's
  own container still holds none of it. A fixture's services directory
  may no longer hold a `bundle` of its own.
- Ingest no longer fails a cell over a tool name the model made up when
  the agent refused the call with an error result (Sonnet calling `bash`
  under Claude Code 2.1.301). The call is counted, as kind `none`; an
  unknown tool that ran still fails the cell as `adapter_unmapped`.
- `suites/tern-docs` on `fixtures/tern-shop`: Claude Code on Sonnet
  implements six shipping functions against an invented carrier's API,
  with its docs as shipped, restructured, or absent; the arms differ only
  in the bundle.
- A Qwen Code call through its `tool_call` wrapper (how 0.25 calls a tool
  it loads on demand, `web_fetch` among them) is counted and graded as the
  tool inside, with that tool's arguments: `core.json`'s kinds, the
  `tool_calls` window and every `tool_*` check. Before, such a fetch
  counted as `meta` and no check could see it. `trajectory.json` keeps the
  wrapper. A cell ingested before this keeps its old `core.json`; a
  regrade reads the calls anew (#10).
- `tool_args` takes `match: regex`: each string in `args` is a .NET
  pattern over the call's string argument, so a check can ask for any URL
  under a base. `not_tool_args` with it asks that no call matches (#10).
- Agent arms run on Modal: `harbor.engine: modal` puts each cell's
  containers in Modal sandboxes on your Modal login, with no Docker or
  podman on the machine (`docs/modal.md`). `--config <file>` on `list`,
  `run`, `resume` and `grade` reads another machine file in place of
  `suites/proctor.json`, so a suite tried locally runs on Modal by naming
  `suites/proctor.modal.json` (`proctor.modal.example.json` is committed).
  On Modal, `run` refuses an agent arm whose model is on this network and
  a fixture with `image.services`, before anything starts. nb's layer for
  an `agent: nb` arm is copied into the task on Modal rather than built
  locally; on docker and podman nothing changed.
- `proctor setup` offers Modal as an experimental sandbox: choosing it
  writes `harbor.engine: modal` into `suites/proctor.json`, checks the
  Modal login, and goes on to the Claude Code and Codex steps. Daytona,
  E2B and Runloop are still recorded as intent only.
- The verdict card opens with a chart (inline SVG, no script): each arm's
  pass rate on its 95% interval, the baseline's own score marked beside it,
  and each arm's change in points with its interval, hollow when not
  confirmed. Below it, a line for each arm that moved and one for every arm
  that did not, where it had a paragraph per arm; `summary.md` has the same
  numbers as a table. A one-arm run with no baseline gets the card too (its
  *Result* row is gone). `stats.json` gains `score` on each baseline arm:
  the mean of the baseline's pinned scores over the tasks it was paired on.
  The arm-against-reference sentence leaves the card; Results has it.
- With a baseline, the card says whose runs it pinned when an arm was
  never among them (`stats.json` `baseline.pinned`), so an arm newer than
  the baseline does not read as "unchanged since then".
- Results compares each arm with the reference in one table, where it had a
  paragraph per arm.
- Results' verdict against the reference uses the card's words (direction,
  then confirmed or not), where it said "no detectable difference" beside a
  card that said "better (unconfirmed)".
- A check that could not grade more than one run, and more than 5 runs
  or 5% of those it applies to, is one warning above the numbers with its
  count per arm (`stats.json` `errored_checks`), and leaves the per-arm
  lists under Failures. `code-change`'s `change-fits` (13 of 27 runs, on
  every arm) had a near-identical block under each of five arms.
- A one-arm run with no baseline no longer states a detectable difference.
- A check whose value is `"@expect"` describes itself in a sentence ("the
  answer contains what the task expects", not "answer contains is what the
  task expects").
- The suite's checks are listed once, in Tasks; each task lists only the
  checks its fixture or its own file adds.
- With different sample counts per arm, the about table says which arm ran
  each task how many times, and the Tasks explainer no longer says every
  score averages several runs.
- The report review (`project/plans/report-structure.md`, 2026-11-02):
  - The verdict card says why a verdict is unconfirmed once, for the run, from
    its widest pairing (`stats.json` `mde` was the narrowest). With a baseline,
    each arm's row says how it stands against the reference arm, where the
    foot repeated "For the same reason" once per arm. An arm paired on fewer
    tasks says its own reach on its row (`mde_points` on its comparison).
  - *What ran* is *Run summary* (anchor `#run-summary`).
  - A check's `error` is out of its rate, as `needs-judge` is, and counted
    beside it; Failures lists it apart as "could not grade", where it said
    "failed". `stats.json` gains a per-arm `errored` list. A headline check
    that errored still fails the run, so pass rates do not move; guardrail
    rates do (`code-change`'s `change-fits` read 44% for `floor` with 5 of
    its 9 runs errored, and reads 100% of the 4 it graded).
  - Agent arms' cost: `cost_usd` from `core.json` reaches `results.jsonl`
    (`usage.cost_usd`), `stats.json` (`cost_usd`, `cost_total_usd`) and a
    Cost column in Cost and effort. nb reports none.
  - "Arms lost runs unequally" compares the share each arm decided, so arms
    with different sample counts that lost nothing no longer warn.
  - A failed run's reason leads with the headline check that failed it,
    not the first check declared.
  - `summary.md` keeps a multi-line reason in its table row (`<br>`).
  - The report no longer says nb where it means any harness: `exit_reason`
    reads "the run ends with 'ok'", "How each completed run ended", an
    "Ended" column.
  - A table wider than the page scrolls inside it.

- Judges no longer assume Cloudflare. A `systemone` judge may name a
  `model`, which `decide` sends in the request (OpenRouter's
  `/alpha/decisions` serves every decision model from one path); one
  without a `model` sends none, as before. The answer is read unwrapped
  (`{model, answers, usage}`); the Cloudflare gateway's `result` envelope
  is no longer read. `suites/proctor.example.json` routes all three judges
  through `${LLM_GATEWAY}/openrouter`: `typesafe/jev-1.14`, `z-ai/glm-5.3`
  and `moonshotai/kimi-k2.6`. A new judge model or endpoint changes every
  verdict hash, so the first `grade` after switching asks every model check
  again (decision models bill input only; the chat judges bill as
  OpenRouter prices them).

- A vendor agent's cell no longer carries a check that reads only nb's
  loop nudge (`loop_nudged` alone, or with a `description`): `checks.json`
  has no entry for it and its rate is over nb's cells, where it used to be
  `error` on every Claude Code, Codex and Qwen Code run. Combined with
  other fields it stays, the nudge part an `error` as before. Such a check
  in `grading.pass` or `grading.validity` beside a vendor arm is a problem,
  since those runs could never pass it. A cell graded before keeps its
  `error` until it is graded again.

- The report's arms table names an agent arm's harness as the agent at its
  pin (`claude-code 2.1.301`) and its provider as "—", where it printed
  `?` for both: the route an agent reached is the machine's and the
  experiment does not record it yet (#9).

- `suites/code-change`: stage 2's arms, `cc-sonnet` (Claude Code on
  claude-sonnet-5-5) and `codex` (Codex on gpt-5.5) on OpenAI's own route.
  `codex-mini` is gone; its October cells ran on the local qwen.

## 0.3.6 — 2026-11-01

- A Harbor trial that proctor kills for outrunning its wall limit is torn
  down: the containers, networks and images of its compose projects, found
  by their `com.docker.compose.project` label. Harbor's own `compose down
  --rmi local` is what a kill skipped, so a timed-out cell used to leave
  the agent's container running with two images behind it, and the
  failure only said to look. `docs/podman.md` §6b says what a trial leaves
  behind and who removes it.

- `suites/oracle-change`: the oracle on a real model. code-change's
  `add-retry-flag` with the semantics left out of the prompt; the answer
  sheet holds them, each deliberately not the conventional choice, and the
  acceptance tests check them, so an agent that guesses fails. nb on Haiku,
  bare and through Harbor, judged by Haiku.

- `checks/acceptance.sh` (code-change, oracle-change) removes the
  acceptance tests it copies into the work tree when it exits. Left in place,
  the fixture's own `tests-pass` ran them too, so the two checks failed
  together.

- The oracle has a gate: `suites/gate-oracle` and `OracleGateTests` run
  one Mock arm bare and through Harbor on three oracle tasks, one per ending,
  and assert the same counted core (apart from the judge's tokens), user
  turns and checks. The gate tests take `PROCTOR_GATE_ENGINE` (`docker`, the
  default, or `podman`); both gates are green on podman.

- The oracle runs on nb agent arms through Harbor
  (`project/plans/oracle-in-proctor.md`, work items 3 and 4), so `run` no
  longer refuses an oracle suite with an nb `agent` arm. Vendor arms beside
  an oracle task are still refused. `Harbor/oracle_user.py` (`OracleUser`)
  is compiled beside the agents with its prompt template when an agent arm
  meets an oracle task, and an oracle cell's job names it as the
  `user_agent`. It sends the task over the ACP bridge with
  `acpx --format json`, judges each finished `ok` turn with the host's
  `nb --judge`, and records each prompt, the judgements and the ending in
  `oracle-user.json`. The cell's compiled program drops the final `run`, and
  its diff leaves out the bridge's `.acpxrc.json`. Ingest lines the record up
  with nb's trajectory: it marks the answers `source: oracle` with their
  keys, as on the bare path, takes `oracle_miss` and `oracle_budget` from
  the record, and fails the cell on a broken judge. It records the judge's
  tokens in the manifest's `oracle`, outside the counted core.

- The oracle runs on bare nb arms (`project/plans/oracle-in-proctor.md`,
  work item 2): a cell of an oracle task gets `oracle @<sheet>`, `oracle
  provider <judge>` and `budget oracle_turns <n>` ahead of its template, and
  nb's in-process oracle answers. `oracle_hit`, `oracle_turns` and
  `oracle_misses` are checks now, read from the user turns nb marks
  `source: oracle` (with their `keys`) and the exit reason; one on a suite
  with no oracle task is a problem. An oracle suite with an `agent` arm is
  still refused at `run`. Also fixed: a `{max}` check given a bare number
  (`"denied_calls": 2`) threw from validation instead of reporting it.

- A task can name an answer sheet (`"oracle": "oracle/<task>.md"`, inside
  the suite directory), and `suite.json` takes an `oracle` block (`judge`,
  an nb config entry; `turns`, default 8). Validation only, for now
  (`project/plans/oracle-in-proctor.md`, work item 1). A vendor arm on a
  suite with an oracle task is refused. A template may not state the oracle
  itself, and with an nb agent arm it must end in exactly `run {{prompt}}`.
  `run` refuses an oracle suite as "not yet" until the oracle's run paths
  land.

- `NbAgent` is an ACP bridge target (nb's `plans/atif-and-acp.md` stage 5):
  it implements Harbor's `ACPAgentMixin`, declares `bridges={acp}`, and
  launches `nb --acp` with `--trajectory` in its logs dir, so nb rewrites
  `agent/trajectory.json` after every prompt and ingest reads it as on the
  direct path. Run once through Harbor 0.24 with the spike's scripted user
  on Mock (podman): two turns, one session. Nothing routes an arm through
  the bridge yet; that is the oracle's step 7.

- nb writes its own ATIF (`--trajectory`, nb's `plans/atif-and-acp.md` stage
  3), and `Run/NbAtif.cs` is gone. An nb cell in Harbor takes
  `agent/trajectory.json` as a vendor agent's, now with per-step model,
  tokens and cost. Bare cells gain `trajectory.json` beside
  `transcript.jsonl` (set by `NB_TRAJECTORY`, which a runner script passes
  on untouched); grading still reads the JSONL there. Needs nb at or after
  this change: an older nb ignores the variable, and its Harbor cells fail
  as "nb exited N without a trajectory".

## 0.3.5 — 2026-10-27

A bug bash: issues #2 to #5. Nothing that read before stops reading, hence
a patch.

- `grade` takes `--arm` (repeatable) and `--tasks`, grading only those
  cells and leaving the others' `checks.json` as they were, so a paid judge
  pass can be staged one arm at a time (#2). `baseline` refuses a second
  `--arm`; `report`'s note on ungraded cells counts them per arm.
- A judge's quote is verified with all whitespace removed, against the
  window as written and with its JSON string escapes undone, so a correct
  quote of indented or escaped JSON is no longer discarded (#3).
- A judge that stays throttled is stopped for the pass (#4): a 429, 503 or
  529, or a rate-limit phrase in the body, is retried at 2, 4 and 8 s or
  its `Retry-After`, then every later check on that judge is `error` with
  the endpoint's own words and no request. The OpenAI SDK no longer
  retries underneath. `grade` prints the stop, and errored checks per
  judge after its summary.
- Under `harbor.engine: podman`, `run` and `resume` refuse to start unless
  `podman compose` is docker-compose v2 and short image names resolve,
  saying what to install or add (#5). The README and the porting guide
  name the prerequisites, and how to reach a gateway on the host's
  loopback.
- Harbor's dependencies resolve under a seven-day cooldown
  (`uv tool run --exclude-newer "7 days"`), so a poisoned release pulled
  within the week never reaches a run; a Harbor pin under a week old
  fails to resolve until it ages.

## 0.3.4 — 2026-10-20

Agent arms on podman. Nothing that read before stops reading, hence a patch.

- `proctor.json` `harbor.engine` is the container engine Harbor drives,
  `docker` (the default) or `podman`: Harbor's job file states it as
  `environment.type`, and nb's layer is built in that engine's store
  (`localhost/proctor-nb:<hash>` under podman). Podman needs its API socket
  (`systemctl --user enable --now podman.socket`) and a compose frontend.
- `experiment.json` records the engine under `harbor.engine` (absent reads
  as `docker`), and `resume` refuses a changed one.

## 0.3.3 — 2026-10-20

Harbor conformance at no cost (`project/plans/harbor-conformance.md`).
Nothing that read before stops reading, hence a patch.

- `fixture.json` `image.services` names a directory beside `repo/` with a
  `docker-compose.yaml` of extra services, copied into each compiled task's
  `environment/`. Harbor runs them beside the agent, in its network
  namespace.
- `fixtures/stub`, a scripted model endpoint (Anthropic Messages, OpenAI
  Responses and Chat Completions; `STUB:run=`, `STUB:fail`, `STUB:hang`),
  and `suites/conformance`, every vendor agent against it.
  `ConformanceTests` (`PROCTOR_CONFORMANCE=1`) runs those cells and an oracle
  pass of `code-change`'s reference solutions through Harbor.
- Ingest maps the 16 tools the three agents offer at their pins that it
  lacked. Two kinds are new: `schedule` (Claude Code's `Cron*`,
  `ScheduleWakeup`) and `meta` (Qwen Code's `tool_search`, `tool_call`).
- `suites/agent-smoke` is retired: it is `Proctor.Tests/suites/harbor-unit`,
  test data that never runs.
- `code-change`'s `add-retry-flag` reference solution works again: it is a
  patch, and it no longer needs python.

## 0.3.2 — 2026-10-20

`code-change`'s first run on all four agents, and what it found. Nothing
that read before stops reading, hence a patch.

- Ingest knows nb's wire names: `todo_write` and `todo_read` (`todo` is the
  directive, not a tool) and `apply_patch`. Every nb cell that planned had
  failed `adapter_unmapped`.
- Ingest knows Codex's goal tools (`get_goal`, `create_goal`,
  `update_goal`, as `plan`) and `request_user_input`, a new kind, `ask`.
- `suites/proctor.json` `agents.codex` takes an optional `config`: a native
  Codex `config.toml`, relative to `suites/`, passed to Harbor's Codex agent,
  which merges the base URL over it. A llama-server Responses endpoint takes
  only function tools, so Codex on one needs `web_search = "disabled"` and
  `[features] multi_agent = false`. `config` on another agent is "not yet".

## 0.3.1 — 2026-10-18

Codex and Qwen Code as agents, and `code-change` ported to agent arms
(staging step 5 of `project/plans/agent-neutral-runs.md`). Nothing that
read before stops reading, hence a patch.

- `arm.agent` takes `codex` (OpenAI models: `gpt-…`, `o<n>…`, `codex-…`) and
  `qwen-code` (Qwen models: `qwen…`), each with a pinned `agent_version` and
  the model bare. `suites/proctor.json` `agents` takes `codex` and
  `qwen-code` beside `claude-code`; both are OpenAI-dialect routes, and the
  key travels as `OPENAI_API_KEY` in Harbor's process only.
- Both CLIs are installed in the task's image from npm, on Node 22 by nvm,
  at the pin. Codex is Harbor's own agent, which finds the pin and installs
  nothing. Qwen Code's Harbor agent reinstalls from the network on every
  trial, so proctor brings `Harbor/qwen_code_agent.py`, which checks the
  image's `qwen --version` against the pin instead and fails setup when it
  differs.
- Ingest maps each agent's tools to proctor's kinds (`Run/Ingest.cs`); an
  unknown tool is `adapter_unmapped`, as on Claude Code. Qwen Code's
  `save_memory` is a new kind, `memory`. Harbor runs both with approvals and
  sandbox bypassed, so `denied` is 0 by construction.
- `suites/code-change` runs four agent arms through Harbor: `floor` (nb on
  qwen3-coder-next, now `agent: nb`), `qwen` (Qwen Code on the same model),
  `cc-haiku` and `codex-mini` (one sample each). Its runner, mounts and
  sample hooks are gone; `fetchcli` and `widgets` gain an `image`. The
  `stays-in-work` validity check is dropped from it: it reads the
  transcript, which the verifier has none of, and in the agent's container
  there is nothing outside the checkout for it to guard. It moves, with
  the sample teardown hook, into `suite-authoring`, which still runs on the
  runner path and shared both. Its `baseline.json` is kept for `floor`.
- A vendor agent's run with no turn at all is failed at ingest ("its model
  never answered"), not a completed run: Codex retries an unreachable
  endpoint until the agent timeout, which would otherwise read as a
  `time_budget` run that did nothing.
- nb's Harbor agent took the key's variable as `secrets` (a list) while the
  job passed `secret`; it takes `secret` now. No run was affected: the only
  nb arm in Harbor so far was on Mock, which has no key.
- `docs/porting-to-agent-neutral.md`: the porting guide's draft, from the
  `code-change` move.

## 0.3.0 — 2026-10-18

nb as a Harbor agent, and the deterministic gate (staging step 4 of
`project/plans/agent-neutral-runs.md`). Two things in `suite.json` change
meaning, hence the minor bump:

- **`"agent": "nb"` now runs nb through Harbor.** It used to be the same as
  no agent (nb bare). An arm without `agent` is nb bare, exactly as before;
  no suite in this repository named `agent: nb`. The arm keeps `harness`,
  `provider` and `model`; `runner`, `command` and `agent_version` are
  refused on it, and `bundle` is "not yet".
- **`harbor` is no longer an arm id**: it is the experiment's directory for
  the compiled Harbor tasks, and an arm by that name put its cells there.

What lands:

- An nb arm's image copies nb's own layer: `suites/proctor.json` gains
  `harbor.nb`, nb's self-contained linux-x64 publish, which `run` builds once
  into a local `proctor-nb:<content hash>` image. Its `nb --version` must be
  the host nb's, which compiles the program; `resume` refuses a changed
  publish.
- The program is compiled on the host per cell, as on the bare path, with
  `{{work}}` as `/app`. nb's Harbor agent is proctor's
  (`Harbor/nb_agent.py`, compiled into `runs/<id>/harbor/agents/` and part
  of the compiled tree's hash); it uploads the program and nb's config and
  runs nb on stdin in `/app`.
- The agent reaches the host of its provider's `Endpoint` in nb's config,
  or nothing at all for a provider without one (Mock). The endpoint's
  `${VAR}`s go to the agent by value; the key's variable goes by name, its
  value only in Harbor's process environment.
- Ingest converts nb's JSONL (`agent/nb.jsonl`) to ATIF (`Run/NbAtif.cs`):
  one agent step per nb turn, and nb's own fields (a turn's source, a
  call's approval, a result's structured result, the trailer) under
  `extra`, which the ATIF reader reads back. An nb arm in Harbor fails as
  on the bare path: no result trailer before the time limit is a failed
  cell. `core.json` counts nb's denials.
- `loop_nudged` is graded on nb arms in Harbor; it stays `error` on a
  vendor agent's cell.
- `suites/gate/`: one Mock arm bare and the same arm through Harbor, five
  tasks. `GateTests` runs it when `PROCTOR_GATE` names the publish and
  asserts the two give the same nb transcript, counted core and
  `checks.json`, apart from timings and the checkout's path (a host
  directory bare, `/app` in the image).

## 0.2.6 — 2026-10-16

`grade` grades agent cells (staging step 3). Nothing on disk breaks; an nb
cell is read and graded as before, and no rendering changed.

- An agent cell's run is its ATIF `trajectory.json`, with `core.json` as the
  trailer: `exit_reason`, `max_tool_calls`, `max_tokens`, `max_duration_ms`
  and `denied_calls` read proctor's own count. The windows (answer, answer
  JSON, tool calls, tool results, user turns) are the main agent's
  conversation; a tool result is an error when the agent marked it so.
- A script check on an agent cell is not rerun: its verdict is the cell's
  `reward.json` key, its reason the first line of `verifier/<check>.out`. A
  missing key is `error`, with the script's output or the diff that did not
  apply as the reason.
- `loop_nudged` on an agent cell is `error`: only nb nudges.
- `core.json` gains `duration_ms`, the agent's phase alone, and takes Harbor's
  token counts when the trajectory has none.

## 0.2.5 — 2026-10-16

Agent arms: a real vendor CLI, run by Harbor in the fixture's image, as an
arm beside nb's. Staging step 2 of `project/plans/agent-neutral-runs.md`:
compile, run and ingest, for Claude Code. Nothing on disk breaks; an nb
suite runs exactly as before.

- An arm with `"agent": "claude-code"`, `"agent_version"` (pinned) and a
  bare Anthropic `model` runs through Harbor. The suite pins
  `harbor.version` and sets `limits.wall_ms`; every task's fixture declares
  an `image` (`from`, `env`, `run`). `codex` and `qwen-code` are "not yet".
  `limits.tool_calls`, `tokens` and `cost_usd` are "not yet"; a sample hook
  in a suite with an agent arm is refused (its setup goes in the image).
- `suites/proctor.json` gains `agents.claude-code` (`base_url`, `api_key`,
  `${VAR}` resolved at `run`) and `harbor.uv`. The base URL's host is the one
  name the agent's container may reach. See `proctor.example.json`.
- `run` compiles each agent arm's tasks under `runs/<id>/harbor/tasks/` and
  records `harbor` (version, tasks hash) and `versions.harbor` in
  `experiment.json`; `resume` refuses compiled tasks changed on disk. Each
  cell runs one Harbor job in `<cell>/harbor/`, with telemetry off and the key
  only in Harbor's environment.
- An agent cell holds `trajectory.json` (ATIF), `agent/` (native logs),
  `diff.patch`, `verifier/` and `reward.json` (one key per script check: 1
  pass, 0 fail, 2 needs-judge, absent error), and `core.json`: proctor's own
  count (turns, tool calls, kinds, tokens, exit reason). Its manifest has
  `agent` in place of `runner`, `harness` and `provider`, which are now
  optional. A Harbor exception other than a timeout, a version other than the
  pinned one, or a tool the mapping does not know fails the cell.
- `grade` skips an agent cell and says so: grading from the trajectory and
  the rewards is staging step 3.
- `Subprocess.Run` takes a timeout; the Harbor process is killed past the
  wall limit plus build, verifier and start-up time.

## 0.2.4 — 2026-10-10

Two fixes and `report --against`. Nothing on disk breaks; `stats.json` only
gains fields.

- `report <id> --against <other>` compares each arm with the arm of the same
  id in another experiment, of this suite or another, in place of the
  baseline: "did the new condition help each arm?". The other experiment's
  decided cells are rescored under this suite's lists from their
  `checks.json`, with no judge calls, and the gap rule below applies. An arm
  the other experiment lacks is listed as unpaired. `stats.json`
  `baseline.against` and `baseline.unpaired`; `--tolerance` and
  `--fail-on regression` work as with a baseline.

- A baseline whose pinned cells were never graded on one of the suite's
  current `pass` checks no longer rescores them to 0. Those tasks keep the
  pinned score, `stats.json` lists the check under `baseline.gaps` (check,
  experiment, tasks), and the report and `report`'s console output warn.
  Within an experiment a missing pass check still fails the run.
- A `null` in a suite's `arms`, `grading.pass` or `grading.validity` is a
  problem naming the field, not a crash.

## 0.2.3 — 2026-09-23

Reasons that read right at a skim. Nothing on disk breaks. A model check's
reason comes from its verdict file, which a plain `grade` reuses, so an
existing experiment gets the new `decide` and `judge` wording only with
`grade --rejudge`; the other changes apply on the next `grade` or `report`.

- A `decide` reason says `, expected <answer>` whenever the label is not the
  expected one (`yes p=0.98, expected no`), below the threshold too; a
  `judge` check that fails unanimously says `no 3/3, expected yes — "…"`.
- A check with several fields leads its reason with the parts that decided
  the verdict, then the rest.
- `summary.md` writes code in single backticks; a path inside quotes in a
  free-text reason is no longer wrapped as code.

## 0.2.2 — 2026-09-23

The report, redesigned: a verdict card up top, one card per task, and a
table of contents. Nothing on disk breaks; `stats.json` only gains fields.

- `stats.json`: `baseline.arms.<arm>.confirmed` (the whole 95% interval lies
  beyond the tolerance on the verdict's side, or within it for `held`);
  `baseline.arms.<arm>.tasks.<task>` gains `diff_points` and `verdict` (the
  same tolerance, per task); `comparisons[].tasks` is the per-task difference
  in points; `arms.<arm>.tasks.<task>` is `{score, passed, decided}`.
  `verdict` is unchanged and is still what `--fail-on regression` reads.
- `report.html`: a sticky rail of sections and tasks (hidden below 900px)
  with an inline scrollspy; the verdict card per arm (Better, Worse or
  Unchanged, confirmed or not, the tasks behind it, why it cannot be
  confirmed, and which arm to read with care); a card per task with its
  checks (its own first) and every arm's runs, score and baseline verdict,
  and each run that did not pass. The Checks section and Results by task
  are folded into the task cards. Explanations are muted; monospace is for
  commands and paths only. The run id moved to Reproducibility and method.
  The wizard hat is inlined from `assets/pointy-hat.svg`, credited in the
  footer (CC BY 3.0).
- `summary.md` carries the same blocks: the card as a list, a `###` per task.

## 0.2.1 — 2026-09-23

A task's own description of a shared check reaches the report
(`project/bugs/per-task-check-descriptions-never-reach-the-report.md`).

- `stats.json` `task_details.<task>.checks` is a list of
  `{name, level, description}` instead of names; `level` is `suite`,
  `fixture` or `task`. `descriptions.checks` carries a check only when every
  declaration describes it the same way. The field is a day old and read by
  nothing outside proctor, so this ships as a patch.
- The report's Tasks table column is "How it is measured": the task's
  fixture- and task-level checks, headline first, each with the task's own
  sentence. A headline every task declares under one name is on every row.
  The Checks table reads "per task; see Tasks" for a check the tasks describe
  differently, and Failures puts each task's sentence beside the task.

## 0.2.0 — 2026-09-23

The vocabulary and the layout, renamed to suite / task / sample / check
(`project/plans/suite-task-check.md`). Breaking:

- `evals/` is `suites/`, `eval.json` is `suite.json`, `cases/` is `tasks/`;
  `evals/eval-authoring` is `suites/suite-authoring`.
- `experiment.json`: `eval`, `eval_hash`, `eval_def`, `cases` are `suite`,
  `suite_hash`, `suite_def`, `tasks`. `manifest.json`: `case` and `eval_hash`
  are `task` and `suite_hash`. `baseline.json`: `eval_hash` and `cases` are
  `suite_hash` and `tasks`. `results.jsonl` rows say `task`; `stats.json`
  says `suite`, `tasks`, `n_tasks` and gains `task_details`.
- Hooks and script checks get `PROCTOR_SUITE_DIR`, `PROCTOR_TASK` and
  `PROCTOR_TASK_JSON`; the program template placeholder is `{{task}}`;
  `baseline --cases` is `--tasks`.
- Every reader accepts the 0.1 spellings for this release only: the
  directory and file names, the JSON keys under `runs/` and in a baseline,
  the old environment names beside the new, and `{{case}}`. Writers use
  the new ones. 0.3 drops the old ones.

Added:

- A task file may declare `checks`, its own beyond the suite's and its
  fixture's; a cell carries the union, and a name at two levels is a problem.
- `@expect` on a built-in field reads `expect.<check>.<field>` before
  `expect.<field>`; a model check's `ask` may be `"@expect"`, read from
  `expect.<check>.ask`.
- The report's Tasks table shows each task's fixture and its own checks;
  the Checks table says which tasks carry each check.

## 0.1.0 — 2026-09-22

The first slice: `list`, `run`, `resume`, `grade`, `report`, `baseline`;
fixtures, arm bundles and baselines; containerised runs; the `decide` and
`judge` model checks; the report rebuilt for a reader who was not there.
