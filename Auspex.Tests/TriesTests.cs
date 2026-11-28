using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>What the report counts: tries as the unit, a value against the live one by Mann–Whitney, and what recurs in friction.</summary>
public class TriesTests
{
    [Fact]
    public void MannWhitney_IsExact_AndSaysWhichCountsOfTriesCanConfirm()
    {
        // Every try of one above every try of the other: the smallest p the sizes allow, 2 over the ways to choose.
        Assert.Equal(2 / 70.0, Tries.MannWhitneyP([5, 6, 7, 8], [1, 2, 3, 4]), 6);
        Assert.Equal(2 / 70.0, Tries.MannWhitneyP([1, 2, 3, 4], [5, 6, 7, 8]), 6);
        Assert.Equal(2 / 20.0, Tries.MannWhitneyP([4, 5, 6], [1, 2, 3]), 6);
        Assert.Equal(1, Tries.MannWhitneyP([3, 3, 3, 3], [3, 3, 3, 3]));
        // Ties take the mean of their ranks, and one overlap is no longer the extreme.
        Assert.True(Tries.MannWhitneyP([4, 6, 7, 8], [1, 2, 3, 5]) > 0.05);
        Assert.Equal(1, Tries.MannWhitneyP([], [1, 2]));

        // 4 a side can confirm, or 3 against 5; 3 against 3 and 2 against 5 never can.
        Assert.True(Tries.MinP(4, 4) < 0.05);
        Assert.True(Tries.MinP(3, 5) < 0.05);
        Assert.True(Tries.MinP(3, 3) >= 0.05);
        Assert.True(Tries.MinP(2, 5) >= 0.05);
    }

    const string A = "assistant=a", B = "assistant=b";

    /// <summary>The smoke exam as two stages and one axis whose two values are the same Mock under two names, a live: four tries each, run, graded and read for friction.</summary>
    static Exam Made(TestRepo repo, int attempts = 4)
    {
        repo.CopyExam("smoke");
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nloop 2\nbudget tool_calls 3\napproval bash *\nrun {{prompt}}\n");
        repo.EditJson("smoke/exam.json", e =>
        {
            e.Remove("hooks");
            e["attempts"] = attempts;
            e["stages"] = new JsonArray(
                JsonNode.Parse("""{"id": "ask-help", "about": "Ask a tool for its help", "prompt": "MOCK:tool=bash git --help"}"""),
                JsonNode.Parse("""{"id": "say-hello", "about": "Answer in one turn", "prompt": "MOCK:response=hello world", "expect": {"answer_contains": "hello"}}"""));
            e["axes"] = JsonNode.Parse("""
                { "assistant": { "about": "who does the work", "live": "a", "values": {
                    "a": { "note": "nb on Mock", "provider": "Mock", "model": "mock" },
                    "b": { "note": "the same as a, under another name", "provider": "Mock", "model": "mock", "harness": "nb" } } } }
                """);
            e["configurations"] = JsonNode.Parse("""{ "a": { "assistant": "a" }, "b": { "assistant": "b" } }""");
        });
        repo.Run("smoke");
        var exam = repo.LoadExam("smoke");
        var current = Bank.Select(repo.Root, exam).Current;
        Grade.Attempts(repo.Root, exam, current, TextWriter.Null);
        Friction.Attempts(repo.Root, exam, current, TextWriter.Null);
        return exam;
    }

