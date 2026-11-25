---
type: plan
title: Proctor becomes auspex — the viewer moves in, then the rename
created: 2026-11-23
status: agreed with the user 2026-11-23 (see "Decided"). Nothing done. Deferred 2026-11-24 (the user): after `axes-build.md` is done, whenever that is; the 2026-11-29 target no longer holds. auspex.bot is registered (2026-11-23). Counts are grep counts from 2026-11-23, to size the work, not a checklist
---

# Proctor becomes auspex

The tool is renamed auspex, after the Roman augur who watched birds and
read what they did. That name is held today by the viewer
(`/srv/aiwork/auspex`, private remote `breitreiter/auspex`), which was
already going to fold into proctor (the user, 2026-11-21). So this plan
covers two jobs: the viewer moves into this repository, and the
repository takes its name.

In this plan, **the viewer** means today's auspex repository and
**proctor** means this one, until stage 2 says otherwise.

## The order: move the viewer in, then rename, as two separate pieces of work

Do not do both in one change. Do not wait for the full fold either.

**Why not together.** Both are big mechanical changes that need
different checks. The move-in is checked by building and testing: two
projects in one solution, both suites green. The rename is checked by a
grep gate: no "proctor" left except where the allow-list says why. If
both land together, a red test could come from either one, and neither
can be reverted on its own.

**Why the move-in comes first.** Moving the viewer in frees the name in
the four places where it collides:

| Where | Taken by the viewer today | Freed by |
|---|---|---|
| GitHub | `breitreiter/auspex` | renaming the viewer's remote once it is merged |
| Local directory | `/srv/aiwork/auspex` | deleting the checkout once it is merged |
| Namespace | `Auspex`, `Auspex.Tests` | the viewer becoming `Auspex.View` during the rename |
| Binary | `auspex` | the viewer becoming `auspex-view` during the rename |

If the rename came first, the viewer would need temporary names in all
four places and would then be renamed again when it moved in. Moving in
first also means the viewer's 686 mentions of proctor are renamed in the
same sweep, under the same gate, once.

**Why not wait for the full fold.** The fold that matters for design (one
binary, `auspex view` as a verb, the viewer reading the bank through
proctor's layout code instead of its own copy of the file names in
`Bank.cs`) waits on proctor verbs that are not built yet (`bank.md`
steps 4 and 5). Tying the rename to it would delay the rename
indefinitely for no gain. The move-in is only a change of repository,
with no design in it, so it can happen now.

**"Let it settle" means:** a working session or two in which viewer
work happens in this repository, and nothing reaches for `../auspex`.
Not weeks.

## Before either stage

- **A clean tree and no open branch.** A rename conflicts with every
  line of every open branch. Today the work is on `exam-variant-attempt`
  with uncommitted changes in six files. Do the rename between slices of
  `bank.md` step 4, after the branch has merged to master. If it cannot
  merge in time, do the rename on it, provided it is the only branch and
  nothing is uncommitted.
- **The viewer arrives as one squashed commit** (decided): today's tree,
  without its history. That removes the history question, but the tree
  itself still becomes public, because proctor is a public repository
  and the viewer's remote is private. Before committing, read through
  `project/learnings/`, `project/trials/` and anything else quoting a
  transcript, a path or a name, and leave behind whatever should not be
  published.

## Stage 1: the viewer moves in

**Done in part, 2026-11-24** (the user: now or later, with freedom over
how; a hosted viewer will have no model key). Steps 1, 3, 4 and 6 are
done: the viewer is `View/`, its design notes `project/view/`, both
projects in `proctor.slnx`, both suites green from the root. It was
done ahead of `axes-build.md`'s end because slice 3d needed it: a
comparison's page has to show proctor's own number, and the viewer can
only do that by calling proctor's code, which a second repository
would have had to copy.

What differs from the steps below, and what is left:

- **`project/trials/` was left behind** (712 files): throwaway scripts
  and their outputs, which quote transcripts. Whether any of it may be
  published is the user's to say. `project/view/` docs still name
  paths under it.
- **Not committed as its own commit naming the viewer's revision**
  (step 2) unless the user asks for that: it came from `5559896`.
- **The viewer references proctor's project** and sees its internals,
  which step 6 ("nothing about its behaviour changes") did not plan.
