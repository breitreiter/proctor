---
type: todo
title: Loose ends
created: 2026-09-17
---

# Loose ends

Small things worth doing that are not worth a plan. An entry here is a
reminder with enough context to start from, not a design.

## Decide the process boundary with nb

Proctor can drive nb two ways: spawn `nb` as a subprocess and parse stdout, or
reference `nb.Core` and call `Nb.RunAsync` in-process. The subprocess path keeps
the boundary at the program file and the JSONL stream, which is the seam every
existing consumer already uses. The in-process path gives a typed `RunResult`
and no quoting or stream-deadlock problems, but couples proctor's build to nb's.
Pick one before the experiment manager grows a second code path.

## Define the JSON the site reads before the report skeleton hardens

Resolved the generation question: the site is a prebuilt bundle plus `data/`
JSON (brief, "The site is a prebuilt app plus data files"). What remains is
the data contract: the index file, the per-experiment files, and where the
derived statistics live (computed by proctor in .NET and written as JSON, so
the browser never does statistics and the markdown summary reads the same
numbers). Settle that schema before implementing the report skeleton in
`learnings/prior-art.md` §5, since both renderers depend on it.

## The first slice renders one self-contained HTML file

Built, then removed on 2026-11-22: auspex is the viewer, and `report.html`
would have had to be refactored for stages. The site below links frozen
copies of it in `docs/reports/`.

Before the site, `proctor report` emits `reports/data/<id>/report.html`, a
single file with inline CSS and script, plus `summary.md`. Both render from
`stats.json` and `results.jsonl`. Keep the template plain: a .NET string
template or a small templating package, no front-end toolchain. Its sections
are the per-experiment page from `learnings/prior-art.md` §5. The point is
to put a page in front of stakeholders in the first week and learn what they
read first. The component-library choice below only matters once the site
starts.

## Pick the component library for the report site

The brief wants an opinionated library with complete components and
micro-interactions, and no design system of our own. The bundle is built once
in proctor's repo, so a React app costs a consumer nothing, and MUI is a real
candidate: its DataGrid does client-side sort, filter and virtualised
rendering of large tables, which is the client-side load the brief now
expects. Alternatives with the same completeness: Mantine (less "Firebase
clone"), Ant Design (table-centric). Web-components libraries such as Web
Awesome remain an option if the bundle should stay framework-free. Tables are
the whole product, so judge candidates on their data table first, then tabs,
tooltip and details.

## CI for proctor

`.github/workflows/test.yml`: build, then `dotnet test`. The runner tests
spawn nb, so the workflow has to get an nb binary from somewhere: check out
`breitreiter/nb` beside proctor and build it (what `TestRepo.NbPath` assumes
today), or, once nb is on nuget.org as a tool, `dotnet tool install` it and
point `NB_PATH` at the shim. The second is the one to keep; the first is
fine until the nb package exists. The Mock provider is all the tests need,
so no keys. `first-slice.md` "Steps 1 through 7 run in proctor's own CI" is
the promise this pays.

## Publish proctor as a dotnet tool

`PackAsTool` on `Proctor.csproj` (`first-slice.md` says "later"; this is the
reminder), a tag-triggered release workflow that packs and pushes, mirroring
nb's `Feature_Release_Workflow_Publishes_To_Nuget.md`. Package id is
`Dreamlands.Proctor`: bare `Proctor` is taken on nuget.org
(`learnings/name-collisions.md`). Command name stays `proctor`. Do nb first,
so a consumer's CI can install both with two lines and proctor's own CI can
drop the sibling-checkout build.

## Candidates for nb, not proctor

All six are filed in nb's own tracker as of 2026-09-17, untracked there until
reviewed. Each is additive; none changes behaviour for a program that does
not name the new thing or for anyone building from source.

- `Feature_Injected_Reminders_Carry_A_Source_Tag.md`: loop and todo reminders
  carry `source` on their user event, so `loop_nudged` counts events.
- `Feature_Trailer_Carries_Cost_When_The_Entry_Declares_A_Price.md`: optional
  per-entry prices, `cost` in USD on the trailer, inherits `estimated`.
- `Feature_Trailer_Carries_Program_Hash_And_Nb_Version.md`: always-on
  `program_sha256` over the resolved events and `nb_version`. Found that
  neither csproj sets a version today.
- `Feature_Sample_Seed_Directive_For_Providers_That_Accept_One.md`:
  `sample seed <n>` in the `budget` key/value shape, named to avoid the
  existing `--seed <file>` flag; warns when the provider ignores it.
- `Feature_PackAsTool_Carries_Providers_Into_The_Tool_Package.md`: the
  provider copy targets are conditioned on a runtime identifier, so a RID-less
  pack would ship no providers; relax that, add the tool metadata.
- `Feature_Release_Workflow_Publishes_To_Nuget.md`: tag-triggered pack and
  push plus per-RID archives on a GitHub Release. The README already points
  at a releases page nothing fills.

## `resume` does not notice a changed agent route

`resume` refuses a changed suite and a changed compiled task (`harbor/tasks`
hashed), but reads `proctor.json`'s `agents.<agent>.base_url` fresh. The
task's `allowed_hosts` was compiled from the old host, so a changed route
gives a cell whose base URL the allowlist does not admit: Harbor's egress
sidecar (gost, transparent, sniffing `Host`/SNI) accepts the TCP connection
and drops it, and Codex logs `Handshake not finished` then `error sending
request`. Seen 2026-10-19 on `20261019-2118-code-change-ax3m` after `library`
became `library.lan`. Record each agent arm's route in `experiment.json` and
refuse a changed one, as for the suite.

## A bare gateway hostname does not resolve for a musl agent

Docker writes `search lan` with `options ndots:0` into the task container's
`resolv.conf`. glibc (Node: Claude Code, Qwen Code) still tries the search
list for `library`; musl applies it only to names with fewer dots than
`ndots`, so Codex (a static musl binary) asks for bare `library` and gets
`failed to lookup address information: Try again` until the agent timeout.
Worked around in the machine's `proctor.json` by spelling Codex's route
`library.lan`. Options: validation warns on a dotless agent host, or compile
pins the host's address in the task (an `extra_hosts`-style entry, if Harbor
allows one) so the spelling does not matter.

## Drop the pasta AppArmor workaround when Ubuntu fixes it

