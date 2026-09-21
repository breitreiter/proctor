using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Steps 2 and 3: one cell against nb's Mock provider, then the matrix, resume and hook failures.</summary>
public class RunnerTests
{
    static string StartSmoke(TestRepo repo, Action<JsonObject>? editEval = null)
    {
        repo.CopyEval("smoke");
        if (editEval is not null) repo.EditJson("smoke/eval.json", editEval);
        var problems = new List<Problem>();
        var config = Eval.LoadConfig(repo.Root, problems);
        var eval = repo.LoadEval("smoke");
        return Runner.Start(repo.Root, eval, config, null, "test", TextWriter.Null);
    }

    static string Cell(TestRepo repo, string id, string arm, string @case, int sample) =>
        Layout.Cell(Layout.Experiment(repo.Root, id), arm, @case, sample);

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    [Fact]
    public void OneCell_MatchesTheLayoutPlanFileForFile()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e =>
        {
            e["arms"]!.AsArray().RemoveAt(1);
            e["arms"]![0]!["samples"] = 1;
        });
        var expDir = Layout.Experiment(repo.Root, id);
        Assert.Matches(@"^\d{8}-\d{4}-smoke-[a-z0-9]{4}$", id);

        // Experiment level.
        Assert.True(File.Exists(Path.Combine(expDir, "experiment.json")));
        Assert.True(File.Exists(Path.Combine(expDir, "status.json")));
        Assert.True(File.Exists(Path.Combine(expDir, "a", "hooks", "arm.setup.log")));
        var status = ReadJson(Path.Combine(expDir, "status.json"));
        Assert.Equal(3, status["planned"]!.GetValue<int>());
        Assert.Equal(3, status["counts"]!["completed"]!.GetValue<int>());

        // Cell level: every file the plan names, and nothing that belongs elsewhere.
        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal(
            ["diff.patch", "hooks", "manifest.json", "program.nb", "status", "stderr.txt", "transcript.jsonl"],
            Directory.GetFileSystemEntries(cell).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["sample.setup.log", "sample.teardown.log"],
            Directory.GetFiles(Path.Combine(cell, "hooks")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("completed", File.ReadAllText(Path.Combine(cell, "status")).Trim());

        var manifest = ReadJson(Path.Combine(cell, "manifest.json"));
        Assert.Matches("^[0-9a-f]{16}$", manifest["run_id"]!.GetValue<string>());
        Assert.Equal(id, manifest["experiment"]!.GetValue<string>());
        Assert.Equal("a", manifest["arm"]!.GetValue<string>());
        Assert.Equal("plain", manifest["case"]!.GetValue<string>());
        Assert.Equal(1, manifest["sample"]!.GetValue<int>());
        Assert.Equal("Mock", manifest["provider"]!.GetValue<string>());
        Assert.Equal("nb-jsonl", manifest["transcript"]!["format"]!.GetValue<string>());
        Assert.Equal("completed", manifest["status"]!.GetValue<string>());
        Assert.Equal(1, manifest["attempts"]!.GetValue<int>());
        Assert.StartsWith("sha256:", manifest["eval_hash"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["program_hash"]!.GetValue<string>());
        Assert.True(manifest["duration_ms"]!.GetValue<long>() > 0);
        Assert.Null(manifest["status_reason"]);

        // The program as run: placeholders resolved, nothing else changed.
        var program = File.ReadAllText(Path.Combine(cell, "program.nb"));
        Assert.Contains("provider Mock\n", program);
        Assert.Contains("run MOCK:response=hello world\n", program);
        Assert.DoesNotContain("{{", program);

        // nb's output untouched: the transcript ends in a result trailer.
        var trailer = Transcript.Read(cell).Trailer;
        Assert.NotNull(trailer);
        Assert.Equal("ok", trailer.ExitReason);
        Assert.Equal("Mock", trailer.Provider);

        // The work directory is where the run happened and the hook put its file.
        Assert.True(File.Exists(Path.Combine(Layout.Work(repo.Root, id, "a", "plain", 1), "note.txt")));
    }

    [Fact]
    public void Matrix_RunsEveryArmCaseAndSample_AndABadTranscriptIsStillCompleted()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo);
        var statuses = Runner.Cells(repo.LoadEval("smoke"))
            .Select(x => Runner.ReadStatus(Cell(repo, id, x.Arm.Id!, x.Case.Id!, x.Sample))).ToList();
        Assert.Equal(12, statuses.Count);
        Assert.All(statuses, s => Assert.Equal("completed", s));

        // `loops` exhausts the tool-call budget: a transcript with exit_reason max_tool_calls, still `completed`.
        Assert.Equal("max_tool_calls", Transcript.Read(Cell(repo, id, "b", "loops", 2)).Trailer!.ExitReason);
    }

    [Fact]
    public void Resume_SkipsCompletedCells_AndRerunsFailedOnes()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; });

        // Pretend one cell died: its status is `failed`, its manifest records an attempt.
        var failedCell = Cell(repo, id, "a", "uses-bash", 1);
        File.WriteAllText(Path.Combine(failedCell, "status"), "failed\n");
        var untouched = Cell(repo, id, "a", "plain", 1);
        var untouchedRunId = ReadJson(Path.Combine(untouched, "manifest.json"))["run_id"]!.GetValue<string>();
        var untouchedStarted = File.GetLastWriteTimeUtc(Path.Combine(untouched, "transcript.jsonl"));

        Runner.Resume(repo.Root, id, null, TextWriter.Null);

        Assert.Equal("completed", Runner.ReadStatus(failedCell));
        Assert.Equal(2, ReadJson(Path.Combine(failedCell, "manifest.json"))["attempts"]!.GetValue<int>());
        Assert.Equal(untouchedRunId, ReadJson(Path.Combine(untouched, "manifest.json"))["run_id"]!.GetValue<string>());
        Assert.Equal(untouchedStarted, File.GetLastWriteTimeUtc(Path.Combine(untouched, "transcript.jsonl")));
    }

    [Fact]
    public void Resume_RefusesWhenTheEvalChanged()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; });
        repo.EditJson("smoke/cases/plain.json", c => c["prompt"] = "MOCK:response=changed");

        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, TextWriter.Null));
        Assert.Contains("has changed", e.Message);
    }

    [Fact]
    public void SampleHookFailure_MarksTheCellFailed_AndTheMatrixContinues()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/hooks/flaky-setup.sh", "#!/usr/bin/env bash\nif [ \"$PROCTOR_CASE\" = plain ]; then echo 'no fixture for plain' >&2; exit 7; fi\nmkdir -p \"$PROCTOR_WORK\"\n");
        File.SetUnixFileMode(Path.Combine(repo.Root, "evals/smoke/hooks/flaky-setup.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = StartSmoke(repo, e =>
        {
            e["arms"]!.AsArray().RemoveAt(1);
            e["arms"]![0]!["samples"] = 1;
            e["hooks"]!["sample"]!["setup"] = "hooks/flaky-setup.sh";
        });

        var failed = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(failed));
        var manifest = ReadJson(Path.Combine(failed, "manifest.json"));
        Assert.Equal("failed", manifest["status"]!.GetValue<string>());
        Assert.Contains("hooks/flaky-setup.sh exited 7: no fixture for plain", manifest["status_reason"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(failed, "transcript.jsonl")), "nb must not run when setup failed");

        Assert.Equal("completed", Runner.ReadStatus(Cell(repo, id, "a", "loops", 1)));
        Assert.Equal("completed", Runner.ReadStatus(Cell(repo, id, "a", "uses-bash", 1)));
        var counts = ReadJson(Path.Combine(Layout.Experiment(repo.Root, id), "status.json"))["counts"]!;
        Assert.Equal(1, counts["failed"]!.GetValue<int>());
        Assert.Equal(2, counts["completed"]!.GetValue<int>());
    }

    [Fact]
    public void ArmHookFailure_FailsEveryCellOfThatArm_AndTheNextArmRuns()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/hooks/arm-b-fails.sh", "#!/usr/bin/env bash\n[ \"$PROCTOR_ARM\" = b ] && { echo 'fakes did not start' >&2; exit 1; }\nexit 0\n");
        File.SetUnixFileMode(Path.Combine(repo.Root, "evals/smoke/hooks/arm-b-fails.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = StartSmoke(repo, e =>
        {
            foreach (var arm in e["arms"]!.AsArray()) arm!["samples"] = 1;
            e["hooks"]!["arm"]!["setup"] = "hooks/arm-b-fails.sh";
        });

        foreach (var c in new[] { "loops", "plain", "uses-bash" })
        {
            Assert.Equal("completed", Runner.ReadStatus(Cell(repo, id, "a", c, 1)));
            Assert.Equal("failed", Runner.ReadStatus(Cell(repo, id, "b", c, 1)));
            Assert.Contains("arm setup hook failed", ReadJson(Path.Combine(Cell(repo, id, "b", c, 1), "manifest.json"))["status_reason"]!.GetValue<string>());
        }
    }

    [Fact]
    public void NbStartupError_IsFailed_NotCompleted()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e =>
        {
            e["arms"]!.AsArray().RemoveAt(1);
            e["arms"]![0]!["samples"] = 1;
            e["arms"]![0]!["provider"] = "NoSuchEntry";
        });
        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(cell));
        var reason = ReadJson(Path.Combine(cell, "manifest.json"))["status_reason"]!.GetValue<string>();
        Assert.StartsWith("nb exited 1 without a result trailer", reason);
    }

    [Fact]
    public void ResolveProgram_EscapesNewlinesAsContinuations()
    {
        var arm = new Arm("floor", "nb", "nb", "Mock", "m", 1, null);
        var c = new CaseDef("x", null, "Add a flag.\nKeep tests green.\n  Indented line.", null);
        var program = Runner.ResolveProgram("run {{prompt}}\n# {{case}} on {{arm}} sample {{sample}} in {{work}}\n", arm, c, 2, "/w");
        Assert.Equal("run Add a flag. \\\nKeep tests green. \\\n  Indented line.\n# x on floor sample 2 in /w\n", program);
    }

    [Fact]
    public void Fixture_IsCheckedOutBeforeTheRun_AndTheDiffCollectedAfter()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; });
        var cell = Cell(repo, id, "a", "plain", 1);
        var work = Layout.Work(repo.Root, id, "a", "plain", 1);

        var manifest = ReadJson(Path.Combine(cell, "manifest.json"));
        Assert.Equal("note", manifest["fixture"]!["id"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["fixture"]!["hash"]!.GetValue<string>());

        // The checkout is a git repository with the fixture committed; the teardown hook's edit is the diff.
        Assert.True(Directory.Exists(Path.Combine(work, ".git")));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(work, "note.txt")).Trim());
        var diff = File.ReadAllText(Path.Combine(cell, "diff.patch"));
        Assert.Contains("diff --git a/note.txt b/note.txt", diff);
        Assert.Contains("-original", diff);
        Assert.Contains("+changed", diff);
        Assert.Equal(["note.txt"], Transcript.Read(cell).TouchedPaths());
    }

    [Fact]
    public void Checkout_RestoresTheFixturePlusTheDiff_WhenTheWorkDirectoryIsGone()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        var fixture = repo.LoadEval("smoke").Fixtures["note"];
        var work = Path.Combine(repo.Root, ".proctor/work/x");
        var cell = Path.Combine(repo.Root, "runs/x");
        Directory.CreateDirectory(cell);

        Assert.Null(Checkout.Materialise(fixture, work));
        File.WriteAllText(Path.Combine(work, "note.txt"), "edited\n");
        File.WriteAllText(Path.Combine(work, "new.txt"), "added\n");
        Assert.Null(Checkout.CollectDiff(work, cell));
        Directory.Delete(work, recursive: true);

        Assert.Null(Checkout.Restore(fixture, work, cell));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(work, "note.txt")).Trim());
        Assert.Equal("added", File.ReadAllText(Path.Combine(work, "new.txt")).Trim());
        // A second restore over a populated directory is a no-op.
        Assert.Null(Checkout.Restore(fixture, work, cell));
    }

    [Fact]
    public void Bundle_IsResolvedOncePerExperiment_RecordedPerCell_AndResumeRefusesWhenItChanged()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { foreach (var arm in e["arms"]!.AsArray()) arm!["samples"] = 1; });
        var bundleDir = Path.Combine(repo.Root, "bundles", "smoke");

        var experiment = ReadJson(Path.Combine(Layout.Experiment(repo.Root, id), "experiment.json"));
        Assert.Null(experiment["bundles"]!["a"]);
        Assert.Equal("bundles/smoke", experiment["bundles"]!["b"]!["source"]!.GetValue<string>());
        Assert.Equal(bundleDir, experiment["bundles"]!["b"]!["path"]!.GetValue<string>());
        Assert.StartsWith("sha256:", experiment["bundles"]!["b"]!["hash"]!.GetValue<string>());

        var b = Cell(repo, id, "b", "plain", 1);
        Assert.Equal(experiment["bundles"]!["b"]!["hash"]!.GetValue<string>(), ReadJson(Path.Combine(b, "manifest.json"))["bundle"]!["hash"]!.GetValue<string>());
        Assert.Contains($"# bundle: {bundleDir}\n", File.ReadAllText(Path.Combine(b, "program.nb")));
        var a = Cell(repo, id, "a", "plain", 1);
        Assert.Null(ReadJson(Path.Combine(a, "manifest.json"))["bundle"]);
        Assert.Contains("# bundle: \n", File.ReadAllText(Path.Combine(a, "program.nb")));

        File.AppendAllText(Path.Combine(bundleDir, "README.md"), "edited\n");
        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, TextWriter.Null));
        Assert.Contains("bundle of arm 'b'", e.Message);
    }
}
