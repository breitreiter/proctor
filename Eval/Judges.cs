using System.Text.RegularExpressions;

namespace Proctor;

/// <summary>
/// One entry of evals/proctor.json's judges block: an endpoint proctor's own checks call at grade time. The question is
/// the eval's (a decide or judge check); the endpoint is the machine's, like nb.path. Kind is the wire shape, never a
/// vendor: systemone (POST /v1/systemone, typed questions with probabilities) or chat (OpenAI-dialect chat completions).
/// </summary>
record JudgeDef(string? Kind, string? Endpoint, string? Model, string? ApiKey, string? Family, int? MaxWindow)
{
    public const string SystemOne = "systemone", Chat = "chat";
    public const int DefaultMaxWindow = 24_000;

    public int MaxWindowOrDefault => MaxWindow ?? DefaultMaxWindow;

    /// <summary>The key with ${VAR} references resolved from the environment, at grade time only, so list and run need no key.</summary>
    public string? ResolvedKey(string judge)
    {
        if (ApiKey is null) return null;
        return Regex.Replace(ApiKey, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value)
            ?? throw new ProctorException($"judge '{judge}': {m.Groups[1].Value} is not set in the environment (evals/proctor.json judges.{judge}.api_key)"));
    }
}

static class Judges
{
    /// <summary>Shape-check the block at load time; every problem names its field.</summary>
    public static void Validate(Dictionary<string, JudgeDef>? judges, Action<string, string> add)
    {
        foreach (var (name, def) in judges ?? [])
        {
            var f = $"judges.{name}";
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) add(f, "judge names are lowercase letters, digits, hyphens and underscores");
            if (def is null) { add(f, "expects {kind, endpoint, model?, api_key?, family?, max_window?}"); continue; }
            switch (def.Kind)
            {
                case JudgeDef.SystemOne:
                    if (def.Model is not null) add($"{f}.model", "a systemone endpoint's model is the endpoint's; leave it out");
                    break;
                case JudgeDef.Chat:
                    if (string.IsNullOrWhiteSpace(def.Model)) add($"{f}.model", "required for a chat judge: the model name the endpoint serves");
                    break;
                case null or "":
                    add($"{f}.kind", "required: systemone or chat");
                    break;
                default:
                    add($"{f}.kind", $"unknown kind '{def.Kind}'; known: systemone, chat");
                    break;
            }
            if (string.IsNullOrWhiteSpace(def.Endpoint) || !Uri.TryCreate(def.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                add($"{f}.endpoint", "required: an http(s) URL");
            if (def.MaxWindow is < 1) add($"{f}.max_window", "must be at least 1 character");
        }
    }

    /// <summary>
    /// The judge a check uses: the one it names with `with`, else the only one of the kind it needs. Null with the
    /// reason when there is none or the choice is ambiguous; the caller reports it against its own field.
    /// </summary>
    public static (string Name, JudgeDef Def)? Resolve(Dictionary<string, JudgeDef>? judges, string kind, string? with, out string? problem)
    {
        problem = null;
        if (with is not null)
        {
            if (judges is null || !judges.TryGetValue(with, out var named)) { problem = $"no judge '{with}' in evals/proctor.json judges"; return null; }
            if (named.Kind != kind) { problem = $"judge '{with}' is {named.Kind}; this check needs {kind}"; return null; }
            return (with, named);
        }
        var ofKind = (judges ?? []).Where(j => j.Value.Kind == kind).ToList();
        problem = ofKind.Count switch
        {
            0 => $"no {kind} judge in evals/proctor.json judges",
            1 => null,
            _ => $"several {kind} judges in evals/proctor.json ({string.Join(", ", ofKind.Select(j => j.Key))}); say which with `with`",
        };
        return ofKind.Count == 1 ? (ofKind[0].Key, ofKind[0].Value) : null;
    }
}
