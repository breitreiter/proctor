using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>One attempt against nb's Mock provider, then every attempt an exam wants, filling its gaps, continuing an interrupted run, and hook failures.</summary>
public class RunnerTests
{
    static string StartSmoke(TestRepo repo, Action<JsonObject>? editExam = null, string? runner = null, string? runnerOverride = null, object? mounts = null)
    {
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => { editExam?.Invoke(e); TestRepo.SetNb(e, runner, mounts); });
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(repo.Root, problems);
        var exam = repo.LoadExam("smoke");
        return Runner.Run(repo.Root, exam, config, null, runnerOverride, "test", TextWriter.Null);
    }

    /// <summary>`proctor run smoke` again, as it is on disk now: the log entry it wrote or continued, and what it said.</summary>
    static (string? Id, string Log) RunAgain(TestRepo repo, string? runnerOverride = null)
    {
        var log = new StringWriter();
        var id = Runner.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, runnerOverride, "test", log);
        return (id, log.ToString());
    }

    /// <summary>Variant a alone, one attempt of each task: an exam edit that also retires variant b's file.</summary>
    static Action<JsonObject> One(TestRepo repo) => e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; };

    /// <summary>The runner contract's bare equivalent: nb itself, started as proctor would start it, with the program on stdin.</summary>
    const string Passthrough = "#!/usr/bin/env bash\nexec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n";

    static string Attempt(TestRepo repo, string id, string variant, string task, int n) =>
        TestRepo.Attempt(repo.Root, id, variant, task, n);

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    [Fact]
    public void OneAttempt_MatchesTheLayoutPlanFileForFile()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e =>
        {
            repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1;
        });
        var entryDir = Layout.LogEntry(repo.Root, id);
        Assert.Matches(@"^\d{8}-\d{4}-smoke-[a-z0-9]{4}$", id);
        Assert.Equal(Path.Combine(repo.Root, "log", id), entryDir);

        // The log entry: what the run did, and nothing a person reads results from.
        Assert.Equal(["entry.json", "hooks", "status.json"], Directory.GetFileSystemEntries(entryDir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(entryDir, "hooks", "a.setup.log")));
        var entry = ReadJson(Path.Combine(entryDir, "entry.json"));
        Assert.Null(entry["exam_def"]);
        var exam = repo.LoadExam("smoke");
        Assert.Equal(exam.Hash, entry["exam_hash"]!.GetValue<string>());
        // The exam as it stood, once per hash.
        var seen = ReadJson(Path.Combine(repo.Root, "exams-seen", $"sha256-{exam.Hash[7..19]}.json"));
        Assert.Equal(("smoke", exam.Hash), (seen["exam"]!.GetValue<string>(), seen["hash"]!.GetValue<string>()));
        Assert.Equal(1, seen["def"]!["attempts"]!.GetValue<int>());
        Assert.Null(seen["def"]!["variants"]);
        var variant = Assert.Single(entry["variants"]!.AsArray())!;
        Assert.Equal(("a", "Mock"), (variant["id"]!.GetValue<string>(), variant["def"]!["provider"]!.GetValue<string>()));
        Assert.StartsWith("sha256:", variant["hash"]!.GetValue<string>());
        Assert.Equal(["loops", "plain", "uses-bash"], entry["tasks"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()));
        var status = ReadJson(Path.Combine(entryDir, "status.json"));
        Assert.Equal(3, status["planned"]!.GetValue<int>());
        Assert.Equal(3, status["counts"]!["completed"]!.GetValue<int>());

        // The attempt, in the bank by exam, variant, task and number: every file the plan names, and nothing that belongs elsewhere.
        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal(Path.Combine(repo.Root, "bank", "smoke", "a", "plain", "1"), attempt);
        Assert.Equal(
            ["diff.patch", "hooks", "manifest.json", "program.jsonl", "program.nb", "status", "stderr.txt", "trajectory.json", "transcript.jsonl"],
            Directory.GetFileSystemEntries(attempt).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["attempt.setup.log", "attempt.teardown.log"],
            Directory.GetFiles(Path.Combine(attempt, "hooks")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("completed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());

        var manifest = ReadJson(Path.Combine(attempt, "manifest.json"));
        Assert.Matches("^[0-9a-f]{16}$", manifest["id"]!.GetValue<string>());
        Assert.Equal("smoke", manifest["exam"]!.GetValue<string>());
        Assert.Equal(id, manifest["log"]!.GetValue<string>());
        Assert.Equal(("a", exam.Variants.Single().Hash), (manifest["variant"]!["id"]!.GetValue<string>(), manifest["variant"]!["hash"]!.GetValue<string>()));
        Assert.Equal(("plain", exam.TaskHashes["plain"]), (manifest["task"]!["id"]!.GetValue<string>(), manifest["task"]!["hash"]!.GetValue<string>()));
        Assert.StartsWith("sha256:", exam.TaskHashes["plain"]);
        Assert.Equal(1, manifest["attempt"]!.GetValue<int>());
        Assert.Equal("Mock", manifest["provider"]!.GetValue<string>());
        Assert.Equal("nb-jsonl", manifest["transcript"]!["format"]!.GetValue<string>());
        Assert.Equal("completed", manifest["status"]!.GetValue<string>());
        Assert.Equal(1, manifest["tries"]!.GetValue<int>());
        Assert.Null(manifest["exam_hash"]);
        Assert.StartsWith("sha256:", manifest["program_hash"]!.GetValue<string>());
        Assert.True(manifest["duration_ms"]!.GetValue<long>() > 0);
        Assert.Null(manifest["status_reason"]);

        // The program as run: placeholders resolved, nothing else changed.
        var program = File.ReadAllText(Path.Combine(attempt, "program.nb"));
        Assert.Contains("provider Mock\n", program);
        Assert.Contains("run MOCK:response=hello world\n", program);
        Assert.DoesNotContain("{{", program);
        // What went down stdin: the same program as JSONL, compiled on the host.
        var compiled = File.ReadAllLines(Path.Combine(attempt, "program.jsonl"));
        Assert.All(compiled, line => Assert.StartsWith("{", line));
        Assert.Contains(compiled, line => line.Contains("\"type\":\"run\"") && line.Contains("MOCK:response=hello world"));

        // nb's output untouched: the transcript ends in a result trailer.
        var trailer = Transcript.Read(attempt).Trailer;
        Assert.NotNull(trailer);
        Assert.Equal("ok", trailer.ExitReason);
        Assert.Equal("Mock", trailer.Provider);

        // The work directory is where the run happened and the hook put its file.
        Assert.True(File.Exists(Path.Combine(TestRepo.Work(repo.Root, id, "a", "plain", 1), "note.txt")));
    }

    [Fact]
    public void Run_MakesEveryAttemptTheExamWants_AndABadTranscriptIsStillCompleted()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo);
        var entry = Runner.LoadEntry(repo.Root, id);
        Assert.Equal(12, entry.Attempts.Count);
        var statuses = Bank.Of(repo.Root, repo.LoadExam("smoke")).Select(a => a.Status).ToList();
        Assert.Equal(12, statuses.Count);
        Assert.All(statuses, s => Assert.Equal("completed", s));

        // `loops` exhausts the tool-call budget: a transcript with exit_reason max_tool_calls, still `completed`.
        Assert.Equal("max_tool_calls", Transcript.Read(Attempt(repo, id, "b", "loops", 2)).Trailer!.ExitReason);

        // Nothing is missing, so the same command makes nothing.
        var (again, log) = RunAgain(repo);
        Assert.Null(again);
        Assert.Equal("smoke: 3 tasks, 2 attempts wanted of each\n  a  6 of 6 attempts\n  b  6 of 6 attempts\nnothing to make\n", log);
        Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, "log")));
    }

    [Fact]
    public void Run_ContinuesAnInterruptedRun_SkippingCompletedAttempts_AndMakingFailedOnesAgain()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; });

        // Pretend the machine died: one attempt failed, one was running.
        var failedAttempt = Attempt(repo, id, "a", "uses-bash", 1);
        File.WriteAllText(Path.Combine(failedAttempt, "status"), "failed\n");
        File.WriteAllText(Path.Combine(Attempt(repo, id, "a", "loops", 1), "status"), "running\n");
        var untouched = Attempt(repo, id, "a", "plain", 1);
        var untouchedId = ReadJson(Path.Combine(untouched, "manifest.json"))["id"]!.GetValue<string>();
        var untouchedStarted = File.GetLastWriteTimeUtc(Path.Combine(untouched, "transcript.jsonl"));
        var exam = repo.LoadExam("smoke");
        Assert.Equal(id, Assert.Single(Bank.Interrupted(exam, Bank.Of(repo.Root, exam))).Entry.Id);

        var (again, log) = RunAgain(repo);

        Assert.Equal(id, again);
        Assert.StartsWith($"smoke: 3 tasks, 1 attempt wanted of each\n  a  1 of 3 attempts   making 2\nrun {id} was interrupted: 1 of 3 attempts finished, 1 was running, 1 failed\n  continuing it: 2 attempts to make\n", log);
        Assert.EndsWith("2 made: 2 completed\n", log);
        Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, "log")));
        Assert.Equal("completed", Runner.ReadStatus(failedAttempt));
        Assert.Equal(2, ReadJson(Path.Combine(failedAttempt, "manifest.json"))["tries"]!.GetValue<int>());
        Assert.Equal(untouchedId, ReadJson(Path.Combine(untouched, "manifest.json"))["id"]!.GetValue<string>());
        Assert.Equal(untouchedStarted, File.GetLastWriteTimeUtc(Path.Combine(untouched, "transcript.jsonl")));
        Assert.Empty(Bank.Interrupted(exam, Bank.Of(repo.Root, exam)));
    }

    [Fact]
    public void Run_AfterATaskChanged_ItsAttemptsAreStale_AndOnlyItIsMadeAgain()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; });
        var before = repo.LoadExam("smoke").TaskHashes["plain"];
        repo.EditJson("smoke/tasks/plain.json", c => c["prompt"] = "MOCK:response=changed");

        var (again, log) = RunAgain(repo);

        Assert.NotEqual(id, again);
        Assert.StartsWith($"smoke: 3 tasks, 1 attempt wanted of each\n  a  2 of 3 attempts   making 1\nrun {again}: 1 attempt to make\n", log);
        var entry = Runner.LoadEntry(repo.Root, again!);
        // Numbered after the stale one, so a/plain/n names one attempt across the exam.
        Assert.Equal([new AttemptRef("a", "plain", 2)], entry.Attempts);
        var exam = repo.LoadExam("smoke");
        var selection = Bank.Select(repo.Root, exam);
        Assert.Equal(["a/loops/1", "a/plain/2", "a/uses-bash/1"], selection.Current.Select(a => a.Label).Order());
        var stale = Assert.Single(selection.Stale);
        Assert.Equal("a/plain/1", stale.Attempt);
        Assert.Equal($"task plain changed ({before[7..15]} then, {exam.TaskHashes["plain"][7..15]} now)", stale.Why);
    }

    [Fact]
    public void Run_AfterAVariantIsAdded_MakesOnlyItsAttempts()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; });
        repo.AddVariant("smoke", """{ "id": "c", "runner": "nb", "harness": "nb", "provider": "Mock", "model": "mock" }""");

        var (again, _) = RunAgain(repo);

        var entry = Runner.LoadEntry(repo.Root, again!);
        Assert.Equal(["c"], entry.Variants.Select(v => v.Id));
        Assert.Equal(3, entry.Attempts.Count);
        Assert.All(entry.Attempts, a => Assert.Equal(("c", 1), (a.Variant, a.Attempt)));
        Assert.Equal(6, Bank.Select(repo.Root, repo.LoadExam("smoke")).Current.Count);

        // --variant narrows: a has all it wants, so asking for a alone makes nothing.
        var log = new StringWriter();
        Assert.Null(Runner.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, null, "test", log, variants: ["a"]));
        Assert.Equal("smoke: 3 tasks, 1 attempt wanted of each\n  a  3 of 3 attempts\n  c  3 of 3 attempts\nnothing to make\n", log.ToString());
    }

    /// <summary>D5: a variant is its content, so a changed one under the id that made attempts would pass for the old one.</summary>
    [Fact]
    public void Run_RefusesAChangedVariantUnderAnIdThatMadeAttempts_AndSaysWhatChanged()
    {
        using var repo = new TestRepo();
        StartSmoke(repo, e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; });
        repo.EditVariant("smoke", "a", v => { v["model"] = "other"; v["note"] = "a note is no part of it"; });
        var config = Exam.LoadConfig(repo.Root, []);

        var e = Assert.Throws<ProctorException>(() => Runner.Run(repo.Root, repo.LoadExam("smoke"), config, null, null, "test", TextWriter.Null));
        Assert.Equal("exams/smoke/variants/a.json: this is not the a that made 3 attempts\n"
            + "  (model mock then, other now). A changed variant is a new variant: give it a new id.", e.Message);
        Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, "log")));

        // Under a new id it is a new variant, and runs.
        repo.RenameVariant("smoke", "a", "a-other");
        var id = Runner.Run(repo.Root, repo.LoadExam("smoke"), config, null, null, "test", TextWriter.Null)!;
        Assert.Equal("completed", Runner.ReadStatus(Attempt(repo, id, "a-other", "plain", 1)));
    }

    [Fact]
    public void AGitBundlesPaths_AreTheOnlyFilesItHands_AndPartOfItsIdentity()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var source = Directory.CreateDirectory(Path.Combine(repo.Root, "upstream")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "docs", "deep"));
        File.WriteAllText(Path.Combine(source, "README.md"), "readme\n");
        File.WriteAllText(Path.Combine(source, "docs", "deep", "page.md"), "page\n");
        File.WriteAllText(Path.Combine(source, "secret.md"), "not in the bundle\n");
        foreach (var args in new[] { "init -q", "add -A", "-c user.email=t@t -c user.name=t commit -q -m one" })
            Assert.Equal(0, Subprocess.Run("git", args.Split(' '), source).ExitCode);
        var rev = Subprocess.Run("git", ["rev-parse", "HEAD"], source).Stdout.Trim();
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse($$"""{"git": "{{source}}", "rev": "{{rev}}", "paths": ["docs/", "README.md"]}"""));

        var exam = repo.LoadExam("smoke");
        var bundle = Runner.ResolveBundles(repo.Root, exam.Variants)["b"];
        Assert.Equal(["README.md", "docs/deep/page.md"], Tree.Files(bundle.Path, new HashSet<string>()).Select(f => f.Relative).Order(StringComparer.Ordinal));
        Assert.Equal($"git:{rev}:README.md,docs/", bundle.Hash);

        // Another subset of the same revision is another variant, in a directory of its own.
        var whole = exam.Variants.Single(v => v.Id == "b").Hash;
        repo.EditVariant("smoke", "b", v => v["bundle"]!["paths"] = JsonNode.Parse("""["README.md"]"""));
        var narrower = repo.LoadExam("smoke");
        Assert.NotEqual(whole, narrower.Variants.Single(v => v.Id == "b").Hash);
        Assert.NotEqual(bundle.Path, Runner.ResolveBundles(repo.Root, narrower.Variants)["b"].Path);

        repo.EditVariant("smoke", "b", v => v["bundle"]!["paths"] = JsonNode.Parse("""["missing.md"]"""));
        var e = Assert.Throws<ProctorException>(() => Runner.ResolveBundles(repo.Root, repo.LoadExam("smoke").Variants));
        Assert.Contains("missing.md is not in", e.Message);
    }

    [Fact]
    public void AttemptHookFailure_MarksTheAttemptFailed_AndTheMatrixContinues()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/hooks/flaky-setup.sh", "#!/usr/bin/env bash\nif [ \"$PROCTOR_TASK\" = plain ]; then echo 'no fixture for plain' >&2; exit 7; fi\nmkdir -p \"$PROCTOR_WORK\"\n");
        File.SetUnixFileMode(Path.Combine(repo.Root, "exams/smoke/hooks/flaky-setup.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = StartSmoke(repo, e =>
        {
            repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1;
            e["hooks"]!["attempt"]!["setup"] = "hooks/flaky-setup.sh";
        });

        var failed = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(failed));
        var manifest = ReadJson(Path.Combine(failed, "manifest.json"));
        Assert.Equal("failed", manifest["status"]!.GetValue<string>());
        Assert.Contains("hooks/flaky-setup.sh exited 7: no fixture for plain", manifest["status_reason"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(failed, "transcript.jsonl")), "nb must not run when setup failed");
        Assert.True(File.Exists(Path.Combine(failed, "hooks", "attempt.teardown.log")), "teardown runs whenever setup ran, so a half-made environment is not left behind");
        Assert.True(File.Exists(Path.Combine(failed, "diff.patch")));

        Assert.Equal("completed", Runner.ReadStatus(Attempt(repo, id, "a", "loops", 1)));
        Assert.Equal("completed", Runner.ReadStatus(Attempt(repo, id, "a", "uses-bash", 1)));
        var counts = ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "status.json"))["counts"]!;
        Assert.Equal(1, counts["failed"]!.GetValue<int>());
        Assert.Equal(2, counts["completed"]!.GetValue<int>());
    }

    [Fact]
    public void Run_ContinuingARunWhoseAttemptFailsAgain_LeavesItForTheNextRun_AndStartsNoSecondOne()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/hooks/flaky-setup.sh", "#!/usr/bin/env bash\nif [ \"$PROCTOR_TASK\" = plain ]; then echo 'no fixture for plain' >&2; exit 7; fi\nmkdir -p \"$PROCTOR_WORK\"\n");
        File.SetUnixFileMode(Path.Combine(repo.Root, "exams/smoke/hooks/flaky-setup.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = StartSmoke(repo, e =>
        {
            repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1;
            e["hooks"]!["attempt"]!["setup"] = "hooks/flaky-setup.sh";
        });

        var log = new StringWriter();
        var again = Runner.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);

        Assert.Equal(id, again);
        Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, Layout.LogDir)));
        Assert.Equal("failed", Runner.ReadStatus(Attempt(repo, id, "a", "plain", 1)));
        Assert.Contains("1 attempt failed again and is left for the next `proctor run smoke`", log.ToString());
    }

    [Fact]
    public void VariantHookFailure_FailsEveryAttemptOfThatVariant_AndTheNextVariantRuns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/hooks/variant-b-fails.sh", "#!/usr/bin/env bash\n[ \"$PROCTOR_VARIANT\" = b ] && { echo 'fakes did not start' >&2; exit 1; }\nexit 0\n");
        File.SetUnixFileMode(Path.Combine(repo.Root, "exams/smoke/hooks/variant-b-fails.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = StartSmoke(repo, e =>
        {
            e["attempts"] = 1;
            e["hooks"]!["variant"]!["setup"] = "hooks/variant-b-fails.sh";
        });

        foreach (var c in new[] { "loops", "plain", "uses-bash" })
        {
            Assert.Equal("completed", Runner.ReadStatus(Attempt(repo, id, "a", c, 1)));
            Assert.Equal("failed", Runner.ReadStatus(Attempt(repo, id, "b", c, 1)));
            Assert.Contains("variant setup hook failed", ReadJson(Path.Combine(Attempt(repo, id, "b", c, 1), "manifest.json"))["status_reason"]!.GetValue<string>());
        }
    }

    [Fact]
    public void NbStartupError_IsFailed_NotCompleted()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e =>
        {
            repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1;
            repo.EditVariant("smoke", "a", v => v["provider"] = "NoSuchEntry");
        });
        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(attempt));
        var reason = ReadJson(Path.Combine(attempt, "manifest.json"))["status_reason"]!.GetValue<string>();
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
        repo.WriteProctorConfig();
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, new List<Problem>()), null, null, "test", TextWriter.Null)!;

        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(attempt));
        // The source keeps the include for reading; the compiled program carries its body and no path.
        var source = File.ReadAllText(Path.Combine(attempt, "program.nb"));
        Assert.Contains("system @notes.md\n", source);
        var compiled = File.ReadAllText(Path.Combine(attempt, "program.jsonl"));
        Assert.Contains("Never mention the sheet.", compiled);
        Assert.DoesNotContain("notes.md", compiled);
        var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Equal(hash, ReadJson(Path.Combine(attempt, "manifest.json"))["program_hash"]!.GetValue<string>());
        // And the model saw it: the transcript's system turn is the note.
        Assert.Contains("Never mention the sheet.", File.ReadAllText(Path.Combine(attempt, "transcript.jsonl")));
    }

    [Fact]
    public void Compile_Failure_IsFailed_BeforeAnyHookRuns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var template = File.ReadAllText(Path.Combine(repo.Root, "exams/smoke/program.nb")).Replace("run {{prompt}}", "system @missing.md\nrun {{prompt}}");
        repo.Write("smoke/program.nb", template);
        repo.WriteProctorConfig();
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, new List<Problem>()), null, null, "test", TextWriter.Null)!;

        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("failed", Runner.ReadStatus(attempt));
        var reason = ReadJson(Path.Combine(attempt, "manifest.json"))["status_reason"]!.GetValue<string>();
        Assert.Equal("program compile failed: nb --compile exited 1: Error: @include not found: missing.md", reason);
        Assert.False(File.Exists(Path.Combine(attempt, "program.jsonl")));
        Assert.False(File.Exists(Path.Combine(attempt, "transcript.jsonl")));
        Assert.False(File.Exists(Path.Combine(attempt, "hooks", "attempt.setup.log")), "nothing was set up for a program nb refused");
        Assert.False(File.Exists(Path.Combine(attempt, "hooks", "attempt.teardown.log")));
    }

    [Fact]
    public void ResolveProgram_EscapesNewlinesAsContinuations()
    {
        var variant = new Variant("floor", "nb", "nb", "Mock", "m", null);
        var c = new TaskDef("x", null, "Add a flag.\nKeep tests green.\n  Indented line.", null);
        var program = Runner.ResolveProgram("run {{prompt}}\n# {{task}} on {{variant}} attempt {{attempt}} in {{work}}\n", variant, c, 2, "/w");
        Assert.Equal("run Add a flag. \\\nKeep tests green. \\\n  Indented line.\n# x on floor attempt 2 in /w\n", program);
    }

    [Fact]
    public void Fixture_IsCheckedOutBeforeTheAttempt_AndTheDiffCollectedAfter()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { repo.KeepOnlyVariant("smoke", "a"); e["attempts"] = 1; });
        var attempt = Attempt(repo, id, "a", "plain", 1);
        var work = TestRepo.Work(repo.Root, id, "a", "plain", 1);

        var manifest = ReadJson(Path.Combine(attempt, "manifest.json"));
        Assert.Equal("note", manifest["fixture"]!["id"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["fixture"]!["hash"]!.GetValue<string>());

        // The checkout is a git repository with the fixture committed; the teardown hook's edit is the diff.
        Assert.True(Directory.Exists(Path.Combine(work, ".git")));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(work, "note.txt")).Trim());
        var diff = File.ReadAllText(Path.Combine(attempt, "diff.patch"));
        Assert.Contains("diff --git a/note.txt b/note.txt", diff);
        Assert.Contains("-original", diff);
        Assert.Contains("+changed", diff);
        Assert.Equal(["note.txt"], Transcript.Read(attempt).TouchedPaths());
    }

    [Fact]
    public void Checkout_RestoresTheFixturePlusTheDiff_WhenTheWorkDirectoryIsGone()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var fixture = repo.LoadExam("smoke").Fixtures["note"];
        var work = Path.Combine(repo.Root, ".proctor/work/x");
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
    public void Bundle_IsResolvedOncePerRun_RecordedPerAttempt_AndAChangedOneIsRefused()
    {
        using var repo = new TestRepo();
        var id = StartSmoke(repo, e => { e["attempts"] = 1; });
        var bundleDir = Path.Combine(repo.Root, "bundles", "smoke");

        var entry = ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "entry.json"));
        Assert.Null(entry["bundles"]!["a"]);
        Assert.Equal("bundles/smoke", entry["bundles"]!["b"]!["source"]!.GetValue<string>());
        Assert.Equal(bundleDir, entry["bundles"]!["b"]!["path"]!.GetValue<string>());
        Assert.StartsWith("sha256:", entry["bundles"]!["b"]!["hash"]!.GetValue<string>());

        var b = Attempt(repo, id, "b", "plain", 1);
        Assert.Equal(entry["bundles"]!["b"]!["hash"]!.GetValue<string>(), ReadJson(Path.Combine(b, "manifest.json"))["bundle"]!["hash"]!.GetValue<string>());
        Assert.Contains($"# bundle: {bundleDir}\n", File.ReadAllText(Path.Combine(b, "program.nb")));
        var a = Attempt(repo, id, "a", "plain", 1);
        Assert.Null(ReadJson(Path.Combine(a, "manifest.json"))["bundle"]);
        Assert.Contains("# bundle: \n", File.ReadAllText(Path.Combine(a, "program.nb")));

        // A changed bundle is a changed variant, and b made attempts under its old one.
        File.AppendAllText(Path.Combine(bundleDir, "README.md"), "edited\n");
        var e = Assert.Throws<ProctorException>(() => RunAgain(repo));
        Assert.StartsWith("exams/smoke/variants/b.json: this is not the b that made 3 attempts\n  (bundle ", e.Message);
    }

    [Fact]
    public void Runner_Passthrough_ProducesTheSameAttemptAsABareRun()
    {
        using var bare = new TestRepo();
        var bareId = StartSmoke(bare, One(bare));
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, One(repo), runner: "runners/passthrough.sh");

        foreach (var c in new[] { "loops", "plain", "uses-bash" })
        {
            var (a, b) = (Attempt(bare, bareId, "a", c, 1), Attempt(repo, id, "a", c, 1));
            Assert.Equal("completed", Runner.ReadStatus(b));
            // Identical but for the milliseconds nb measures.
            foreach (var file in new[] { "transcript.jsonl", "program.nb", "program.jsonl", "diff.patch", "status" })
                Assert.Equal(Timeless(File.ReadAllText(Path.Combine(a, file))), Timeless(File.ReadAllText(Path.Combine(b, file))));
            Assert.Equal(Directory.GetFileSystemEntries(a).Select(Path.GetFileName).Order(), Directory.GetFileSystemEntries(b).Select(Path.GetFileName).Order());
        }

        // The manifest and the log entry say how nb was run; a bare run says nothing.
        var manifest = ReadJson(Path.Combine(Attempt(repo, id, "a", "plain", 1), "manifest.json"));
        Assert.Equal("runners/passthrough.sh", manifest["nb"]!["runner"]!["script"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Equal(TestRepo.NbPath, manifest["nb"]!["path"]!.GetValue<string>());
        var entry = ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "entry.json"));
        Assert.Equal(manifest["nb"]!["runner"]!["hash"]!.GetValue<string>(), entry["nb"]!["runner"]!["hash"]!.GetValue<string>());
        Assert.Null(ReadJson(Path.Combine(Attempt(bare, bareId, "a", "plain", 1), "manifest.json"))["nb"]!["runner"]);

        static string Timeless(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"""(duration|provider)_ms"":\d+", "");
    }

    [Fact]
    public void Runner_GetsTheProgramOnStdin_TheAttemptEnvironment_AndTheWorkDirectory()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"argv=$#\" >&2\necho \"cwd=$PWD\" >&2\necho \"container=$PROCTOR_CONTAINER\" >&2\necho \"runner=$PROCTOR_RUNNER\" >&2\necho \"work=$PROCTOR_WORK\" >&2\necho \"nocolor=$NO_COLOR\" >&2\n"
            + "exec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n");
        var id = StartSmoke(repo, One(repo), runner: "runners/spy.sh");

        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(attempt));
        var stderr = File.ReadAllText(Path.Combine(attempt, "stderr.txt"));
        var work = TestRepo.Work(repo.Root, id, "a", "plain", 1);
        Assert.Contains("argv=0\n", stderr);
        Assert.Contains($"cwd={work}\n", stderr);
        Assert.Contains($"work={work}\n", stderr);
        Assert.Contains("container=proctor-smoke-a-plain-1\n", stderr);
        Assert.Contains("runner=runners/spy.sh\n", stderr);
        Assert.Contains("nocolor=1\n", stderr);
        // The hooks see the same runner and container name, so they can own a container nb is exec'd into.
        Assert.Contains("proctor-smoke-a-plain-1", File.ReadAllText(Path.Combine(attempt, "hooks", "attempt.setup.log")));
        Assert.Contains("runners/spy.sh", File.ReadAllText(Path.Combine(Layout.LogEntry(repo.Root, id), "hooks", "a.setup.log")));
    }

    [Fact]
    public void Mounts_ResolveThePlaceholders_AndHooksSeeBothTheHostPathAndTheMount()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/spy.sh", "#!/usr/bin/env bash\n"
            + "echo \"work=$PROCTOR_WORK mount=$PROCTOR_WORK_MOUNT bundle=$PROCTOR_BUNDLE bundle_mount=$PROCTOR_BUNDLE_MOUNT\" >&2\n"
            + "exec \"$PROCTOR_NB\" --output jsonl --config \"$PROCTOR_NB_CONFIG\" -\n");
        repo.WriteScript("smoke/hooks/mounts.sh", "#!/usr/bin/env bash\necho \"work=$PROCTOR_WORK mount=$PROCTOR_WORK_MOUNT bundle=$PROCTOR_BUNDLE bundle_mount=$PROCTOR_BUNDLE_MOUNT\"\n");
        // Variant b is the one with a bundle.
        var id = StartSmoke(repo, e =>
        {
            repo.KeepOnlyVariant("smoke", "b"); e["attempts"] = 1;
            e["hooks"]!["attempt"]!["setup"] = "hooks/mounts.sh";
            WithWorkComment(repo);
        }, runner: "runners/spy.sh", mounts: new { work = "/work", bundle = "/bundle" });

        var attempt = Attempt(repo, id, "b", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(attempt));
        var work = TestRepo.Work(repo.Root, id, "b", "plain", 1);
        var bundleDir = Path.Combine(repo.Root, "bundles", "smoke");
        // The program tells the model the paths inside the container.
        var program = File.ReadAllText(Path.Combine(attempt, "program.nb"));
        Assert.Contains("# work: /work\n", program);
        Assert.Contains("# bundle: /bundle\n", program);
        // The runner and the hooks, on the host, get both.
        var expected = $"work={work} mount=/work bundle={bundleDir} bundle_mount=/bundle";
        Assert.Contains(expected, File.ReadAllText(Path.Combine(attempt, "stderr.txt")));
        Assert.Contains(expected, File.ReadAllText(Path.Combine(attempt, "hooks", "attempt.setup.log")));
        // The manifest records the mounts beside the runner.
        var nb = ReadJson(Path.Combine(attempt, "manifest.json"))["nb"]!;
        Assert.Equal("/work", (string)nb["mounts"]!["work"]!);
        Assert.Equal("/bundle", (string)nb["mounts"]!["bundle"]!);
    }

    [Fact]
    public void Mounts_AreIgnoredOnABareRun_SoTheShakedownSeesTheHostPaths()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, e => { One(repo)(e); WithWorkComment(repo); }, runner: "runners/passthrough.sh", runnerOverride: "none", mounts: new { work = "/work" });

        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(attempt));
        var work = TestRepo.Work(repo.Root, id, "a", "plain", 1);
        Assert.Contains($"# work: {work}\n", File.ReadAllText(Path.Combine(attempt, "program.nb")));
        Assert.Contains($"work_mount={work}", File.ReadAllText(Path.Combine(attempt, "hooks", "attempt.setup.log")));
        Assert.Null(ReadJson(Path.Combine(attempt, "manifest.json"))["nb"]!["mounts"]);
    }

    /// <summary>The smoke template names the bundle in a comment; add the checkout the same way, so the resolved program shows what the model was told.</summary>
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
    public void RunnerAndMounts_LeftInProctorJson_AreAProblem_NotIgnored()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        File.WriteAllText(Path.Combine(repo.Root, "exams", "proctor.json"),
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
        var id = StartSmoke(repo, One(repo));
        var versions = ReadJson(Path.Combine(Attempt(repo, id, "a", "plain", 1), "manifest.json"))["versions"]!;
        Assert.Matches(@"^\d+\.\d+", (string)versions["nb"]!);
        Assert.DoesNotContain("+", (string)versions["nb"]!);
    }

    [Fact]
    public void Runner_None_OverridesTheConfiguredRunner_AndHooksSeeABareRun()
    {
        using var repo = new TestRepo();
        repo.WriteScript("runners/never.sh", "#!/usr/bin/env bash\necho 'the runner must not run' >&2\nexit 9\n");
        var id = StartSmoke(repo, One(repo), runner: "runners/never.sh", runnerOverride: "none");

        var attempt = Attempt(repo, id, "a", "plain", 1);
        Assert.Equal("completed", Runner.ReadStatus(attempt));
        Assert.Null(ReadJson(Path.Combine(attempt, "manifest.json"))["nb"]!["runner"]);
        Assert.Contains("runner= container=", File.ReadAllText(Path.Combine(attempt, "hooks", "attempt.setup.log")));
    }

    [Fact]
    public void Run_DoesNotContinueARunWhoseRunnerChanged_AndMakesWhatIsMissingInANewOne()
    {
        using var repo = new TestRepo();
        var script = repo.WriteScript("runners/passthrough.sh", Passthrough);
        var id = StartSmoke(repo, One(repo), runner: "runners/passthrough.sh");
        File.WriteAllText(Path.Combine(Attempt(repo, id, "a", "plain", 1), "status"), "failed\n");

        File.AppendAllText(script, "# edited\n");
        var (again, log) = RunAgain(repo);
        Assert.Contains($"run {id} was interrupted: 2 of 3 attempts finished, 1 failed\n  not continuing it: the runner is runners/passthrough.sh sha256:", log);
        Assert.NotEqual(id, again);
        Assert.Contains($"run {again}: 1 attempt to make\n", log);
        Assert.Equal("completed", Runner.ReadStatus(Attempt(repo, again!, "a", "plain", 2)));
        Assert.Equal("failed", Runner.ReadStatus(Attempt(repo, id, "a", "plain", 1)));

        // Dropping the runner is a change too; continuing a run that had a runner bare would run nb somewhere else.
        File.WriteAllText(Path.Combine(Attempt(repo, again!, "a", "plain", 2), "status"), "failed\n");
        var (_, bare) = RunAgain(repo, runnerOverride: "none");
        Assert.Contains($"run {again} was interrupted: 0 of 1 attempt finished, 1 failed\n  not continuing it: the runner is none now, runners/passthrough.sh sha256:", bare);
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
        var e = Assert.Throws<ProctorException>(() => StartSmoke(repo, One(repo), runnerOverride: "runners/missing.sh"));
        Assert.Contains("runner not found", e.Message);
        Assert.Contains("--runner", e.Message);
    }
}
