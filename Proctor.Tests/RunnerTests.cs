using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Steps 2 and 3: one cell against nb's Mock provider, then the matrix, resume and hook failures.</summary>
public class RunnerTests
{
    static string StartSmoke(TestRepo repo, Action<JsonObject>? editEval = null, string? runner = null, string? runnerOverride = null, object? mounts = null)
    {
        repo.CopyEval("smoke");
        if (editEval is not null) repo.EditJson("smoke/eval.json", editEval);
        repo.WriteProctorConfig(runner, mounts);
        var problems = new List<Problem>();
        var config = Eval.LoadConfig(repo.Root, problems);
        var eval = repo.LoadEval("smoke");
        return Runner.Start(repo.Root, eval, config, null, runnerOverride, "test", TextWriter.Null);
    }

    static void OneArmOneSample(JsonObject e) { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; }

    /// <summary>The runner contract's bare equivalent: nb itself, started as proctor would start it, with the program on stdin.</summary>
    const string Passthrough = "#!/usr/bin/env bash\nexec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n";

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
            ["diff.patch", "hooks", "manifest.json", "program.jsonl", "program.nb", "status", "stderr.txt", "transcript.jsonl"],
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
        // What went down stdin: the same program as JSONL, compiled on the host.
        var compiled = File.ReadAllLines(Path.Combine(cell, "program.jsonl"));
        Assert.All(compiled, line => Assert.StartsWith("{", line));
        Assert.Contains(compiled, line => line.Contains("\"type\":\"run\"") && line.Contains("MOCK:response=hello world"));

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

