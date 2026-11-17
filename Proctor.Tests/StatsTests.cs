using Proctor;

namespace Proctor.Tests;

/// <summary>Step 6: Wilson against the published table, Newcombe against a worked example, MDE, summaries, task-mean reduction.</summary>
public class StatsTests
{
    // Percent intervals from project/learnings/prior-art/stats.md, Wilson column.
    [Theory]
    [InlineData(5, 5, 57, 100)]
    [InlineData(4, 5, 38, 96)]
    [InlineData(3, 5, 23, 88)]
    [InlineData(0, 5, 0, 43)]
    [InlineData(20, 20, 84, 100)]
    [InlineData(18, 20, 70, 97)]
    [InlineData(12, 20, 39, 78)]
    [InlineData(50, 50, 93, 100)]
    [InlineData(45, 50, 79, 96)]
    [InlineData(31, 50, 48, 74)]
    public void Wilson_MatchesPublishedValues(int k, int n, int lo, int hi)
    {
        var ci = Stats.Wilson((double)k / n, n);
        Assert.Equal(lo, (int)Math.Round(100 * ci.Lo));
        Assert.Equal(hi, (int)Math.Round(100 * ci.Hi));
    }

    [Fact]
    public void Wilson_NeverDegenerate_AndInsideZeroOne()
    {
        var full = Stats.Wilson(1.0, 3);
        Assert.True(full.Lo < 1.0 && full.Hi == 1.0);
        var none = Stats.Wilson(0.0, 3);
        Assert.True(none.Lo == 0.0 && none.Hi > 0.0);
    }

    [Fact]
    public void Newcombe_Paired_WorkedExample()
    {
        // Newcombe 1998 (Stat Med 17:2635) worked example: n=50; both 22, first only 9, second only 2, neither 17.
        // p1=31/50, p2=24/50, difference 0.14; method 10 gives about (0.01, 0.26). Checked to two decimals.
        var a = new double[50];
        var b = new double[50];
        for (var i = 0; i < 22; i++) { a[i] = 1; b[i] = 1; }
        for (var i = 22; i < 31; i++) a[i] = 1;
        for (var i = 31; i < 33; i++) b[i] = 1;
        var (diff, ci) = Stats.NewcombePaired(a, b);
        Assert.Equal(0.14, diff, 3);
        Assert.Equal(0.011, ci.Lo, 2);
        Assert.Equal(0.261, ci.Hi, 2);
    }

    [Fact]
    public void Newcombe_AllDiscordantOneWay_StillGivesAnInterval()
    {
        var a = new double[] { 1, 1, 1 };
        var b = new double[] { 0, 0, 0 };
        var (diff, ci) = Stats.NewcombePaired(a, b);
        Assert.Equal(1.0, diff);
        Assert.True(ci.Lo > 0 && ci.Lo < 1);
        Assert.Equal(1.0, ci.Hi);
    }

    [Fact]
    public void Newcombe_IdenticalVariants_StraddlesZero()
    {
        var a = new double[] { 1, 0, 1, 0 };
        var (diff, ci) = Stats.NewcombePaired(a, a);
        Assert.Equal(0, diff);
        Assert.True(ci.Lo < 0 && ci.Hi > 0);
    }

    [Fact]
    public void Newcombe_FractionalTaskScores_ReduceBeforeTheInterval()
    {
        // Three tasks, three attempts each: task means, not nine attempts.
        var a = new double[] { 2 / 3.0, 1, 1 / 3.0 };
        var b = new double[] { 1 / 3.0, 1, 0 };
        var (diff, ci) = Stats.NewcombePaired(a, b);
        Assert.Equal(0.222, diff, 3);
        Assert.True(ci.Lo < 0 && ci.Hi > diff);
    }

    [Theory]
    [InlineData(3, 81)]
    [InlineData(5, 63)]
    [InlineData(10, 44)]
    [InlineData(20, 31)]
    [InlineData(50, 20)]
    public void Mde_MatchesTheLearningsWorkedValues(int n, int points)
    {
        var mde = Stats.Mde(n);
        Assert.Equal(points, mde.Points);
        Assert.Equal(200, mde.TasksFor10Points);
        Assert.Contains($"With {n} paired tasks", mde.Sentence);
        Assert.Contains($"about {points} points", mde.Sentence);
        Assert.Contains("about 200 tasks", mde.Sentence);
    }

    [Fact]
    public void Mde_ZeroPairs_SaysSo() => Assert.Contains("No paired tasks", Stats.Mde(0).Sentence);

    [Fact]
    public void Summary_MedianP90MeanRange()
    {
        var s = Stats.Summarise([812, 1402, 1118, 900, 1000, 1300, 1150, 1210, 1050])!;
        Assert.Equal(9, s.N);
        Assert.Equal(1118, s.Median);
        Assert.Equal(1402, s.P90);
        Assert.Equal(812, s.Min);
        Assert.Equal(1402, s.Max);
        Assert.Equal(1105, s.Mean);
        Assert.Null(Stats.Summarise([]));
        Assert.Equal(2.5, Stats.Summarise([1, 2, 3, 4])!.Median);
    }

