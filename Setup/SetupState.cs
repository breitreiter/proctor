using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proctor;

/// <summary>
/// One provider setup knows: its name in the suites, its catalogue kind, the env variable holding its key (none for a
/// keyless OpenAI-compatible server), and what the key check last said. <see cref="Models"/> are the ids the key could
/// reach on <see cref="Checked"/>: a hint for whoever plans a suite, never a choice setup made. <see cref="Values"/>
/// are the form's fields that are not secret (an Azure endpoint and deployment), so a later job can write the entry.
/// </summary>
record SetupProvider(string Name, string Kind, string? Env, string Check, DateOnly? Checked = null, List<string>? Models = null, string? Words = null, Dictionary<string, string>? Values = null)
{
    public const string CheckOk = "ok", CheckFailed = "failed", CheckUnchecked = "unchecked", CheckNotAvailable = "not available";
}

/// <summary>
/// The answer for one job. <see cref="State"/> is <c>set</c> (a provider, or for the sandbox a place), <c>later</c>
/// (skipped for now) or <c>by-hand</c> (the provider is chosen, the config is the user's agent's to write).
/// <see cref="Where"/> is the sandbox's: <c>local</c> or <c>hosted</c>, the hosted service in <see cref="Service"/>;
/// Modal, the one hosted service proctor runs on, also has <see cref="Engine"/> <c>modal</c>.
/// </summary>
record SetupJob(string State, List<string>? Providers = null, string? Where = null, string? Service = null, string? Engine = null)
{
    public const string Set = "set", Later = "later", ByHand = "by-hand";
}

/// <summary>
/// <c>suites/setup.json</c>: the wizard's answers minus keys, intent and skips included, so a rerun starts where the
/// last ended and <c>setup --check</c> can report what the user wants as well as what works. Gitignored: it names this
/// machine's providers. Setup owns exactly the config entries its providers and jobs name, and edits no other.
/// </summary>
record SetupState(DateTime Updated, List<SetupProvider> Providers, Dictionary<string, SetupJob> Jobs)
{
    public const string FileName = "setup.json";

    /// <summary>The jobs in the wizard's order: the sandbox before the agents that need it.</summary>
    public static readonly (string Id, string Title)[] JobOrder =
    [
        ("nb", "Built-in harness"), ("sandbox", "Sandbox"), ("claude-code", "Claude Code"), ("codex", "Codex"),
        ("classifier", "Classifier checks"), ("judge", "LLM-as-judge"),
    ];

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SetupState Empty() => new(DateTime.UtcNow, [], []);

    public static string PathIn(string suitesDir) => Path.Combine(suitesDir, FileName);

    public static SetupState? Load(string suitesDir)
    {
        var path = PathIn(suitesDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<SetupState>(File.ReadAllText(path), Options); }
        catch (JsonException e) { throw new ProctorException($"{path}: {e.Message}"); }
    }

    public void Save(string suitesDir)
    {
        Directory.CreateDirectory(suitesDir);
        File.WriteAllText(PathIn(suitesDir), JsonSerializer.Serialize(this with { Updated = DateTime.UtcNow }, Options) + "\n");
    }

    public SetupProvider? Provider(string name) => Providers.FirstOrDefault(p => p.Name == name);

    public SetupJob? Job(string id) => Jobs.GetValueOrDefault(id);

    /// <summary>The jobs that name a provider, for "used by" and for refusing to remove one in use.</summary>
    public List<string> JobsUsing(string provider) =>
        Jobs.Where(j => j.Value.Providers?.Contains(provider) == true).Select(j => j.Key).ToList();

    /// <summary>
    /// A sandbox on this machine counts for the agent jobs before its plan is carried out, and so does Modal before its
    /// login; another hosted service (intent only) or a skip does not.
    /// </summary>
    [JsonIgnore] public bool HasSandbox => Job("sandbox") is { State: SetupJob.Set, Where: "local" } or { State: SetupJob.Set, Engine: HarborConfig.Modal };
}
