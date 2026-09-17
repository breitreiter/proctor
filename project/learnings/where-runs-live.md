---
type: learnings
title: Where runs live — commit the small layers, archive the big one
created: 2026-09-17
---

# Where runs live — commit the small layers, archive the big one

Prior runs turned out to be worth keeping, or at least their reports did, and
committing the run directories felt like using GitHub as a file store. It is.
Nobody keeps transcripts in git. The normal solution is to split a run into
tiers by size and value, commit the small ones, and put the big one in object
storage with a pointer back from the report.

## Three tiers

| tier | contents | size | where |
|---|---|---|---|
| definition | eval files, cases, rubrics, sheets | kilobytes | committed; it is source |
| derived | `summary.md`, `results.jsonl`, the report | kilobytes | committed beside the evals, or posted to the pull request |
| raw | transcripts, stderr, config snapshots, per-cell manifests | megabytes to gigabytes | gitignored; archived to a bucket |

The derived tier is what people actually reopen. The raw tier is what an
expert asks for when they challenge a verdict, and it is needed rarely and
one cell at a time.

The raw tier must never reach GitHub for a second reason: the oracle bench
writes a config snapshot beside its results that may carry API keys. It is
written with owner-only permissions on disk, which is no protection once it
is pushed.

## Where the raw tier goes

**An object storage bucket with the same layout as the local run directory.**
This is what MLflow, W&B and Inspect all do underneath; Inspect's log
directory can simply be an S3 path. For one developer the cheap choices are
Cloudflare R2 or Backblaze B2, pushed with `rclone`. The layout is
`runs/<experiment-id>/<arm>/<sample>/`, identical to disk, so a local run and
its remote copy are the same thing and a report can link to a transcript by
run id alone.

**DVC pointer files on top of the bucket** if git should know which run
belongs with which commit. A tiny `.dvc` file is committed and `dvc pull`
restores the blobs. It works, but it is a second tool with its own
vocabulary, and the tracker survey in `prior-art.md` found its hidden git
refs a nuisance. A run id in the committed report covers most of the value
without it.

**GitHub Releases for milestone runs only.** Releases are permanent and
linkable, so "the baseline run for v1.2" can live there. Workflow artifacts
are not a store; they expire, ninety days by default.

**A separate results repository** is the same discomfort moved one repo over.
It only makes sense when the results are small, in which case they are the
derived tier and can be committed in place.

## What to avoid

Git LFS. It looks like the answer and is not: GitHub's LFS bandwidth quota is
small, every clone pays for it, and there is no browsing a bucket would give.

## The proctor shape

Two verbs, recorded in `brief.md`:

- `proctor archive <experiment>` syncs the gitignored run directory to the
  bucket and writes the archive location into the report's reproducibility
  block. Idempotent; re-running after a resumed experiment uploads only the
  new cells.
- `proctor fetch <run-id>` pulls one cell back down on demand, so a verdict
  can be challenged against its transcript without restoring the whole
  experiment.

Every row in a committed results table carries a run id that resolves through
the archive location. That is the "defensible to an expert" requirement from
the brief, met without the blobs.
