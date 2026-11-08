using System.Text.RegularExpressions;
using Spectre.Console;

namespace Proctor;

/// <summary>
/// <c>proctor setup</c>, the interactive wizard: a list of jobs, each answered with a provider already set up, a new
/// provider, or "skip for now". Keys are typed at a secret prompt and go straight to the env file; every config entry
/// holds <c>${VAR}</c>. The screens are <c>project/plans/setup/</c>. Spectre is used here and nowhere else.
/// </summary>
static partial class SetupWizard
{
    const string AddProvider = "\0add", Skip = "\0skip", Keep = "\0keep", Back = "\0back", Done = "\0done", Gateway = "\0gateway";

    /// <summary>What each job needs of a provider, said above the provider list.</summary>
    static readonly Dictionary<string, string> Needs = new()
    {
        ["nb"] = "any provider nb supports",
        ["claude-code"] = "the Anthropic Messages API",
        ["codex"] = "the OpenAI Responses API",
        ["classifier"] = "a decisions (System One) endpoint",
        ["judge"] = "an OpenAI-dialect chat endpoint",
    };

    public static async Task<int> RunAsync(string root)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            Console.Error.WriteLine("proctor setup asks for API keys, so it needs a person at a terminal.");
            Console.Error.WriteLine("Ask the user to run `proctor setup` themselves. To see what is already set up, run `proctor setup --check`.");
            return 2;
        }
        var w = new Session(Path.Combine(root, Layout.SuitesDir), EnvFile.DefaultPath());
        var first = SetupState.Load(w.SuitesDir) is null;
        w.State = SetupState.Load(w.SuitesDir) ?? SetupState.Empty();
        Banner();

        if (first)
        {
            Welcome(w);
            if (!AnsiConsole.Confirm("Continue?")) return 0;
            Prepare(w);
            var n = 1;
            foreach (var (id, title) in SetupState.JobOrder)
            {
                await Job(w, id, $"{n++}. {title}");
                w.State.Save(w.SuitesDir);
            }
            Summary(w);
            return 0;
        }

        Prepare(w);
        var foreign = SetupFiles.ForeignNbEntries(w.SuitesDir, w.State);
        if (foreign.Count > 0)
            AnsiConsole.MarkupLine($"[grey]{Esc(SetupCheck.Shown(Path.Combine(w.SuitesDir, SetupFiles.NbFile)))} has entries setup did not write ({Esc(string.Join(", ", foreign))}). Setup leaves them alone.[/]\n");
        while (true)
        {
            var pick = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title($"Which job do you want to change?  [grey]last run {w.State.Updated.ToLocalTime():yyyy-MM-dd HH:mm}[/]")
                .PageSize(12)
                .AddChoices(SetupState.JobOrder.Select(j => j.Id).Append("providers").Append(Done))
                .UseConverter(c => c switch
                {
                    Done => "Done",
                    "providers" => $"{"Providers",-22} {w.State.Providers.Count,-11} add, re-check, change a key, remove",
                    _ => $"{Title(c),-22} {Esc(StateWord(w.State, c)),-11} {Esc(string.Join(", ", w.State.Job(c)?.Providers ?? []))}",
                }));
            if (pick == Done) break;
            if (pick == "providers") await Providers(w);
            else await Job(w, pick, Title(pick));
            w.State.Save(w.SuitesDir);
        }
        Summary(w);
        return 0;
    }

    /// <summary>What one run of the wizard has touched, for the summary.</summary>
    sealed class Session(string suitesDir, string envPath)
    {
        public string SuitesDir { get; } = suitesDir;
        public string EnvPath { get; } = envPath;
        public SetupState State { get; set; } = SetupState.Empty();
        public SortedDictionary<string, SortedSet<string>> Written { get; } = [];

        public void Wrote(string path, string what)
        {
            var shown = path == EnvPath ? path : SetupCheck.Shown(path);
            if (!Written.TryGetValue(shown, out var set)) Written[shown] = set = [];
            set.Add(what);
        }
    }

    // ── screens ──

    static void Banner()
    {
        var mark = AnsiConsole.Profile.Capabilities.Unicode ? "🧙 " : "";
        AnsiConsole.MarkupLine($"\n{mark}[bold]Proctor {Esc(Runner.ProctorVersion)}[/]\n");
    }

    static void Welcome(Session w)
    {
        AnsiConsole.WriteLine("Proctor is highly configurable. This wizard will walk you through initial setup. You can run it again later to fill in any sections you skip.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Setup will write files, some of which contain sensitive information:");
        var rows = new (string Path, string What)[]
        {
            (w.EnvPath, "your API keys (only you can read it)"),
            (SetupCheck.Shown(Path.Combine(w.SuitesDir, Layout.ProctorConfigFile)), "proctor's config for this machine"),
            (SetupCheck.Shown(Path.Combine(w.SuitesDir, SetupFiles.NbFile)), "AI providers for nb, proctor's built-in harness"),
            (SetupCheck.Shown(SetupState.PathIn(w.SuitesDir)), "your choices from this wizard, minus keys"),
        };
        Columns(rows.Select(r => new[] { r.Path, r.What }));
        AnsiConsole.WriteLine();
        var suites = SetupCheck.Shown(w.SuitesDir);
        AnsiConsole.WriteLine($"Setup creates {Path.Combine(suites, SetupFiles.GitIgnore)} which excludes setup's files.");
        AnsiConsole.WriteLine("API keys are only written to your user profile, never to this repository.");
        AnsiConsole.WriteLine();
    }

    static void Prepare(Session w)
    {
        if (SetupFiles.EnsureGitIgnore(w.SuitesDir)) w.Wrote(Path.Combine(w.SuitesDir, SetupFiles.GitIgnore), "setup's files excluded");
        var proctor = Path.Combine(w.SuitesDir, Layout.ProctorConfigFile);
        if (!File.Exists(proctor)) w.Wrote(SetupFiles.EnsureProctor(w.SuitesDir), "created: nb from PATH, nb.json beside it");
        var nb = Path.Combine(w.SuitesDir, SetupFiles.NbFile);
        if (!File.Exists(nb)) w.Wrote(SetupFiles.EnsureNb(w.SuitesDir), "created: nb's Mock provider, for the playground");
    }

    static async Task Job(Session w, string id, string heading)
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[bold]{Esc(heading)}[/]\n");
        switch (id)
        {
            case "nb": await Harness(w); break;
            case "sandbox": Sandbox(w); break;
            case "claude-code" or "codex": await Agent(w, id); break;
            default: await Judge(w, id); break;
        }
        AnsiConsole.WriteLine();
    }

    static async Task Harness(Session w)
    {
        AnsiConsole.WriteLine("Proctor includes a built-in, highly-configurable agent harness. This is how proctor tests agents that don't run in a standard coding harness, like chatbots or headless analysis jobs.");
        AnsiConsole.WriteLine();
        var job = w.State.Job("nb");
        var chosen = job?.Providers ?? [];
        if (chosen.Count == 0 && w.State.Providers.Count == 0) AnsiConsole.WriteLine("No AI providers are set up yet.\n");
        else if (chosen.Count > 0) AnsiConsole.MarkupLine($"Used now: [bold]{Esc(string.Join(", ", chosen))}[/]\n");

        while (true)
        {
            var name = await ChooseProvider(w, "nb", exclude: chosen, chosen.Count > 0 ? (Keep, "Done") : (Skip, "Skip for now"));
            if (name is null) break;
            var provider = w.State.Provider(name)!;
            var entry = SetupFiles.NbEntry(provider, Catalogue.Get(provider.Kind), provider.Values ?? []);
            w.Wrote(SetupFiles.PutNbEntry(w.SuitesDir, entry), $"provider \"{name}\"");
            chosen = [.. chosen, name];
            w.State.Jobs["nb"] = new SetupJob(SetupJob.Set, chosen);
            AnsiConsole.MarkupLine($"\n  Written: {Esc(SetupCheck.Shown(Path.Combine(w.SuitesDir, SetupFiles.NbFile)))}  provider \"{Esc(name)}\"{(Catalogue.Get(provider.Kind).CompletedByAgent ? " [grey](Endpoint and Model left for your agent)[/]" : "")}\n");
            AnsiConsole.WriteLine("Add another provider for the built-in harness? Suites name a provider per arm, so two providers let one suite compare them.");
        }
        if (chosen.Count == 0) w.State.Jobs["nb"] = new SetupJob(SetupJob.Later);
    }

    static void Sandbox(Session w)
    {
        AnsiConsole.WriteLine("Coding harnesses, like Claude Code and Codex, expect rich filesystem access and the ability to run arbitrary commands. Running these bare on your local machine creates three types of hazards:");
        AnsiConsole.WriteLine("  - Agents under test are intentionally stressed. They're often given vague, conflicting, or dangerous instructions to see how well they handle uncertainty. They may cause unwanted damage if run with your account permissions.");
        AnsiConsole.WriteLine("  - Agents under your account have access to the same files as you, including test descriptions and answer keys.");
        AnsiConsole.WriteLine("  - They also have access to your memories and preferences, which may skew their baseline behavior.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Proctor uses Harbor for sandboxing. Running Harbor on your local machine requires installing several packages and performing extra configuration. Setup will create a plan for your coding agent to wire everything up.");
        AnsiConsole.WriteLine();

        var pick = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("How would you prefer to run Harbor?")
            .AddChoices("local", "hosted", Skip)
            .UseConverter(c => c switch
            {
                "local" => $"{"On this machine",-30} [grey]Docker or podman, plus uv[/]",
                "hosted" => $"{"A hosted sandbox service",-30} [grey]Daytona, Modal, …: not supported yet[/]",
                _ => $"{"Skip for now",-30} [grey]You won't be able to measure Claude Code, Codex, etc[/]",
            }));

        if (pick == Skip) { w.State.Jobs["sandbox"] = new SetupJob(SetupJob.Later); return; }
        if (pick == "hosted")
        {
            var service = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Proctor cannot run agent arms on a hosted service yet. Which would you use?")
                .AddChoices("Daytona", "Modal", "E2B", "Runloop", "Another / not sure"));
            w.State.Jobs["sandbox"] = new SetupJob(SetupJob.Set, Where: "hosted", Service: service.StartsWith("Another") ? null : service.ToLowerInvariant());
            AnsiConsole.WriteLine($"\nRecorded as wanted: hosted sandbox ({service}). Until it is supported, agent arms need Docker or podman on this machine; nb runs need neither.");
            return;
        }

        var seen = AnsiConsole.Status().Start("Looking at what is installed ...", _ => SandboxPlan.Observe());
        var engine = SandboxPlan.EngineFrom(seen);
        if (engine is not null) w.Wrote(SetupFiles.PutEngine(w.SuitesDir, engine), $"harbor.engine {engine}");
        w.State.Jobs["sandbox"] = new SetupJob(SetupJob.Set, Where: "local", Engine: engine);
        var wanted = new[] { "claude-code", "codex" }.Where(a => w.State.Job(a) is { State: not SetupJob.Later }).Select(Title);
        var plan = Path.Combine(w.SuitesDir, SetupFiles.SandboxPlan);
        File.WriteAllText(plan, SandboxPlan.Render(DateOnly.FromDateTime(DateTime.Now), seen, wanted, engine));
        w.Wrote(plan, "the brief for your coding agent");

        AnsiConsole.WriteLine("\nSetup looked at what is installed and wrote a plan for your coding agent:");
        AnsiConsole.MarkupLine($"  [bold]{Esc(SetupCheck.Shown(plan))}[/]\n");
        AnsiConsole.WriteLine("Ask your agent to read it and help you finish the sandbox. Every machine (and every employer's rules about what you may install) is different, so the plan says what proctor needs and leaves the route to you and your agent.");
    }

    static async Task Agent(Session w, string id)
    {
        if (!w.State.HasSandbox)
        {
            AnsiConsole.MarkupLine($"[grey]Skipped: {Esc(Title(id))} runs only in a sandbox, and none is set up (step 2). Run setup again once one is.[/]");
            return;
        }
        if (id == "claude-code")
        {
            AnsiConsole.WriteLine("Proctor can drive Claude Code instances. This means you can evaluate how your tools and documentation behave under Anthropic's preferred harness.");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("For a number of reasons (Anthropic ToS, technical complexity) proctor does not use your Claude Code login. It instead requires an API key.");
        }
        else
        {
            AnsiConsole.WriteLine("Proctor can also drive OpenAI Codex instances.");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("As with Claude Code, you'll need to supply an API key to use Codex.");
        }
        AnsiConsole.WriteLine();

        var current = w.State.Job(id);
        var name = await ChooseProvider(w, id, exclude: [], current is { State: not SetupJob.Later } ? (Keep, $"Keep {string.Join(", ", current.Providers ?? [])}") : (Skip, $"Skip for now      [grey]You won't be able to measure {Title(id)}[/]"));
        if (name is null) { if (current is null) w.State.Jobs[id] = new SetupJob(SetupJob.Later); return; }

        var provider = w.State.Provider(name)!;
        var kind = Catalogue.Get(provider.Kind);
        var fit = kind.Fit(id);
        if (fit is (JobFit.Yes or JobFit.Untested) && kind.Agents?.GetValueOrDefault(id) is { } baseUrl && provider.Env is not null)
        {
            w.Wrote(SetupFiles.PutAgent(w.SuitesDir, id, baseUrl, provider.Env), $"agents.{id}");
            w.State.Jobs[id] = new SetupJob(SetupJob.Set, [name]);
            AnsiConsole.MarkupLine($"\n  Written: suites/proctor.json  agents.{id} → {Esc(baseUrl)}{(fit == JobFit.Untested ? " [yellow](untested pairing: the first shakedown run is the check)[/]" : "")}");
            return;
        }
        SetupFiles.RemoveAgent(w.SuitesDir, id);
        w.State.Jobs[id] = new SetupJob(SetupJob.ByHand, [name]);
        AnsiConsole.WriteLine($"\nRecorded: {Title(id)} on {name}, by hand. Setup has the key; ask your coding agent to write agents.{id} in suites/proctor.json (base_url, and api_key as ${{{provider.Env ?? "VAR"}}}). See docs/configuration.md, \"Agent arms\".");
    }

    static async Task Judge(Session w, string id)
    {
        if (id == "classifier")
        {
            AnsiConsole.WriteLine("Proctor supports classifier checks. These checks use a lightweight AI judge to answer questions about a run (\"does the answer claim the work is finished?\").");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("These checks are usually significantly faster and cheaper than asking a full chat AI. However, they require specialized models, like Jev or Clef.");
        }
        else
        {
            AnsiConsole.WriteLine("Proctor supports LLM-as-judge checks. These checks use a chat model judge to review part of a run and answer a question with evidence from the transcript.");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("For best results, we recommend:");
            AnsiConsole.WriteLine("  - Selecting a provider that offers a model you're not already measuring. For example, if you're only measuring Claude, use an OpenAI model as a judge.");
            AnsiConsole.WriteLine("  - Using a capable, frontier model as a judge");
        }
        AnsiConsole.WriteLine();

        var current = w.State.Job(id);
        var noun = id == "classifier" ? "classifier checks" : "LLM-as-judge checks";
        var name = await ChooseProvider(w, id, exclude: [], current is { State: not SetupJob.Later } ? (Keep, $"Keep {string.Join(", ", current.Providers ?? [])}") : (Skip, $"Skip for now      [grey]You won't be able to create {noun}[/]"));
        if (name is null) { if (current is null) w.State.Jobs[id] = new SetupJob(SetupJob.Later); return; }
        w.State.Jobs[id] = new SetupJob(SetupJob.Set, [name]);
        AnsiConsole.WriteLine($"\nRecorded: {noun} on {name}. A judge entry names its model, which is the suite's choice, so setup writes none: your coding agent adds it to suites/proctor.json \"judges\" with a model from the list `proctor setup --check` reports.");
    }

    /// <summary>Runs the key check again, changes a key, or removes a provider no job uses.</summary>
    static async Task Providers(Session w)
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        var pick = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[bold]Providers[/]")
            .AddChoices(w.State.Providers.Select(p => p.Name).Append(AddProvider).Append(Back))
            .UseConverter(c => c switch
            {
                AddProvider => "Add a provider",
                Back => "Back",
                _ => $"{Esc(c),-14} {Esc(Catalogue.Get(w.State.Provider(c)!.Kind).Label),-28} [grey]{Esc(CheckWord(w.State.Provider(c)!))}[/]",
            }));
        if (pick == Back) return;
        if (pick == AddProvider) { await NewProvider(w, null); return; }

        var provider = w.State.Provider(pick)!;
        var usedBy = w.State.JobsUsing(pick);
        var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title($"[bold]{Esc(pick)}[/]")
            .AddChoices("check", "key", "remove", Back)
            .UseConverter(c => c switch
            {
                "check" => "Check again",
                "key" => "Change the key",
                "remove" => usedBy.Count == 0 ? "Remove" : $"Remove               [grey]used by: {Esc(string.Join(", ", usedBy.Select(Title)))}[/]",
                _ => "Back",
            }));
        var kind = Catalogue.Get(provider.Kind);
        switch (action)
        {
            case "check":
                var key = provider.Env is null ? null : Environment.GetEnvironmentVariable(provider.Env);
                Replace(w, provider, await Check(kind, key, provider));
                break;
            case "key" when provider.Env is not null:
                var (newKey, _) = AskKey(w, kind, provider.Env);
                if (newKey is null) break;
                Replace(w, provider, await Check(kind, newKey, provider));
                break;
            case "remove":
                if (usedBy.Count > 0 && !AnsiConsole.Confirm($"{pick} is used by {string.Join(", ", usedBy.Select(Title))}. Remove it from those jobs too?", false)) break;
                foreach (var job in usedBy)
                {
                    var rest = w.State.Jobs[job].Providers!.Where(p => p != pick).ToList();
                    w.State.Jobs[job] = rest.Count > 0 ? w.State.Jobs[job] with { Providers = rest } : new SetupJob(SetupJob.Later);
                    if (job is "claude-code" or "codex") SetupFiles.RemoveAgent(w.SuitesDir, job);
                }
                SetupFiles.RemoveNbEntry(w.SuitesDir, pick);
                w.State.Providers.Remove(provider);
                AnsiConsole.MarkupLine(provider.Env is null ? $"Removed {Esc(pick)}." : $"Removed {Esc(pick)}. Its key stays in {Esc(w.EnvPath)} as {Esc(provider.Env)}; delete that line by hand if no longer needed.");
                break;
        }
    }

    // ── the provider flow (plans/setup/providers/) ──

    /// <summary>
    /// The provider list for a job: providers set up already that fit, then "Add a provider", then the job's way out
    /// (skip, keep or done). Returns the chosen or newly added provider's name, or null for the way out.
    /// </summary>
    static async Task<string?> ChooseProvider(Session w, string job, IReadOnlyCollection<string> exclude, (string Key, string Label) exit)
    {
        while (true)
        {
            var fitting = w.State.Providers.Where(p => !exclude.Contains(p.Name) && Catalogue.Get(p.Kind).Fit(job) != JobFit.No).ToList();
            var otherUse = w.State.Jobs.Where(j => j.Key is "nb" or "claude-code" or "codex").SelectMany(j => j.Value.Providers ?? []).ToHashSet();
            var pick = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .AddChoices(fitting.Select(p => p.Name).Append(AddProvider).Append(exit.Key))
                .UseConverter(c => c == AddProvider ? "Add a provider"
                    : c == exit.Key ? exit.Label
                    : $"{Esc(c),-14} {Esc(Catalogue.Get(w.State.Provider(c)!.Kind).Label),-28} [grey]{Esc(FitNote(w.State.Provider(c)!, job, otherUse))}[/]"));
            if (pick == exit.Key) return null;
            if (pick != AddProvider) return pick;
            if (await NewProvider(w, job) is { } added) return added;
        }
    }

    /// <summary>pick.txt, then the kind's form, then the key check. Returns the new provider's name, or null on Back.</summary>
    static async Task<string?> NewProvider(Session w, string? job)
    {
        var kinds = Catalogue.Kinds.Where(k => job is null || k.Fit(job) != JobFit.No).ToList();
        if (job is not null)
        {
            AnsiConsole.MarkupLine($"\n[bold]Which provider?[/]   [grey]for: {Esc(Title(job))}; needs: {Esc(Needs[job])}[/]");
            var cannot = Catalogue.Kinds.Where(k => k.Fit(job) == JobFit.No).Select(k => k.Label).ToList();
            if (cannot.Count > 0) AnsiConsole.MarkupLine($"[grey]can't serve this job: {Esc(string.Join(", ", cannot))}[/]");
        }
        var pick = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .PageSize(12)
            .AddChoices(kinds.Select(k => k.Kind).Append(Gateway).Append(Back))
            .UseConverter(c => c switch
            {
                Back => "Back",
                Gateway => $"{"Something else",-30} [grey]a gateway of your own: setup takes the key, your agent the rest[/]",
                _ => $"{Esc(Catalogue.Get(c).Label),-30} [grey]{Esc(KindNote(Catalogue.Get(c), job))}[/]",
            }));
        if (pick == Back) return null;
        var kind = Catalogue.Get(pick == Gateway ? "openai-compatible" : pick);
        var provider = await Form(w, kind);
        if (provider is null) return null;
        w.State.Providers.Add(provider);
        w.State.Save(w.SuitesDir);
        return provider.Name;
    }

    /// <summary>The kind's form, the same layout for every kind: where to get a key, the fields, the key check, the name.</summary>
    static async Task<SetupProvider?> Form(Session w, ProviderKind kind)
    {
        AnsiConsole.MarkupLine($"\n[bold]{Esc(kind.Label)}[/]\n");
        if (kind.CompletedByAgent)
            AnsiConsole.WriteLine("These servers differ in what they need (an endpoint, a context size, sometimes headers), so setup takes only the key, which is the one part that must not pass through your coding agent. Ask your agent to fill in the rest of the entry.\n");
        else
        {
            AnsiConsole.MarkupLine($"  Where to get a key: [link]{Esc(kind.HelpUrl)}[/]");
            AnsiConsole.MarkupLine($"                      [grey]({Esc(kind.Where)})[/]");
        }
        if (kind.Notice is not null) AnsiConsole.MarkupLine($"  [yellow]{Esc(kind.Notice)}[/]");
        AnsiConsole.WriteLine();

        var secret = kind.Fields.Single(f => f.Secret);
        var env = secret.Env;
        string? name = null;
        if (kind.CompletedByAgent || env is null || w.State.Providers.Any(p => p.Env == env))
        {
            name = AskName(w, kind);
            env = EnvName(name);
        }

        var values = new Dictionary<string, string>();
        foreach (var field in kind.Fields.Where(f => !f.Secret).Where(f => f.Name != "ChatDeploymentName" && f.Name != "Model"))
            values[field.Name] = AnsiConsole.Prompt(new TextPrompt<string>($"  {Esc(field.Prompt)}:"));

        var (key, saved) = AskKey(w, kind, env, required: secret.Required);
        if (saved is null && key is null && secret.Required) return null;
        var hasKey = key is { Length: > 0 };

        foreach (var field in kind.Fields.Where(f => f.Name is "ChatDeploymentName" or "Model"))
            values[field.Name] = AnsiConsole.Prompt(new TextPrompt<string>($"  {Esc(field.Prompt)}:"));

        var provisional = new SetupProvider(name ?? kind.Kind, kind.Kind, hasKey || saved is not null ? env : null, SetupProvider.CheckUnchecked, Values: values.Count > 0 ? values : null);
        SetupProvider checkedProvider;
        while (true)
        {
            checkedProvider = await Check(kind, key, provisional);
            if (checkedProvider.Check != SetupProvider.CheckFailed) break;
            var next = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .AddChoices("again", "keep", Back)
                .UseConverter(c => c switch { "again" => "Enter the key again", "keep" => "Keep it anyway (marked unchecked)", _ => "Back to the provider list" }));
            if (next == Back) return null;
            if (next == "keep") { checkedProvider = checkedProvider with { Check = SetupProvider.CheckUnchecked }; break; }
            (key, _) = AskKey(w, kind, env!, required: true);
            if (key is null) return null;
        }
        return checkedProvider with { Name = name ?? AskName(w, kind) };
    }

    /// <summary>
    /// The secret prompt; the key goes straight to the env file. When the environment already holds the variable (an
    /// export in the user's shell), that key can be used as it is, written nowhere. Returns the key and where it was saved.
    /// </summary>
    static (string? Key, string? Saved) AskKey(Session w, ProviderKind kind, string env, bool required = true)
    {
        var inFile = EnvFile.Names(w.EnvPath).Contains(env);
        if (!inFile && Environment.GetEnvironmentVariable(env) is { Length: > 0 } existing)
        {
            var use = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title($"  {Esc(env)} is already set in your environment.")
                .AddChoices("use", "enter")
                .UseConverter(c => c == "use" ? "Use it (setup writes no key)" : "Enter a key (saved to the env file; while your environment sets it, the environment's wins)"));
            if (use == "use") return (existing, "environment");
        }
        var prompt = new TextPrompt<string>($"  {Esc(kind.Fields.Single(f => f.Secret).Prompt)}:").Secret();
        if (!required) prompt.AllowEmpty();
        var key = AnsiConsole.Prompt(prompt).Trim();
        if (key.Length == 0) return (null, null);
        EnvFile.Set(w.EnvPath, env, key);
        Environment.SetEnvironmentVariable(env, key);
        w.Wrote(w.EnvPath, env);
        AnsiConsole.MarkupLine($"  [grey]Saved as {Esc(env)} in {Esc(w.EnvPath)}[/]");
        return (key, w.EnvPath);
    }

    static async Task<SetupProvider> Check(ProviderKind kind, string? key, SetupProvider provider)
    {
        var result = await AnsiConsole.Status().StartAsync("Checking the key ...", _ => KeyCheck.RunAsync(kind, key));
        switch (result.Status)
        {
            case KeyCheckStatus.Ok:
                AnsiConsole.MarkupLine($"  Checking the key ... [green]ok[/], {result.Models.Count} models available to this key");
                break;
            case KeyCheckStatus.Failed:
                AnsiConsole.MarkupLine($"  Checking the key ... [red]failed[/]");
                AnsiConsole.MarkupLine($"    {Esc(kind.Label)}: {Esc(result.Words ?? "")}");
                AnsiConsole.MarkupLine($"    [grey]({Esc(result.Reading ?? "")})[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"  [grey]Not checked: {Esc(result.Reading ?? "")}[/]");
                break;
        }
        return SetupCheck.Recorded(provider, result);
    }

    static string AskName(Session w, ProviderKind kind)
    {
        var foreign = SetupFiles.ForeignNbEntries(w.SuitesDir, w.State);
        var taken = w.State.Providers.Select(p => p.Name).Concat(foreign).Append("Mock").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggestion = kind.CompletedByAgent ? null : Enumerable.Range(1, 99).Select(i => i == 1 ? kind.Kind : $"{kind.Kind}-{i}").First(n => !taken.Contains(n));
        var prompt = new TextPrompt<string>("  Name for this provider in your suites:")
            .Validate(n => !NameRule().IsMatch(n) ? ValidationResult.Error("[red]lowercase letters, digits, '-' and '_' only[/]")
                : taken.Contains(n) ? ValidationResult.Error($"[red]{Esc(n)} is taken{(foreign.Contains(n) ? " by an entry in nb.json setup did not write" : "")}[/]")
                : ValidationResult.Success());
        if (suggestion is not null) prompt.DefaultValue(suggestion);
        return AnsiConsole.Prompt(prompt);
    }

    static void Replace(Session w, SetupProvider old, SetupProvider updated) =>
        w.State.Providers[w.State.Providers.IndexOf(old)] = updated;

    // ── words ──

    /// <summary>The env variable named from a provider name: uppercased, non-alphanumerics as <c>_</c>, <c>_API_KEY</c> after.</summary>
    internal static string EnvName(string providerName) =>
        Regex.Replace(providerName.ToUpperInvariant(), "[^A-Z0-9]", "_") + "_API_KEY";

    static string Title(string job) => job switch
    {
        "providers" => "Providers",
        _ => SetupState.JobOrder.First(j => j.Id == job).Title,
    };

    static string StateWord(SetupState state, string job) => state.Job(job) switch
    {
        null => "not asked",
        { State: SetupJob.Later } => "later",
        { State: SetupJob.ByHand } => "by hand",
        { Where: "local" } => "this machine",
        { Where: "hosted" } => "wanted",
        _ => "set",
    };

    static string CheckWord(SetupProvider p) => p.Check switch
    {
        SetupProvider.CheckOk => $"checked {p.Checked:yyyy-MM-dd}",
        SetupProvider.CheckFailed => "check failed",
        SetupProvider.CheckNotAvailable => "no key check",
        _ => "unchecked",
    };

    static string FitNote(SetupProvider p, string job, HashSet<string> otherUse) =>
        Catalogue.Get(p.Kind).Fit(job) switch
        {
            JobFit.Untested => "untested for this job",
            JobFit.ByHand => "by hand: your agent writes the config",
            _ when job == "judge" && otherUse.Contains(p.Name) => "also used for arms: a judge from another family is better",
            _ => "",
        };

    static string KindNote(ProviderKind kind, string? job) => job is null ? kind.Blurb ?? "" : kind.Fit(job) switch
    {
        JobFit.Untested => "untested for this job",
        JobFit.ByHand => "by hand: setup takes the key, your agent the rest",
        _ => kind.Blurb ?? "",
    };

    static void Summary(Session w)
    {
        AnsiConsole.Write(new Rule("Done").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("\n[bold]Providers[/]");
        if (w.State.Providers.Count == 0) AnsiConsole.MarkupLine("  [grey]none[/]");
        Columns(w.State.Providers.Select(p => new[]
            { p.Name, Catalogue.Get(p.Kind).Label, p.Check == SetupProvider.CheckOk ? $"{p.Models?.Count} models" : "", CheckWord(p), p.Env ?? "" }));
        AnsiConsole.MarkupLine("\n[bold]Jobs[/]");
        Columns(SetupState.JobOrder.Select(j =>
        {
            var job = w.State.Job(j.Id);
            var detail = job switch
            {
                { Where: "local" } => $"{job.Engine ?? "no engine yet"} on this machine; see {SetupCheck.Shown(Path.Combine(w.SuitesDir, SetupFiles.SandboxPlan))}",
                { Where: "hosted" } => $"{job.Service ?? "hosted"} (not supported yet)",
                _ => string.Join(", ", job?.Providers ?? []),
            };
            return new[] { j.Title, StateWord(w.State, j.Id), detail };
        }));
        if (w.Written.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Written[/]");
            Columns(w.Written.Select(kv => new[] { kv.Key, string.Join(", ", kv.Value) }));
        }
        AnsiConsole.MarkupLine("\n[bold]Next[/]");
        Columns([["proctor run playground", "no model, no key: your first report"], ["then see docs/getting-started.md", ""]]);
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Your coding agent can read all of this with `proctor setup --check`.");
        AnsiConsole.WriteLine("Run `proctor setup` again to change anything.");
    }

    /// <summary>Rows as aligned columns, indented, the last column grey; a long cell wraps within its column.</summary>
    static void Columns(IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;
        var width = list.Max(r => r.Length);
        var grid = new Grid();
        for (var i = 0; i < width; i++) grid.AddColumn(new GridColumn().PadLeft(i == 0 ? 2 : 0).PadRight(2));
        foreach (var row in list)
            grid.AddRow(row.Select((cell, i) => (Spectre.Console.Rendering.IRenderable)new Markup(i == width - 1 ? $"[grey]{Esc(cell)}[/]" : Esc(cell))).ToArray());
        AnsiConsole.Write(grid);
    }

    static string Esc(string s) => Markup.Escape(s);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex NameRule();
}
