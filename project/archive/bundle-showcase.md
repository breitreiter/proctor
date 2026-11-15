---
type: plan
title: The bundle showcase: two versions of an API's docs, and none, on Claude Code
created: 2026-11-12
status: built 2026-11-12 (`suites/tern-docs`, `fixtures/tern-shop`), shaken down at $0, run once on Sonnet ($1.83; "First results" below). This is step 7 of `lab.md`, brought forward onto today's report. Where the build departs from this plan, "As built" below says so
---

Written in proctor's earlier vocabulary (suite, arm, experiment, baseline, sample). Superseded by project/plans/exam-variant-attempt.md; kept for the reasoning.

# The bundle showcase

## As built (2026-11-12)

- **Bundles are `as-shipped`, `restructure` and no bundle (arm `no-docs`)**, not `v1`/`v2`/`none`, so they
  do not collide with the API's own versions.
- **The planted regression is sharper than D2 alone.** v1 took kilograms and v2 takes grams. The restructured
  Python quickstart still calls `/v1/quotes` with `"weight": 1.2`, so copying it whole gives the right price on
  the retired endpoint (`current-endpoint` fails, `acceptance` passes), while mixing it with the v2 reference
  gives the wrong price (`acceptance` fails). `/v1/quotes` answers with a `Deprecation` header.
- **Errors name a missing field or unknown parameter**, never a unit, so a run without docs can find the
  field names but not the semantics.
- **`docs-opened` is two checks**, `docs-fetched` (`WebFetch`) and `docs-curled` (`Bash`), since `tool_args`
  takes one tool name. Together they answer unknown 1 below from the runs themselves.
- **Unknown 2 is settled**: the oracle pass ran every task's verifier offline with Tern's API on its loopback.
  Inside the agent's container the docs answered 200 (404 for `no-docs`) and the API 401 without a key.

## First results (2026-11-12/13)

- **Sonnet** (36 runs, $1.83): `as-shipped` 12/12, `restructure` 12/12, `no-docs` 2/12 (confirmed worse).
  `current-endpoint` passed everywhere: every quote run in `restructure` read the stale Python quickstart, then
  the quotes reference, and built on `/v2` in grams. Without docs Sonnet stopped and asked for them on 10 runs
  rather than guess; only `sync-shipments` (a guessable GET) passed. It read the docs with `curl`, never `WebFetch`.
- **Qwen Code on the local model** (18 runs, from a private copy): 6/6, 6/6, 1/6. Without docs it guessed paths
  until its call limit, at six times the tokens. It too corrected the stale quickstart from the reference.
- **The expected result above is refuted on one count**: the restructure cost neither agent anything. Two
  questions follow rather than a showcase: does a large treatment move the needle (yes), and how much friction
  can agents work around (a stale snippet, at least). The next step, if wanted, is bundles that add one
  defect at a time until the pass rate drops.
- The run found an ingest bug: Sonnet called a tool `bash`, Claude Code refused it, and ingest failed the cell
  as `adapter_unmapped`. Such a call is now kind `none`; the seven cells were re-ingested from their trials.

## What it is for

The site says proctor measures how a change to an agent-facing surface
changes what agents do. Every committed showcase compares models
(`code-change`) or harnesses (`oracle-change`). This suite is the first whose
arms differ only in the bundle, on a vendor agent: one agent, one model,
the same tasks and fixture, and three versions of an API's documentation.

It runs on the report as it is today. It does not wait for `lab.md`'s
surface block, event table, paired effort statistics or agent-by-bundle
layout (steps 3 to 6). What today's report cannot say, this plan says under
"What the report will not say".

## The domain: an invented shipping-quotes API, not telemetry

`lab-report-worked-example.md` uses an invented telemetry service. A public
suite in which an agent instruments a repository for a telemetry service
from a docs URL, with the docs and the collector simulated, repeats the
design of a vendor bench proctor learned from. So the showcase uses a
different domain.

**Tern** (an invented carrier; check that the name is free before the
report goes on the site) has a small HTTP JSON API for shipping quotes and
bookings. It needs only the standard library, it gives several natural
tasks, and its details can be planted cleanly. The product under test is
Tern's documentation; the customer's code is the fixture.

