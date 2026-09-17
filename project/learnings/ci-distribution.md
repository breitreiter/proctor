---
type: learnings
title: How other repos pull proctor and nb into GitHub Actions
created: 2026-09-17
---

# How other repos pull proctor and nb into GitHub Actions

A reading pass, staying on .NET, over how well-known .NET CLI tools ship and
how projects that run LLM evals in CI actually set them up. Raw findings with
URLs and full YAML in `prior-art/ci.md`. The question was the shape, not the
implementation, and the shape turned out to be nearly unanimous.

## Where nb is today

nb's CI publishes self-contained binaries per platform, with the provider
plugins copied to a `providers/` directory beside the executable, and uploads
them as a workflow artifact. It cuts no releases and publishes no NuGet
package. Its own evals run in CI through a bash script. A consumer today would
have to clone and build nb, which is the ceremony we want gone.

## The recommendation

**Primary: both tools as .NET local tools on nuget.org, pinned in the
consumer's tool manifest.** This is what GitVersion, ReportGenerator, Cake,
Nuke, dotnet-outdated and coverlet all do. The manifest gives reproducible
versions that Dependabot bumps, nuget.org needs no consumer auth, and the
GitHub-hosted Ubuntu runner already carries the .NET 10 SDK. A tool package
packs the publish output, so nb's `providers/` ships inside it as long as it
is in publish output, and resolves at runtime from the NuGet cache.

The consumer side is a manifest and a short job:

```json
{ "version": 1, "isRoot": true, "tools": {
  "proctor": { "version": "1.4.2", "commands": ["proctor"] },
  "nb":      { "version": "0.9.0", "commands": ["nb"] } } }
```

```yaml
on:
  pull_request: { paths: ['evals/**', 'src/**'] }
  push: { branches: [main] }
concurrency: { group: evals-${{ github.ref }}, cancel-in-progress: true }
jobs:
  evals:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with: { global-json-file: global.json }
      - run: dotnet tool restore
      - run: dotnet proctor run evals/ --out runs/ --summary "$GITHUB_STEP_SUMMARY"
        env: { ANTHROPIC_API_KEY: "${{ secrets.ANTHROPIC_API_KEY }}" }
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: eval-runs, path: runs/, retention-days: 14 }
```

A thin composite action can collapse that to one `uses:` line with inputs for
the evals directory, version and key. It is sugar; the manifest stays the
source of truth for versions, and the action stays thin.

**Fallback: self-contained single-file binaries in GitHub Releases** fetched
with `gh release download`, for runners without a .NET SDK. Untrimmed, with
`providers/` as loose files excluded from the single-file bundle.

**Rejected.** GitHub Packages as the NuGet feed, because it needs a token
even for public packages and the default job token cannot read across repos.
A Docker container action, which is Linux-only for no benefit. A custom
Microsoft.Testing.Platform test framework as the primary path: it is viable
later as a typed C# eval mode, where `dotnet test` runs evals and reports them
as tests, and TUnit is exactly that shape, but it is per-repo opt-in and
mixing it with VSTest projects is unsupported.

## Reporting into GitHub

The step summary takes markdown tables, up to one mebibyte per step and
silently dropped above that. The run directory goes up as an artifact with a
retention window. Annotations are for regressed cases only, since they cap at
ten per step. A sticky pull-request comment is optional, needs write
permission, and does not work on fork pull requests.

## Guardrails that the eval-in-CI projects converge on

- A path filter so evals run only when evals or source change.
- A label gate plus concurrency cancellation for expensive suites.
- A smoke subset on pull requests and the full suite on pushes to main or on
  a schedule. The `deterministic`/`judged` tag axis from
  `test-framework-patterns.md` is what makes this split cheap.
- A response cache keyed on the evals directory hash, tool version and model
  id, so an unchanged eval does not re-spend.
- A budget cap inside the tool, not just in the workflow.
- Fork pull requests get no secrets; skip them explicitly.
- A self-hosted runner for local models only for same-repo pull requests.

**Report, do not gate, by default.** Every project surveyed says the same
thing: a stochastic judged eval is not a required check. Gate only on
low-variance deterministic scorers with a stated tolerance, or on main and
nightly runs. This matches the brief's rule that a report states what it
could not have detected.

## Reproducibility

Pin the tool versions in the manifest, the exact model snapshot id in the eval
definition, and the evals directory's git sha, and write all three into the
run record and the summary header. This is the reproducibility block from the
report skeleton, fed by the manifest rather than by hand.

## .NET gotchas that will bite

- Providers must be in publish output, resolved via `AppContext.BaseDirectory`
  and never `Assembly.Location`, which is empty under single-file. Do not name
  a project folder `tools/`, which the SDK excludes.
- The plugin load context must fall through to the default context for
  `nb.Core` so types keep their identity, and use the dependency resolver for
  provider-private dependencies.
- Never trim or AOT the host. Dynamic assembly loading is a documented
  trimming incompatibility.
- .NET 10's RID-specific tool packages write a settings format older SDKs
  refuse. Plain framework-dependent packaging is simplest; document the SDK
  minimum.
- A tool package cannot depend on another tool package, so nb and proctor are
  separate manifest entries, or proctor embeds `nb.Core` and the providers.
  This is the process-boundary question from the brief showing up again from
  the packaging side.
- The runner picks the newest installed SDK unless `global.json` pins it. Pass
  the file to the setup action.
- The `dnx` one-shot runner prompts before downloading; in CI use the manifest
  and restore.

## What this adds to the design

A fourth decision joins the three in `prior-art.md`: proctor and nb ship as
nuget.org tool packages with a thin optional composite action, and nb needs a
release pipeline it does not have today. Two nb-side items follow, both
additive: a `PackAsTool` target that carries `providers/`, and a release
workflow that publishes to nuget.org. Neither changes nb's behaviour for
anyone building from source.
