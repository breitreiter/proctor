---
type: plan
title: Report hosting — app.auspex.bot
created: 2026-11-27
status: decisions (the user, 2026-11-27) and a proposal for the rest. Nothing built
---

# Report hosting

The one part of auspex that runs somewhere other than the user's
machine: the viewer, hosted, so a report can be shared by link.

## Decided (the user, 2026-11-27)

- **`app.auspex.bot`, on Azure.**
- **Sign-in with Google**, and nothing else.
- **Invite-only alpha.** Accounts are added by hand. Someone who signs in
  with an email that isn't on the list sees a friendly page: online
  auspex is in invite-only alpha, and the tool itself is free in the
  repository (link). It is not an error page, and there is no sign-up
  form.
- **A public mode: an exam shared publicly at a stable URL**, readable
  without signing in. We need it ourselves, for the site's evidence.
- **Report hosting only.** No runs, no judges, no keys, and no model
  call on the server (the viewer makes none today).
- **A verb uploads.** Scrubbing sensitive data is pinned: hard, important,
  and not designed here (below).
- **No promise to keep enough to regrade.** The viewer is nearly the
  only reader. Keep regrade material only where it is cheap.
- **The allowlist starts as a text file** in the old auspex repository.
  Before that file goes in, the repository is deleted from GitHub and its
  remote moved to library, so the list is never public.

## What is uploaded (proposal)

The viewer reads, per attempt, `manifest.json`, `checks.json`,
`core.json`, `trajectory.json` and `friction.json`. Through
`View/Measured.cs` it also calls proctor's own code: `Exam.Load`,
`Bank.Of`, `Configurations.Compute`, `Answers.Compare`, `Predictions.Load`.
That code reads the exam's definition, the log entries (the bank is what
they list) and each attempt's `grades/`. It also hashes each task's
fixture *by content*, to tell current attempts from stale ones.

So the hosted copy cannot be "the bank, uploaded". If it were, the
server would need every fixture's source tree just to hash it. Instead,
**`publish` takes a snapshot on the user's machine**, where everything
is, and freezes what only that machine can work out:

- **Kept**: the exam's definition files (`exam.json`, `tasks/`,
  `variants/` or `axes`, `predictions/`, `live.json`, `retired.json`),
  each fixture's `fixture.json` without `repo/`, the log entries the
  attempts came from, and per attempt `manifest.json`, `checks.json`,
  `grades/`, `core.json`, `trajectory.json` (or `transcript.jsonl` for
  bare nb), `friction.json` and `diff.patch` (small, and half of a
  regrade if the fixture is ever uploaded too).
- **Left behind**: the work directory, `harbor/` (the job, container logs
  and verifier output), `verdicts/` (requests and responses: large, and
  they hold the judged windows again), `program.nb` and `program.jsonl`,
  every fixture's `repo/`, and stale attempts.
- **Frozen**: a `snapshot.json` (schema `snapshot/1`, per `remix.md`'s
  rule) with the exam, variant and task hashes as computed at publish
  time, the standard's hash, proctor's version, and when. The server
  takes current-or-stale from it and never hashes a fixture. A hosted
  report is therefore "as of publish", and the page says so.

The viewer gains one mode: read a snapshot in place of a root. That is
also how `project/view/demo.md`'s static read-only demo falls out. The
public mode *is* the demo, so that plan folds into this one.

## Publishing again: content-addressed, so every publish is incremental (proposal)

An exam grows: every run adds attempts. It also changes underneath:
new checks, regrades of old attempts (a new `checks.json`, another file
in `grades/`), an edited definition, and attempts that go stale. So
neither "upload everything every time" nor "upload what is new since
last time" is right. The first is wasteful, and the second misses every
regrade and edit.

What fits is **storing each file by its content hash and making a
snapshot a list of files.**

- **A blob** is one file's bytes, stored under its SHA-256
  (`blobs/<hash>`), and never changed or replaced.
