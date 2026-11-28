# tern-docs

Does a restructure of an API's documentation change what a coding agent builds from it?

Claude Code on Sonnet implements one function at a time in a small shop's Python backend
(`fixtures/tern-shop`), against Tern, an invented parcel carrier. Tern's sandbox API runs beside the agent at
`localhost:8081`, the same in every configuration. Tern's docs site runs at `localhost:8080` and serves what the
`docs` axis sets (the fixture's `DOCS` parameter, a tree of pages under `values/docs/`). `as-shipped` is live.
The three configurations differ only in that value:

| `docs` | Docs |
|---|---|
| `as-shipped` | One quickstart on `/v2/quotes` that says weights are grams and amounts pence; a reference; a changelog that retires `/v1`. |
| `restructure` | A quickstart per language. The Python one still calls `/v1/quotes` with a weight in kilograms, as v1 took it; the units are only in the reference; the changelog is gone. Everything else is the same pages. |
| `none` | None: every page is a 404. The configuration is named `no-docs`. |

The prompt is the same in every configuration. It names both hosts and never says what the docs contain.

## Stages and what each needs from the docs

One try is the six stages in this order, in one container. Each stage starts from the shop as the fixture has
it and in a new conversation, so no stage builds on another's code. Tern's sandbox at `localhost:8081` is not
restarted between stages: a shipment one stage books is still there for the next. The checks are not affected,
since the verifier starts a sandbox of its own.

| Stage | Implement | Needs |
|---|---|---|
| `quote-at-checkout` | the cheapest price for a cart, in pence | grams (the cart weighs in kilograms) |
| `quote-options` | every service, cheapest first | grams; quotes come unsorted |
| `book-shipment` | a booking that is safe to retry | `Idempotency-Key` (a common convention) |
| `sync-shipments` | every shipment since a date | `created_since`, and paging (visible in the answer) |
| `order-status` | the shop's status from Tern's | `held` is customs, not a problem |
| `cancel-before-pickup` | cancel unless collected | the cancel path; 409 once collected |

The fixture's README lists what is planted and why.

## Checks

- **Pass:** `exit_ok`, `acceptance` (the stage's hidden pytest file, run in the verifier against Tern's API
  started there from the fixture, with test controls on: reset, drop the next answer, set a status), and the
  fixture's `tests-pass`.
- **Guardrails:** `current-endpoint` (no added line names `/v1/`; `/v1/quotes` still answers, so an attempt that
  copies the restructured Python quickstart whole can pass), `docs-fetched` and `docs-curled` (the agent opened
  the docs site with `WebFetch`, or from the shell), and the fixture's `tests-untouched`.

## What the report will not say

The report compares each `docs` value with the live one try against try, on how many of a try's correctness checks held and how many of its
steps were flagged as friction. So:

- `current-endpoint` is a quality check: reported, and deciding nothing.
- Which pages were opened is not counted; `docs-fetched` and `docs-curled` say only whether any were.
- `no-docs` is a configuration like the others, not labelled as the control.

The `n` of a comparison is tries, and a difference needs four a side to be confirmed (or three against five).
`tries` is 2, which shows the size of a gap and cannot confirm one; raising it to 4 doubles the spend.

## Running it

```
exams/tern-docs/reference/shakedown.sh               # every check passes on the reference
exams/tern-docs/reference/shakedown.sh --unsolved    # acceptance fails everywhere
exams/tern-docs/reference/shakedown.sh --v1          # current-endpoint fails; acceptance passes
exams/tern-docs/reference/shakedown.sh --kilograms   # acceptance fails on the two quote stages
proctor build tern-docs                                # what each docs value hands the image build; nothing run
proctor run tern-docs                                  # the three named configurations: 6 tries of 6 stages on Sonnet, real API spend
```

The shakedown needs python3 with pytest, git, curl and jq, and costs nothing. Services need podman (or Docker). The
plan behind it is `project/archive/bundle-showcase.md`.
