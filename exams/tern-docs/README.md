# tern-docs

Does a restructure of an API's documentation change what a coding agent builds from it?

Claude Code on Sonnet implements one function at a time in a small shop's Python backend
(`fixtures/tern-shop`), against Tern, an invented parcel carrier. Tern's sandbox API runs beside the agent at
`localhost:8081`, the same in every variant. Tern's docs site runs at `localhost:8080` and serves the variant's bundle.
The three variants differ only in that bundle:

| Variant | Docs |
|---|---|
| `as-shipped` | One quickstart on `/v2/quotes` that says weights are grams and amounts pence; a reference; a changelog that retires `/v1`. |
| `restructure` | A quickstart per language. The Python one still calls `/v1/quotes` with a weight in kilograms, as v1 took it; the units are only in the reference; the changelog is gone. Everything else is the same pages. |
| `no-docs` | None: every page is a 404. |

The prompt is the same in every variant. It names both hosts and never says what the docs contain.

## Tasks and what each needs from the docs

| Task | Implement | Needs |
|---|---|---|
| `quote-at-checkout` | the cheapest price for a cart, in pence | grams (the cart weighs in kilograms) |
| `quote-options` | every service, cheapest first | grams; quotes come unsorted |
| `book-shipment` | a booking that is safe to retry | `Idempotency-Key` (a common convention) |
| `sync-shipments` | every shipment since a date | `created_since`, and paging (visible in the answer) |
| `order-status` | the shop's status from Tern's | `held` is customs, not a problem |
| `cancel-before-pickup` | cancel unless collected | the cancel path; 409 once collected |

The fixture's README lists what is planted and why.

## Checks

- **Pass:** `exit_ok`, `acceptance` (the task's hidden pytest file, run in the verifier against Tern's API
  started there from the fixture, with test controls on: reset, drop the next answer, set a status), and the
  fixture's `tests-pass`.
- **Guardrails:** `current-endpoint` (no added line names `/v1/`; `/v1/quotes` still answers, so an attempt that
  copies the restructured Python quickstart whole can pass), `docs-fetched` and `docs-curled` (the agent opened
  the docs site with `WebFetch`, or from the shell), and the fixture's `tests-untouched`.

## What the report will not say

Today's report leads with pass rate, shows guardrail rates per variant in the Checks table, and tokens per variant under
Cost and effort. So:

- `current-endpoint` is a rate in the Checks table, not a headline row.
- Effort is per variant, not paired by task.
- Which pages were opened is not counted; `docs-fetched` and `docs-curled` say only whether any were.
- `no-docs` is a variant like the others, not labelled as the control.

Six tasks is the `n` of every paired comparison, so a difference is confirmed only when nearly every task moves
the same way.

## Running it

```
exams/tern-docs/reference/shakedown.sh               # every check passes on the reference
exams/tern-docs/reference/shakedown.sh --unsolved    # acceptance fails everywhere
exams/tern-docs/reference/shakedown.sh --v1          # current-endpoint fails; acceptance passes
exams/tern-docs/reference/shakedown.sh --kilograms   # acceptance fails on the two quote tasks
proctor run tern-docs                                  # 36 attempts on Sonnet: real API spend
```

The shakedown needs python3 with pytest, git, curl and jq, and costs nothing. Services need Docker or podman; the
exam cannot run on Modal. The plan behind it is `project/archive/bundle-showcase.md`.
