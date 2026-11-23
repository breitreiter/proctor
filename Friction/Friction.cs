using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

/// <summary>The exam's surface declaration, compiled: what is under test, what an agent would use in its place, and what a failed call to it looks like.</summary>
sealed class Surface
{
    public bool Declared { get; }
    public string? Hash { get; }
    public IReadOnlyList<string> Tools { get; }
    public IReadOnlyList<string> Commands { get; }
    public IReadOnlyList<string> Docs { get; }
    public IReadOnlyList<string> Internals { get; }
    public IReadOnlyList<string> Hosts { get; }
    public IReadOnlyList<string> SubstituteCommands { get; }
    public IReadOnlyList<string> SubstituteTools { get; }
    public IReadOnlyList<string> SubstituteHosts { get; }
    /// <summary>A surface call's own failure: a 4xx or 5xx status line, or a declared pattern.</summary>
    public Regex Fails { get; }
    /// <summary>A surface command named anywhere in a text, such as a script the attempt wrote.</summary>
    public Regex? Named { get; }
    public Regex? InstalledCheck { get; }
    public Regex? Locating { get; }

    public static readonly Surface None = new(null);

    public Surface(SurfaceDef? def)
    {
        Declared = def is not null;
        Tools = def?.Tools ?? [];
        Commands = def?.Commands ?? [];
        Docs = def?.Docs ?? [];
        Internals = def?.Internals ?? [];
        Hosts = def?.Hosts ?? [];
        SubstituteCommands = def?.Substitutes?.Commands ?? [];
        SubstituteTools = def?.Substitutes?.Tools ?? [];
        SubstituteHosts = def?.Substitutes?.Hosts ?? [];
        var fails = def?.Fails ?? [];
        Fails = new Regex(string.Join("|", fails.Select(f => $"(?:{f})").Prepend(@"HTTP/[\d.]+ [45]\d\d")));
        if (Commands.Count > 0)
        {
            var names = string.Join("|", Commands.Select(Regex.Escape));
            Named = new Regex(@"(^|[\s;&|(`$/])(" + names + @")(\s|$)");
            InstalledCheck = new Regex($@"^(which|type|command -v) ({names})\b");
            Locating = new Regex($@"^(whereis|readlink|file|realpath)\b.*\b({names})\b");
        }
        if (def is not null)
        {
            var matched = new JsonObject
            {
                ["tools"] = List(Tools), ["commands"] = List(Commands), ["docs"] = List(Docs), ["internals"] = List(Internals), ["hosts"] = List(Hosts), ["fails"] = List(fails),
                ["substitutes"] = new JsonObject { ["commands"] = List(SubstituteCommands), ["tools"] = List(SubstituteTools), ["hosts"] = List(SubstituteHosts) },
            };
            Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(matched.ToJsonString())))[..12];
        }
    }

    static JsonArray List(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>A path under one of the globs; an entry spelled <c>tool:Name</c> is a tool, not a path.</summary>
    public static bool Under(string path, IEnumerable<string> globs) =>
        globs.Any(g => !g.StartsWith("tool:") && (Shell.Matches(path, g) || Shell.Matches(path + "/", g) || path == g.TrimEnd('/', '*')));
}

/// <summary>
/// The friction rules: a finished attempt's trajectory into friction.json, the points where the attempt paid a cost the task
/// did not ask for. Rules only, no model: the same trajectory and declaration give the same file. Each call is split
/// into actions; with a surface declared each action is on, about or around the surface, a substitute for it, or
/// background. A point is a stretch of adjacent steps with one label, and it is written only with a quote that is in
/// the call it cites.
/// </summary>
static class Friction
{
    /// <summary>friction/2 (2026-11-23, check-evidence.md C5.1): labels as objects with their sentence, `rule` and `because` on each evidence item, `flagged_steps`.</summary>
    public const string Schema = "friction/2";
    /// <summary>The rules' own version, apart from proctor's: a file says which rules made it. 0.10 renamed every label so each reads bare.</summary>
    public const string RulesVersion = "0.10";

