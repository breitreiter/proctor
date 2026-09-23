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
