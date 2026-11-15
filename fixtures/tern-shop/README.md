# tern-shop

Larkspur Tea's shop backend (`repo/`, invented) beside two services (`services/`): Tern's sandbox API on
`localhost:8081` and Tern's documentation site on `localhost:8080`. Tern is an invented parcel carrier.

The API is the fixture's and the same in every variant. The docs site serves the variant's bundle, which proctor copies
beside the services as `bundle/` (empty for a variant with none, so every page is a 404). The exam that uses this
fixture, `exams/tern-docs`, compares versions of those docs.

## Planted on purpose

Each of these is documented only in the docs, and the API is built so that an agent without them can go wrong
quietly. The README says so in case a reader takes them for bugs.

- `/v2/quotes` takes `weight` in **grams**; the retired `/v1/quotes` takes kilograms and still answers 200, with a
  `Deprecation` header. The cart weighs in kilograms.
- A booking repeated with the same `Idempotency-Key` returns the same shipment; without one, a retry books twice.
- Listings take `created_since` and `cursor` and page twenty at a time; any other parameter is a 400.
- A status of `held` means the parcel is at customs and still on its way, not that something went wrong.
- Cancelling is `POST /v2/shipments/{id}/cancel`, refused with a 409 once the parcel is collected.
- Error bodies name a missing field or an unknown parameter, never a unit.

`tern_api.py` answers `/_test/*` (reset, drop the next response, set a status) only with `TERN_TEST_CONTROL=1`,
which the exam's acceptance tests set and the agent's sandbox does not.
