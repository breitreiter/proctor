using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>A throwaway repository root with a suites/ directory, built per test.</summary>
sealed class TestRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "proctor-tests", Guid.NewGuid().ToString("N")[..8]);

    public TestRepo()
    {
        Directory.CreateDirectory(Path.Combine(Root, "suites"));
    }

    /// <summary>The repository this test assembly was built from: walk up to the directory holding Proctor.csproj.</summary>
    public static string SourceRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "Proctor.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Proctor.csproj not found above the test assembly");
        }
    }

    /// <summary>nb's built binary: $NB_PATH, else the sibling checkout, as suites/proctor.json in this repo assumes.</summary>
    public static string NbPath =>
        Environment.GetEnvironmentVariable("NB_PATH")
        ?? Path.GetFullPath(Path.Combine(SourceRoot, "..", "nb", "bin", "Debug", "net10.0", "nb"));

    public static string NbMockConfig => Path.Combine(SourceRoot, "suites", "nb.json");

    /// <summary>Copy a suite from this repository's suites/ into the temp root, with a proctor.json pointing at nb.</summary>
    public string CopySuite(string suiteId)
    {
        CopyDirectory(Path.Combine(SourceRoot, "suites", suiteId), Path.Combine(Root, "suites", suiteId));
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "suites", suiteId, "tasks"), "*.json"))
            if (JsonNode.Parse(File.ReadAllText(file))?["fixture"]?.GetValue<string>() is { } fixture) CopyFixture(fixture);
        foreach (var arm in JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "suites", suiteId, "suite.json")))?["arms"]?.AsArray() ?? [])
            if (arm?["bundle"]?["path"]?.GetValue<string>() is { } bundle && !Directory.Exists(Path.Combine(Root, bundle)))
                CopyDirectory(Path.Combine(SourceRoot, bundle), Path.Combine(Root, bundle));
        WriteProctorConfig();
        return Path.Combine(Root, "suites", suiteId);
    }

    /// <summary>Copy a fixture from this repository's fixtures/ into the temp root.</summary>
    public void CopyFixture(string id)
    {
        if (Directory.Exists(Path.Combine(Root, "fixtures", id))) return;
        CopyDirectory(Path.Combine(SourceRoot, "fixtures", id), Path.Combine(Root, "fixtures", id));
    }

    public void WriteProctorConfig(object? judges = null) =>
        File.WriteAllText(Path.Combine(Root, "suites", "proctor.json"),
            JsonSerializer.Serialize(new { nb = new { path = NbPath, config = NbMockConfig }, judges }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

    /// <summary>The suite's nb block: a runner named relative to suites/ (where the tests write them), rewritten relative to the suite directory as suite.json wants it.</summary>
    public static void SetNb(JsonObject suite, string? runner, object? mounts)
    {
        if (runner is null && mounts is null) return;
        var nb = new JsonObject();
        if (runner is not null) nb["runner"] = "../" + runner;
        if (mounts is not null) nb["mounts"] = JsonSerializer.SerializeToNode(mounts);
        suite["nb"] = nb;
    }

    /// <summary>Write an executable script under suites/. Returns the full path.</summary>
    public string WriteScript(string relativePath, string content)
    {
        var path = Write(relativePath, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>Write a file under suites/, creating directories. Returns the full path.</summary>
    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, "suites", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Edit a JSON file under suites/ in place.</summary>
    public void EditJson(string relativePath, Action<JsonObject> edit)
    {
        var path = Path.Combine(Root, "suites", relativePath);
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(node);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public Suite LoadSuite(string suiteId)
    {
        var problems = new List<Problem>();
        var suite = Suite.Load(Root, suiteId, problems);
        Assert.True(suite is not null, string.Join("\n", problems));
        return suite!;
    }

    public List<Problem> Problems(string suiteId)
    {
        var problems = new List<Problem>();
        Suite.Load(Root, suiteId, problems);
        return problems;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
