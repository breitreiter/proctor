using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// Axes, values and configurations (project/plans/axes.md; the declaration is project/plans/axes-ports.md).
// An exam of stages may declare axes in place of variants/: each axis a named thing with several values, a
// configuration one value from each. Slice 1 of axes-build.md: declared, validated and listed. Nothing runs yet.

/// <summary>
/// A parameter the base declares, in fixture.json or (for the program template) exam.json: the only names a value may
/// set. Text unless Tree, which is a directory of files. Secret is passed by name and kept out of the bank.
/// </summary>
record ParamDef(JsonNode? Default = null, bool? Tree = null, bool? Secret = null, string? About = null);

/// <summary>A tree of files a value carries: a directory under the exam, or a git revision and optionally paths in it. To is where an overlay lands in the checkout.</summary>
record FilesSource(string? Path = null, string? Git = null, string? Rev = null, List<string>? Paths = null, string? To = null)
{
    public BundleSource AsBundle() => new(Path, Git, Rev, Paths);
}

/// <summary>
/// One choice on an axis. Set names parameters the base declares, which the base's own scripts act on. Files is laid
/// over the checkout by proctor. Agent to Harness say who does the work, and Fixture names a whole base, for an axis of
/// worlds; those are proctor's own. Note is for a reader and no part of the value's identity.
/// </summary>
record ValueDef(string? Note = null, Dictionary<string, JsonNode?>? Set = null, FilesSource? Files = null, string? Agent = null, string? AgentVersion = null,
    string? Route = null, string? Model = null, string? Provider = null, string? Harness = null, string? Fixture = null)
{
    public const string Bare = "bare", Harbor = "harbor";

    /// <summary>The fields that are proctor's own, with what this value gives each.</summary>
    public IEnumerable<(string Key, string? Value)> Own() =>
        [("agent", Agent), ("agent_version", AgentVersion), ("route", Route), ("model", Model), ("provider", Provider), ("harness", Harness), ("fixture", Fixture)];
}

/// <summary>An axis: its values in declared order, and optionally the live one, its reference. Without a live value it is swept.</summary>
record AxisDef(string? About = null, string? Live = null, Dictionary<string, ValueDef?>? Values = null);

/// <summary>One value from each axis, in the order the exam declares its axes: what an attempt ran as. Its identity is its values' content.</summary>
sealed record Configuration(IReadOnlyList<(string Axis, string Value)> Values, string? Name, bool Excluded, string Hash)
{
    /// <summary>As files and commands spell it: axis=value, comma-separated.</summary>
    public string Key => string.Join(",", Values.Select(v => $"{v.Axis}={v.Value}"));
    /// <summary>As a person reads it.</summary>
    public string Label => string.Join(" × ", Values.Select(v => $"{v.Axis}={v.Value}"));
    public string? this[string axis] => Values.FirstOrDefault(v => v.Axis == axis).Value;
    public bool Has(IReadOnlyDictionary<string, string> values) => values.All(v => this[v.Key] == v.Value);
}

/// <summary>A stage's or an exclusion's condition: every term must hold. A term is axis = value or axis != value.</summary>
sealed record When(IReadOnlyList<(string Axis, bool Not, string Value)> Terms)
{
    public bool Holds(Configuration c) => Terms.All(t => (c[t.Axis] == t.Value) != t.Not);

    /// <summary>Parse `axis = value [and axis != value …]`, or say what is wrong with it.</summary>
    public static When? Parse(string text, out string? problem)
    {
        problem = null;
        var terms = new List<(string, bool, string)>();
        foreach (var part in Regex.Split(text.Trim(), @"\s+and\s+"))
        {
            var m = Regex.Match(part, @"^([a-z][a-z0-9_-]*)\s*(!=|=)\s*([A-Za-z0-9][A-Za-z0-9._-]*)$");
            if (!m.Success)
            {
                problem = Regex.IsMatch(part, "[<>]")
                    ? $"'{part}': only = and != are understood; a value is a name, not a number to compare"
                    : $"'{part}' is not a condition; write `axis = value` or `axis != value`, joined by `and`";
                return null;
            }
            terms.Add((m.Groups[1].Value, m.Groups[2].Value == "!=", m.Groups[3].Value));
        }
        return new When(terms);
    }
}