    /// <summary>
    /// Each label and the sentence that defines it ("a step where ..."), written into every file so a viewer words nothing
    /// itself. Every label reads on its own, met bare as a code in a table (reviewed with the user, 2026-11-22).
    /// </summary>
    public static readonly (string Name, string Describes)[] LabelDefs =
    [
        ("read-internals", "a step where the agent read the surface's own source or notes, or went looking for where it is installed"),
        ("used-substitute", "a step where the agent used a declared substitute for the surface"),
        ("fell-back", "a step where the agent used a substitute within two steps of a call to the surface that failed"),
        ("searched-docs", "a step where the agent searched the surface's docs"),
        ("ran-help", "a step where the agent ran --help, -h or man"),
        ("inline-script", "a step where the agent ran a script inline or wrote a file to scratch"),
        ("machine-refused", "a step where the machine refused: command not found, permission, timeout, network"),
        ("harness-refused", "a step where the agent's own tool refused: output too large, a rejected call, an edit that did not apply"),
    ];
    public static readonly string[] Labels = [.. LabelDefs.Select(l => l.Name)];

    /// <summary>Labels as written into a file: [{name, describes}].</summary>
    internal static JsonArray LabelObjects(IEnumerable<(string Name, string Describes)> defs) =>
        new([.. defs.Select(l => (JsonNode?)new JsonObject { ["name"] = l.Name, ["describes"] = l.Describes })]);

    /// <summary>Steps within which a substitute after a failed surface call is "abandoned".</summary>
    const int Window = 2;
    /// <summary>Characters of a result read for error lines, from each end.</summary>
    const int Scan = 200_000;
    const int MaxEvidence = 6;

    // What the machine lacks or refuses, and what the agent's own tools refuse. Matched on a result's lines.
    static readonly Regex Environment = new(
        @"^\s*(?:[\w./-]+: )(?:line \d+: )?(?:[\w./-]+: ){0,3}(?:command not found|Permission denied)\s*$|^\s*(?:/\S*/)?(?:ba)?sh: (?:line )?\d+: \S+: not found\s*$"
        + @"|^\s*Command (?:did not complete within its \d+s timeout|timed out)|^\s*curl: \(\d+\) |^\s*fatal: unable to access|^\s*(?:ssh|wget|apt|apt-get|pip|npm|dotnet|git)\S*: .*(?:Could not resolve host|Connection refused|Network is unreachable)"
        + @"|No space left on device\s*$|^\s*Killed\s*$", RegexOptions.IgnoreCase);
    static readonly Regex ToolError = new(
        @"^\s*(?:<tool_use_error>|InputValidationError|Output too large|(?:Error: )?File has not been read yet|(?:Error: )?String to replace not found|.{0,40}exceeds maximum allowed tokens)", RegexOptions.IgnoreCase);
    /// <summary>A script run inline, or a file written to scratch.</summary>
    static readonly Regex Inline = new(@"\b(python3?|node|ruby|perl|awk)\s+(-c|-e|-\s|<<)|\b(python3?|node|ruby|perl)\s*<<|>\s*/tmp/[\w./-]+");
    static readonly Regex WrittenByHeredoc = new(@">\s*(\S+)\s*<<-?\s*'?""?(\w+)'?""?[^\n]*\n(.*?)\n\2(?=\n|$)", RegexOptions.Singleline);
    static readonly Regex Subcommand = new(@"^[a-z][a-z-]{1,14}$");
    static readonly Regex AsksHelp = new(@"(^|\s)(--help|-h|help)(\s|$)");
    static readonly Regex HelpFlag = new(@"\s(--help|-h)(\s|$)");
    static readonly Regex Recursive = new(@"^-\w*[rR]");
    static readonly HashSet<string> Trivial = ["cd", "ls", "cat", "echo", "printf", "head", "tail", "pwd", "true", "sleep", "wc", "mkdir", "chmod", "cp", "mv", "rm", "touch", "grep", "sed", "find", "git", "export", "set", "test", "which", "type"];
    static readonly char[] LineBreaks = ['\n', '\r', '\v', '\f', '\u001c', '\u001d', '\u001e', '\u0085', '\u2028', '\u2029'];

    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    internal record Action(string Part, string? Rule, string[] Quotes, string Head);

