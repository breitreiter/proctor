using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// Step 7: both renderings are snapshotted for the worked experiment. A changed rendering fails
/// until it is reviewed and approved with PROCTOR_APPROVE=1, the record-review-approve loop from
/// the test-framework learnings. The .received file beside the snapshot shows the diff.
/// </summary>
public class ReportTests
{
    static string SnapshotDir => Path.Combine(TestRepo.SourceRoot, "Proctor.Tests", "snapshots");

    static (StatsFile Stats, List<ResultRow> Rows, Experiment Exp) Worked()
    {
        var suite = WorkedExperiment.Suite();
        var rows = WorkedExperiment.Rows();
        return (Stats.Compute(WorkedExperiment.Experiment(suite), suite, rows, WorkedExperiment.Guard()), rows, WorkedExperiment.Experiment(suite));
    }

    static void AssertSnapshot(string name, string actual)
    {
        Directory.CreateDirectory(SnapshotDir);
        var verified = Path.Combine(SnapshotDir, name);
        var received = Path.Combine(SnapshotDir, name + ".received");
        if (Environment.GetEnvironmentVariable("PROCTOR_APPROVE") == "1")
        {
            File.WriteAllText(verified, actual);
            File.Delete(received);
            return;
        }
        if (File.Exists(verified) && File.ReadAllText(verified) == actual)
        {
            File.Delete(received);
            return;
        }
        File.WriteAllText(received, actual);
        Assert.Fail($"{name} differs from the approved snapshot; review {received} and approve with PROCTOR_APPROVE=1 dotnet test");
    }

    [Fact]
    public void Html_MatchesApprovedSnapshot()
    {
        var (stats, rows, exp) = Worked();
        AssertSnapshot("report.html", Report.Html(stats, rows, exp));
    }

    [Fact]
    public void Markdown_MatchesApprovedSnapshot()
    {
        var (stats, rows, exp) = Worked();
        AssertSnapshot("summary.md", Report.Markdown(stats, rows, exp));
    }

    /// <summary>Arms with different sample counts that lost nothing are not "unequal": the warning compares the share each arm decided.</summary>
    [Fact]
    public void Markdown_DifferentSampleCounts_LosingNothing_WarnsNothing()
    {
        var suite = WorkedExperiment.Suite();
        var rows = WorkedExperiment.Rows().Where(r => r.Arm == "b" || r.Sample == 1).Select(r => r with { Pass = true, Checks = r.Checks is null ? null : r.Checks.ToDictionary(k => k.Key, _ => "pass"), Reasons = null, Invalid = null, Status = "completed", StatusReason = null }).ToList();
        var md = Report.Markdown(Stats.Compute(WorkedExperiment.Experiment(suite), suite, rows), rows, WorkedExperiment.Experiment(suite));
        Assert.DoesNotContain("lost runs unequally", md);
    }

    /// <summary>A check that errored is the check's weakness: out of its rate, not called a failure, and listed apart under Failures.</summary>
    [Fact]
    public void AnErroredCheck_IsOutOfItsRate_AndListedApartFromFailures()
    {
        var suite = WorkedExperiment.Suite();
        var rows = WorkedExperiment.Rows().Select(r => r is { Arm: "b", Task: "uses-bash", Sample: 1 }
            ? r with { Checks = new(r.Checks!) { ["used-bash"] = Verdict.Error }, Reasons = new() { ["used-bash"] = "judge: line one\nline two" } }
            : r).ToList();
        var stats = Stats.Compute(WorkedExperiment.Experiment(suite), suite, rows);
        var b = stats.Arms["b"];
        Assert.Equal((2, 1), (b.Checks["used-bash"].NCells, b.Checks["used-bash"].Errors));
        Assert.DoesNotContain(b.Failures, f => f.Check == "used-bash");
        Assert.Equal(("used-bash", 1, 3), Assert.Single(b.Errored!) is var e ? (e.Check, e.Cells, e.Of) : default);
        var md = Report.Markdown(stats, rows, WorkedExperiment.Experiment(suite));
        Assert.Contains("could not grade 1 of 3 runs", md);
        // A reason over two lines stays in its table row.
        Assert.Contains("judge: line one<br>line two", md);
    }

    /// <summary>
    /// The card's foot gives the run's reach once; an arm paired on fewer tasks says its own on its row, and with a
    /// baseline each row carries the arm against the reference rather than the foot repeating it.
    /// </summary>
    [Fact]
    public void AnArmPairedOnFewerTasks_SaysItsOwnReach_OnItsRow()
    {
        var suite = WorkedExperiment.Suite();
        var rows = WorkedExperiment.Rows().Where(r => !(r.Arm == "b" && r.Task == "plain")).ToList();
        var stats = Stats.Compute(WorkedExperiment.Experiment(suite), suite, rows, WorkedExperiment.Guard());
        Assert.Equal(3, stats.Mde.NPairs);
        Assert.Equal(2, stats.Baseline!.Arms["b"].NPairs);
        Assert.NotNull(stats.Baseline.Arms["b"].MdePoints);
        Assert.Null(stats.Baseline.Arms["floor"].MdePoints);
        var md = Report.Markdown(stats, rows, WorkedExperiment.Experiment(suite));
        var bRow = md.Split('\n').Single(l => l.StartsWith("- **b**"));
        Assert.Contains($"Paired on 2 of 3 tasks, so it can only confirm a change of about {stats.Baseline.Arms["b"].MdePoints} points.", bRow);
        Assert.Contains("Against **floor**:", bRow);
        Assert.DoesNotContain("For the same reason", md);
    }