/// <summary>An exam's axes, loaded: the declarations, every configuration, and which stages apply in each.</summary>
sealed class Axes
{
    public required Dictionary<string, AxisDef> Def { get; init; }
    /// <summary>Every configuration the axes span, in declared order, the excluded ones marked.</summary>
    public required List<Configuration> Configurations { get; init; }
    /// <summary>Each value's identity by (axis, value): a hash of what it sets and of the files it carries, never its note.</summary>
    public required Dictionary<(string Axis, string Value), string> ValueHashes { get; init; }
    /// <summary>Each stage's condition by stage id; a stage without one applies everywhere.</summary>
    public required Dictionary<string, When> Conditions { get; init; }
    /// <summary>The axis whose values each name a fixture, or null when the exam has one fixture.</summary>
    public string? WorldAxis { get; init; }

    public IEnumerable<Configuration> Wanted => Configurations.Where(c => !c.Excluded);
    public Configuration? Of(string key) => Configurations.FirstOrDefault(c => c.Key == key);

    /// <summary>The files a configuration's values lay over the checkout, in the order the axes are declared.</summary>
    public List<(string Dir, string? To)> Overlays(Configuration c, string examDir) =>
        c.Values.Select(v => Value(v.Axis, v.Value).Files).OfType<FilesSource>().Where(f => f.Path is not null)
            .Select(f => (Path.GetFullPath(Path.Combine(examDir, f.Path!)), f.To)).ToList();

    /// <summary>
    /// Every parameter the base declares, as a configuration has it: the declared default, or what one of its values
    /// sets. Text as it is; a tree as the directory's path on the host; a secret as the value of the variable it names.
    /// </summary>
    public Dictionary<string, string> Params(Configuration c, ExamDef def, Fixture? fixture, string examDir)
    {
        var result = new Dictionary<string, string>();
        void Give(string name, ParamDef declared, JsonNode? given, string dir) => result[name] =
            given is null ? ""
            : declared.Tree == true ? given.Deserialize<FilesSource>(Exam.JsonOptions)?.Path is { } p ? Path.GetFullPath(Path.Combine(dir, p)) : ""
            : declared.Secret == true ? Environment.GetEnvironmentVariable(given.ToString()) ?? ""
            : given.ToString();
        foreach (var (name, declared) in fixture?.Def.Params ?? []) Give(name, declared, declared.Default, fixture!.Dir);
        foreach (var (name, declared) in def.Params ?? []) Give(name, declared, declared.Default, examDir);
        foreach (var (axis, value) in c.Values)
            foreach (var (name, given) in Value(axis, value).Set ?? [])
                Give(name, def.Params?.GetValueOrDefault(name) ?? fixture!.Def.Params![name], given, examDir);
        return result;
    }

    /// <summary>A configuration as a log entry and a manifest keep it: each axis's value by name and by the hash that is its identity.</summary>
    public Dictionary<string, Ref> Seen(Configuration c) => c.Values.ToDictionary(v => v.Axis, v => new Ref(v.Value, ValueHashes[v]));

    /// <summary>Why a configuration cannot be run yet, or null: what later slices of plans/axes-build.md build.</summary>
    public string? NotYet(Configuration c)
    {
        if (Assistant(c).IsAgent) return "runs through Harbor, which does not read a configuration yet (plans/axes-build.md, slice 4)";
        foreach (var (axis, value) in c.Values)
            if (Value(axis, value) is var v && (v.Files?.Git is not null || (v.Set ?? []).Values.Any(g => g is JsonObject o && o["git"] is not null)))
                return $"{axis}={value} takes its files from a git revision, which is not fetched yet; give it a directory";
        return null;
    }
    public ValueDef Value(string axis, string value) => Def[axis].Values![value]!;
    public bool Applies(TaskDef stage, Configuration c) => !Conditions.TryGetValue(stage.Id!, out var when) || when.Holds(c);
    /// <summary>The fixture a configuration runs in: its world value's, or the exam's.</summary>
    public string? FixtureOf(Configuration c, string? examFixture) => WorldAxis is null ? examFixture : Value(WorldAxis, c[WorldAxis]!).Fixture;

    const string AxisName = "^[a-z][a-z0-9_-]*$", ValueName = "^[a-z0-9][a-z0-9._-]*$", ParamName = "^[A-Za-z_][A-Za-z0-9_]*$";

    /// <summary>The fixtures the values of a world axis name, so the exam can load them before the rest is validated.</summary>
    public static IEnumerable<string> FixturesNamed(ExamDef def) =>
        (def.Axes ?? []).Values.SelectMany(a => a?.Values?.Values.AsEnumerable() ?? []).Select(v => v?.Fixture).OfType<string>().Where(f => Regex.IsMatch(f, "^[a-z0-9][a-z0-9-]*$")).Distinct();

