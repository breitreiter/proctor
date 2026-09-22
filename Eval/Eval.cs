using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Proctor;

// The definition tier: proctor.json, eval.json, the case files and the program template,
// loaded into records and validated with a file and field on every problem.

/// <summary>nb.path is the host binary and nb.config the config it runs with: the machine's side of running nb. How an eval runs it is the eval's (EvalNb).</summary>
record NbConfig(string Path = "nb", string? Config = null);

/// <summary>
/// eval.json's nb block: the runner script that runs nb for a cell instead of the binary (see the runner contract in the
/// README), relative to the eval directory like a hook, and where it shows nb the checkout and the bundle. It sits beside
/// the hooks because it only works with the hooks that make its container; --runner overrides it for one run.
/// </summary>
record EvalNb(string? Runner = null, NbMounts? Mounts = null);

/// <summary>Where the runner shows nb the checkout and the bundle: what {{work}} and {{bundle}} resolve to when a runner is in effect. Absent, the host paths.</summary>
record NbMounts(string? Work = null, string? Bundle = null);

/// <summary>evals/proctor.json. Everything optional; the defaults are the layout's defaults.</summary>
record ProctorConfig(NbConfig? Nb)
{
    public NbConfig NbOrDefault => Nb ?? new NbConfig();
}

record Arm(string? Id, string? Runner, string? Harness, string? Provider, string? Model, int? Samples, string? Command, BundleSource? Bundle = null)
{
    public int SamplesOrOne => Samples ?? 1;
}

/// <summary>The pinned version of what an arm puts under test: a directory in this repository, or a git revision. What is inside is the eval's business.</summary>
record BundleSource(string? Path, string? Git, string? Rev)
{
    public string Describe() => Git is not null ? $"{Git}@{Rev}" : Path ?? "";
}

record HookPair(string? Setup, string? Teardown);

record Hooks(HookPair? Run, HookPair? Arm, HookPair? Case, HookPair? Sample);

record Grading(Dictionary<string, JsonObject>? Checks, List<string>? Pass, List<string>? Validity);

/// <summary>Tags is what labels replaced; it is here so a leftover is a problem rather than silently ignored.</summary>
record EvalDef(string? Id, List<Arm>? Arms, Hooks? Hooks, Grading? Grading, EvalNb? Nb = null, Dictionary<string, JsonNode?>? Labels = null, JsonNode? Tags = null);

record CaseDef(string? Id, string? Fixture, string? Prompt, JsonObject? Expect, Dictionary<string, JsonNode?>? Labels = null);

/// <summary>
/// Consumer metadata: a key with one or more string values, on an eval, a fixture or a case. Proctor never
/// interprets a key; it stores them, prints them, filters on them and records them on every result row.
/// A tree is a value with slashes in it, matched by a glob.
/// </summary>
static class Labels
{
    public static void Validate(Dictionary<string, JsonNode?>? labels, Action<string, string> add)
    {
        foreach (var (key, value) in labels ?? [])
        {
            if (!Regex.IsMatch(key, "^[a-z0-9][a-z0-9_.-]*$")) { add($"labels.{key}", "label keys are lowercase letters, digits, dots, hyphens and underscores"); continue; }
            var values = value is JsonArray a ? a.ToList() : [value];
            if (values.Count == 0) add($"labels.{key}", "a label is a string or a non-empty list of strings");
            foreach (var v in values)
                if (v is not JsonValue jv || !jv.TryGetValue<string>(out var str) || str.Length == 0)
                    add($"labels.{key}", "a label is a string or a non-empty list of strings");
        }
    }

    /// <summary>Lay each set over the last, key by key: a later set's key replaces the earlier set's values for it.</summary>
    public static Dictionary<string, List<string>> Merge(params Dictionary<string, JsonNode?>?[] sets)
    {
        var merged = new Dictionary<string, List<string>>();
        foreach (var set in sets)
            foreach (var (key, value) in set ?? [])
                merged[key] = value is JsonArray a ? a.Select(v => v!.GetValue<string>()).ToList() : [value!.GetValue<string>()];
        return merged;
    }

    /// <summary>A filter is `key`, any value, or `key=glob` over the values (`*` within a slash segment, `**` across).</summary>
    public static bool Matches(Dictionary<string, List<string>> labels, string filter)
    {
        var eq = filter.IndexOf('=');
        var key = eq < 0 ? filter : filter[..eq];
        if (!labels.TryGetValue(key, out var values)) return false;
        return eq < 0 || values.Any(v => Glob.IsMatch(filter[(eq + 1)..], v));
    }

