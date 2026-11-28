using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Auspex;

// A fixture: the repository a stage is run against and what "done" looks like in it. Repo-level,
// under fixtures/<id>/, reused across exams. Its checker lives beside the checkout source, never
// inside it: only source.path (or the clone) is ever copied into a work directory.

record FixtureSource(string? Path, string? Git, string? Rev, List<string>? Exclude);

/// <summary>
/// The image the fixture is run in through Harbor: a base image, the environment, and the commands that make the checkout
/// ready (a package restore), run in the work directory after the fixture is committed, at build time and with network,
/// so neither the agent nor the checks need any. Services is a directory beside repo/ holding a docker-compose.yaml of
/// extra services (and whatever it mounts), copied into the task's environment/: Harbor runs them beside the agent, in
/// its network namespace. Serves is the ports those services answer on at the agent's localhost, which is how a model
/// endpoint there (the stub's) is told from one on the machine that runs proctor. HostPorts is ports of that machine the
/// agent is to reach at its own localhost (a dev server started by hand beside the run): each is relayed, and the try
/// then depends on something the exam does not hold. AllowedHosts is hosts the agent may reach beside its model's, for a
/// service that cannot sensibly be simulated: by name, as Harbor's allowlist matches them, never a port or a path.
/// </summary>
record FixtureImage(string? From, List<string>? Run, Dictionary<string, string>? Env, string? Services = null, List<string>? Params = null, List<int>? Serves = null, List<int>? HostPorts = null, List<string>? AllowedHosts = null);

record FixtureDef(string? Id, FixtureSource? Source, string? Stack, JsonObject? Expect, Dictionary<string, JsonObject>? Checks, Dictionary<string, JsonNode?>? Labels = null, string? Description = null, FixtureImage? Image = null, Dictionary<string, ParamDef>? Params = null);

