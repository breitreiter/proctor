using System.ClientModel;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Proctor;

// The two model checks. `decide` asks a System One endpoint a typed question over a window and reads a probability;
// `judge` asks a chat model a criterion over a window and reads a label with verbatim evidence, reasoning discarded.
// Both write everything they sent and received to verdicts/ beside the evidence; checks.json gets a Verdict like any other.

/// <summary>The judges of evals/proctor.json with the transports the checks call through. Tests hand in fakes; the verbs hand in the real ones.</summary>
sealed class JudgeClient
{
    public required Dictionary<string, JudgeDef> Defs { get; init; }
    /// <summary>`--judge a=b`: a check naming a is graded by b, into b's verdict file, beside a's and never into checks.json.</summary>
    public Dictionary<string, string> Remap { get; init; } = [];
    public bool Comparing => Remap.Count > 0;
    /// <summary>Ignore verdict files and call again.</summary>
    public bool Rejudge { get; init; }
    public HttpClient Http { get; init; } = new() { Timeout = TimeSpan.FromMinutes(2) };
    public Func<JudgeDef, string?, IChatClient> ChatClient { get; init; } = OpenAiCompatible;

    public static JudgeClient From(ProctorConfig config, List<string>? remaps, bool rejudge)
    {
        var remap = new Dictionary<string, string>();
        foreach (var r in remaps ?? [])
        {
            var parts = r.Split('=', 2);
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new ProctorException($"--judge expects from=to, got '{r}'");
            foreach (var name in parts)
                if (config.Judges?.ContainsKey(name) != true) throw new ProctorException($"--judge {r}: no judge '{name}' in evals/proctor.json");
            remap[parts[0]] = parts[1];
        }
        return new JudgeClient { Defs = config.Judges ?? [], Remap = remap, Rejudge = rejudge };
    }

    /// <summary>The judge a check is graded by: what it names (or the only one of its kind), through the remap.</summary>
    public (string Name, JudgeDef Def) Resolve(string kind, string? with, string check)
    {
        var declared = Declared(kind, with, check);
        if (!Remap.TryGetValue(declared.Name, out var to)) return declared;
        if (Defs[to].Kind != kind) throw new ProctorException($"--judge {declared.Name}={to}: '{to}' is {Defs[to].Kind}; check '{check}' needs {kind}");
        return (to, Defs[to]);
    }

    /// <summary>The judge the eval declares for a check, remap aside.</summary>
    public (string Name, JudgeDef Def) Declared(string kind, string? with, string check) =>
        Judges.Resolve(Defs, kind, with, out var problem) ?? throw new ProctorException($"check '{check}': {problem}");

    /// <summary>Whether a comparison pass touches this check: its declared judge is remapped.</summary>
    public bool Remaps(string name, JsonObject spec, string check) =>
        Remap.ContainsKey(Declared(Judge.KindOf(name), (spec["with"] as JsonValue)?.GetValue<string>(), check).Name);

    static IChatClient OpenAiCompatible(JudgeDef def, string? key) =>
        new OpenAIClient(new ApiKeyCredential(key ?? "none"), new OpenAIClientOptions { Endpoint = new Uri(def.Endpoint!) })
            .GetChatClient(def.Model!).AsIChatClient();
}

/// <summary>One sample of a model's answer, as read. Problem says why it was discarded; a discarded sample is still in the file.</summary>
record JudgeSample(string? Label, double? Probability, List<string>? Evidence, string? Reasoning, string? Problem);

record JudgeUsage(long? Input, long? Output);

/// <summary>
/// verdicts/&lt;check&gt;.&lt;judge&gt;.&lt;hash&gt;.json: the request as sent (key redacted), the responses as received, the samples as read,
/// and the verdict. Applied says whether checks.json holds it: true from a plain grade, false from a `--judge` comparison pass.
/// </summary>
record VerdictFile(
    string Check, string Judge, string Kind, string? Model, string Endpoint, string PromptHash, string Window,
    string Graded, long DurationMs, JsonNode Request, List<JsonNode> Responses, List<JudgeSample> Samples, JudgeUsage? Usage, Verdict Verdict, bool Applied = true);

