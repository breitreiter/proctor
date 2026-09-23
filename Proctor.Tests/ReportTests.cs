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

    [Fact]
    public void Html_ReferencesNoExternalUrl_AndIsOneFile()
    {
        var (stats, rows, exp) = Worked();
        var html = Report.Html(stats, rows, exp);
        Assert.DoesNotMatch(new Regex(@"(https?:)?//[a-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("@import", html);
        Assert.DoesNotContain("url(", html);
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