    /// <summary>The fixture a stage's condition pins it to, when the exam has a world axis and the condition names one value on it.</summary>
    public static string? FixturePinned(ExamDef def, TaskDef stage)
    {
        if (stage.When is null || When.Parse(stage.When, out _) is not { } when) return null;
        foreach (var (axis, not, value) in when.Terms)
            if (!not && def.Axes?.GetValueOrDefault(axis)?.Values?.GetValueOrDefault(value)?.Fixture is { } fixture) return fixture;
        return null;
    }

    /// <summary>
    /// Validate the declarations against the base and build the configurations. Every refusal names its field in
    /// exam.json and says what to change. Returns null when anything is wrong.
    /// </summary>
    public static Axes? Load(ExamDef def, string root, string dir, List<TaskDef> stages, Dictionary<string, Fixture> fixtures, Action<string, string> add)
    {
        var problems = 0;
        void Add(string field, string message) { problems++; add(field, message); }
        var axes = def.Axes!;

        if (def.Stages is null) Add("axes", "goes with stages: an exam with axes is one job of stages; move tasks/*.json into stages first");
        var variantsDir = Layout.VariantsDirIn(dir);
        if (Directory.Exists(variantsDir) && Directory.GetFiles(variantsDir, "*.json").Length > 0)
            Add("axes", "this exam also has variants/*.json; with axes, what is under test is declared here: make each variant's fields a value and delete the files");
        if (axes.Count == 0) Add("axes", "at least one axis is required");

        // Each axis and value on its own.
        foreach (var (axisName, axis) in axes)
        {
            var at = $"axes.{axisName}";
            if (!Regex.IsMatch(axisName, AxisName)) Add(at, "an axis is named in lowercase letters, digits, hyphens and underscores, starting with a letter");
            if (axis is null) { Add(at, "an axis is an object: {about, live, values}"); continue; }
            if (axis.About is not null && string.IsNullOrWhiteSpace(axis.About)) Add($"{at}.about", "a sentence saying what the axis is, not an empty string");
            if (axis.Values is null or { Count: 0 }) { Add($"{at}.values", "at least one value is required"); continue; }
            if (axis.Live is not null && !axis.Values.ContainsKey(axis.Live)) Add($"{at}.live", $"'{axis.Live}' is not a value of this axis; declared: {string.Join(", ", axis.Values.Keys)}");
            foreach (var (valueName, value) in axis.Values)
            {
                var v = $"{at}.values.{valueName}";
                if (!Regex.IsMatch(valueName, ValueName)) Add(v, "a value is named in lowercase letters, digits, dots, hyphens and underscores");
                if (value is null) { Add(v, "a value is an object; one that sets nothing is {} or {\"note\": …}"); continue; }
                if (value.Note is not null && string.IsNullOrWhiteSpace(value.Note)) Add($"{v}.note", "a note is a sentence, not an empty string");
                if (value.Route is not null and not ValueDef.Bare and not ValueDef.Harbor) Add($"{v}.route", $"'{value.Route}' is not a route; known: {ValueDef.Bare}, {ValueDef.Harbor}");
                if (value.Fixture is not null && !Regex.IsMatch(value.Fixture, "^[a-z0-9][a-z0-9-]*$")) Add($"{v}.fixture", "a fixture is named by its directory under fixtures/: lowercase letters, digits and hyphens");
                ValidateFiles(value.Files, dir, $"{v}.files", Add);
            }
        }

        // The world axis: at most one, and then every value of it names a fixture and the exam names none.
        var worlds = axes.Where(a => a.Value?.Values?.Values.Any(v => v?.Fixture is not null) == true).Select(a => a.Key).ToList();
        if (worlds.Count > 1) Add($"axes.{worlds[1]}", $"axis '{worlds[0]}' already names the fixture; one axis may be the worlds an exam runs in");
        var world = worlds.FirstOrDefault();
        if (world is not null)
        {
            if (def.Fixture is not null) Add("fixture", $"axis '{world}' names the fixture per value; an exam has one or the other");
            foreach (var (valueName, value) in axes[world]!.Values!)
                if (value is not null && value.Fixture is null) Add($"axes.{world}.values.{valueName}.fixture", $"required: every value of '{world}' names a fixture, since some do");
        }

        // Refusal 2: values of different axes cannot set the same thing. A parameter, one of proctor's own fields, or a path.
        var setBy = new Dictionary<string, string>();
        var paths = new Dictionary<string, string>();
        foreach (var (axisName, axis) in axes)
        {
            var sets = new HashSet<string>();
            var written = new HashSet<string>();
            foreach (var (valueName, value) in axis?.Values ?? [])
            {
                foreach (var name in value?.Set?.Keys ?? Enumerable.Empty<string>()) sets.Add("parameter " + name);
                foreach (var (key, given) in value?.Own() ?? []) if (given is not null) sets.Add(key);
                if (value?.Files is { Path: { } p } files && Directory.Exists(Path.Combine(dir, p)))
                    foreach (var (relative, _) in Tree.Files(Path.Combine(dir, p), new HashSet<string> { ".git" }))
                        written.Add(files.To is null ? relative : files.To.Trim('/') + "/" + relative);
            }
            foreach (var thing in sets)
                if (!setBy.TryAdd(thing, axisName)) Add($"axes.{axisName}", $"sets {thing}, which axis '{setBy[thing]}' also sets; one thing belongs to one axis, or the two axes are one");
            foreach (var path in written)
                if (!paths.TryAdd(path, axisName)) Add($"axes.{axisName}", $"writes {path} in the checkout, which axis '{paths[path]}' also writes; a path belongs to one axis");
        }

        // The stages' conditions, the named configurations and the exclusions: each may name only what is declared.
        string? Undeclared(string axis, string value) =>
            !axes.TryGetValue(axis, out var a) || a is null ? $"'{axis}' is not a declared axis; declared: {string.Join(", ", axes.Keys)}"
            : a.Values?.ContainsKey(value) != true ? $"'{value}' is not a value of axis '{axis}'; declared: {string.Join(", ", a.Values?.Keys.AsEnumerable() ?? [])}"
            : null;

        var conditions = new Dictionary<string, When>();
        foreach (var stage in stages.Where(s => s.When is not null && s.Id is not null))
        {
            var when = When.Parse(stage.When!, out var problem);
            if (when is null) { Add(stage.At + "when", problem!); continue; }
            foreach (var (axis, _, value) in when.Terms)
                if (Undeclared(axis, value) is { } why) Add(stage.At + "when", why);
            conditions[stage.Id!] = when;
        }
        foreach (var (name, values) in def.Configurations ?? [])
        {
            var at = $"configurations.{name}";
            if (!Regex.IsMatch(name, ValueName)) Add(at, "a configuration is named in lowercase letters, digits, dots, hyphens and underscores");
            if (values is null) { Add(at, "a configuration is one value per axis: {axis: value, …}"); continue; }
            foreach (var (axis, value) in values)
                if (value is null || Undeclared(axis, value) is not null) Add($"{at}.{axis}", value is null ? "a value's name" : Undeclared(axis, value)!);
            foreach (var missing in axes.Keys.Where(a => !values.ContainsKey(a)))
                Add(at, $"names no value for axis '{missing}'; a configuration is one value on every axis");
        }
        foreach (var (entry, i) in (def.Exclude ?? []).Select((e, i) => (e, i)))
        {
            var at = $"exclude[{i}]";
            if (entry is null or { Count: 0 }) { Add(at, "an exclusion names the values that cannot go together: {axis: value, …}"); continue; }
            foreach (var (axis, value) in entry)
                if (value is null || Undeclared(axis, value) is not null) Add($"{at}.{axis}", value is null ? "a value's name" : Undeclared(axis, value)!);
        }
        if (problems > 0) return null;

        // Refusal 4: a value sets only parameters the base declares, in the shape declared. The base is the exam's own
        // parameters (the program template's) and the fixture a configuration runs in.
        var hashes = new Dictionary<(string, string), string>();
        foreach (var (axisName, axis) in axes)
            foreach (var (valueName, value) in axis.Values!)
            {
                foreach (var (param, given) in value!.Set ?? [])
                {
                    var at = $"axes.{axisName}.values.{valueName}.set.{param}";
                    if (!Regex.IsMatch(param, ParamName)) { Add(at, "a parameter is named like an environment variable"); continue; }
                    var bases = world is null ? [def.Fixture] : world == axisName ? [value.Fixture] : axes[world]!.Values!.Values.Select(w => w!.Fixture).Distinct().ToList();
                    foreach (var fixtureId in bases)
                    {
                        var declared = def.Params?.GetValueOrDefault(param) ?? (fixtureId is null ? null : fixtures.GetValueOrDefault(fixtureId)?.Def.Params?.GetValueOrDefault(param));
                        if (declared is null)
                        {
                            Add(at, $"not a parameter the base declares; declare it under params in {(fixtureId is null ? "exam.json" : $"fixtures/{fixtureId}/fixture.json, or in exam.json if the program template reads it")}");
                            break;
                        }
                        if (ShapeProblem(declared, given, dir) is { } shape) { Add(at, shape); break; }
                    }
                }
                hashes[(axisName, valueName)] = ValueHash(root, dir, value);
            }

        var excluded = (def.Exclude ?? []).Select(e => (IReadOnlyDictionary<string, string>)e!.ToDictionary(k => k.Key, k => k.Value!)).ToList();
        var named = (def.Configurations ?? []).ToDictionary(k => string.Join(",", axes.Keys.Select(a => $"{a}={k.Value![a]}")), k => k.Key);
        var configurations = new List<Configuration>();
        foreach (var combination in Cross(axes.Select(a => a.Value.Values!.Keys.Select(v => (a.Key, v)).ToList()).ToList()))
        {
            var c = new Configuration(combination, null, false, "");
            var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", combination.OrderBy(v => v.Axis, StringComparer.Ordinal).Select(v => $"{v.Axis}={hashes[v]}")))));
            configurations.Add(c with { Name = named.GetValueOrDefault(c.Key), Excluded = excluded.Any(c.Has), Hash = hash });
        }
        foreach (var (name, key) in named.Select(k => (k.Value, k.Key)))
            if (configurations.First(c => c.Key == key).Excluded) Add($"configurations.{name}", "is excluded by exclude; a named configuration is one that can be made");
        foreach (var group in named.GroupBy(k => k.Key).Where(g => g.Count() > 1))
            Add($"configurations.{group.Last().Value}", $"the same values as '{group.First().Value}'; a configuration has one name");

        // A stage belongs to one world for now: its checks and expected values come from that world's fixture.
        if (world is not null)
            foreach (var stage in stages.Where(s => s.Id is not null && !(conditions.GetValueOrDefault(s.Id!)?.Terms.Any(t => t.Axis == world && !t.Not) ?? false)))
                Add(stage.At + "when", $"required: axis '{world}' names the fixture, so each stage says which one it belongs to (`{world} = <value>`); a stage that runs in several is not built yet");
        if (world is null && def.Fixture is null && axes.Values.Any(a => a.Values!.Values.Any(v => v!.Files is not null)))
            Add("fixture", "required: a value's files are laid over a checkout, and this exam has none");
        var loaded = new Axes { Def = axes!, Configurations = configurations, ValueHashes = hashes, Conditions = conditions, WorldAxis = world };
        if (!loaded.Wanted.Any()) Add("exclude", "excludes every configuration; nothing is left to run");
        foreach (var c in loaded.Wanted)
            if (!stages.Any(s => loaded.Applies(s, c)))
                Add("stages", $"no stage applies in {c.Label}; give it one, or list it under exclude");
        return problems > 0 ? null : loaded;
    }

