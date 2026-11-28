using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>
/// A named configuration retired (the user, 2026-11-27): recorded with its reason in exams/&lt;exam&gt;/retired.json,
/// given no new tries by `run`, and brought back with nothing lost.
/// </summary>
public class RetiredTests
{
    const string Id = "axes-answers";

    static TestRepo Repo()
    {
        var repo = new TestRepo();
        repo.CopyExam(Id);
        return repo;
    }

    static string File(TestRepo repo) => Path.Combine(repo.Root, "exams", Id, "retired.json");

    static List<string?> Asked(TestRepo repo, string? name = null)
    {
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam(Id), null, null, name);
        return keys.Select(k => exam.Axes!.Of(k)!.Name).ToList();
    }

    [Fact]
    public void RetiringRecordsTheReasonAndRunLeavesItOut()
    {
        using var repo = Repo();
        Assert.Contains("rewritten", Asked(repo));

        Assert.Equal(0, Verbs.Retire(repo.Root, Id, "rewritten", "the rewrite was dropped", false));

        var mark = JsonNode.Parse(System.IO.File.ReadAllText(File(repo)))!["configurations"]!["rewritten"]!;
        Assert.Equal("retired/1", (string)JsonNode.Parse(System.IO.File.ReadAllText(File(repo)))!["schema"]!);
        Assert.Equal("the rewrite was dropped", (string)mark["reason"]!);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", (string)mark["since"]!);
        Assert.DoesNotContain("rewritten", Asked(repo));
        Assert.Contains("long", Asked(repo));
        var refused = Assert.Throws<AuspexException>(() => Asked(repo, "rewritten"));
        Assert.Contains("is retired", refused.Message);
        Assert.Contains("--bring-back", refused.Message);
    }

    [Fact]
    public void ARetiredConfigurationIsStillNamedAndItsHashesAreUnmoved()
    {
        using var repo = Repo();
        var before = repo.LoadExam(Id);
        Verbs.Retire(repo.Root, Id, "rewritten", "dropped", false);
        var after = repo.LoadExam(Id);

        Assert.Equal(before.Hash, after.Hash);
        Assert.Contains(after.Axes!.Named, c => c.Name == "rewritten");
        Assert.Equal(before.Axes!.Named.Single(c => c.Name == "rewritten").Hash, after.Axes.Named.Single(c => c.Name == "rewritten").Hash);
    }

    [Fact]
    public void CoverageSaysWhichConfigurationsAreRetired_AndStillCountsThem()
    {
        using var repo = Repo();
        Verbs.Retire(repo.Root, Id, "rewritten", "the rewrite was dropped", false);

        var coverage = Answers.Coverage(repo.Root, repo.LoadExam(Id), DateTime.UtcNow);

        Assert.Equal(new RetiredMark("the rewrite was dropped", DateTime.UtcNow.ToString("yyyy-MM-dd")), coverage.Configurations.Single(c => c.Name == "rewritten").Retired);
        Assert.Null(coverage.Configurations.Single(c => c.Name == "long").Retired);
        Assert.Contains("retired on a configuration", coverage.Methods);
    }

    [Fact]
    public void BroughtBackItRunsAgainAndAnExamWithNothingRetiredHasNoFile()
    {
        using var repo = Repo();
        Verbs.Retire(repo.Root, Id, "rewritten", "dropped", false);

        Assert.Equal(0, Verbs.Retire(repo.Root, Id, "rewritten", null, true));

        Assert.Contains("rewritten", Asked(repo));
        Assert.False(System.IO.File.Exists(File(repo)));
        Assert.Contains("is not retired", Assert.Throws<AuspexException>(() => Verbs.Retire(repo.Root, Id, "rewritten", null, true)).Message);
    }

    [Fact]
    public void TheJsonAnswerSaysWhatWasDoneAndWhatIsRetiredNow()
    {
        using var repo = Repo();
        repo.Write($"{Id}/retired.json", """{ "schema": "retired/1", "configurations": { "gone": { "reason": "renamed away", "since": "2026-01-02" } } }""");

        var retired = Verbs.Retiring(repo.Root, Id, "rewritten", "dropped", false).Answer;
        Assert.Equal(("retire/1", Id, $"exams/{Id}/retired.json", "retired", "rewritten"), (retired.Schema, retired.Exam, retired.File, retired.Did, retired.Configuration));
        Assert.Null(retired.Was);
        Assert.Equal(["gone", "rewritten"], retired.Retired.Keys);
        Assert.False(retired.Retired["gone"].Named);
        Assert.True(retired.Retired["rewritten"].Named);

        var again = Verbs.Retiring(repo.Root, Id, "rewritten", "dropped for good", false).Answer;
        Assert.Equal(("reason_replaced", "dropped"), (again.Did, again.Was!.Reason));

        var back = Verbs.Retiring(repo.Root, Id, "rewritten", null, true).Answer;
        Assert.Equal(("brought_back", "dropped for good"), (back.Did, back.Was!.Reason));
        Assert.Equal(["gone"], back.Retired.Keys);

        var listed = Verbs.Retiring(repo.Root, Id, null, null, false).Answer;
        Assert.Equal(("listed", null), (listed.Did, listed.Configuration));

        var json = System.Text.Json.JsonSerializer.SerializeToNode(listed, Exam.JsonOptions)!;
        Assert.Equal("retire/1", (string)json["schema"]!);
        Assert.Equal("renamed away", (string)json["retired"]!["gone"]!["reason"]!);
        Assert.False((bool)json["retired"]!["gone"]!["named"]!);
    }

    [Fact]
    public void RetiredAgainTheReasonIsReplacedAndTheDateKept()
    {
        using var repo = Repo();
        repo.Write($"{Id}/retired.json", """{ "schema": "retired/1", "configurations": { "rewritten": { "reason": "first", "since": "2026-01-02" } } }""");

        Verbs.Retire(repo.Root, Id, "rewritten", "second", false);

        var mark = Retired.Load(repo.LoadExam(Id))["rewritten"];
        Assert.Equal(("second", "2026-01-02"), (mark.Reason, mark.Since));
    }

    [Fact]
    public void WhatCannotBeRetiredIsRefusedWithWhatToDo()
    {
        using var repo = Repo();
        Assert.Contains("is live", Assert.Throws<AuspexException>(() => Verbs.Retire(repo.Root, Id, "shipped", "no", false)).Message);
        Assert.Contains("--reason", Assert.Throws<AuspexException>(() => Verbs.Retire(repo.Root, Id, "rewritten", null, false)).Message);
        Assert.Contains("names no configuration 'nope'", Assert.Throws<AuspexException>(() => Verbs.Retire(repo.Root, Id, "nope", "no", false)).Message);
        Assert.False(System.IO.File.Exists(File(repo)));
    }

    [Fact]
    public void WithEveryNamedConfigurationRetiredRunSaysSo()
    {
        using var repo = Repo();
        var exam = repo.LoadExam(Id);
        repo.Write($"{Id}/retired.json", "{\"schema\": \"retired/1\", \"configurations\": {" + string.Join(",", exam.Axes!.Named.Select(c => $"\"{c.Name}\": {{ \"reason\": \"x\", \"since\": \"2026-01-02\" }}")) + "}}");

        Assert.Contains("is retired, so there is nothing to run", Assert.Throws<AuspexException>(() => Asked(repo)).Message);
    }
}
