using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Proctor;

// The definition tier: proctor.json, suite.json, the task files and the program template,
// loaded into records and validated with a file and field on every problem.

/// <summary>nb.path is the host binary and nb.config the config it runs with: the machine's side of running nb. How a suite runs it is the suite's (SuiteNb).</summary>
record NbConfig(string Path = "nb", string? Config = null);

/// <summary>
/// suite.json's nb block: the runner script that runs nb for a cell instead of the binary (see the runner contract in the
/// README), relative to the suite directory like a hook, and where it shows nb the checkout and the bundle. It sits beside
/// the hooks because it only works with the hooks that make its container; --runner overrides it for one run.
/// </summary>
record SuiteNb(string? Runner = null, NbMounts? Mounts = null);

/// <summary>Where the runner shows nb the checkout and the bundle: what {{work}} and {{bundle}} resolve to when a runner is in effect. Absent, the host paths.</summary>
record NbMounts(string? Work = null, string? Bundle = null);

/// <summary>suites/proctor.json. Everything optional; the defaults are the layout's defaults. The judges are the endpoints the grade-time checks call.</summary>
record ProctorConfig(NbConfig? Nb, Dictionary<string, JudgeDef>? Judges = null)
{
    public NbConfig NbOrDefault => Nb ?? new NbConfig();
}

record Arm(string? Id, string? Runner, string? Harness, string? Provider, string? Model, int? Samples, string? Command, BundleSource? Bundle = null, string? Description = null)
{
    public int SamplesOrOne => Samples ?? 1;
}

/// <summary>The pinned version of what an arm puts under test: a directory in this repository, or a git revision. What is inside is the suite's business.</summary>
record BundleSource(string? Path, string? Git, string? Rev)
{
    public string Describe() => Git is not null ? $"{Git}@{Rev}" : Path ?? "";
}

record HookPair(string? Setup, string? Teardown);

record Hooks(HookPair? Run, HookPair? Arm, HookPair? Task, HookPair? Sample);

record Grading(Dictionary<string, JsonObject>? Checks, List<string>? Pass, List<string>? Validity);

/// <summary>Tags is what labels replaced; it is here so a leftover is a problem rather than silently ignored.</summary>
record SuiteDef(string? Id, List<Arm>? Arms, Hooks? Hooks, Grading? Grading, SuiteNb? Nb = null, Dictionary<string, JsonNode?>? Labels = null, JsonNode? Tags = null, string? Description = null);

/// <summary>A task: one input and one desired outcome. Expect parameterises the shared checks; Checks are the ones only this task can state.</summary>
record TaskDef(string? Id, string? Fixture, string? Prompt, JsonObject? Expect, Dictionary<string, JsonNode?>? Labels = null, string? Description = null, Dictionary<string, JsonObject>? Checks = null);

/// <summary>
/// Consumer metadata: a key with one or more string values, on a suite, a fixture or a task. Proctor never
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

/// <summary>A check as declared, with the directory its script path is relative to: the suite's (for the suite's own and a task's) or a fixture's.</summary>
record CheckDef(JsonObject Spec, string Dir);

record Problem(string File, string Field, string Message)
{
    public override string ToString() => $"{File}: {Field}: {Message}";
}

