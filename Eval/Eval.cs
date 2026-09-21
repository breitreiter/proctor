using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Proctor;

// The definition tier: proctor.json, eval.json, the case files and the program template,
// loaded into records and validated with a file and field on every problem.

record NbConfig(string Path = "nb", string? Config = null);

/// <summary>evals/proctor.json. Everything optional; the defaults are the layout's defaults.</summary>
record ProctorConfig(NbConfig? Nb)
{
    public NbConfig NbOrDefault => Nb ?? new NbConfig();
}

record Arm(string? Id, string? Runner, string? Harness, string? Provider, string? Model, int? Samples, string? Command)
{
    public int SamplesOrOne => Samples ?? 1;
}

record HookPair(string? Setup, string? Teardown);

record Hooks(HookPair? Run, HookPair? Arm, HookPair? Case, HookPair? Sample);

record Grading(Dictionary<string, JsonObject>? Checks, List<string>? Pass, List<string>? Validity);

record EvalDef(string? Id, List<string>? Tags, List<Arm>? Arms, Hooks? Hooks, Grading? Grading);

record CaseDef(string? Id, JsonObject? Fixture, string? Prompt, JsonObject? Expect);

record Problem(string File, string Field, string Message)
{
    public override string ToString() => $"{File}: {Field}: {Message}";
}

