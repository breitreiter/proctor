using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>What setup writes into suites/ and what <c>setup --check</c> reads back. The wizard's prompts are driven by hand (a pty); these cover everything under them.</summary>
public class SetupFilesTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("proctor-setup-").FullName;
    readonly string env;
    readonly string suites;
    // Process-wide: a key name of this test's own, so parallel tests cannot collide.
    readonly string keyVar = "PROCTOR_TEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_API_KEY";

    public SetupFilesTests()
    {
        suites = Path.Combine(root, "suites");
        env = Path.Combine(root, "profile", "env");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(keyVar, null);
        Directory.Delete(root, recursive: true);
    }

    static SetupProvider Provider(string name, string kind, string? env, string check = SetupProvider.CheckOk, Dictionary<string, string>? values = null) =>
        new(name, kind, env, check, new DateOnly(2026, 11, 8), check == SetupProvider.CheckOk ? ["m1", "m2"] : null, Values: values);

    JsonObject Read(string file) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(suites, file)))!;

    // ── nb.json ──

    [Fact]
    public void NbEntry_HoldsTheKindsFields_AndTheKeyByName_AndNoModel()
    {
        var entry = SetupFiles.NbEntry(Provider("gemini", "gemini", "GEMINI_API_KEY"), Catalogue.Get("gemini"), new Dictionary<string, string>());
        Assert.Equal("""{"Name":"gemini","Provider":"Gemini","ApiKey":"${GEMINI_API_KEY}"}""", entry.ToJsonString());
    }

    [Fact]
    public void NbEntry_ForAzure_CarriesTheProvisionedDeployment()
    {
        var values = new Dictionary<string, string> { ["Endpoint"] = "https://x.openai.azure.com/", ["ChatDeploymentName"] = "o4-mini" };
        var entry = SetupFiles.NbEntry(Provider("az", "azure-openai", "AZ_API_KEY", values: values), Catalogue.Get("azure-openai"), values);
        Assert.Equal("""{"Name":"az","Provider":"AzureOpenAI","Endpoint":"https://x.openai.azure.com/","ApiKey":"${AZ_API_KEY}","ChatDeploymentName":"o4-mini"}""", entry.ToJsonString());
    }

    [Fact]
    public void NbEntry_ForAKeylessServer_HasNoApiKey() =>
        Assert.Null(SetupFiles.NbEntry(Provider("ollama", "openai-compatible", null), Catalogue.Get("openai-compatible"), new Dictionary<string, string>())["ApiKey"]);

    [Fact]
    public void PutNbEntry_OnAFreshDirectory_KeepsMockForThePlayground()
    {
        SetupFiles.PutNbEntry(suites, SetupFiles.NbEntry(Provider("gemini", "gemini", "GEMINI_API_KEY"), Catalogue.Get("gemini"), new Dictionary<string, string>()));
        Assert.Equal(["Mock", "gemini"], Read("nb.json")["ChatProviders"]!.AsArray().Select(e => (string?)e!["Name"]));
    }

    [Fact]
    public void PutNbEntry_ReplacesOnlyItsOwnEntry_AndLeavesTheRestAsWritten()
    {
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "nb.json"), """
            { "//": "mine", "Harness": "nb", "ChatProviders": [ { "Name": "local-qcoder", "Provider": "LocalLlm", "Model": "q" }, { "Name": "gemini", "Provider": "Gemini" } ] }
            """);
        SetupFiles.PutNbEntry(suites, SetupFiles.NbEntry(Provider("gemini", "gemini", "GEMINI_API_KEY"), Catalogue.Get("gemini"), new Dictionary<string, string>()));

        var nb = Read("nb.json");
        Assert.Equal("mine", (string?)nb["//"]);
        var entries = nb["ChatProviders"]!.AsArray();
        Assert.Equal("""{"Name":"local-qcoder","Provider":"LocalLlm","Model":"q"}""", entries[0]!.ToJsonString());
        Assert.Equal("${GEMINI_API_KEY}", (string?)entries[1]!["ApiKey"]);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ForeignNbEntries_AreTheOnesSetupDidNotWrite()
    {
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "nb.json"), """{ "ChatProviders": [ { "Name": "Mock" }, { "Name": "local-qcoder" }, { "Name": "gemini" } ] }""");
        var state = SetupState.Empty() with { Providers = [Provider("gemini", "gemini", "GEMINI_API_KEY")] };
        Assert.Equal(["local-qcoder"], SetupFiles.ForeignNbEntries(suites, state));
    }

    [Fact]
    public void AnUnreadableConfig_IsRefused_NotOverwritten()
    {
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "nb.json"), "{ not json");
        Assert.Contains("fix it by hand", Assert.Throws<ProctorException>(() =>
            SetupFiles.PutNbEntry(suites, new JsonObject { ["Name"] = "x" })).Message);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(suites, "nb.json")));
    }

    // ── proctor.json and .gitignore ──

    [Fact]
    public void PutAgent_WritesTheRoute_AndKeepsTheRestOfProctorJson()
    {
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "proctor.json"), """{ "nb": { "path": "/opt/nb" }, "judges": { "glm": { "kind": "chat" } } }""");
        SetupFiles.PutAgent(suites, "claude-code", "https://api.anthropic.com", "ANTHROPIC_API_KEY");
        SetupFiles.PutEngine(suites, "podman");

        var config = Read("proctor.json");
        Assert.Equal("/opt/nb", (string?)config["nb"]!["path"]);
        Assert.NotNull(config["judges"]!["glm"]);
        Assert.Equal("""{"base_url":"https://api.anthropic.com","api_key":"${ANTHROPIC_API_KEY}"}""", config["agents"]!["claude-code"]!.ToJsonString());
        Assert.Equal("podman", (string?)config["harbor"]!["engine"]);

        SetupFiles.RemoveAgent(suites, "claude-code");
        Assert.Null(Read("proctor.json")["agents"]!["claude-code"]);
    }

    [Fact]
    public void EnsureProctor_CreatesOneWithoutTheExamplesGateway()
    {
        SetupFiles.EnsureProctor(suites);
        Assert.Equal("""{"nb":{"config":"nb.json"}}""", Read("proctor.json").ToJsonString());
    }

    [Fact]
    public void EnsureGitIgnore_AddsWhatIsMissing_Once()
    {
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, ".gitignore"), "/nb.json\n*.log\n");
        Assert.True(SetupFiles.EnsureGitIgnore(suites));
        Assert.False(SetupFiles.EnsureGitIgnore(suites));
        Assert.Equal(["/nb.json", "*.log", "proctor.json", "setup.json", "sandbox-plan.md"], File.ReadAllLines(Path.Combine(suites, ".gitignore")));
    }

    [Theory]
    [InlineData("ollama", "OLLAMA_API_KEY")]
    [InlineData("my-gateway.2", "MY_GATEWAY_2_API_KEY")]
    public void EnvName_IsTheProviderNameUppercased(string name, string expected) =>
        Assert.Equal(expected, SetupWizard.EnvName(name));

    // ── setup.json ──

    [Fact]
    public void State_RoundTrips_AndKeepsNoDerivedFields()
    {
        var state = SetupState.Empty() with { Providers = [Provider("gemini", "gemini", "GEMINI_API_KEY")] };
        state.Jobs["nb"] = new SetupJob(SetupJob.Set, ["gemini"]);
        state.Jobs["sandbox"] = new SetupJob(SetupJob.Set, Where: "local", Engine: "podman");
        state.Save(suites);
        var text = File.ReadAllText(SetupState.PathIn(suites));
        Assert.DoesNotContain("has_sandbox", text);

        var back = SetupState.Load(suites)!;
        Assert.True(back.HasSandbox);
        Assert.Equal(["m1", "m2"], back.Provider("gemini")!.Models);
        Assert.Equal(["nb"], back.JobsUsing("gemini"));
    }

    // ── setup --check ──

    async Task<(int Exit, string Output)> Check()
    {
        var output = new StringWriter();
        var exit = await SetupCheck.RunAsync(root, live: false, output, envPath: env);
        return (exit, output.ToString());
    }

    void WriteState(params SetupProvider[] providers)
    {
        var nbPath = typeof(SetupFilesTests).Assembly.Location;   // any file that exists stands in for nb
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "proctor.json"), new JsonObject { ["nb"] = new JsonObject { ["path"] = nbPath } }.ToJsonString());
        var state = SetupState.Empty() with { Providers = [.. providers] };
        foreach (var p in providers)
            SetupFiles.PutNbEntry(suites, SetupFiles.NbEntry(p, Catalogue.Get(p.Kind), p.Values ?? []));
        state.Jobs["nb"] = new SetupJob(SetupJob.Set, providers.Select(p => p.Name).ToList());
        state.Jobs["sandbox"] = new SetupJob(SetupJob.Later);
        state.Jobs["judge"] = new SetupJob(SetupJob.Set, [providers[0].Name]);
        state.Save(suites);
    }

    [Fact]
    public async Task Check_BeforeSetup_SaysToRunIt()
    {
        var (exit, output) = await Check();
        Assert.Equal(1, exit);
        Assert.Contains("problem: proctor setup has not been run here", output);
    }

    [Fact]
    public async Task Check_WhenEverythingIsThere_ReportsEachJob_AndNeverAValue()
    {
        EnvFile.Set(env, keyVar, "sk-secret-value-123");
        WriteState(Provider("gem", "gemini", keyVar));

        var (exit, output) = await Check();
        Assert.Equal(0, exit);
        Assert.Contains($"{keyVar} set (env file)", output);
        Assert.Contains($"provider gem: gemini, key {keyVar}, checked ok 2026-11-08, 2 models", output);
        Assert.Contains("job nb: ready (gem)", output);
        Assert.Contains("job sandbox: later", output);
        Assert.Contains("job claude-code: not asked yet", output);
        Assert.Contains("job judge: provider chosen (gem); no chat entry", output);
        Assert.Contains("missing: none", output);
        Assert.DoesNotContain("sk-secret-value-123", output);
    }

    [Fact]
    public async Task Check_AKeyThatIsNowhere_IsAProblem()
    {
        WriteState(Provider("gem", "gemini", keyVar));
        var (exit, output) = await Check();
        Assert.Equal(1, exit);
        Assert.Contains($"{keyVar} not set", output);
        Assert.Contains($"problem: {keyVar} is not set (not in the environment or {env})", output);
    }

    [Fact]
    public async Task Check_AKeyInTheEnvironment_Counts()
    {
        Environment.SetEnvironmentVariable(keyVar, "from-shell");
        WriteState(Provider("gem", "gemini", keyVar));
        var (_, output) = await Check();
        Assert.Contains($"{keyVar} set (environment)", output);
        Assert.DoesNotContain("from-shell", output);
    }

    [Fact]
    public async Task Check_AFailedKeyInUse_AndAnIncompleteServer_AreProblems()
    {
        EnvFile.Set(env, keyVar, "x");
        WriteState(Provider("gem", "gemini", keyVar, SetupProvider.CheckFailed) with { Words = "400 API key not valid (the key was refused)" },
            Provider("ollama", "openai-compatible", null, SetupProvider.CheckNotAvailable));

        var (exit, output) = await Check();
        Assert.Equal(1, exit);
        Assert.Contains("problem: provider gem: the last key check failed (400 API key not valid (the key was refused))", output);
        Assert.Contains("problem: provider ollama is incomplete: its entry in", output);
    }

    [Fact]
    public async Task Check_AJobWhoseEntryWasRemovedByHand_IsAProblem()
    {
        EnvFile.Set(env, keyVar, "x");
        WriteState(Provider("gem", "gemini", keyVar));
        SetupFiles.RemoveNbEntry(suites, "gem");
        var (exit, output) = await Check();
        Assert.Equal(1, exit);
        Assert.Contains("job nb names provider gem, but", output);
    }

    // ── the sandbox plan ──

    [Fact]
    public void SandboxPlan_SaysWhatWasSeen_AndWhichEngineSetupWrote()
    {
        var seen = new List<SandboxPlan.Observation> { new("OS", "Ubuntu 26.04, x64"), new("uv", "uv 0.9.4"), new("docker", "not found"), new("podman", "podman version 5.6.1") };
        Assert.Equal("podman", SandboxPlan.EngineFrom(seen));
        var plan = SandboxPlan.Render(new DateOnly(2026, 11, 8), seen, ["Claude Code"], "podman");
        Assert.Contains("| podman | podman version 5.6.1 |", plan);
        Assert.Contains("- Agent arms wanted: Claude Code.", plan);
        Assert.Contains("setup wrote `podman` here", plan);
        Assert.Contains("Here podman is present", plan);
        Assert.Contains("Ask before you change anything.", plan);
    }

    [Fact]
    public void SandboxPlan_WithNoEngine_WritesNone() =>
        Assert.Null(SandboxPlan.EngineFrom([new("docker", "not found"), new("podman", "not found")]));
}