/// <summary>A judge as the report names it: which checks it graded, through which endpoint and model.</summary>
record JudgeUse(string Judge, string Kind, string? Model, string Endpoint, List<string> Checks);

static class Judge
{
    public const string Decide = "decide", JudgeCheck = "judge";
    const string Yes = "yes", No = "no", Unknown = "unknown";
    const double DefaultThreshold = 0.9;
    const int DefaultSamples = 3;
    const int QuoteChars = 80;

    // ---- validation and description (load time) ----

    public static string? ValidateSpec(string name, JsonNode? value)
    {
        if (value is not JsonObject spec) return name == Decide
            ? "expects {ask, window, options?, expect, threshold?, with?}"
            : "expects {ask, window, expect?, samples?, with?}";
        var known = name == Decide ? new[] { "ask", "window", "options", "expect", "threshold", "with" } : ["ask", "window", "expect", "samples", "with"];
        if (spec.FirstOrDefault(f => !known.Contains(f.Key)) is { Key: { } extra }) return $"unknown field '{extra}'; known: {string.Join(", ", known)}";
        if (spec["ask"] is not JsonValue a || !a.TryGetValue<string>(out var ask) || string.IsNullOrWhiteSpace(ask)) return "ask is required: the question, in a sentence";
        if (Window.Validate((spec["window"] as JsonValue)?.GetValue<string>()) is { } w) return $"window: {w}";
        if (spec["with"] is { } with && (with is not JsonValue wv || !wv.TryGetValue<string>(out _))) return "with is a judge name from evals/proctor.json";
        var expect = spec["expect"];
        var fromCase = IsFromExpect(expect);
        if (name == Decide)
        {
            var options = Options(spec);
            if (spec["options"] is not null && options is { Count: 0 }) return "options is a list of labels, or an object of label to its description";
            if (options is { Count: 1 }) return "options needs at least two labels";
            if (options is not null && !fromCase && (expect is not JsonValue || !options.ContainsKey(expect.ToString())))
                return $"expect is required: one of {string.Join(", ", options.Keys)}, or @expect";
            if (options is null && !fromCase && expect is not null && YesNo(expect) is null) return "expect is true or false for a yes/no question (default true), or @expect";
            if (spec["threshold"] is { } t && (t is not JsonValue tv || !tv.TryGetValue<double>(out var th) || th <= 0 || th > 1)) return "threshold is a probability above 0 and up to 1";
        }
        else
        {
            if (!fromCase && expect is not null && YesNo(expect) is null) return "expect is yes or no (default yes), or @expect";
            if (spec["samples"] is { } s && (s is not JsonValue sv || !sv.TryGetValue<int>(out var n) || n < 1)) return "samples is a whole number of at least 1";
        }
        return null;
    }

    /// <summary>The check's judge exists in proctor.json; null when it does. Called when the eval is loaded with the config in hand.</summary>
    public static string? ValidateUse(string name, JsonObject spec, Dictionary<string, JudgeDef>? judges)
    {
        Judges.Resolve(judges, KindOf(name), (spec["with"] as JsonValue)?.GetValue<string>(), out var problem);
        return problem;
    }

    public static string Describe(string name, JsonNode? v)
    {
        var ask = v!["ask"]!.GetValue<string>();
        var expect = v["expect"] is { } e ? (IsFromExpect(e) ? "what the case expects" : YesNo(e) ?? e.ToString()) : Yes;
        return name == Decide ? $"a decider answers '{expect}' to: {ask}" : $"a judge says {expect} to: {ask}";
    }

    public static string KindOf(string name) => name == Decide ? JudgeDef.SystemOne : JudgeDef.Chat;

    // ---- evaluation (grade time) ----

