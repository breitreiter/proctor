# Research: distributing proctor + nb to other repos' GitHub Actions jobs (.NET-native)

Date: 2026-09-17. Reading-only pass; sources linked inline.

## TL;DR recommendation

**Primary shape: .NET local tools pinned in the consumer's `.config/dotnet-tools.json`, published to nuget.org, wrapped by a thin composite action `joseph/proctor-action@v1` (optional sugar).**
The tool packages carry nb's `providers/` directory because `PackAsTool` packs the *publish* output, not just the main assembly. Consumers get reproducible versions from the manifest (Dependabot/Renovate both understand `dotnet-tools.json`), no auth (nuget.org), no PATH games (`dotnet tool run` / `dotnet proctor`), and the runner already has .NET 10 SDK.

**Fallback shape: self-contained single-file binaries in GitHub Releases + `gh release download`**, for consumers without a .NET SDK on the runner (or a wildly different SDK pin). Needs `PublishTrimmed=false` and providers shipped as loose files next to the exe (`ExcludeFromSingleFile`).

Reject: GitHub Packages NuGet feed (auth required even for public packages), Docker container action (slow, Linux-only, no benefit), MTP custom test framework as the *primary* path (worth it later as a "library reference" option, not for CI distribution).

---

## 1. .NET global / local tools

