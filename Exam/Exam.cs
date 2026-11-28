using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Auspex;

// The definition tier: auspex.json, exam.json with its stages, and the program template,
// loaded into records and validated with a file and field on every problem.

/// <summary>nb.path is the host binary and nb.config the config it runs with: the machine's side of running nb. How an exam runs it is the exam's (ExamNb).</summary>
record NbConfig(string Path = "nb", string? Config = null);

/// <summary>
/// exam.json's nb block: the runner script that runs nb for an attempt instead of the binary (see the runner contract in
/// docs/configuration.md), relative to the exam directory like a hook, and where it shows nb the checkout. It sits beside
/// the hooks because it only works with the hooks that make its container; --runner overrides it for one run.
/// </summary>
record ExamNb(string? Runner = null, NbMounts? Mounts = null);

/// <summary>Where the runner shows nb the checkout: what {{work}} resolves to when a runner is in effect. Absent, the host path. Bundle is read only to be refused: it went with the bundle.</summary>
record NbMounts(string? Work = null, string? Bundle = null);

/// <summary>
/// exams/auspex.json. Everything optional; the defaults are the layout's defaults. The judges are the endpoints the
/// grade-time checks call; agents are where each vendor agent's model calls go (the machine's, like nb.path); harbor is
/// how Harbor is started on this machine.
/// </summary>
record AuspexConfig(NbConfig? Nb, Dictionary<string, JudgeDef>? Judges = null, Dictionary<string, AgentEndpoint>? Agents = null, HarborConfig? Harbor = null)
{
    public NbConfig NbOrDefault => Nb ?? new NbConfig();
    public HarborConfig HarborOrDefault => Harbor ?? new HarborConfig();

    /// <summary>The file this was read from, as messages name it: exams/auspex.json, or what --config named.</summary>
    [JsonIgnore] public string Source { get; init; } = "exams/auspex.json";
}

/// <summary>
/// One entry of auspex.json's agents block: the base URL an agent's model calls go to and its key, ${VAR} resolved at
/// run time. The base URL's host is the one name the agent's container may reach. Config is a native config file for
/// the agent, relative to exams/ (Codex's config.toml, which Harbor's own settings merge over): what the endpoint
/// needs of the agent, such as tools a local server cannot take.
/// </summary>
record AgentEndpoint(string? BaseUrl, string? ApiKey, string? Config = null);

/// <summary>
/// auspex.json's harbor block: the uv that runs the exam's pinned Harbor (default: uv on PATH), nb's self-contained
/// linux-x64 publish directory, which the image carries where nb runs through Harbor (relative to exams/, like nb.path), and the container
/// engine Harbor drives, its environment type: docker (the default) or podman, on this machine. An engine off the machine (Modal) was
/// taken out of the core on 2026-11-27 and is kept at the tag modal-in-core (project/archive/modal-runs.md).
/// </summary>
record HarborConfig(string Uv = "uv", string? Nb = null, string Engine = "docker")
{
    public static readonly string[] Engines = ["docker", "podman"];
    /// <summary>An engine that was here once, with where its code went.</summary>
    public const string Gone = "modal";
    public const string GoneWhere = "the modal engine was moved out of proctor on 2026-11-27; its code is at the git tag modal-in-core, with its plan and guide in project/archive/. Supported: podman";
}

/// <summary>
/// Who does the work in one configuration, as its values say (<see cref="Axes.Assistant"/>); Id is the configuration's
/// key. Without an agent it is nb, run bare: runner, harness and provider say how. With an agent it runs through Harbor
/// in the fixture's image: a vendor agent is that agent's real CLI at a pinned version; agent nb is nb itself, the binary
/// auspex.json's harbor.nb names, on the provider named.
/// </summary>
record Assistant(string? Id, string? Runner, string? Harness, string? Provider, string? Model, string? Agent = null, string? AgentVersion = null)
{
    public const string Nb = "nb", ClaudeCode = "claude-code", Codex = "codex", QwenCode = "qwen-code";
    /// <summary>The vendor agents, each its vendor's CLI on its vendor's models only.</summary>
    public static readonly string[] Vendors = [ClaudeCode, Codex, QwenCode];

    /// <summary>The configuration's identity: a hash of its values' own.</summary>
    [JsonIgnore] public string Hash { get; init; } = "";
    /// <summary>Run through Harbor: a vendor agent, or nb as a Harbor agent.</summary>
    public bool IsAgent => Agent is not null;
    /// <summary>A vendor's CLI, not nb.</summary>
    public bool IsVendor => IsAgent && Agent != Nb;
}

/// <summary>exam.json's harbor block: the Harbor release every try through Harbor runs on, pinned.</summary>
record HarborDef(string? Version);

/// <summary>exam.json's limits: the policy each stage through Harbor runs under. wall_ms is Harbor's agent timeout; the rest are not yet.</summary>
record Limits(long? WallMs, int? ToolCalls, long? Tokens, double? CostUsd);

record HookPair(string? Setup, string? Teardown);

/// <summary>
/// The exam's hooks, each a setup and a teardown script run on the host: around each configuration's tries, and around
/// each try. `arm` and `sample` are those two as proctor 0.3 named them, read only to be refused; Run and Task were
/// never built.
/// </summary>
record Hooks(HookPair? Run = null, HookPair? Configuration = null, HookPair? Task = null, HookPair? Try = null, JsonNode? Arm = null, JsonNode? Sample = null);

/// <summary>
/// The checks, and the role of each by the list that names it (the user, 2026-11-26): correctness (was the stage
/// completed as intended; together they decide right or wrong, and none may ask a model), integrity (can the result be
/// trusted; a failure takes the try out of every number), quality (preferred, not required; reported, decides nothing).
/// A check in no list is an observation: counted, with no preferred answer. Pass and Validity are the lists' earlier names, refused.
/// </summary>
record Grading(Dictionary<string, JsonObject>? Checks, List<string>? Correctness, List<string>? Integrity, List<string>? Quality = null, JsonNode? Pass = null, JsonNode? Validity = null);

static class Roles
{
    public const string Correctness = "correctness", Integrity = "integrity", Quality = "quality", Observation = "observation";
}

/// <summary>
/// exam.json's oracle block, for the stages that name an answer sheet: the nb config entry that judges (absent, the assistant's
/// own provider, as nb's in-process oracle does) and how many answers an attempt may get. See plans/oracle-in-proctor.md.
/// </summary>
record OracleDef(string? Judge = null, int? Turns = null)
{
    /// <summary>nb's own default for budget oracle_turns.</summary>
    public const int DefaultTurns = 8;
}