    public static Verdict Evaluate(string name, JsonObject spec, string check, CellContext cell, Transcript t)
    {
        var client = cell.Judges ?? throw new ProctorException($"check '{check}': grading a {name} check needs the judges of evals/proctor.json");
        var (judge, def) = client.Resolve(KindOf(name), (spec["with"] as JsonValue)?.GetValue<string>(), check);

        var expect = spec["expect"];
        if (IsFromExpect(expect))
        {
            expect = cell.Expect?[check];
            if (expect is null) return Verdict.Err($"{name}: case has no expect.{check}");
        }

        var window = spec["window"]!.GetValue<string>();
        var (parts, error) = Window.Extract(window, cell, t, def.MaxWindowOrDefault);
        if (parts is null) return Verdict.Err($"{name}: {error}");

        var call = new Call(client, check, judge, def, window, parts, Path.Combine(cell.CellDir, Layout.VerdictsDir));
        return name == Decide ? RunDecide(call, spec, expect) : RunJudge(call, spec, expect);
    }

    /// <summary>Everything one evaluation carries from the cell to the endpoint and back to the verdict file.</summary>
    record Call(JudgeClient Client, string Check, string Judge, JudgeDef Def, string Window, Dictionary<string, string> Parts, string VerdictsDir);

    // ---- decide: one typed question, one call, a probability ----

