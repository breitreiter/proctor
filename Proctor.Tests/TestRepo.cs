using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>A throwaway repository root with an exams/ directory, built per test.</summary>
sealed class TestRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "proctor-tests", Guid.NewGuid().ToString("N")[..8]);

    public TestRepo()
    {
        Directory.CreateDirectory(Path.Combine(Root, "exams"));
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

    /// <summary>nb's built binary: $NB_PATH, else the sibling checkout, as exams/proctor.json in this repo assumes.</summary>
    public static string NbPath =>
        Environment.GetEnvironmentVariable("NB_PATH")
        ?? Path.GetFullPath(Path.Combine(SourceRoot, "..", "nb", "bin", "Debug", "net10.0", "nb"));

    public static string NbMockConfig => Path.Combine(SourceRoot, "exams", "nb.example.json");

    /// <summary>Copy an exam from this repository's exams/ into the temp root, with a proctor.json pointing at nb.</summary>
    public string CopyExam(string examId)
    {
        var source = Path.Combine(SourceRoot, "exams", examId);
        if (!Directory.Exists(source)) source = Path.Combine(SourceRoot, "Proctor.Tests", "exams", examId);   // test data, never an exam to run
        CopyDirectory(source, Path.Combine(Root, "exams", examId));
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "exams", examId, "tasks"), "*.json"))
            if (JsonNode.Parse(File.ReadAllText(file))?["fixture"]?.GetValue<string>() is { } fixture) CopyFixture(fixture);
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "exams", examId, "variants"), "*.json"))
            if (JsonNode.Parse(File.ReadAllText(file))?["bundle"]?["path"]?.GetValue<string>() is { } bundle && !Directory.Exists(Path.Combine(Root, bundle)))
                CopyDirectory(Path.Combine(SourceRoot, bundle), Path.Combine(Root, bundle));
        WriteProctorConfig();
        return Path.Combine(Root, "exams", examId);
    }

    /// <summary>Copy a fixture from this repository's fixtures/ into the temp root.</summary>
    public void CopyFixture(string id)
    {
        if (Directory.Exists(Path.Combine(Root, "fixtures", id))) return;
        CopyDirectory(Path.Combine(SourceRoot, "fixtures", id), Path.Combine(Root, "fixtures", id));
    }

    public void WriteProctorConfig(object? judges = null) =>
        File.WriteAllText(Path.Combine(Root, "exams", "proctor.json"),
            JsonSerializer.Serialize(new { nb = new { path = NbPath, config = NbMockConfig }, judges }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

    /// <summary>The exam's nb block: a runner named relative to exams/ (where the tests write them), rewritten relative to the exam directory as exam.json wants it.</summary>
    public static void SetNb(JsonObject exam, string? runner, object? mounts)
    {
        if (runner is null && mounts is null) return;
        var nb = new JsonObject();
        if (runner is not null) nb["runner"] = "../" + runner;
        if (mounts is not null) nb["mounts"] = JsonSerializer.SerializeToNode(mounts);
        exam["nb"] = nb;
    }

    /// <summary>Write an executable script under exams/. Returns the full path.</summary>
    public string WriteScript(string relativePath, string content)
    {
        var path = Write(relativePath, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>Write a file under exams/, creating directories. Returns the full path.</summary>
    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, "exams", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Edit a JSON file under exams/ in place.</summary>
    public void EditJson(string relativePath, Action<JsonObject> edit)
    {
        var path = Path.Combine(Root, "exams", relativePath);
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(node);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Edit one variant's file, exams/&lt;exam&gt;/variants/&lt;id&gt;.json, in place.</summary>
    public void EditVariant(string examId, string variantId, Action<JsonObject> edit) => EditJson($"{examId}/variants/{variantId}.json", edit);

    /// <summary>Write a variant's file from its JSON, named by its id.</summary>
    public void AddVariant(string examId, string json) =>
        Write($"{examId}/variants/{JsonNode.Parse(json)!["id"]!.GetValue<string>()}.json", json);

    /// <summary>Retire a variant: delete its file.</summary>
    public void RemoveVariant(string examId, string variantId) => File.Delete(Path.Combine(Root, "exams", examId, "variants", variantId + ".json"));

    /// <summary>Give a variant another id: its file moves with it.</summary>
    public void RenameVariant(string examId, string from, string to)
    {
        EditVariant(examId, from, v => v["id"] = to);
        File.Move(Path.Combine(Root, "exams", examId, "variants", from + ".json"), Path.Combine(Root, "exams", examId, "variants", to + ".json"));
    }

    /// <summary>Retire every variant of the exam but one.</summary>
    public void KeepOnlyVariant(string examId, string keep)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "exams", examId, "variants"), "*.json"))
            if (Path.GetFileNameWithoutExtension(file) != keep) File.Delete(file);
    }

    /// <summary>The smoke exam's commonest shape in a test: variant a alone, one attempt of each task.</summary>
    public void OneVariantOneAttempt(string examId = "smoke", string keep = "a")
    {
        KeepOnlyVariant(examId, keep);
        EditJson($"{examId}/exam.json", e => e["attempts"] = 1);
    }

    /// <summary>Turn a copied task exam into one job of stages: each task, in file order, becomes a stage of exam.json on the one fixture they share.</summary>
    public void Stage(string examId)
    {
        var tasksDir = Path.Combine(Root, "exams", examId, "tasks");
        var stages = new JsonArray();
        string? fixture = null;
        foreach (var file in Directory.GetFiles(tasksDir, "*.json").Order(StringComparer.Ordinal))
        {
            var task = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            fixture = task["fixture"]?.GetValue<string>();
            task.Remove("fixture");
            if (task["description"] is { } description) { task["about"] = description.DeepClone(); task.Remove("description"); }
            stages.Add(task);
        }
        Directory.Delete(tasksDir, recursive: true);
        EditJson($"{examId}/exam.json", e => { e["fixture"] = fixture; e["stages"] = stages; });
    }

    public Exam LoadExam(string examId)
    {
        var problems = new List<Problem>();
        var exam = Exam.Load(Root, examId, problems);
        Assert.True(exam is not null, string.Join("\n", problems));
        return exam!;
    }

    public List<Problem> Problems(string examId)
    {
        var problems = new List<Problem>();
        Exam.Load(Root, examId, problems);
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

    /// <summary>An attempt's directory in the bank, from the log entry of the run that made it, which names the exam.</summary>
    public static string Attempt(string root, string entryId, string variant, string task, int n) =>
        Layout.Attempt(root, Runner.LoadEntry(root, entryId).Exam, variant, task, n, Runner.LoadEntry(root, entryId).Stages);

    /// <summary>An attempt's checkout, likewise.</summary>
    public static string Work(string root, string entryId, string variant, string task, int n) =>
        Layout.Work(root, Runner.LoadEntry(root, entryId).Exam, variant, task, n, Runner.LoadEntry(root, entryId).Stages);
}