/// <summary>
/// exam.json. Tries is how many tries every configuration the exam names should have. Tags is what labels replaced
/// and `arms` what axes replaced; both are here so a leftover is a problem rather than silently ignored.
/// </summary>
record ExamDef(string? Id, JsonNode? Arms, Hooks? Hooks, Grading? Grading, ExamNb? Nb = null, Dictionary<string, JsonNode?>? Labels = null, JsonNode? Tags = null, string? Description = null, HarborDef? Harbor = null, Limits? Limits = null, OracleDef? Oracle = null, SurfaceDef? Surface = null, int? Tries = null, string? Fixture = null, List<StageDef>? Stages = null, bool? Chained = null,
    Dictionary<string, AxisDef>? Axes = null, Dictionary<string, Dictionary<string, string?>?>? Configurations = null, List<Dictionary<string, string?>?>? Exclude = null, Dictionary<string, ParamDef>? Params = null);

/// <summary>
/// What the exam is testing, inside a fixture that holds much else: the tools, commands, docs and hosts that are the
/// surface under test, the surface's own internals (source, private notes), the ordinary ways round it, and patterns
/// that mark a call to it as failed. `proctor friction` sorts each attempt's actions by it; nothing else reads it.
/// Paths are the container's; a path or a tool name is a glob in which `*` crosses directories.
/// </summary>
record SurfaceDef(string? Description = null, List<string>? Tools = null, List<string>? Commands = null, List<string>? Docs = null, List<string>? Internals = null,
    List<string>? Hosts = null, List<string>? Fails = null, SubstitutesDef? Substitutes = null);

/// <summary>The other ways to meet the need the surface meets.</summary>
record SubstitutesDef(List<string>? Commands = null, List<string>? Tools = null, List<string>? Hosts = null);

/// <summary>
/// A stage: one prompt and one desired outcome. Expect parameterises the shared checks; Checks are the ones only this
/// stage can state. Oracle is an answer sheet (nb's format), relative to the exam directory and inside it, so it never
/// reaches a fixture's repo or the agent's container: a scripted user that answers when the agent stops to ask. When says
/// in which configurations it applies. Fixture, Labels and Description are read only to be refused in exam.json: the
/// fixture is the exam's (or its world axis's, set here once loaded), and a stage's sentence is About.
/// </summary>
record StageDef(string? Id, string? Fixture, string? Prompt, JsonObject? Expect, Dictionary<string, JsonNode?>? Labels = null, string? Description = null, Dictionary<string, JsonObject>? Checks = null, string? Oracle = null, string? About = null, string? When = null)
{
    /// <summary>The file that declares it, exam.json, and the field prefix inside it: `stages[i].`.</summary>
    [JsonIgnore] public string? File { get; init; }
    [JsonIgnore] public string At { get; init; } = "";
}

/// <summary>
/// Consumer metadata: a key with one or more string values, on an exam or a fixture. Proctor never
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

/// <summary>A check as a stage carries it: its spec, the directory its script resolves against, and the level that declared it (exam, fixture or stage).</summary>
record CheckDef(JsonObject Spec, string Dir, string Level);

record Problem(string File, string Field, string Message)
{
    public override string ToString() => $"{File}: {Field}: {Message}";
}

