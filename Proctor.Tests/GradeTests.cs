using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class GradeTests
{
    [Fact]
    public void ByCheck_CountsDecidedVerdictsPerCheck_TheNewOnes_AndWhyTheRestAreNot()
    {
        static Verdict V(string result) => new(result, "");
        var grades = new List<Grade.AttemptGrade>
        {
            new("a", "t", 1, AttemptStatus.Completed, new() { ["completes"] = V(Verdict.Pass), ["acceptance"] = V(Verdict.Outdated), ["tone"] = V(Verdict.NeedsJudge) }, null, null),
            new("a", "t", 2, AttemptStatus.Completed, new() { ["completes"] = V(Verdict.Fail), ["acceptance"] = V(Verdict.Pass), ["tone"] = V(Verdict.NeedsJudge) }, null, null),
            new("a", "t", 3, AttemptStatus.Failed, null, null, null),
        };
        // Before this grade only a/t/1 had been graded, and only for `completes`.
        var before = new Dictionary<string, Dictionary<string, Verdict>?> { ["a/t/1"] = new() { ["completes"] = V(Verdict.Pass) }, ["a/t/2"] = null };

        Assert.Equal(
        [
            "  completes   graded 2 of 2   1 new",
            "  acceptance  graded 1 of 2   1 new   1 cannot be regraded from what was kept",
            "  tone        graded 0 of 2   2 need a judge",
        ], Grade.ByCheck(grades, before));
    }

    [Fact]
    public void Grade_WritesChecksJsonPerCompletedAttempt_AndComputesPass()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.OneVariantOneAttempt();
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(repo.Root, problems);
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        var entryDir = Layout.LogEntry(repo.Root, id);
        File.WriteAllText(Path.Combine(TestRepo.Attempt(repo.Root, id, "a", "uses-bash", 1), "status"), "failed\n");

        var grades = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);

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
        Assert.False(File.Exists(Path.Combine(TestRepo.Attempt(repo.Root, id, "a", "uses-bash", 1), "checks.json")));

        // checks.json is the map the plan shows, re-readable.
        var written = Grade.ReadChecks(TestRepo.Attempt(repo.Root, id, "a", "plain", 1))!;
        Assert.Equal(exam.Grading.Checks!.Keys.Order(), written.Keys.Order());
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepo.Attempt(repo.Root, id, "a", "plain", 1), "checks.json")))!;
        Assert.Equal("exit_reason=ok", raw["exit_ok"]!["reason"]!.GetValue<string>());
        Assert.Equal("pass", raw["exit_ok"]!["result"]!.GetValue<string>());
    }

    [Fact]
    public void Grade_LimitedToVariantsAndTasks_LeavesTheOtherAttemptsAsTheyWere()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 1);
        var config = Exam.LoadConfig(repo.Root, []);
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        var entryDir = Layout.LogEntry(repo.Root, id);
        bool Graded(string variant, string task) => File.Exists(Path.Combine(TestRepo.Attempt(repo.Root, id, variant, task, 1), "checks.json"));

        var onlyA = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam, variants: ["a"]).Current, TextWriter.Null);
        Assert.All(onlyA, g => Assert.Equal("a", g.Variant));
        Assert.True(Graded("a", "plain"));
        Assert.False(Graded("b", "plain"));

        // a later pass over b's plain task leaves a's checks.json untouched
        var aPlain = Path.Combine(TestRepo.Attempt(repo.Root, id, "a", "plain", 1), "checks.json");
        File.WriteAllText(aPlain, "{}");
        var bPlain = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam, variants: ["b"], tasks: ["plain"]).Current, TextWriter.Null);
        Assert.Equal(("b", "plain"), (Assert.Single(bPlain).Variant, bPlain[0].Task));
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