    static Verdict RunDecide(Call call, JsonObject spec, JsonNode? expect)
    {
        var options = Options(spec);
        var threshold = spec["threshold"]?.GetValue<double>() ?? DefaultThreshold;
        var expected = options is null ? YesNo(expect ?? true)! : expect!.ToString();
        if (options is not null && !options.ContainsKey(expected)) return Verdict.Err($"decide: expect '{expected}' is not one of {string.Join(", ", options.Keys)}");

        var question = new JsonObject { ["type"] = options is null ? "noul" : "choice", ["instructions"] = spec["ask"]!.GetValue<string>() };
        if (options is not null) question["criteria"] = new JsonObject(options.Select(o => KeyValuePair.Create(o.Key, (JsonNode?)o.Value)));
        var state = new JsonObject(call.Parts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)new JsonObject { ["what_this_is"] = Window.Known[p.Key], ["content"] = p.Value })));
        var request = new JsonObject { ["state"] = state, ["questions"] = new JsonObject { ["q"] = question } };
        var hash = Hash(call.Def.Kind!, call.Def.Endpoint!, request.ToJsonString(), threshold.ToString("R"), expected);

        if (Cached(call, hash) is { } cached) return cached;

        var started = DateTime.UtcNow;
        JsonNode response;
        try { response = PostSystemOne(call, request).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or ProctorException)
        {
            return Save(call, hash, request, [], [new JudgeSample(null, null, null, null, e.Message)], null, null, started,
                Verdict.Err($"decide: {call.Judge} did not answer: {FirstLine(e.Message)}"));
        }

        var result = response["result"] as JsonObject ?? response as JsonObject;
        var answer = result?["answers"]?["q"];
        var model = result?["model"]?.GetValue<string>();
        var usage = result?["usage"] is JsonObject u ? new JudgeUsage(u["input_tokens"]?.GetValue<long>(), u["output_tokens"]?.GetValue<long>()) : null;
        string? label; double p;
        if (options is null && answer?["noul"] is JsonValue nv && nv.TryGetValue<double>(out var pYes))
            (label, p) = pYes >= 0.5 ? (Yes, pYes) : (No, 1 - pYes);
        else if (options is not null && answer?["choice"] is JsonValue cv && cv.TryGetValue<string>(out var chosen) && answer["probabilities"]?[chosen] is JsonValue pv && pv.TryGetValue<double>(out var pChosen))
            (label, p) = (chosen, pChosen);
        else
            return Save(call, hash, request, [response], [new JudgeSample(null, null, null, null, "no answer of the expected type in the response")], usage, model, started,
                Verdict.Err($"decide: {call.Judge} returned no {(options is null ? "noul" : "choice")} answer"));

        var verdict = p < threshold ? new Verdict(Verdict.NeedsJudge, $"{label} p={p:0.00} (below {threshold:0.00})")
            : Verdict.Of(label == expected, $"{label} p={p:0.00}");
        return Save(call, hash, request, [response], [new JudgeSample(label, p, null, null, null)], usage, model, started, verdict);
    }

    static async Task<JsonNode> PostSystemOne(Call call, JsonObject request)
    {
        // A bare media type: Cloudflare's AI Gateway answers "Required value missing: input" to application/json; charset=utf-8.
        using var message = new HttpRequestMessage(HttpMethod.Post, call.Def.Endpoint) { Content = new StringContent(request.ToJsonString(), new MediaTypeHeaderValue("application/json")) };
        if (call.Def.ResolvedKey(call.Judge) is { } key) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await call.Client.Http.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new ProctorException($"HTTP {(int)response.StatusCode}: {FirstLine(body)}");
        return JsonNode.Parse(body) ?? throw new JsonException("empty response");
    }

    // ---- judge: one criterion, several samples at temperature 0, a label with verbatim evidence ----

    const string SystemPrompt = """
        You are grading one criterion over material extracted from an AI coding assistant's run. The material is given in labelled sections; each section says what it is.

        Read the material, then answer the criterion with exactly one label: "yes", "no" or "unknown". "unknown" means the material does not let you decide; use it rather than guessing.

        Think it through first inside <thinking> and </thinking> tags. Then output exactly one fenced block:

        ```json
        {"label": "yes", "evidence": ["..."]}
        ```

        Each evidence string must be an excerpt copied verbatim from the material, short enough to read at a glance. Excerpts that do not appear verbatim in the material are rejected and your answer with them. Give one or two excerpts that most directly support the label; "unknown" may have none.

        Judge only the criterion. Do not weigh length, style, tone or confidence unless the criterion asks about them.
        """;

    static Verdict RunJudge(Call call, JsonObject spec, JsonNode? expect)
    {
        var samples = spec["samples"]?.GetValue<int>() ?? DefaultSamples;
        var expected = YesNo(expect ?? Yes)!;
        var user = Window.Compile(call.Parts) + $"\n### criterion\n{spec["ask"]!.GetValue<string>()}\n\nAnswer with the label and the evidence, as instructed.";
        var request = new JsonObject
        {
            ["model"] = call.Def.Model, ["temperature"] = 0, ["samples"] = samples,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = SystemPrompt }, new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        var hash = Hash(call.Def.Kind!, call.Def.Model!, request.ToJsonString(), expected);

        if (Cached(call, hash) is { } cached) return cached;

        var started = DateTime.UtcNow;
        var responses = new List<JsonNode>();
        var read = new List<JudgeSample>();
        long? input = null, output = null;
        IChatClient chat;
        try { chat = call.Client.ChatClient(call.Def, call.Def.ResolvedKey(call.Judge)); }
        catch (Exception e) when (e is UriFormatException or ProctorException)
        {
            return Save(call, hash, request, [], [new JudgeSample(null, null, null, null, e.Message)], null, null, started, Verdict.Err($"judge: {call.Judge}: {FirstLine(e.Message)}"));
        }
        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt), new(ChatRole.User, user) };
        for (var i = 0; i < samples; i++)
        {
            ChatResponse response;
            try { response = chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 }).GetAwaiter().GetResult(); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                read.Add(new JudgeSample(null, null, null, null, $"no answer: {FirstLine(e.Message)}"));
                continue;
            }
            responses.Add(JsonValue.Create(response.Text)!);
            if (response.Usage is { } u) { input = (input ?? 0) + (u.InputTokenCount ?? 0); output = (output ?? 0) + (u.OutputTokenCount ?? 0); }
            read.Add(ReadSample(response.Text, call.Parts));
        }

        var kept = read.Where(s => s.Problem is null).ToList();
        var needed = Math.Min(2, samples);
        var verdict = kept.Count < needed
            ? Verdict.Err($"judge: {kept.Count} of {samples} usable answers from {call.Judge}: {string.Join("; ", read.Where(s => s.Problem is not null).Select(s => s.Problem).Distinct())}")
            : Settle(kept, expected, samples);
        return Save(call, hash, request, responses, read, input is null ? null : new JudgeUsage(input, output), call.Def.Model, started, verdict);
    }

    static Verdict Settle(List<JudgeSample> kept, string expected, int samples)
    {
        var labels = kept.Select(s => s.Label!).ToList();
        var quote = kept.SelectMany(s => s.Evidence ?? []).FirstOrDefault();
        var cite = quote is null ? "" : $" — \"{Cut(quote)}\"";
        if (labels.All(l => l == expected)) return new Verdict(Verdict.Pass, $"{expected} {labels.Count}/{samples}{cite}");
        var other = expected == Yes ? No : Yes;
        if (labels.All(l => l == other)) return new Verdict(Verdict.Fail, $"{other} {labels.Count}/{samples}{cite}");
        var counts = string.Join(", ", labels.GroupBy(l => l).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
        return new Verdict(Verdict.NeedsJudge, $"{(labels.Contains(Unknown) ? "unknown" : "split")}: {counts} of {samples}{cite}");
    }

    static readonly Regex Thinking = new("<thinking>(.*?)</thinking>", RegexOptions.Singleline);

    /// <summary>The label and evidence from one answer; the reasoning kept for the file; a problem when the answer cannot be used.</summary>
    static JudgeSample ReadSample(string text, Dictionary<string, string> parts)
    {
        var reasoning = Thinking.Match(text) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
        var json = Transcript.LastJsonFence(text) as JsonObject;
        if (json is null) return new JudgeSample(null, null, null, reasoning, "no json block in the answer");
        var label = (json["label"] as JsonValue)?.ToString().Trim().ToLowerInvariant();
        if (label is not (Yes or No or Unknown)) return new JudgeSample(label, null, null, reasoning, $"label '{label}' is not yes, no or unknown");
        var evidence = (json["evidence"] as JsonArray)?.Select(e => e?.ToString() ?? "").Where(e => e.Length > 0).ToList() ?? [];
        var material = Normalise(string.Join("\n", parts.Values));
        var unverified = evidence.Where(e => !material.Contains(Normalise(e), StringComparison.Ordinal)).ToList();
        if (unverified.Count > 0) return new JudgeSample(label, null, evidence, reasoning, $"evidence not found verbatim in the material: \"{Cut(unverified[0])}\"");
        if (label != Unknown && evidence.Count == 0) return new JudgeSample(label, null, evidence, reasoning, "no evidence quoted");
        return new JudgeSample(label, null, evidence, reasoning, null);
    }

    static readonly Regex Spaces = new(@"\s+");
    static string Normalise(string s) => Spaces.Replace(s, " ").Trim();

    // ---- the verdict file: the cache and the record ----

    static string FileOf(Call call, string hash) => Path.Combine(call.VerdictsDir, $"{call.Check}.{call.Judge}.{hash}.json");

    /// <summary>
    /// The verdict on file for this exact request, unless rejudging. An error is not a verdict: the next grade calls again.
    /// A plain grade that reuses a comparison pass's file marks it applied, since checks.json now holds it.
    /// </summary>
    static Verdict? Cached(Call call, string hash)
    {
        var file = FileOf(call, hash);
        if (call.Client.Rejudge || !File.Exists(file)) return null;
        var on = JsonSerializer.Deserialize<VerdictFile>(File.ReadAllText(file), Eval.JsonOptions);
        if (on is null || on.Verdict.Result == Verdict.Error) return null;
        if (!on.Applied && !call.Client.Comparing) Runner.WriteJson(file, on with { Applied = true });
        return on.Verdict;
    }

    static Verdict Save(Call call, string hash, JsonNode request, List<JsonNode> responses, List<JudgeSample> samples, JudgeUsage? usage, string? model, DateTime started, Verdict verdict)
    {
        Directory.CreateDirectory(call.VerdictsDir);
        var file = new VerdictFile(call.Check, call.Judge, call.Def.Kind!, model, call.Def.Endpoint!, hash, call.Window,
            started.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), (long)(DateTime.UtcNow - started).TotalMilliseconds,
            request, responses, samples, usage, verdict, Applied: !call.Client.Comparing);
        Runner.WriteJson(FileOf(call, hash), file);
        return verdict;
    }

    /// <summary>The judges whose verdicts the experiment's checks.json files hold, for the report's reproducibility section. A comparison pass's files are not counted.</summary>
    public static List<JudgeUse> Uses(string root, Experiment experiment, Eval eval)
    {
        var uses = new Dictionary<(string, string, string?, string), List<string>>();
        foreach (var (arm, c, sample) in Runner.Cells(eval))
        {
            var dir = Path.Combine(Layout.Cell(Layout.Experiment(root, experiment.Id), arm.Id!, c.Id!, sample), Layout.VerdictsDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
            {
                VerdictFile? v;
                try { v = JsonSerializer.Deserialize<VerdictFile>(File.ReadAllText(file), Eval.JsonOptions); }
                catch (JsonException) { continue; }
                if (v is null || !v.Applied) continue;
                var checks = uses.GetValueOrDefault((v.Judge, v.Kind, v.Model, v.Endpoint)) ?? (uses[(v.Judge, v.Kind, v.Model, v.Endpoint)] = []);
                if (!checks.Contains(v.Check)) checks.Add(v.Check);
            }
        }
        return uses.OrderBy(u => u.Key.Item1, StringComparer.Ordinal).Select(u => new JudgeUse(u.Key.Item1, u.Key.Item2, u.Key.Item3, u.Key.Item4, u.Value)).ToList();
    }

    /// <summary>An arm whose model or provider carries a judge's family name, so the grader can say so once; the off-family rule is the brief's to keep.</summary>
    public static IEnumerable<string> SameFamilyNotes(Eval eval, JudgeClient client)
    {
        var specs = eval.Grading.Checks!.Concat(eval.Fixtures.Values.SelectMany(f => f.Checks));
        var seen = new HashSet<(string, string)>();
        foreach (var (check, spec) in specs)
            foreach (var name in new[] { Decide, JudgeCheck })
            {
                if (spec[name] is not JsonObject s) continue;
                var (judge, def) = client.Resolve(KindOf(name), (s["with"] as JsonValue)?.GetValue<string>(), check);
                if (def.Family is not { } family) continue;
                foreach (var arm in eval.Arms.Where(a => $"{a.Model} {a.Provider}".Contains(family, StringComparison.OrdinalIgnoreCase)))
                    if (seen.Add((arm.Id!, judge)))
                        yield return $"arm '{arm.Id}' ({arm.Model}) is in the family of judge '{judge}' ({family}); a judge should be off-family from every arm it grades";
            }
    }

    // ---- helpers ----

    static bool IsFromExpect(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s == Checks.FromExpect;

    /// <summary>"yes"/"no" from true/false or the words; null for anything else.</summary>
    static string? YesNo(JsonNode? n) => n switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? Yes : No,
        JsonValue v when v.TryGetValue<string>(out var s) && s.ToLowerInvariant() is Yes or "true" => Yes,
        JsonValue v when v.TryGetValue<string>(out var s) && s.ToLowerInvariant() is No or "false" => No,
        _ => null,
    };

    /// <summary>A decide's options as label to description: a list gives each label its name as its description. Null when absent or malformed.</summary>
    static Dictionary<string, string>? Options(JsonObject spec) => spec["options"] switch
    {
        null => null,
        JsonArray a when a.Count > 0 && a.All(x => x is JsonValue v && v.TryGetValue<string>(out _)) => a.ToDictionary(x => x!.GetValue<string>(), x => x!.GetValue<string>()),
        JsonObject o when o.Count > 0 && o.All(f => f.Value is JsonValue v && v.TryGetValue<string>(out _)) => o.ToDictionary(f => f.Key, f => f.Value!.GetValue<string>()),
        _ => new Dictionary<string, string>(),   // malformed: the validator reports it as such
    };

    static string Hash(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", parts)));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    static string Cut(string s) => s.Length <= QuoteChars ? s : s[..QuoteChars] + "…";
    static string FirstLine(string s) => s.Split('\n')[0].Trim();
}
