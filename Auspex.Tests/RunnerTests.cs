using System.Text.Json;
using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>One try against nb's Mock provider, then every try an exam wants, filling its gaps, hook failures, and the runner script.</summary>
public class RunnerTests
{
    const string A = TestRepo.A, B = TestRepo.B;

    static string StartSmoke(TestRepo repo, Action<JsonObject>? editExam = null, string? runner = null, string? runnerOverride = null, object? mounts = null)
    {
        if (!File.Exists(Path.Combine(repo.Root, "exams", "smoke", "exam.json"))) repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => { editExam?.Invoke(e); TestRepo.SetNb(e, runner, mounts); });
        return RunAgain(repo, runnerOverride).Id!;
    }

    /// <summary>`auspex run smoke` again, as it is on disk now: the log entry it wrote, and what it said.</summary>
    static (string? Id, string Log) RunAgain(TestRepo repo, string? runnerOverride = null)
    {
        var log = new StringWriter();
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("smoke"), null, null, null);
        var id = Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, runnerOverride, "test", log, keys);
        return (id, log.ToString());
    }

    /// <summary>Configuration a alone, one try: an exam edit.</summary>
    static readonly Action<JsonObject> One = e =>
    {
        e["tries"] = 1;
        e["configurations"]!.AsObject().Remove("b");
    };

    /// <summary>The runner contract's bare equivalent: nb itself, started as auspex would start it, with the program on stdin.</summary>
    const string Passthrough = "#!/usr/bin/env bash\nexec \"$AUSPEX_NB\" --output jsonl --config \"$AUSPEX_NB_CONFIG\" -\n";

    static string Stage(TestRepo repo, string key, string stage, int n = 1) => repo.StageDir("smoke", key, stage, n);

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    [Fact]
    public void OneTry_MatchesTheLayoutPlanFileForFile()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, One);
        var entryDir = Layout.LogEntry(repo.Root, id);
        Assert.Matches(@"^\d{8}-\d{4}-smoke-[a-z0-9]{4}$", id);
        Assert.Equal(Path.Combine(repo.Root, "log", id), entryDir);

        // The log entry: what the run did, and nothing a person reads results from.
        Assert.Equal(["entry.json", "hooks", "status.json"], Directory.GetFileSystemEntries(entryDir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(entryDir, "hooks", $"{A}.setup.log")));
        var entry = ReadJson(Path.Combine(entryDir, "entry.json"));
        Assert.Null(entry["exam_def"]);
        var exam = repo.LoadExam("smoke");
        Assert.Equal(exam.Hash, entry["exam_hash"]!.GetValue<string>());
        // The exam as it stood, once per hash.
        var seen = ReadJson(Path.Combine(repo.Root, "exams-seen", $"sha256-{exam.Hash[7..19]}.json"));
        Assert.Equal(("smoke", exam.Hash), (seen["exam"]!.GetValue<string>(), seen["hash"]!.GetValue<string>()));
        Assert.Equal(1, seen["def"]!["tries"]!.GetValue<int>());
        // The configuration it made tries of: each axis's value, by name and by the hash that is its identity.
        var made = Assert.Single(entry["configurations"]!.AsArray())!;
        Assert.Equal((A, "mock", "none"), (made["id"]!.GetValue<string>(), made["def"]!["assistant"]!["id"]!.GetValue<string>(), made["def"]!["extra"]!["id"]!.GetValue<string>()));
        Assert.StartsWith("sha256:", made["hash"]!.GetValue<string>());
        Assert.Equal(["plain", "uses-bash", "loops", "writes-a-file"], entry["stages"]!.AsArray().Select(t => t!.GetValue<string>()));
        var status = ReadJson(Path.Combine(entryDir, "status.json"));
        Assert.Equal(4, status["planned"]!.GetValue<int>());
        Assert.Equal(4, status["counts"]!["completed"]!.GetValue<int>());

        // The try, in the bank under an id of its own: its hooks' logs, what setup left, and a directory per stage in order.
        var tryDir = repo.TryDir("smoke", A, 1);
        Assert.Equal(Path.Combine(repo.Root, "bank", "smoke"), Path.GetDirectoryName(tryDir));
        Assert.Matches(@"^\d{8}-\d{4}-[a-z0-9]{4}$", Path.GetFileName(tryDir));
        Assert.Equal(["hooks", "setup.patch", "stages"], Directory.GetFileSystemEntries(tryDir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["try.setup.log", "try.teardown.log"], Directory.GetFiles(Path.Combine(tryDir, "hooks")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["1-plain", "2-uses-bash", "3-loops", "4-writes-a-file"], Directory.GetDirectories(Path.Combine(tryDir, "stages")).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // A stage: every file the plan names, and nothing that belongs elsewhere.
        var stage = Stage(repo, A, "plain");
        Assert.Equal(Path.Combine(tryDir, "stages", "1-plain"), stage);
        Assert.Equal(
            ["diff.patch", "manifest.json", "program.jsonl", "program.nb", "status", "stderr.txt", "trajectory.json", "transcript.jsonl"],
            Directory.GetFileSystemEntries(stage).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("completed", File.ReadAllText(Path.Combine(stage, "status")).Trim());

        var manifest = ReadJson(Path.Combine(stage, "manifest.json"));
        Assert.Matches("^[0-9a-f]{16}$", manifest["id"]!.GetValue<string>());
        Assert.Equal("smoke", manifest["exam"]!.GetValue<string>());
        Assert.Equal(id, manifest["log"]!.GetValue<string>());
        Assert.Equal((A, exam.Assistants.Single().Hash), (manifest["configuration"]!["id"]!.GetValue<string>(), manifest["configuration"]!["hash"]!.GetValue<string>()));
        Assert.Equal(("plain", exam.StageHashes["plain"]), (manifest["stage"]!["id"]!.GetValue<string>(), manifest["stage"]!["hash"]!.GetValue<string>()));
        Assert.Equal((1, 1, Path.GetFileName(tryDir)), (manifest["try_number"]!.GetValue<int>(), manifest["place"]!.GetValue<int>(), manifest["try"]!.GetValue<string>()));
        Assert.Equal("mock", manifest["values"]!["assistant"]!["id"]!.GetValue<string>());
        Assert.Equal("Mock", manifest["provider"]!.GetValue<string>());
        Assert.Equal("nb-jsonl", manifest["transcript"]!["format"]!.GetValue<string>());
        Assert.Equal("completed", manifest["status"]!.GetValue<string>());
        Assert.Null(manifest["exam_hash"]);
        Assert.StartsWith("sha256:", manifest["program_hash"]!.GetValue<string>());
        Assert.True(manifest["duration_ms"]!.GetValue<long>() > 0);
        Assert.Null(manifest["status_reason"]);

        // The program as run: placeholders resolved, nothing else changed.
        var program = File.ReadAllText(Path.Combine(stage, "program.nb"));
        Assert.Contains("provider Mock\n", program);
        Assert.Contains("run MOCK:response=hello world\n", program);
        Assert.DoesNotContain("{{", program);
        // What went down stdin: the same program as JSONL, compiled on the host.
        var compiled = File.ReadAllLines(Path.Combine(stage, "program.jsonl"));
        Assert.All(compiled, line => Assert.StartsWith("{", line));
        Assert.Contains(compiled, line => line.Contains("\"type\":\"run\"") && line.Contains("MOCK:response=hello world"));

        // nb's output untouched: the transcript ends in a result trailer.
        var trailer = Transcript.Read(stage).Trailer;
        Assert.NotNull(trailer);
        Assert.Equal("ok", trailer.ExitReason);
        Assert.Equal("Mock", trailer.Provider);
    }

    [Fact]
    public void Run_MakesEveryTryTheExamWants_AndABadTranscriptIsStillCompleted()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo);
        var entry = Runner.LoadEntry(repo.Root, id);
        Assert.Equal(16, entry.Attempts.Count);   // two configurations, two tries of each, four stages a try
        var statuses = Bank.Of(repo.Root, repo.LoadExam("smoke")).Select(a => a.Status).ToList();
        Assert.Equal(16, statuses.Count);
        Assert.All(statuses, s => Assert.Equal("completed", s));

        // `loops` exhausts the tool-call budget: a transcript with exit_reason max_tool_calls, still `completed`.
        Assert.Equal("max_tool_calls", Transcript.Read(Stage(repo, B, "loops", 2)).Trailer!.ExitReason);
        // b's value lays its files over the checkout before the first stage; a's lays nothing.
        Assert.True(File.Exists(Path.Combine(Layout.Work(repo.Root, "smoke", entry.IdOf(B, 1)!), "extra", "README.md")));
        Assert.False(Directory.Exists(Path.Combine(Layout.Work(repo.Root, "smoke", entry.IdOf(A, 1)!), "extra")));

        // Nothing is missing, so the same command makes nothing.
        var (again, log) = RunAgain(repo);
        Assert.Null(again);
        Assert.Equal($"smoke: one job of 4 stages, 2 tries wanted\n  note: 2 tries a configuration can show a difference between two and never confirm one; 4 a configuration can\n  {A}     2 of 2 tries\n  {B}  2 of 2 tries\nnothing to make\n", log);
        Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, "log")));
    }

    [Fact]
    public void Run_AfterAStageChanged_EveryTryIsStale_AndIsMadeAgain()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, One);
        var before = repo.LoadExam("smoke").StageHashes["plain"];
        repo.EditStage("smoke", "plain", c => c["prompt"] = "MOCK:response=hello again");

        var (again, log) = RunAgain(repo);

        Assert.NotEqual(id, again);
        Assert.Contains($"  {A}  0 of 1 try   making 1\n", log);
        Assert.Contains($"run {again}: 1 try to make\n", log);
        var entry = Runner.LoadEntry(repo.Root, again!);
        // Numbered after the stale one, so a configuration's n-th try names one try for good.
        Assert.All(entry.Attempts, a => Assert.Equal((A, 2), (a.Configuration, a.TryNumber)));
        var exam = repo.LoadExam("smoke");
        var selection = Bank.Select(repo.Root, exam);
        Assert.Equal([$"{A}/2/loops", $"{A}/2/plain", $"{A}/2/uses-bash", $"{A}/2/writes-a-file"], selection.Current.Select(a => a.Label).Order(StringComparer.Ordinal));
        Assert.Equal(4, selection.Stale.Count);
        Assert.Contains(selection.Stale, s => s.Attempt == $"{A}/1/plain" && s.Why == $"the stages changed ({before[7..15]} then, {exam.StageHashes["plain"][7..15]} now)");
    }

    [Fact]
    public void Run_AfterAConfigurationIsNamed_MakesOnlyItsTries()
    {
        using var repo = new TestRepo();
        StartSmoke(repo, One);
        repo.EditJson("smoke/exam.json", e => e["configurations"]!["c"] = JsonNode.Parse("""{ "assistant": "mock-b", "extra": "none" }"""));
        const string c = "assistant=mock-b,extra=none";

        var (again, _) = RunAgain(repo);

        var entry = Runner.LoadEntry(repo.Root, again!);
        Assert.Equal([c], entry.Configurations.Select(v => v.Id));
        Assert.Equal(4, entry.Attempts.Count);
        Assert.All(entry.Attempts, a => Assert.Equal((c, 1), (a.Configuration, a.TryNumber)));
        Assert.Equal(8, Bank.Select(repo.Root, repo.LoadExam("smoke")).Current.Count);

        // Asking for one configuration narrows: a has all it wants, so asking for a alone makes nothing.
        var log = new StringWriter();
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("smoke"), null, null, "a");
        Assert.Null(Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", log, keys));
        Assert.EndsWith("nothing to make\n", log.ToString());
    }

    [Fact]
    public void TrySetupHookFailure_LosesTheTry_AndTheNextConfigurationRuns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.WriteScript("smoke/hooks/flaky-setup.sh", "#!/usr/bin/env bash\nif [ \"$AUSPEX_AXIS_ASSISTANT\" = mock ]; then echo 'no fakes for mock' >&2; exit 7; fi\n");
        var id = StartSmoke(repo, e =>
        {
            e["tries"] = 1;
            e["hooks"]!["try"]!["setup"] = "hooks/flaky-setup.sh";
        });

        var failed = Stage(repo, A, "plain");
        Assert.Equal("failed", Runner.ReadStatus(failed));
        var manifest = ReadJson(Path.Combine(failed, "manifest.json"));
        Assert.Equal("failed", manifest["status"]!.GetValue<string>());
        Assert.Contains("hooks/flaky-setup.sh exited 7: no fakes for mock", manifest["status_reason"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(failed, "transcript.jsonl")), "nb must not run when setup failed");
        Assert.True(File.Exists(Path.Combine(repo.TryDir("smoke", A, 1), "hooks", "try.teardown.log")), "teardown runs whenever setup ran, so a half-made environment is not left behind");
        // Nothing more of a lost try is launched.
        Assert.Equal("skipped", Runner.ReadStatus(Stage(repo, A, "loops")));
        Assert.Contains("the try was lost at stage plain", ReadJson(Path.Combine(Stage(repo, A, "loops"), "manifest.json"))["status_reason"]!.GetValue<string>());

        Assert.Equal("completed", Runner.ReadStatus(Stage(repo, B, "loops")));
        var counts = ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "status.json"))["counts"]!;
        Assert.Equal(1, counts["failed"]!.GetValue<int>());
        Assert.Equal(4, counts["completed"]!.GetValue<int>());

        // A lost try is never taken up again part way: the next run makes it again, whole, under the next number.
        var (again, log) = RunAgain(repo);
        Assert.NotEqual(id, again);
        Assert.Contains($"  {A}     0 of 1 try   making 1\n", log);
        Assert.All(Runner.LoadEntry(repo.Root, again!).Attempts, a => Assert.Equal((A, 2), (a.Configuration, a.TryNumber)));
    }

    [Fact]
    public void ConfigurationHookFailure_FailsEveryStageOfItsTries_AndTheNextConfigurationRuns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.WriteScript("smoke/hooks/b-fails.sh", "#!/usr/bin/env bash\n[ \"$AUSPEX_AXIS_EXTRA\" = smoke ] && { echo 'fakes did not start' >&2; exit 1; }\nexit 0\n");
        StartSmoke(repo, e =>
        {
            e["tries"] = 1;
            e["hooks"]!["configuration"]!["setup"] = "hooks/b-fails.sh";
        });

        foreach (var c in new[] { "loops", "plain", "uses-bash", "writes-a-file" })
        {
            Assert.Equal("completed", Runner.ReadStatus(Stage(repo, A, c)));
            Assert.Equal("failed", Runner.ReadStatus(Stage(repo, B, c)));
            Assert.Contains("setup hook failed", ReadJson(Path.Combine(Stage(repo, B, c), "manifest.json"))["status_reason"]!.GetValue<string>());
        }
    }

    [Fact]
    public void NbStartupError_IsFailed_NotCompleted()
    {
        using var repo = new TestRepo();
        StartSmoke(repo, e =>
        {
            One(e);
            e["axes"]!["assistant"]!["values"]!["mock"]!["provider"] = "NoSuchEntry";
        });
        var stage = Stage(repo, A, "plain");
        Assert.Equal("failed", Runner.ReadStatus(stage));
        var reason = ReadJson(Path.Combine(stage, "manifest.json"))["status_reason"]!.GetValue<string>();
        Assert.StartsWith("nb exited 1 without a result trailer", reason);
    }

    [Fact]
    public void Compile_InlinesIncludesOnTheHost_AndTheHashStaysTheSources()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/notes.md", "Never mention the sheet.\n");
        var template = File.ReadAllText(Path.Combine(repo.Root, "exams/smoke/program.nb")).Replace("run {{prompt}}", "system @notes.md\nrun {{prompt}}");
        repo.Write("smoke/program.nb", template);
        StartSmoke(repo, One);

        var stage = Stage(repo, A, "plain");
        Assert.Equal("completed", Runner.ReadStatus(stage));
        // The source keeps the include for reading; the compiled program carries its body and no path.
        var source = File.ReadAllText(Path.Combine(stage, "program.nb"));
        Assert.Contains("system @notes.md\n", source);
        var compiled = File.ReadAllText(Path.Combine(stage, "program.jsonl"));
        Assert.Contains("Never mention the sheet.", compiled);
        Assert.DoesNotContain("notes.md", compiled);
        var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Equal(hash, ReadJson(Path.Combine(stage, "manifest.json"))["program_hash"]!.GetValue<string>());
        // And the model saw it: the transcript's system turn is the note.
        Assert.Contains("Never mention the sheet.", File.ReadAllText(Path.Combine(stage, "transcript.jsonl")));
    }

    [Fact]
    public void Compile_Failure_LosesTheTry_BeforeNbRuns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var template = File.ReadAllText(Path.Combine(repo.Root, "exams/smoke/program.nb")).Replace("run {{prompt}}", "system @missing.md\nrun {{prompt}}");
        repo.Write("smoke/program.nb", template);
        StartSmoke(repo, One);

        var stage = Stage(repo, A, "plain");
        Assert.Equal("failed", Runner.ReadStatus(stage));
        var reason = ReadJson(Path.Combine(stage, "manifest.json"))["status_reason"]!.GetValue<string>();
        Assert.Equal("program compile failed: nb --compile exited 1: Error: @include not found: missing.md", reason);
        Assert.False(File.Exists(Path.Combine(stage, "program.jsonl")));
        Assert.False(File.Exists(Path.Combine(stage, "transcript.jsonl")));
        Assert.Equal("skipped", Runner.ReadStatus(Stage(repo, A, "uses-bash")));
    }

    [Fact]
    public void ResolveProgram_EscapesNewlinesAsContinuations()
    {
        var assistant = new Assistant("floor", "nb", "nb", "Mock", "m");
        var c = new StageDef("x", null, "Add a flag.\nKeep tests green.\n  Indented line.", null);
        var program = Runner.ResolveProgram("run {{prompt}}\n# {{stage}} on {{configuration}} try {{try_number}} in {{work}}\n", assistant, c, 2, "/w");
        Assert.Equal("run Add a flag. \\\nKeep tests green. \\\n  Indented line.\n# x on floor try 2 in /w\n", program);
    }

    [Fact]
    public void Fixture_IsCheckedOutBeforeTheTry_AndEachStagesDiffIsItsOwn()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, One);
        var entry = Runner.LoadEntry(repo.Root, id);
        var stage = Stage(repo, A, "writes-a-file");
        var work = Layout.Work(repo.Root, "smoke", entry.IdOf(A, 1)!);

        var manifest = ReadJson(Path.Combine(stage, "manifest.json"));
        Assert.Equal("note", manifest["fixture"]!["id"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["fixture"]!["hash"]!.GetValue<string>());

        // The checkout is a git repository with the fixture committed; what the stage wrote is its diff, and no other stage's.
        Assert.True(Directory.Exists(Path.Combine(work, ".git")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(work, "note.txt")).Trim());
        var diff = File.ReadAllText(Path.Combine(stage, "diff.patch"));
        Assert.Contains("diff --git a/made.txt b/made.txt", diff);
        Assert.Equal(["made.txt"], Transcript.Read(stage).TouchedPaths());
        Assert.Empty(Transcript.Read(Stage(repo, A, "plain")).TouchedPaths());
    }

    [Fact]
    public void Checkout_RestoresTheFixturePlusTheDiff_WhenTheWorkDirectoryIsGone()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var fixture = repo.LoadExam("smoke").Fixtures["note"];
        var work = Path.Combine(repo.Root, ".auspex/work/x");
        var attempt = Path.Combine(repo.Root, "attempts/x");
        Directory.CreateDirectory(attempt);

        Assert.Null(Checkout.Materialise(fixture, work));
        File.WriteAllText(Path.Combine(work, "note.txt"), "edited\n");
        File.WriteAllText(Path.Combine(work, "new.txt"), "added\n");
        Assert.Null(Checkout.CollectDiff(work, attempt));
        Directory.Delete(work, recursive: true);

        Assert.Null(Checkout.Restore(fixture, work, attempt));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(work, "note.txt")).Trim());
        Assert.Equal("added", File.ReadAllText(Path.Combine(work, "new.txt")).Trim());
        // A second restore over a populated directory is a no-op.
        Assert.Null(Checkout.Restore(fixture, work, attempt));
    }

    [Fact]
    public void Runner_Passthrough_ProducesTheSameTryAsABareRun()
    {
        using var bare = new TestRepo();
        var bareId = StartSmoke(bare, One);
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, One, runner: "runners/passthrough.sh");
        // Each ran in its own checkout, which a tool's output may name.
        string Placed(TestRepo r, string entryId, string text) => Runner.LoadEntry(r.Root, entryId) is var e ? text.Replace(Layout.Work(r.Root, "smoke", e.IdOf(A, 1)!), "/work") : text;

        foreach (var c in new[] { "loops", "plain", "uses-bash", "writes-a-file" })
        {
            var (a, b) = (Stage(bare, A, c), Stage(repo, A, c));
            Assert.Equal("completed", Runner.ReadStatus(b));
            // Identical but for the milliseconds nb measures.
            foreach (var file in new[] { "transcript.jsonl", "program.nb", "program.jsonl", "diff.patch", "status" })
                Assert.Equal(Placed(bare, bareId, Timeless(File.ReadAllText(Path.Combine(a, file)))), Placed(repo, id, Timeless(File.ReadAllText(Path.Combine(b, file)))));
            Assert.Equal(Directory.GetFileSystemEntries(a).Select(Path.GetFileName).Order(), Directory.GetFileSystemEntries(b).Select(Path.GetFileName).Order());
        }

        // The manifest and the log entry say how nb was run; a bare run says nothing.
        var manifest = ReadJson(Path.Combine(Stage(repo, A, "plain"), "manifest.json"));
        Assert.Equal("runners/passthrough.sh", manifest["nb"]!["runner"]!["script"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Equal(TestRepo.NbPath, manifest["nb"]!["path"]!.GetValue<string>());
        var entry = ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "entry.json"));
        Assert.Equal(manifest["nb"]!["runner"]!["hash"]!.GetValue<string>(), entry["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Null(ReadJson(Path.Combine(Stage(bare, A, "plain"), "manifest.json"))["nb"]!["runner"]);

        static string Timeless(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"""(duration|provider)_ms"":\d+", "");
    }

    [Fact]
    public void Runner_GetsTheProgramOnStdin_TheAttemptEnvironment_AndTheWorkDirectory()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"argv=$#\" >&2\necho \"cwd=$PWD\" >&2\necho \"container=$AUSPEX_CONTAINER\" >&2\necho \"runner=$AUSPEX_RUNNER\" >&2\necho \"work=$AUSPEX_WORK\" >&2\necho \"nocolor=$NO_COLOR\" >&2\n"
            + "exec \"$AUSPEX_NB\" --output jsonl --config \"$AUSPEX_NB_CONFIG\" -\n");
        var id = StartSmoke(repo, One, runner: "runners/spy.sh");
        var entry = Runner.LoadEntry(repo.Root, id);

        var stage = Stage(repo, A, "plain");
        Assert.Equal("completed", Runner.ReadStatus(stage));
        var stderr = File.ReadAllText(Path.Combine(stage, "stderr.txt"));
        var work = Layout.Work(repo.Root, "smoke", entry.IdOf(A, 1)!);
        Assert.Contains("argv=0\n", stderr);
        Assert.Contains($"cwd={work}\n", stderr);
        Assert.Contains($"work={work}\n", stderr);
        // The container's name is the try's: the exam and the id the try is kept under.
        var container = $"auspex-smoke-{Path.GetFileName(repo.TryDir("smoke", A, 1))}";
        Assert.Contains($"container={container}\n", stderr);
        Assert.Contains("runner=runners/spy.sh\n", stderr);
        Assert.Contains("nocolor=1\n", stderr);
        // The hooks see the same runner and container name, so they can own a container nb is exec'd into, and every stage of a try runs in the one.
        Assert.Contains(container, File.ReadAllText(Path.Combine(repo.TryDir("smoke", A, 1), "hooks", "try.setup.log")));
        Assert.Contains($"container={container}\n", File.ReadAllText(Path.Combine(Stage(repo, A, "loops"), "stderr.txt")));
        Assert.Contains("runners/spy.sh", File.ReadAllText(Path.Combine(Layout.LogEntry(repo.Root, id), "hooks", $"{A}.setup.log")));
    }

    [Fact]
    public void Mounts_ResolveThePlaceholders_AndHooksSeeBothTheHostPathAndTheMount()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"work=$AUSPEX_WORK mount=$AUSPEX_WORK_MOUNT\" >&2\n"
            + "exec \"$AUSPEX_NB\" --output jsonl --config \"$AUSPEX_NB_CONFIG\" -\n");
        repo.WriteScript("smoke/hooks/mounts.sh", "#!/usr/bin/env bash\necho \"work=$AUSPEX_WORK mount=$AUSPEX_WORK_MOUNT\"\n");
        var id = StartSmoke(repo, e =>
        {
            One(e);
            e["hooks"]!["try"]!["setup"] = "hooks/mounts.sh";
            WithWorkComment(repo);
        }, runner: "runners/spy.sh", mounts: new { work = "/work" });
        var entry = Runner.LoadEntry(repo.Root, id);

        var stage = Stage(repo, A, "plain");
        Assert.Equal("completed", Runner.ReadStatus(stage));
        var work = Layout.Work(repo.Root, "smoke", entry.IdOf(A, 1)!);
        // The program tells the model the path inside the container.
        Assert.Contains("# work: /work\n", File.ReadAllText(Path.Combine(stage, "program.nb")));
        // The runner and the hooks, on the host, get both.
        var expected = $"work={work} mount=/work";
        Assert.Contains(expected, File.ReadAllText(Path.Combine(stage, "stderr.txt")));
        Assert.Contains(expected, File.ReadAllText(Path.Combine(repo.TryDir("smoke", A, 1), "hooks", "try.setup.log")));
        // The manifest records the mounts beside the runner.
        Assert.Equal("/work", (string)ReadJson(Path.Combine(stage, "manifest.json"))["nb"]!["mounts"]!["work"]!);
    }

    [Fact]
    public void Mounts_AreIgnoredOnABareRun_SoTheShakedownSeesTheHostPaths()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, e => { One(e); WithWorkComment(repo); }, runner: "runners/passthrough.sh", runnerOverride: "none", mounts: new { work = "/work" });
        var entry = Runner.LoadEntry(repo.Root, id);

        var stage = Stage(repo, A, "plain");
        Assert.Equal("completed", Runner.ReadStatus(stage));
        var work = Layout.Work(repo.Root, "smoke", entry.IdOf(A, 1)!);
        Assert.Contains($"# work: {work}\n", File.ReadAllText(Path.Combine(stage, "program.nb")));
        Assert.Contains($"work_mount={work}", File.ReadAllText(Path.Combine(repo.TryDir("smoke", A, 1), "hooks", "try.setup.log")));
        Assert.Null(ReadJson(Path.Combine(stage, "manifest.json"))["nb"]!["mounts"]);
    }

    /// <summary>Name the checkout in a comment of the template, so the resolved program shows what the model was told.</summary>
    static void WithWorkComment(TestRepo repo)
    {
        var file = Path.Combine(repo.Root, "exams", "smoke", "program.nb");
        File.WriteAllText(file, "# work: {{work}}\n" + File.ReadAllText(file));
    }

    [Fact]
    public void Mounts_MustBeAbsolute()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => TestRepo.SetNb(e, null, new { work = "work" }));
        var problem = Assert.Single(repo.Problems("smoke"));
        Assert.Equal("nb.mounts.work", problem.Field);
    }

    [Fact]
    public void RunnerAndMounts_LeftInAuspexJson_AreAProblem_NotIgnored()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        File.WriteAllText(Path.Combine(repo.Root, "exams", "auspex.json"),
            $$"""{ "nb": { "path": "{{TestRepo.NbPath}}", "runner": "runners/x.sh", "mounts": { "work": "/work" } } }""");
        var problems = new List<Problem>();
        Exam.LoadConfig(repo.Root, problems);
        Assert.Equal(["nb.runner", "nb.mounts"], problems.Select(p => p.Field));
        Assert.All(problems, p => Assert.Contains("exam.json", p.Message));
    }

    [Fact]
    public void TheManifestRecordsNbsVersion_ReadFromTheHostBinary()
    {
        using var repo = new TestRepo();
        StartSmoke(repo, One);
        var versions = ReadJson(Path.Combine(Stage(repo, A, "plain"), "manifest.json"))["versions"]!;
        Assert.Matches(@"^\d+\.\d+", (string)versions["nb"]!);
        Assert.DoesNotContain("+", (string)versions["nb"]!);
    }

    [Fact]
    public void Runner_None_OverridesTheConfiguredRunner_AndHooksSeeABareRun()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/never.sh", "#!/usr/bin/env bash\necho 'the runner must not run' >&2\nexit 9\n");
        StartSmoke(repo, One, runner: "runners/never.sh", runnerOverride: "none");

        var stage = Stage(repo, A, "plain");
        Assert.Equal("completed", Runner.ReadStatus(stage));
        Assert.Null(ReadJson(Path.Combine(stage, "manifest.json"))["nb"]!["runner"]);
        Assert.Contains("runner= container=", File.ReadAllText(Path.Combine(repo.TryDir("smoke", A, 1), "hooks", "try.setup.log")));
    }

    [Fact]
    public void Runner_NotFound_IsAUserFacingFailure()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => TestRepo.SetNb(e, "runners/missing.sh", null));
        var problem = Assert.Single(repo.Problems("smoke"));
        Assert.Equal("nb.runner", problem.Field);
        Assert.Contains("script not found", problem.Message);
        // And on the command line, where nothing validated it first.
        repo.EditJson("smoke/exam.json", e => e.Remove("nb"));
        var e = Assert.Throws<AuspexException>(() => StartSmoke(repo, One, runnerOverride: "runners/missing.sh"));
        Assert.Contains("runner not found", e.Message);
        Assert.Contains("--runner", e.Message);
    }
}