- **Steps 5 and 7 are not done** and are the user's: `datasets/` moved
  out, the private remote renamed and archived, the old checkout
  deleted. Until then the old checkout is the only home of
  `project/trials/` and `datasets/`.

1. Copy the viewer's tracked files (`git ls-files`, so `bin/`, `obj/`
   and `datasets/` stay behind) into `View/`, matching proctor's
   PascalCase top-level directories (`Run/`, `Grade/`, `Report/`). Its
   `project/` becomes `project/view/` in proctor; its `CLAUDE.md` stays
   as `View/CLAUDE.md`, which Claude Code reads when working in that
   directory.
2. Commit it as one commit that names the viewer's last revision
   (`61c9081` on 2026-11-23, or whatever is newest on the day), so the
   private remote can still be found from it.
3. **`Proctor.csproj` compiles every `.cs` under the root by default.**
   Add `View\**` to its `Compile Remove` and `None Remove`, or proctor's
   exe will try to compile the viewer's `Program.cs` and fail. The
   viewer's csproj already compiles only its own directory
   (`EnableDefaultCompileItems` false, `*.cs`), so that one is fine.
4. Add `View/Auspex.csproj` and `View/Auspex.Tests/…` to `proctor.slnx`.
5. Move `datasets/` (1.9 GB, partly private) out of any repository:
   `/srv/aiwork/auspex-datasets/`, used through the `AUSPEX_TEST_ROOT`
   the viewer's tests already read. Private data in a public
   repository's working tree is one `git add -f` away from being
   published.
6. The viewer keeps its namespace, its binary name and its
   `<proctor root>` argument. Nothing about its behaviour changes in this
   stage.
7. Rename the viewer's GitHub remote to `auspex-viewer-archive` and
   archive it. GitHub will not rename proctor to `auspex` while another
   repository of the user's holds the name, even an archived one.
   Delete the local `/srv/aiwork/auspex` only after `datasets/` has
   moved out.

Done when one `dotnet test` from the root runs both suites green and
`View/CLAUDE.md` no longer says "keep it a separate repository".

## Stage 2: the rename

One commit per layer, so each can be reviewed and reverted on its own.
The gate runs at the end of each one.

| # | Layer | Size | Notes |
|---|---|---|---|
| 1 | Repository and directory | — | GitHub `proctor` → `auspex`. GitHub redirects the old URLs, so variants that pin proctor's repository at a revision as their bundle (`exams/exam-authoring/`) keep resolving; update them anyway. Locally, `/srv/aiwork/proctor` → `/srv/aiwork/auspex` |
| 2 | Code | 34 + 25 files in `namespace Proctor`/`Proctor.Tests` | `Proctor` → `Auspex`, `Proctor.Tests` → `Auspex.Tests`, `AssemblyName` `proctor` → `auspex`, the csproj and `.slnx` file names, `InternalsVisibleTo`. The viewer becomes namespace `Auspex.View` (tests `Auspex.View.Tests`), assembly `auspex-view` |
| 3 | Environment variables | about 40 `PROCTOR_*` | → `AUSPEX_*`. The viewer's `AUSPEX_TEST_ROOT` and `AUSPEX_TEST_PUBLISH` would collide with the renamed `PROCTOR_TEST_*`, so they become `AUSPEX_VIEW_TEST_*`. Change it in one step, with no fallback (decided): at startup, if any `PROCTOR_*` is set, refuse, give its new name and point to `docs/migrating-from-proctor.md`. A silent fallback leaves two names in use forever |
| 4 | Files on disk | `proctor.json` (171 mentions), `.proctor/` | → `auspex.json`, `.auspex/`, and the `.gitignore` patterns (`exams/proctor.*.json`). An existing machine that has `proctor.json` and no `auspex.json` gets a one-line refusal that says to rename the file and points to the guide. **The bank is a different case:** it is data already written. If an attempt's files name proctor (`versions.proctor` turns up in grep), the reader must keep accepting the old field for those attempts. Check every file kind under `bank/` before deciding |
| 5 | The skill | `skills/proctor-exam/` | → `skills/auspex-exam/`. Users copied it into their agents, so the CHANGELOG says so |
| 6 | The docs site | `breitreiter.github.io/proctor/` in `Setup/providers.json`, `Setup/Catalogue.cs`, `docs/providers.html` and setup's plans | The site is not live (404 on 2026-11-23), so no anchor has been released yet. Change the base to `auspex.bot` now, before one is. Released anchors are permanent (`Setup/providers.json`) |
| 7 | Prose | `CLAUDE.md`, README, `docs/`, live plans, `todo.md`, `View/` docs | `project/archive/`, the CHANGELOG's past entries, and learnings that record what was true then all keep "proctor" and are allow-listed |
| 8 | The gate | `project/gate/` | Gate "proctor" as `old-words.sh` gates the old model's words. The allow-list covers the archive, past CHANGELOG entries, `learnings/name-collisions.md`, and captured fixtures holding absolute paths such as `/home/joseph/repos/proctor/.proctor/work/`, which are records |
| 9 | The migration guide | new `docs/migrating-from-proctor.md` | For anyone with exams written for proctor. A table from old name to new for each thing that changed: the binary, every `PROCTOR_*` variable, `proctor.json`, `.proctor/`, the skill, and names inside exam, variant and hook files. A `sed` one-liner that does the variables and the file names. What does not change: the bank, which auspex reads as it is. The refusals in layers 3 and 4 link to it |
| 10 | Version | `0.3.6` | → `0.4.0`, with a CHANGELOG entry listing each breaking rename and linking the guide |