/// <summary>One eval directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed class Eval
{
    public static readonly string[] RegisteredTags = ["deterministic", "judged"];

    /// <summary>Placeholders a program template may use; resolved per cell by the runner.</summary>
    public static readonly string[] Placeholders = ["prompt", "case", "work", "provider", "model", "harness", "arm", "sample"];

    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required EvalDef Def { get; init; }
    public required List<CaseDef> Cases { get; init; }
    public required string ProgramTemplate { get; init; }
    /// <summary>sha256 over every definition file, so a changed eval is a different eval in the manifest.</summary>
    public required string Hash { get; init; }

    public List<Arm> Arms => Def.Arms!;
    public Grading Grading => Def.Grading!;
    public int PlannedCells => Arms.Sum(a => a.SamplesOrOne) * Cases.Count;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ProctorConfig LoadConfig(string root, List<Problem> problems)
    {
        var file = Path.Combine(Layout.Evals(root), Layout.ProctorConfigFile);
        if (!File.Exists(file)) return new ProctorConfig(null);
        var config = ReadJson<ProctorConfig>(file, problems);
        return config ?? new ProctorConfig(null);
    }

    /// <summary>Load and validate. Returns null when anything is wrong; every problem is in the list.</summary>
    public static Eval? Load(string root, string evalId, List<Problem> problems)
    {
        var dir = Layout.Eval(root, evalId);
        var evalFile = Path.Combine(dir, Layout.EvalFile);
        var rel = (string p) => Path.GetRelativePath(root, p);

        if (!Directory.Exists(dir))
        {
            problems.Add(new Problem(rel(dir), "", "no such eval directory"));
            return null;
        }

        var def = ReadJson<EvalDef>(evalFile, problems);
        if (def is null) return null;
        var before = problems.Count;
        ValidateDef(def, dir, rel(evalFile), evalId, problems);

        var cases = LoadCases(dir, rel, problems);

        var templateFile = Path.Combine(dir, Layout.ProgramTemplateFile);
        var template = "";
        if (!File.Exists(templateFile))
            problems.Add(new Problem(rel(templateFile), "", "missing program template"));
        else
        {
            template = File.ReadAllText(templateFile);
            foreach (Match m in Regex.Matches(template, @"\{\{\s*([^}]*?)\s*\}\}"))
                if (!Placeholders.Contains(m.Groups[1].Value))
                    problems.Add(new Problem(rel(templateFile), m.Value, $"unknown placeholder; known: {string.Join(", ", Placeholders.Select(p => "{{" + p + "}}"))}"));
        }

        if (problems.Count > before) return null;

        return new Eval
        {
            Id = evalId,
            Dir = dir,
            Def = def,
            Cases = cases,
            ProgramTemplate = template,
            Hash = HashDefinition(dir, def),
        };
    }

    private static List<CaseDef> LoadCases(string dir, Func<string, string> rel, List<Problem> problems)
    {
        var casesDir = Path.Combine(dir, Layout.CasesDir);
        var cases = new List<CaseDef>();
        if (!Directory.Exists(casesDir))
        {
            problems.Add(new Problem(rel(casesDir), "", "missing cases directory"));
            return cases;
        }
        foreach (var file in Directory.GetFiles(casesDir, "*.json").Order(StringComparer.Ordinal))
        {
            var c = ReadJson<CaseDef>(file, problems);
            if (c is null) continue;
            var expectedId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(c.Id))
                problems.Add(new Problem(rel(file), "id", "required"));
            else if (c.Id != expectedId)
                problems.Add(new Problem(rel(file), "id", $"'{c.Id}' does not match the file name '{expectedId}'; the id is the file name"));
            if (string.IsNullOrWhiteSpace(c.Prompt))
                problems.Add(new Problem(rel(file), "prompt", "required"));
            cases.Add(c);
        }
        if (cases.Count == 0)
            problems.Add(new Problem(rel(casesDir), "", "no cases; an eval needs at least one *.json case"));
        return cases;
    }

    private static void ValidateDef(EvalDef def, string dir, string file, string evalId, List<Problem> problems)
    {
        void Add(string field, string message) => problems.Add(new Problem(file, field, message));

        if (string.IsNullOrWhiteSpace(def.Id)) Add("id", "required");
        else if (def.Id != evalId) Add("id", $"'{def.Id}' does not match the directory name '{evalId}'");

        foreach (var tag in def.Tags ?? [])
            if (!RegisteredTags.Contains(tag))
                Add("tags", $"unknown tag '{tag}'; registered: {string.Join(", ", RegisteredTags)}");

        if (def.Arms is null or { Count: 0 }) Add("arms", "at least one arm is required");
        var seen = new HashSet<string>();
        foreach (var (arm, i) in (def.Arms ?? []).Select((a, i) => (a, i)))
        {
            var f = $"arms[{i}]";
            if (string.IsNullOrWhiteSpace(arm.Id)) Add($"{f}.id", "required");
            else if (!seen.Add(arm.Id)) Add($"{f}.id", $"duplicate arm id '{arm.Id}'");
            else if (!Regex.IsMatch(arm.Id, "^[a-z0-9][a-z0-9-]*$")) Add($"{f}.id", "arm ids are path segments: lowercase letters, digits and hyphens");
            switch (arm.Runner)
            {
                case "nb":
                    if (string.IsNullOrWhiteSpace(arm.Provider)) Add($"{f}.provider", "required for the nb runner (a provider entry name in nb's config)");
                    if (string.IsNullOrWhiteSpace(arm.Harness)) Add($"{f}.harness", "required for the nb runner (nb, qwen-code, codex or claude-code)");
                    break;
                case "command":
                    Add($"{f}.runner", "'command' is not yet implemented; only 'nb' arms run in this version");
                    break;
                case null or "":
                    Add($"{f}.runner", "required");
                    break;
                default:
                    Add($"{f}.runner", $"unknown runner '{arm.Runner}'; known: nb");
                    break;
            }
            if (arm.Samples is < 1) Add($"{f}.samples", "must be at least 1");
        }

        if (def.Hooks?.Run is not null) Add("hooks.run", "run-level hooks are not yet implemented; use arm or sample");
        if (def.Hooks?.Case is not null) Add("hooks.case", "case-level hooks are not yet implemented; use arm or sample");
        foreach (var (level, pair) in new[] { ("arm", def.Hooks?.Arm), ("sample", def.Hooks?.Sample) })
        {
            if (pair is null) continue;
            foreach (var (kind, script) in new[] { ("setup", pair.Setup), ("teardown", pair.Teardown) })
                if (script is not null && !File.Exists(Path.Combine(dir, script)))
                    Add($"hooks.{level}.{kind}", $"script not found: {script}");
        }

        if (def.Grading?.Checks is null or { Count: 0 })
        {
            Add("grading.checks", "at least one check is required");
            return;
        }
        foreach (var (name, spec) in def.Grading.Checks)
        {
            var f = $"grading.checks.{name}";
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) Add(f, "check names are lowercase letters, digits, hyphens and underscores");
            if (spec is null or { Count: 0 }) { Add(f, "a check is an object with at least one field"); continue; }
            foreach (var (key, value) in spec)
            {
                var problem = Checks.ValidateSpec(key, value, dir);
                if (problem is not null) Add($"{f}.{key}", problem);
            }
        }
        if (def.Grading.Pass is null or { Count: 0 }) Add("grading.pass", "required: the checks whose conjunction is the headline pass");
        foreach (var name in def.Grading.Pass ?? [])
            if (!def.Grading.Checks.ContainsKey(name)) Add("grading.pass", $"'{name}' is not a declared check");
        foreach (var name in def.Grading.Validity ?? [])
        {
            if (!def.Grading.Checks.ContainsKey(name)) Add("grading.validity", $"'{name}' is not a declared check");
            else if (def.Grading.Pass?.Contains(name) == true) Add("grading.validity", $"'{name}' is also in pass; a check decides the pass or whether the sample counts, not both");
        }
    }

    private static string HashDefinition(string dir, EvalDef def)
    {
        var files = new List<string> { Path.Combine(dir, Layout.EvalFile), Path.Combine(dir, Layout.ProgramTemplateFile) };
        files.AddRange(Directory.GetFiles(Path.Combine(dir, Layout.CasesDir), "*.json"));
        foreach (var spec in def.Grading!.Checks!.Values)
            if (spec["script"] is JsonValue v && v.TryGetValue<string>(out var script)) files.Add(Path.Combine(dir, script));
        foreach (var pair in new[] { def.Hooks?.Arm, def.Hooks?.Sample })
            foreach (var script in new[] { pair?.Setup, pair?.Teardown })
                if (script is not null) files.Add(Path.Combine(dir, script));

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.Distinct().Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(dir, file) + "\0"));
            sha.AppendData(File.ReadAllBytes(file));
            sha.AppendData("\0"u8);
        }
        return "sha256:" + Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static T? ReadJson<T>(string file, List<Problem> problems) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), JsonOptions);
        }
        catch (JsonException e)
        {
            problems.Add(new Problem(file, e.Path ?? "", e.Message.Split(" Path:")[0]));
            return null;
        }
    }
}