    static (ConfigurationsFile Stats, FrictionRollup Friction) Report(TestRepo repo, Exam exam)
    {
        var selection = Bank.Select(repo.Root, exam);
        return Configurations.Compute(repo.Root, exam, selection.Current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, selection.Current), null);
    }

    /// <summary>A configuration's tries in the bank, oldest first: each try's directory.</summary>
    static List<string> TriesOf(TestRepo repo, Exam exam, string key) =>
        Bank.Of(repo.Root, exam).Where(a => a.Configuration == key).OrderBy(a => a.Attempt).Select(a => Path.GetDirectoryName(Path.GetDirectoryName(a.Dir(repo.Root)))!).Distinct().ToList();

    static void EditChecks(string tryDir, string stage, Action<JsonObject> edit)
    {
        var file = Path.Combine(tryDir, "stages", stage, "checks.json");
        var checks = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        edit(checks);
        File.WriteAllText(file, checks.ToJsonString());
        // The grade kept under the standard carries the same verdicts; the report reads checks.json.
    }

    [Fact]
    public void AValueIsComparedWithLive_TryAgainstTry_AndEachStageAndCheckIsTallied()
    {
        using var repo = new TestRepo();
        var exam = Made(repo);

        // As run, the two values are the same: every try holds every correctness check, with the same friction.
        var (same, friction) = Report(repo, exam);
        Assert.Equal(("configurations/1", 4, true), (same.Schema, same.Configurations[0].CorrectnessChecks, same.Configurations[0].Live));
        Assert.Equal(["exit_ok", "says-expected"], same.Stages[0].CorrectnessChecks);
        Assert.All(same.Configurations, c => Assert.Equal((4, 4, 0, new Spread(4, 4, 4)), (c.Tries!.Tries, c.Tries.Passed, c.Tries.Lost, c.Tries.Held)));
        var cell = Assert.Single(Assert.Single(same.Comparisons).Cells);
        Assert.Equal(("compared", "same", 16), (cell.State, cell.Held!.Direction, cell.Held.Pairings));

        // What recurs: asking for help, in every try of both, one flagged step each time, on the stage that asked.
        var kind = Assert.Single(friction.Configurations[0].Kinds);
        Assert.Equal(("ran-help", 4, 4, new Spread(1, 1, 1), 1.0), (kind.Label, kind.In, kind.Of, kind.When, kind.PerTry));
        Assert.Equal(["ask-help"], kind.Stages);
        Assert.Equal(new Spread(1, 1, 1), same.Configurations[0].Tries!.Flagged);

        // Now b's second stage answers wrong in every try: fewer correctness checks held, in all 16 pairings.
        foreach (var dir in TriesOf(repo, exam, B))
            EditChecks(dir, "2-say-hello", c => c["says-expected"]!["result"] = "fail");
        var (worse, _) = Report(repo, exam);
        var b = worse.Configurations[1].Tries!;
        Assert.Equal((0, new Spread(3, 3, 3)), (b.Passed, b.Held));
        var held = worse.Comparisons[0].Cells[0].Held!;
        Assert.Equal(("worse", 0, 16, true), (held.Direction, held.Higher, held.Lower, held.Confirmed));
        Assert.Equal(["right", "wrong"], b.Rows[0].Stages.Select(s => s.Result));
        // A stage across the tries: right in none, and each try held one of its two correctness checks, so the share is a half however many checks a stage has.
        var stage = b.Stages[1];
        Assert.Equal(("say-hello", 0, 4, 2, 4, new Spread(0.5, 0.5, 0.5), 0.5), (stage.Id, stage.Right, stage.Of, stage.Checks, stage.Graded, stage.Share, stage.Average));
        Assert.Equal(("says-expected", 4), (Assert.Single(stage.DidNotHold!).Check, stage.DidNotHold![0].Tries));
        Assert.Null(b.Stages[0].DidNotHold);
        Assert.Equal((1, 2), (b.Rows[0].Stages[1].Held, b.Rows[0].Stages[1].Of));
        // Each check's rate is over the times it was asked, whatever the stage.
        var said = b.Checks!.Single(k => k.Check == "says-expected");
        Assert.Equal((Roles.Correctness, said.Asked - 4, said.Asked), (said.Role, said.Held, said.Asked));
    }

    [Fact]
    public void TooFewTries_CannotConfirm_AndALostTryIsInNoNumber()
    {
        using var repo = new TestRepo();
        var exam = Made(repo, attempts: 2);
        var tries = TriesOf(repo, exam, B);
        EditChecks(tries[0], "2-say-hello", c => c["says-expected"]!["result"] = "fail");
        // One of b's tries turns out to have been lost: its second stage did not run.
        var lostStage = Path.Combine(tries[1], "stages", "2-say-hello");
        File.WriteAllText(Path.Combine(lostStage, "status"), "failed\n");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(lostStage, "manifest.json")))!.AsObject();
        manifest["status"] = "failed"; manifest["status_reason"] = "did not run: provider_error";
        File.WriteAllText(Path.Combine(lostStage, "manifest.json"), manifest.ToJsonString());

        var (stats, friction) = Report(repo, exam);
        var b = stats.Configurations[1];
        Assert.Equal((1, 1, "partial"), (b.Made, b.Lost, b.State));
        var lost = Assert.Single(stats.Lost);
        Assert.Equal((B, "say-hello", "did not run: provider_error"), (lost.Configuration, lost.At, lost.Why));
        var cell = stats.Comparisons[0].Cells[0];
        Assert.Equal((1, 2), (cell.Tries, cell.LiveTries));
        Assert.False(cell.Held!.Confirmed);

        var md = Configurations.Markdown(stats, friction);
        Assert.Contains("| assistant=b | 1 of 2 |", md);
        Assert.Contains("did not run: provider_error", md);
    }

    [Fact]
    public void TheReportVerb_WritesTheNumbersAndTheFrictionRollUp()
    {
        using var repo = new TestRepo();
        Made(repo, attempts: 1);
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        var dir = Path.Combine(repo.Root, "reports", "smoke", "explore");
        Assert.Equal(["friction.json", "results.jsonl", "stats.json", "summary.md"], Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var stats = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "stats.json")))!;
        Assert.Equal("configurations/1", stats["schema"]!.GetValue<string>());
        Assert.Equal(2, stats["configurations"]![0]!["tries"]!["rows"]![0]!["stages"]!.AsArray().Count);
        Assert.Equal("friction-rollup/1", JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "friction.json")))!["schema"]!.GetValue<string>());
        Assert.Equal(4, File.ReadAllLines(Path.Combine(dir, "results.jsonl")).Length);
    }
}