/// <summary>One exam directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed class Exam
{
    /// <summary>Placeholders a program template may use; resolved per attempt by the runner.</summary>
    public static readonly string[] Placeholders = ["prompt", "stage", "work", "provider", "model", "harness", "configuration", "try_number"];

    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required ExamDef Def { get; init; }
    public required List<StageDef> Stages { get; init; }
    /// <summary>The fixtures the stages name, by id.</summary>
    public required Dictionary<string, Fixture> Fixtures { get; init; }
    public required string ProgramTemplate { get; init; }
    /// <summary>sha256 over every definition file, so a changed exam is a different exam in a log entry and under exams-seen/.</summary>
    public required string Hash { get; init; }
    /// <summary>Who does the work in each configuration the exam carries, by the configuration's key, in the order of the axes' values; each carries the configuration's hash.</summary>
    public required List<Assistant> Assistants { get; init; }
    /// <summary>
    /// Each stage's hash by id. An exam is one job of stages (plans/bank.md, B5): one workspace, set up once, the agent
    /// launched once per stage in order. So every stage carries the job's hash: what the agent meets (each prompt and
    /// answer sheet in order, the program template, the hooks and the fixture), not the checks or expected values, and a
    /// change to any stage changes what every later stage meets.
    /// </summary>
    public required Dictionary<string, string> StageHashes { get; init; }
    /// <summary>The exam's axes (plans/axes.md): what is under test, declared in exam.json.</summary>
    public required Axes Axes { get; init; }
    /// <summary>
    /// Whether a later stage consumes what an earlier one made. Then the workspace is kept from stage to stage, and a
    /// stage that was stopped ends the try: the fault cascades, so the stages after it are not launched, and the try
    /// counts with what it held before the break. Otherwise each stage starts fresh, from the workspace as the try
    /// began, and the next one runs whatever became of the last.
    /// </summary>
    public bool Chained => Def.Chained == true;
    /// <summary>The stage ids in the order they run: what the bank's paths are built from.</summary>
    public List<string> StageIds => Stages.Select(c => c.Id!).ToList();
    /// <summary>The stages a try of a configuration runs, by its key: those whose condition holds in it.</summary>
    public List<StageDef> StagesOf(string key) => Axes.Of(key) is { } c ? Stages.Where(s => Axes.Applies(s, c)).ToList() : Stages;

    /// <summary>Whether any configuration the exam carries runs through Harbor, which then needs a pinned version, a wall limit and an image per fixture.</summary>
    public bool HasHarbor => Assistants.Any(v => v.IsAgent);
    public Grading Grading => Def.Grading!;
    /// <summary>How many tries every configuration the exam names should have.</summary>
    public int Tries => Def.Tries ?? 1;

    /// <summary>An exam is judged when any check an attempt can carry asks a model (decide or judge); there is no marker to declare.</summary>
    public bool Judged => AllChecks().Any(c => Checks.AsksAModel(c.Spec));

    /// <summary>A stage's labels: its fixture's, with the exam's laid over them. A stage has none of its own.</summary>
    public Dictionary<string, List<string>> LabelsFor(StageDef task) => Labels.Merge(FixtureOf(task)?.Def.Labels, Def.Labels);

    /// <summary>Every label the exam carries, its fixtures' included, for finding the exam by one.</summary>
    public Dictionary<string, List<string>> AllLabels()
    {
        var all = new Dictionary<string, List<string>>();
        foreach (var (key, values) in Stages.SelectMany(c => LabelsFor(c)).Concat(Labels.Merge(Def.Labels)))
            all[key] = (all.GetValueOrDefault(key) ?? []).Concat(values).Distinct().ToList();
        return all;
    }

    public Fixture? FixtureOf(StageDef task) => task.Fixture is null ? null : Fixtures[task.Fixture];

    /// <summary>The stage's answer sheet on the host, or null when it has none.</summary>
    public string? OracleOf(StageDef task) => task.Oracle is null ? null : Path.GetFullPath(Path.Combine(Dir, task.Oracle));

    /// <summary>How many answers an attempt at an oracle stage may get.</summary>
    public int OracleTurns => Def.Oracle?.Turns ?? OracleDef.DefaultTurns;

    /// <summary>The checks that apply to a stage, the union of three levels: the exam's, its fixture's, then its own. A stage's scripts resolve against the exam directory, as the exam's do.</summary>
    public Dictionary<string, CheckDef> ChecksFor(StageDef c)
    {
        var checks = Grading.Checks!.ToDictionary(k => k.Key, k => new CheckDef(k.Value, Dir, "exam"));
        foreach (var (name, spec) in FixtureOf(c)?.Checks ?? []) checks[name] = new CheckDef(spec, FixtureOf(c)!.Dir, "fixture");
        foreach (var (name, spec) in c.Checks ?? []) checks[name] = new CheckDef(spec, Dir, "stage");
        return checks;
    }

    /// <summary>The correctness checks a stage carries.</summary>
    public List<string> CorrectnessFor(StageDef c) => Grading.Correctness!.Where(ChecksFor(c).ContainsKey).ToList();
    /// <summary>A check's role, by the list that names it; in none, an observation.</summary>
    public string RoleOf(string check) =>
        Grading.Correctness!.Contains(check) ? Roles.Correctness : Grading.Integrity?.Contains(check) == true ? Roles.Integrity : Grading.Quality?.Contains(check) == true ? Roles.Quality : Roles.Observation;
    /// <summary>The integrity checks a stage carries.</summary>
    public List<string>? IntegrityFor(StageDef c) => Grading.Integrity?.Where(ChecksFor(c).ContainsKey).ToList();
    public StageDef StageOf(string id) => Stages.First(t => t.Id == id);

    /// <summary>Every check name any attempt can carry, exam checks first, then each stage's fixture's and its own, in declared order.</summary>
    public List<string> CheckNames => AllChecks().Select(c => c.Name).Distinct().ToList();

    /// <summary>What a check tests, in plain words, when it says one thing everywhere: the declared description, or the one derived from a built-in spec.
    /// A check that stages describe differently (a correctness check every stage declares with its own criterion) is left out; the sentence is each stage's, in <see cref="ChecksFor"/>.</summary>
    public Dictionary<string, string> CheckDescriptions() =>
        AllChecks().GroupBy(c => c.Name, c => Checks.Describe(c.Spec))
            .Where(g => g.Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Every declared check in report order: the exam's, then per stage its fixture's and its own. A name shared across stages appears once per stage.</summary>
    IEnumerable<(string Name, JsonObject Spec)> AllChecks() =>
        Grading.Checks!.Select(k => (k.Key, k.Value))
            .Concat(Stages.SelectMany(c => (FixtureOf(c)?.Checks ?? []).Concat(c.Checks ?? []).Select(k => (k.Key, k.Value))));

    /// <summary>What the stage is about, for a reader: its description, else the first line of its prompt.</summary>
    public static string Describe(StageDef task) => task.Description ?? task.Prompt!.Split('\n')[0].Trim();

    /// <summary>What the stage expects: its fixture's defaults with the stage's own block laid over them, key by key.</summary>
    public JsonObject? ExpectFor(StageDef task)
    {
        var fixture = FixtureOf(task)?.Def.Expect;
        if (fixture is null) return task.Expect;
        var merged = JsonNode.Parse(fixture.ToJsonString())!.AsObject();
        foreach (var (key, value) in task.Expect ?? []) merged[key] = value?.DeepClone();
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

    /// <summary>
    /// The machine's config: exams/auspex.json, or the file --config names (resolved against the working directory),
    /// which must exist. Relative paths inside either resolve against exams/, wherever the file is.
    /// </summary>
    public static AuspexConfig LoadConfig(string root, List<Problem> problems, string? configFile = null)
    {
        var file = configFile is null ? Path.Combine(Layout.Exams(root), Layout.AuspexConfigFile) : Path.GetFullPath(configFile);
        if (configFile is not null && !File.Exists(file)) throw new AuspexException($"--config {configFile}: no such file");
        if (!File.Exists(file))
        {
            // The machine's file as proctor named it, left where it was: read as nothing, it would silently drop the judges and the agents' endpoints.
            if (File.Exists(Path.Combine(Layout.Exams(root), "proctor.json"))) throw new AuspexException($"{Layout.ExamsDir}/proctor.json is {Layout.ExamsDir}/{Layout.AuspexConfigFile} now: rename it");
            return new AuspexConfig(null);
        }
        var config = ReadJson<AuspexConfig>(file, problems);
        // The runner and its mounts moved to exam.json (they are the exam's, with its hooks); a leftover here would be silently ignored.
        var raw = config is null ? null : JsonNode.Parse(File.ReadAllText(file), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["nb"];
        foreach (var field in new[] { "runner", "mounts" })
            if (raw?[field] is not null)
                problems.Add(new Problem(Path.GetRelativePath(root, file), $"nb.{field}", $"belongs in the exam: exam.json nb.{field}"));
        if (config?.Harbor is { } harbor && !HarborConfig.Engines.Contains(harbor.Engine))
            problems.Add(new Problem(Path.GetRelativePath(root, file), "harbor.engine", harbor.Engine == HarborConfig.Gone ? HarborConfig.GoneWhere : $"unknown engine '{harbor.Engine}'; known: {string.Join(", ", HarborConfig.Engines)}"));
        Judges.Validate(config?.Judges, (field, message) => problems.Add(new Problem(Path.GetRelativePath(root, file), field, message)));
        foreach (var (name, endpoint) in config?.Agents ?? [])
        {
            if (!Assistant.Vendors.Contains(name)) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}", $"unknown agent; known: {string.Join(", ", Assistant.Vendors)}"));
            else if (string.IsNullOrWhiteSpace(endpoint?.BaseUrl)) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}.base_url", "required: where the agent's model calls go"));
            if (endpoint?.Config is not null && name != Assistant.Codex) problems.Add(new Problem(Path.GetRelativePath(root, file), $"agents.{name}.config", $"not yet: only {Assistant.Codex} takes a native config"));
        }
        return (config ?? new AuspexConfig(null)) with { Source = Path.GetRelativePath(root, file) };
    }

    /// <summary>
    /// Load and validate. Returns null when anything is wrong; every problem is in the list. With the config's judges in
    /// hand, every model check's judge is resolved too; without them (null), that is left to grade. An exam with axes
    /// carries the configurations it names and those with tries in its bank; also names, by key, one a command asked for.
    /// </summary>
    public static Exam? Load(string root, string examId, List<Problem> problems, Dictionary<string, JudgeDef>? judges = null, IEnumerable<string>? also = null)
    {
        var dir = Layout.Exam(root, examId);
        var examFile = Layout.ExamFileIn(dir);
        var rel = (string p) => Path.GetRelativePath(root, p);

        if (!Directory.Exists(dir))
        {
            problems.Add(new Problem(rel(dir), "", "no such exam directory" + (Directory.Exists(Path.Combine(root, "suites")) ? "; this root has suites/, the layout of proctor 0.3, which docs/porting-from-suites.md maps to exams of stages and axes" : "")));
            return null;
        }

        if (!File.Exists(examFile))
        {
            problems.Add(new Problem(rel(Path.Combine(dir, Layout.ExamFile)), "", "missing exam.json"));
            return null;
        }
        var def = ReadJson<ExamDef>(examFile, problems);
        if (def is null) return null;
        var before = problems.Count;

        // The earlier shape, said once and plainly: an exam is stages and axes in exam.json (plans/axes-build.md, slice 7).
        if (def.Stages is null || def.Axes is null)
        {
            var has = Directory.Exists(Layout.TasksDirIn(dir)) ? " This one has tasks/, the earlier shape: each task becomes a stage." : "";
            if (def.Stages is null) problems.Add(new Problem(rel(examFile), "stages", "required: an exam is one job of stages, listed here in the order they run." + has + " docs/configuration.md shows the form"));
            if (def.Axes is null) problems.Add(new Problem(rel(examFile), "axes", "required: what is under test is declared as axes, each with its values, and the configurations to run are named under \"configurations\"." + (def.Stages is null ? "" : has) + " docs/configuration.md shows the form"));
            return null;
        }
        var tasks = LoadStages(def, dir, rel(examFile), problems);
        var assistants = new List<Assistant>();
        var fixtures = new Dictionary<string, Fixture>();
        foreach (var name in tasks.Select(c => c.Fixture).Concat(Axes.FixturesNamed(def)).Where(f => f is not null).Distinct())
            if (Fixture.Load(root, name!, problems) is { } fixture) fixtures[name!] = fixture;

        // Who does the work in each configuration the exam carries, so that what an exam owes its agents is checked.
        Axes? axes = null;
        if (problems.Count == before)
        {
            axes = Axes.Load(def, root, dir, tasks, fixtures, (field, message) => problems.Add(new Problem(rel(examFile), field, message)), [.. also ?? [], .. Bank.Keys(root, examId)]);
            // Each value alone is checked too, where every other axis has a reference to hold it at: one configuration per value, so a
            // value nobody has named yet is still read once. Only the carried ones are run.
            var alone = axes is null ? [] : axes.Def.SelectMany(a => a.Value.Values!.Keys.Select(v => axes.Alone(new Dictionary<string, string> { [a.Key] = v }, out _)))
                .OfType<Configuration>().Where(c => !c.Excluded && axes.Configurations.All(k => k.Key != c.Key)).DistinctBy(c => c.Key).ToList();
            foreach (var c in (axes?.Configurations ?? []).Concat(alone))
            {
                void Add(string field, string message) => problems.Add(new Problem(rel(examFile), "axes", $"{c.Label}: {(field.Length == 0 ? "" : field + ": ")}{message}"));
                var who = axes!.AssistantProblems(c).ToList();
                foreach (var problem in who) Add("", problem);
                if (who.Count == 0) ValidateWho(axes.AssistantOf(c), Add);
                if (problems.Count == before && axes.Configurations.Contains(c)) assistants.Add(axes.AssistantOf(c));
            }
        }
        // A check's `when` says which configurations it applies in (plans/axes-build.md, slice 5): it names only what the exam declares.
        var conditioned = (def.Grading?.Checks ?? []).Select(k => (File: rel(examFile), Field: $"grading.checks.{k.Key}.when", k.Value))
            .Concat(fixtures.Values.SelectMany(f => (f.Checks ?? []).Select(k => (File: rel(Path.Combine(f.Dir, Layout.FixtureFile)), Field: $"checks.{k.Key}.when", k.Value))))
            .Concat(tasks.SelectMany(c => (c.Checks ?? []).Select(k => (File: rel(c.File!), Field: $"{c.At}checks.{k.Key}.when", k.Value))));
        foreach (var (file, field, spec) in conditioned)
        {
            if (spec?[Checks.When] is not JsonValue v || !v.TryGetValue<string>(out var text)) continue;
            foreach (var (axis, _, value) in When.Parse(text, out _)?.Terms ?? [])
                if (!def.Axes.TryGetValue(axis, out var declared) || declared is null) problems.Add(new Problem(file, field, $"'{axis}' is not a declared axis; declared: {string.Join(", ", def.Axes.Keys)}"));
                else if (declared.Values?.ContainsKey(value) != true) problems.Add(new Problem(file, field, $"'{value}' is not a value of axis '{axis}'; declared: {string.Join(", ", declared.Values?.Keys.AsEnumerable() ?? [])}"));
        }

        ValidateDef(def, root, dir, rel(examFile), examId, tasks, assistants, fixtures, problems);
        if (judges is not null)
        {
            foreach (var (check, spec) in def.Grading?.Checks ?? [])
                foreach (var (name, model) in Checks.ModelChecks(spec))
                    if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(examFile), $"grading.checks.{check}.{name}.with", problem));
            foreach (var fixture in fixtures.Values)
                foreach (var (check, spec) in fixture.Checks)
                    foreach (var (name, model) in Checks.ModelChecks(spec))
                        if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(Path.Combine(fixture.Dir, Layout.FixtureFile)), $"checks.{check}.{name}.with", problem));
            foreach (var c in tasks)
                foreach (var (check, spec) in c.Checks ?? [])
                    foreach (var (name, model) in Checks.ModelChecks(spec))
                        if (Judge.ValidateUse(name, model, judges) is { } problem) problems.Add(new Problem(rel(c.File!), $"{c.At}checks.{check}.{name}.with", problem));
        }

        var templateFile = Path.Combine(dir, Layout.ProgramTemplateFile);
        var template = "";
        if (!File.Exists(templateFile))
            problems.Add(new Problem(rel(templateFile), "", "missing program template"));
        else
        {
            template = File.ReadAllText(templateFile);
            foreach (Match m in Regex.Matches(template, @"\{\{\s*([^}]*?)\s*\}\}"))
                if (m.Groups[1].Value is var name && name.StartsWith("axis.") && def.Axes.ContainsKey(name[5..])) continue;
                else if (name.StartsWith("param.") && (def.Params?.ContainsKey(name[6..]) == true || fixtures.Values.Any(f => f.Def.Params?.ContainsKey(name[6..]) == true))) continue;
                else if (name == "task")
                    problems.Add(new Problem(rel(templateFile), m.Value, "renamed: {{stage}}, the stage's id"));
                else if (name == "arm")
                    problems.Add(new Problem(rel(templateFile), m.Value, "renamed: {{configuration}}, the configuration's key; one axis's value is {{axis.<name>}}"));
                else if (name is "sample")
                    problems.Add(new Problem(rel(templateFile), m.Value, "renamed: {{try_number}}"));
                else if (name == "bundle")
                    problems.Add(new Problem(rel(templateFile), m.Value, "there is no bundle: a value's files are laid over the checkout, so read them from {{work}}; a tree a value sets is {{param.NAME}}"));
                else if (!Placeholders.Contains(m.Groups[1].Value))
                    problems.Add(new Problem(rel(templateFile), m.Value, $"unknown placeholder; known: {string.Join(", ", Placeholders.Select(p => "{{" + p + "}}"))}"));
            if (tasks.Any(c => c.Oracle is not null))
                foreach (var problem in OracleTemplateProblems(template, assistants.Any(a => a is { IsAgent: true, IsVendor: false })))
                    problems.Add(new Problem(rel(templateFile), "", problem));
        }

        if (problems.Count > before || axes is null) return null;

        return new Exam
        {
            Id = examId,
            Dir = dir,
            Def = def,
            Stages = tasks,
            Fixtures = fixtures,
            ProgramTemplate = template,
            Hash = HashDefinition(root, dir, def, tasks, fixtures.Values),
            Axes = axes,
            Assistants = assistants,
            StageHashes = tasks.ToDictionary(c => c.Id!, _ => JobHash(root, dir, def, tasks, def.Fixture is null ? null : fixtures[def.Fixture])),
        };
    }

    /// <summary>
    /// What an oracle stage needs of the program template. The exam's oracle block owns the oracle's directives, which
    /// proctor prepends, so the template may not state its own. Where nb runs through Harbor the prompt is sent by the
    /// simulated user rather than run by the program, so the template's one run must be the last directive and run the
    /// stage's prompt as it is, or the bridge would run something other than what the bare path does.
    /// </summary>
    static List<string> OracleTemplateProblems(string template, bool bridged)
    {
        var problems = new List<string>();
        var directives = Directives(template);
        if (directives.Any(d => Verb(d) == "oracle"))
            problems.Add("an oracle directive with a stage naming an answer sheet: the exam's oracle block sets the oracle; remove the directive");
        if (directives.Any(d => Regex.IsMatch(d, @"^budget\s+oracle_turns\b")))
            problems.Add("budget oracle_turns with a stage naming an answer sheet: set oracle.turns in exam.json instead");
        if (bridged && (directives.Count(d => Verb(d) == "run") != 1 || directives.LastOrDefault() != "run {{prompt}}"))
            problems.Add("a stage with an answer sheet, where nb runs through Harbor, needs the template to end in its one run, exactly `run {{prompt}}`: the simulated user sends the prompt over the bridge");
        return problems;
    }

    /// <summary>The template's directives: comments and blank lines dropped, backslash continuations joined.</summary>
    static List<string> Directives(string template)
    {
        var directives = new List<string>();
        var pending = "";
        foreach (var raw in template.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (pending.Length == 0 && (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))) continue;
            if (line.EndsWith('\\')) { pending += line[..^1] + " "; continue; }
            directives.Add((pending + line).Trim());
            pending = "";
        }
        if (pending.Length > 0) directives.Add(pending.Trim());
        return directives;
    }

    static string Verb(string directive) => directive.Split(' ', 2)[0];

    /// <summary>
    /// The stages, in the order exam.json lists them, which is the order they run in. Each comes back on the fixture it
    /// runs in (the exam's, or the one its condition names on a world axis), its about as its description. A stage has
    /// no fixture or labels of its own: the workspace is the job's, and a stage is never run or counted alone.
    /// </summary>
    private static List<StageDef> LoadStages(ExamDef def, string dir, string file, List<Problem> problems)
    {
        void Add(string field, string message) => problems.Add(new Problem(file, field, message));
        var stages = new List<StageDef>();
        if (Directory.Exists(Layout.TasksDirIn(dir)) && Directory.GetFiles(Layout.TasksDirIn(dir), "*.json").Length > 0)
            Add("stages", "this exam also has tasks/*.json, the earlier shape; its work is the stages listed here, so move each in and delete the files");
        if (def.Fixture is not null && !Regex.IsMatch(def.Fixture, "^[a-z0-9][a-z0-9-]*$"))
            Add("fixture", "a fixture is named by its directory under fixtures/: lowercase letters, digits and hyphens");
        if (def.Stages!.Count == 0) Add("stages", "at least one stage is required");
        foreach (var (stage, i) in def.Stages.Select((s, i) => (s, i)))
        {
            var at = $"stages[{i}].";
            if (stage is null) { Add($"stages[{i}]", "a stage is an object, not null"); continue; }
            if (string.IsNullOrWhiteSpace(stage.Id)) Add(at + "id", "required");
            else if (!Regex.IsMatch(stage.Id, "^[a-z0-9][a-z0-9-]*$")) Add(at + "id", "stage ids are path segments: lowercase letters, digits and hyphens");
            else if (stages.Any(s => s.Id == stage.Id)) Add(at + "id", $"'{stage.Id}' is already a stage of this exam");
            if (stage.Fixture is not null) Add(at + "fixture", "the job's, not a stage's: every stage runs in one workspace, set up once; set fixture in exam.json");
            if (stage.Labels is not null) Add(at + "labels", "the exam's, not a stage's: a stage is never run or counted alone");
            if (stage.Description is not null) Add(at + "description", "a stage's sentence is about");
            if (stage.About is not null && string.IsNullOrWhiteSpace(stage.About)) Add(at + "about", "a sentence saying what the stage asks, not an empty string");
            ValidateTask(stage, dir, (field, message) => Add(at + field, message));
            stages.Add(stage with { Fixture = Axes.FixturePinned(def, stage) ?? def.Fixture, Description = stage.About, File = Path.Combine(dir, Layout.ExamFile), At = at });
        }
        return stages;
    }

    /// <summary>A stage's own: a prompt, an answer sheet that resolves, and checks that are well formed.</summary>
    static void ValidateTask(StageDef c, string dir, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(c.Prompt)) add("prompt", "required");
        if (c.Oracle is not null && OracleSheetProblem(dir, c.Oracle) is { } sheetProblem) add("oracle", sheetProblem);
        foreach (var (name, spec) in c.Checks ?? [])
        {
            var f = $"checks.{name}";
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) add(f, "check names are lowercase letters, digits, hyphens and underscores");
            foreach (var (field, problem) in Checks.ValidateCheck(spec, dir)) add(field.Length == 0 ? f : $"{f}.{field}", problem);
        }
    }

    /// <summary>Stage ids that will not read as a label on screen (plans/bank.md, B9): over 20 characters, or only a number or a place ("stage-12", "q3").</summary>
    public List<string> UnreadableStageIds() => Stages.Select(c => c.Id!).Where(id => id.Length > 20 || Regex.IsMatch(id, @"^([a-z]+-?)?\d+$")).ToList();

    /// <summary>A sheet inside the exam directory with at least one headed entry, which is what nb's answer sheet selects.</summary>
    static string? OracleSheetProblem(string dir, string oracle)
    {
        var full = Path.GetFullPath(Path.Combine(dir, oracle));
        var examDir = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(oracle) || Path.IsPathRooted(oracle) || !full.StartsWith(examDir, StringComparison.Ordinal))
            return "an answer sheet is a path inside the exam directory, relative to it, so it never reaches a fixture or the agent's container";
        if (!File.Exists(full)) return $"answer sheet not found: {oracle}";
        if (!File.ReadLines(full).Any(l => l.StartsWith('#')))
            return "the answer sheet has no headed entries (## <id>, then the answer), so the oracle could select nothing";
        return null;
    }

    private static void ValidateDef(ExamDef def, string root, string dir, string file, string examId, List<StageDef> tasks, List<Assistant> assistants, Dictionary<string, Fixture> fixtures, List<Problem> problems)
    {
        void Add(string field, string message) => problems.Add(new Problem(file, field, message));

        if (string.IsNullOrWhiteSpace(def.Id)) Add("id", "required");
        else if (def.Id != examId) Add("id", $"'{def.Id}' does not match the directory name '{examId}'");

        if (def.Description is not null && string.IsNullOrWhiteSpace(def.Description)) Add("description", "a description is a sentence, not an empty string");
        if (def.Tags is not null) Add("tags", "removed: an exam's own metadata is labels, and whether it is judged follows from its checks");
        Labels.Validate(def.Labels, Add);

        if (def.Arms is not null) Add("arms", "removed: what is under test is declared under \"axes\", each arm's fields a value of an axis, and the ones to run are named under \"configurations\"");
        if (def.Tries is < 1) Add("tries", "must be at least 1");

        ValidateHarbor(def, Add, dir, tasks, assistants, fixtures, root, problems);
        ValidateOracle(def, Add, tasks, assistants, fixtures, (c, field, message) => problems.Add(new Problem(Path.GetRelativePath(root, c.File!), c.At + field, message)),
            (f, field, message) => problems.Add(new Problem(Path.GetRelativePath(root, Path.Combine(f.Dir, Layout.FixtureFile)), field, message)));

        if (def.Hooks?.Run is not null) Add("hooks.run", "run-level hooks are not built; use configuration or try");
        if (def.Hooks?.Task is not null) Add("hooks.task", "a hook around one stage is not built; use configuration or try");
        if (def.Hooks?.Arm is not null) Add("hooks.arm", "renamed: hooks.configuration, which runs once around each configuration's tries");
        if (def.Hooks?.Sample is not null) Add("hooks.sample", "renamed: hooks.try, which runs once around each try, its setup before the first stage and its teardown after the last");
        foreach (var (level, pair) in new[] { ("configuration", def.Hooks?.Configuration), ("try", def.Hooks?.Try) })
        {
            if (pair is null) continue;
            foreach (var (kind, script) in new[] { ("setup", pair.Setup), ("teardown", pair.Teardown) })
                if (script is not null && !File.Exists(Path.Combine(dir, script)))
                    Add($"hooks.{level}.{kind}", $"script not found: {script}");
        }

        if (def.Nb?.Runner is { } runner && !File.Exists(Path.Combine(dir, runner)))
            Add("nb.runner", $"script not found: {runner}");
        if (def.Nb?.Mounts?.Work is { } mount && !Path.IsPathRooted(mount))
            Add("nb.mounts.work", "a mount is an absolute path inside the container");
        if (def.Nb?.Mounts?.Bundle is not null)
            Add("nb.mounts.bundle", "removed with the bundle: a value's files are laid over the checkout, so they are under nb.mounts.work");

        foreach (var (pattern, n) in (def.Surface?.Fails ?? []).Select((p, n) => (p, n)))
            try { _ = new Regex(pattern); }
            catch (ArgumentException e) { Add($"surface.fails[{n}]", $"not a regular expression: {e.Message}"); }

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
                Add($"grading.checks.{name}", $"also declared by fixture '{fixture.Id}'; a check is declared at one level: the exam's, a fixture's or a stage's");
        foreach (var c in tasks)
        {
            var fixture = c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture);
            foreach (var name in (c.Checks ?? []).Keys)
            {
                if (def.Grading.Checks.ContainsKey(name)) problems.Add(new Problem(Path.GetRelativePath(root, c.File!), $"{c.At}checks.{name}", "also declared by the exam; a check is declared at one level: the exam's, a fixture's or a stage's"));
                else if (fixture?.Checks.ContainsKey(name) == true) problems.Add(new Problem(Path.GetRelativePath(root, c.File!), $"{c.At}checks.{name}", $"also declared by fixture '{fixture.Id}'; a check is declared at one level: the exam's, a fixture's or a stage's"));
            }
        }

        // A correctness or integrity check the exam does not declare must reach every stage: from its fixture or its own block.
        string? Undeclared(string name)
        {
            if (def.Grading.Checks.ContainsKey(name)) return null;
            foreach (var c in tasks)
            {
                if (c.Checks?.ContainsKey(name) == true) continue;
                var fixture = c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture);
                if (fixture is null) return $"'{name}' is not a declared check, and stage '{c.Id}' neither declares it nor names a fixture that could";
                if (!fixture.Checks.ContainsKey(name)) return $"'{name}' is not a declared check, and neither fixture '{fixture.Id}' nor stage '{c.Id}' declares it";
            }
            return tasks.Count == 0 ? $"'{name}' is not a declared check" : null;
        }
        if (def.Grading.Pass is not null) Add("grading.pass", "renamed: these are \"correctness\", the checks that together decide right or wrong");
        if (def.Grading.Validity is not null) Add("grading.validity", "renamed: these are \"integrity\", the checks that decide whether a try counts at all");
        if (def.Grading.Correctness is null or { Count: 0 }) Add("grading.correctness", "required: the checks that together decide whether the stage was completed as intended");
        foreach (var name in def.Grading.Correctness ?? [])
            if (name is null) Add("grading.correctness", "a check name, not null");
            else if (Undeclared(name) is { } why) Add("grading.correctness", why);
            else if (NbOnlyBesideVendor(name)) Add("grading.correctness", $"'{name}' reads only nb's loop nudge, which a vendor agent's attempts do not carry, so they could never pass; leave it out of the lists, as an observation");
        foreach (var name in def.Grading.Integrity ?? [])
        {
            if (name is null) Add("grading.integrity", "a check name, not null");
            else if (Undeclared(name) is { } why) Add("grading.integrity", why);
            else if (def.Grading.Correctness?.Contains(name) == true) Add("grading.integrity", $"'{name}' is also in correctness; a check has one role");
            else if (NbOnlyBesideVendor(name)) Add("grading.integrity", $"'{name}' reads only nb's loop nudge, which a vendor agent's attempts do not carry; leave it out of the lists, as an observation");
        }

        foreach (var name in def.Grading.Quality ?? [])
        {
            if (name is null) Add("grading.quality", "a check name, not null");
            else if (Undeclared(name) is { } why) Add("grading.quality", why);
            else if (def.Grading.Correctness?.Contains(name) == true || def.Grading.Integrity?.Contains(name) == true) Add("grading.quality", $"'{name}' is also in {(def.Grading.Correctness?.Contains(name) == true ? "correctness" : "integrity")}; a check has one role");
        }
        // Correctness has no fuzziness: a check that asks a model can prefer, and so be quality, never decide right or wrong.
        foreach (var name in def.Grading.Correctness ?? [])
            if (name is not null && SpecsOf(name).Any(s => s is not null && Checks.AsksAModel(s)))
                Add("grading.correctness", $"'{name}' asks a model (judge or decide), and correctness is decided by deterministic checks only; name it under \"quality\"");

        IEnumerable<JsonObject?> SpecsOf(string name) =>
            def.Grading.Checks.TryGetValue(name, out var spec) ? [spec] : tasks.Select(c => c.Checks?.GetValueOrDefault(name) ?? (c.Fixture is null ? null : fixtures.GetValueOrDefault(c.Fixture)?.Checks.GetValueOrDefault(name)));
        bool NbOnlyBesideVendor(string name) =>
            assistants.Any(v => v.IsVendor)
            && SpecsOf(name).Any(s => s is not null && Checks.NbOnly(s));
    }

    /// <summary>Who does the work in a configuration and how it is reached (<see cref="Axes.Assistant"/>): a vendor's agent, nb through Harbor, or nb bare.</summary>
    static void ValidateWho(Assistant assistant, Action<string, string> add)
    {
        if (assistant.IsVendor) { ValidateVendor(assistant, add); return; }
        if (assistant.IsAgent) { ValidateNbThroughHarbor(assistant, add); return; }
        if (string.IsNullOrWhiteSpace(assistant.Provider)) add("provider", "required for nb (a provider entry name in nb's config)");
    }

    /// <summary>
    /// A vendor's agent: a model of the agent's own vendor, with no provider prefix (Harbor passes it to the gateway
    /// verbatim), at a pinned agent version. The nb fields have no meaning on it.
    /// </summary>
    static void ValidateVendor(Assistant assistant, Action<string, string> add)
    {
        var (vendor, models, example) = assistant.Agent switch
        {
            Assistant.ClaudeCode => ("an Anthropic", @"^claude-", "claude-haiku-5-5"),
            Assistant.Codex => ("an OpenAI", @"^(gpt-|o\d|codex-)", "gpt-5-mini"),
            Assistant.QwenCode => ("a Qwen", @"^qwen", "qwen3-coder-next"),
            _ => (null, null, null),
        };
        if (vendor is null) { add("agent", $"unknown agent '{assistant.Agent}'; known: {Assistant.Nb}, {string.Join(", ", Assistant.Vendors)}"); return; }
        if (assistant.Provider is not null) add("provider", $"an nb field; agent {assistant.Agent} is that agent, on the model it names");
        if (string.IsNullOrWhiteSpace(assistant.Model)) add("model", $"required for agent {assistant.Agent}");
        else if (assistant.Model.Contains('/')) add("model", $"'{assistant.Model}' has a provider prefix; name the model bare ({example}), as the gateway receives it");
        else if (!Regex.IsMatch(assistant.Model, models!)) add("model", $"'{assistant.Model}' is not {vendor} model; {assistant.Agent} runs {vendor[(vendor.IndexOf(' ') + 1)..]} models only, so the harness and the model are one vendor's");
        if (string.IsNullOrWhiteSpace(assistant.AgentVersion)) add("agent_version", $"required: the {assistant.Agent} release under test, pinned (there is no latest)");
        else if (!Regex.IsMatch(assistant.AgentVersion, @"^\d+(\.\d+)*$")) add("agent_version", $"'{assistant.AgentVersion}' is not a version number");
    }

    /// <summary>
    /// nb as a Harbor agent: the harness and provider as on the bare path, the model optional likewise. The version is
    /// the binary's, so it is not a value's to say.
    /// </summary>
    static void ValidateNbThroughHarbor(Assistant assistant, Action<string, string> add)
    {
        if (assistant.AgentVersion is not null) add("agent_version", "nb's version is the binary's (auspex.json harbor.nb), which must match the host nb; agent_version pins a vendor agent");
        if (string.IsNullOrWhiteSpace(assistant.Provider)) add("provider", "required for nb (a provider entry name in nb's config)");
    }

    /// <summary>
    /// What running through Harbor needs from the rest of the exam: a pinned Harbor, a wall limit (Harbor's agent timeout) and an
    /// image on every stage's fixture. An attempt hook runs on the host, which an agent's container never sees; the setup it
    /// did belongs in the fixture's image.
    /// </summary>
    static void ValidateHarbor(ExamDef def, Action<string, string> add, string dir, List<StageDef> tasks, List<Assistant> assistants, Dictionary<string, Fixture> fixtures, string root, List<Problem> problems)
    {
        // Only the configurations the exam carries stand in `assistants`, so a value that names an agent or the Harbor route counts too.
        var agents = assistants.Any(v => v.IsAgent) || def.Axes!.Values.Any(a => a?.Values?.Values.Any(v => v?.Agent is not null || v?.Route == ValueDef.Harbor) == true);
        if (def.Harbor is { } harbor && (string.IsNullOrWhiteSpace(harbor.Version) || !Regex.IsMatch(harbor.Version, @"^\d+(\.\d+)*$")))
            add("harbor.version", "required: the Harbor release, pinned (0.26.0)");
        foreach (var (field, value) in new (string, object?)[] { ("tool_calls", def.Limits?.ToolCalls), ("tokens", def.Limits?.Tokens), ("cost_usd", def.Limits?.CostUsd) })
            if (value is not null) add($"limits.{field}", "not yet implemented; only wall_ms is enforced in this version");
        if (def.Limits?.WallMs is < 1000) add("limits.wall_ms", "at least 1000");
        if (!agents)
        {
            if (def.Harbor is not null) add("harbor", "no value names an agent or the harbor route, so nothing runs through Harbor");
            if (def.Limits is not null) add("limits", "applies through Harbor; bare, nb's limits are budget directives in the program");
            return;
        }
        if (def.Harbor is null) add("harbor.version", "required where a value runs through Harbor: the Harbor release it runs on, pinned (0.26.0)");
        if (def.Limits?.WallMs is null) add("limits.wall_ms", "required where a value runs through Harbor: how long each stage may take");
        if (def.Hooks?.Try is not null) add("hooks.try", "not yet through Harbor: this hook runs on the host, the agent in its container; move the setup into the fixture's image");
        foreach (var c in tasks)
        {
            if (c.Fixture is null) problems.Add(new Problem(Path.GetRelativePath(root, c.File!), "fixture", "required through Harbor: the agent runs in the fixture's image"));
            else if (fixtures.GetValueOrDefault(c.Fixture) is { Def.Image: null } fixture)
                problems.Add(new Problem(Path.GetRelativePath(root, Path.Combine(fixture.Dir, Layout.FixtureFile)), "image", "required where this exam runs through Harbor: {from, run, env}, the image the agent and the checks run in"));
        }
    }

    /// <summary>
    /// The oracle block needs a stage to serve, and a stage with a sheet needs an assistant that can take one: nb, bare or through
    /// Harbor. A vendor agent can be a bridge target, but bridged it leaves no ATIF (Harbor converts the user agent's
    /// log, not the target's), and Qwen Code cannot be a target at all (plans/oracle-in-proctor.md, question 4).
    /// </summary>
    static void ValidateOracle(ExamDef def, Action<string, string> add, List<StageDef> tasks, List<Assistant> assistants, Dictionary<string, Fixture> fixtures,
        Action<StageDef, string, string> addToTask, Action<Fixture, string, string> addToFixture)
    {
        var oracleTasks = tasks.Where(c => c.Oracle is not null).Select(c => c.Id).ToList();
        if (def.Oracle is { } oracle)
        {
            if (oracleTasks.Count == 0) add("oracle", "no stage names an answer sheet (a stage's oracle), so there is nothing for the oracle to serve");
            if (oracle.Turns is < 1) add("oracle.turns", "must be at least 1");
            if (oracle.Judge is not null && string.IsNullOrWhiteSpace(oracle.Judge)) add("oracle.judge", "an nb config entry name, or absent for the provider that does the work");
        }
        if (oracleTasks.Count == 0)
        {
            // A check that can never fire is still an error: nothing would answer it.
            const string never = "an oracle check, and no stage names an answer sheet (a stage's oracle), so the oracle never answers";
            static IEnumerable<string> Oracular(Dictionary<string, JsonObject>? checks) =>
                (checks ?? []).Where(k => k.Value?.Any(f => Checks.IsOracleCheck(f.Key)) == true).Select(k => k.Key);
            foreach (var name in Oracular(def.Grading?.Checks)) add($"grading.checks.{name}", never);
            foreach (var fixture in fixtures.Values) foreach (var name in Oracular(fixture.Checks)) addToFixture(fixture, $"checks.{name}", never);
            foreach (var c in tasks) foreach (var name in Oracular(c.Checks)) addToTask(c, $"checks.{name}", never);
            return;
        }
        // Through Harbor a try with an answer sheet must be one stage: the simulated user answers a task, never a step of one. On an
        // exam with axes a try is the stages whose condition holds in its configuration, so each configuration is looked at for itself.
        List<StageDef> StagesOf(Assistant v)
        {
            var values = v.Id!.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            return tasks.Where(s => s.When is null || When.Parse(s.When, out _) is not { } when || when.Terms.All(t => (values.GetValueOrDefault(t.Axis) == t.Value) != t.Not)).ToList();
        }
        if (assistants.FirstOrDefault(a => a.IsAgent && StagesOf(a) is { Count: > 1 } several && several.Any(s => s.Oracle is not null)) is { } staged)
            add("axes", $"not yet: '{staged.Id}' runs through Harbor a try of {StagesOf(staged).Count} stages, and one of them names an answer sheet; Harbor's simulated user answers one task, not a step of several. "
                + "Give it a try of one stage (a stage's `when`), or run it bare, where nb's own oracle answers");
        foreach (var assistant in assistants.Where(a => a.IsVendor && StagesOf(a).Any(s => s.Oracle is not null)))
            add("axes", $"not yet: '{assistant.Id}' is {assistant.Agent}, and stage {StagesOf(assistant).First(s => s.Oracle is not null).Id} names an answer sheet, and an oracle runs on nb only. Through Harbor's simulated user, {assistant.Agent} would leave no ATIF trajectory to grade (plans/oracle-in-proctor.md)");
    }

    /// <summary>
    /// A job's identity is what an agent meets when it attempts it: each prompt in order, the answer sheets an oracle
    /// reads from, the program template, the hooks, and the fixture (how it is built, and its source by content). An
    /// exam is one job of all its stages, by id and prompt, so changing, adding, moving or renaming one stage is a
    /// different job (plans/bank.md, B2). Not the checks, their scripts, the expected values
    /// or a stage's about: those are the standard an attempt is graded by and how it is labelled, so a change there
    /// means grading again, not attempting again.
    /// </summary>
    static string JobHash(string root, string dir, ExamDef def, List<StageDef> prompts, Fixture? fixture)
    {
        var files = new List<string> { Path.Combine(dir, Layout.ProgramTemplateFile) };
        files.AddRange(prompts.Where(c => c.Oracle is not null).Select(c => Path.Combine(dir, c.Oracle!)));
        foreach (var pair in new[] { def.Hooks?.Configuration, def.Hooks?.Try })
            foreach (var script in new[] { pair?.Setup, pair?.Teardown })
                if (script is not null) files.Add(Path.Combine(dir, script));
        var built = fixture is null ? "" : JsonSerializer.Serialize(new { fixture.Id, fixture.Def.Source, fixture.Def.Stack, fixture.Def.Image, fixture.Hash }, JsonOptions);
        var met = string.Join("\0", prompts.Select(c => c.Id + "\0" + c.Prompt + "\0" + c.Oracle));
        return HashFiles(root, files.Where(File.Exists), met + "\0" + built);
    }

    /// <summary>Every definition file: the exam's own, plus each named fixture's fixture.json and check scripts. Not the fixture's source tree; that is the fixture hash, recorded per stage of a try.</summary>
    private static string HashDefinition(string root, string dir, ExamDef def, List<StageDef> tasks, IEnumerable<Fixture> fixtures)
    {
        var files = new List<string> { Layout.ExamFileIn(dir), Path.Combine(dir, Layout.ProgramTemplateFile) };
        files.AddRange(tasks.Where(c => c.Oracle is not null).Select(c => Path.Combine(dir, c.Oracle!)));
        foreach (var spec in def.Grading!.Checks!.Values.Concat(tasks.SelectMany(c => (c.Checks ?? []).Values)))
            if (spec["script"] is JsonValue v && v.TryGetValue<string>(out var script)) files.Add(Path.Combine(dir, script));
        foreach (var pair in new[] { def.Hooks?.Configuration, def.Hooks?.Try })
            foreach (var script in new[] { pair?.Setup, pair?.Teardown })
                if (script is not null) files.Add(Path.Combine(dir, script));
        foreach (var fixture in fixtures) files.AddRange(fixture.DefinitionFiles());
        return HashFiles(root, files);
    }

    /// <summary>sha256 over files by their path from the root and their bytes, in path order, then any text given.</summary>
    static string HashFiles(string root, IEnumerable<string> files, string text = "")
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.Select(Path.GetFullPath).Distinct().Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/') + "\0"));
            sha.AppendData(File.ReadAllBytes(file));
            sha.AppendData("\0"u8);
        }
        sha.AppendData(Encoding.UTF8.GetBytes(text));
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