    static void ValidateFiles(FilesSource? files, string dir, string at, Action<string, string> add)
    {
        switch (files)
        {
            case null: return;
            case { Path: not null, Git: not null }: add(at, "path or git, not both"); break;
            case { Path: { } p } when Path.IsPathRooted(p) || p.Replace('\\', '/').Split('/').Contains(".."): add($"{at}.path", "a directory inside the exam, relative to it"); break;
            case { Path: { } p } when !Directory.Exists(Path.Combine(dir, p)): add($"{at}.path", $"no such directory in the exam: {p}"); break;
            case { Path: not null, Paths: not null }: add($"{at}.paths", "goes with git: a directory names the directory it means"); break;
            case { Git: not null, Rev: null or "" }: add($"{at}.rev", "required with git: a value's files are pinned to a revision"); break;
            case { Path: null, Git: null }: add(at, "expects {path} or {git, rev}"); break;
            case { Paths: { } paths }:
                if (paths.Count == 0) add($"{at}.paths", "a non-empty list, or absent for the whole revision");
                foreach (var path in paths)
                    if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Contains(".."))
                        add($"{at}.paths", $"'{path}' is not a path inside the repository: relative, without ..");
                break;
        }
        // Refusal 1: an overlay stays inside the checkout. The answer key (checks, answer sheets) lives outside it.
        if (files.To is { } to && (string.IsNullOrWhiteSpace(to) || Path.IsPathRooted(to) || to.Replace('\\', '/').Split('/').Any(s => s is ".." or ".git")))
            add($"{at}.to", "a directory inside the checkout, relative to it, without .. or .git");
    }

    /// <summary>Whether what a value gives a parameter is what the base declared it to be: text, or for a tree a directory or a git revision.</summary>
    static string? ShapeProblem(ParamDef declared, JsonNode? given, string dir)
    {
        if (declared.Tree == true)
        {
            FilesSource? files;
            try { files = given.Deserialize<FilesSource>(Exam.JsonOptions); }
            catch (JsonException) { files = null; }
            if (given is not JsonObject || files is null) return "declared as a tree: expects {path} or {git, rev}";
            string? problem = null;
            ValidateFiles(files with { To = null }, dir, "", (field, message) => problem ??= (field.TrimStart('.') is { Length: > 0 } f ? f + ": " : "") + message);
            return problem ?? (files.To is null ? null : "to goes with files: where a tree parameter lands is the base's business");
        }
        return given is JsonValue ? null : "declared as text: expects a string, a number or true/false";
    }

    /// <summary>A value's identity: what it sets, proctor's own fields, and its files by content. Not its note.</summary>
    static string ValueHash(string root, string dir, ValueDef value)
    {
        string Identity(FilesSource f) => (f.Git is not null ? f.AsBundle().GitIdentity() : Tree.Hash(Path.GetFullPath(Path.Combine(dir, f.Path!)), new HashSet<string> { ".git" })) + (f.To is null ? "" : "@" + f.To.Trim('/'));
        var parts = new List<string>();
        foreach (var (name, given) in (value.Set ?? []).OrderBy(k => k.Key, StringComparer.Ordinal))
            parts.Add($"set {name}=" + (given is JsonObject ? Identity(given.Deserialize<FilesSource>(Exam.JsonOptions)!) : given?.ToJsonString() ?? "null"));
        foreach (var (key, given) in value.Own())
            if (given is not null) parts.Add($"{key}={given}");
        if (value.Files is { } files) parts.Add("files=" + Identity(files));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));
    }

    static IEnumerable<List<(string Axis, string Value)>> Cross(List<List<(string, string)>> axes)
    {
        IEnumerable<List<(string, string)>> rows = [[]];
        foreach (var axis in axes)
            rows = rows.SelectMany(row => axis.Select(v => row.Append(v).ToList())).ToList();
        return rows;
    }

    /// <summary>
    /// Who does the work in a configuration, as the runner knows it: proctor's own fields from each value, merged. The
    /// route says bare or through Harbor; without one, a vendor agent goes through Harbor and nb runs bare.
    /// </summary>
    public Variant Assistant(Configuration c)
    {
        string? Pick(Func<ValueDef, string?> field) => c.Values.Select(v => field(Value(v.Axis, v.Value))).FirstOrDefault(f => f is not null);
        var (agent, route) = (Pick(v => v.Agent), Pick(v => v.Route));
        var vendor = agent is not null && agent != Variant.Nb;
        var bare = !vendor && route != ValueDef.Harbor;
        return new Variant(c.Key, bare ? Variant.Nb : null, vendor ? null : Pick(v => v.Harness) ?? Variant.Nb, Pick(v => v.Provider), Pick(v => v.Model), null,
            Agent: bare ? null : agent ?? Variant.Nb, AgentVersion: Pick(v => v.AgentVersion)) { Hash = c.Hash };
    }

    /// <summary>What is wrong with who does the work in a configuration, in the words of the fields that say it.</summary>
    public IEnumerable<string> AssistantProblems(Configuration c)
    {
        string? Pick(Func<ValueDef, string?> field) => c.Values.Select(v => field(Value(v.Axis, v.Value))).FirstOrDefault(f => f is not null);
        var (agent, route) = (Pick(v => v.Agent), Pick(v => v.Route));
        if (agent is not null && agent != Variant.Nb && route == ValueDef.Bare)
            yield return $"agent {agent} with route {ValueDef.Bare}: a vendor's agent runs through Harbor only";
        if (agent is null && Pick(v => v.Model) is null && Pick(v => v.Provider) is null)
            yield return "no value says who does the work; one axis needs values with agent and model (or, for nb, provider and model)";
    }
}