/// <summary>One fixture directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed record Fixture
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required FixtureDef Def { get; init; }
    /// <summary>The identity of what an attempt is run against: a content hash of the source tree, or the git revision.</summary>
    public required string Hash { get; init; }

    public FixtureSource Source => Def.Source!;
    public Dictionary<string, JsonObject> Checks => Def.Checks ?? [];
    public string? SourceDir => Source.Path is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Dir, Source.Path));

    /// <summary>Directory names never copied into a work directory or hashed: .git always, plus the source's exclude list.</summary>
    public HashSet<string> Excluded => [".git", .. Source.Exclude ?? []];

    /// <summary>The definition files the exam hash covers: fixture.json and every check script.</summary>
    public IEnumerable<string> DefinitionFiles()
    {
        yield return System.IO.Path.Combine(Dir, Layout.FixtureFile);
        foreach (var spec in Checks.Values)
            if (spec["script"] is JsonValue v && v.TryGetValue<string>(out var script)) yield return System.IO.Path.Combine(Dir, script);
    }

    public static Fixture? Load(string root, string id, List<Problem> problems)
    {
        var dir = Layout.Fixture(root, id);
        var file = System.IO.Path.Combine(dir, Layout.FixtureFile);
        var rel = System.IO.Path.GetRelativePath(root, file);
        if (!File.Exists(file))
        {
            problems.Add(new Problem(System.IO.Path.GetRelativePath(root, dir), "", "no such fixture (fixtures/<id>/fixture.json)"));
            return null;
        }
        FixtureDef? def;
        try { def = JsonSerializer.Deserialize<FixtureDef>(File.ReadAllText(file), Exam.JsonOptions); }
        catch (JsonException e) { problems.Add(new Problem(rel, e.Path ?? "", e.Message.Split(" Path:")[0])); return null; }
        if (def is null) return null;

        var before = problems.Count;
        void Add(string field, string message) => problems.Add(new Problem(rel, field, message));
        if (string.IsNullOrWhiteSpace(def.Id)) Add("id", "required");
        else if (def.Id != id) Add("id", $"'{def.Id}' does not match the directory name '{id}'");
        switch (def.Source)
        {
            case null: Add("source", "required: {path} or {git, rev}"); break;
            case { Path: not null, Git: not null }: Add("source", "path or git, not both"); break;
            case { Path: { } p } when !Directory.Exists(System.IO.Path.Combine(dir, p)): Add("source.path", $"no such directory: {p}"); break;
            case { Git: not null, Rev: null or "" }: Add("source.rev", "required with git: a fixture is pinned to a revision"); break;
            case { Path: null, Git: null }: Add("source", "required: {path} or {git, rev}"); break;
        }
        Labels.Validate(def.Labels, Add);
        if (def.Image is { } image)
        {
            if (string.IsNullOrWhiteSpace(image.From)) Add("image.from", "required: the base image (mcr.microsoft.com/dotnet/sdk:10.0)");
            foreach (var key in (image.Env ?? []).Keys.Where(k => !Regex.IsMatch(k, "^[A-Za-z_][A-Za-z0-9_]*$"))) Add($"image.env.{key}", "not an environment variable name");
            if (image.Run?.Any(string.IsNullOrWhiteSpace) == true) Add("image.run", "a command, not an empty string");
            foreach (var (field, ports) in new[] { ("image.serves", image.Serves), ("image.host_ports", image.HostPorts) })
                if (ports?.Any(p => p is < 1 or > 65535) == true) Add(field, "ports, each 1 to 65535");
            foreach (var host in image.AllowedHosts ?? [])
                if (!Regex.IsMatch(host, @"^(\*\.)?[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$"))
                    Add("image.allowed_hosts", $"'{host}' is not a host: a name or an address, with no scheme, port or path (the allowlist is by host, and every web port of it is open)");
            if (image.Serves is { Count: > 0 } && image.Services is null) Add("image.serves", "the ports image.services' services answer on, and there is no image.services");
            if (image.HostPorts?.Intersect(image.Serves ?? []).Order().ToList() is { Count: > 0 } both)
                Add("image.host_ports", $"{string.Join(", ", both)} {(both.Count == 1 ? "is" : "are")} in image.serves too: a port is the fixture's service or this machine's, not both");
            if (image.Services is { } services)
            {
                var servicesDir = System.IO.Path.Combine(dir, services);
                if (!File.Exists(System.IO.Path.Combine(servicesDir, "docker-compose.yaml"))) Add("image.services", $"no docker-compose.yaml in {services}");
                else if (File.Exists(System.IO.Path.Combine(servicesDir, "Dockerfile")) || new[] { "repo", HarborCompile.ServiceParamsDir }.Any(d => System.IO.Path.Exists(System.IO.Path.Combine(servicesDir, d))))
                    Add("image.services", $"Dockerfile, repo and {HarborCompile.ServiceParamsDir} are the task's own names in environment/");
            }
        }
        // The parameters a value may set (plans/axes-ports.md): the only names an exam's axes can reach in this base.
        foreach (var (name, param) in def.Params ?? [])
        {
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) Add($"params.{name}", "a parameter is named like an environment variable");
            if (param is null) { Add($"params.{name}", "a parameter is an object: {default, tree, secret, about}"); continue; }
            if (param.Tree == true && param.Default is not null and not JsonObject) Add($"params.{name}.default", "a tree's default is {path} or {git, rev}, or absent for none");
            if (param.Tree != true && param.Default is not null and not JsonValue) Add($"params.{name}.default", "text: a string, a number or true/false");
            if (param.Secret == true && param.Default is not null) Add($"params.{name}.default", "a secret has no default in a file; it is passed by name");
        }
        foreach (var name in def.Image?.Params ?? [])
            if (def.Params?.ContainsKey(name) != true) Add("image.params", $"'{name}' is not a declared parameter; declare it under params");
        foreach (var (name, spec) in def.Checks ?? [])
        {
            var f = $"checks.{name}";
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) Add(f, "check names are lowercase letters, digits, hyphens and underscores");
            foreach (var (field, problem) in Auspex.Checks.ValidateCheck(spec, dir)) Add(field.Length == 0 ? f : $"{f}.{field}", problem);
        }
        if (problems.Count > before) return null;

        var unhashed = new Fixture { Id = id, Dir = dir, Def = def, Hash = "" };
        return unhashed with { Hash = unhashed.ComputeHash() };
    }

    /// <summary>Every file under the source tree, as (relative path, full path), sorted, minus the excluded directories.</summary>
    public IEnumerable<(string Relative, string Full)> SourceFiles() => Tree.Files(SourceDir!, Excluded);

    string ComputeHash() => Source.Git is not null ? $"git:{Source.Rev}" : Tree.Hash(SourceDir!, Excluded);
}