    public static string Format(Dictionary<string, List<string>> labels) =>
        string.Join(" ", labels.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={string.Join(",", k.Value)}"));
}

/// <summary>A check as declared, with the directory its script path is relative to: the eval's or a fixture's.</summary>
record CheckDef(JsonObject Spec, string Dir);

record Problem(string File, string Field, string Message)
{
    public override string ToString() => $"{File}: {Field}: {Message}";
}

/// <summary>One eval directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed class Eval
{
    /// <summary>Placeholders a program template may use; resolved per cell by the runner.</summary>
    public static readonly string[] Placeholders = ["prompt", "case", "work", "bundle", "provider", "model", "harness", "arm", "sample"];

    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required EvalDef Def { get; init; }
    public required List<CaseDef> Cases { get; init; }
    /// <summary>The fixtures the cases name, by id.</summary>
    public required Dictionary<string, Fixture> Fixtures { get; init; }
    public required string ProgramTemplate { get; init; }
    /// <summary>sha256 over every definition file, so a changed eval is a different eval in the manifest.</summary>
    public required string Hash { get; init; }

    public List<Arm> Arms => Def.Arms!;
    public Grading Grading => Def.Grading!;
    public int PlannedCells => Arms.Sum(a => a.SamplesOrOne) * Cases.Count;

    /// <summary>An eval is judged when any check a cell can carry names a judge; there is no marker to declare.</summary>
    public bool Judged => Grading.Checks!.Values.Concat(Fixtures.Values.SelectMany(f => f.Checks.Values)).Any(spec => spec.ContainsKey("judge"));

    /// <summary>A case's labels: its fixture's, the eval's laid over them, the case's own over both.</summary>
    public Dictionary<string, List<string>> LabelsFor(CaseDef c) => Labels.Merge(FixtureOf(c)?.Def.Labels, Def.Labels, c.Labels);

    /// <summary>Every label any case of the eval carries, for finding the eval by one.</summary>
    public Dictionary<string, List<string>> AllLabels()
    {
        var all = new Dictionary<string, List<string>>();
        foreach (var (key, values) in Cases.SelectMany(c => LabelsFor(c)).Concat(Labels.Merge(Def.Labels)))
            all[key] = (all.GetValueOrDefault(key) ?? []).Concat(values).Distinct().ToList();
        return all;
    }

    public Fixture? FixtureOf(CaseDef c) => c.Fixture is null ? null : Fixtures[c.Fixture];

    /// <summary>The checks that apply to a case: the eval's, then its fixture's.</summary>
    public Dictionary<string, CheckDef> ChecksFor(CaseDef c)
    {
        var checks = Grading.Checks!.ToDictionary(k => k.Key, k => new CheckDef(k.Value, Dir));
        foreach (var (name, spec) in FixtureOf(c)?.Checks ?? []) checks[name] = new CheckDef(spec, FixtureOf(c)!.Dir);
        return checks;
    }

    /// <summary>Every check name any cell can carry, eval checks first, in declared order.</summary>
    public List<string> CheckNames =>
        Grading.Checks!.Keys.Concat(Cases.Select(FixtureOf).Where(f => f is not null).SelectMany(f => f!.Checks.Keys)).Distinct().ToList();

    /// <summary>What the case expects: its fixture's defaults with the case's own block laid over them, key by key.</summary>
    public JsonObject? ExpectFor(CaseDef c)
    {
        var fixture = FixtureOf(c)?.Def.Expect;
        if (fixture is null) return c.Expect;
        var merged = JsonNode.Parse(fixture.ToJsonString())!.AsObject();
        foreach (var (key, value) in c.Expect ?? []) merged[key] = value?.DeepClone();
        return merged;
    }

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
        // The runner and its mounts moved to eval.json (they are the eval's, with its hooks); a leftover here would be silently ignored.
        var raw = config is null ? null : JsonNode.Parse(File.ReadAllText(file), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["nb"];
        foreach (var field in new[] { "runner", "mounts" })
            if (raw?[field] is not null)
                problems.Add(new Problem(Path.GetRelativePath(root, file), $"nb.{field}", $"belongs in the eval: eval.json nb.{field}"));
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

        if (!File.Exists(evalFile))
        {
            problems.Add(new Problem(rel(evalFile), "", "missing eval.json"));
            return null;
        }
        var def = ReadJson<EvalDef>(evalFile, problems);
        if (def is null) return null;
        var before = problems.Count;

        var cases = LoadCases(dir, rel, problems);
        var fixtures = new Dictionary<string, Fixture>();
        foreach (var name in cases.Select(c => c.Fixture).Where(f => f is not null).Distinct())
            if (Fixture.Load(root, name!, problems) is { } fixture) fixtures[name!] = fixture;

        ValidateDef(def, root, dir, rel(evalFile), evalId, cases, fixtures, problems);

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
            Fixtures = fixtures,
            ProgramTemplate = template,
            Hash = HashDefinition(root, dir, def, fixtures.Values),
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
            if (c.Fixture is not null && !Regex.IsMatch(c.Fixture, "^[a-z0-9][a-z0-9-]*$"))
                problems.Add(new Problem(rel(file), "fixture", "a fixture is named by its directory under fixtures/: lowercase letters, digits and hyphens"));
            Labels.Validate(c.Labels, (field, message) => problems.Add(new Problem(rel(file), field, message)));
            cases.Add(c);
        }
        if (cases.Count == 0)
            problems.Add(new Problem(rel(casesDir), "", "no cases; an eval needs at least one *.json case"));
        return cases;
    }