    static ResultRow Row(string variant, string task, int n, bool? pass, string status = "completed", string exit = "ok", string? invalid = null) =>
        WorkedExam.Row(variant, task, n, pass, status, exit, reason: "hook failed", invalid: invalid);

    static Exam TwoVariantExam(int attempts) => WorkedExam.Exam(attempts);

    [Fact]
    public void Compute_TheWorkedExamShape()
    {
        var exam = TwoVariantExam(attempts: 3);   // tasks: loops, plain, uses-bash
        var rows = new List<ResultRow>();
        var floor = new Dictionary<string, bool[]> { ["loops"] = [true, true, false], ["plain"] = [true, true, true], ["uses-bash"] = [false, true, false] };
        var b = new Dictionary<string, bool[]> { ["loops"] = [true, true, true], ["plain"] = [true, true, true], ["uses-bash"] = [true, false, true] };
        foreach (var (c, v) in floor) for (var s = 0; s < 3; s++) rows.Add(Row("floor", c, s + 1, v[s], exit: v[s] ? "ok" : "max_tool_calls"));
        foreach (var (c, v) in b) for (var s = 0; s < 3; s++) rows.Add(Row("b", c, s + 1, v[s]));

        var stats = Stats.Compute(exam, rows, WorkedExam.Live(exam));

        Assert.Equal(3, stats.NTasks);
        Assert.True(stats.Paired);
        var f = stats.Variants["floor"];
        Assert.Equal((9, 9, 9, 9, 9), (f.Planned, f.Started, f.Completed, f.Graded, f.Analysed));
        Assert.Empty(f.Excluded);
        Assert.Equal(0.667, f.Pass!.Rate);
        Assert.Equal(3, f.Pass.N);
        Assert.Equal((6, 9), (f.Pass.KAttempts, f.Pass.NAttempts));
        Assert.True(f.Pass.Ci95.Lo > 0 && f.Pass.Ci95.Hi < 1);
        Assert.Equal(new Dictionary<string, int> { ["ok"] = 6, ["max_tool_calls"] = 3 }, f.ExitReasons);
        Assert.Equal(0.667, f.Checks["builds"].Rate);
        Assert.Equal(0.667, f.Checks["exit_ok"].Rate);
        Assert.Equal(1.0, stats.Variants["b"].Checks["exit_ok"].Rate);
        Assert.Equal(0, f.Checks["builds"].Errors);
        Assert.Equal(9, f.DurationMs!.N);
        Assert.Equal(9, f.TokensTotal!.N);
        Assert.False(f.TokensEstimated);

        var cmp = Assert.Single(stats.Comparisons);
        Assert.Equal(("b", "floor", 3), (cmp.Variant, cmp.Vs, cmp.NPairs));
        Assert.Equal(22, cmp.DiffPoints);
        Assert.Equal((2, 0, 1), (cmp.Won, cmp.Lost, cmp.Tied));
        Assert.Equal("no-detectable-difference", cmp.Verdict);
        Assert.True(cmp.Ci95[0] < 0 && cmp.Ci95[1] > 22);

        Assert.Equal(3, stats.Mde.NPairs);
        Assert.Equal(81, stats.Mde.Points);
        Assert.Equal(["pass", "pass", "fail"], stats.Matrix["floor"]["loops"]);
        Assert.Equal(["loops", "plain", "uses-bash"], stats.Tasks);
        Assert.Contains("Wilson", stats.Methods);
        Assert.Contains("1 comparison shown", stats.Methods);
    }

    [Fact]
    public void Compute_ExcludedAttemptsStayInAccounting_AndOutOfRates()
    {
        var exam = TwoVariantExam(attempts: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, true), Row("floor", "uses-bash", 1, null, status: "failed"),
            Row("b", "loops", 1, false), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var stats = Stats.Compute(exam, rows, WorkedExam.Live(exam));
        var f = stats.Variants["floor"];
        Assert.Equal((3, 3, 2, 2, 2), (f.Planned, f.Started, f.Completed, f.Graded, f.Analysed));
        var ex = Assert.Single(f.Excluded);
        Assert.Equal(("floor/uses-bash/1", "failed", "hook failed"), (ex.Attempt, ex.Status, ex.Reason));
        Assert.Equal(1.0, f.Pass!.Rate);
        Assert.Equal(2, f.Pass.N);
        Assert.Equal(["failed"], stats.Matrix["floor"]["uses-bash"]);

        // The comparison pairs only the tasks both variants analysed.
        var cmp = Assert.Single(stats.Comparisons);
        Assert.Equal(2, cmp.NPairs);
        Assert.Equal(-50, cmp.DiffPoints);
        Assert.Equal((0, 1, 1), (cmp.Won, cmp.Lost, cmp.Tied));
        Assert.Equal(2, stats.Mde.NPairs);
    }

