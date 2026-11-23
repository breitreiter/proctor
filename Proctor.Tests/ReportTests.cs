using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// Step 7: both renderings are snapshotted for the worked exam. A changed rendering fails
/// until it is reviewed and approved with PROCTOR_APPROVE=1, the record-review-approve loop from
/// the test-framework learnings. The .received file beside the snapshot shows the diff.
/// </summary>
public class ReportTests
{
    static string SnapshotDir => Path.Combine(TestRepo.SourceRoot, "Proctor.Tests", "snapshots");

    static (StatsFile Stats, List<ResultRow> Rows, LogEntry Latest) Worked()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows();
        return (Stats.Compute(exam, rows, WorkedExam.Live(exam), WorkedExam.Built()), rows, WorkedExam.Latest(exam));
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
    public void Markdown_MatchesApprovedSnapshot()
    {
        var (stats, rows, exp) = Worked();
        AssertSnapshot("summary.md", Report.Markdown(stats, rows, exp));
    }

    /// <summary>Variants with different attempt counts that lost nothing are not "unequal": the warning compares the share each variant decided.</summary>
    [Fact]
    public void Markdown_DifferentAttemptCounts_LosingNothing_WarnsNothing()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows().Where(r => r.Variant == "b" || r.Attempt == 1).Select(r => r with { Pass = true, Checks = r.Checks is null ? null : r.Checks.ToDictionary(k => k.Key, _ => "pass"), Reasons = null, Invalid = null, Status = "completed", StatusReason = null }).ToList();
        var md = Report.Markdown(Stats.Compute(exam, rows), rows, WorkedExam.Latest(exam));
        Assert.DoesNotContain("lost attempts unequally", md);
    }

    /// <summary>A check that errored is the check's weakness: out of its rate, not called a failure, and listed apart under Failures.</summary>
    [Fact]
    public void AnErroredCheck_IsOutOfItsRate_AndListedApartFromFailures()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows().Select(r => r is { Variant: "b", Task: "uses-bash", Attempt: 1 }
            ? r with { Checks = new(r.Checks!) { ["used-bash"] = Verdict.Error }, Reasons = new() { ["used-bash"] = "judge: line one\nline two" } }
            : r).ToList();
        var stats = Stats.Compute(exam, rows);
        var b = stats.Variants["b"];
        Assert.Equal((2, 1), (b.Checks["used-bash"].NAttempts, b.Checks["used-bash"].Errors));
        Assert.DoesNotContain(b.Failures, f => f.Check == "used-bash");
        Assert.Equal(("used-bash", 1, 3), Assert.Single(b.Errored!) is var e ? (e.Check, e.Attempts, e.Of) : default);
        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        Assert.Contains("could not grade 1 of 3 attempts", md);
        // A reason over two lines stays in its table row.
        Assert.Contains("judge: line one<br>line two", md);
    }

    /// <summary>The card's foot gives the report's reach once; a variant paired with the live one on fewer tasks says its own on its row.</summary>
    [Fact]
    public void AVariantPairedOnFewerTasks_SaysItsOwnReach_OnItsRow()
    {
        var exam = WorkedExam.Exam(third: true);
        var rows = WorkedExam.Rows();
        rows.AddRange(rows.Where(r => r.Variant == "b" && r.Task != "plain").Select(r => r with { Variant = "c", Id = "c" + r.Id[1..] }).ToList());
        var stats = Stats.Compute(exam, rows, WorkedExam.Live(exam));
        Assert.Equal(3, stats.Mde.NPairs);
        var c = stats.Comparisons.Single(x => x.Variant == "c");
        Assert.Equal((2, "floor"), (c.NPairs, c.Vs));
        Assert.NotNull(c.MdePoints);
        Assert.Null(stats.Comparisons.Single(x => x.Variant == "b").MdePoints);
        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        var cRow = md.Split('\n').Single(l => l.StartsWith("**c**:"));
        Assert.Contains($"Paired on 2 of 3 tasks, so it can only confirm a change of about {c.MdePoints} points.", cRow);
    }

    /// <summary>With nothing live the variants stand side by side: no reference, no change, no verdict, and the page says none is live.</summary>
    [Fact]
    public void NothingLive_TheVariantsSideBySide_NoneComparedWithAnother()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows();
        var stats = Stats.Compute(exam, rows, null, WorkedExam.Built());
        Assert.Null(stats.Live);
        Assert.Empty(stats.Comparisons);
        Assert.Equal(3, stats.Mde.NPairs);
        Assert.Contains("No variant is live, so none is compared with another.", stats.Methods);
        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        Assert.Contains("**Two variants, side by side**\n\n| Variant | Passed | 95% interval |\n", md);
        Assert.Contains("None is live yet.", md);
        Assert.Contains("Picking the best of several flatters it: part of any lead is luck.", md);
        Assert.Contains(stats.Mde.Sentence, md);
        Assert.Contains("None is live, so the variants stand side by side and none is compared with another.", md);
        Assert.Contains("| Variants | 2: `floor`, `b` |", md);
        foreach (var gone in new[] { "Against the live variant", "Difference from", "Change against", "(live)", "reference", "unconfirmed" })
            Assert.DoesNotContain(gone, md);
    }

    /// <summary>A live mark that does not hold, or names a variant with no attempt here, is said on the page, and nothing is compared with it.</summary>
    [Fact]
    public void ALiveMarkThatIsNotTheReference_IsAWarning_AndNothingIsCompared()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows();
        var moved = Stats.Compute(exam, rows, new Live(new Ref("floor", "sha256:0000000011111111"), "2026-09-10T09:12:00Z"));
        Assert.Empty(moved.Comparisons);
        Assert.StartsWith("its file has changed since it was marked (00000000 then, ", moved.Live!.NotReference);
        var md = Report.Markdown(moved, rows, WorkedExam.Latest(exam));
        Assert.Contains("> **Warning.** `floor` is marked live, but its file has changed since it was marked", md);
        Assert.DoesNotContain("None is live yet.", md);

        var floorOnly = rows.Where(r => r.Variant == "floor").ToList();
        var absent = Stats.Compute(exam, floorOnly, WorkedExam.Live(exam, "b"));
        Assert.Equal("it has no current attempt in this report", absent.Live!.NotReference);
        Assert.Contains("`b` is marked live, but it has no current attempt in this report, so no variant is compared with it.", Report.Markdown(absent, floorOnly, WorkedExam.Latest(exam)));
    }

    /// <summary>A check that could not grade many runs is one warning with its count per variant, and leaves the per-variant failure lists.</summary>
    [Fact]
    public void ACheckThatErroredOften_IsOneWarning_NotALineUnderEachVariant()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows().Select(r => r.Task == "uses-bash" && r.Checks is not null
            ? r with { Checks = new Dictionary<string, string>(r.Checks) { ["used-bash"] = Verdict.Error } } : r).ToList();
        var stats = Stats.Compute(exam, rows);
        var x = Assert.Single(stats.ErroredChecks!);
        Assert.Equal("used-bash", x.Check);
        Assert.Equal(x.Attempts, x.Variants.Values.Sum());
        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        Assert.Contains($"> **Warning.** The check `used-bash` (uses bash) could not grade {x.Attempts} of the {x.Of} attempts it applies to, on every variant", md);
        Assert.DoesNotContain("could not grade some of", md);
        Assert.Contains("`used-bash` could not grade many attempts, and is left out of the lists above", md);
    }

    /// <summary>
    /// An outdated verdict (a script check on agent attempts, changed since they were made) is out of every rate, never a
    /// failure, and said once at the top with the variants and how to make the attempts again; each attempt shows it by name.
    /// </summary>
    [Fact]
    public void AnOutdatedVerdict_IsOutOfEveryRate_AndAWarningSaysWhatToMakeAgain()
    {
        var exam = WorkedExam.Exam();
        const string why = "changed since the attempt was made: fixtures/ledger/checks/builds.sh, so the verdict on file may not be this check's as it is now; make the attempt again";
        var before = Stats.Compute(exam, WorkedExam.Rows()).Variants["b"];
        var rows = WorkedExam.Rows().Select(r => r is { Variant: "b", Task: "plain" }
            ? r with { Checks = new(r.Checks!) { ["builds"] = Verdict.Outdated }, Pass = null, Reasons = new() { ["builds"] = why }, Undecided = $"builds: {why}", Outdated = $"builds: {why}" }
            : r).ToList();
        var outdated = rows.Count(r => r.Outdated is not null);
        Assert.True(outdated > 0);

        var stats = Stats.Compute(exam, rows);
        var b = stats.Variants["b"];
        Assert.Equal((before.Checks["builds"].NAttempts - outdated, outdated), (b.Checks["builds"].NAttempts, b.Checks["builds"].Outdated));
        Assert.Equal(before.Pass!.NAttempts - outdated, b.Pass!.NAttempts);
        Assert.Equal(before.Decided - outdated, b.Decided);
        Assert.Equal(before.Analysed, b.Analysed);
        Assert.DoesNotContain(b.Failures, f => f.Check == "builds" && f.Tasks.ContainsKey("plain"));
        Assert.Null(b.Errored);
        Assert.Empty(stats.UndecidedChecks);
        Assert.All(stats.Matrix["b"]["plain"], m => Assert.Equal("outdated", m));
        var o = Assert.Single(stats.OutdatedChecks!);
        Assert.Equal(("builds", outdated), (o.Check, o.Attempts));
        Assert.Equal(new Dictionary<string, int> { ["b"] = outdated }, o.Variants);
        Assert.Null(Stats.Compute(exam, WorkedExam.Rows()).OutdatedChecks);

        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        Assert.Contains($"> **Warning.** The check `builds` (the repository builds after the change) has changed or been added since {outdated} of the {o.Of} attempts it applies to were made, on `b` (`b` {outdated}). "
            + "On an agent variant a script check runs when the attempt is made, so their verdicts are outdated: left out of its rates and, as a headline check, out of the pass rates. "
            + "Make those attempts again: `proctor grade smoke` names them; move their directories out of `bank/smoke/` and run `proctor run smoke`.", md);
        Assert.Contains($"· {outdated} outdated", md);
        Assert.Contains($"{outdated} attempts are undecided because a headline check's verdict is outdated; see the warning at the top.", md);
        Assert.DoesNotContain("undecided, and can be reviewed", md);
        Assert.Contains("| `b` | `plain` | 1 | ? outdated |", md);
        Assert.Contains($"builds: {why.Replace("fixtures/ledger/checks/builds.sh", "`fixtures/ledger/checks/builds.sh`")}", md);
    }

    /// <summary>An agent's reported cost reaches the Cost and effort table; a variant without one shows none.</summary>
    [Fact]
    public void Markdown_ShowsCost_WhenAVariantReportsIt()
    {
        var exam = WorkedExam.Exam();
        var rows = WorkedExam.Rows().Select(r => r.Variant == "b" && r.Usage is not null ? r with { Usage = r.Usage with { CostUsd = 0.05 } } : r).ToList();
        var stats = Stats.Compute(exam, rows);
        Assert.Equal((0.05, 0.45), (stats.Variants["b"].CostUsd!.Median, stats.Variants["b"].CostTotalUsd!.Value));
        Assert.Null(stats.Variants["floor"].CostUsd);
        var md = Report.Markdown(stats, rows, WorkedExam.Latest(exam));
        Assert.Contains("| Cost: median | total |", md);
        Assert.Contains("| $0.05 | $0.45 |", md);
    }

    [Fact]
    public void Markdown_CarriesTheNumbers()
    {
        var (stats, rows, exp) = Worked();
        var md = Report.Markdown(stats, rows, exp);
        foreach (var variant in stats.Variants.Values)
        {
            Assert.Contains($"{Math.Round(100 * variant.Pass!.Ci95.Lo):F0}% to {Math.Round(100 * variant.Pass.Ci95.Hi):F0}%", md);
            Assert.Contains($"{variant.Pass.KAttempts} / {variant.Pass.NAttempts}", md);
        }
        Assert.Contains(stats.Mde.Sentence, md);
        Assert.Contains("Variants lost attempts unequally", md);
    }

    /// <summary>A headline every task declares in its own words: each task card carries the task's sentence, the check pass rates point there, and a failure names the task's criterion.</summary>
    [Fact]
    public void ACheckDescribedPerTask_ShowsEachTasksSentence()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e =>
        {
            e["grading"]!["checks"] = JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"no_denials\": {\"denied_calls\": {\"max\": 0}}}");
            e["grading"]!["pass"] = JsonNode.Parse("[\"exit_ok\", \"builds\"]");
            e["grading"]!["validity"] = JsonNode.Parse("[\"no_denials\"]");
        });
        foreach (var (task, criterion) in new[] { ("loops", "the loop ends within four turns"), ("plain", "the answer gives `#fbedef`"), ("uses-bash", "the answer gives 120") })
            repo.EditJson($"smoke/tasks/{task}.json", t => t["checks"] = JsonNode.Parse($"{{\"builds\": {{\"script\": \"checks/answer-nonempty.sh\", \"description\": \"{criterion}\"}}}}"));
        var exam = repo.LoadExam("smoke");
        var rows = WorkedExam.Rows();
        var exp = WorkedExam.Latest(exam);
        var stats = Stats.Compute(exam, rows);

        var builds = stats.TaskDetails!["plain"].Checks.Single(k => k.Name == "builds");
        Assert.Equal(("task", "the answer gives `#fbedef`"), (builds.Level, builds.Description));
        Assert.False(stats.Descriptions.Checks.ContainsKey("builds"));

        var md = Report.Markdown(stats, rows, exp);
        var plain = md.Split("### plain")[1].Split("### uses-bash")[0];
        Assert.Contains("**3 checks**: 2 from the exam, 1 of its own", plain);
        Assert.Contains("| builds | the answer gives `#fbedef` | headline | this task |", plain);
        Assert.Contains("| `builds` | per task; see Tasks | headline |", md);
        Assert.Contains("**`builds`** (headline) failed in", md);
        Assert.Contains("on uses-bash (the answer gives 120)", md);
        Assert.DoesNotContain("the answer gives `#fbedef`", md.Split("## Check pass rates")[1]);
    }
}
