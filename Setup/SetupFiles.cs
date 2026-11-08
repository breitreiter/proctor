using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// What setup writes into <c>suites/</c>: nb.json's provider entries, proctor.json's <c>agents</c> and <c>harbor.engine</c>,
/// and <c>suites/.gitignore</c>. Each file is read as a JSON tree and written back whole, so entries setup did not
/// write (named nowhere in setup.json) are kept as they were; setup replaces or removes only its own.
/// </summary>
static class SetupFiles
{
    public const string NbFile = "nb.json", GitIgnore = ".gitignore", SandboxPlan = "sandbox-plan.md";

    /// <summary>Everything setup writes in suites/ that names this machine, and so is never committed.</summary>
    public static readonly string[] Ignored = [Layout.ProctorConfigFile, NbFile, SetupState.FileName, SandboxPlan];

    static readonly JsonSerializerOptions Write = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A fresh nb.json: nb's Mock provider, which the playground suite runs on with no key.</summary>
    static JsonObject FreshNb() => new()
    {
        ["Harness"] = "nb",
        ["ActiveProvider"] = "Mock",
        ["Trust"] = false,
        ["ChatProviders"] = new JsonArray(new JsonObject { ["Name"] = "Mock", ["Response"] = "OK" }),
    };

    /// <summary>A fresh proctor.json: nb on PATH and its config beside this file. The examples are not copied, since they route through a gateway a new user does not have.</summary>
    static JsonObject FreshProctor() => new() { ["nb"] = new JsonObject { ["config"] = NbFile } };

    /// <summary>The entry nb.json gets for a provider: the kind's fixed fields, the form's values, the key as <c>${ENV}</c>. No Model: the model is the suite's.</summary>
    public static JsonObject NbEntry(SetupProvider provider, ProviderKind kind, IReadOnlyDictionary<string, string> values)
    {
        var entry = new JsonObject { ["Name"] = provider.Name };
        foreach (var (k, v) in kind.Nb) entry[k] = v;
        foreach (var field in kind.Fields)
        {
            if (field.Secret) { if (provider.Env is not null) entry[field.Name] = $"${{{provider.Env}}}"; }
            else if (values.TryGetValue(field.Name, out var value)) entry[field.Name] = value;
        }
        return entry;
    }

    /// <summary>Names in nb.json's ChatProviders that setup did not write: kept, never edited, and not available as a new provider's name.</summary>
    public static List<string> ForeignNbEntries(string suitesDir, SetupState state) =>
        NbProviders(ReadObject(Path.Combine(suitesDir, NbFile)) ?? FreshNb())
            .Select(e => (string?)e["Name"]).OfType<string>()
            .Where(n => state.Provider(n) is null && n != "Mock").ToList();

    /// <summary>Adds or replaces setup's entry for one provider. Returns the file written.</summary>
    public static string PutNbEntry(string suitesDir, JsonObject entry)
    {
        var path = Path.Combine(suitesDir, NbFile);
        var root = ReadObject(path) ?? FreshNb();
        var array = root["ChatProviders"] as JsonArray ?? (JsonArray)(root["ChatProviders"] = new JsonArray());
        var name = (string)entry["Name"]!;
        var existing = array.FirstOrDefault(e => (string?)e?["Name"] == name);
        if (existing is not null) array[array.IndexOf(existing)] = entry; else array.Add(entry);
        Save(path, root);
        return path;
    }

    public static void RemoveNbEntry(string suitesDir, string name)
    {
        var path = Path.Combine(suitesDir, NbFile);
        if (ReadObject(path) is not { } root || root["ChatProviders"] is not JsonArray array) return;
        foreach (var e in array.Where(e => (string?)e?["Name"] == name).ToList()) array.Remove(e);
        Save(path, root);
    }

    /// <summary>Whether nb.json has an entry by that name, and the fields it sets (names only).</summary>
    public static HashSet<string>? NbEntryFields(string suitesDir, string name) =>
        NbProviders(ReadObject(Path.Combine(suitesDir, NbFile)) ?? new JsonObject())
            .FirstOrDefault(e => (string?)e["Name"] == name) is { } entry ? entry.Select(p => p.Key).ToHashSet() : null;

    /// <summary>proctor.json <c>agents.&lt;agent&gt;</c>: the kind's base URL for that agent and the provider's key as <c>${ENV}</c>.</summary>
    public static string PutAgent(string suitesDir, string agent, string baseUrl, string env)
    {
        var path = Path.Combine(suitesDir, Layout.ProctorConfigFile);
        var root = ReadObject(path) ?? FreshProctor();
        var agents = root["agents"] as JsonObject ?? (JsonObject)(root["agents"] = new JsonObject());
        agents[agent] = new JsonObject { ["base_url"] = baseUrl, ["api_key"] = $"${{{env}}}" };
        Save(path, root);
        return path;
    }

    public static void RemoveAgent(string suitesDir, string agent)
    {
        var path = Path.Combine(suitesDir, Layout.ProctorConfigFile);
        if (ReadObject(path) is not { } root || root["agents"] is not JsonObject agents || !agents.Remove(agent)) return;
        Save(path, root);
    }

    public static string PutEngine(string suitesDir, string engine)
    {
        var path = Path.Combine(suitesDir, Layout.ProctorConfigFile);
        var root = ReadObject(path) ?? FreshProctor();
        var harbor = root["harbor"] as JsonObject ?? (JsonObject)(root["harbor"] = new JsonObject());
        harbor["engine"] = engine;
        Save(path, root);
        return path;
    }

    /// <summary>proctor.json exists, so a verb that needs it finds it; created fresh when missing.</summary>
    public static string EnsureProctor(string suitesDir)
    {
        var path = Path.Combine(suitesDir, Layout.ProctorConfigFile);
        if (!File.Exists(path)) Save(path, FreshProctor());
        return path;
    }

    /// <summary>Adds to <c>suites/.gitignore</c> each of setup's files it does not already name. Returns whether it changed.</summary>
    public static bool EnsureGitIgnore(string suitesDir)
    {
        var path = Path.Combine(suitesDir, GitIgnore);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var missing = Ignored.Where(f => !lines.Any(l => l.Trim() == f || l.Trim() == "/" + f)).ToList();
        if (missing.Count == 0) return false;
        if (lines.Count == 0) lines.Add("# Written by proctor setup: this machine's config and choices; keys live in your profile, never here.");
        lines.AddRange(missing);
        Directory.CreateDirectory(suitesDir);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return true;
    }

    static List<JsonObject> NbProviders(JsonObject root) =>
        (root["ChatProviders"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    static JsonObject? ReadObject(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new ProctorException($"{path}: expected a JSON object");
        }
        catch (JsonException e) { throw new ProctorException($"{path}: {e.Message}; setup edits only a file it can read, so fix it by hand first"); }
    }

    static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(Write) + "\n");
    }
}
