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

/// <summary>
/// suites/proctor.json. Everything optional; the defaults are the layout's defaults. The judges are the endpoints the
/// grade-time checks call; agents are where each vendor agent's model calls go (the machine's, like nb.path); harbor is
/// how Harbor is started on this machine.
/// </summary>
record ProctorConfig(NbConfig? Nb, Dictionary<string, JudgeDef>? Judges = null, Dictionary<string, AgentEndpoint>? Agents = null, HarborConfig? Harbor = null)
{
    public NbConfig NbOrDefault => Nb ?? new NbConfig();
}

/// <summary>
/// One entry of proctor.json's agents block: the base URL an agent's model calls go to and its key, ${VAR} resolved at
/// run time. The base URL's host is the one name the agent's container may reach. Config is a native config file for
/// the agent, relative to suites/ (Codex's config.toml, which Harbor's own settings merge over): what the endpoint
/// needs of the agent, such as tools a local server cannot take.
/// </summary>
record AgentEndpoint(string? BaseUrl, string? ApiKey, string? Config = null);

/// <summary>
/// proctor.json's harbor block: the uv that runs the suite's pinned Harbor (default: uv on PATH), nb's self-contained
/// linux-x64 publish directory, which an nb arm's image carries (relative to suites/, like nb.path), and the container
/// engine Harbor drives, its environment type: docker (the default) or podman.
/// </summary>
record HarborConfig(string Uv = "uv", string? Nb = null, string Engine = "docker")
{
    public static readonly string[] Engines = ["docker", "podman"];
}

/// <summary>
/// An arm is what is under test. Without an agent it is nb, run bare: runner, harness and provider say how. With an
/// agent it runs through Harbor in the fixture's image: a vendor agent is that agent's real CLI at a pinned version;
/// agent nb is nb itself, the binary proctor.json's harbor.nb names, on the provider the arm names.
/// </summary>
record Arm(string? Id, string? Runner, string? Harness, string? Provider, string? Model, int? Samples, string? Command, BundleSource? Bundle = null, string? Description = null, string? Agent = null, string? AgentVersion = null)
{
    public const string Nb = "nb", ClaudeCode = "claude-code", Codex = "codex", QwenCode = "qwen-code";
    /// <summary>The vendor agents, each its vendor's CLI on its vendor's models only.</summary>
    public static readonly string[] Vendors = [ClaudeCode, Codex, QwenCode];

    public int SamplesOrOne => Samples ?? 1;
    /// <summary>Run through Harbor: a vendor agent, or nb as a Harbor agent.</summary>
    public bool IsAgent => Agent is not null;
    /// <summary>A vendor's CLI, not nb.</summary>
    public bool IsVendor => IsAgent && Agent != Nb;
}

/// <summary>suite.json's harbor block: the Harbor release every agent arm runs on, pinned.</summary>
record HarborDef(string? Version);

/// <summary>suite.json's limits: run policy for agent arms. wall_ms is Harbor's agent timeout; the rest are not yet.</summary>
record Limits(long? WallMs, int? ToolCalls, long? Tokens, double? CostUsd);

/// <summary>The pinned version of what an arm puts under test: a directory in this repository, or a git revision. What is inside is the suite's business.</summary>
record BundleSource(string? Path, string? Git, string? Rev)
{
    public string Describe() => Git is not null ? $"{Git}@{Rev}" : Path ?? "";
}

record HookPair(string? Setup, string? Teardown);

record Hooks(HookPair? Run, HookPair? Arm, HookPair? Task, HookPair? Sample);

record Grading(Dictionary<string, JsonObject>? Checks, List<string>? Pass, List<string>? Validity);

