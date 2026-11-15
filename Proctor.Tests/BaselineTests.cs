using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Steps 4 and 5 of project/archive/fixtures-arms-baselines.md: the baseline verb pins attempts; the report compares against them and can fail on a regression.</summary>
public class BaselineTests
{
    [Fact]
    public void Baseline_PinsAnalysedAttemptsPerCase_ReportComparesAgainstThem_AndFailsOnRegressionWhenAsked()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.OneVariantOneAttempt();
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(repo.Root, problems);
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        Grade.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam).Current, TextWriter.Null);

        // No baseline yet: the report has no baseline block and --fail-on has nothing to fail on.
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke", failOn: "regression"));
        var statsFile = Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "stats.json");
        Assert.Null(JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]);

        Assert.Equal(0, Verbs.Baseline(repo.Root, id, null, null));
        var file = Path.Combine(repo.Root, "exams/smoke/baseline.json");
        var baseline = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(exam.Hash, baseline["suite_hash"]!.GetValue<string>());
        var loops = baseline["tasks"]!["loops"]!;
        Assert.Equal(id, loops["experiment"]!.GetValue<string>());
        Assert.Equal("a", loops["arm"]!.GetValue<string>());
        Assert.Equal([1], loops["samples"]!.AsArray().Select(s => s!.GetValue<int>()));
        Assert.Equal(0.0, loops["score"]!.GetValue<double>());          // loops exhausts its budget: exit_ok fails
        Assert.Equal(1.0, baseline["tasks"]!["plain"]!["score"]!.GetValue<double>());
        Assert.StartsWith("sha256:", loops["fixture"]!.GetValue<string>());

        // The same batch against its own baseline: no difference, held, scores recomputed from the attempts.
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke", failOn: "regression"));
        var bl = JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]!;
        Assert.Equal("recomputed", bl["scores"]!.GetValue<string>());
        Assert.Equal(0, bl["variants"]!["a"]!["diff_points"]!.GetValue<int>());
        Assert.Equal("held", bl["variants"]!["a"]!["verdict"]!.GetValue<string>());
        Assert.Contains("Against the baseline", File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "summary.md")));

        // A baseline whose attempts are gone stands as pinned; a higher pinned score is a regression at tolerance 0 and held at 40.
        repo.EditJson("smoke/baseline.json", b =>
        {
            foreach (var c in b["tasks"]!.AsObject()) { c.Value!["experiment"] = "gone"; c.Value!["score"] = 1.0; }
        });
        Assert.Equal(1, Verbs.Report(repo.Root, "smoke", tolerancePoints: 0, failOn: "regression"));
        bl = JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]!;
        Assert.Equal("as pinned", bl["scores"]!.GetValue<string>());
        Assert.Equal(-33, bl["variants"]!["a"]!["diff_points"]!.GetValue<int>());
        Assert.Equal("regressed", bl["variants"]!["a"]!["verdict"]!.GetValue<string>());
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke", tolerancePoints: 40, failOn: "regression"));

        // Re-baselining one task keeps the other pins.
        Assert.Equal(0, Verbs.Baseline(repo.Root, id, ["a"], ["plain"]));
        baseline = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(id, baseline["tasks"]!["plain"]!["experiment"]!.GetValue<string>());
        Assert.Equal("gone", baseline["tasks"]!["loops"]!["experiment"]!.GetValue<string>());
    }

    [Fact]
    public void Baseline_PinnedAttemptsMissingAPassCheck_StandAsPinned_AndTheReportNamesTheCheck()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.OneVariantOneAttempt();
        var config = Exam.LoadConfig(repo.Root, []);
        var exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        Grade.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam).Current, TextWriter.Null);
        Assert.Equal(0, Verbs.Baseline(repo.Root, id, null, null));

        // The pass list grows after pinning: the pinned attempts were never graded on the new check. Rescoring them
        // would read it as an error and every task as 0; instead each task stands as pinned and the gap is named.
        repo.EditJson("smoke/exam.json", e =>
        {
            e["grading"]!["checks"]!["answer-short"] = JsonNode.Parse("""{ "max_tokens": 100000 }""");
            e["grading"]!["pass"]!.AsArray().Add("answer-short");
        });
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        var bl = JsonNode.Parse(File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "stats.json")))!["baseline"]!;
        Assert.Equal("as pinned", bl["scores"]!.GetValue<string>());
        var gap = Assert.Single(bl["gaps"]!.AsArray())!;
        Assert.Equal("answer-short", gap["check"]!.GetValue<string>());
        Assert.Equal(id, gap["experiment"]!.GetValue<string>());
        Assert.Equal(exam.Tasks.Count, gap["tasks"]!.AsArray().Count);
        Assert.Equal(1.0, bl["variants"]!["a"]!["tasks"]!["plain"]!["baseline"]!.GetValue<double>());
        Assert.Contains("never graded on the headline check `answer-short`", File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "summary.md")));
    }

    [Fact]
    public void ReportAgainst_PairsEachVariantWithItsOwnEarlierRun_AndNamesAVariantTheOtherLacks()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 1);
        var config = Exam.LoadConfig(repo.Root, []);
        var exam = repo.LoadExam("smoke");
        var earlier = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        Grade.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam).Current, TextWriter.Null);

        // Variant b did worse on plain last time; variant a did not. A single pin could not show both.
        var attempt = Layout.Attempt(Layout.Batch(repo.Root, earlier), "b", "plain", 1);
        var checks = JsonNode.Parse(File.ReadAllText(Path.Combine(attempt, "checks.json")))!;
        checks["says-expected"]!["result"] = "fail";
        File.WriteAllText(Path.Combine(attempt, "checks.json"), checks.ToJsonString());

        // The new batch adds a variant the earlier one never had, and a second attempt of every task, so a and b have attempts of their own in it.
        repo.AddVariant("smoke", """{ "id": "c", "runner": "nb", "harness": "nb", "provider": "Mock", "model": "mock" }""");
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 2);
        exam = repo.LoadExam("smoke");
        var id = Runner.Run(repo.Root, exam, config, null, null, "test", TextWriter.Null)!;
        Assert.NotEqual(earlier, id);
        Grade.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam, id).Current, TextWriter.Null);

        Assert.Equal(0, Verbs.Report(repo.Root, "smoke", againstId: earlier, failOn: "regression", batchId: id));
        var bl = JsonNode.Parse(File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "stats.json")))!["baseline"]!;
        Assert.Equal(earlier, bl["against"]!.GetValue<string>());
        Assert.Equal("recomputed", bl["scores"]!.GetValue<string>());
        Assert.Equal("held", bl["variants"]!["a"]!["verdict"]!.GetValue<string>());
        Assert.Equal("improved", bl["variants"]!["b"]!["verdict"]!.GetValue<string>());
        Assert.Equal(100, bl["variants"]!["b"]!["tasks"]!["plain"]!["diff_points"]!.GetValue<int>());
        Assert.Null(bl["variants"]!["c"]);
        Assert.Equal(["c"], bl["unpaired"]!.AsArray().Select(x => x!.GetValue<string>()));
        var summary = File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "summary.md"));
        Assert.Contains($"Against {earlier}, variant by variant", summary);
        Assert.Contains($"not in {earlier}", summary);

        Assert.Throws<ProctorException>(() => Verbs.Report(repo.Root, "smoke", againstId: "no-such-batch"));
    }
}