        Runner.Resume(repo.Root, id, null, null, TextWriter.Null);

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

        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, null, TextWriter.Null));
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
        Assert.True(File.Exists(Path.Combine(failed, "hooks", "sample.teardown.log")), "teardown runs whenever setup ran, so a half-made environment is not left behind");
        Assert.True(File.Exists(Path.Combine(failed, "diff.patch")));

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
    public void Compile_InlinesIncludesOnTheHost_AndTheHashStaysTheSources()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/notes.md", "Never mention the sheet.\n");
        var template = File.ReadAllText(Path.Combine(repo.Root, "evals/smoke/program.nb")).Replace("run {{prompt}}", "system @notes.md\nrun {{prompt}}");
        repo.Write("smoke/program.nb", template);
        repo.WriteProctorConfig();
        var eval = repo.LoadEval("smoke");
        var id = Runner.Start(repo.Root, eval, Eval.LoadConfig(repo.Root, new List<Problem>()), null, null, "test", TextWriter.Null);

        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(cell));
        // The source keeps the include for reading; the compiled program carries its body and no path.
        var source = File.ReadAllText(Path.Combine(cell, "program.nb"));
        Assert.Contains("system @notes.md\n", source);
        var compiled = File.ReadAllText(Path.Combine(cell, "program.jsonl"));
        Assert.Contains("Never mention the sheet.", compiled);
        Assert.DoesNotContain("notes.md", compiled);
        var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Equal(hash, ReadJson(Path.Combine(cell, "manifest.json"))["program_hash"]!.GetValue<string>());
        // And the model saw it: the transcript's system turn is the note.
        Assert.Contains("Never mention the sheet.", File.ReadAllText(Path.Combine(cell, "transcript.jsonl")));
    }

    [Fact]
    public void Compile_Failure_IsFailed_BeforeAnyHookRuns()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        var template = File.ReadAllText(Path.Combine(repo.Root, "evals/smoke/program.nb")).Replace("run {{prompt}}", "system @missing.md\nrun {{prompt}}");
        repo.Write("smoke/program.nb", template);
        repo.WriteProctorConfig();
        var eval = repo.LoadEval("smoke");
        var id = Runner.Start(repo.Root, eval, Eval.LoadConfig(repo.Root, new List<Problem>()), null, null, "test", TextWriter.Null);

        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(cell));
        var reason = ReadJson(Path.Combine(cell, "manifest.json"))["status_reason"]!.GetValue<string>();
        Assert.Equal("program compile failed: nb --compile exited 1: Error: @include not found: missing.md", reason);
        Assert.False(File.Exists(Path.Combine(cell, "program.jsonl")));
        Assert.False(File.Exists(Path.Combine(cell, "transcript.jsonl")));
        Assert.False(File.Exists(Path.Combine(cell, "hooks", "sample.setup.log")), "nothing was set up for a program nb refused");
        Assert.False(File.Exists(Path.Combine(cell, "hooks", "sample.teardown.log")));
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
        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, null, TextWriter.Null));
        Assert.Contains("bundle of arm 'b'", e.Message);
    }
    [Fact]
    public void Runner_Passthrough_ProducesTheSameCellAsABareRun()
    {
        using var bare = new TestRepo();
        var bareId = StartSmoke(bare, OneArmOneSample);
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, OneArmOneSample, runner: "runners/passthrough.sh");

        foreach (var c in new[] { "loops", "plain", "uses-bash" })
        {
            var (a, b) = (Cell(bare, bareId, "a", c, 1), Cell(repo, id, "a", c, 1));
            Assert.Equal("completed", Runner.ReadStatus(b));
            // Identical but for the milliseconds nb measures.
            foreach (var file in new[] { "transcript.jsonl", "program.nb", "program.jsonl", "diff.patch", "status" })
                Assert.Equal(Timeless(File.ReadAllText(Path.Combine(a, file))), Timeless(File.ReadAllText(Path.Combine(b, file))));
            Assert.Equal(Directory.GetFileSystemEntries(a).Select(Path.GetFileName).Order(), Directory.GetFileSystemEntries(b).Select(Path.GetFileName).Order());
        }

        // The manifest and the experiment say how nb was run; a bare run says nothing.
        var manifest = ReadJson(Path.Combine(Cell(repo, id, "a", "plain", 1), "manifest.json"));
        Assert.Equal("runners/passthrough.sh", manifest["nb"]!["runner"]!["script"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Equal(TestRepo.NbPath, manifest["nb"]!["path"]!.GetValue<string>());
        var experiment = ReadJson(Path.Combine(Layout.Experiment(repo.Root, id), "experiment.json"));
        Assert.Equal(manifest["nb"]!["runner"]!["hash"]!.GetValue<string>(), experiment["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Null(ReadJson(Path.Combine(Cell(bare, bareId, "a", "plain", 1), "manifest.json"))["nb"]!["runner"]);

        static string Timeless(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"""(duration|provider)_ms"":\d+", "");
    }

    [Fact]
    public void Runner_GetsTheProgramOnStdin_TheCellEnvironment_AndTheWorkDirectory()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"argv=$#\" >&2\necho \"cwd=$PWD\" >&2\necho \"container=$PROCTOR_CONTAINER\" >&2\necho \"runner=$PROCTOR_RUNNER\" >&2\necho \"work=$PROCTOR_WORK\" >&2\necho \"nocolor=$NO_COLOR\" >&2\n"
            + "exec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n");
        var id = StartSmoke(repo, OneArmOneSample, runner: "runners/spy.sh");

        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(cell));
        var stderr = File.ReadAllText(Path.Combine(cell, "stderr.txt"));
        var work = Layout.Work(repo.Root, id, "a", "plain", 1);
        Assert.Contains("argv=0\n", stderr);
        Assert.Contains($"cwd={work}\n", stderr);
        Assert.Contains($"work={work}\n", stderr);
        Assert.Contains($"container=proctor-{id}-a-plain-1\n", stderr);
        Assert.Contains("runner=runners/spy.sh\n", stderr);
        Assert.Contains("nocolor=1\n", stderr);
        // The hooks see the same runner and container name, so they can own a container nb is exec'd into.
        Assert.Contains($"proctor-{id}-a-plain-1", File.ReadAllText(Path.Combine(cell, "hooks", "sample.setup.log")));
        Assert.Contains("runners/spy.sh", File.ReadAllText(Path.Combine(Layout.Experiment(repo.Root, id), "a", "hooks", "arm.setup.log")));
    }

    [Fact]
    public void Mounts_ResolveThePlaceholders_AndHooksSeeBothTheHostPathAndTheMount()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"work=$PROCTOR_WORK mount=$PROCTOR_WORK_MOUNT bundle=$PROCTOR_BUNDLE bundle_mount=$PROCTOR_BUNDLE_MOUNT\" >&2\n"
            + "exec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n");
        repo.WriteScript("smoke/hooks/mounts.sh", "#!/usr/bin/env bash\necho \"work=$PROCTOR_WORK mount=$PROCTOR_WORK_MOUNT bundle=$PROCTOR_BUNDLE bundle_mount=$PROCTOR_BUNDLE_MOUNT\"\n");
        // Arm b is the one with a bundle.
        var id = StartSmoke(repo, e =>
        {
            e["arms"]!.AsArray().RemoveAt(0); e["arms"]![0]!["samples"] = 1;
            e["hooks"]!["sample"]!["setup"] = "hooks/mounts.sh";
            WithWorkComment(repo);
        }, runner: "runners/spy.sh", mounts: new { work = "/work", bundle = "/bundle" });

        var cell = Cell(repo, id, "b", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(cell));
        var work = Layout.Work(repo.Root, id, "b", "plain", 1);
        var bundleDir = Path.Combine(repo.Root, "bundles", "smoke");
        // The program tells the model the paths inside the container.
        var program = File.ReadAllText(Path.Combine(cell, "program.nb"));
        Assert.Contains("# work: /work\n", program);
        Assert.Contains("# bundle: /bundle\n", program);
        // The runner and the hooks, on the host, get both.
        var expected = $"work={work} mount=/work bundle={bundleDir} bundle_mount=/bundle";
        Assert.Contains(expected, File.ReadAllText(Path.Combine(cell, "stderr.txt")));
        Assert.Contains(expected, File.ReadAllText(Path.Combine(cell, "hooks", "sample.setup.log")));
        // The manifest records the mounts beside the runner.
        var nb = ReadJson(Path.Combine(cell, "manifest.json"))["nb"]!;
        Assert.Equal("/work", (string)nb["mounts"]!["work"]!);
        Assert.Equal("/bundle", (string)nb["mounts"]!["bundle"]!);
    }

    [Fact]
    public void Mounts_AreIgnoredOnABareRun_SoTheShakedownSeesTheHostPaths()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, e => { OneArmOneSample(e); WithWorkComment(repo); }, runner: "runners/passthrough.sh", runnerOverride: "none", mounts: new { work = "/work" });

        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(cell));
        var work = Layout.Work(repo.Root, id, "a", "plain", 1);
        Assert.Contains($"# work: {work}\n", File.ReadAllText(Path.Combine(cell, "program.nb")));
        Assert.Contains($"work_mount={work}", File.ReadAllText(Path.Combine(cell, "hooks", "sample.setup.log")));
        Assert.Null(ReadJson(Path.Combine(cell, "manifest.json"))["nb"]!["mounts"]);
    }

    /// <summary>The smoke template names the bundle in a comment; add the checkout the same way, so the resolved program shows what the model was told.</summary>
    static void WithWorkComment(TestRepo repo)
    {
        var file = Path.Combine(repo.Root, "evals", "smoke", "program.nb");
        File.WriteAllText(file, "# work: {{work}}\n" + File.ReadAllText(file));
    }

    [Fact]
    public void Mounts_MustBeAbsolute()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.WriteProctorConfig(mounts: new { work = "work" });
        var problems = new List<Problem>();
        Eval.LoadConfig(repo.Root, problems);
        var problem = Assert.Single(problems);
        Assert.Equal("nb.mounts.work", problem.Field);
    }

    [Fact]
    public void TheManifestRecordsNbsVersion_ReadFromTheHostBinary()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, OneArmOneSample);
        var versions = ReadJson(Path.Combine(Cell(repo, id, "a", "plain", 1), "manifest.json"))["versions"]!;
        Assert.Matches(@"^\d+\.\d+", (string)versions["nb"]!);
        Assert.DoesNotContain("+", (string)versions["nb"]!);
    }

    [Fact]
    public void Runner_None_OverridesTheConfiguredRunner_AndHooksSeeABareRun()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/never.sh", "#!/usr/bin/env bash\necho 'the runner must not run' >&2\nexit 9\n");
        var id = StartSmoke(repo, OneArmOneSample, runner: "runners/never.sh", runnerOverride: "none");

        var cell = Cell(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(cell));
        Assert.Null(ReadJson(Path.Combine(cell, "manifest.json"))["nb"]!["runner"]);
        Assert.Contains("runner= container=", File.ReadAllText(Path.Combine(cell, "hooks", "sample.setup.log")));
    }

    [Fact]
    public void Resume_RefusesWhenTheRunnerChanged()
    {
        using var repo = new TestRepo();
        var script = repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, OneArmOneSample, runner: "runners/passthrough.sh");
        File.WriteAllText(Path.Combine(Cell(repo, id, "a", "plain", 1), "status"), "failed\n");

        File.AppendAllText(script, "# edited\n");
        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, null, TextWriter.Null));
        Assert.Contains("the runner has changed", e.Message);

        // Dropping the runner is a change too; a bare resume of a runner experiment would run nb somewhere else.
        var none = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, "none", TextWriter.Null));
        Assert.Contains("none vs runners/passthrough.sh", none.Message);
    }

    [Fact]
    public void Runner_NotFound_IsAUserFacingFailure()
    {
        using var repo = new TestRepo();
        var e = Assert.Throws<ProctorException>(() => StartSmoke(repo, OneArmOneSample, runner: "runners/missing.sh"));
        Assert.Contains("runner not found", e.Message);
        Assert.Contains("nb.runner", e.Message);
    }
}
