using Proctor;

namespace Proctor.Tests;

/// <summary>Step 6: Wilson against the published table, Newcombe against a worked example, MDE, summaries, case-mean reduction.</summary>
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
    public void Newcombe_IdenticalArms_StraddlesZero()
    {
        var a = new double[] { 1, 0, 1, 0 };
        var (diff, ci) = Stats.NewcombePaired(a, a);
        Assert.Equal(0, diff);
        Assert.True(ci.Lo < 0 && ci.Hi > 0);
    }

    [Fact]
    public void Newcombe_FractionalCaseScores_ReduceBeforeTheInterval()
    {
        // Three cases, three samples each: case means, not nine cells.
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
        Assert.Equal(200, mde.CasesFor10Points);
        Assert.Contains($"With {n} paired cases", mde.Sentence);
        Assert.Contains($"about {points} points", mde.Sentence);
        Assert.Contains("about 200 cases", mde.Sentence);
    }

    [Fact]
    public void Mde_ZeroPairs_SaysSo() => Assert.Contains("No paired cases", Stats.Mde(0).Sentence);

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

    static ResultRow Row(string arm, string @case, int sample, bool? pass, string status = "completed", string exit = "ok", string? invalid = null) =>
        WorkedExperiment.Row(arm, @case, sample, pass, status, exit, reason: "hook failed", invalid: invalid);

    static Eval TwoArmEval(int samples) => WorkedExperiment.Eval(samples);

    static Experiment Exp(Eval eval) => WorkedExperiment.Experiment(eval);

    [Fact]
    public void Compute_TheWorkedExperimentShape()
    {
        var eval = TwoArmEval(samples: 3);   // cases: loops, plain, uses-bash
        var rows = new List<ResultRow>();
        var floor = new Dictionary<string, bool[]> { ["loops"] = [true, true, false], ["plain"] = [true, true, true], ["uses-bash"] = [false, true, false] };
        var b = new Dictionary<string, bool[]> { ["loops"] = [true, true, true], ["plain"] = [true, true, true], ["uses-bash"] = [true, false, true] };
        foreach (var (c, v) in floor) for (var s = 0; s < 3; s++) rows.Add(Row("floor", c, s + 1, v[s], exit: v[s] ? "ok" : "max_tool_calls"));
        foreach (var (c, v) in b) for (var s = 0; s < 3; s++) rows.Add(Row("b", c, s + 1, v[s]));

        var stats = Stats.Compute(Exp(eval), eval, rows);

        Assert.Equal(3, stats.NCases);
        Assert.True(stats.Paired);
        var f = stats.Arms["floor"];
        Assert.Equal((9, 9, 9, 9, 9), (f.Planned, f.Attempted, f.Completed, f.Graded, f.Analysed));
        Assert.Empty(f.Excluded);
        Assert.Equal(0.667, f.Pass!.Rate);
        Assert.Equal(3, f.Pass.N);
        Assert.Equal((6, 9), (f.Pass.KCells, f.Pass.NCells));
        Assert.True(f.Pass.Ci95.Lo > 0 && f.Pass.Ci95.Hi < 1);
        Assert.Equal(new Dictionary<string, int> { ["ok"] = 6, ["max_tool_calls"] = 3 }, f.ExitReasons);
        Assert.Equal(0.667, f.Checks["builds"].Rate);
        Assert.Equal(0.667, f.Checks["exit_ok"].Rate);
        Assert.Equal(1.0, stats.Arms["b"].Checks["exit_ok"].Rate);
        Assert.Equal(0, f.Checks["builds"].Errors);
        Assert.Equal(9, f.DurationMs!.N);
        Assert.Equal(9, f.TokensTotal!.N);
        Assert.False(f.TokensEstimated);

        var cmp = Assert.Single(stats.Comparisons);
        Assert.Equal(("b", "floor", 3), (cmp.Arm, cmp.Vs, cmp.NPairs));
        Assert.Equal(22, cmp.DiffPoints);
        Assert.Equal((2, 0, 1), (cmp.Won, cmp.Lost, cmp.Tied));
        Assert.Equal("no-detectable-difference", cmp.Verdict);
        Assert.True(cmp.Ci95[0] < 0 && cmp.Ci95[1] > 22);

        Assert.Equal(3, stats.Mde.NPairs);
        Assert.Equal(81, stats.Mde.Points);
        Assert.Equal(["pass", "pass", "fail"], stats.Matrix["floor"]["loops"]);
        Assert.Equal(["loops", "plain", "uses-bash"], stats.Cases);
        Assert.Contains("Wilson", stats.Methods);
        Assert.Contains("1 comparison shown", stats.Methods);
    }

    [Fact]
    public void Compute_ExcludedCellsStayInAccounting_AndOutOfRates()
    {
        var eval = TwoArmEval(samples: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, true), Row("floor", "uses-bash", 1, null, status: "failed"),
            Row("b", "loops", 1, false), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var stats = Stats.Compute(Exp(eval), eval, rows);
        var f = stats.Arms["floor"];
        Assert.Equal((3, 3, 2, 2, 2), (f.Planned, f.Attempted, f.Completed, f.Graded, f.Analysed));
        var ex = Assert.Single(f.Excluded);
        Assert.Equal(("floor/uses-bash/1", "failed", "hook failed"), (ex.Cell, ex.Status, ex.Reason));
        Assert.Equal(1.0, f.Pass!.Rate);
        Assert.Equal(2, f.Pass.N);
        Assert.Equal(["failed"], stats.Matrix["floor"]["uses-bash"]);

        // The comparison pairs only the cases both arms analysed.
        var cmp = Assert.Single(stats.Comparisons);
        Assert.Equal(2, cmp.NPairs);
        Assert.Equal(-50, cmp.DiffPoints);
        Assert.Equal((0, 1, 1), (cmp.Won, cmp.Lost, cmp.Tied));
        Assert.Equal(2, stats.Mde.NPairs);
    }

    [Fact]
    public void Compute_InvalidSamplesAreExcluded_ValidityRateIsOverEveryGradedSample()
    {
        var eval = TwoArmEval(samples: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, true), Row("floor", "uses-bash", 1, true, invalid: "no_denials: 1 denied call"),
            Row("b", "loops", 1, false), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var stats = Stats.Compute(Exp(eval), eval, rows);
        var f = stats.Arms["floor"];
        Assert.Equal((3, 3, 3, 3, 2), (f.Planned, f.Attempted, f.Completed, f.Graded, f.Analysed));
        var ex = Assert.Single(f.Excluded);
        Assert.Equal(("floor/uses-bash/1", "invalid", "no_denials: 1 denied call"), (ex.Cell, ex.Status, ex.Reason));
        Assert.Equal(2, f.Pass!.N);
        Assert.Equal(["invalid"], stats.Matrix["floor"]["uses-bash"]);
        Assert.Equal(["no_denials"], stats.ValidityChecks);
        // The validity check is rated over all three graded samples; the pass checks over the two that count.
        Assert.Equal((2, 3), (f.Checks["no_denials"].KCells, f.Checks["no_denials"].NCells));
        Assert.Equal((2, 2), (f.Checks["builds"].KCells, f.Checks["builds"].NCells));
        Assert.Equal(2, Assert.Single(stats.Comparisons).NPairs);
    }

    [Fact]
    public void Compute_OneArm_NoComparisons_MdeFromItsCases()
    {
        var eval = TwoArmEval(samples: 1);
        var rows = new List<ResultRow> { Row("floor", "loops", 1, true), Row("floor", "plain", 1, false), Row("floor", "uses-bash", 1, true) };
        var stats = Stats.Compute(Exp(eval), eval, rows);
        Assert.Empty(stats.Comparisons);
        Assert.Equal(3, stats.Mde.NPairs);
        Assert.Null(stats.Arms["b"].Pass);
        Assert.Equal(0, stats.Arms["b"].Attempted);
        Assert.Empty(stats.Matrix["b"]["loops"]);
    }

    [Fact]
    public void Compute_AgainstBaseline_VerdictIsThePointEstimateAgainstTheTolerance()
    {
        var eval = TwoArmEval(samples: 1);
        var rows = new List<ResultRow>
        {
            Row("floor", "loops", 1, true), Row("floor", "plain", 1, false), Row("floor", "uses-bash", 1, false),
            Row("b", "loops", 1, true), Row("b", "plain", 1, true), Row("b", "uses-bash", 1, true),
        };
        var guard = new GuardInput("2026-09-21T16:40:12Z", "recomputed", new() { ["loops"] = 1.0, ["plain"] = 1.0, ["uses-bash"] = 0.0 }, TolerancePoints: 10);
        var stats = Stats.Compute(Exp(eval), eval, rows, guard);
        var bl = stats.Baseline!;
        Assert.Equal(("2026-09-21T16:40:12Z", "recomputed", 10), (bl.Set, bl.Scores, bl.TolerancePoints));

        var floor = bl.Arms["floor"];      // 1/3 against 2/3: −33 points, beyond the tolerance
        Assert.Equal((3, -33, "regressed"), (floor.NPairs, floor.DiffPoints, floor.Verdict));
        Assert.Equal((0, 1, 2), (floor.Won, floor.Lost, floor.Tied));
        Assert.Equal(new BaselineCase(1.0, 0.0), floor.Cases["plain"]);
        var b = bl.Arms["b"];              // 3/3 against 2/3: +33 points
        Assert.Equal((33, "improved"), (b.DiffPoints, b.Verdict));
        Assert.True(b.Ci95[0] < 0 && b.Ci95[1] > 0, "three cases cannot make the interval exclude zero");

        // Inside the tolerance is held, whichever way it leans; the between-arm comparison is untouched.
        var held = Stats.Compute(Exp(eval), eval, rows, guard with { TolerancePoints = 40 }).Baseline!;
        Assert.All(held.Arms.Values, a => Assert.Equal("held", a.Verdict));
        Assert.Equal(67, Assert.Single(stats.Comparisons).DiffPoints);
        Assert.Contains("tolerance of 10 points", stats.Methods);
    }
}
