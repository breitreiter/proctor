---
date: 2026-11-13
status: nice to have, not a requirement. Nothing built. Constrains choices made now so a later demo stays cheap
---

# A read-only demo

## What it is for

People who want to try auspex before taking on proctor and Harbor, and
potential employers. Both will kick the tires on the views and the
information architecture: open an experiment, read a run, compare arms.
Neither is likely to wire their own agent into it.

## What it is

- Hosted read-only (Azure static hosting or similar), on a fixed set of
  public experiments.
- Reads transcripts and proctor's runs: experiments, arms, cells, checks.
- Anything that writes (kept queries, notes, saved views) goes to browser
  local storage, with export and import as a file, or is marked
  unavailable.
- No runtime model features. The model's account of a run is the user's
  own session; in the demo it is absent, or shown as a saved example with
  its citations resolved.

## Make it fall out, not a port

If these hold, the demo is a build target and not a second product:

1. **The reader takes one built file.** Building the event table from cells
   is a separate step that writes one artifact (DuckDB or Parquet). The
   reader never touches cells. The demo ships that artifact plus static
   pages.
2. **The demo is the local reader with a read-only flag**, served the way
   `../dreamlands` is (static frontend, API on Azure Functions). Considered
   and dropped (2026-11-13): DuckDB-WASM with the data in the browser. It
   saves a server nobody needs at a session a week, and costs a second query
   path, a multi-MB first load, and every view designed around browser SQL.
   The demo does not choose DuckDB or SQLite for the event table; the first
   test in `direction.md` does. Open cost: Functions consumption cold start
   on nearly every visit at this traffic.
3. **Writes are plain documents.** A kept query or a note is a JSON
   document of one shape, whether it lands in a file beside the analysis or
   in local storage.
4. **Citation checks are pure.** A quote, a step, the whitespace-stripped
   substring rule: no model, so they run in the browser too, and a saved
   example account can show every quote resolving.
5. **Model features live in their own layer**, so the static build leaves
   them out by construction rather than by flags.

## MCP

A hosted MCP server needs compute, and invites abuse; not worth it for an
audience that will not register it. The cheap version: once the CLI exists
with JSON output, a read-only MCP wrapper over it, run locally against the
downloadable demo file. Worth it only if it is a thin shim; the reaction it
buys is "thoughtful", not use.

## Before anything is published

- **Which experiments.** Never `datasets/proctor-private/`. Scripted
  controls (`smoke`, `gate-oracle`, `playground`, `conformance`) may appear
  only labelled as controls.
- **A scrub pass** on everything shipped, by the public-copy rules in
  `CLAUDE.md` (no employer, no employer product, no Grafana), plus the key
  scan `datasets/README.md` describes. Transcripts are agent output and
  may hold paths, hostnames or names nobody reviewed.
- **Size.** The demo ships the built table and the transcripts it renders,
  not Harbor's job copies (about 110 MB of the 300).
