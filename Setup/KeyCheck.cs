using System.Net;
using System.Text.Json.Nodes;

namespace Proctor;

enum KeyCheckStatus { Ok, Failed, NotAvailable }

/// <summary>
/// What the key check found. On <see cref="KeyCheckStatus.Ok"/>, the model ids the key can reach, sorted. On
/// <see cref="KeyCheckStatus.Failed"/>, the provider's own words (the key redacted) and a plain reading of them that
/// is the same for every kind. <see cref="KeyCheckStatus.NotAvailable"/> is a kind that cannot list models with a
/// key alone (Azure), or one with no endpoint yet.
/// </summary>
record KeyCheckResult(KeyCheckStatus Status, List<string> Models, string? Words = null, string? Reading = null)
{
    public static KeyCheckResult Ok(IEnumerable<string> models) => new(KeyCheckStatus.Ok, models.Distinct().Order(StringComparer.Ordinal).ToList());
    public static KeyCheckResult Failed(string words, string reading) => new(KeyCheckStatus.Failed, [], words, reading);
    public static KeyCheckResult NotAvailable(string reading) => new(KeyCheckStatus.NotAvailable, [], null, reading);
}

/// <summary>
/// The key check: ask the provider for its model list. It costs nothing and uses no tokens, proves the key, and
/// says which models the key can reach. Setup picks none of them (plans/setup/providers/check.txt).
/// </summary>
static class KeyCheck
{
    static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// <paramref name="baseUrl"/> overrides the kind's <c>base</c>; for <c>openai-compatible</c>, which has none, it
    /// is the entry's Endpoint (the <c>/v1</c> URL), and without it the check is not available.
    /// </summary>
    public static async Task<KeyCheckResult> RunAsync(ProviderKind kind, string? key, string? baseUrl = null, HttpClient? http = null, CancellationToken ct = default)
    {
        http ??= DefaultHttp;
        var root = (baseUrl ?? kind.Base)?.TrimEnd('/');
        try
        {
            return kind.Kind switch
            {
                "gemini" => await Gemini(http, root!, key!, ct),
                "anthropic" => await List(http, $"{root}/v1/models?limit=1000", key, ct, ("x-api-key", key!), ("anthropic-version", "2023-06-01")),
                "openai" => await List(http, $"{root}/v1/models", key, ct, Bearer(key)),
                "openrouter" => await OpenRouter(http, root!, key!, ct),
                "openai-compatible" when root is null => KeyCheckResult.NotAvailable("no endpoint yet: ask your coding agent to fill in the entry"),
                "openai-compatible" => await List(http, $"{root}/models", key, ct, key is { Length: > 0 } ? [Bearer(key)] : []),
                "azure-openai" or "azure-foundry" => KeyCheckResult.NotAvailable("Azure does not list deployments with a key alone"),
                _ => KeyCheckResult.NotAvailable($"no key check for {kind.Kind}"),
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            var host = Uri.TryCreate(root, UriKind.Absolute, out var u) ? u.Host : root;
            return KeyCheckResult.Failed(Redact(e.InnerException?.Message ?? e.Message, key), $"could not reach {host}; is the endpoint right?");
        }
    }

    /// <summary>Gemini lists embedding and other non-chat models too; only those that generate content are kept.</summary>
    static async Task<KeyCheckResult> Gemini(HttpClient http, string root, string key, CancellationToken ct)
    {
        var (body, failed) = await Get(http, $"{root}/v1beta/models?pageSize=1000", key, ct, ("x-goog-api-key", key));
        if (failed is not null) return failed;
        return KeyCheckResult.Ok(body!["models"]!.AsArray()
            .Where(m => m!["supportedGenerationMethods"]?.AsArray().Any(x => (string?)x == "generateContent") == true)
            .Select(m => ((string)m!["name"]!).Replace("models/", "")));
    }

    /// <summary>OpenRouter's model list is public, so the key is proved by its own endpoint first.</summary>
    static async Task<KeyCheckResult> OpenRouter(HttpClient http, string root, string key, CancellationToken ct)
    {
        var (_, failed) = await Get(http, $"{root}/v1/key", key, ct, Bearer(key));
        return failed ?? await List(http, $"{root}/v1/models", key, ct, Bearer(key));
    }

    /// <summary>The OpenAI-shaped list, which Anthropic, OpenRouter and every OpenAI-compatible server share: <c>data[].id</c>.</summary>
    static async Task<KeyCheckResult> List(HttpClient http, string url, string? key, CancellationToken ct, params (string, string)[] headers)
    {
        var (body, failed) = await Get(http, url, key, ct, headers);
        if (failed is not null) return failed;
        return body?["data"] is JsonArray data
            ? KeyCheckResult.Ok(data.Select(m => (string?)m?["id"]).OfType<string>())
            : KeyCheckResult.Failed(Redact(Excerpt(body?.ToJsonString() ?? ""), key), "the answer was not a model list; is the endpoint right?");
    }

    static async Task<(JsonNode? Body, KeyCheckResult? Failed)> Get(HttpClient http, string url, string? key, CancellationToken ct, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return (null, KeyCheckResult.Failed(Redact(ProviderWords(text, response.StatusCode), key), Reading(response.StatusCode, text)));
        try { return (JsonNode.Parse(text), null); }
        catch (System.Text.Json.JsonException) { return (null, KeyCheckResult.Failed(Redact(Excerpt(text), key), "the answer was not JSON; is the endpoint right?")); }
    }

    /// <summary>How a refusal reads, the same for every kind. Gemini refuses a bad key with 400, not 401.</summary>
    internal static string Reading(HttpStatusCode status, string body) => (int)status switch
    {
        401 or 403 => "the key was refused",
        400 when body.Contains("API key", StringComparison.OrdinalIgnoreCase) => "the key was refused",
        404 => "this endpoint has no model list; is the endpoint right?",
        429 => "rate-limited or out of quota; the key itself may be fine",
        >= 500 => "the provider had an error; try again later",
        var code => $"the provider answered {code}",
    };

    /// <summary><c>error.message</c>, which all four vendors use, else the start of the body.</summary>
    static string ProviderWords(string body, HttpStatusCode status)
    {
        try
        {
            if (JsonNode.Parse(body)?["error"] is JsonNode error)
                return $"{(int)status} {(error is JsonValue ? (string?)error : (string?)error["message"])}";
        }
        catch (System.Text.Json.JsonException) { }
        return $"{(int)status} {Excerpt(body)}";
    }

    static string Excerpt(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 200 ? line : line[..200] + "…";
    }

    /// <summary>A provider's error may quote what it was sent; a key never reaches the screen or a file.</summary>
    static string Redact(string text, string? key) => key is { Length: >= 8 } ? text.Replace(key, "****") : text;

    static (string, string) Bearer(string? key) => ("Authorization", $"Bearer {key}");
}