**Collision check for the new name** (the same check as
`learnings/name-collisions.md`, add it there): `Auspex` is free on
nuget.org (2026-11-23), unlike `Proctor`, so a dotnet tool could use the
bare id. PyPI and npm have an `auspex`, and the GitHub user `auspex`
exists; none of these matter for a .NET tool. The domain is `auspex.bot`.

## Stage 2b: nb, after the rename has landed

nb names proctor in comments, plans and one string that a model reads.
Change nb after proctor, so the string never points at a tool that does
not exist yet under the new name.

- **The refusal for a retired harness** (`nb.Core/Harness/HarnessRegistry.cs`)
  says *"a proctor arm with "agent": …"*. "Arm" is one of proctor's own
  old words as well, so the string becomes *"an auspex variant with
  "agent": …"*. `nb.Tests/HarnessDefaultTests.cs` and `evals/run.sh`
  assert it word for word, so all three change in one commit, and
  `evals/run.sh --skip-llm` runs before calling it done.
- `nb.Tests/ProctorContractTests.cs` → `AuspexContractTests.cs`.
- `evals/run.sh` points at `../proctor/Proctor.Tests/RunnerTests.cs`;
  that becomes `../auspex/Auspex.Tests/RunnerTests.cs`.
- Comments in `AtifWriter`, `AcpServer`, `Nb.cs` and `Program.cs`, plus
  nb's docs and plans. nb's `bugs/` is a read-only archive and keeps
  "proctor".

## Not in this plan

The full fold: one binary, `auspex view` as a verb in place of
`auspex-view`, and the viewer's `Bank.cs` replaced by proctor's layout
code. It happens when `bank.md` delivers what the viewer's design waits
on, as decided on 2026-11-21. This plan only makes it cheaper: by then
the two programs share a repository, a solution and a name.

## Decided (the user, 2026-11-23)

1. **The viewer moves in squashed.** There is no reason to keep its
   history. Its tree is still read before it becomes public.
2. **The viewer is `auspex-view` until the fold.** The user was
   ambivalent and wanted it done fast. A separate binary needs no code,
   whereas an `auspex view` verb would have the console exe host a web
   project. The command changes once, at the fold.
3. **No fallback for the old names; a migration guide instead.** The
   user was happy with either. The guide is cheaper than keeping both
   names working, and it leaves one name in use. Refusals point to it.

## Schedule (to 2026-11-29)

It can be messy in between. What matters is a working auspex at the end.

| Day | Work |
|---|---|
| 1 (11-24) | Before either stage: `exam-variant-attempt` merged or the only branch, a clean tree. Stage 1: the viewer's tree read, then moved in, `datasets/` moved out, both suites green |
| 2 (11-25) | Settle: viewer work happens here. Collision check written. GitHub: viewer remote renamed and archived, proctor renamed |
| 3–4 (11-26, 11-27) | Stage 2, layers 2 to 6 (code, variables, files on disk, skill, site URL), each with the suites green |
| 5 (11-28) | Layers 7 to 10: prose, the gate, the guide, the version |
| 6 (11-29) | Stage 2b in nb (unit suite and `evals/run.sh --skip-llm`). Spare time for whatever broke |