`docs/podman.md` §6 has Ubuntu 26.04 users add an include line and a
signal rule to pasta's AppArmor profile, without which every sidecar and
its network leaks (#7, closed with that workaround). Watch
[LP#2154379](https://bugs.launchpad.net/bugs/2154379). Once a passt
update ships a profile that lets podman signal pasta, say from which
version §6 is no longer needed. If more people trip on it first,
`HarborRun.CheckPodman` could warn when pasta's profile and its local
file have no `signal` rule; both files are world-readable.

## An oracle for vendor arms, the hokey way, if the refusal starts costing us

A task with an `oracle` is refused on Claude Code, Codex and Qwen Code arms
(`plans/oracle-in-proctor.md`, question 4): bridged through Harbor's simulated user, cc
and Codex leave no ATIF, and Qwen Code can't be a target at all. Revisit only if a
real use case is being disqualified by it, i.e. a suite we want to run on a vendor
agent needs the scripted user.

The cheap route skips the bridge entirely. Run the vendor agent direct, as today, and
give it a way to ask:
- **The channel:** inject an MCP server (`proctor_chat`, say) whose one tool takes a
  question and answers it with `nb --judge` against the task's sheet. The server runs
  on the host side of the wall, so the sheet stays out of the container.
- **The prompt:** append a line telling the agent it exists, e.g. "I'm not at my desk
  right now. Use the proctor_chat tool if you need to ask me a question; I'll respond
  immediately."

Costs to weigh first:
- **The prompt under test changes.** That's the thing nb's oracle refuses to do (no ask
  tool, no system-prompt steer). The results compare only to other arms run the same
  way, never to an nb arm on the in-process oracle.
- **A tool call is not a halt.** A model that would have stopped and asked in prose may
  call the tool instead, or may not. Ingest would count `proctor_chat` calls as oracle
  turns, and the method section has to say this arm's oracle is a tool.

## Find out why glm's multi-line diff quotes fail verification

Stage 2's grade (`runs/20261102-1143-code-change-jy0n`) had `change-fits` on glm `error` in
12 of 27 runs, every one a quote the verbatim check refused. On `add-retry-flag` the refused
quotes span several added lines and drop the diff's `+` markers
(`codex/add-retry-flag/1/verdicts/change-fits.glm.*.json` against its `diff.patch`), and a
single-line quote from the same answer passes; since the check strips only whitespace, the
markers alone would explain it, which would make it proctor's bug, not glm's. Unconfirmed:
reproduce it in `JudgeTests` with that window and quote before deciding. If it holds, the
likely fix is one more candidate form for a diff part (each side's lines with the marker cut,
old and new kept apart so a quote cannot stitch them), and then it is an issue. The
`rename-module` errors are a different thing: glm wrote `Legacy_Widgets` for
`Legacy.Widgets`, a wrong quote rightly discarded.

## Ids in the judge's material, so a citation can resolve without a perfect quote

The last suite run left a lot of checks undecided because the judge's
evidence was close to the material but not in it: a summary, or a
near-copy. `Judge.Parse` refuses the whole sample when any excerpt is not
found verbatim with whitespace removed, so a right answer with a sloppy
quote counts the same as an invented one, even when the excerpt was of
something that does exist.

The fix seen in prior art (auspex `project/learnings/inspect-petri.md`):
every item the judge reads carries an id (`[M12]`), and the citation is
the id. Bloom shows the failure on the other side
(`learnings/bloom-viewer.md`): its prompt asks for ids its formatter never
writes.

Sketch, untried:

- **Ids in `Window.Render`.** `tool_calls` and `tool_results` already
  number their items; make the numbers ids the judge can cite (`call 3`,
  `result 3`). Number the hunks in `diff` and the paragraphs in `answer`
  and `user_turns`; `prompt` is cited whole.
- **Evidence as `{"at": "result 3", "quote": "..."}`.** The id has to
  resolve, or the sample is refused as now. The quote is checked
  verbatim against that item alone, which is stricter than the whole
  window.
- **An id that resolves with a quote that doesn't** keeps the label. It is
  marked paraphrased, and the evidence shown is the item's own text, cut
  to length, never the judge's words. The verdict file records both, so
  the paraphrase rate per judge is countable.
- It may also settle the glm diff item above: a hunk id resolves whether
  or not the quote keeps the `+` markers.

Open: whether "paraphrased" counts towards decided straight away, or only
after a look at how often the cited item fails to support the label.

## Two loose ends of the move to OpenRouter

Cloudflare is gone (CHANGELOG, Unreleased): code, both configs and the docs are on
OpenRouter, and the candidate decision models are in `project/learnings/jev-judges.md`.

- **The judges are new models.** The move pinned `typesafe/jev-1.14` (served as
  `typesafe/jev-1.14-20261014`) and `z-ai/glm-5.3` where Cloudflare served `jev-1.13.0`
  and `@cf/zai-org/glm-5.2`, as each verdict file's `model` records. Stage 2's verdicts
  (`20261102-1143-code-change-jy0n`) and every bench number in `jev-trial.md` are the old
  models'. No threshold was ever set by calibration (`jev-trial.md` is open), so nothing
  in `grading.pass` rests on them, but a regrade asks the new models afresh (the model is
  in the verdict hash) and can move guardrail rates. Say which models in any comparison
  across the move. A one-cell regrade (2026-11-04, `cc-sonnet/fix-null-deref/1`) went
  through both paths on OpenRouter and agreed with the old verdicts: `ran-tests` yes
  p=0.99 (was 0.98), `stance` complete p=1.00, `change-fits` yes 3/3.
- **minrouter holds about $1 per decision call** (`X-Minrouter-Charged-Usd: 0.9616` for a
  call that settled at $0.000016): decision models are not in its price table, so it
  reserves its top rate until the call settles. Sequential grading fits the $5 wallet; give
  minrouter a row for decision models (minrouter's repository) before grading in parallel.

## A GitHub Pages site to show off the reports

Proctor has no public place where someone can see what a report looks like
without running it. Build a GitHub Pages site for the repository: what
proctor is for (the README's opening), how a suite, task, arm and check fit
together, and a handful of real `report.html` pages from `reports/data/`
linked from it, so the first thing a sceptical reader meets is a finished
report rather than a config file. The bar is the brief's: it reads as done
properly at a glance and holds up to an expert who opens the intervals.
Pick the showcased experiments deliberately (a `code-change` run across
agent arms, an `oracle-change` run, a baseline comparison), and read each
before it goes up: no machine names, no employer, endpoints as written.
The wizard hat is CC BY 3.0, so its credit goes on the site
(`assets/`). Decide whether it is a plain static page beside copied
reports or the first cut of the report site the entries above describe;
the cheap one first is fine if it does not paint the data contract into a
corner. The review is done (`plans/report-structure.md`, *Review, 2026-11-02*); before a
report goes up, scrub the host and gateway names it records there.

## Do not enable Pages until the site's promises are kept

`docs/index.html` is committed as a draft, and Pages stays off until every
claim on it is true of the repository. Today two are not:

- **"Proctor ships with agent-friendly documentation, examples and skills"**
  ("How you use it", step 1). Since 2026-11-08 there is a skill
  (`skills/proctor-suite/`) and a guide (`docs/getting-started.md`); read
  the sentence against them before Pages, and measure the docs with the
  onboarding suite (`plans/onboarding.md`) when it exists.
- **The README no longer links to the site** (`breitreiter.github.io/proctor`):
  the 2026-11-09 rewrite dropped the link rather than carry a dead one.
  Put it back in the opening when Pages is on, and point the README's
  example reports (`docs/reports/`) at their rendered pages there.

(The "Get started" links to `suites/playground` and `suites/cli-for-agents`
were the third; both suites are committed now.)

`proctor setup` prints links to `docs/providers.html#<kind>` on the site
(`plans/setup/README.md`, "The provider catalogue"). The page exists
(2026-11-08; `SetupTests` holds it to an anchor per catalogue kind), but
until Pages is on, every link setup prints is dead, so a public release
of setup waits for Pages. Once released, those anchors are permanent. Add
the weekly lychee check over its vendor links when Pages goes on.

Before switching Pages on, read the page against the repository once more,
line by line: every capability it names should be one a stranger can run
from the README.

## The wrap-up list for 2026-11-01

Cross-repo checklist for tying up the agent-neutral and oracle arc:
`../nb/plans/wrap-up-2026-11-01.md`. B3 of that list is this paragraph.

## Thin the onboarding once binaries ship

When #6 (prebuilt binaries for macOS and Linux, no .NET needed) lands,
revisit the README's Install, `docs/getting-started.md` and the onboarding
skill: no .NET SDK, no sibling nb checkout and build, no `alias` to a dll.
The install step becomes a download, and the guide should get shorter by
about that much.

## Hosted sandboxes for agent arms

Modal shipped as `harbor.engine: modal` (`docs/modal.md`,
`project/plans/modal-runs.md`). Other hosted services wait for a user who
needs one. Harbor's `environment.type` is believed to cover Daytona, E2B and
Runloop too (verify at the pinned version), and Modal's work is the
template for each:

- credentials reaching Harbor's process, with a preflight check;
- nb's publish copied into the task context, since the service cannot see a
  local image;
- whether its egress allowlist holds from inside a sandbox;
- what `Unreachable` refuses;
- sandbox lifetimes, so a killed run stops billing.

Still open for every remote engine, Modal included: the report does not
record where a cell ran.

## Clean up Modal volumes when the spend limit kills a run

Modal's workspace spend limit stops "all workspace workloads" once it is hit,
but "Volume storage charges will still accrue." A run killed that way skips
Harbor's trial teardown, and `HarborRun.TearDown` (`Run/Harbor.cs`) does
nothing for Modal: it trusts the sandbox's idle limit, which covers compute
only. Any Volume a run created would keep billing after the cap.

As of Harbor 0.24.0 nothing creates one. `environments/modal.py` mounts only
volumes the user named (`Volume.from_name`, no `create_if_missing`), and
proctor passes none. So today this is a guard, not a leak:

- If proctor or a Harbor bump starts creating Volumes (the `runs/` on a
  Volume idea under "Proctor as a hosted service", or a per-trial cache), name
  or tag them by run so they can be found afterward. Then make `resume` and
  the kill path delete the ones a dead run left behind.
- Confirm how a spend-limit stop shows up to proctor: a sandbox error, a hung
  `exec`, or something else. The attempt's failure should say "Modal spend
  limit," not something generic.
- Check whether the Apps and built images that `App.lookup(create_if_missing)`
  and `Image.from_dockerfile` leave behind are billed. If they are, they
  belong in the same cleanup.

## Proctor as a hosted service, if anyone asks

Not planned, and of no use to us. This is the answer to "what would it take to
make proctor a product you could sell", written down on 2026-11-12. The
assumption: the user brings their own Modal account and their own model keys,
so we pay for neither inference nor containers, but we still orchestrate.

**What the host does today.** Even on `harbor.engine: modal`, the machine that
starts proctor runs these:

- bare nb arms, with tools, in the work directory;
- hooks, runner scripts and the script checks of bare cells;
- `nb --compile`, whose `@file` includes resolve against the suite;
- `oracle_user.py` and its `nb --judge`;
- the judge calls;
- Harbor itself, one `uv tool run` per cell.

State is `runs/<id>/` on a local disk. `resume`, baselines (pinned cells read
back by path) and `report --against` all need that tree to outlive the
process. Cells run one at a time. On a shared host, the suite's code runs
with our privileges.

**The cheap shape: run proctor on the user's Modal too.** Make the orchestrator
one long-running Modal function per experiment, its image holding the proctor
CLI, nb's publish and Harbor's pin. Harbor should be able to start sandboxes
from inside Modal with the user's token; that is the first thing to prove.
Then the following holds:

- hooks, bare arms, script checks and the oracle run in the user's account,
  not ours;
- we pay for no compute;
- keys can be Modal Secrets in the user's workspace, reaching proctor as the
  environment its `${VAR}` resolution already reads, so we never hold a model
  key;
- the host-nb-equals-image-nb check cannot fail, since we ship both.

What remains is ours to build:

- auth and accounts;
- a secret store for the Modal token. A token is roughly the whole
  workspace, which is the biggest trust question; users will want a
  dedicated workspace.
- getting the suite in: a GitHub app, or a pushed tarball;
- durable storage for `runs/`, which can be a Modal Volume on the user's
  account or object storage we own if we serve the reports. The on-disk
  layout is already the contract, so a sync is enough.
- the job lifecycle: start, cancel, progress, recovery. `resume` with its
  suite-hash refusal is already "the function died, restart it". Sandbox
  lifetimes and `ModalIdle` already make a killed run stop billing. Progress
  is plain log lines today and a UI wants events.
- a report host behind auth, since `report.html` is a static page;
- onboarding: `setup` as a web form, with `setup --check --live` as "check
  my keys".

**What would bite.**

- *Sequential cells.* Three arms by three tasks by five samples takes hours,
  which is the first complaint a service gets. Running cells in parallel
  touches the manifests, `resume` and the judge throttle, all written for
  one cell at a time.
- *Reachability.* BYOK works only for public endpoints. `HarborRun.Unreachable`
  enforces that for agent arms, but judges and bare arms would meet it too.
  A gateway on the LAN, or the user's own vLLM box, needs a public URL or a
  tunnel.
- *Fixtures with `image.services`* cannot run on Modal, so conformance stays
  our CI. We would be promising vendor agent pins we have tested, and keeping
  up with those pins is a standing cost.
- *Image builds.* Harbor builds every task image on Modal from a context we
  upload. Cold builds per experiment cost time, so cache fixture images
  across experiments early.
- *Spend.* "Agree the spend first" has to become a pre-run estimate and a hard
  cap, since a runaway loop (Codex retrying an unreachable endpoint until the
  timeout, an agent at its call budget on every sample) bills the user's keys.

**The quality-of-life work underneath.** Most of a sellable product is here,
not in the control plane. Every place a suite leans on the author's machine
has to become portable. The largest part is tooling to stage a fixture
portably, so that what `repo/`, `image` and its build need can be shipped to
Modal and rebuilt there, not found on the author's disk. After that come
hooks that assume a local engine (`command -v podman`), runner scripts and
mounts, models on the LAN, and `suites/proctor.json` as the machine's file.
Each of these is fine for one person on one box and a support ticket in a
service.

**Then the product pull.** Anyone shaping this as a product would bring Modal
and inference in-house at once: no Modal account, no vendor keys, sign up and
run. That simplifies onboarding a great deal and moves every cost onto us.
The runaway experiment stops being the user's bill and becomes ours, across
every tenant. That service needs:

- metering and per-tenant budgets, enforced mid-experiment;
- a kill switch that stops a cell's model calls, not only its sandbox;
- abuse controls, since we would be selling arbitrary containers with an
  attached LLM;
- pricing that covers a suite whose cost nobody estimated.

Proctor's own records help: `core.json` counts every call, and the sandbox
lifetimes already end on their own. Estimating an experiment's cost before
it runs, though, is a problem proctor has never had to solve.

**Size.** The minimal version takes weeks: a "proctor in a box" Modal app the
user deploys to their own workspace, started from the CLI or a webhook, with
results on a Volume and no accounts or hosted UI. It proves the orchestrator
can run on Modal, and "run it on your Modal, no Docker" is an adoption story
on its own. The full BYOK service would take months, most of it the
portability work above. The in-house version is a different business, one
whose main risk is the cost control described in the previous section.

## Sample suites that demonstrate the site's thesis

A read of `docs/index.html` against what it links to (2026-11-09) found
the page's claims ahead of its examples. The capability has been used in
the field; what is missing is committed suites and reports that show it.

- The showcase contradicts "not a leaderboard". `code-change` compares
  five agent and model arms, and `oracle-change` compares nb bare with nb
  through Harbor. Neither shows one bundle against another on a real
  agent, which is the pitch. Wanted: a suite whose arms differ only in the
  bundle, on a vendor agent, with its report on the site.
- The page says the arms are the population and the thing under test is
  yours, while an arm includes its bundle, so two bundles are two arms.
  The design is agent by bundle. Decide whether the vocabulary names the
  bundle as its own axis or the page's wording changes.
- "Assistants and conversations" addresses a different reader (someone
  building an assistant) from the headline (someone whose product agents
  use), and offers nb one section after the page says only the real
  harness counts. Demote it to "also works for", and describe the cheap
  model as what it is: see the next entry.
- The hero names APIs and websites; every example is a repository
  fixture, a mounted bundle and an offline verifier. Wanted: one example
  graded against a service the fixture runs (`image.services`), in the
  manner of the Stripe study the page cites.
- "Propensity" is claimed in full. A mounted bundle measures whether the
  agent opened and used what was there, not whether it would find it
  unprompted. Say which, and show a check that reads the tool calls for
  it.
- "Nothing else does all three" is a conjunction of our own three
  properties. Soften or cut.

## Pass rate has no headroom on frontier agents

Field use so far (2026-11-09): every fix proctor has driven was a large
stumble (missing data, a broken or mislabelled tool, a known issue absent
from the docs), found by reading failures on a small local model. Once
that model usually completes the task, changes stop being detectable: the
current frontier agents finish anyway, inefficiently, on sketchy tools and
incomplete instructions. So the binary outcome is saturated exactly where
most users are, and more tasks would not help. The paired intervals are
attached to the least informative measure.

What still has headroom on a frontier agent:

- Efficiency: turns, tool calls, tokens, wall time, tool errors met on the
  way. `Stats.cs` summarises duration and tokens per arm (median, p90,
  mean) and compares nothing; only pass rates get a paired difference with
  an interval. A budget check (`max_tool_calls`) turns a continuous
  measure back into a yes or no and discards the power. Wanted: paired
  differences on the counted core, per task, with an interval, as a result
  beside the pass rate. Method to be chosen (a ratio on the log scale is
  the likely shape) and written up in `learnings/prior-art/stats.md`
  first.
- Propensity and workarounds: the run passed, but through the tool under
  test, or around it?
- Harder tasks: friction compounds over a long trajectory, so a
  three-step task sits at the ceiling where a fifty-step one may not.

Two things to decide alongside:

- Which question `n` answers. `n` as the task count answers "does this
  generalise to tasks like these". A regression guard over a fixed suite
  asks "did these tasks change", where samples are the unit and power is
  bought with reruns, not authoring. The report could state which it is
  answering, or offer both.
- How the page describes the cheap model. It is the stress probe: it
  fails where a frontier agent only pays. That is a method worth stating,
  and it is at odds with "anything but the real harness measures
  something your users will never see".

The risk to keep in view: as models get more robust, defect finding
shrinks toward "lint your tools with a weak model". What remains is what
the friction costs, propensity, and the cheaper models users and
subagents actually run, so the pitch should rest there and not on whether
the agent finishes.

## Specimens for human review

A use that has grown up beside the written one (2026-11-09): tasks with
many correct work products, some of which a domain expert prefers. Some
of the preference can go in the prompt, but without seeing a spread of
outputs there is no telling when the prompt over-specifies and narrows
them. Proctor fits because the agent sees the prompt and the bundle and
nothing of the author's own agent configuration, the prompt variants are
bundles (so arms), and the samples, which for a pass rate only reduce
noise, are here the thing being looked at.

What does not fit yet:

- The report is built on checks and rates. A reviewer needs the work
  products: per task, side by side, the arm hidden and the order shuffled,
  as the arm is already hidden from a judge. Wanted: a specimen sheet,
  deterministic, from each cell's diff or final answer. No model writes
  any of it.
- A check is asked of one finished sample. "Would you accept this as it
  is?" has that shape, and a person's answer could fill the `needs-judge`
  slot as a verdict file, after which the existing paired intervals apply.
  "Which of these two?" is a comparison across cells and needs its own
  statistic; leave it until the yes or no proves too coarse.
- Nothing measures how different one task's samples are from each other,
  which is the over-specification signal.

Later: the expert's stated reasons become a rubric, and the human
verdicts the labels a judge check is calibrated against (the bench's job,
as now). Review cost is arms by tasks by samples and reviewers tire, so
these suites stay small on purpose.

## The framing: a usability lab for agent-facing surfaces

A step back (2026-11-09), to be weighed before the site or the README is
rewritten. The parts held with confidence are the ones that run, observe
and present: Harbor, the static checks, the decider and judge checks, nb
as a harness that can be reshaped per question, and the report's visual
language. The part the three entries above put in doubt is inference: the
paired pass-rate verdict as the headline.

The problem underneath is social, and the brief already states it. Tool,
doc, CLI and prompt decisions are made on the author's impression and "it
worked for me once", so a critique of the surface becomes a critique of
its author, and there is no equivalent of "show both proposals to users
and see". User research solved that for interfaces, with small numbers
and observed behaviour, not with significance. The site drifted from this
toward "measure agent experience, with confidence intervals", a different
and weaker promise.

Read that way, the parts fit:

- Harbor and nb supply the participants. A study recruits novices as
  well as experts, because novices expose what experts work around. The
  small local model is the novice, the vendor agents on frontier models
  the experts, and nb the participant that can be configured.
- Checks are the observation protocol: what happened, with quotes, and
  nobody's opinion.
- The report is the neutral third party. Its authority is observed
  behaviour and visible accounting, and that is what takes the author's
  competence out of the argument.

What it would change:

- The central act is two proposals, the same agents, the same tasks. The
  vocabulary serves it worst, the bundle being inside the arm.
- The evidence has an order: observed stumbles decide most often, then
  what it cost to get there, then expert preference among specimens. The
  pass rate is a gate, not the headline.
- The verdict card leads with the least informative result; in most real
  use it reads "Unchanged (unconfirmed)". The visual language stays; the
  hierarchy is what to revisit.
- The statistics keep one job, stopping over-reading. "Could not tell at
  this size" is a useful end to a design dispute: either proposal will do.

Open: whether the page still reads as rigorous at a glance once the
intervals are no longer the lead. The accounting table, the per-run
matrix and the quoted evidence may carry it; that is a judgement about
the audience.

## An analyst over the transcripts, not a moderator in the run

Follows from the usability-lab framing (2026-11-09). A moderator watches
for a participant who pauses, wanders through menus, tries something
wrong and recovers, or opens help, and asks why. In a trajectory those
are: long reasoning with no action or the same call repeated; `read` and
`search` calls the task never needed, and a long stretch before the first
edit; a tool result that errored followed by the same tool with other
arguments, or an edit later undone; `--help`, a docs read, a `skill` or
`web` call. Proctor holds the material (a kind per call, `IsError` per
result) and uses it only as tallies and maxima, which lose the order.
Order matters: help opened first is propensity, help opened after a
failure is a stumble.

Not a moderator: a question put into the run reads to a model as a
correction and changes the rest of it, it works only on nb, and the
answer is a self-report. The text an agent writes before a call is the
think-aloud, to be quoted beside the event (vendor agents may summarise
or hide their reasoning; check at the pins). If an interview is ever
wanted, fork it: replay the transcript to the moment in a separate nb
conversation, so the measured run never sees the question. And "why" has
a better answer here than in a lab, since the participant can be rerun:
the analyst says where runs stumble, the fix and the rerun say whether
the guess was right.

Observations, as a second thing beside checks:

- Proctor's, not the suite's: every run of every suite, nothing to author.
  A check is a question someone knew to ask.
- Events, not verdicts: each has a step and a quote, and none touches the
  pass rate.
- Deterministic first (error then retry, repeated calls, help after a
  failure, calls before the first edit), tuned to catch too much. A judge
  labelling what is left, from a fixed vocabulary with verified quotes,
  comes later.
- Aggregated across runs by a plain key (the tool and its normalised
  error text, the file, the command). "The same error from the same tool
  in 7 of 9 runs" is the finding, and needs no judgement.
- Relevance from the other runs: the cheapest successful run on a task is
  the reference nobody had to write. What most runs read was needed.
- Reported as a short list of moments worth reading, each linked to its
  step. The analyst ranks; a person concludes.
- Ten samples of one model are one participant on ten days. A stumble
  that recurs across arms belongs to the surface; one confined to an arm
  is that model's habit.

How to know it is any good, at no cost: replay the cells behind fixes
proctor has already driven and see whether it surfaces what reading
found; and planted defects (`jotcli` has one), with a clean control that
should stay quiet. Start with the replay, if those cells still exist.

## An event table per cell, indexed by the surface under test

The problem behind the analyst (2026-11-09). When a run was three tool
calls the transcript was the analysis. A recent task of the present kind
(from a documentation site, instrument an arbitrary codebase for
telemetry export and verify it) runs to hundreds of calls across several
languages. No expected trajectory can be written for it, and outcome
checks reduce megabytes of ATIF to one bit and two scalars. That answers
"does it work and what does it cost", not "what should the author
change".

Trajectories cannot be prescribed but can be described against what does
not vary: the surface under test, its pages, commands and error messages,
which every run touches whatever the repository. So the suite declares
the surface (hosts, paths, commands), one small declaration in place of
the expected trajectory.

What that would measure, all of it already in the trajectories:

1. Page-level use: which pages were fetched, in what order, repeatedly,
   never; what the agent searched for, against the docs' own vocabulary.
2. Cost by phase: orient, read docs, edit, verify, repair. The boundaries
   are the uncertain part; try by script on real transcripts first.
3. Right the first time: whether the first verification passed. A yes or
   no with headroom on frontier agents, so the existing paired statistics
   apply. Needs the suite to name the verification command.
4. Docs or memory: an edit before any fetch came from training data, and
   a run can pass on a deprecated pattern without exercising the docs.
5. Spread of solutions: dependencies added and files touched, per run.
6. Cost by fixture stratum, from the fixture's labels.

The shape, so that nothing reads the megabytes:

- One event table per cell: a row per tool call with its step, kind,
  target (URL, path or command), whether it errored, and the tokens and
  time since the previous row. A derived file beside `core.json`, from the
  same walk in `Ingest` that makes the counts.
- Aggregates over the tables, per page, command, label and arm, which the
  report renders with each row linking to its steps.

Before any of it: a throwaway script over real cells. The trajectories
under `runs/` here are small tasks with no documentation site, enough to
try the table, measure 3 and measure 4; page-level use needs transcripts
from a task of the kind above.

## A middle layer for an agent that investigates

Three readers, three layers (2026-11-09): the raw capture, which stays
the ground truth; the report, which in practice is a person's quick look
at whether the treatment moved the needle; and between them something for
the user's own coding agent, told "this looks off in the report, go and
find out why". Proctor already writes for an agent elsewhere
(`setup --check`, `skills/proctor-suite`).

Two observations behind it:

- A reader needs to be stronger than the participant. Given a frontier
  model's trajectory, the same model rarely finds anything, and when
  pressed suggests nonsense; given a small model's, it finds the errors
  and missteps and usually the correction. The stronger reader carries
  the expected trajectory nobody could write. Unchecked: whether it holds
  with the model's name hidden, as a judge window hides it.
- The strongest model a user has is the one in their working session,
  which is ahead of anything they would run as an arm day to day. So the
  review need not be a proctor feature or a paid judge: it is the session
  the user is already in, and it may reach the frontier arms' traces too.
  Untested.

The reader's question has to be put right. "What did this agent do
wrong" lists the participant's faults. The useful question is what in
the surface invited each misstep: the ambiguous page, the misleading tool
description, the missing fact.

It also moves the cost from doing to reading: a free local run leaves a
trajectory dense with stumbles, the session's model reads it and blames
parts of the surface, one thing is fixed, the local run repeats. A pass
per change, with the paid agents run only to confirm a bundle. Limits:
some stumbles are the small model's habit (does the frontier agent spend
extra at the same place?), and a fluent diagnosis is a guess until the
rerun.

An agent's limit is context, so the layer must answer most questions in a
few thousand tokens and address into the raw by step. Smallest first:

1. `events.jsonl` per cell (the event table above), one truncated line
   per tool call with its step id, and one concatenation per experiment
   so a question across runs is one search.
2. One verb that prints a range of a cell's steps, readable and
   truncated. No query language; files and grep.
3. The way from the report to the cells: `summary.md` and `stats.json`
   carry the cell paths behind each number.
4. An investigation skill beside `skills/proctor-suite`: start from the
   failures in `stats.json`, go to the events, open steps, cite cell and
   step for every claim, check that a stumble recurs across runs, blame
   the surface.

This makes the agent, not the report, the event table's first reader, so
the first version needs no rendering and no statistics. The aggregates a
user keeps asking their agent for are the ones to promote into the report
later. The agent's prose stays in the session; what lasts from an
investigation is a fix to the surface or a new check. The report's part
is to make "looks off" noticeable (a run whose cost is far from its
siblings), once the event files exist.

## What checks are for, once trajectories are long

2026-11-09. From reading `runs/20261102-1143-code-change-jy0n` cold in a
session (7 of 27 cells closely, counts across all; arm ids not hidden).

- **`pass` collapses toward the final state, and that is right.** On a long
  task with no expected trajectory the outcome is the only thing that can be
  stated in advance. In that experiment the checks that read the work
  directory (`acceptance`, `builds`, `tests-pass`, `diff-in-scope`) and the
  counted core (`exit_ok`, `no_denials`, `under_budget`) decided every cell.
  The two that did not were model checks: `ran-tests` (decider over the
  trajectory) and `change-fits` (judge over the diff).
- **Three things stay outside the final state.**
  - Validity: whether the agent edited the acceptance tests, reached the
    answer, never got a model response, or never reached the surface under
    test is not visible in the end state.
  - Propensity: whether the agent opened the page or called the tool is a
    lookup in the event table, not a model's opinion.
  - Claim against state: `stance` compares what the agent said with what it
    did. The one model check worth carrying on every suite.
- **A process question with a yes-or-no answer is a query over events, not a
  decider.** `ran-tests` asked jev what a regex over the untruncated command
  answers exactly, and was undecided on a cell where 14 tests ran. A process
  question without a yes-or-no answer ("why did it wander here?") is an
  observation, not a check.
- **The judge stays for final-state questions no script decides.** Not because
  it reads better than a session model: because it is blind, cached,
  repeatable over every cell and every regrade, and its verdict can go in the
  report. Nothing said in a session has those properties.
- **Order to try for a new question:** event query, then decider, then judge.
- **Wanted: try a proposed check over old cells without writing
  `checks.json`.** A check spec and a set of cells in, verdicts out.
  `grade --judge a=b` swaps the judge, not the check. This closes the loop
  from a session's finding to a check proven on evidence already paid for.
  The session does not need a "run this by glm" tool for reading; a fresh
  subagent with arm ids hidden is the better guard against anchoring.

To investigate, from the same read (not yet reproduced, so not issues):

- `ran-tests`: the `tool_calls` window cuts each call's arguments, so a
  `dotnet test` at the end of a long heredoc is invisible
  (`cc-sonnet/add-retry-flag/1`, no at p=0.87).
- `change-fits` errors on every `rename-module` cell in all five arms: the
  judge quotes `Legacy_Widgets` where the diff says `Legacy.Widgets`, in all
  three samples, so every quote is rejected.
- The nb image sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`
  (`Harbor/Compile.cs`, `AgentInstall`); every `dotnet test` on the `floor`
  arm prints 51 to 62 copies of `NETSDK1188`, 21 to 25 KB of the 33 to 38 KB
  a cell reads back, truncated. No vendor arm has one. The `floor` arm's
  tokens and wall time are not comparable with the others until this is gone.
- The fixture images have no `python3`; Sonnet's first edit in
  `add-retry-flag` was a 4 KB Python script that failed, one turn of five.
- The cell's nb config carries MCP servers that cannot start in the sandbox
  (`floor/add-retry-flag/1` stderr). That cell ended `provider_error` at zero
  turns; cause not established.
- The three `qwen` samples of `fix-null-deref` and of `rename-module` have
  identical call counts and observation sizes. If the transcripts are
  identical, three samples on that arm are one.
- nb's system prompt ("Use the file tools to read and edit; use bash for
  `dotnet build` and `dotnet test`") has the local model walk the tree with
  five `list_dir` calls where every vendor agent uses one listing command;
  `todo_write` is 8 of 29 calls in `floor/add-retry-flag/3`.

## Docs or memory

2026-11-09. From field use: on the task "from a documentation site,
instrument an arbitrary codebase for telemetry export and verify it", one arm
ran with the in-container docs host broken. Sonnet completed the task from
memory with full marks on every outcome check. Nothing flagged the arm.

- **The author's premise was a vibe too.** The assumption was that full
  instrumentation was impossible without the docs naming each landmine and
  forcing each incremental step and test; the author could not have done it
  without them. That an expert human needs the docs is not evidence that the
  agent does. This is the same error the project exists to counter, made by
  the person running the experiment, and found only by accident.
- **The arm should not have counted.** Two `validity` checks were missing: a
  setup-hook probe that the declared surface answers from inside the
  container, and an event-table query that the agent fetched at least one
  page of it. A run that never touched the docs is not a sample of the docs.
- **A no-surface arm is the control, and it comes first.** If the arm without
  the docs scores the same as the arm with them, no revision of the docs can
  move the outcome on that task and model, and treatments are wasted spend.
  This is the saturation problem ("Pass rate has no headroom on frontier
  agents") seen from the other side. It is one more arm, not a new mechanism:
  an arm whose bundle is empty.
- **Outcome checks cannot tell memory from docs, and the two agree only until
  the product changes.** When the docs say the new way and memory says the
  old, an agent that skips the docs ships the old way, and the checks pass
  for as long as the old way still works.
- **The fix is the one `oracle-change` already uses.** Its semantics are in
  the answer sheet only and unconventional, so a guess fails acceptance. For
  docs: plant a detail that exists nowhere but the surface under test and
  that a check verifies in the final state (an endpoint path, a required
  header, a renamed config key). Then "docs or memory" is an outcome, not an
  inference from fetches.
- **Propensity changes meaning for a product already in the training data.**
  The question is not whether the agent can do the task but whether it
  consults the surface at all, and whether consulting changed what it did.
  One broken arm suggests the first answer may often be no. Untested beyond
  that arm.
- **What to write down for suite authors** (`skills/proctor-suite`,
  `docs/configuration.md`): run the no-surface arm before any treatment;
  declare the surface so reachability and use are checked; give each task one
  detail memory cannot supply.

## An agent arm's bundle reaches a service, never the agent's container

2026-11-10. Found while scoping `agent-onboarding`. 2026-11-12: #11 landed
the host half; the same day, compile began copying the bundle beside the
fixture's services (`environment/bundle/`), which `tern-docs` serves as a
docs site.

- **What is true today.** An agent arm's bundle is hashed, recorded per
  cell, handed to arm hooks as `PROCTOR_BUNDLE`, guards `resume`, and is
  copied beside the fixture's compose services when there are any. The
  agent's own container never holds it; the verifier's `PROCTOR_BUNDLE` is
  empty; an `agent: nb` arm with a bundle is refused `{{bundle}}`.
- **What is still missing.** A bundle the agent should hold on disk: a
  skill or an MCP server installed where each agent looks, or a docs tree
  to grep. That is per agent, and it changes the condition measured (a
  site read over HTTP against a tree it can search), so it waits for a
  suite that needs it.
- **An arm hook that serves the bundle on the host is not reachable from a
  vendor agent's container**: the allowlist names the model's host only.
  A suite that needs a host-served corpus (as #11's describes) should serve
  it as a fixture service instead, or proctor needs a way for a suite to
  add a host to the allowlist.

## Beats

2026-11-10. The unit a run is cut into for reading is a **beat**, as in a
story: consecutive calls of one kind with one outcome ("explored", "edited
to fix the compile error", "verified, failed"). Not a tool call, which is
too fine and differs by agent (Codex splits one edit into many calls;
Sonnet puts six things in one shell command), and not a phase, which is too
coarse. `project/lab/observe.py` cuts beats by rule; a model's part, if it
keeps one, is the agent's own sentence attached to a beat. The word is not
yet in `CLAUDE.md`'s vocabulary table; it goes there when beats reach the
report.

## Beats, second cut: orient, iterations, wrap

2026-11-11. Supersedes the definition under "Beats" above; the word stays.

- **What failed.** Beats as "consecutive calls of one kind with one outcome"
  gave more beats than calls on Claude Code with Sonnet (17 calls, 28 beats:
  one shell call holds several actions) and routes that group nothing (15
  runs, 14 or 15 routes). Evidence: `agent-onboarding` on Sonnet
  (`20261111-0120-…-dd5u`, 15 runs, 11 passed, $3.90) and on Qwen Code
  (`20261111-0309-…-1u5l`, 9 runs, 0 passed, $0).
- **What was tried** (`project/lab/trials/strategies.py`, throwaway): one
  mode per call; write-try folded into a loop (too coarse); a fixed frame
  alone (says nothing); the agent's own narration as the cut (Sonnet barely
  narrates, Qwen narrates every call); by artifact written (one Sonnet call
  writes many).
- **What coheres** (`trials/s6.py`). A run is **orient** (everything before
  the first write), then one beat per **iteration** (what was written, then
  what was tried, up to the next write), then **wrap**. Cut on events, not
  calls, so a call that writes and runs is cut inside. Sonnet's 17 calls
  and Qwen's 32 both come to 4 to 6 beats; an 82-call run and a 23-call run
  both come to about 12. A write that is never tried is its own beat.
- **Three intents fall out by rule**: advance (writes an artifact for the
  first time), rework (only artifacts already written), repair (follows a
  failed try). Not built.
- **Still weak.** What a try said: a compound call shares one result, so a
  failure can show under two iterations, and the last line of a result is a
  poor summary. Pass or fail per iteration is unreliable (an agent's own
  shakedown fails on purpose in one mode). The cut was checked against
  call-by-call strips, not yet against full transcripts by eye.
- **Routes are dropped.** Runs are compared by counts within the frame:
  orient size, docs reads, iterations, writes per iteration, writes never
  tried, returns to the docs (`trials/two.py`).
- **Embeddings are not the instrument for grouping beats.** They would group
  by agent syntax and file names, are unstable at 90 points, and a cluster
  cannot be defended or reproduced. Kept for discovery only: grouping error
  text, finding the odd beat, suggesting what to count.

## Three things a reader does with trajectories

2026-11-11, from the user. A narrative timeline serves these better than a
coloured strip; each may need its own view.

1. **How is this agent solving this task**, so friction and errors can be
   reduced. Draft: `trials/timeline.py`, a templated timeline per run (beat,
   what was written and tried, the tool's and the agent's own words quoted)
   and a per-arm table of what the runs ran into and where in the run.
   Missing: one typical run per task with "n of N runs did this too" against
   each beat; a better rule for what a try said; friction filtered from
   noise.
2. **Did a treatment improve outcomes.** There is a manual step in the
   middle: the person codes failures from view 1, then says which ones the
   treatment is meant to correct. The treatment's report leads with those
   named counts.
3. **Do different agents need different affordances.** View 1 per agent,
   then view 2 per agent. No alignment of trajectories across agents.

Open, for the lab's canvas (a React frontend is being specified in a
parallel session; a free canvas and a SQLite database tracking revisions,
problems, tests and changes):

- When a problem is detected, how it is marked, and on what (a run, a beat,
  a step).
- How a marked problem becomes a test that checks for it in future runs
  (`observe.py count` and `grade --try` in `lab.md` step 4 are the seeds).
- How a new treatment is recorded as introduced, and against which revision
  of the surface.
- How a claim about a treatment's impact is made, and what it rests on.
- How two runs are compared side by side.
- How variance within an arm is characterised (every run shown, task by
  arm, never an average, is the position so far).

## What `agent-onboarding` found on its first two experiments

2026-11-11. Sonnet `dd5u` and Qwen Code `1u5l`, as above.

- **Every Sonnet run met the same dead end**: `proctor setup --check` says to
  ask the user to run `proctor setup` at a terminal (15 of 15, while
  orienting). All carried on.
- **Six of nine Qwen Code runs never loaded the skill** and built something
  that is not proctor (shell scripts, `.qwen/workflows/*.js`). Only
  `revised-prompt` triggered it, 3 of 3. Why that prompt and not the other
  two is unexamined.
- **The docs fall short**: 6 Sonnet runs read
  `project/plans/on-disk-layout.md`, 3 read `Grade/Checks.cs`.
- **The image lacks what agents reach for**: `python3` (4 runs), a model at
  `localhost:8080` (4 runs).
- **A grader fault, unfixed**: `trial/stays-on-topic/plants.json` sorts the
  agent's tasks by keyword and filed a bike-framed tax question as on-topic
  (`cc-sonnet/stays-on-topic/5`). The other three Sonnet failures are the
  agent's.
- **Qwen Code's skill tool is `skill`, lowercase**; an `observe.json` that
  names only `Skill` misses it.

## The cut read blind: 73% of beats called right

2026-11-11. The cut is in `project/lab/observe.py` (routes and fixes are
gone; it writes `observe-timelines.md` beside `observe.md`). A blinded
reader (one Sonnet subagent, `observe.py blind` renderings, fixed labels)
read ten runs across Claude Code on Sonnet and Haiku, Qwen Code, Codex and
nb, from `agent-onboarding` and `code-change`: 78 beats.

- **57 of 78 cuts right (73%)**; 17 it would join to the beat before, 4 it
  would split. Below the 80% set in `plans/debugging-suite.md`.
- **Joins are fix chains.** One problem worked over several write-then-try
  rounds reads to a person as one piece of work, and the cut makes each
  round a beat. No join rule tried matches the reader: joining every repair
  and rework agrees on 67%, every repair on 62%. The reader's own repair
  label predicts its joins no better (63%).
- **Splits are long unbroken writes** (19 and 32 steps with no try). No rule
  cuts them; that the run wrote that long without trying anything is the
  finding.
- **Two joins were scratch files**: a throwaway under `/tmp` opened the
  first iteration. Fixed after the read (a write wholly under a scratch
  directory is a try, not a write); not re-read.
- **Intent labels kept on 62 of 78 (79%).** Repair is the weak one: it
  fires after any error-looking line and misses repairs whose failure the
  rules did not read. 10 beats the rule called advance or rework the reader
  called repair; 3 the other way.
- **One reader, one model.** How far two readers agree with each other on
  "one piece of work" is not known, so 73% has no ceiling to compare with.
- **Read as:** the iteration is a sound atom and a poor sentence. A second
  level, the episode (an advance and the rounds that follow it on the same
  problem), is what a person names; it has no rule yet. The timeline can
  nest rounds under their advance without claiming where an episode ends.

## `nb-changes`: built, no model run yet (2026-11-11)

`suites/nb-changes` is the debugging suite of `project/plans/debugging-suite.md`:
five defect tasks on nb at `0e11b21`, one fixture each, and the feature task
`ask-user-question`. Loose ends:

- **nb has a timing-dependent test.**
  `RetryingChatClientTests.MinRequestInterval_PacesEveryCall...` failed once
  in 18 full runs of nb's tests, on a loaded machine. Seen once, not
  reproduced. `suite-green` repeats a failing run for this reason. Worth
  reproducing in nb and filing there.
- **`not-a-bug` passes on an empty diff**, which is what giving up looks like
  too. `named-cause` separates them but is a guardrail. A per-task `pass`
  check is not expressible: `grading.pass` is the suite's list.
- **The plants are readable in this repository** (`fixtures/nb-*/plant.patch`),
  so after one public run a later model could have seen them.
- **`ask_user_question` exists only as a patch** in
  `suites/nb-changes/reference/`. Whether it goes into nb is undecided.
- **Arms.** The suite lists only `qwen`. The Haiku shakedown and the Sonnet arm
  wait on agreed spend.
- **nb's golden tool-surface tests fail when `SHELL` is unset** (confirmed
  2026-11-11 in the fixture image). `ShellEnvironment` falls back to
  `/bin/sh`, and `ToolSurfaceGoldenTests.Scrub` replaces the shell's name
  wherever it occurs, so "finish" becomes "fini<SHELL>" and three goldens
  miss. The `nb-*` fixtures set `SHELL=/bin/bash` in the image. An nb issue,
  not yet filed.
- **Proven in the images** (2026-11-11): Harbor's oracle agent with each
  reference as the solution scores every script check 1 on all six tasks,
  in podman, verifier offline. The pass is a scratch script, not a test.

## `nb-changes` on Haiku: the defects are too easy to press on the cut (2026-11-12)

Experiment `20261112-0020-nb-changes-h7at`, Claude Code on Haiku, one run a
task: 5 of 6 passed, $0.86 by `core.json` ($0.81 of it the feature task),
47 minutes.

| Task | Calls | Strip | Shape wanted | Shape got |
|---|---|---|---|---|
| `red-test` | 5 | `o3 A` | a try before any write | yes: `dotnet test` in orient, then one write |
| `report-only` | 11 | `o4 A! R` | a scratch reproduction first | a regression test first, then the fix; failed acceptance |
| `two-causes` | 13 | `o5 A! R! R` | the first fix does not clear it | yes, in three short iterations |
| `far-cause` | 17 | `o12 A! R .` | long diagnosis, logging added and removed | found by reading in 12 calls; no logging |
| `not-a-bug` | 5 | `o5` | no write | yes: one beat |
| `ask-user-question` | 93 | `o30 A! R! R! R!` | (feature) | a 23-write advance, then three repairs |

Against the failure list in `project/plans/debugging-suite.md`:

- **Test-first reads as advance then repair.** In `report-only`,
  `far-cause` and `two-causes` the agent wrote a failing test, saw it fail,
  then wrote the fix. The cut calls the test "advance" and the fix "repair",
  because the beat before met an error. The error was the point. This is the
  listed `report-only` failure, on three tasks.
- **No orient beat reached 30 calls on a defect**, so "reproduce inside
  orient" was not pressed. The feature's orient is 30 calls and one beat.
- **`not-a-bug` is one beat**; the timeline says "20 reads, 9 of the docs"
  and quotes the agent, and nothing else.
- **The feature's first iteration is 23 writes in one beat**: the "split"
  fault the blind read found.
- "Other" is 0%.

What to change before a Sonnet arm:

- **`far-cause` is not far.** Reading from the trailer back to the side call
  took 12 calls. A longer chain, or a cause that reading does not show, is
  needed for an instrumented diagnosis.
- **`report-only`'s acceptance asks for more than the report.** The plant
  also stops a blank line ending a continued prompt; the report mentions
  only headings; the agent fixed headings and failed
  `BlankLineInsideAContinuedPrompt_EndsIt`. Either the report mentions it or
  the test goes.
- **`observe.py` counted coloured `PASS` lines and "0 failed" as errors**
  (ANSI codes hid them from the quiet rule). Fixed; nb's own compiler
  warnings still count as an error met.

Done 2026-11-12: `far-cause` has a new plant (the journal's turn numbering,
seen only on seed replay of a nudged run), and `report-only` lost the
blank-line test. Both shaken down three ways. Neither has met a model.

## The feature task on Sonnet: four passes, about 46 calls each (2026-11-12)

Experiment `20261112-0154-nb-changes-8707` (run from a root outside the
repository), Claude Code on Sonnet, `ask-user-question` four times: 4 of 4
passed, $3.15 by `core.json`, 45 to 48 calls, 7 to 16 minutes. Haiku took 93
calls for the same pass. Strips: `o11 A! R A A! R! R! R! .`,
`o13 A A! R! r .`, `o15 A A! R! R! R!`, `o12 A! R A A! R! R! R! .`. Not yet
read against the cut.

## A cookbook: proctor output for human expert review (after the rewrite)

Queued 2026-11-15, to do once the move to exam, variant and attempt has
landed (`project/plans/exam-variant-attempt.md`), so the recipe is
written against the commands that will exist.

Write a cookbook page in `docs/` for the rare, high-touch case where
proctor makes the material and a professional judges it: "you work in
this field, which of these versions makes the most sense to you?"
Proctor's part is generating, not grading. A person cleans up the
output, picks the few comparisons that matter, and stages them into a
session, usually run on a call.

What the page should walk through, on one worked example:

- an exam whose checks are only the sanity ones (there is output, it did
  not time out), with the variants being the versions to compare;
- running it and finding each attempt's output whole, with the variant
  that made it;
- picking and pairing outputs by hand, and what to strip so the reviewer
  cannot tell which variant wrote which;
- recording what the reviewer said beside the attempts, as a result that
  is not a check.

See "Specimens for human review" above for the earlier thinking. If
writing the page shows the last step has nowhere to live, that is a
finding for the model, not something to work around in the recipe.

## Confirm a regrade costs nothing for unchanged checks

Queued 2026-11-15, replacing a maybe ("regrade only new or altered
checks"). Settled the same day: the checks are our best understanding of
what success means, one standard, so a changed check regrades every
current attempt and `grade` keeps doing all of them. "What we check now,
given current conditions" would mean a leaky fixture (a vendor API,
firmware, input data), and that is a fixture change, which makes
attempts stale by the task hash. A change of taste is slow enough to be
a new exam.

What is left is cost. A judged check's verdict is kept beside the
attempt, keyed on the exact request (`Grade/Judge.cs`, `Cached`), so an
untouched check on an untouched attempt should come back without a call
or a flipped verdict. That is read from the code, not measured: after
the rewrite, grade a judged exam twice across two batches and confirm
the second pass makes no judge calls.

## Does a pinned vendor agent change behaviour at the same version?

Unconfirmed. The rubric in `docs/configuration.md` ("When an exam gets a
copy") rests on an agent at a pinned `agent_version` behaving the same
from week to week. If Claude Code (or Codex, or Qwen Code) fetches remote
feature flags or config at startup, it may not. Proctor turns off only
Harbor's telemetry (`HARBOR_TELEMETRY=off`, `Run/Harbor.cs`) and sets
nothing for the agents. To investigate: what each agent at its pin fetches
on start (a conformance attempt with the egress sidecar logging, at $0),
and whether a setting such as `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`
in the agent's environment stops it without changing what is under test.
An issue only once it is seen.

## Grade the proctor-on-proctor exams with the proctor they were shown

Wait until the exam-variant-attempt refactor settles; nothing is pinned
before then. `exam-authoring`'s variants pin proctor's docs at a revision
(the bundle, `b3250b6` today), but `checks/validates.sh` and
`checks/trial.sh` grade with the host's build
(`$PROCTOR_EXAM_DIR/../../bin/Debug/net10.0/proctor.dll`), whatever the
checkout last built. So every commit that changes what `list` accepts, or
how a trial runs, changes the verdicts on attempts already made, with no
hash moving: by the rubric in `docs/configuration.md` ("When an exam gets
a copy"), the world changes on every commit. The fix: build proctor at the
variant's bundle rev (the variant-setup hook already builds proctor) and
hand that build's path to the checks, so a proctor change reaches the exam
only as a new variant at a new rev, with a claim. Check `agent-onboarding`
for the same shape when doing it; its scripts do not name the host build,
but may reach it another way. Then decide the cadence: a new bundle rev
when a docs, skill or CLI change is meant to help authors; agent pins at
most quarterly.

## A freeze for an exam and setup worth protecting

From the user, 2026-11-16. Changing an exam's job or a variant empties its bank
(`plans/bank.md`, B2), which is right and unavoidable. Early on that happens
constantly and costs nothing. Later a careless edit can turn hundreds of
dollars of transcripts into nothing. Wanted: a ceremony for "this is good
enough, do not change it", after which `run` and `list` refuse or warn
loudly when the hash of a frozen exam or variant would change, and say how
many reps would be lost. Not designed.

## Back-fill a check that needs the workspace

`plans/bank.md`, "Back-fill". A new or changed script check cannot be graded
over banked reps from an agent variant; it shows as outdated. Agreed as a
fast follow after the bank layout: rebuild the workspace from the fixture's
image and the kept diff, run the script, record that the image is a later
build.

## Retry a stage that did not run, in place (2026-11-23)

A stage that did not run (a provider error, a rate limit) loses the whole
try, every finished stage with it (`plans/bank.md`, "How a stage ends").
For six independent coding stages that is an hour of work thrown away for
one bad request. Once the bare runner snapshots the workspace after each
stage (per-stage workspace checks, decided 2026-11-23), it can put the
workspace back to the last snapshot and launch the stage again, a few
times, before giving the try up. The user: fine to do later, not to
forget.

- Harbor cannot: it owns the steps of a trial. A lost try through Harbor
  stays lost, so the two runners would differ in cost, not in grading.
- Say in the stage's manifest that it was retried and how often; a retry
  is a fact about the run.
- Decide what counts as worth retrying: `rate_limited` and
  `provider_error`, not a crash of nb or a failed hook.

## Something above an exam that `proctor run` can target (2026-11-23)

Stages replace tasks, so an exam is one job in one environment, and a
set of related jobs is a set of exams: `code-change` becomes three, the
scripted controls a handful. The user: "we're generating a lot of exams,
do we need something above them that proctor run targets? or some sort
of metadata/tagging system so you can say 'run all the oracle exams'".

- Exams already carry `labels`, and `proctor list --label` filters by
  them. The smallest step is `run`, `grade` and `report` taking the same
  filter.
- A named group of exams is the other shape. The word for it is on the
  old-words gate with its former meaning (`plans/bank.md`, B7), so it
  needs the gate changed or another name.
- Exams split from one family copy their variants, bundles and hooks.
  Whatever sits above an exam is also where shared variants would live,
  and where a variant would be compared across the family's exams.
- Not decided: whether a report over several exams is one report or a
  page that lists each exam's.

## The container-engine guides: what is left (2026-11-27)

Written 2026-11-27 (`plans/container-network-finish.md`, stage 4):
`docs/network.md` (the page each engine guide points at),
`docs/service-fixtures.md`, `docs/docker.md`; `docs/podman.md` and
`docs/modal.md` brought up to Harbor 0.26.0; setup's sandbox plan, the
`proctor-exam` skill and the README point at them. Left:

- **Nothing was run on macOS**, Docker Desktop or rootful Docker; the
  Docker guide says so, section by section. There is no Mac. The
  relay's socket mount through an engine's VM is the open question.
- **One walk through the guides on a clean machine**, by an agent from
  the pages alone, has not been made.
- **`docs/podman.md` section 8's wrapper is podman's**; not adapted for
  Docker.
- Each guide's date and versions move when the pin does (0.27.0 from
  2026-11-28).

## One host and one port for an agent's model, on any machine

Asked for by the user 2026-11-26. **Built for the machine that runs
proctor** (2026-11-27): a loopback base URL is relayed, one port, and
the agent's phase is `no-network` (`docs/network.md`). **Not built for
a host elsewhere**: Harbor's allowlist is by host, never by port, so an
agent allowed a gateway on another machine reaches every web service
on it. That is Harbor's to give (`harbor-issue-drafts.md`, draft 1);
the relay could carry a remote host's port too, from the machine that
runs proctor, and nobody has asked.

## A fixture's services are in no hash

Found 2026-11-27 writing `docs/service-fixtures.md`. A task's hash
covers the fixture's `image` block and the content of `repo/`
(`Exam.JobHash`), not the files under `image.services`. Edit
`fixtures/stub/services/stub.py` or `tern-shop`'s API and every attempt
stays current, though the agent met another service. The guide says to
copy the fixture and exam; to decide: whether the services directory
belongs in the task's hash (it is what the agent meets), which would
make the banks of `conformance` and `tern-docs` stale once.

## Keep the model's endpoint and key out of the agent's reach

Pinned by the user 2026-11-26. An agent under test holds its model's
base URL and key (`ANTHROPIC_API_KEY` and the like in its container),
so it can call the gateway itself, outside its harness: another model,
another upstream the key reaches (a web search one, on the user's
gateway), or its own model with no record in the trajectory. Scoping a
key per upstream at the gateway closes part of it and does not scale:
"we're going to need tokens for every model variant we want to test".
Two directions, neither designed:

- **Hide them.** The agent's container holds a placeholder key and a
  local address; something outside the agent's reach (the relay of
  `plans/container-network.md`, leg B, or a sidecar) adds the real key
  and forwards to the one route. Then the key the agent can read is
  worth nothing and there is one route to call. To find out: whether
  each vendor agent is content with a placeholder key, and whether the
  sidecar's process and environment can be kept from a root agent in
  the same namespace (a fixture's service is in the agent's network
  namespace, not its filesystem or process list; not checked).
- **Flag it.** The user: "flag the run if the model tries to reach
  minrouter directly (not through the approved harness). not sure how
  fast we can catch that". After the fact it is cheap: a rule over the
  trajectory for a tool call that names the gateway's host, the base
  URL or the key's variable (a friction rule, or a validity check so
  the try does not count); and the gateway's own request count for the
  run held against the turns the trajectory records, which catches a
  call the trajectory hides. During the run it needs the gateway to
  tell the harness's requests from others, which nothing does today.

Beside "One host and one port for an agent's model, on any machine"
above; C3 of the plan is the same question as advice.

## An agent told to get out: adversarial runs against the isolation

Pinned by the user 2026-11-26, for later: after the core of proctor
works, and probably after the viewer does. Everything measured so far
(`plans/container-network.md`, `learnings/harbor.md`) is what a probe
script can reach, and `container-network.md` leaves out "an agent that
means to get out" on purpose. This is that: an exam whose task is to
break the isolation, so the boundary is tried by something that looks
for a way and not by a list we thought of.

- **What it tries**, each a check with a planted thing to find: the
  open internet under an allowlist; another service on the machine; a
  fixture service's source or configuration (goal 2 of
  `plans/container-network-finish.md`); the verifier's files and the
  expected values; the model's key used outside the harness, and a
  second model or sub-agents farmed out to on it (the todo above);
  anything left for a later stage of a staged try.
- **The model.** The user expects the frontier vendors' models to
  decline a request to red-team the isolation, and so a model from one
  of the Chinese labs through OpenRouter. Worth one cheap try each
  first, mine: a task that says plainly it is the owner's sandbox and
  an authorised check may be taken up, and the more capable the agent
  the better the test. Which complied is itself a finding.
- **The agent** has to be one of ours through Harbor on that model (nb,
  or Qwen Code), so the try is in a trajectory like any other.
- **Spend** is a hosted model for a model question, so it is agreed
  before any run. The shakedown is the local model, at $0.
- **Where it runs**: on a machine that does not matter, with nothing on
  its loopback worth reaching, never the work laptop.
- What it finds is an issue here, or one for Harbor written for the
  user to file.

## What an interrupted Harbor trial leaves in the engine

Seen on rootless Docker (golem, 2026-11-27, `learnings/harbor.md`):
proctor sent SIGINT during an agent attempt left that trial's compose
project behind, Harbor's sidecar running, the other containers exited,
and its network. The continued run was not hindered. Not known: whether
a Ctrl-C at a terminal, which signals Harbor too, leaves the same;
whether podman does; and whose it is to remove (proctor on continuing,
by the project's name, or Harbor on the signal). To reproduce both ways
before anything is filed.

## A prediction's date is the author's, and required

**Built 2026-11-27**: `written` is required and the author's, proctor pins only the standard, "late" is "post hoc" and "Not pinned" is gone.

Decided with the user on 2026-11-27, during the viewer's design day. Not built.

- **`written` is a required field of a prediction file**, set by the person who
  writes it. A prediction without one is a validation problem, refused like any
  other missing field. Today `proctor predict` and `run` set it
  (`Predictions.Stamp`), and a file without it is reported as "Not pinned".
- **`predict` and `run` stop setting `written`** and keep stamping `standard`,
  each stage's checks at that moment, which nobody can type by hand. A prediction
  with a date and no standard yet needs no mark of its own.
- **"Not pinned" goes away** as a label in `PredictionOutcomes` and everywhere
  it is printed. What is left: the date is the author's claim, and proctor
  compares it with when the tries were made.
- **"Late" becomes "post hoc"** on the screens, and should in the report and
  `list` too, so the three say one word.
- **Migrate what exists:** `exams/tern-docs/predictions/*.json` already carry
  `written`; `docs/configuration.md`, `docs/cli.md` and the changelog say who
  sets it.

## Four roles for a check: correctness, integrity, quality, observation

**Built 2026-11-27**: the lists are `grading.correctness`, `grading.integrity` and `grading.quality` in every exam; a judged check is refused under correctness; fewer tool calls, tokens and time count as better (the user, the same day). Still open below: integrity's own rate. Not built: anything that computes with quality apart from observation.

Decided with the user on 2026-11-27, during the viewer's design day ("the
taxonomy is solid"). Drawn on the canvas; nothing is built. It replaces the
three kinds a check has by where it is named (`grading.pass`, the headline;
`grading.validity`; neither, a guardrail rate).

- **correctness:** was the task completed in a way that satisfies our intent.
  Zero fuzziness. Decides right or wrong. Today's `grading.pass`.
- **integrity:** can the result be trusted; did the agent cheat or act
  improperly. Today's `grading.validity`: a failure takes the try out of the
  numbers, with its reason.
- **quality:** the result has a quality we prefer and do not require (vanilla
  in a sundae). It has a preferred answer, so it can be said to be better or
  worse, and it aggregates as correctness does: quality checks held, of N,
  unweighted. No weights: nobody can defend the numbers, and a composite hides
  the trade-offs.
- **observation:** a quality we are interested in, or that is diagnostic, with
  no preferred answer (a favourite flavour). Never aggregated and never
  coloured better or worse; each is its own rate. The default: a check named
  in no list.

In `tern-docs` as drawn: `exit_ok`, `acceptance`, `tests-pass` are correctness;
`tests-untouched` is integrity (it sits among the guardrails today, so an agent
that edits the shop's tests still counts); `current-endpoint` is quality;
`docs-fetched` and `docs-curled` are observations.

Proposed and not yet answered by the user:

- **The names in the exam's files too** (`grading.correctness`,
  `grading.integrity`, `grading.quality`), not only on the screens, so the user,
  the agent and proctor say one word. "Headline" goes from the report, `list`
  and the docs with it.
- **Correctness is deterministic by rule:** a judged check (`judge`, `decide`)
  named as correctness is refused, and can only be quality.
- **Integrity gets its own rate** on the exam and in a comparison, since a
  configuration whose agent cheats more otherwise only has fewer tries.
- **The measures have the same question.** Tool calls, tokens and time are
  observations unless the exam says fewer is preferred; the viewer's mock
  assumes fewer is better to colour them.

## Four roles are too many to explain in a section's side note

From the same day. The viewer's sections each carry a two-line note beside the
heading, and four roles with their consequences do not fit in one. Drawn for
now: the note says a role is what the answer is used for, and each role's tag
carries its sentence as a tooltip. To settle when the pages are built: a
tooltip, a legend under the table, or a short glossary the tags link to; and
whether a tooltip is enough on a screen with no hover.

## A configuration can be retired

From the user, 2026-11-27, reviewing the viewer's index of exams. The index
lists each exam's named configurations with their marks, and that list is the
headline read: what is shipping, what is in the pipe to replace it, how that is
going. It stops being one after six months of configurations nobody is
interested in any more. So a named configuration needs a way out of the list
that deletes nothing.

**Built 2026-11-27** as proposed below (`Exam/Retired.cs`, `proctor retire`,
`--bring-back`; the viewer's exam, index and configuration pages). Not built:
the same through MCP, since proctor has no MCP server yet. Open: the report's
`summary.md` and `coverage` still list a retired configuration among the rest,
marked.

Settled by the user the same day: the word is **retired**, a reason is
recorded, and a retired configuration can be brought back. The rest was proposed.

- **A verb beside `live`** (`proctor retire <exam> <configuration> --reason
  "..."`, and one to bring it back; the same through MCP), since the viewer has
  no write actions in v1. Recorded with the exam with the reason and the date,
  committed, in no hash. "Rejected" was passed over because it is a judgement
  proctor does not make, "expired" because it suggests a clock.
- **Nothing is deleted.** A retired configuration keeps its tries in the bank,
  its page, its predictions and their outcomes, and can still be compared by
  name.
- **It leaves the main lists:** the index and the exam page's
  Configurations show the ones not retired and a count of the retired, which
  opens them. `run` makes no new tries of it.
- **The live configuration cannot be retired;** mark another live first.
- The retired list shows each one's reason and date.

## The viewer's pages are designed; two exams to follow the build

From the user, 2026-11-27, at the end of the design days. The five pages
(exams, exam, configuration, comparison, stage of a try) and a board of states
are drawn on the canvas and agreed as far as drawings go; the next gains need
the pages live on real data. The build goes ahead on tern-docs. Two exams
follow it closely, both for the data they give the viewer and for their own
questions:

- **Can Claude Code build a conforming, useful exam?** Including the Harbor
  side. Probably on Modal, since Harbor cannot run inside Harbor.
  `exams/exam-authoring` and `exams/agent-onboarding` are the starting points.
- **Can an agent break out of our sandbox?** Qwen Code or opencode as the
  agent, on whatever model OpenRouter offers that will try. A measurement of
  our own containment (`docs/network.md` says what it claims). Spend agreed
  before any run; opencode is not an agent proctor drives yet.

## A link to compare any two configurations

From the user, 2026-11-27. The comparison page answers any pair
(`/<exam>/compare?configuration=X&against=Y`), but the viewer links only to
"X vs live" (the exam page's rows, the configuration page) and to the pair a
prediction names. Any other pair is reached by typing the address. Wanted: a
way to get from the pages to X vs Y for two configurations the reader picks.

Not designed. The viewer has no forms in v1, so the likely shape is links: on
a configuration's page, "vs" each other named configuration that has tries.
Retired ones are a question (probably listed last, or under their count).

## Many variables at once: can an agent do the analysis with what we offer

From the user, 2026-11-27. Not for this weekend. Somebody will want to turn
several axes at once and read what comes out: which value matters, where two
of them interact, whether a finding holds on another model. That is not who
proctor is for, it brings real complexity and a real risk of the reader
confusing themselves, and it may not be worth a screen at all.

What is wanted first is evidence, not a page: an exam in which an agent (Opus)
is asked questions of that kind about a bank with several varying axes and has
only proctor's tools to answer with (`compare`, `coverage`, `--within`,
`--against`, the skill `proctor-answers`). The bank's answers are known, as
`Proctor.Tests/exams/axes-answers` is built to have, so each answer can be
graded. What it finds decides whether the tools need more, and only then
whether a screen is worth drawing.

## exam-authoring still shows the agent the docs from before the four roles

**Done 2026-11-27**, the user's call: the four variants have new ids at the
new pin (`docs-r2`, `docs-haiku-r2`, `docs-and-examples-r2`,
`docs-and-examples-haiku-r2`, at `c91da64`). The old files are gone from
`variants/`; their definitions are in the log entries of the runs that used
them, and their 36 tries stay in the bank, listed as stale. What follows is
how it was found. `continue-the-batch` is still open.

Found 2026-11-27 while moving the pins. `exams/exam-authoring`'s four variants
pin proctor's docs at `b3250b6`, which teach `grading.pass` and
`grading.validity`. The exam an agent writes is graded by the host's proctor,
which refuses those names since the same day, so a try made now would fail for
the docs it was shown. The graders are ported (`trial/run.sh`,
`reference/shakedown.sh` read `correctness` and `integrity`; the shakedown
passes on three tasks and fails `--naive` as it should).

The pin is not moved, because that is a naming decision. A variant's bundle is
part of its identity, and `docs-haiku` and `docs-and-examples-haiku` made tries
under these ids (9 current, 27 stale), so `run` refuses a changed pin under the
same id: "a changed variant is a new variant: give it a new id". Either the
four variants get new ids at the new pin and the old files stay as the record
of what made the banked tries, or the old tries are moved out of the bank.

Separately, and from before: `continue-the-batch` and `hooks/interrupt.sh` are
written for the layout before the bank, and that task fails its own shakedown.

`exams/agent-onboarding` had no tries and its pin is moved (`e1706b4`); its
shakedown passes, and nb and proctor build at the pins in the fixture's base
image.

## An exam's README is part of what a script verdict was made under

Found 2026-11-27. `exams/tern-docs/README.md` still says "headline checks"
and calls `current-endpoint` a guardrail. Correcting it would mark every
script verdict of the banked tries outdated, since `HarborCompile.Outdated`
compares every file of the exam's compiled copy but the definition files, and
a README is one of them: the tries would have to be made again, on a paid
model, for a change no script reads. Either a README (or `*.md`) joins the
files that comparison leaves out, or the wording waits for the next tries.
