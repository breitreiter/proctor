using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// <c>proctor setup --check</c>: what is set up, read by a coding agent, so plain lines and no Spectre. One line per
/// provider and per job; anything the agent should fix is a line starting <c>problem:</c> and makes the exit 1. It
/// never prints a value, and calls no provider unless <c>--live</c>, which repeats each key check and records the
/// result in setup.json.
/// </summary>
static class SetupCheck
{
    public static async Task<int> RunAsync(string root, bool live, TextWriter output, HttpClient? http = null, string? envPath = null)
    {
        var suitesDir = Path.Combine(root, Layout.SuitesDir);
        envPath ??= EnvFile.DefaultPath();
        var problems = new List<string>();
        var state = SetupState.Load(suitesDir);
        if (state is null)
        {
            output.WriteLine($"setup: not run ({Shown(SetupState.PathIn(suitesDir))} does not exist)");
            output.WriteLine("problem: proctor setup has not been run here; ask the user to run `proctor setup` at a terminal");
            return 1;
        }

        var proctorPath = Path.Combine(suitesDir, Layout.ProctorConfigFile);
        var nbPath = Path.Combine(suitesDir, SetupFiles.NbFile);
        output.WriteLine($"setup: {Shown(SetupState.PathIn(suitesDir))} ({state.Updated.ToLocalTime():yyyy-MM-dd HH:mm}), "
            + string.Join(", ", new[] { proctorPath, nbPath }.Select(p => File.Exists(p) ? Shown(p) : $"{Shown(p)} missing")));

        var inFile = EnvFile.Names(envPath);
        var envs = state.Providers.Select(p => p.Env).OfType<string>().Distinct().ToList();
        output.WriteLine($"env: {envPath}{EnvMode(envPath)}{(envs.Count == 0 ? "" : "; " + string.Join(", ", envs.Select(e => $"{e} {KeySource(e, inFile) ?? "not set"}")))}");
        foreach (var env in envs.Where(e => KeySource(e, inFile) is null))
            problems.Add($"{env} is not set (not in the environment or {envPath}); ask the user to run `proctor setup` and enter the key again");

        output.WriteLine(NbLine(root, suitesDir, problems));

        foreach (var provider in state.Providers.ToList())
        {
            var p = provider;
            var kind = Catalogue.Get(p.Kind);
            if (live && (p.Env is null || KeySource(p.Env, inFile) is not null))
            {
                var endpoint = kind.CompletedByAgent ? NbEntryValue(nbPath, p.Name, "Endpoint") : null;
                var result = await KeyCheck.RunAsync(kind, p.Env is null ? null : Environment.GetEnvironmentVariable(p.Env), endpoint, http);
                p = Recorded(p, result);
                state.Providers[state.Providers.IndexOf(provider)] = p;
            }
            output.WriteLine($"provider {p.Name}: {p.Kind}, key {p.Env ?? "none"}, {ProviderStatus(p)}");
            if (kind.CompletedByAgent && NbEntryValue(nbPath, p.Name, "Endpoint") is null && state.JobsUsing(p.Name).Count > 0)
                problems.Add($"provider {p.Name} is incomplete: its entry in {Shown(nbPath)} has no Endpoint (and nb's LocalLlm needs a Model); write them from the server's own docs");
            if (p.Check == SetupProvider.CheckFailed && state.JobsUsing(p.Name).Count > 0)
                problems.Add($"provider {p.Name}: the last key check failed ({p.Words}); ask the user to run `proctor setup` and change the key");
        }
        if (live) state.Save(suitesDir);

        var config = ReadConfig(proctorPath);
        foreach (var (id, _) in SetupState.JobOrder)
            output.WriteLine($"job {id}: {JobLine(id, state, suitesDir, config, problems)}");
        if (state.Job("sandbox") is { Where: "hosted", Engine: not HarborConfig.Modal } hosted)
            output.WriteLine($"want: hosted sandbox ({hosted.Service ?? "not sure which"}), not supported yet");

        foreach (var problem in problems) output.WriteLine($"problem: {problem}");
        if (problems.Count == 0) output.WriteLine("missing: none for the jobs marked ready");
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>A provider after a key check: the models and the date on success, the provider's words on failure.</summary>
    public static SetupProvider Recorded(SetupProvider p, KeyCheckResult result) => result.Status switch
    {
        KeyCheckStatus.Ok => p with { Check = SetupProvider.CheckOk, Checked = DateOnly.FromDateTime(DateTime.Now), Models = result.Models, Words = null },
        KeyCheckStatus.Failed => p with { Check = SetupProvider.CheckFailed, Checked = DateOnly.FromDateTime(DateTime.Now), Words = $"{result.Words} ({result.Reading})" },
        _ => p with { Check = SetupProvider.CheckNotAvailable, Words = result.Reading },
    };

    /// <summary>Where a key is set: the env file or the environment (which wins). Never its value.</summary>
    public static string? KeySource(string env, HashSet<string> inFile) =>
        inFile.Contains(env) ? "set (env file)" : Environment.GetEnvironmentVariable(env) is { Length: > 0 } ? "set (environment)" : null;

    /// <summary>A repository path as the user would type it here: relative to the current directory, the platform's separators.</summary>
    public static string Shown(string path)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return relative.StartsWith("..") ? path : relative;
    }