    [Fact]
    public void Compute_InvalidAttemptsAreExcluded_ValidityRateIsOverEveryGradedAttempt()
    {
        var exam = TwoVariantExam(attempts: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, true), Row("floor", "uses-bash", 1, true, invalid: "no_denials: 1 denied call"),
            Row("b", "loops", 1, false), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var stats = Stats.Compute(exam, rows, WorkedExam.Live(exam));
        var f = stats.Variants["floor"];
        Assert.Equal((3, 3, 3, 3, 2), (f.Planned, f.Started, f.Completed, f.Graded, f.Analysed));
        var ex = Assert.Single(f.Excluded);
        Assert.Equal(("floor/uses-bash/1", "invalid", "no_denials: 1 denied call"), (ex.Attempt, ex.Status, ex.Reason));
        Assert.Equal(2, f.Pass!.N);
        Assert.Equal(["invalid"], stats.Matrix["floor"]["uses-bash"]);
        Assert.Equal(["no_denials"], stats.ValidityChecks);
        // The validity check is rated over all three graded attempts; the pass checks over the two that count.
        Assert.Equal((2, 3), (f.Checks["no_denials"].KAttempts, f.Checks["no_denials"].NAttempts));
        Assert.Equal((2, 2), (f.Checks["builds"].KAttempts, f.Checks["builds"].NAttempts));
        Assert.Equal(2, Assert.Single(stats.Comparisons).NPairs);
    }

    [Fact]
    public void Compute_OneVariantWithAttempts_NoComparisons_MdeFromItsTasks()
    {
        var exam = TwoVariantExam(attempts: 1);
        var rows = new List<ResultRow> { Row("floor", "loops", 1, true), Row("floor", "plain", 1, false), Row("floor", "uses-bash", 1, true) };
        var stats = Stats.Compute(exam, rows);
        Assert.Empty(stats.Comparisons);
        Assert.Equal(3, stats.Mde.NPairs);
        // A variant with no attempts among the rows is not in the statistics at all; `report` says so.
        Assert.Equal(["floor"], stats.Variants.Keys);
        Assert.False(stats.Matrix.ContainsKey("b"));
    }

    /// <summary>The report's stamp: the log entries of its runs, today's hashes, current and stale counts, and a variant whose attempts came from runs on different versions.</summary>
    [Fact]
    public void Stamp_SaysWhatTheReportWasBuiltFrom_AndWhichVariantsWereMadeUnderMixedConditions()
    {
        var exam = TwoVariantExam(attempts: 1);
        var first = WorkedExam.Latest(exam) with { Id = "20260916-0910-smoke-q2rd" };
        var second = first with { Id = "20260917-1432-smoke-k7px", Versions = new() { ["proctor"] = "0.1.0", ["nb"] = "1.0.1" } };
        BankAttempt A(LogEntry b, string variant, string task) => new(b, variant, task, 1, "completed", exam.Variants.Single(v => v.Id == variant).Hash, exam.TaskHashes[task]);
        var selection = new BankSelection(
            [A(first, "floor", "loops"), A(second, "floor", "plain"), A(first, "b", "loops"), A(first, "b", "plain")],
            [new StaleAttempt("b/uses-bash/1", "task uses-bash changed (1a2b3c4d then, 5e6f7a8b now)")]);

        var built = Stats.Stamp(exam, selection, "--tasks loops,plain", new DateTime(2026, 9, 17, 15, 2, 0, DateTimeKind.Utc));

        Assert.Equal("2026-09-17T15:02:00Z", built.At);
        Assert.Equal([first.Id, second.Id], built.Log);
        Assert.Equal(["floor", "b"], built.Variants.Keys);
        Assert.Equal(exam.TaskHashes["plain"], built.Tasks["plain"]);
        Assert.False(built.Tasks.ContainsKey("uses-bash"));
        Assert.Equal((4, 1), (built.Current, built.Stale));
        Assert.Equal("--tasks loops,plain", built.Narrowed);
        Assert.Equal(["nb 1.0.0, 1.0.1"], Assert.Single(built.Mixed!, m => m.Key == "floor").Value);
        Assert.False(built.Mixed!.ContainsKey("b"));
    }

    /// <summary>The live variant is the reference wherever it sits in the exam's order; with nothing live, nothing is compared.</summary>
    [Fact]
    public void Compute_TheLiveVariantIsTheReference_AndNothingLiveComparesNothing()
    {
        var exam = TwoVariantExam(attempts: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, false), Row("floor", "uses-bash", 1, false),
            Row("b", "loops", 1, true), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var stats = Stats.Compute(exam, rows, WorkedExam.Live(exam, "b"));
        Assert.Equal(new LiveStats("b", "2026-09-10T09:12:00Z"), stats.Live);
        var cmp = Assert.Single(stats.Comparisons);
        Assert.Equal(("floor", "b", -67), (cmp.Variant, cmp.Vs, cmp.DiffPoints));
        Assert.Contains("Differences from the live variant, b, use Newcombe's paired method", stats.Methods);
        Assert.Contains("1 comparison shown", stats.Methods);

        var none = Stats.Compute(exam, rows);
        Assert.Null(none.Live);
        Assert.Empty(none.Comparisons);
        Assert.Equal(3, none.Mde.NPairs);
        Assert.DoesNotContain("Newcombe", none.Methods);
    }
}