    private static void ValidateDef(EvalDef def, string root, string dir, string file, string evalId, List<CaseDef> cases, Dictionary<string, Fixture> fixtures, List<Problem> problems)
    {
        void Add(string field, string message) => problems.Add(new Problem(file, field, message));

        if (string.IsNullOrWhiteSpace(def.Id)) Add("id", "required");
        else if (def.Id != evalId) Add("id", $"'{def.Id}' does not match the directory name '{evalId}'");

        if (def.Tags is not null) Add("tags", "removed: an eval's own metadata is labels, and whether it is judged follows from its checks");
        Labels.Validate(def.Labels, Add);

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
            switch (arm.Bundle)
            {
                case null: break;
                case { Path: not null, Git: not null }: Add($"{f}.bundle", "path or git, not both"); break;
                case { Path: { } p } when !Directory.Exists(Path.Combine(root, p)): Add($"{f}.bundle.path", $"no such directory under the repository root: {p}"); break;
                case { Git: not null, Rev: null or "" }: Add($"{f}.bundle.rev", "required with git: a bundle is pinned to a revision"); break;
                case { Path: null, Git: null }: Add($"{f}.bundle", "expects {path} or {git, rev}"); break;
            }
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

        if (def.Nb?.Runner is { } runner && !File.Exists(Path.Combine(dir, runner)))
            Add("nb.runner", $"script not found: {runner}");
        foreach (var (field, mount) in new[] { ("work", def.Nb?.Mounts?.Work), ("bundle", def.Nb?.Mounts?.Bundle) })
            if (mount is not null && !Path.IsPathRooted(mount))
                Add($"nb.mounts.{field}", "a mount is an absolute path inside the container");

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
        foreach (var fixture in fixtures.Values)
            foreach (var name in fixture.Checks.Keys.Where(def.Grading.Checks.ContainsKey))
                Add($"grading.checks.{name}", $"also declared by fixture '{fixture.Id}'; a check is the eval's or the fixture's, not both");

        // A pass or validity check the eval does not declare must come from every case's fixture.
        string? Undeclared(string name)
        {
            if (def.Grading.Checks.ContainsKey(name)) return null;
            foreach (var c in cases)
            {
                var fixture = c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture);
                if (fixture is null) return $"'{name}' is not a declared check, and case '{c.Id}' names no fixture that could declare it";
                if (!fixture.Checks.ContainsKey(name)) return $"'{name}' is not a declared check, and fixture '{fixture.Id}' (case '{c.Id}') does not declare it";
            }
            return cases.Count == 0 ? $"'{name}' is not a declared check" : null;
        }
        if (def.Grading.Pass is null or { Count: 0 }) Add("grading.pass", "required: the checks whose conjunction is the headline pass");
        foreach (var name in def.Grading.Pass ?? [])
            if (Undeclared(name) is { } why) Add("grading.pass", why);
        foreach (var name in def.Grading.Validity ?? [])
        {
            if (Undeclared(name) is { } why) Add("grading.validity", why);
            else if (def.Grading.Pass?.Contains(name) == true) Add("grading.validity", $"'{name}' is also in pass; a check decides the pass or whether the sample counts, not both");
        }
    }

    /// <summary>Every definition file: the eval's own, plus each named fixture's fixture.json and check scripts. Not the fixture's source tree; that is the fixture hash, recorded per cell.</summary>
    private static string HashDefinition(string root, string dir, EvalDef def, IEnumerable<Fixture> fixtures)
    {
        var files = new List<string> { Path.Combine(dir, Layout.EvalFile), Path.Combine(dir, Layout.ProgramTemplateFile) };
        files.AddRange(Directory.GetFiles(Path.Combine(dir, Layout.CasesDir), "*.json"));
        foreach (var spec in def.Grading!.Checks!.Values)
            if (spec["script"] is JsonValue v && v.TryGetValue<string>(out var script)) files.Add(Path.Combine(dir, script));
        foreach (var pair in new[] { def.Hooks?.Arm, def.Hooks?.Sample })
            foreach (var script in new[] { pair?.Setup, pair?.Teardown })
                if (script is not null) files.Add(Path.Combine(dir, script));
        foreach (var fixture in fixtures) files.AddRange(fixture.DefinitionFiles());

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.Select(Path.GetFullPath).Distinct().Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/') + "\0"));
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