### Mechanics
- Add `<PackAsTool>true</PackAsTool>` + `<ToolCommandName>proctor</ToolCommandName>` to the csproj; `dotnet pack` produces `proctor.<ver>.nupkg` with `tools/net10.0/any/` containing **the full publish output** plus a generated `DotnetToolSettings.xml`. ([MS tutorial](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create); Nate McMaster's write-up notes explicitly that `PackAsTool` "gathers all publish output, not just the assembly you compile" — [natemcmaster.com](https://natemcmaster.com/blog/2018/05/12/dotnet-global-tools/).)
- **Plugin DLLs / runtime assets:** anything that lands in the publish directory is in the package. So nb's `providers/**` just needs to be in publish output: either `ProjectReference` to each provider with a post-publish copy target into `providers/`, or `<Content Include="providers/**" CopyToPublishDirectory="PreserveNewest" />`. At runtime the tool is executed from the NuGet cache (`~/.nuget/packages/nb/<ver>/tools/net10.0/any/`), so `AppContext.BaseDirectory/providers` resolves correctly. Known SDK wrinkle: files in a folder literally named `tools/` in the *project* aren't auto-included ([dotnet/sdk#8677](https://github.com/dotnet/sdk/issues/8677)); use a different folder name (`providers/` is fine).
- Native assets (if a provider ever P/Invokes): tool packages are framework-dependent by default and `runtimes/<rid>/native` in publish output is preserved, so it works, but .NET 10 now supports **RID-specific tool packages** (see below) if you ever need a per-platform build.

### .NET 10 changes that matter ([What's new in .NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk))
- `dotnet tool exec proctor@1.4.2 run ...` and the `dnx proctor ...` shim: one-shot execution, downloads to NuGet cache, honours a nearby `.config/dotnet-tools.json` for the version. Confirmation prompt appears in interactive terminals; in CI pass `--yes` (docs: [dotnet tool exec](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-exec)).
- `dotnet tool install proctor@1.4.2` (`@` syntax) and auto-creation of the manifest if none exists (`--create-manifest-if-needed` defaults on in .NET 10; [dotnet tool install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install)).
- **Platform-specific tools**: set `<RuntimeIdentifiers>linux-x64;win-x64;osx-arm64;any</RuntimeIdentifiers>`; `dotnet pack` emits a top-level manifest package plus `proctor.linux-x64.<ver>.nupkg` etc., and any publish option (`PublishSelfContained`, `PublishTrimmed`, `PublishAot`) applies to tools. The `any` RID keeps a framework-dependent fallback. Caveat: RID-specific packages write `DotnetToolSettings.xml` `Version="2"` with `Runner="executable"`, which **pre-.NET-10 SDKs refuse** ("Command uses unsupported runner 'executable'") — Andrew Lock's deep dive: [Supporting platform-specific .NET tools on old SDKs](https://andrewlock.net/exploring-dotnet-10-preview-features-8-supporting-platform-specific-dotnet-tools-on-old-sdks/). For proctor/nb (net10.0 only) this is irrelevant unless you want self-contained tools; plain `any`-style framework-dependent packaging is simplest and works everywhere the .NET 10 runtime is.
- `--allow-roll-forward` on install lets a net10.0 tool run on a newer runtime.

### Version pinning
`.config/dotnet-tools.json` in the consumer repo:
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "proctor": { "version": "1.4.2", "commands": ["proctor"] },
    "nb":      { "version": "0.9.0", "commands": ["nb"] }
  }
}
```
`dotnet tool restore` then `dotnet proctor ...` (or `dotnet tool run proctor`). Dependabot's `nuget` ecosystem and Renovate both bump this file, which is the reproducibility win over a floating action tag.

### Where to publish
| Feed | Consumer auth | Notes |
|---|---|---|
| **nuget.org** | none | Standard. Push with `dotnet nuget push --api-key ${{ secrets.NUGET_API_KEY }}` (or trusted publishing/OIDC, now supported on nuget.org). Package IDs are global; reserve a prefix. |
| GitHub Packages NuGet | **always required, even for public packages** ("You need an access token to publish, install, and delete private, internal, and public packages" — [GitHub docs](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)). `GITHUB_TOKEN` only works for packages whose linked repo granted access; cross-org consumers need a PAT with `read:packages`. | Adds a `dotnet nuget add source --username … --password ${{ secrets.GITHUB_TOKEN }} --store-password-in-clear-text` step to every consumer. Fine for a single org, painful otherwise. |
| nupkg as a GitHub Release asset | none, but consumer must `gh release download` then `dotnet tool install --add-source ./dir` | Works (`--add-source` accepts a folder), but there's no version discovery, and `dotnet tool restore` can't resolve it from the manifest without a nuget.config source. Only sensible as a stopgap. |

### What well-known tools do
- **GitVersion**: nuget.org tool `GitVersion.Tool` + a Node action `gittools/actions/gitversion/setup@v4` with `versionSpec: '6.x'` that installs the tool, then `.../execute` runs it ([GitTools/actions](https://github.com/GitTools/actions)). Two steps for the consumer.
- **ReportGenerator**: nuget.org tool `dotnet-reportgenerator-globaltool` + a Node action `danielpalme/ReportGenerator-GitHub-Action@5` that runs `dotnet tool install` into a `toolpath` (no version pin input; installs latest) ([action.yml](https://github.com/danielpalme/ReportGenerator-GitHub-Action/blob/master/action.yml)).
- **dotnet-format** shipped in the SDK; **Cake/Nuke**: local tools via manifest (`dotnet cake`, `dotnet nuke`) plus optional bootstrap scripts; **dotnet-outdated**, **coverlet.console**, **Verify.Tool**, **dotnet-ef**: plain nuget.org tools, docs say "dotnet tool install -g".
- Pattern: **nuget.org tool + optional Node/composite action wrapper that pins a version input**. Nobody serious uses GitHub Packages for public tools.

## 2. Composite actions / reusable workflows / container actions

- **Composite action** (`runs: using: composite`, steps with `shell: bash`; [docs](https://docs.github.com/en/actions/sharing-automations/creating-actions/creating-a-composite-action)): the right wrapper. It can call `actions/setup-dotnet`, run `dotnet tool install --tool-path`, run proctor, and write `$GITHUB_STEP_SUMMARY`. No build step, no `dist/` to commit (Node actions need a compiled bundle).
- **Reusable workflow** (`on: workflow_call`, consumer `jobs.evals.uses: joseph/proctor/.github/workflows/evals.yml@v1`, `secrets: inherit`; [docs](https://docs.github.com/en/actions/how-tos/sharing-automations/reuse-workflows)): controls the *whole job* (runner, permissions, concurrency, artifact upload). Good when you want to own the cost-guardrail logic (label gating, concurrency) centrally. Less flexible for consumers who want to build first, then eval in the same job.
- **Docker container action**: Linux-only, pulls/builds an image each run, and you'd still have to bake the .NET runtime in. No advantage here.
- **Versioning**: floating major tag `@v1` is the ecosystem convention (move the tag on each release), but GitHub's guidance is to pin to a 40-char SHA with a version comment; org policies can now *require* SHA pinning ([changelog 2025-08-15](https://github.blog/changelog/2025-08-15-github-actions-policy-now-supports-blocking-and-sha-pinning-actions/); [secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)). GitHub's **immutable releases** make a release tag + assets non-mutable, which also helps the Release-binaries fallback.
- Important: the action version and the *tool* version are separate things. Keep the action thin and take `version:` as an input that defaults to the manifest, so the manifest remains the single source of truth.

Consumer examples per pattern are in section 8.

## 3. Self-contained binaries in GitHub Releases

- `dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` per RID; upload to a Release; consumer runs `gh release download v1.4.2 -R joseph/proctor -p 'proctor-linux-x64.tar.gz'` (gh is preinstalled on hosted runners, `GH_TOKEN: ${{ github.token }}` suffices for public repos).
- **Plugin loading and single-file**: managed assemblies in the bundle are loaded from memory; `Assembly.Location` is `""` for bundled assemblies, so any code computing the providers dir from `Assembly.Location` breaks; use `AppContext.BaseDirectory` ([single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)). Plugins loaded with a custom `AssemblyLoadContext` + `LoadFromAssemblyPath` from a **loose** `providers/` directory next to the exe work: the ALC's `Load` override returns `null` for shared assemblies (nb.Core, Microsoft.Extensions.*) so they fall through to `AssemblyLoadContext.Default`, which resolves from the bundle. Use `AssemblyDependencyResolver` for the plugin's own private deps. Do **not** rely on `IncludeAllContentForSelfExtract` (legacy 3.1 mode, extracts to `$HOME/.net`, "not recommended", breaks under systemd without `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).
- To keep providers as loose files in a single-file publish: `<Content Include="providers/**" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />`.
- **Trimming is out** for a plugin host: "Trimming relies on seeing all assemblies at build time... Most plugin systems load third-party code dynamically, so it's not possible for the trimmer to identify what code is needed" ([known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)). Same for Native AOT. Self-contained, untrimmed single-file is ~70-90 MB per RID; fine for a release asset, ugly for a NuGet tool.
- So: yes, a plugin CLI can be single-file, provided the plugins stay outside the bundle and nothing is trimmed. Reflection inside providers (DI, JSON) is unaffected when untrimmed.