    internal sealed class Call
    {
        public required string Id, Command, Result;
        public required List<Action> Actions;
        public double? Cost;
        public (string Line, string Match)? Refused, ToolRefused, SurfaceFailed;
        public string? InlineScript;
        string? squashedCommand, squashedResult;
        public string SquashedCommand => squashedCommand ??= Shell.Squash(Command);
        public string SquashedResult => squashedResult ??= Shell.Squash(Result);
    }

    internal record Step(string Id, List<Call> Calls);

    public record AttemptFriction(string Variant, string Task, int Attempt, int? Points, string? Skipped, FrictionReader.Reading? Reading = null);

    /// <summary>friction.json beside each given attempt's trajectory, each a current one from the bank. An attempt not completed, or with no trajectory, is skipped and said so.</summary>
    /// <param name="reader">A chat judge of the machine's config that reads each attempt for what rules cannot see; its points are added with a model source. Null for rules only.</param>
    public static List<AttemptFriction> Attempts(string root, Exam exam, List<BankAttempt> attempts, TextWriter log, JudgeClient? judges = null, string? reader = null)
    {
        var surface = new Surface(exam.Def.Surface);
        var done = new List<AttemptFriction>();
        foreach (var a in attempts)
        {
            var (variant, task, n) = (a.Variant, a.Task, a.Attempt);
            var attemptDir = a.Dir(root);
            var trajectory = Path.Combine(attemptDir, Layout.TrajectoryFile);
            var skipped = Runner.ReadStatus(attemptDir) != AttemptStatus.Completed ? "not completed" : !File.Exists(trajectory) ? "no trajectory" : null;
            if (skipped is not null)
            {
                done.Add(new AttemptFriction(variant, task, n, null, skipped));
                continue;
            }
            var attempt = new JsonObject { ["exam"] = exam.Id, ["variant"] = variant, ["task"] = task, ["attempt"] = n.ToString() };
            var parsed = JsonNode.Parse(File.ReadAllText(trajectory))!.AsObject();
            var (file, steps) = Detect(parsed, surface, attempt);
            var reading = reader is null ? null : FrictionReader.Read(attemptDir, parsed, file, steps, exam.Def.Surface?.Description, judges!, reader);
            File.WriteAllText(Path.Combine(attemptDir, Layout.FrictionFile), file.ToJsonString(Written) + "\n");
            var points = file["points"]!.AsArray();
            done.Add(new AttemptFriction(variant, task, n, points.Count, null, reading));
            log.WriteLine($"  {variant}/{task}/{n}  {points.Count} point{(points.Count == 1 ? "" : "s")}  {string.Join("  ", file["summary"]!["by_label"]!.AsObject().Select(k => $"{k.Key} {k.Value}"))}{(reading is null ? "" : $"  [reader: {reading.Says}]")}");
        }
        return done;
    }

    /// <summary>One trajectory into the file's content. <paramref name="attempt"/> is written as given: which attempt this is.</summary>
    public static JsonObject Detect(string trajectoryFile, Surface surface, JsonObject attempt) =>
        Detect(JsonNode.Parse(File.ReadAllText(trajectoryFile))!.AsObject(), surface, attempt).File;

    internal static (JsonObject File, List<Step> Steps) Detect(JsonObject trajectory, Surface surface, JsonObject attempt)
    {
        var steps = Read(trajectory, surface);
        var points = Points(steps, surface);
        return (new JsonObject
        {
            ["schema"] = Schema,
            ["attempt"] = attempt,
            ["agent"] = (trajectory["agent"] as JsonObject)?["name"]?.DeepClone(),
            ["atif"] = trajectory["schema_version"]?.DeepClone(),
            ["detector"] = new JsonObject
            {
                ["name"] = "proctor friction", ["version"] = RulesVersion,
                ["labels"] = LabelObjects(LabelDefs), ["surface"] = surface.Hash,
            },
            ["summary"] = Summary(steps, points, surface),
            ["points"] = new JsonArray(points.Select(p => (JsonNode?)p).ToArray()),
        }, steps);
    }