/// <summary>One suite directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed class Suite
{
    /// <summary>Placeholders a program template may use; resolved per cell by the runner. {{case}} is {{task}}'s old spelling, accepted for one release.</summary>
    public static readonly string[] Placeholders = ["prompt", "task", "work", "bundle", "provider", "model", "harness", "arm", "sample", "case"];

    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required SuiteDef Def { get; init; }
    public required List<TaskDef> Tasks { get; init; }
    /// <summary>The fixtures the tasks name, by id.</summary>
    public required Dictionary<string, Fixture> Fixtures { get; init; }
    public required string ProgramTemplate { get; init; }
    /// <summary>sha256 over every definition file, so a changed suite is a different suite in the manifest.</summary>
    public required string Hash { get; init; }

    public List<Arm> Arms => Def.Arms!;
    public Grading Grading => Def.Grading!;
    public int PlannedCells => Arms.Sum(a => a.SamplesOrOne) * Tasks.Count;

    /// <summary>A suite is judged when any check a cell can carry asks a model (decide or judge); there is no marker to declare.</summary>
    public bool Judged => AllChecks().Any(c => Checks.AsksAModel(c.Spec));

    /// <summary>A task's labels: its fixture's, the suite's laid over them, the task's own over both.</summary>
    public Dictionary<string, List<string>> LabelsFor(TaskDef c) => Labels.Merge(FixtureOf(c)?.Def.Labels, Def.Labels, c.Labels);

    /// <summary>Every label any task of the suite carries, for finding the suite by one.</summary>
    public Dictionary<string, List<string>> AllLabels()
    {
        var all = new Dictionary<string, List<string>>();
        foreach (var (key, values) in Tasks.SelectMany(c => LabelsFor(c)).Concat(Labels.Merge(Def.Labels)))
            all[key] = (all.GetValueOrDefault(key) ?? []).Concat(values).Distinct().ToList();
        return all;
    }

    public Fixture? FixtureOf(TaskDef c) => c.Fixture is null ? null : Fixtures[c.Fixture];

    /// <summary>The checks that apply to a task, the union of three levels: the suite's, its fixture's, then its own. A task's scripts resolve against the suite directory, as the suite's do.</summary>
    public Dictionary<string, CheckDef> ChecksFor(TaskDef c)
    {
        var checks = Grading.Checks!.ToDictionary(k => k.Key, k => new CheckDef(k.Value, Dir));
        foreach (var (name, spec) in FixtureOf(c)?.Checks ?? []) checks[name] = new CheckDef(spec, FixtureOf(c)!.Dir);
        foreach (var (name, spec) in c.Checks ?? []) checks[name] = new CheckDef(spec, Dir);
        return checks;
    }

    /// <summary>Every check name any cell can carry, suite checks first, then each task's fixture's and its own, in declared order.</summary>
    public List<string> CheckNames => AllChecks().Select(c => c.Name).Distinct().ToList();

    /// <summary>What every check the suite can carry tests, in plain words: the declared description, or the one derived from a built-in spec.</summary>
    public Dictionary<string, string> CheckDescriptions()
    {
        var described = new Dictionary<string, string>();
        foreach (var (name, spec) in AllChecks()) described.TryAdd(name, Checks.Describe(spec));
        return described;
    }

    /// <summary>Every declared check in report order: the suite's, then per task its fixture's and its own. A name shared across tasks appears once per task.</summary>
    IEnumerable<(string Name, JsonObject Spec)> AllChecks() =>
        Grading.Checks!.Select(k => (k.Key, k.Value))
            .Concat(Tasks.SelectMany(c => (FixtureOf(c)?.Checks ?? []).Concat(c.Checks ?? []).Select(k => (k.Key, k.Value))));

    /// <summary>What the task is about, for a reader: its description, else the first line of its prompt.</summary>
    public static string Describe(TaskDef c) => c.Description ?? c.Prompt!.Split('\n')[0].Trim();

    /// <summary>What the task expects: its fixture's defaults with the task's own block laid over them, key by key.</summary>
    public JsonObject? ExpectFor(TaskDef c)
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
        var file = Path.Combine(Layout.Suites(root), Layout.ProctorConfigFile);
        if (!File.Exists(file)) return new ProctorConfig(null);
        var config = ReadJson<ProctorConfig>(file, problems);
        // The runner and its mounts moved to suite.json (they are the suite's, with its hooks); a leftover here would be silently ignored.
        var raw = config is null ? null : JsonNode.Parse(File.ReadAllText(file), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["nb"];
        foreach (var field in new[] { "runner", "mounts" })
            if (raw?[field] is not null)
                problems.Add(new Problem(Path.GetRelativePath(root, file), $"nb.{field}", $"belongs in the suite: suite.json nb.{field}"));
        Judges.Validate(config?.Judges, (field, message) => problems.Add(new Problem(Path.GetRelativePath(root, file), field, message)));
        return config ?? new ProctorConfig(null);
    }

    /// <summary>
    /// Load and validate. Returns null when anything is wrong; every problem is in the list. With the config's judges in
    /// hand, every model check's judge is resolved too; without them (null), that is left to grade.
    /// </summary>
    public static Suite? Load(string root, string suiteId, List<Problem> problems, Dictionary<string, JudgeDef>? judges = null)
    {
        var dir = Layout.Suite(root, suiteId);
        var suiteFile = Layout.SuiteFileIn(dir);
        var rel = (string p) => Path.GetRelativePath(root, p);

        if (!Directory.Exists(dir))
        {
            problems.Add(new Problem(rel(dir), "", "no such suite directory"));
            return null;
        }

        if (!File.Exists(suiteFile))
        {
            problems.Add(new Problem(rel(Path.Combine(dir, Layout.SuiteFile)), "", "missing suite.json"));
            return null;
        }
        var def = ReadJson<SuiteDef>(suiteFile, problems);
        if (def is null) return null;
        var before = problems.Count;

        var tasks = LoadTasks(dir, rel, problems);
        var fixtures = new Dictionary<string, Fixture>();
        foreach (var name in tasks.Select(c => c.Fixture).Where(f => f is not null).Distinct())
            if (Fixture.Load(root, name!, problems) is { } fixture) fixtures[name!] = fixture;

        ValidateDef(def, root, dir, rel(suiteFile), suiteId, tasks, fixtures, problems);
        if (judges is not null)
        {
            foreach (var (check, spec) in def.Grading?.Checks ?? [])
                foreach (var (name, model) in Checks.ModelChecks(spec))
                    if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(suiteFile), $"grading.checks.{check}.{name}.with", problem));
            foreach (var fixture in fixtures.Values)
                foreach (var (check, spec) in fixture.Checks)
                    foreach (var (name, model) in Checks.ModelChecks(spec))
                        if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(Path.Combine(fixture.Dir, Layout.FixtureFile)), $"checks.{check}.{name}.with", problem));
            foreach (var c in tasks)
                foreach (var (check, spec) in c.Checks ?? [])
                    foreach (var (name, model) in Checks.ModelChecks(spec))
                        if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(TaskFile(dir, c)), $"checks.{check}.{name}.with", problem));
        }

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

        return new Suite
        {
            Id = suiteId,
            Dir = dir,
            Def = def,
            Tasks = tasks,
            Fixtures = fixtures,
            ProgramTemplate = template,
            Hash = HashDefinition(root, dir, def, tasks, fixtures.Values),
        };
    }

    static string TaskFile(string dir, TaskDef c) => Path.Combine(Layout.TasksDirIn(dir), c.Id + ".json");

    private static List<TaskDef> LoadTasks(string dir, Func<string, string> rel, List<Problem> problems)
    {
        var tasksDir = Layout.TasksDirIn(dir);
        var tasks = new List<TaskDef>();
        if (!Directory.Exists(tasksDir))
        {
            problems.Add(new Problem(rel(tasksDir), "", "missing tasks directory"));
            return tasks;
        }
        foreach (var file in Directory.GetFiles(tasksDir, "*.json").Order(StringComparer.Ordinal))
        {
            var c = ReadJson<TaskDef>(file, problems);
            if (c is null) continue;
            var expectedId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(c.Id))
                problems.Add(new Problem(rel(file), "id", "required"));
            else if (c.Id != expectedId)
                problems.Add(new Problem(rel(file), "id", $"'{c.Id}' does not match the file name '{expectedId}'; the id is the file name"));
            if (string.IsNullOrWhiteSpace(c.Prompt))
                problems.Add(new Problem(rel(file), "prompt", "required"));
            if (c.Description is not null && string.IsNullOrWhiteSpace(c.Description))
                problems.Add(new Problem(rel(file), "description", "a description is a sentence, not an empty string"));
            if (c.Fixture is not null && !Regex.IsMatch(c.Fixture, "^[a-z0-9][a-z0-9-]*$"))
                problems.Add(new Problem(rel(file), "fixture", "a fixture is named by its directory under fixtures/: lowercase letters, digits and hyphens"));
            Labels.Validate(c.Labels, (field, message) => problems.Add(new Problem(rel(file), field, message)));
            foreach (var (name, spec) in c.Checks ?? [])
            {
                var f = $"checks.{name}";
                if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) problems.Add(new Problem(rel(file), f, "check names are lowercase letters, digits, hyphens and underscores"));
                foreach (var (field, problem) in Checks.ValidateCheck(spec, dir)) problems.Add(new Problem(rel(file), field.Length == 0 ? f : $"{f}.{field}", problem));
            }
            tasks.Add(c);
        }
        if (tasks.Count == 0)
            problems.Add(new Problem(rel(tasksDir), "", "no tasks; a suite needs at least one *.json task"));
        return tasks;
    }

    private static void ValidateDef(SuiteDef def, string root, string dir, string file, string suiteId, List<TaskDef> tasks, Dictionary<string, Fixture> fixtures, List<Problem> problems)
    {
        void Add(string field, string message) => problems.Add(new Problem(file, field, message));

        if (string.IsNullOrWhiteSpace(def.Id)) Add("id", "required");
        else if (def.Id != suiteId) Add("id", $"'{def.Id}' does not match the directory name '{suiteId}'");

        if (def.Description is not null && string.IsNullOrWhiteSpace(def.Description)) Add("description", "a description is a sentence, not an empty string");
        if (def.Tags is not null) Add("tags", "removed: a suite's own metadata is labels, and whether it is judged follows from its checks");
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
            if (arm.Description is not null && string.IsNullOrWhiteSpace(arm.Description)) Add($"{f}.description", "a description is a sentence, not an empty string");
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
        if (def.Hooks?.Task is not null) Add("hooks.task", "task-level hooks are not yet implemented; use arm or sample");
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
            foreach (var (field, problem) in Checks.ValidateCheck(spec, dir)) Add(field.Length == 0 ? f : $"{f}.{field}", problem);
        }
        foreach (var fixture in fixtures.Values)
            foreach (var name in fixture.Checks.Keys.Where(def.Grading.Checks.ContainsKey))
                Add($"grading.checks.{name}", $"also declared by fixture '{fixture.Id}'; a check is declared at one level: the suite's, a fixture's or a task's");
        foreach (var c in tasks)
        {
            var fixture = c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture);
            foreach (var name in (c.Checks ?? []).Keys)
            {
                if (def.Grading.Checks.ContainsKey(name)) problems.Add(new Problem(Path.GetRelativePath(root, TaskFile(dir, c)), $"checks.{name}", "also declared by the suite; a check is declared at one level: the suite's, a fixture's or a task's"));
                else if (fixture?.Checks.ContainsKey(name) == true) problems.Add(new Problem(Path.GetRelativePath(root, TaskFile(dir, c)), $"checks.{name}", $"also declared by fixture '{fixture.Id}'; a check is declared at one level: the suite's, a fixture's or a task's"));
            }
        }

        // A pass or validity check the suite does not declare must reach every task: from its fixture or its own block.
        string? Undeclared(string name)
        {
            if (def.Grading.Checks.ContainsKey(name)) return null;
            foreach (var c in tasks)
            {
                if (c.Checks?.ContainsKey(name) == true) continue;
                var fixture = c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture);
                if (fixture is null) return $"'{name}' is not a declared check, and task '{c.Id}' neither declares it nor names a fixture that could";
                if (!fixture.Checks.ContainsKey(name)) return $"'{name}' is not a declared check, and neither fixture '{fixture.Id}' nor task '{c.Id}' declares it";
            }
            return tasks.Count == 0 ? $"'{name}' is not a declared check" : null;
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

    /// <summary>Every definition file: the suite's own, plus each named fixture's fixture.json and check scripts. Not the fixture's source tree; that is the fixture hash, recorded per cell.</summary>
    private static string HashDefinition(string root, string dir, SuiteDef def, List<TaskDef> tasks, IEnumerable<Fixture> fixtures)
    {
        var files = new List<string> { Layout.SuiteFileIn(dir), Path.Combine(dir, Layout.ProgramTemplateFile) };
        files.AddRange(Directory.GetFiles(Layout.TasksDirIn(dir), "*.json"));
        foreach (var spec in def.Grading!.Checks!.Values.Concat(tasks.SelectMany(c => (c.Checks ?? []).Values)))
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