    /// <summary>An agent's reported cost reaches the Cost and effort table; an arm without one shows none.</summary>
    [Fact]
    public void Markdown_ShowsCost_WhenAnArmReportsIt()
    {
        var suite = WorkedExperiment.Suite();
        var rows = WorkedExperiment.Rows().Select(r => r.Arm == "b" && r.Usage is not null ? r with { Usage = r.Usage with { CostUsd = 0.05 } } : r).ToList();
        var stats = Stats.Compute(WorkedExperiment.Experiment(suite), suite, rows);
        Assert.Equal((0.05, 0.45), (stats.Arms["b"].CostUsd!.Median, stats.Arms["b"].CostTotalUsd!.Value));
        Assert.Null(stats.Arms["floor"].CostUsd);
        var md = Report.Markdown(stats, rows, WorkedExperiment.Experiment(suite));
        Assert.Contains("| Cost: median | total |", md);
        Assert.Contains("| $0.05 | $0.45 |", md);
    }

    /// <summary>The page loads nothing: styles, script and logo are inline. Links out (the icon's credit) are only followed by a reader.</summary>
    [Fact]
    public void Html_LoadsNothingFromOutside_AndIsOneFile()
    {
        var (stats, rows, exp) = Worked();
        var html = Report.Html(stats, rows, exp);
        Assert.DoesNotMatch(new Regex(@"<(script|img|link|iframe|source)\b[^>]*\b(src|href)=""(?!data:|#)", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("@import", html);
        Assert.DoesNotMatch(new Regex(@"url\((?!#)", RegexOptions.IgnoreCase), html);
        Assert.Contains("Lorc", html);   // CC BY 3.0: the credit ships with the icon
    }

    [Fact]
    public void BothRenderings_CarryTheSameNumbers()
    {
        var (stats, rows, exp) = Worked();
        var html = Report.Html(stats, rows, exp);
        var md = Report.Markdown(stats, rows, exp);
        foreach (var arm in stats.Arms.Values)
        {
            var interval = $"{Math.Round(100 * arm.Pass!.Ci95.Lo):F0}% to {Math.Round(100 * arm.Pass.Ci95.Hi):F0}%";
            var runs = $"{arm.Pass.KCells} / {arm.Pass.NCells}";
            Assert.Contains(interval, html);
            Assert.Contains(interval, md);
            Assert.Contains(runs, html);
            Assert.Contains(runs, md);
        }
        Assert.Contains(stats.Mde.Sentence, html);
        Assert.Contains(stats.Mde.Sentence, md);
        Assert.Contains("Arms lost runs unequally", html);
        Assert.Contains("Arms lost runs unequally", md);
        foreach (var row in rows) Assert.Contains(row.RunId, html);
    }

    /// <summary>A headline every task declares in its own words: each task card carries the task's sentence, the check pass rates point there, and a failure names the task's criterion.</summary>
    [Fact]
    public void ACheckDescribedPerTask_ShowsEachTasksSentence()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e =>
        {
            e["grading"]!["checks"] = JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"no_denials\": {\"denied_calls\": {\"max\": 0}}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"builds\"]");
            e["grading"]!["validity"] = JsonNode.Parse("[\"no_denials\"]");
        });
        foreach (var (task, criterion) in new[] { ("loops", "the loop ends within four turns"), ("plain", "the answer gives `#fbedef`"), ("uses-bash", "the answer gives 120") })
            repo.EditJson($"smoke/tasks/{task}.json", t => t["checks"] = JsonNode.Parse($"{{\"builds\": {{\"script\": \"checks/answer-nonempty.sh\", \"description\": \"{criterion}\"}}}}"));
        var suite = repo.LoadSuite("smoke");
        var rows = WorkedExperiment.Rows();
        var exp = WorkedExperiment.Experiment(suite);
        var stats = Stats.Compute(exp, suite, rows);

        var builds = stats.TaskDetails!["plain"].Checks.Single(k => k.Name == "builds");
        Assert.Equal(("task", "the answer gives `#fbedef`"), (builds.Level, builds.Description));
        Assert.False(stats.Descriptions.Checks.ContainsKey("builds"));

        var md = Report.Markdown(stats, rows, exp);
        var plain = md.Split("### plain")[1].Split("### uses-bash")[0];
        Assert.Contains("**3 checks**: 2 from the suite, 1 of its own", plain);
        Assert.Contains("| **builds** | the answer gives `#fbedef` | headline | this task |", plain);
        Assert.Contains("| `builds` | per task; see Tasks | headline |", md);
        Assert.Contains("**`builds`** (headline) failed in", md);
        Assert.Contains("on uses-bash (the answer gives 120)", md);
        Assert.DoesNotContain("the answer gives `#fbedef`", md.Split("## Check pass rates")[1]);
    }

    [Fact]
    public void HtmlEscapesWhatItPrints()
    {
        var (stats, rows, exp) = Worked();
        rows[0] = rows[0] with { StatusReason = "<b>bold</b> & \"quoted\"", Status = "failed", Pass = null, Checks = null };
        var html = Report.Html(Stats.Compute(exp, WorkedExperiment.Suite(), rows), rows, exp);
        Assert.DoesNotContain("<b>bold</b>", html);
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt; &amp; &quot;quoted&quot;", html);
    }
}