## The fixture: `tern-shop`

A small Python shop backend (standard library and pytest only, preinstalled
in the image) with a `shipping.py` that raises `NotImplementedError`, a
passing test suite that never touches shipping, and a README that says the
shop ships with Tern.

Two compose services in `services/`, both on the agent's loopback, as the
stub model is (`project/learnings/harbor.md`, staging 5):

- **`api`** at `http://localhost:8081`: Tern's API, the same in every arm.
  It records every request (method, path, headers, body, answer) to a
  JSON-lines log.
- **`docs`** at `http://localhost:8080`: a static server over
  `/srv/docs`, which is **the arm's bundle**. With no bundle it answers 404
  to everything.

The prompt is identical in every arm and names both hosts and a test key.
It never mentions what the docs contain.

## The planted details

Each detail is in the docs, cannot be read from the API's answers, and is
checked by a script. The terse errors are deliberate and part of the
fixture; the README says so, as `jotcli`'s does about its own plant.

| # | Detail | What happens without it | Role |
|---|---|---|---|
| D1 | `weight` is in grams; `amount` is in minor units | The API accepts kilograms and quotes the wrong band; the shop shows the wrong price | `pass`, through acceptance |
| D2 | `/v2/quotes` is current; `/v1/quotes` is retired but still answers 200 | Nothing visible; the run passes | guardrail `current-endpoint` |
| D3 | Bookings take an `Idempotency-Key`; a retry without one books twice | A double booking under the acceptance test's dropped response | `pass`, through acceptance |
| D4 | Listings page by `cursor`; the first page is 20 | Shipments after the 20th are missed | `pass`, through acceptance |
| D5 | A status of `held` means the parcel awaits customs, not a failure | Wrong mapping in the shop's order state | `pass`, through acceptance |

D3 and D4 follow conventions a frontier agent may guess, and D4 is visible
in the API's answer (`next_cursor`). They are where Sonnet is expected to
pass without docs, which the report should show and say.

## The bundles

`suites/<suite>/bundles/`, as in `readme-answers`:

- **`none`**: no bundle. The docs server answers 404.
- **`v1`**, as shipped: a quickstart that shows a `/v2/quotes` call with
  grams and minor units, and reference pages for bookings, listings and
  statuses.
- **`v2`**, the restructure under review: the quickstart split into one
  page per language, its example still on `/v1/quotes` (the stale-snippet
  regression a docs restructure really makes); units moved from the
  quickstart to a reference page; idempotency and statuses unchanged.

The expected result, written down before any run so the run can refute it:
Sonnet passes about as often with `v2` as with `v1`, uses `/v1/quotes`
more often with `v2`, and spends more tokens with `v2`. With `none` it fails
D1 and D5 and passes D3 and D4.

## The tasks

Six tasks on the one fixture, each one function in `shipping.py`, each
needing at least one detail. A paired comparison's `n` is the task count
(`CLAUDE.md`, "Each task is scored as its mean"), so three tasks can never
confirm a difference. Six can, only when nearly every task moves one way.

| Task | Asks for | Needs |
|---|---|---|
| `quote-at-checkout` | the cheapest quote for a cart, in cents | D1, D2 |
| `quote-options` | every service level for an address, cheapest first | D1, D2 |
| `book-shipment` | book a paid order, safe to retry | D3 |
| `sync-shipments` | every shipment since a date | D4 |
| `order-status` | the shop's order state from Tern's status | D5 |
| `cancel-before-pickup` | cancel unless collected | D2 (v2 path), D5 |

## Grading, all offline in the verifier

The verifier has no network and no services. The fixture's `services/`
reaches it anyway (`tests/fixture/`), so each acceptance script starts
Tern's API on the verifier's loopback, runs the agent's `shipping.py`
against it, and reads the API's log.

| Check | Level | Role |
|---|---|---|
| `exit_ok` | suite | validity |
| `acceptance` | task (`acceptance/<task>.sh`) | pass |
| `tests-pass` | fixture | pass |
| `current-endpoint` | suite: the API log has no `/v1/` request | guardrail |
| `docs-opened` | suite: `tool_args` regex on `localhost:8080` in any call | guardrail |
| `tests-untouched` | fixture | guardrail |