## 4. Library reference path (evals as C# code)

- Consumers add `<PackageReference Include="nb.Core" />` / `proctor.Sdk` and write evals in C#. Two ways to run:
  1. **`dotnet run --project evals/`** with a console host that calls into proctor. Zero test-platform ceremony; results are whatever proctor emits.
  2. **Microsoft.Testing.Platform custom test framework**: implement `ITestFramework` (`CreateTestSessionAsync`, `ExecuteRequestAsync` handling `DiscoverTestExecutionRequest`/`RunTestExecutionRequest`, publishing `TestNodeUpdateMessage`s with `Passed/Failed/SkippedTestNodeStateProperty`), register via `builder.RegisterTestFramework(capsFactory, frameworkFactory)`; MTP then gives you `--report-trx`, `--list-tests`, `--filter`, exit codes, IDE test explorer, and `dotnet test` in the .NET 10 MTP mode (enable with `global.json` `{"test": {"runner": "Microsoft.Testing.Platform"}}`; requires MTP >= 1.7) ([build a test framework](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-architecture-test-framework); [dotnet test modes](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)). Run-level files (the run directory) can be published as `SessionFileArtifact`; per-test attachments as `FileArtifactProperty`. Stable `TestNodeUid` is mandatory - map it to eval id + case id.
  - **TUnit** is exactly this: a pure-MTP framework (no `Microsoft.NET.Test.Sdk`), source-generated discovery, entry point generated by `Microsoft.Testing.Platform.MSBuild` ([MTP adoption blog](https://devblogs.microsoft.com/dotnet/mtp-adoption-frameworks/); [tunit.dev](https://tunit.dev/)). xunit v3 and MSTest also run natively on MTP.
  - Cheaper variant: don't write a framework; write evals as ordinary xunit v3/TUnit tests that call `nb.Core` and assert on scores. That gets `dotnet test` + TRX + test reporters for free, at the cost of proctor owning less of the run.
- Verdict: valuable as a *second* consumption mode (typed evals for .NET shops), but it couples consumers to nb.Core's API surface and forces them to build. For the "evals/ sidecar with minimal ceremony" goal, the CLI tool is the primary path; expose the MTP adapter later if demand exists.

## 5. Reporting results into GitHub

| Channel | Limits | Fit |
|---|---|---|
| **Job summary** `$GITHUB_STEP_SUMMARY` | GitHub-flavored Markdown incl. tables; **1 MiB per step**, 20 summaries shown per job; oversize summary is dropped silently ([workflow commands](https://docs.github.com/en/actions/writing-workflows/choosing-what-your-workflow-does/workflow-commands-for-github-actions); [dependency-review-action#786](https://github.com/actions/dependency-review-action/issues/786)) | **Best for the markdown eval report.** No permissions needed, works on fork PRs, links from the checks tab. Truncate/paginate above ~900 KB. |
| Annotations `::error file=..,line=..::` | 10 warnings + 10 errors per step, 50 per job ([community #26680](https://github.com/orgs/community/discussions/26680)) | Only for a handful of regressed cases pointing at `evals/**.yaml` lines. |
| Check run (Checks API, `checks: write`) | 50 annotations per request, 65535-char text | What `dorny/test-reporter` and `EnricoMi/publish-unit-test-result-action` do; **cannot run on fork PRs** without the `workflow_run` two-workflow dance ([dorny/test-reporter](https://github.com/dorny/test-reporter)). |
| PR comment (`pull-requests: write`) | 65536 chars body | What promptfoo and Braintrust do (create-or-update one sticky comment with a table + link). Nice for reviewers; needs write token, so same fork limitation. |
| Artifacts (`actions/upload-artifact@v4`) | 90-day default retention (1-400 configurable), storage quota per plan; individual artifacts up to several GB ([artifacts docs](https://docs.github.com/en/actions/tutorials/store-and-share-data)) | Upload the whole run directory (`runs/<id>/**`) with `retention-days: 14`; link it from the summary. |

Recommendation: summary (always) + artifact (always) + optional sticky PR comment behind an input, + `::error` annotations only for the regressed eval ids. Emit a TRX too so `dotnet test`-style reporters can consume it if the consumer wants.

## 6. Secrets, model access, cost guardrails

- API keys: repo/org secrets passed as env (`ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}`). Fork PRs get no secrets, so evals must `if:` out gracefully or run only on `pull_request` from the same repo / after a label.
- Local models: a self-hosted runner (the model box) with `runs-on: [self-hosted, gpu]`; GitHub warns against self-hosted runners on public repos because fork PRs can execute code on them ([security hardening](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions#hardening-for-self-hosted-runners)). Restrict with `if: github.event.pull_request.head.repo.full_name == github.repository` and require label. Point `nb` at the model box via a repo variable (`LLM_GATEWAY`), and have proctor call `swap-model` (or fail fast if the profile isn't loaded).
- Guardrails seen in the wild:
  - **promptfoo action**: `on: pull_request: paths: ['prompts/**']`, `actions/cache` of `~/.cache/promptfoo` so unchanged prompt/model pairs are not re-billed, posts sticky PR comment ([promptfoo GitHub Action docs](https://www.promptfoo.dev/docs/integrations/github-action/)).
  - **Braintrust**: smoke subset on PRs (`--first N`/`--sample N`, "about 20 high-signal cases"), full dataset on `push` to main + `schedule` for drift, `workflow_dispatch` for manual; "use blocking checks for criteria already accepted as release requirements and report-only checks for signals still being calibrated"; `terminate_on_failure` for infra errors ([Braintrust article](https://www.braintrust.dev/articles/llm-eval-pipeline-github-actions); [eval-action](https://github.com/braintrustdata/eval-action)).
  - Label gating: `if: contains(github.event.pull_request.labels.*.name, 'run-evals')` with `types: [labeled, synchronize]`; plus `concurrency: { group: evals-${{ github.ref }}, cancel-in-progress: true }` so a PR never has two paid runs in flight.
  - Budget caps belong in proctor itself (`--max-cost`, `--max-cases`, `--max-samples-per-case`), since the action can't meter tokens.
- Caching/skipping: hash `evals/**` + tool version + model id into a cache key; proctor should key its response cache the same way (`~/.cache/proctor`) and restore it with `actions/cache`. Skip entirely with `paths:` filters or `dorny/paths-filter`.

## 7. Reproducibility

- Pin three things and record them in the run output: tool versions (manifest), model id (exact snapshot id e.g. `claude-sonnet-4-5-20250929`, not an alias), eval definition (git sha of `evals/`). Proctor should write a `run.json` with `{proctor, nb, providers[], model, temperature, seed?, evals_sha, runner, started_at}` and print the same into the summary header.
- Gate or report? Consensus (promptfoo, Braintrust, Inspect users): **report on PRs, gate only on criteria that are already release requirements and have low variance** (exact-match/programmatic scorers, N>=3 samples, threshold with a tolerance band). Stochastic judge scores flake, so most teams make the evals job non-required, show delta vs. base branch, and let humans decide; blocking is usually reserved for `main`/nightly with a manually raised baseline. Implement as `--fail-on regression --tolerance 0.02` defaulting to off.
- Baselines: store the last `main` run's scores as an artifact or committed `evals/baseline.json`; PR runs diff against it.

## 8. Concrete consumer YAML

### Primary: local tool manifest + `dotnet tool restore` (12 lines of steps)
`.config/dotnet-tools.json` (above) plus:
```yaml
name: evals
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
(`setup-dotnet` is optional on `ubuntu-latest`, which preinstalls .NET 8/9/10 SDKs ([Ubuntu 24.04 image](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md)), but pin with `global.json` for reproducibility.)

### Primary, sugar variant: composite action (7 lines of steps)
```yaml
    steps:
      - uses: actions/checkout@v5
      - uses: joseph/proctor-action@v1   # or @<sha>
        with:
          evals: evals/
          version: 1.4.2                  # omit to use .config/dotnet-tools.json
          anthropic-api-key: ${{ secrets.ANTHROPIC_API_KEY }}
          comment: true                   # sticky PR comment; needs pull-requests: write
```
Action skeleton (`action.yml` in joseph/proctor-action):
```yaml
runs:
  using: composite
  steps:
    - uses: actions/setup-dotnet@v5
      with: { dotnet-version: 10.0.x }
    - shell: bash
      run: dotnet tool install proctor@${{ inputs.version }} --tool-path "$RUNNER_TEMP/proctor"
    - shell: bash
      env: { ANTHROPIC_API_KEY: "${{ inputs.anthropic-api-key }}" }
      run: "$RUNNER_TEMP/proctor/proctor" run "${{ inputs.evals }}" --out runs/ --summary "$GITHUB_STEP_SUMMARY"
    - uses: actions/upload-artifact@v4
      if: always()
      with: { name: eval-runs, path: runs/, retention-days: 14 }
```

### Fallback: release binaries (no .NET SDK needed)
```yaml
    steps:
      - uses: actions/checkout@v5
      - run: |
          gh release download v1.4.2 -R joseph/proctor -p 'proctor-linux-x64.tar.gz'
          tar -xzf proctor-linux-x64.tar.gz -C "$RUNNER_TEMP"
        env: { GH_TOKEN: "${{ github.token }}" }
      - run: "$RUNNER_TEMP/proctor/proctor" run evals/ --out runs/ --summary "$GITHUB_STEP_SUMMARY"
        env: { ANTHROPIC_API_KEY: "${{ secrets.ANTHROPIC_API_KEY }}" }
```
(the tarball is the self-contained single-file `proctor` + `nb` + loose `providers/`).

### Reusable-workflow alternative (owns the job; 4 lines for the consumer)
```yaml
jobs:
  evals:
    uses: joseph/proctor/.github/workflows/evals.yml@v1
    with: { evals: evals/, label: run-evals }
    secrets: inherit
```

## 9. .NET-specific gotchas (checklist)

1. **Plugin loading inside a tool package**: providers must be in *publish* output (`CopyToPublishDirectory`), not just build output; resolve the folder from `AppContext.BaseDirectory`, never `Assembly.Location` (empty under single-file). Plugin ALC must fall through to `Default` for `nb.Core` so types unify; otherwise `InvalidCastException` on the provider interface.
2. **Never trim or AOT the host** (plugin systems are documented trimming incompatibilities). Also skip `IncludeAllContentForSelfExtract`.
3. **Shared provider deps**: if a provider ships its own copy of an assembly the host also has (e.g. `System.Text.Json` version skew), ALC isolation is what you want; use `AssemblyDependencyResolver` with the provider's `.deps.json`, so publish providers with `GenerateDependencyFile=true`.
4. **RID-specific tool packages need .NET 10 SDK on the consumer** (`Runner="executable"`); framework-dependent `any` packages install on 8/9 SDKs too if you multi-target, but nb is net10.0-only anyway, so document "SDK 10.0.100+".
5. **`dotnet tool exec`/`dnx` prompts** before download in interactive terminals; in CI pass `--yes`, or prefer `dotnet tool restore` + manifest.
6. **GitHub Packages NuGet requires a token even for public packages**, and `GITHUB_TOKEN` can't read packages from other repos unless the package's linked repo grants access. Use nuget.org.
7. **Runner .NET version**: `ubuntu-latest` ships several SDKs and picks the latest unless `global.json` pins; put `global.json` with `"rollForward": "latestFeature"` in the consumer repo and pass `global-json-file` to `setup-dotnet` (plain `dotnet-version:` can be overridden by preinstalled versions if `global.json` conflicts). `runs-on: windows-latest` works identically for tool packages; release binaries need per-RID assets.
8. **Tool restore caching**: `actions/setup-dotnet` `cache: true` only caches from `packages.lock.json`; for tools, cache `~/.nuget/packages` keyed on `hashFiles('.config/dotnet-tools.json')`.
9. **NuGet `PackAsTool` cannot depend on other tool packages**: nb and proctor must be separate tools (or proctor embeds nb as a library + providers). If proctor shells out to nb, both go in the manifest.
10. **Native assets** in providers: include `runtimes/<rid>/native/**` in publish output; the tool host copies them, but under single-file they must be loose (`ExcludeFromSingleFile`) or use `IncludeNativeLibrariesForSelfExtract`.
11. **Job summary silently dropped above 1 MiB**; annotations capped at 10/step; PR comments at 65536 chars. Truncate the table and link the artifact.
12. **Fork PRs** get read-only tokens and no secrets: skip with `if: github.event.pull_request.head.repo.full_name == github.repository` and prefer the job summary over check runs/comments.
13. **`dotnet test` MTP mode** is opted in per-repo via `global.json` `test.runner`; mixing VSTest and MTP projects in one solution is unsupported. If proctor ever ships an MTP adapter, consumers must move all test projects or use `--project`.
14. **`--interactive` now defaults on in .NET 10 CLI** in interactive terminals; CI is non-interactive so no change, but scripts run under `script`/pty should pass `--interactive false`.

## Sources
- .NET 10 SDK what's new (tools, dnx, RID-specific, MTP in dotnet test): https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk
- dotnet tool install: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install
- dotnet tool exec: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-exec
- Create a .NET tool: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create
- Tool package = publish output (Nate McMaster): https://natemcmaster.com/blog/2018/05/12/dotnet-global-tools/
- dotnet/sdk#8677 (tools/ folder not packed): https://github.com/dotnet/sdk/issues/8677
- Andrew Lock, platform-specific tools on old SDKs: https://andrewlock.net/exploring-dotnet-10-preview-features-8-supporting-platform-specific-dotnet-tools-on-old-sdks/
- GitHub Packages NuGet registry: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry
- Single-file overview (API incompatibilities, ExcludeFromSingleFile, self-extract): https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Known trimming incompatibilities (plugins): https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities
- Build an MTP test framework: https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-architecture-test-framework
- dotnet test VSTest vs MTP mode: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test
- MTP adoption / TUnit: https://devblogs.microsoft.com/dotnet/mtp-adoption-frameworks/ , https://tunit.dev/
- Composite actions: https://docs.github.com/en/actions/sharing-automations/creating-actions/creating-a-composite-action
- Reusable workflows: https://docs.github.com/en/actions/how-tos/sharing-automations/reuse-workflows
- SHA pinning policy: https://github.blog/changelog/2025-08-15-github-actions-policy-now-supports-blocking-and-sha-pinning-actions/ , https://docs.github.com/en/actions/reference/security/secure-use
- Workflow commands / job summary: https://docs.github.com/en/actions/writing-workflows/choosing-what-your-workflow-does/workflow-commands-for-github-actions
- Job summary 1 MiB drop: https://github.com/actions/dependency-review-action/issues/786 ; annotation limits: https://github.com/orgs/community/discussions/26680
- Artifacts retention: https://docs.github.com/en/actions/tutorials/store-and-share-data
- dorny/test-reporter (checks API, fork limitation): https://github.com/dorny/test-reporter
- GitTools/actions: https://github.com/GitTools/actions ; ReportGenerator action: https://github.com/danielpalme/ReportGenerator-GitHub-Action/blob/master/action.yml
- promptfoo GitHub Action: https://www.promptfoo.dev/docs/integrations/github-action/
- Braintrust eval pipeline article: https://www.braintrust.dev/articles/llm-eval-pipeline-github-actions ; action: https://github.com/braintrustdata/eval-action
- Runner images (.NET SDKs preinstalled): https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md
- Self-hosted runner hardening: https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions#hardening-for-self-hosted-runners
