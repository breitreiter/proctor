using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class GradeTests
{
    [Fact]
    public void Grade_WritesChecksJsonPerCompletedCell_AndComputesPass()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; });
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(repo.Root, problems);
        var suite = repo.LoadSuite("smoke");
        var id = Runner.Start(repo.Root, suite, config, null, null, "test", TextWriter.Null);
        var expDir = Layout.Experiment(repo.Root, id);
        File.WriteAllText(Path.Combine(Layout.Cell(expDir, "a", "uses-bash", 1), "status"), "failed\n");

        var grades = Grade.Experiment(repo.Root, Runner.LoadExperiment(repo.Root, id), suite, TextWriter.Null);

        Assert.Equal(3, grades.Count);
        var plain = grades.Single(g => g.Task == "plain");
        Assert.True(plain.Pass);
        Assert.Equal("pass", plain.Checks!["exit_ok"].Result);
        Assert.Equal("pass", plain.Checks["says-expected"].Result);
        Assert.Equal("pass", plain.Checks["touches-note"].Result);
        Assert.Equal("pass", plain.Checks["answer-nonempty"].Result);
        Assert.Contains("words", plain.Checks["answer-nonempty"].Reason);

        var loops = grades.Single(g => g.Task == "loops");
        Assert.False(loops.Pass);
        Assert.Equal("fail", loops.Checks!["exit_ok"].Result);
        Assert.Equal("fail", loops.Checks["not_nudged"].Result);
        Assert.Equal("pass", loops.Checks["under_budget"].Result);

        var failed = grades.Single(g => g.Task == "uses-bash");
        Assert.Null(failed.Checks);
        Assert.Null(failed.Pass);
        Assert.False(File.Exists(Path.Combine(Layout.Cell(expDir, "a", "uses-bash", 1), "checks.json")));

        // checks.json is the map the plan shows, re-readable.
        var written = Grade.ReadChecks(Layout.Cell(expDir, "a", "plain", 1))!;
        Assert.Equal(suite.Grading.Checks!.Keys.Order(), written.Keys.Order());
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(Layout.Cell(expDir, "a", "plain", 1), "checks.json")))!;
        Assert.Equal("exit_reason=ok", raw["exit_ok"]!["reason"]!.GetValue<string>());
        Assert.Equal("pass", raw["exit_ok"]!["result"]!.GetValue<string>());
    }

    [Fact]
    public void Grade_LimitedToArmsAndTasks_LeavesTheOtherCellsAsTheyWere()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => { foreach (var arm in e["arms"]!.AsArray()) arm!["samples"] = 1; });
        var config = Suite.LoadConfig(repo.Root, []);
        var suite = repo.LoadSuite("smoke");
        var id = Runner.Start(repo.Root, suite, config, null, null, "test", TextWriter.Null);
        var expDir = Layout.Experiment(repo.Root, id);
        var experiment = Runner.LoadExperiment(repo.Root, id);
        bool Graded(string arm, string task) => File.Exists(Path.Combine(Layout.Cell(expDir, arm, task, 1), "checks.json"));

        var onlyA = Grade.Experiment(repo.Root, experiment, suite, TextWriter.Null, arms: ["a"]);
        Assert.All(onlyA, g => Assert.Equal("a", g.Arm));
        Assert.True(Graded("a", "plain"));
        Assert.False(Graded("b", "plain"));

        // a later pass over b's plain task leaves a's checks.json untouched
        var aPlain = Path.Combine(Layout.Cell(expDir, "a", "plain", 1), "checks.json");
        File.WriteAllText(aPlain, "{}");
        var bPlain = Grade.Experiment(repo.Root, experiment, suite, TextWriter.Null, arms: ["b"], tasks: ["plain"]);
        Assert.Equal(("b", "plain"), (Assert.Single(bPlain).Arm, bPlain[0].Task));
        Assert.True(Graded("b", "plain"));
        Assert.False(Graded("b", "loops"));
        Assert.Equal("{}", File.ReadAllText(aPlain));
    }

    [Fact]
    public void Pass_IsTheConjunctionOfNamedChecks_ErrorIsNotAPass()
    {
        var checks = new Dictionary<string, Verdict>
        {
            ["a"] = new("pass", ""), ["b"] = new("fail", ""), ["c"] = new("error", ""), ["d"] = new("needs-judge", ""),
        };
        Assert.True(Grade.Pass(checks, ["a"]));
        Assert.False(Grade.Pass(checks, ["a", "b"]));
        Assert.False(Grade.Pass(checks, ["a", "c"]));
        Assert.Null(Grade.Pass(checks, ["a", "d"]));            // undecided: nothing failed, one check needs a judge
        Assert.False(Grade.Pass(checks, ["b", "d"]));           // a fail decides it, whatever else is undecided
        Assert.Equal("d: ", Grade.Undecided(checks, ["a", "d"]));
        Assert.Null(Grade.Undecided(checks, ["b", "d"]));
    }

    [Fact]
    public void Invalid_IsTheFirstFailedValidityCheck_ErrorDoesNotInvalidate()
    {
        var checks = new Dictionary<string, Verdict>
        {
            ["a"] = new("pass", ""), ["b"] = new("fail", "read ../tasks"), ["c"] = new("error", "script missing"), ["d"] = new("fail", "second"),
        };
        Assert.Null(Grade.Invalid(checks, null));
        Assert.Null(Grade.Invalid(checks, ["a", "c"]));
        Assert.Equal("b: read ../tasks", Grade.Invalid(checks, ["a", "b", "d"]));
    }
}
