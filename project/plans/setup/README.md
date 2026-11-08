---
type: plan
title: proctor setup — mockups
created: 2026-11-08
status: mockups for review; nothing built
---

# proctor setup

Text mockups of the `proctor setup` wizard, one file per screen, in the
order a first run shows them. Spectre.Console's prompts are drawn as plain
text: `>` is the highlighted row of a selection prompt, `[x]` a checked row
of a multi-select, `****` a secret prompt.

## Rules, from day 1

1. **Setup is a planning aid, not a launcher.** Finishing it does not mean a
   suite can run. Its job is that a coding agent, reading
   `proctor setup --check`, has everything it needs to make a coherent plan:
   which providers exist, which jobs they can serve, what the user wants
   eventually, and what is missing.
2. **Intent is a first-class answer.** "I want this, not now" and "I want
   this, and proctor cannot do it yet" (hosted sandboxes) are recorded, not
   refused. Data gathering is allowed for its own sake.
3. **Jobs, not settings.** The wizard is a list of jobs (run nb, run Claude
   Code arms, judge, classify, sandbox, …). For each the user picks: use a
   provider already set up, add a provider, or skip for now. Each job says
   which class of provider it needs and offers only the providers that fit.
4. **The key never passes through an agent.** Keys are typed at a secret
   prompt and land in `~/.config/proctor/env` (mode 600); every config file
   holds `${VAR}`. `setup` refuses to run without a terminal and says so in
   words an agent will relay. `--check` never prints a value.
5. **Weird is by hand.** When a job's real-world setups vary more than a
   wizard can ask about (Codex against a llama-server, a corporate gateway),
   setup records the intent, says what file to edit, and points at the docs.
6. **A good citizen on Windows.** .NET users skew Windows. Every path setup
   prints is the one it will actually write, resolved for its platform
   (`C:\Users\Ana\AppData\Local\proctor\env`, `suites\nb.json`), never `~`
   or a forward slash to translate; every command it suggests has a Windows
   form or says it has none. `01-welcome.txt` shows both renderings; the other
   screens are drawn as on Linux and macOS to save space, and each carries the
   same rule.
7. **Spectre is for interactive verbs only.** `setup` uses it; `--check`
   prints plain lines, since an agent reads it; no other verb changes.