    // ---------- a call into actions ----------

    internal static string Text(JsonNode? node) => node is null ? "null" : node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString(Compact);

    internal static string? String(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A call's shell command, when it has one: <c>command</c> or <c>cmd</c>, a string or a list of words.</summary>
    internal static string? CommandOf(JsonObject arguments)
    {
        var node = arguments["command"] ?? arguments["cmd"];
        return node is JsonArray words ? string.Join(' ', words.Select(Text)) : String(node);
    }

    /// <summary>A short name for an action: the command and, where it reads as one, its subcommand.</summary>
    static string Head(List<string> w)
    {
        var c = Shell.BaseName(w[0]);
        return w.Count > 1 && Subcommand.IsMatch(w[1]) && !Trivial.Contains(c) && !c.EndsWith(".sh") ? c + " " + w[1] : c;
    }

    static (string Part, string? Rule, string[] Quotes) Sort(List<string> w, string cwd, Surface d, HashSet<string> scripts)
    {
        var c = Shell.BaseName(w[0]);
        var line = string.Join(' ', w);
        var paths = w.Skip(1).Where(x => !x.StartsWith('-') && (x.Contains('/') || x.Contains('.')) && !x.Contains("://") && x.Length < 200)
            .Select(x => (Written: x, Full: Shell.Resolve(cwd, x))).ToList();
        if (c is "grep" or "rg" or "find" or "ls" && paths.Count == 0 && w.Count > 1) paths = [("", cwd)];
        var searching = (c is "grep" or "rg" && (c == "rg" || w.Skip(1).Any(x => Recursive.IsMatch(x)))) || (c == "find" && w.Any(x => x is "-name" or "-iname" or "-path"));
        var firstTwo = string.Join(' ', w.Take(2));

        if (scripts.Contains(c) || (c is "bash" or "sh" && w.Count > 1 && scripts.Contains(Shell.BaseName(w[1])))) return ("on", "surface-script", [c]);
        if (d.Commands.Contains(c)) return AsksHelp.IsMatch(line) ? ("about", "surface-help", [firstTwo, c]) : ("on", "surface-command", [c]);
        if (d.SubstituteCommands.Any(s => line.StartsWith(s + " ") || line == s)) return ("sub", "substitute-command", [firstTwo, c]);
        foreach (var x in w.Skip(1))          // a URL or host:port argument, of any command
        {
            if (d.Hosts.FirstOrDefault(x.Contains) is { } host) return ("on", "surface-url", [host, c]);
            if (d.SubstituteHosts.FirstOrDefault(x.Contains) is { } other) return ("sub", "substitute-url", [other, c]);
        }
        if (d.InstalledCheck is not null)
        {
            if (d.InstalledCheck.IsMatch(line)) return ("about", "installed-check", [firstTwo, c]);          // is it there: not a hunt
            if (d.Locating!.IsMatch(line) || line.StartsWith("find / ")) return ("around", "locating", [firstTwo, c]);
        }
        foreach (var (written, full) in paths)
            if (Surface.Under(full, d.Docs)) return ("about", searching ? "docs-search" : "docs-read", [written, Shell.BaseName(full)]);
        foreach (var (written, full) in paths)          // a folder the docs live under
            if (d.Docs.Any(g => g.StartsWith(full.TrimEnd('/') + "/"))) return ("about", "docs-read", [written, Shell.BaseName(full)]);
        foreach (var (written, full) in paths)
            if (Surface.Under(full, d.Internals)) return ("around", "internals", [written, Shell.BaseName(full), full]);
        return ("background", null, []);
    }

    /// <summary>The actions of one tool call. <paramref name="cwd"/> follows `cd`; <paramref name="scripts"/> gains each file the attempt writes that calls a surface command.</summary>
    static List<Action> ActionsOf(JsonObject call, ref string cwd, Surface d, HashSet<string> scripts)
    {
        var name = String(call["function_name"]) ?? "";
        var arguments = call["arguments"] as JsonObject ?? [];
        if (d.Tools.Any(g => Shell.Matches(name, g))) return [new("on", "surface-tool", [name], name)];
        if (d.SubstituteTools.Any(g => Shell.Matches(name, g))) return [new("sub", "substitute-tool", [name], name)];
        var url = String(arguments["url"]) ?? "";
        if (url.Length > 0 && d.Hosts.Any(url.Contains)) return [new("on", "surface-url", [url, name], name)];
        if (url.Length > 0 && d.SubstituteHosts.Any(url.Contains)) return [new("sub", "substitute-url", [url, name], name)];
        if (d.Docs.Contains("tool:" + name)) return [new("about", "docs-read", [name], name)];

        if (CommandOf(arguments) is { Length: > 0 } command)
        {
            if (command.Length > 100_000) command = command[..100_000];
            if (d.Named is not null)
                foreach (Match m in WrittenByHeredoc.Matches(command))
                    if (d.Named.IsMatch(m.Groups[3].Value)) scripts.Add(Shell.BaseName(m.Groups[1].Value));
            var actions = new List<Action>();
            foreach (var w in Shell.Actions(command))
            {
                if (w[0] == "cd")
                {
                    if (w.Count > 1) cwd = Shell.Resolve(cwd, w[1]);
                    continue;
                }
                var (part, rule, quotes) = Sort(w, cwd, d, scripts);
                actions.Add(new(part, rule, quotes, Head(w)));
                if ((rule != "surface-help" && HelpFlag.IsMatch(" " + string.Join(' ', w.Take(4)) + " ")) || w[0] == "man")
                    actions.Add(new("background", "help", [string.Join(' ', w.Take(3)), w[0]], Head(w)));
            }
            return actions.Count > 0 ? actions : [new("background", null, [], "cd")];
        }

        var path = String(arguments["file_path"]) is { Length: > 0 } a ? a : String(arguments["path"]) is { Length: > 0 } b ? b : String(arguments["notebook_path"]) ?? "";
        if (d.Named is not null && d.Named.IsMatch(String(arguments["content"]) ?? "")) scripts.Add(Shell.BaseName(path));
        if (path.Length > 0)
        {
            var lower = name.ToLowerInvariant();
            var full = Shell.Resolve(cwd, path);
            if (Surface.Under(full, d.Docs)) return [new("about", lower.Contains("grep") || lower.Contains("glob") || lower.Contains("search") ? "docs-search" : "docs-read", [path], name)];
            if (Surface.Under(full, d.Internals)) return [new("around", "internals", [path], name)];
        }
        return [new("background", null, [], name)];
    }

    // ---------- an attempt ----------

    /// <summary>The first line of a result the pattern matches: the line, trimmed and cut, and what matched. Long results are read at both ends only.</summary>
    static (string Line, string Match)? FirstLine(Regex pattern, string result)
    {
        if (result.Length > 2 * Scan) result = result[..Scan] + "\n" + result[^Scan..];
        foreach (var line in result.Split(LineBreaks))
        {
            if (line.Length >= 2000) continue;
            var m = pattern.Match(line);
            if (m.Success)
            {
                var trimmed = line.Trim();
                return (trimmed.Length > 160 ? trimmed[..160] : trimmed, m.Value);
            }
        }
        return null;
    }

    static List<Step> Read(JsonObject trajectory, Surface d)
    {
        var steps = new List<Step>();
        var cwd = "";
        var scripts = new HashSet<string>();
        foreach (var s in (trajectory["steps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var toolCalls = (s["tool_calls"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            if (String(s["source"]) != "agent" || toolCalls.Count == 0) continue;
            cwd = String((s["extra"] as JsonObject)?["cwd"]) is { Length: > 0 } recorded ? recorded : cwd.Length > 0 ? cwd : "/";
            var results = new Dictionary<string, string>();
            foreach (var o in ((s["observation"] as JsonObject)?["results"] as JsonArray ?? []).OfType<JsonObject>())
                results[String(o["source_call_id"]) ?? ""] = o["content"] is { } content && content is not (JsonArray { Count: 0 } or JsonObject { Count: 0 }) ? Text(content) : "";
            double? cost = (s["metrics"] as JsonObject)?["cost_usd"] is JsonValue c && c.TryGetValue<double>(out var usd) ? usd : null;
            var stepId = Text(s["step_id"]);
            var calls = new List<Call>();
            foreach (var (call, n) in toolCalls.Select((call, i) => (call, i + 1)))
            {
                var command = call["arguments"] is JsonObject arguments && CommandOf(arguments) is { } shell ? shell : Text(call["arguments"]);
                var result = results.GetValueOrDefault(String(call["tool_call_id"]) ?? "", "");
                var actions = ActionsOf(call, ref cwd, d, scripts);
                var inline = Inline.Match(Shell.StripHeredocs(command));
                calls.Add(new Call
                {
                    Id = $"s{stepId}.{n}", Command = command, Result = result, Actions = actions, Cost = cost / toolCalls.Count,
                    Refused = FirstLine(Environment, result), ToolRefused = FirstLine(ToolError, result), InlineScript = inline.Success ? inline.Value : null,
                    SurfaceFailed = actions.Any(a => a.Part == "on") ? FirstLine(d.Fails, result) : null,
                });
            }
            steps.Add(new Step(stepId, calls));
        }
        return steps;
    }

    internal static bool Verified(string quote, Call call)
    {
        var q = Shell.Squash(quote);
        return q.Length > 0 && (call.SquashedCommand.Contains(q, StringComparison.Ordinal) || call.SquashedResult.Contains(q, StringComparison.Ordinal));
    }

    record Flag(JsonObject Evidence, string Call, string Quote, string Rule, string Detail);

    /// <summary>Flags per step, then adjacent steps with one label joined into a point.</summary>
    static List<JsonObject> Points(List<Step> steps, Surface d)
    {
        var flags = new Dictionary<(string Label, int Step), List<Flag>>();
        // `because` is one sentence the rule writes from what it already holds, a template per rule, so the same trajectory
        // still gives the same file (check-evidence.md, C5.1).
        void Mark(string label, int i, Call call, string rule, string detail, IEnumerable<string> candidates, string because)
        {
            var quote = candidates.FirstOrDefault(q => q.Length > 0 && Verified(q, call));
            if (quote is null) return;          // a point with no quote that is in the call is not written
            var evidence = new JsonObject
            {
                ["step"] = steps[i].Id, ["call"] = call.Id, ["quote"] = quote,
                ["where"] = call.SquashedCommand.Contains(Shell.Squash(quote), StringComparison.Ordinal) ? "command" : "result",
                ["rule"] = rule, ["because"] = because,
            };
            if (!flags.TryGetValue((label, i), out var list)) flags[(label, i)] = list = [];
            list.Add(new Flag(evidence, call.Id, quote, rule, detail));
        }

        (int Step, string Head, Call Call)? failed = null;          // the last surface call that failed
        for (var i = 0; i < steps.Count; i++)
            foreach (var call in steps[i].Calls)
            {
                foreach (var a in call.Actions)
                {
                    if (a.Rule == "internals")
                    {
                        var path = a.Quotes.Length == 3 ? a.Quotes[^1] : a.Head;
                        Mark("read-internals", i, call, a.Rule, path, a.Quotes, $"read {path}, which is the surface's own source or notes, not its published side");
                    }
                    if (a.Rule == "locating") Mark("read-internals", i, call, a.Rule, a.Head, a.Quotes, $"went looking for where {a.Head} is installed");
                    if (a.Part == "sub")
                    {
                        Mark("used-substitute", i, call, a.Rule!, a.Head, a.Quotes, $"used {a.Head}, a declared substitute for the surface");
                        if (failed is { } f && i - f.Step <= Window)
                        {
                            var match = f.Call.SurfaceFailed!.Value.Match;
                            var gap = i - f.Step;
                            Mark("fell-back", i, call, "substitute-after-failed-surface", $"{f.Head} then {a.Head}", a.Quotes,
                                $"used {a.Head}, a declared substitute, {(gap == 0 ? "in the same step as" : gap == 1 ? "one step after" : $"{gap} steps after")} the call to {f.Head} at {f.Call.Id} failed ({match})");
                            Mark("fell-back", f.Step, f.Call, "substitute-after-failed-surface", $"{f.Head} then {a.Head}", [match],
                                $"the call to {f.Head} failed ({match}), and the agent used {a.Head}, a declared substitute, at {call.Id}");
                        }
                    }
                    if (a.Rule == "docs-search") Mark("searched-docs", i, call, a.Rule, a.Head, a.Quotes, $"searched the surface's docs ({a.Head})");
                    if (a.Rule is "surface-help" or "help") Mark("ran-help", i, call, a.Rule, a.Head, a.Quotes, $"asked {a.Head} for its help");
                }
                if (call.SurfaceFailed is not null)
                    foreach (var a in call.Actions.Where(a => a.Part == "on")) failed = (i, a.Head, call);
                if (call.Refused is { } refused) Mark("machine-refused", i, call, "machine-refuses", Cut(Normalise(refused.Line), 80), [refused.Line, refused.Match], $"the machine refused: {Cut(refused.Line.Trim(), 120)}");
                if (call.ToolRefused is { } tool) Mark("harness-refused", i, call, "tool-refuses", Cut(Normalise(tool.Match), 80), [tool.Line, tool.Match], $"the agent's own tool refused: {Cut(tool.Line.Trim(), 120)}");
                if (d.Declared && call.InlineScript is { } script)
                {
                    var shown = Cut(string.Join(' ', Shell.SplitWhitespace(script)), 40);
                    Mark("inline-script", i, call, "inline-script-or-scratch", shown, [script], $"ran a script inline or wrote to a scratch file ({shown})");
                }
            }

        var order = steps.Select((s, n) => (s.Id, n)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Last().n);
        var points = new List<(int First, int Label, JsonObject Point)>();
        foreach (var (label, labelIndex) in Labels.Select((l, n) => (l, n)))
        {
            var flagged = flags.Keys.Where(k => k.Label == label).Select(k => k.Step).Order().ToList();
            var stretch = new List<int>();
            foreach (var i in flagged.Select(i => (int?)i).Append(null))
            {
                if (stretch.Count > 0 && (i is null || i != stretch[^1] + 1))
                {
                    var all = stretch.SelectMany(k => flags[(label, k)]).ToList();
                    var evidence = all.DistinctBy(f => (f.Call, f.Quote)).ToList();
                    var calls = evidence.Select(f => f.Call).Distinct().OrderBy(CallOrder, Comparer<(long, long)>.Default).ToList();
                    var costs = stretch.SelectMany(k => steps[k].Calls).Where(c => calls.Contains(c.Id)).Select(c => c.Cost).ToList();
                    points.Add((order[steps[stretch[0]].Id], labelIndex, new JsonObject
                    {
                        ["label"] = label,
                        ["steps"] = new JsonArray(steps[stretch[0]].Id, steps[stretch[^1]].Id),
                        ["calls"] = Strings(calls),
                        ["evidence"] = new JsonArray(evidence.Take(MaxEvidence).Select(f => (JsonNode?)f.Evidence).ToArray()),
                        ["rules"] = Strings(all.Select(f => f.Rule).Distinct().Order(StringComparer.Ordinal)),
                        ["detail"] = Strings(all.Select(f => f.Detail).Distinct().Order(StringComparer.Ordinal).Take(6)),
                        ["cost_usd"] = costs.Any(c => c is null) ? null : Math.Round(costs.Sum(c => c!.Value), 6),
                        ["source"] = new JsonObject { ["kind"] = "rule", ["detector"] = RulesVersion },
                    }));
                    stretch = [];
                }
                if (i is { } step) stretch.Add(step);
            }
        }
        var sorted = points.OrderBy(p => p.First).ThenBy(p => p.Label).Select(p => p.Point).ToList();
        for (var n = 0; n < sorted.Count; n++) sorted[n]["id"] = $"p{n + 1}";
        return sorted;
    }

    internal static (long, long) CallOrder(string callId)
    {
        var numbers = Regex.Matches(callId, @"\d+").Select(m => long.TryParse(m.Value, out var v) ? v : long.MaxValue).ToList();
        return (numbers.ElementAtOrDefault(0), numbers.ElementAtOrDefault(1));
    }

    static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

    /// <summary>An error line with its paths, hashes and numbers blanked, so the same trouble in two attempts is the same string.</summary>
    public static string Normalise(string line)
    {
        line = Regex.Replace(line, @"HTTP/[\d.]+ (?=\d{3})", "HTTP ");
        line = Regex.Replace(line, @"(?:[\w.\-~]*/)+([\w.\-]+)", "…/$1");
        line = Regex.Replace(line, @"\b[0-9a-f]{8,}\b", "H");
        var parts = Regex.Split(string.Join(' ', Shell.SplitWhitespace(line)), @"(HTTP \d{3})");          // a status is kept; every other number is blanked
        return Cut(string.Concat(parts.Select((x, i) => i % 2 == 1 ? x : Regex.Replace(x, @"(?<![A-Za-z0-9])\d+", "N"))), 140);
    }

    static JsonObject Summary(List<Step> steps, List<JsonObject> points, Surface d)
    {
        string[] names = ["on", "about", "around", "substitute", "background"];
        var actions = names.ToDictionary(n => n, _ => 0);
        var costs = names.ToDictionary(n => n, _ => 0.0);
        var priced = true;
        JsonObject? first = null;
        foreach (var step in steps)
            foreach (var call in step.Calls)
                foreach (var a in call.Actions)
                {
                    var part = a.Part == "sub" ? "substitute" : a.Part;
                    actions[part]++;
                    if (call.Cost is { } cost) costs[part] += cost / call.Actions.Count;
                    else priced = false;
                    if (first is null && a.Part is "on" or "sub") first = new JsonObject { ["step"] = step.Id, ["reached"] = a.Part == "on" ? "surface" : "substitute" };
                }
        var ids = steps.Select(s => s.Id).ToList();
        var flagged = new HashSet<string>();
        foreach (var p in points)
        {
            int a = ids.IndexOf(p["steps"]![0]!.GetValue<string>()), b = ids.IndexOf(p["steps"]![1]!.GetValue<string>());
            flagged.UnionWith(ids.Skip(a).Take(b - a + 1));
        }
        var byLabel = new JsonObject();
        foreach (var label in Labels)
            if (points.Count(p => p["label"]!.GetValue<string>() == label) is > 0 and var count) byLabel[label] = count;
        var summary = new JsonObject
        {
            // flagged_steps: the steps inside any point, each once, whatever the label: friction's one measure (C5.3).
            ["steps"] = steps.Count, ["calls"] = steps.Sum(s => s.Calls.Count), ["flagged_steps"] = flagged.Count, ["points"] = points.Count, ["by_label"] = byLabel,
        };
        if (!d.Declared) return summary;
        var surfaceCalls = steps.SelectMany(s => s.Calls).Where(c => c.Actions.Any(a => a.Part == "on")).ToList();
        var parts = new JsonObject();
        foreach (var n in names) parts[n] = new JsonObject { ["actions"] = actions[n], ["cost_usd"] = priced ? Math.Round(costs[n], 6) : null };
        summary["surface"] = new JsonObject
        {
            ["reached"] = first is not null, ["first_reach"] = first, ["used"] = actions["on"] > 0, ["worked"] = surfaceCalls.Any(c => c.SurfaceFailed is null),
            ["surface_calls"] = surfaceCalls.Count, ["surface_calls_failed"] = surfaceCalls.Count(c => c.SurfaceFailed is not null),
            ["used_substitute"] = actions["substitute"] > 0, ["parts"] = parts,
        };
        return summary;
    }
}
