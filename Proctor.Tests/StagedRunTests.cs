using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// A try of a staged exam on the bare runner, against nb's Mock provider: one workspace, nb started once per stage,
/// each stage's files under the try, and the three ways a stage ends (plans/bank.md, "How a stage ends").
/// </summary>
public class StagedRunTests
{
    /// <summary>The smoke exam as one job of the given stages, variant a alone.</summary>
    static void Stage(TestRepo repo, int attempts, bool chained, params (string Id, string Prompt)[] stages)
    {
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        repo.KeepOnlyVariant("smoke", "a");
        // The smoke program with shell writes allowed, so a scripted stage can leave a file for the next.
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nloop 2\nbudget tool_calls 3\napproval bash *\nrun {{prompt}}\n");
        repo.EditJson("smoke/exam.json", e =>
        {
            e["attempts"] = attempts;
            if (chained) e["chained"] = true;
            e["stages"] = new JsonArray(stages.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id, ["prompt"] = s.Prompt }).ToArray());
        });
    }

    static (string? Id, string Log) Run(TestRepo repo)
    {
        var log = new StringWriter();
        var id = Runner.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);
        return (id, log.ToString());
    }

    static string TryDir(TestRepo repo, int n) => Path.Combine(repo.Root, "bank", "smoke", "a", n.ToString());
    static string Status(TestRepo repo, int n, string stage) => File.ReadAllText(Path.Combine(TryDir(repo, n), "stages", stage, "status")).Trim();
    static JsonObject Manifest(TestRepo repo, int n, string stage) => JsonNode.Parse(File.ReadAllText(Path.Combine(TryDir(repo, n), "stages", stage, "manifest.json")))!.AsObject();

    [Fact]
    public void ATry_IsEveryStageInOrder_InOneWorkspace_EachWithItsOwnFiles()
    {
        using var repo = new TestRepo();
        Stage(repo, 1, false, ("write", "MOCK:tool=bash echo one > made.txt"), ("read", "MOCK:tool=bash cat made.txt"), ("say", "MOCK:response=done"));
        var (id, log) = Run(repo);

        Assert.Contains("one job of 3 stages, 1 attempt wanted", log);
        Assert.Contains("a/1  whole", log);
        Assert.Contains("1 attempt made: 1 whole", log);
        Assert.Equal(["1-write", "2-read", "3-say"], Directory.GetDirectories(Path.Combine(TryDir(repo, 1), "stages")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var stage in new[] { "1-write", "2-read", "3-say" })
        {
            Assert.Equal("completed", Status(repo, 1, stage));
            foreach (var file in new[] { "manifest.json", "program.nb", "program.jsonl", "transcript.jsonl", "trajectory.json", "stderr.txt" })
                Assert.True(File.Exists(Path.Combine(TryDir(repo, 1), "stages", stage, file)), $"{stage}/{file}");
        }

        // The workspace is the try's and is never reset: the second stage reads what the first wrote.
        Assert.Contains("\"output\":\"one", File.ReadAllText(Path.Combine(TryDir(repo, 1), "stages", "2-read", "transcript.jsonl")));
        Assert.DoesNotContain("made.txt", File.ReadAllText(Path.Combine(TryDir(repo, 1), "stages", "3-say", "transcript.jsonl")));

        // The hooks run once around the try and the diff is the whole try's.
        Assert.True(File.Exists(Path.Combine(TryDir(repo, 1), "hooks", "attempt.setup.log")));
        Assert.Contains("made.txt", File.ReadAllText(Path.Combine(TryDir(repo, 1), "diff.patch")));
        Assert.False(File.Exists(Path.Combine(TryDir(repo, 1), "stages", "3-say", "diff.patch")));

        // A stage's manifest says its place, and carries the job's hash; the log entry names the stages in order.
        var exam = repo.LoadExam("smoke");
        var manifest = Manifest(repo, 1, "2-read");
        Assert.Equal((2, 1, "read", exam.TaskHashes["read"]), (manifest["stage"]!.GetValue<int>(), manifest["attempt"]!.GetValue<int>(), manifest["task"]!["id"]!.GetValue<string>(), manifest["task"]!["hash"]!.GetValue<string>()));
        Assert.Equal(["write", "read", "say"], Runner.LoadEntry(repo.Root, id!).Stages);

        Assert.Contains("nothing to make", Run(repo).Log);
    }

    [Fact]
    public void TriesAreNumbered_AndARunMakesOnlyTheOnesWanting()
    {
        using var repo = new TestRepo();
        Stage(repo, 1, false, ("say", "MOCK:response=one"), ("again", "MOCK:response=two"));
        Run(repo);
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 3);
        var (_, log) = Run(repo);
        Assert.Contains("1 of 3 attempts   making 2", log);
        Assert.Contains("2 attempts made: 2 whole", log);
        Assert.Equal(["1", "2", "3"], Directory.GetDirectories(Path.Combine(repo.Root, "bank", "smoke", "a")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AStageThatDidNotRun_LosesTheTry_AndNothingMoreOfItIsLaunched()
    {
        using var repo = new TestRepo();
        Stage(repo, 1, false, ("say", "MOCK:response=fine"), ("breaks", "MOCK:throw"), ("after", "MOCK:response=never"));
        var (_, log) = Run(repo);

        Assert.Equal(("completed", "failed", "skipped"), (Status(repo, 1, "1-say"), Status(repo, 1, "2-breaks"), Status(repo, 1, "3-after")));
        Assert.Equal("did not run: provider_error", Manifest(repo, 1, "2-breaks")["status_reason"]!.GetValue<string>());
        Assert.StartsWith("not launched: the try was lost at stage breaks", Manifest(repo, 1, "3-after")["status_reason"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(TryDir(repo, 1), "stages", "3-after", "transcript.jsonl")));
        Assert.Contains("a/1  lost at breaks: did not run: provider_error", log);
        Assert.Contains("1 attempt made: 0 whole, 1 lost", log);

        // None of a lost try is a result, the stage that finished included, and it is not taken up again: a new try is made.
        var exam = repo.LoadExam("smoke");
        Assert.All(Bank.Of(repo.Root, exam), a => Assert.True(a.Lost));
        Assert.Empty(Bank.Select(repo.Root, exam).Current);
        var again = Run(repo).Log;
        Assert.DoesNotContain("interrupted", again);
        Assert.Contains("0 of 1 attempt   making 1", again);
        Assert.True(Directory.Exists(TryDir(repo, 2)));
    }

    [Theory]
    [InlineData(false, "completed")]
    [InlineData(true, "skipped")]
    public void AStoppedStage_IsAResult_AndOnlyAChainedExamBreaksThere(bool chained, string after)
    {
        using var repo = new TestRepo();
        // The Mock repeats a call until the program's budget of three stops it: an exit reason that is neither ok nor a failure to run.
        Stage(repo, 1, chained, ("loops", "MOCK:loop=bash echo x"), ("after", "MOCK:response=fine"));
        var (_, log) = Run(repo);

        Assert.Equal(("completed", after), (Status(repo, 1, "1-loops"), Status(repo, 1, "2-after")));
        Assert.Contains(chained ? "a/1  broke at loops (" : "a/1  whole", log);
        if (chained) Assert.StartsWith("not reached: stage loops was stopped", Manifest(repo, 1, "2-after")["status_reason"]!.GetValue<string>());

        // Either way the try counts: it is whole, and nothing is made again.
        Assert.Contains("1 attempt made: 1 whole", log);
        Assert.All(Bank.Of(repo.Root, repo.LoadExam("smoke")), a => Assert.True(a.Counts));
        Assert.Contains("nothing to make", Run(repo).Log);
    }

    [Fact]
    public void Run_RefusesWhatAJobOfStagesCannotDoYet()
    {
        using var repo = new TestRepo();
        Stage(repo, 1, false, ("say", "MOCK:response=one"));
        Assert.Contains("never run alone", Assert.Throws<ProctorException>(() => Verbs.Run(repo.Root, "smoke", null, null, taskIds: ["say"])).Message);
        Assert.Contains("cannot read yet", Assert.Throws<ProctorException>(() => Verbs.Grade(repo.Root, "smoke")).Message);
        Assert.Contains("cannot read yet", Assert.Throws<ProctorException>(() => Verbs.Report(repo.Root, "smoke")).Message);
    }
}
