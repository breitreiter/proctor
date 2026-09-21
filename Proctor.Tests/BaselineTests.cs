using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Steps 4 and 5 of fixtures-arms-baselines.md: the baseline verb pins cells; the report compares against them and can fail on a regression.</summary>
public class BaselineTests
{
    [Fact]
    public void Baseline_PinsAnalysedCellsPerCase_ReportComparesAgainstThem_AndFailsOnRegressionWhenAsked()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.EditJson("smoke/eval.json", e => { e["arms"]!.AsArray().RemoveAt(1); e["arms"]![0]!["samples"] = 1; });
        var problems = new List<Problem>();
        var config = Eval.LoadConfig(repo.Root, problems);
        var eval = repo.LoadEval("smoke");
        var id = Runner.Start(repo.Root, eval, config, null, null, "test", TextWriter.Null);
        Grade.Experiment(repo.Root, Runner.LoadExperiment(repo.Root, id), eval, TextWriter.Null);

        // No baseline yet: the report has no baseline block and --fail-on has nothing to fail on.
        Assert.Equal(0, Verbs.Report(repo.Root, id, failOn: "regression"));
        var statsFile = Path.Combine(Layout.ReportData(repo.Root, id), "stats.json");
        Assert.Null(JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]);

        Assert.Equal(0, Verbs.Baseline(repo.Root, id, null, null));
        var file = Path.Combine(repo.Root, "evals/smoke/baseline.json");
        var baseline = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(eval.Hash, baseline["eval_hash"]!.GetValue<string>());
        var loops = baseline["cases"]!["loops"]!;
        Assert.Equal(id, loops["experiment"]!.GetValue<string>());
        Assert.Equal("a", loops["arm"]!.GetValue<string>());
        Assert.Equal([1], loops["samples"]!.AsArray().Select(s => s!.GetValue<int>()));
        Assert.Equal(0.0, loops["score"]!.GetValue<double>());          // loops exhausts its budget: exit_ok fails
        Assert.Equal(1.0, baseline["cases"]!["plain"]!["score"]!.GetValue<double>());
        Assert.StartsWith("sha256:", loops["fixture"]!.GetValue<string>());

        // The same experiment against its own baseline: no difference, held, scores recomputed from the cells.
        Assert.Equal(0, Verbs.Report(repo.Root, id, failOn: "regression"));
        var bl = JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]!;
        Assert.Equal("recomputed", bl["scores"]!.GetValue<string>());
        Assert.Equal(0, bl["arms"]!["a"]!["diff_points"]!.GetValue<int>());
        Assert.Equal("held", bl["arms"]!["a"]!["verdict"]!.GetValue<string>());
        Assert.Contains("Against baseline", File.ReadAllText(Path.Combine(Layout.ReportData(repo.Root, id), "summary.md")));

        // A baseline whose cells are gone stands as pinned; a higher pinned score is a regression at tolerance 0 and held at 40.
        repo.EditJson("smoke/baseline.json", b =>
        {
            foreach (var c in b["cases"]!.AsObject()) { c.Value!["experiment"] = "gone"; c.Value!["score"] = 1.0; }
        });
        Assert.Equal(1, Verbs.Report(repo.Root, id, tolerancePoints: 0, failOn: "regression"));
        bl = JsonNode.Parse(File.ReadAllText(statsFile))!["baseline"]!;
        Assert.Equal("as pinned", bl["scores"]!.GetValue<string>());
        Assert.Equal(-33, bl["arms"]!["a"]!["diff_points"]!.GetValue<int>());
        Assert.Equal("regressed", bl["arms"]!["a"]!["verdict"]!.GetValue<string>());
        Assert.Equal(0, Verbs.Report(repo.Root, id, tolerancePoints: 40, failOn: "regression"));

        // Re-baselining one case keeps the other pins.
        Assert.Equal(0, Verbs.Baseline(repo.Root, id, "a", ["plain"]));
        baseline = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(id, baseline["cases"]!["plain"]!["experiment"]!.GetValue<string>());
        Assert.Equal("gone", baseline["cases"]!["loops"]!["experiment"]!.GetValue<string>());
    }
}