- **A snapshot** is `snapshot.json`: the frozen hashes above, plus every
  path the viewer will read, each with the hash of its blob. It is small
  (a line per file) and is the only thing written in full each time.
- **`publish`** builds the list locally, asks the server which of those
  hashes it lacks, uploads only those blobs (the server checks each
  hash), then writes the snapshot and moves the exam's "latest" pointer
  to it. If an upload is cut off, nothing changes: the pointer moves
  last, and the next publish skips the blobs that already arrived.

This covers each kind of change without a special case:

| what changed | what uploads |
|---|---|
| a new run | its attempts' files; everything else is already there |
| a regrade, or a new check | each attempt's new `checks.json` and `grades/` file: a few KB each. The trajectories, nearly all the bytes, are unchanged and not sent |
| an edited definition | the changed definition files |
| attempts gone stale | nothing: they drop out of the new list. Their blobs stay while an older snapshot still lists them |
| nothing | the snapshot, and `publish` says nothing changed |

The dated snapshots cost nearly nothing, because they share blobs. A
blob no kept snapshot lists can be deleted. That collection, and how
many old snapshots to keep, is decided when storage is worth counting.

## The verb (proposal)

`auspex publish <exam> [--public]` builds the snapshot and prints what
would be sent compared with the latest: attempts added, regraded or
dropped, definition files changed, and the bytes to upload. After a
confirmation (it sends files off the machine), it uploads only those
blobs. `--yes` skips the confirmation, for an agent or a script.
Without `--yes` and without a terminal, it never waits on input: it
prints the summary, says to pass `--yes`, and exits non-zero having
sent nothing. That is the bug `jotcli` plants, and we don't ship it. Its JSON answer and exit codes are written down, again per
`remix.md`. Earlier snapshots stay reachable, unlisted, so a link to
"as of" a date keeps working.

**Scrubbing is pinned.** Until it is designed, `publish` refuses
nothing, and the confirmation names where secrets can hide: endpoints
and host names in manifests and log entries, paths, and whatever the
agent printed in a trajectory. Our own public exams are read by hand
before `--public`, as the old todo asked.

## Addresses (proposal)

- `app.auspex.bot/<handle>/<exam>`: the latest snapshot. Public or
  signed-in, by the snapshot's flag.
- `app.auspex.bot/<handle>/<exam>/<published-at>`: one snapshot, for good.
- `<handle>` is the account's, set when the account is added by hand.

## Where it runs on Azure (proposal)

The precedent is `../dreamlands` (`imp/reference/deployment_ops.md`):
a static front end, the API on Azure Functions (Consumption), and Cosmos
DB (Serverless). The viewer is ASP.NET rendered on the server, not an
API behind a static page, so Functions fits it badly. The proposal:

- **Azure Container Apps on the consumption plan, scaling to zero.**
  It is the same container the viewer builds locally. The cost is near
  nothing at this traffic, and a cold start on the first visit is
  acceptable for an alpha.
- **Built-in authentication (Easy Auth) with the Google provider**, with
  anonymous requests allowed through. The app decides: a public snapshot
  is served to anyone, everything else needs a signed-in email on the
  allowlist, and any other signed-in email gets the alpha page.
- **Blob Storage** for snapshots. No database: the allowlist is a file
  baked into the deploy for now, and a snapshot's flag lives in its
  `snapshot.json`.
- **DNS**: `app.auspex.bot` to the Container App, and the apex to
  GitHub Pages (`site.md`).

## Open

- **Scrubbing**: what counts as sensitive, found how, and whether
  `publish` redacts or refuses.
- **Who may set `--public`**: any invited account, or only us during
  the alpha?
- **Size**: a cap per snapshot, and whether trajectories are compressed.
- **The read-only rule** (`viewer-v1-read-only`): uploading is a CLI
  write, so the hosted viewer stays read-only.
