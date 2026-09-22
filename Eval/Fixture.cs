using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// A fixture: the repository a case is run against and what "done" looks like in it. Repo-level,
// under fixtures/<id>/, reused across evals. Its checker lives beside the checkout source, never
// inside it: only source.path (or the clone) is ever copied into a work directory.

record FixtureSource(string? Path, string? Git, string? Rev, List<string>? Exclude);

record FixtureDef(string? Id, FixtureSource? Source, string? Stack, JsonObject? Expect, Dictionary<string, JsonObject>? Checks, Dictionary<string, JsonNode?>? Labels = null, string? Description = null);

/// <summary>One fixture directory, loaded. Construct through <see cref="Load"/>.</summary>
sealed record Fixture
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required FixtureDef Def { get; init; }
    /// <summary>The identity of what a cell is run against: a content hash of the source tree, or the git revision.</summary>
    public required string Hash { get; init; }

    public FixtureSource Source => Def.Source!;
    public Dictionary<string, JsonObject> Checks => Def.Checks ?? [];
    public string? SourceDir => Source.Path is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Dir, Source.Path));

    /// <summary>Directory names never copied into a work directory or hashed: .git always, plus the source's exclude list.</summary>
    public HashSet<string> Excluded => [".git", .. Source.Exclude ?? []];

    /// <summary>The definition files the eval hash covers: fixture.json and every check script.</summary>
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
        try { def = JsonSerializer.Deserialize<FixtureDef>(File.ReadAllText(file), Eval.JsonOptions); }
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
        foreach (var (name, spec) in def.Checks ?? [])
        {
            var f = $"checks.{name}";
            if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]*$")) Add(f, "check names are lowercase letters, digits, hyphens and underscores");
            foreach (var (field, problem) in Proctor.Checks.ValidateCheck(spec, dir)) Add(field.Length == 0 ? f : $"{f}.{field}", problem);
        }
        if (problems.Count > before) return null;

        var unhashed = new Fixture { Id = id, Dir = dir, Def = def, Hash = "" };
        return unhashed with { Hash = unhashed.ComputeHash() };
    }

    /// <summary>Every file under the source tree, as (relative path, full path), sorted, minus the excluded directories.</summary>
    public IEnumerable<(string Relative, string Full)> SourceFiles() => Tree.Files(SourceDir!, Excluded);

    string ComputeHash() => Source.Git is not null ? $"git:{Source.Rev}" : Tree.Hash(SourceDir!, Excluded);
}