`docs-opened` is read on the host from the trajectory, so it sees
`WebFetch` and `curl` alike. For the `none` arm it reads whether the agent
tried.

## Arms

Claude Code on Sonnet, one arm per bundle, two samples:
`sonnet-none`, `sonnet-v1`, `sonnet-v2`, with `sonnet-v1` as the
reference. 6 tasks × 3 arms × 2 samples = 36 runs.

No second model in the committed suite: the verdict card compares every
arm with the reference, so a Haiku arm beside them would put a model
comparison on the page the suite exists to avoid. A Haiku experiment, if
wanted, runs from a copy with the arms swapped and is reported apart
(`lab.md` decision 6). The free shakedown arms (Qwen Code on the local
model) also live only in that copy.

## What proctor needs: the bundle reaches a fixture service

Today an agent arm's bundle stays on the host (#11). The smallest change
that serves it, without putting it in the agent's container (#11's point:
a docs site is read over HTTP, not grepped):

- `HarborCompile.Task`: when the fixture has `image.services`, copy the
  arm's bundle to `environment/bundle/`, or create that directory empty
  when the arm has none, so a compose service can mount `./bundle`. The
  task directory is already per (arm, task).
- Nothing else changes. The verifier's `PROCTOR_BUNDLE` stays empty, the
  agent's container holds no bundle, `resume` already refuses a changed
  one, and Modal already refuses `image.services`.
- `HarborTests`: a compiled task has the bundle beside the services, and
  an arm without one has the empty directory.
- `docs/configuration.md` (the bundle section), `CLAUDE.md` (the bundle
  convention), `CHANGELOG.md`, and the todo entry "An agent arm's bundle
  never reaches its container" narrowed to the agent's container.

## Unknowns to settle for nothing

1. **Does Claude Code's `WebFetch` reach `http://localhost:8080` behind the
   allowlist?** If it first asks Anthropic whether a domain is safe, that
   call is blocked, and the agent falls back to `curl`. Both are fine to
   measure, but the report should know which. The stub model only calls
   the shell, so this takes one Haiku run (a few cents), agreed first, or
   reading Claude Code's settings at the pin.
2. **Loopback in the `no-network` verifier.** Expected to work (loopback
   is exempt from the sidecar). Proven by the oracle pass below.

## Order and cost

1. **Proctor change**, above. $0.
2. **Fixture, services, bundles, tasks, acceptance and reference
   solutions.** $0.
3. **Shakedown**, $0: each reference solution passes every check with each
   bundle (Harbor's oracle agent); an unsolved checkout fails `acceptance`;
   a reference that uses `/v1/` fails `current-endpoint`.
4. **Free arm**: Qwen Code on the local model, three bundles, from a
   private copy. Overnight. Shows whether the docs move a weaker agent and
   catches task wording problems before paid runs.
5. **Sonnet**, the 36 runs. About $0.25 to $0.60 a run by the last two
   Sonnet experiments, so **$9 to $22**. Spend agreed first. The
   promotional credits expire 2026-11-17, so steps 1 to 4 need to be done
   by the 16th.
6. **The report on the site**: copied to `docs/reports/`, its gateway
   host scrubbed, read against the claims in `docs/index.html`, and that
   page's "Sample suites that demonstrate the site's thesis" items closed
   or reworded.

## What the report will not say

Today's report leads with pass rate, shows guardrail rates per arm in the
Checks table, and shows tokens per arm under Cost and effort. So:

- **D2 is a check rate, not a Result row.** A reader has to find
  `current-endpoint` in the Checks table; `lab.md` step 6 promotes it.
- **Effort is per arm, not paired.** Medians and ranges per arm, no
  "higher in 5 of 6 pairs".
- **Which pages were opened** is not counted. `docs-opened` says only
  whether any were.
- **The `none` arm is an arm like the others**, not labelled as the
  control.

The suite's README states these four, so the showcase does not claim what
the page cannot show.
