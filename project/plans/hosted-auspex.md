---
type: plan
title: Hosted auspex — the site, public exams, private exams behind Google sign-in
created: 2026-11-23
status: settled enough to start (the user, 2026-11-23); the decisions marked "the user" are theirs, the rest is a recommendation to confirm as it is built. Nothing built. The data model under it (what ties definitions, attempts and marks together, and the tags that make a task's world) is the next design conversation, not this plan. Follows `rename-to-auspex.md`: everything here is written as if the rename has happened (auspex the tool, the viewer inside it)
---

# Hosted auspex

Three things need to be online at auspex.bot:

1. **A site** for the tool: what it is, the docs, how to install it.
2. **Public exams** anyone can browse, including every mark, as the
   examples the site links to.
3. **Private exams** shared with named people, and later annotated by
   them and by a coding agent.

The plan: the site is static. The viewer runs as itself, the same .NET
server as on a laptop, on Azure App Service, reading a bank on an Azure
Files share. Sign-in is Google, done inside auspex, for the browser and
the CLI alike. There are no share URLs.

## Decided by the user (2026-11-23)

- **Annotations are a near-future feature.** A person labels steps in
  the UI for Claude to reference, and, more importantly, the person's
  coding agent writes into the report. Hosting must allow writes from
  day one of that feature, so it is not a static export.
- **Public is a flag, not a secret.** An exam is public because its
  files say so, and the site links to it. No unguessable URL.
- **Sharing is to Google accounts only, so there are no share URLs.**
  Everyone in the user's office is on Google Workspace. A private exam is
  shared by adding a person to who may read it, and unshared by removing
  them.
- **Azure,** for the $50 a month of credit the user already has, and
  because auspex is .NET.

## Considered and dropped

- **A static export of the viewer** (crawl every route to files, host on
  Pages or a bucket; `View/project/demo.md` argued for it). It holds for
  reading only. Annotations need a server, an identity per writer and a
  store, so the export would be a second product to retire later.
- **Bearer share URLs** (`/r/<random key>/…`). Anyone holding the link
  reads it, a forwarded link is public, and revoking cuts everyone off
  at once. They also cannot attribute a write. Dropped with the decision
  above.
- **Cloudflare.** Workers run JavaScript and WebAssembly, not .NET in any
  practical form; Containers would run it but cost a paid plan beside
  credits already held. Cloudflare Access was the cheapest Google
  sign-in in front of static files, which stopped mattering once the
  viewer needs a server anyway.
- **App Service Easy Auth.** It does the browser login with no code, but
  a CLI token reaches it only through Azure's own token exchange and an
  Azure-only header, and none of it runs on a laptop. Sign-in inside
  auspex is a few lines of standard ASP.NET Core and runs everywhere.
- **API keys for the CLI.** A table of keys, issuing and revoking them,
  and a secret in a file anyway. Google sign-in gives the same file with
  offboarding for free (below).

## The pieces

| Piece | Where | Cost, roughly (check current prices) |
|---|---|---|
| The site and docs | `auspex.bot`, static, from the repository's `docs/` (Static Web Apps free tier or GitHub Pages) | free |
| The viewer | App Service B1 Linux, always on, at e.g. `view.auspex.bot` | about $13 a month |
| The hosted bank | an Azure Files share mounted into the app | cents a month |
| Sign-in | Google, inside auspex | free |

**Always on, not scale to zero.** Container Apps on consumption would
likely be free under its monthly grant, but a .NET start of a few seconds
on most visits makes a shown link look broken. Revisit if the credit
goes elsewhere.

**The share is a directory of files.** Mounted, it is a bank like any
other, so `Bank.cs` reads it unchanged. Pushing an exam is copying its
new files up; an annotation is a file beside the attempt it is about.
No database.

The site keeps the anchors `rename-to-auspex.md` stage 2 row 6 moves to
`auspex.bot`; released anchors are permanent (`Setup/providers.json`).

## Who can see and write what

One check, after sign-in by either path:

| Viewer | Public exam | Private exam |
|---|---|---|
| Not signed in | read | not found (404, so its name does not leak) |
| Signed in, allowed on the exam | read, annotate | read, annotate |
| Signed in, not allowed | read | not found |

**Allowed** means: the Google ID token's `hd` claim is the user's
Workspace domain, or the email is on the exam's list of outside readers.
`hd` is set by Google only for accounts the Workspace manages, so it is
stronger than matching the email's suffix. Which of the two an exam uses,
and where the domain and the list live, is OPEN (below).

**Public** is a field in the exam's files that `push` copies
up. A public exam is scrubbed before it is first pushed, by the rules in
`View/project/demo.md` ("Before anything is published"): never a private
dataset, scripted controls labelled as controls, the key scan on every
file shipped, and a read of transcripts for paths, hostnames and names.
A private exam is scanned for keys too: it is shared with people, and
attempts can hold keys in their config snapshots (the reason the local
server has a token).

## Sign-in

### In the browser

ASP.NET Core's Google handler (`AddGoogle`) with a cookie, scopes
`openid email profile`. Nothing beyond identity is asked of Google.

### In the CLI

`auspex login` uses Google's **device flow**: it prints a short code, the
person enters it at google.com/device in any browser, and approves. It
is the default because the user works over SSH, where the loopback
redirect would need a browser on the same machine. The device flow needs
its own OAuth client of the "TVs and limited input devices" type, and
supports these scopes.

The CLI keeps the refresh token in `~/.config/auspex/credentials`, mode
0600, and exchanges it for a short-lived Google ID token (about an hour)
per call. The server takes it as a bearer token (`AddJwtBearer` with
Google's issuer): signature, audience (auspex's client ids), expiry,
then the same check as the browser.

**What this buys over API keys.** Disabling someone's Workspace account
ends their CLI access with nothing to revoke here. Annotations from the
CLI and the browser carry the same email. The credential on disk is a
secret either way.

**Once logged in, the agent never sees a browser.** The person runs
`auspex login` once; the coding agent then runs `push` and `pull` on
the stored credential, as it would with `gh`.

### Not covered

A machine with no person, such as an Action republishing the public
examples, cannot approve a device code. When one is needed, add one
narrow service credential that may push public exams and nothing
else. Not before.

## Where each kind of record is authoritative (the user, 2026-11-23)

The hosted copy is not just a snapshot pushed whenever something is to
be shown: that would leave nowhere for several people to work. An exam
is three kinds of record, and each has its own home:

| Kind | What | Written by | Authoritative |
|---|---|---|---|
| The definition | `exams/<exam>/`: exam, tasks, variants, checks, live | people, by review | **git**, as today. The server keeps a copy of each definition an attempt it holds was made under, by hash, so the page can show the exam as given |
| The attempts | `bank/<exam>/…`: evidence, grades, friction | `run` and `grade` | **the server**, as a shared bank. A laptop's bank is a working copy that pushes. Attempts are written once, so a push is a union, and a push that would change an attempt the server holds is refused |
| The marks | annotations: labels, replies, the agent's notes | people and agents | **the server**. A laptop's marks are a cache that `pull` refreshes |

So the verbs are:

- **`auspex push <exam>`**: new attempts, the definitions they were made
  under, and new marks, up to the server. Prints the URL to open.
- **`auspex pull <exam>`**: other people's attempts and every mark,
  down into the local bank, where Claude reads them as files.

The coding agent does not write to the hosted copy over HTTP. It writes
marks as files in the local bank (the marks proposal in
`View/project/inventory.md`: one plain file per attempt that either the
session or the person may append to), and `push` carries them up. In the
hosted UI a signed-in person labels a step and the server appends it
under their email. So the only HTTP writer is a signed-in browser, and
`push` and `pull` are the only CLI traffic. A direct API for the agent
waits until it is wanted.

**With one person this is just "push when there is something to show".**
The split matters once a second person runs attempts or annotates, and
the data model has gaps there (OPEN, below): attempt numbers collide
across machines, and a regrade overwrites `checks.json`. Neither bites
while only the user runs attempts and colleagues read and annotate,
which is the first shape to build.

## The rule this bends

`View/project/workbench.md` rule 2: no mode switch, and the server is
loopback only with a per-start token. Hosted, it listens publicly and
learns who is viewing from sign-in.

Keep one program and make **the viewer** an input rather than a mode:

- Locally, the viewer is the person who started the server: allowed on
  everything, the token as today.
- Hosted, the viewer comes from sign-in, and the table above decides.

Pages render for a viewer: a viewer who may not annotate gets no
annotation controls, because the page is built for them, not because a
flag hid them. What changes between laptop and Azure is only how the
viewer is established, and where the bank is mounted.

The crawl test (`workbench.md` rule 4) runs as each kind of viewer:
anonymous reaches only public exams and no write route, sees no
controls, and gets 404 on a private exam; signed in and allowed reaches
everything and can write one mark, which must equal the file `pull`
brings down. The installed form (rule 5) becomes the container or
package deployed to App Service.

## Order

1. **Nothing here before the move-in and the rename**
   (`rename-to-auspex.md`). The hosted viewer is `auspex-view` reading
   auspex's bank.
2. **The site** at `auspex.bot`, static, with the provider anchors. No
   viewer needed.
3. **The viewer as an input**: the viewer seam and the per-viewer crawl,
   on a laptop, with a fake signed-in viewer in tests. No Azure yet.
4. **Sign-in**: the Google handlers, the allow check, the `public` field,
   `auspex login`. Tested against a local server.
5. **Azure**: App Service, the Files share, the domain, the OAuth
   clients' redirect URLs. `auspex push` and `pull` against it.
6. **The first public exams**, scrubbed, linked from the site. They
   replace the two frozen `report.html` copies in `docs/reports/`, which
   were rendered by the removed renderer in the earlier words.
7. **Hosted annotations**, with build step 6's marks.

Steps 2 and 3 do not depend on each other.

## OPEN

- **Which exams are public.** The user: to be decided. Candidates come
  only from public datasets.
- **Where the allowed list lives**: in the exam's files (copied up with
  it, so the local bank says who may read the hosted copy), or on the
  share beside it (changed without a push). The first keeps one
  source of truth; the second lets the user share without the CLI.
- **Whether private exams default to the Workspace domain** or to only
  the person who pushed it until someone is added.
- **Merging marks** on `pull`. Part of the marks design (build step 6 of
  `View/project/build-plan.md`), not of hosting. Append-only files with
  an id per mark make it a union.
- **The data model for more than one writer of attempts.** Attempt ids
  unique across machines (today `n` is numbered after the local bank's
  highest, and a mark's address contains it); a grade that names the
  standard it was graded under, so a regrade is a new record and not a
  rewrite; variant definitions kept by hash outside the log entry, which
  holds machine details not worth pushing. Pending its own plan.
- **Spend.** The viewer makes no model calls, so the hosted cost is the
  plan and the share. Anything that would add a hosted model call is a
  change to the line in `View/CLAUDE.md` (auspex renders, the runner
  codes), not to this plan.
