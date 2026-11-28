---
type: plan
title: Report hosting — app.auspex.bot
created: 2026-11-27
status: decisions only (the user, 2026-11-27). Nothing designed or built
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
- **Report hosting only.** No runs, no judges, no keys, and no model
  call on the server (the viewer makes none today).

## To work out

- **What is uploaded, and how.** The viewer reads the bank's files and
  calls proctor's code (`View/Measured.cs`), so the server needs an
  exam's definition and the attempts a report reads, not a rendered
  page. Is it a verb (`auspex publish <exam>`), and what does it leave
  out: transcripts, and the scrub of host, gateway and machine names
  that the old todo asked for by hand?
- **Who sees what.** Private to the uploader, shared by link, or public?
  The site's evidence has to be readable by a stranger with no invite,
  so at least one public mode is needed before the site can link to it.
- **Where it runs on Azure**: App Service or Container Apps, and where
  the uploaded files live (Blob Storage). The viewer is ASP.NET today
  and reads a local directory.
- **The allowlist**: where it is kept and how an account is added
  (a config value, a table, an Entra group).
- **How it relates to the static demo** (`project/view/demo.md`): is
  that plan superseded, or kept as the public read-only mode?
- **The viewer is read-only in v1** (`viewer-v1-read-only`). Does
  uploading count as a write on the hosted side?