8. **The console brand mark is 🧙, the gender-neutral mage** (U+1F9D9 alone,
   no ZWJ gender sequence; Spectre's `:mage:`). It stands in for the wizard
   hat, which a terminal cannot draw. Shown only where Spectre reports Unicode
   support; a legacy Windows console gets plain `Proctor 0.9.6`. Whether the
   mage spreads into the rest of the brand is undecided: keep it to setup's
   banner until it is.
9. **Help lives on the site, not in the binary.** See the provider catalogue.

## Screens

| file | screen |
|---|---|
| `01-welcome.txt` | what setup is for, where it writes |
| `03-nb-model.txt` | job: the built-in harness; hands off to `providers/`, then what it writes |
| `04-sandbox.txt` | job: sandbox for agent arms; local (writes the agent's plan), hosted (intent), skip. Before the agent jobs, which are pointless without it |
| `05-claude-code.txt` | job: Claude Code arms; only Anthropic-dialect providers offered; with no sandbox, no provider questions |
| `06-codex.txt` | job: Codex arms; the by-hand path; gated on the sandbox likewise |
| `07-classifier.txt` | job: classifier checks (Jev); a decisions endpoint |
| `08-judge.txt` | job: LLM-as-judge; steered off-family |
| `09-summary.txt` | what was written, what is ready, what is intended |
| `10-check.txt` | `proctor setup --check`, the agent's view; and `setup` without a terminal |
| `11-rerun.txt` | running setup again: the jobs with their state, change one |
| `sandbox-plan.md` | mockup of `suites/sandbox-plan.md`, the brief setup writes for the agent that finishes a local sandbox |
| `providers/` | the one flow every job uses to add a provider: `pick.txt`, a form per kind, `check.txt` (the key check lists models; a live request only for Azure). Setup picks no models |

## The provider catalogue

Every provider kind has a fixed contract: nb and proctor already decide which
fields it takes, so setup needs no per-vendor screens. Proctor embeds one
catalogue, a JSON resource with one entry per kind, and adding a provider in
any job shows that kind's form. A new kind is a catalogue entry, not a screen.

```json
{
  "kind": "gemini",
  "label": "Gemini (Google AI Studio)",
  "blurb": "free tier, no card; good for getting started",
  "help": "providers.html#gemini",
  "where": "Google AI Studio → Get API key",
  "nb": { "Provider": "Gemini" },
  "fields": [
    { "name": "ApiKey", "secret": true, "required": true, "env": "GEMINI_API_KEY" }
  ],
  "models": "GET /v1beta/models",
  "serves": ["nb", "judge?"],
  "notice": "Free-tier requests may be used by Google to improve its models."
}
```

`serves` is the capability table below, kept in the catalogue so the table
in the docs is generated from it and cannot drift; `?` marks a pairing never
tried, offered as "untested". The form, per field: a secret prompt for a
secret (written to the env file under `env`, the config holding `${env}`),
a text prompt otherwise; optional fields are behind one "More options" row
(endpoint, headers). No kind asks for a model except Azure's two, whose
deployment is something the user provisioned and setup cannot list. `models`
is where the key check lists what the key can reach (`providers/check.txt`).

The contracts, from nb's providers and proctor's judge and agent blocks:

| kind | required | optional |
|---|---|---|
| Gemini | key, model | — |
| Anthropic | key, model | endpoint (a gateway), headers |
| OpenAI | key, model | endpoint, headers |
| OpenRouter | key, model (written as an OpenAI-compatible entry on its URL) | attribution headers |
| OpenAI-compatible | endpoint, model | key (Ollama takes none), context size |
| Azure OpenAI | endpoint (the resource URL), key, deployment name (not a model) | — |
| Azure AI Foundry | endpoint, key, model | — |

**Help URLs.** The binary carries one link per kind, to a page on the
project site, `https://breitreiter.github.io/proctor/providers.html#<kind>`,
plus the `where` words, which still help if the page is unreachable. The
page holds the vendors' own key-page links and any caveats; when a vendor
moves its console, the page changes and every released binary already
points at the fix. Rules:

- Never a vendor deep link in the binary: those are what rot.
- Never a domain or shortener of our own: if it lapsed, every old binary
  would print a URL a squatter could turn into a key-harvesting page. The
  github.io address costs nothing to keep and follows a renamed repository.
- A scheduled link check (lychee, weekly) over `docs/providers.html` catches
  a vendor's move before a user does.
- An anchor, once released, is never removed or renamed; a retired kind's
  section stays, saying so.

`docs/providers.html` is built with setup: spartan is fine, one section per
kind (where to get a key, what the free tier allows, what the provider may
do with the data), styled like `index.html`. It depends on Pages being on,
which is gated by `project/todo.md` "Do not enable Pages until the site's
promises are kept".

## The capability table the jobs filter on

Which provider kind can serve which job. ✓ known to work in proctor today;
? should work, never tried; — no. Every ? is checked before setup offers it
as ready rather than "untested".

| provider kind | nb runs | Claude Code | Codex | Qwen Code | judge (chat) | classifier (systemone) |
|---|---|---|---|---|---|---|
| Anthropic API | ✓ | ✓ | — | — | ? (Anthropic's OpenAI-compatible endpoint) | — |
| OpenAI API | ✓ | — | ✓ | — | ✓ | — |
| Gemini API | ✓ (nb's Gemini provider) | — | — | — | ? (Gemini's OpenAI-compatible endpoint) | — |
| OpenRouter | ✓ (OpenAI-compatible) | ? (its Anthropic-compatible endpoint) | by hand | ? | ✓ | ✓ (`/alpha/decisions`, Jev) |
| OpenAI-compatible (llama-server, Ollama, Groq, …) | ✓ | — | by hand | ✓ | ✓ | — |
| Azure OpenAI / Foundry | ✓ (nb's providers) | — | by hand | — | ? | — |
| a gateway of your own | by hand | by hand | by hand | by hand | by hand | by hand |

## What setup writes

- The env file: `~/.config/proctor/env` (or `$XDG_CONFIG_HOME/proctor/env`) on
  Linux and macOS, mode 600; `%LOCALAPPDATA%\proctor\env` on Windows, local
  rather than roaming so keys never sync to a profile server. `NAME=value`
  lines. Every verb loads it at startup; the real environment wins.
- `suites/proctor.json` and `suites/nb.json`: created from the examples when
  missing, then the entries each job needs, keys as `${VAR}`. Gitignored.
- `suites/setup.json`: the answers, including intent and skips, so a rerun
  starts where the last one ended and `--check` can report intent. Gitignored
  with the other two, since it names this machine's providers.
- `suites/sandbox-plan.md`, when the sandbox is to run on this machine: a
  brief for the user's coding agent (mockup: `sandbox-plan.md`). It holds
  what setup observed, what the user chose, what proctor requires, and how to
  proceed, never a fixed list of commands: routes differ by OS, distro,
  licence and what a work laptop allows, and resolving that takes a strong
  model talking to the user, not a static file. Gitignored too.

## Open questions

0. **Agent arms on Windows.** Harbor's support for a native Windows host is
   unverified, and `docs/podman.md` is Linux-only. Until it is checked, the
   sandbox job on Windows should say what is known (Docker Desktop with WSL2
   is the likely path) and record the intent rather than run Linux checks.

1. **Where providers live.** A provider used by three jobs is written into
   nb.json (nb runs), proctor.json `agents` (Claude Code) and proctor.json
   `judges`. Setup needs its own list (in `setup.json`) to know they are one
   provider; the generated entries then must not be hand-edited apart, or a
   rerun needs to notice. Alternative: proctor.json grows a `providers`
   block the other blocks reference by name. Bigger change, cleaner.
2. **A hand-edited config.** A rerun over files the user changed by hand:
   merge (only touch entries setup owns, marked how?), or refuse and print a
   diff. Proposed: setup owns entries it created, named `setup-*` or tracked
   in `setup.json`, and never edits another.
3. **This repository versus the user's.** In a fresh clone of proctor,
   `suites/` exists; in the user's own repository it may not. Proposed:
   setup works on `./suites/`, creating it, and says which directory it used.
4. **The check.** Listing models proves a key for free on every kind but
   Azure, which needs one live request to the named deployment, its cost
   stated first (`providers/check.txt`).
5. **Providers without a model.** Setup writes nb.json entries with no
   `Model`, since the model is the suite's. Today's program templates
   (`suites/*/program.nb`) never say `model {{model}}`; they take the entry's
   model. Check that nb accepts an entry without `Model`, and have templates
   (and the suite-authoring skill) put `model {{model}}` under `provider`, the
   arm's `model` then being required for nb arms. That is also where a dated
   id versus an alias (`gemini-flash-lite-latest`) gets decided: on the arm,
   where the report records it.
6. **Stale model lists.** `setup.json` keeps the ids a key could reach on the
   day of the check. `--check --live` refreshes them; whoever plans a suite
   should treat the list as a hint, not a guarantee.
