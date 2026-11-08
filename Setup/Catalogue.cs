using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proctor;

/// <summary>One field of a kind's form: a value written into the provider's nb.json entry, a secret going to the env file under <see cref="Env"/>.</summary>
record ProviderField(string Name, string Prompt, bool Secret = false, bool Required = true, string? Env = null);

/// <summary>
/// A provider kind from setup's catalogue (<c>Setup/providers.json</c>). <see cref="Serves"/> names the jobs it
/// can do, a trailing <c>?</c> marking a pairing never tried; <see cref="ByHand"/> the jobs it can do only with
/// config setup does not write. <see cref="Base"/> is where the key check lists models; a kind without one has no
/// key check.
/// </summary>
record ProviderKind(
    string Kind, string Label, string Help, string Where, List<ProviderField> Fields, List<string> Serves,
    Dictionary<string, string> Nb, string? Blurb = null, string? Notice = null, string? Base = null,
    [property: JsonPropertyName("by_hand")] List<string>? ByHand = null,
    [property: JsonPropertyName("completed_by_agent")] bool CompletedByAgent = false)
{
    /// <summary>The site page holding each kind's key-page links; the binary carries only this, never a vendor URL.</summary>
    public const string SiteBase = "https://breitreiter.github.io/proctor/";

    public string HelpUrl => SiteBase + Help;

    public JobFit Fit(string job) =>
        Serves.Contains(job) ? JobFit.Yes
        : Serves.Contains(job + "?") ? JobFit.Untested
        : ByHand?.Contains(job) == true ? JobFit.ByHand
        : JobFit.No;
}

enum JobFit { Yes, Untested, ByHand, No }

static class Catalogue
{
    /// <summary>The jobs a kind can serve, in setup's order.</summary>
    public static readonly string[] Jobs = ["nb", "claude-code", "codex", "qwen-code", "classifier", "judge"];

    static readonly Lazy<List<ProviderKind>> kinds = new(Load);

    public static IReadOnlyList<ProviderKind> Kinds => kinds.Value;

    public static ProviderKind Get(string kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind) ?? throw new ProctorException($"no provider kind '{kind}' (known: {string.Join(", ", Kinds.Select(k => k.Kind))})");

    static List<ProviderKind> Load()
    {
        using var stream = typeof(Catalogue).Assembly.GetManifestResourceStream("providers.json")!;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<CatalogueFile>(stream, options)!.Kinds;
    }

    record CatalogueFile(List<ProviderKind> Kinds);
}