/// <summary>Tags is what labels replaced; it is here so a leftover is a problem rather than silently ignored.</summary>
record SuiteDef(string? Id, List<Arm>? Arms, Hooks? Hooks, Grading? Grading, SuiteNb? Nb = null, Dictionary<string, JsonNode?>? Labels = null, JsonNode? Tags = null, string? Description = null, HarborDef? Harbor = null, Limits? Limits = null);

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
/// <summary>A check as a task carries it: its spec, the directory its script resolves against, and the level that declared it (suite, fixture or task).</summary>
record CheckDef(JsonObject Spec, string Dir, string Level);

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
    /// <summary>Whether any arm runs through Harbor, which then needs a pinned version, a wall limit and an image per fixture.</summary>
    public bool HasAgentArms => Arms.Any(a => a.IsAgent);
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
        var checks = Grading.Checks!.ToDictionary(k => k.Key, k => new CheckDef(k.Value, Dir, "suite"));
        foreach (var (name, spec) in FixtureOf(c)?.Checks ?? []) checks[name] = new CheckDef(spec, FixtureOf(c)!.Dir, "fixture");
        foreach (var (name, spec) in c.Checks ?? []) checks[name] = new CheckDef(spec, Dir, "task");
        return checks;
    }

    /// <summary>Every check name any cell can carry, suite checks first, then each task's fixture's and its own, in declared order.</summary>
    public List<string> CheckNames => AllChecks().Select(c => c.Name).Distinct().ToList();

    /// <summary>What a check tests, in plain words, when it says one thing everywhere: the declared description, or the one derived from a built-in spec.
    /// A check that tasks describe differently (a headline every task declares with its own criterion) is left out; the sentence is each task's, in <see cref="ChecksFor"/>.</summary>
    public Dictionary<string, string> CheckDescriptions() =>
        AllChecks().GroupBy(c => c.Name, c => Checks.Describe(c.Spec))
            .Where(g => g.Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

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
        if (config?.Harbor is { } harbor && !HarborConfig.Engines.Contains(harbor.Engine))
            problems.Add(new Problem(Path.GetRelativePath(root, file), "harbor.engine", $"unknown engine '{harbor.Engine}'; known: {string.Join(", ", HarborConfig.Engines)}"));
        Judges.Validate(config?.Judges, (field, message) => problems.Add(new Problem(Path.GetRelativePath(root, file), field, message)));
        foreach (var (name, endpoint) in config?.Agents ?? [])
        {
            if (!Arm.Vendors.Contains(name)) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}", $"unknown agent; known: {string.Join(", ", Arm.Vendors)}"));
            else if (string.IsNullOrWhiteSpace(endpoint?.BaseUrl)) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}.base_url", "required: where the agent's model calls go"));
            if (endpoint?.Config is not null && name != Arm.Codex) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}.config", $"not yet: only {Arm.Codex} takes a native config"));
        }
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
            if (arm is null) { Add(f, "an arm is an object, not null"); continue; }
            if (string.IsNullOrWhiteSpace(arm.Id)) Add($"{f}.id", "required");
            else if (!seen.Add(arm.Id)) Add($"{f}.id", $"duplicate arm id '{arm.Id}'");
            else if (!Regex.IsMatch(arm.Id, "^[a-z0-9][a-z0-9-]*$")) Add($"{f}.id", "arm ids are path segments: lowercase letters, digits and hyphens");
            else if (arm.Id == Layout.HarborDir) Add($"{f}.id", $"'{Layout.HarborDir}' is the experiment's directory for the tasks compiled for Harbor; name the arm something else");
            if (arm.IsVendor) { ValidateAgentArm(arm, f, Add); continue; }
            if (arm.IsAgent) { ValidateNbAgentArm(arm, f, Add); continue; }
            switch (arm.Runner)
            {
                case "nb":
                    if (string.IsNullOrWhiteSpace(arm.Provider)) Add($"{f}.provider", "required for the nb runner (a provider entry name in nb's config)");
                    if (string.IsNullOrWhiteSpace(arm.Harness)) Add($"{f}.harness", "required for the nb runner: nb, the only value since nb retired its costumes; for claude-code, codex or qwen-code use that agent");
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

        ValidateHarbor(def, Add, dir, tasks, fixtures, root, problems);

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
            if (name is null) Add("grading.pass", "a check name, not null");
            else if (Undeclared(name) is { } why) Add("grading.pass", why);
        foreach (var name in def.Grading.Validity ?? [])
        {
            if (name is null) Add("grading.validity", "a check name, not null");
            else if (Undeclared(name) is { } why) Add("grading.validity", why);
            else if (def.Grading.Pass?.Contains(name) == true) Add("grading.validity", $"'{name}' is also in pass; a check decides the pass or whether the sample counts, not both");
        }
    }

    /// <summary>
    /// A vendor agent arm: a model of the agent's own vendor, with no provider prefix (Harbor passes it to the gateway
    /// verbatim), at a pinned agent version. The nb fields have no meaning on it.
    /// </summary>
    static void ValidateAgentArm(Arm arm, string f, Action<string, string> add)
    {
        var (vendor, models, example) = arm.Agent switch
        {
            Arm.ClaudeCode => ("an Anthropic", @"^claude-", "claude-haiku-5-5"),
            Arm.Codex => ("an OpenAI", @"^(gpt-|o\d|codex-)", "gpt-5-mini"),
            Arm.QwenCode => ("a Qwen", @"^qwen", "qwen3-coder-next"),
            _ => (null, null, null),
        };
        if (vendor is null) { add($"{f}.agent", $"unknown agent '{arm.Agent}'; known: {Arm.Nb}, {string.Join(", ", Arm.Vendors)}"); return; }
        foreach (var (field, value) in new[] { ("runner", arm.Runner), ("harness", arm.Harness), ("provider", arm.Provider), ("command", arm.Command) })
            if (value is not null) add($"{f}.{field}", $"an nb field; an arm with agent {arm.Agent} is that agent, on the model it names");
        if (arm.Bundle is not null) add($"{f}.bundle", "a bundle on an agent arm is not yet implemented");
        if (string.IsNullOrWhiteSpace(arm.Model)) add($"{f}.model", $"required for agent {arm.Agent}");
        else if (arm.Model.Contains('/')) add($"{f}.model", $"'{arm.Model}' has a provider prefix; name the model bare ({example}), as the gateway receives it");
        else if (!Regex.IsMatch(arm.Model, models!)) add($"{f}.model", $"'{arm.Model}' is not {vendor} model; {arm.Agent} runs {vendor[(vendor.IndexOf(' ') + 1)..]} models only, so the harness and the model are one vendor's");
        if (string.IsNullOrWhiteSpace(arm.AgentVersion)) add($"{f}.agent_version", $"required: the {arm.Agent} release under test, pinned (there is no latest)");
        else if (!Regex.IsMatch(arm.AgentVersion, @"^\d+(\.\d+)*$")) add($"{f}.agent_version", $"'{arm.AgentVersion}' is not a version number");
    }

    /// <summary>
    /// nb as a Harbor agent: the harness and provider as on the bare path, the model optional likewise. The runner is the
    /// bare path's (Harbor runs nb here) and the version is the binary's, so neither is the arm's to say.
    /// </summary>
    static void ValidateNbAgentArm(Arm arm, string f, Action<string, string> add)
    {
        if (arm.Runner is not null) add($"{f}.runner", "the bare path's; an arm with agent nb runs nb through Harbor");
        if (arm.Command is not null) add($"{f}.command", "the bare path's; an arm with agent nb runs nb through Harbor");
        if (arm.AgentVersion is not null) add($"{f}.agent_version", "nb's version is the binary's (proctor.json harbor.nb), which must match the host nb; agent_version pins a vendor agent");
        if (string.IsNullOrWhiteSpace(arm.Provider)) add($"{f}.provider", "required for agent nb (a provider entry name in nb's config)");
        if (string.IsNullOrWhiteSpace(arm.Harness)) add($"{f}.harness", "required for agent nb: nb, the only value since nb retired its costumes; for claude-code, codex or qwen-code use that agent");
        if (arm.Bundle is not null) add($"{f}.bundle", "a bundle on an agent arm is not yet implemented");
        if (arm.Samples is < 1) add($"{f}.samples", "must be at least 1");
    }

    /// <summary>
    /// What an agent arm needs from the rest of the suite: a pinned Harbor, a wall limit (Harbor's agent timeout) and an
    /// image on every task's fixture. A sample hook runs on the host, which an agent's container never sees; the setup it
    /// did belongs in the fixture's image.
    /// </summary>
    static void ValidateHarbor(SuiteDef def, Action<string, string> add, string dir, List<TaskDef> tasks, Dictionary<string, Fixture> fixtures, string root, List<Problem> problems)
    {
        var agents = (def.Arms ?? []).Any(a => a?.IsAgent == true);
        if (def.Harbor is { } harbor && (string.IsNullOrWhiteSpace(harbor.Version) || !Regex.IsMatch(harbor.Version, @"^\d+(\.\d+)*$")))
            add("harbor.version", "required: the Harbor release, pinned (0.24.0)");
        foreach (var (field, value) in new (string, object?)[] { ("tool_calls", def.Limits?.ToolCalls), ("tokens", def.Limits?.Tokens), ("cost_usd", def.Limits?.CostUsd) })
            if (value is not null) add($"limits.{field}", "not yet implemented; only wall_ms is enforced in this version");
        if (def.Limits?.WallMs is < 1000) add("limits.wall_ms", "at least 1000");
        if (!agents)
        {
            if (def.Harbor is not null) add("harbor", "no arm names an agent, so nothing runs through Harbor");
            if (def.Limits is not null) add("limits", "applies to agent arms; an nb arm's limits are budget directives in the program");
            return;
        }
        if (def.Harbor is null) add("harbor.version", "required with an agent arm: the Harbor release it runs on, pinned (0.24.0)");
        if (def.Limits?.WallMs is null) add("limits.wall_ms", "required with an agent arm: how long its run may take");
        if (def.Hooks?.Sample is not null) add("hooks.sample", "not yet with an agent arm: a sample hook runs on the host, the agent in its container; move the setup into the fixture's image");
        foreach (var c in tasks)
        {
            if (c.Fixture is null) problems.Add(new Problem(Path.GetRelativePath(root, TaskFile(dir, c)), "fixture", "required with an agent arm: the agent runs in the fixture's image"));
            else if (fixtures.GetValueOrDefault(c.Fixture) is { Def.Image: null } fixture)
                problems.Add(new Problem(Path.GetRelativePath(root, Path.Combine(fixture.Dir, Layout.FixtureFile)), "image", "required by an agent arm of this suite: {from, run, env}, the image the agent and the checks run in"));
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
