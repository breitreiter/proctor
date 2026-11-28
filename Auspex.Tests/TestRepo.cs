using System.Text.Json;
using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>A throwaway repository root with an exams/ directory, built per test.</summary>
sealed class TestRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "proctor-tests", Guid.NewGuid().ToString("N")[..8]);

    public TestRepo()
    {
        Directory.CreateDirectory(Path.Combine(Root, "exams"));
    }

    /// <summary>The repository this test assembly was built from: walk up to the directory holding Auspex.csproj.</summary>
    public static string SourceRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "Auspex.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Auspex.csproj not found above the test assembly");
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
        if (!Directory.Exists(source)) source = Path.Combine(SourceRoot, "Auspex.Tests", "exams", examId);   // test data, never an exam to run
        CopyDirectory(source, Path.Combine(Root, "exams", examId));
        var exam = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "exams", examId, "exam.json")))!;
        if (exam["fixture"]?.GetValue<string>() is { } examFixture) CopyFixture(examFixture);
        // A world axis names a fixture per value.
        foreach (var fixture in (exam["axes"] as JsonObject ?? []).SelectMany(a => (a.Value?["values"] as JsonObject ?? []).Select(v => v.Value?["fixture"]?.GetValue<string>())).OfType<string>())
            CopyFixture(fixture);
        WriteAuspexConfig();
        return Path.Combine(Root, "exams", examId);
    }

    /// <summary>Copy a fixture from this repository's fixtures/ into the temp root.</summary>
    public void CopyFixture(string id)
    {
        if (Directory.Exists(Path.Combine(Root, "fixtures", id))) return;
        CopyDirectory(Path.Combine(SourceRoot, "fixtures", id), Path.Combine(Root, "fixtures", id));
    }

    public void WriteAuspexConfig(object? judges = null) =>
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

    /// <summary>Edit one stage of an exam, by id, in its exam.json.</summary>
    public void EditStage(string examId, string stage, Action<JsonObject> edit) =>
        EditJson($"{examId}/exam.json", e => edit(e["stages"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["id"]!.GetValue<string>() == stage)));

    /// <summary>The smoke exam's commonest shape in a test: one named configuration alone (a: the first Mock, nothing laid over), one try.</summary>
    public void OneConfiguration(string examId = "smoke", string keep = "a") =>
        EditJson($"{examId}/exam.json", e =>
        {
            e["tries"] = 1;
            var named = e["configurations"]!.AsObject();
            foreach (var name in named.Select(c => c.Key).Where(n => n != keep).ToList()) named.Remove(name);
        });

    /// <summary>`proctor run` as the verb does it: the configurations the exam names (or those asked for), made. Returns the log entry's id and what was printed.</summary>
    public (string? Id, string Log) Run(string examId, List<string>? values = null, string? configuration = null, AuspexConfig? config = null)
    {
        var log = new StringWriter();
        var (exam, keys) = Verbs.ForRun(Root, LoadExam(examId), null, values, configuration);
        var id = Runner.Run(Root, exam, config ?? Exam.LoadConfig(Root, []), null, null, "test", log, keys);
        return (id, log.ToString());
    }

    /// <summary>The smoke exam's two named configurations, by key: a (the first Mock, nothing laid over) and b (the second Mock, with the extra files).</summary>
    public const string A = "assistant=mock,extra=none", B = "assistant=mock-b,extra=smoke";

    /// <summary>One stage of a configuration's n-th try: the directory its manifest, transcript and checks are in.</summary>
    public string StageDir(string examId, string key, string stage, int n = 1)
    {
        var entry = Bank.Entries(Root, examId).Last(e => e.Attempts.Any(a => a.Configuration == key && a.TryNumber == n));
        return Layout.Attempt(Root, examId, stage, entry.Stages!, entry.IdOf(key, n)!);
    }

    /// <summary>The directory of a configuration's n-th try, by its number, whatever id it is kept under.</summary>
    public string TryDir(string examId, string key, int n)
    {
        var entry = Bank.Entries(Root, examId).Last(e => e.Attempts.Any(a => a.Configuration == key && a.TryNumber == n));
        return Layout.Try(Root, examId, entry.IdOf(key, n)!);
    }

    /// <summary>
    /// The gate exam as two stages, each on one value of an `ending` axis, so a try is one stage: plain names an answer
    /// sheet, writes does not. Each is named bare and through Harbor, where the one with the sheet is a plain task.
    /// </summary>
    public static TestRepo GateWithOracle(string sheet = "# Answers\n\n## deploy-target\nStaging only.\n", string path = "oracle/plain.md")
    {
        var repo = new TestRepo();
        repo.CopyExam("gate");
        repo.Write("gate/oracle/plain.md", sheet);
        repo.EditJson("gate/exam.json", e =>
        {
            var stages = e["stages"]!.AsArray().Select(s => s!.AsObject()).Where(s => s["id"]!.GetValue<string>() is "plain" or "writes").Select(s => s.DeepClone().AsObject()).ToList();
            foreach (var stage in stages) stage["when"] = $"ending = {stage["id"]!.GetValue<string>()}";
            stages.Single(s => s["id"]!.GetValue<string>() == "plain")["oracle"] = path;
            e["stages"] = new JsonArray(stages.ToArray<JsonNode?>());
            var axes = new JsonObject { ["ending"] = JsonNode.Parse("""{ "about": "which stage a try is", "reference": "plain", "values": { "plain": { "note": "the stage with the sheet" }, "writes": { "note": "the stage that writes" } } }""") };
            foreach (var (name, axis) in e["axes"]!.AsObject().ToList()) { e["axes"]!.AsObject().Remove(name); axes[name] = axis; }
            e["axes"] = axes;
            e["configurations"] = JsonNode.Parse("""
                { "plain-bare": { "ending": "plain", "route": "bare" }, "plain-harbor": { "ending": "plain", "route": "harbor" },
                  "writes-bare": { "ending": "writes", "route": "bare" }, "writes-harbor": { "ending": "writes", "route": "harbor" } }
                """);
        });
        return repo;
    }

    /// <summary>Three scripted stages with no check of their own, in the order tests written before smoke had four expect: loops, plain, uses-bash.</summary>
    public const string ThreeStages = """
        [ { "id": "loops", "about": "The model repeats a bash command until nb nudges it out of the loop", "prompt": "MOCK:loop=bash echo x" },
          { "id": "plain", "about": "The model answers in one turn with no tools", "prompt": "MOCK:response=hello world", "expect": { "answer_contains": "hello" } },
          { "id": "uses-bash", "about": "The model runs one bash command and answers", "prompt": "MOCK:tool=bash echo hi" } ]
        """;

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
}