    static string ProviderStatus(SetupProvider p) => p.Check switch
    {
        SetupProvider.CheckOk => $"checked ok {p.Checked:yyyy-MM-dd}, {p.Models?.Count ?? 0} models (suites/setup.json)",
        SetupProvider.CheckFailed => $"check failed {p.Checked:yyyy-MM-dd}: {p.Words}",
        SetupProvider.CheckNotAvailable => $"not checked: {p.Words}",
        _ => "unchecked (kept without a passing check)",
    };

    static string JobLine(string id, SetupState state, string suitesDir, JsonObject? config, List<string> problems)
    {
        var job = state.Job(id);
        if (job is null) return "not asked yet";
        if (job.State == SetupJob.Later) return "later";
        var providers = string.Join(", ", job.Providers ?? []);
        switch (id)
        {
            case "sandbox":
                if (job.Engine == HarborConfig.Modal)
                {
                    if (HarborRun.ModalProblem(Environment.GetEnvironmentVariable, File.Exists) is { } login)
                        problems.Add($"{login} (the user runs it: it opens their browser)");
                    if (Runner.FindOnPath("uv") is null) problems.Add("sandbox is Modal, but uv is not installed; proctor runs Harbor with it");
                    if ((string?)config?["harbor"]?["engine"] is not HarborConfig.Modal)
                        problems.Add("sandbox is Modal, but suites/proctor.json harbor.engine is not modal; ask the user to rerun `proctor setup`, or pass a Modal machine file with --config (docs/modal.md)");
                    return "Modal (experimental): agent cells run as sandboxes on the user's Modal account; their models must be public endpoints and fixtures with image.services cannot run there (docs/modal.md)";
                }
                if (job.Where == "hosted") return $"wanted: hosted ({job.Service ?? "not sure which"}), not supported yet";
                var engine = (string?)config?["harbor"]?["engine"] ?? "docker";
                var uv = Runner.FindOnPath("uv");
                var plan = Path.Combine(suitesDir, SetupFiles.SandboxPlan);
                return Runner.FindOnPath(engine) is not null && uv is not null
                    ? $"engine {engine} and uv found; Harbor itself is not proven until a suite's agent arm runs (or conformance)"
                    : $"planned: {engine} or uv not found yet" + (File.Exists(plan) ? $" ({Shown(plan)})" : "");
            case "nb":
                foreach (var name in job.Providers ?? [])
                    if (SetupFiles.NbEntryFields(suitesDir, name) is null)
                        problems.Add($"job nb names provider {name}, but {Shown(Path.Combine(suitesDir, SetupFiles.NbFile))} has no entry for it; ask the user to rerun `proctor setup`");
                return $"ready ({providers})";
            case "claude-code" or "codex":
                var written = config?["agents"]?[id] is JsonObject;
                if (job.State == SetupJob.ByHand)
                    return written ? $"by hand ({providers}), agents.{id} written" : $"by hand ({providers}): agents.{id} in suites/proctor.json is yours to write; the key is in the env file";
                if (!written) problems.Add($"job {id} names provider {providers}, but suites/proctor.json has no agents.{id}; ask the user to rerun `proctor setup`");
                var untested = job.Providers?.Any(p => state.Provider(p) is { } sp && Catalogue.Get(sp.Kind).Fit(id) == JobFit.Untested) == true;
                return $"ready ({providers}{(untested ? ", untested pairing" : "")}){(state.HasSandbox ? "" : "; no sandbox chosen")}";
            default:   // judge, classifier: the entry needs a model, which is the suite's choice, so setup writes none
                var kind = id == "classifier" ? JudgeDef.SystemOne : JudgeDef.Chat;
                var judges = (config?["judges"] as JsonObject ?? []).Where(j => (string?)j.Value?["kind"] == kind).Select(j => j.Key).ToList();
                return judges.Count > 0
                    ? $"ready ({providers}; judges: {string.Join(", ", judges)})"
                    : $"provider chosen ({providers}); no {kind} entry in suites/proctor.json judges yet: write one with a model from the provider's list";
        }
    }

    static string NbLine(string root, string suitesDir, List<string> problems)
    {
        var config = ReadConfig(Path.Combine(suitesDir, Layout.ProctorConfigFile));
        var path = (string?)config?["nb"]?["path"] ?? "nb";
        var found = path.Contains('/') || path.Contains('\\')
            ? (File.Exists(Path.GetFullPath(Path.Combine(suitesDir, path))) ? Path.GetFullPath(Path.Combine(suitesDir, path)) : null)
            : Runner.FindOnPath(path);
        if (found is null) problems.Add($"nb not found ('{path}', suites/proctor.json nb.path, else PATH); every suite needs it");
        return $"nb: {found ?? "not found"}";
    }

    static string EnvMode(string path)
    {
        if (!File.Exists(path)) return " (not written yet)";
        if (OperatingSystem.IsWindows()) return "";
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0 ? " (others can read it: chmod 600)" : " (mode 600)";
    }

    static string? NbEntryValue(string nbPath, string name, string field)
    {
        var root = ReadConfig(nbPath);
        return (root?["ChatProviders"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(e => (string?)e["Name"] == name)?[field] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
    }

    static JsonObject? ReadConfig(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
