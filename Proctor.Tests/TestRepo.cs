using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>A throwaway repository root with an evals/ directory, built per test.</summary>
sealed class TestRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "proctor-tests", Guid.NewGuid().ToString("N")[..8]);

    public TestRepo()
    {
        Directory.CreateDirectory(Path.Combine(Root, "evals"));
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

    /// <summary>nb's built binary: $NB_PATH, else the sibling checkout, as evals/proctor.json in this repo assumes.</summary>
    public static string NbPath =>
        Environment.GetEnvironmentVariable("NB_PATH")
        ?? Path.GetFullPath(Path.Combine(SourceRoot, "..", "nb", "bin", "Debug", "net10.0", "nb"));

    public static string NbMockConfig => Path.Combine(SourceRoot, "evals", "nb.json");

    /// <summary>Copy an eval from this repository's evals/ into the temp root, with a proctor.json pointing at nb.</summary>
    public string CopyEval(string evalId)
    {
        CopyDirectory(Path.Combine(SourceRoot, "evals", evalId), Path.Combine(Root, "evals", evalId));
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "evals", evalId, "cases"), "*.json"))
            if (JsonNode.Parse(File.ReadAllText(file))?["fixture"]?.GetValue<string>() is { } fixture) CopyFixture(fixture);
        foreach (var arm in JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "evals", evalId, "eval.json")))?["arms"]?.AsArray() ?? [])
            if (arm?["bundle"]?["path"]?.GetValue<string>() is { } bundle && !Directory.Exists(Path.Combine(Root, bundle)))
                CopyDirectory(Path.Combine(SourceRoot, bundle), Path.Combine(Root, bundle));
        WriteProctorConfig();
        return Path.Combine(Root, "evals", evalId);
    }

    /// <summary>Copy a fixture from this repository's fixtures/ into the temp root.</summary>
    public void CopyFixture(string id)
    {
        if (Directory.Exists(Path.Combine(Root, "fixtures", id))) return;
        CopyDirectory(Path.Combine(SourceRoot, "fixtures", id), Path.Combine(Root, "fixtures", id));
    }

    public void WriteProctorConfig(string? runner = null, object? mounts = null) =>
        File.WriteAllText(Path.Combine(Root, "evals", "proctor.json"),
            JsonSerializer.Serialize(new { nb = new { path = NbPath, config = NbMockConfig, runner, mounts } }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

    /// <summary>Write an executable script under evals/. Returns the full path.</summary>
    public string WriteScript(string relativePath, string content)
    {
        var path = Write(relativePath, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>Write a file under evals/, creating directories. Returns the full path.</summary>
    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, "evals", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Edit a JSON file under evals/ in place.</summary>
    public void EditJson(string relativePath, Action<JsonObject> edit)
    {
        var path = Path.Combine(Root, "evals", relativePath);
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(node);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public Eval LoadEval(string evalId)
    {
        var problems = new List<Problem>();
        var eval = Eval.Load(Root, evalId, problems);
        Assert.True(eval is not null, string.Join("\n", problems));
        return eval!;
    }

    public List<Problem> Problems(string evalId)
    {
        var problems = new List<Problem>();
        Eval.Load(Root, evalId, problems);
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
