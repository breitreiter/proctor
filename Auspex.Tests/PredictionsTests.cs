using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>
/// Predictions and predictions (plans/axes-build.md, slice 6), on the scripted exam with known answers: a prediction names
/// one full configuration, its predictions say when they were written, are pinned before its tries or said to be post hoc, and each is held against the
/// tries, a miss as plainly as a hit.
/// </summary>
public class PredictionsTests
{
    static TestRepo Repo(string prediction, string body, int attempts = 4)
    {
        var repo = new TestRepo();
        repo.CopyExam("axes-answers");
        repo.EditJson("axes-answers/exam.json", e => e["attempts"] = attempts);
        repo.Write($"axes-answers/predictions/{prediction}.json", body);
        return repo;
    }

    static void Make(TestRepo repo, params string[] names)
    {
        foreach (var name in names)
        {
            var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("axes-answers"), null, null, name);
            Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null, keys);
        }
        var graded = repo.LoadExam("axes-answers");
        Grade.Attempts(repo.Root, graded, Bank.Select(repo.Root, graded).Current, TextWriter.Null);
    }

    static (List<Prediction> Predictions, List<Problem> Problems) Load(TestRepo repo)
    {
        var problems = new List<Problem>();
        return (Predictions.Load(repo.Root, repo.LoadExam("axes-answers"), problems), problems);
    }

    static List<PredictionReport> Outcomes(TestRepo repo)
    {
        var exam = repo.LoadExam("axes-answers");
        var current = Bank.Select(repo.Root, exam).Current;
        return Configurations.Compute(repo.Root, exam, current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, current), null, Load(repo).Predictions).Stats.Predictions ?? [];
    }

    [Fact]
    public void APrediction_PinnedBeforeItsTries_IsHeldAgainstThem_AMissAsPlainlyAsAHit()
    {
        // The scripted exam makes the rewritten guide better on the first assistant: one prediction is right, and one is wrong on purpose.
        using var repo = Repo("rewritten", """
            { "configuration": "rewritten", "note": "the guide rewritten", "written": "2020-01-01",
              "changes": [ { "metric": "held", "direction": "more", "because": "it says to read the note first" },
                               { "check": "cites-note", "direction": "less" },
                               { "metric": "tool_calls", "direction": "more" } ] }
            """);
        var prediction = Assert.Single(Load(repo).Predictions);
        Assert.Equal(("rewritten", "shipped"), (prediction.Configuration.Name, prediction.Against.Name));
        Assert.Null(prediction.Def.Standard);

        // Pinned once, with the standard of each stage it will be graded under; pinning again changes nothing.
        Assert.True(Predictions.Stamp(repo.LoadExam("axes-answers"), prediction));
        prediction = Load(repo).Predictions[0];
        Assert.Equal("2020-01-01", prediction.Def.Written);
        Assert.Equal(["answer"], prediction.Def.Standard!.Keys);
        Assert.False(Predictions.Stamp(repo.LoadExam("axes-answers"), prediction));
        Assert.Contains("\"note\": \"the guide rewritten\"", File.ReadAllText(prediction.File));

        // Before any try: not run, never a miss.
        Assert.All(Assert.Single(Outcomes(repo)).Changes, p => Assert.Equal("not_run", p.Outcome));

        Make(repo, "shipped", "rewritten");
        var report = Assert.Single(Outcomes(repo));
        Assert.False(report.PostHoc);
        Assert.Empty(report.Labels);
        var (held, cites, calls) = (report.Changes[0], report.Changes[1], report.Changes[2]);
        Assert.Contains(held.Outcome, new[] { "supported", "inconclusive" });
        Assert.StartsWith("Predicted more correctness checks held than shipped: it went that way", held.Sentence);
        Assert.Equal("it says to read the note first", held.Because);
        // Predicted the wrong way round: said so, in the same words a hit gets.
        Assert.Contains(cites.Outcome, new[] { "contradicted", "inconclusive" });
        Assert.StartsWith("Predicted cites-note to hold in fewer stages than shipped: it went the other way", cites.Sentence);
        // Every scripted try makes one tool call: a prediction of more did not happen.
        Assert.Equal("no_effect", calls.Outcome);
        Assert.Contains("it did not happen", calls.Sentence);

        var exam = repo.LoadExam("axes-answers");
        var stats = Configurations.Compute(repo.Root, exam, Bank.Select(repo.Root, exam).Current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, Bank.Select(repo.Root, exam).Current), null, Load(repo).Predictions).Stats;
        Assert.Contains("## Predictions", Configurations.Markdown(stats, new FrictionRollup("friction-rollup/1", exam.Id, [], null)));
        // A comparison that reads the prediction's configuration carries its predictions.
        Assert.Equal("rewritten", Assert.Single(Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, new Dictionary<string, string> { ["assistant"] = "mock" }, null).Predictions!).Name);
    }

    [Fact]
    public void APredictionWrittenAfterItsTries_IsPostHoc_AndOneWhoseChecksChanged_SaysTheStandardMoved()
    {
        using var repo = Repo("rewritten", """{ "configuration": "rewritten", "written": "2020-01-01", "changes": [ { "metric": "held", "direction": "more" } ] }""", attempts: 1);
        Make(repo, "shipped", "rewritten");
        // Written before any try was made: nothing to say.
        Assert.Empty(Assert.Single(Outcomes(repo)).Labels);

        // Written at a time before which a try had been made: post hoc, and said.
        repo.EditJson("axes-answers/predictions/rewritten.json", c => { c["written"] = "2099-01-01T00:00Z"; c["standard"] = new JsonObject { ["answer"] = Standard.Hash(repo.LoadExam("axes-answers"), repo.LoadExam("axes-answers").Stages[0]) }; });
        var late = Assert.Single(Outcomes(repo));
        Assert.True(late.PostHoc);
        Assert.StartsWith("Post hoc: 1 try of rewritten had been made before this prediction was written", Assert.Single(late.Labels));

        // The checks change after the pin: the prediction is held against a standard it was not written under.
        repo.EditJson("axes-answers/exam.json", e => e["grading"]!["checks"]!["cites-note"]!["when"] = "assistant != mock-b");
        var graded = repo.LoadExam("axes-answers");
        Grade.Attempts(repo.Root, graded, Bank.Select(repo.Root, graded).Current, TextWriter.Null);
        var moved = Assert.Single(Outcomes(repo));
        Assert.True(moved.StandardChanged);
        Assert.Contains(moved.Labels, l => l.StartsWith("The standard changed after this prediction was pinned"));
    }

    public static TheoryData<string, string, string> Refusals => new()
    {
        { """{ "changes": [ { "metric": "held", "direction": "more" } ] }""", "configuration", "required" },
        // One full configuration by name: never a value with the other axes however they fall.
        { """{ "configuration": "prompt=rewritten", "written": "2026-11-24", "changes": [ { "metric": "held", "direction": "more" } ] }""", "configuration", "is not a configuration this exam names" },
        { """{ "configuration": "shipped", "changes": [ { "metric": "held", "direction": "more" } ] }""", "configuration", "nothing live to compare it with" },
        { """{ "configuration": "rewritten" }""", "changes", "at least one is required" },
        { """{ "configuration": "rewritten", "changes": [ { "metric": "held", "direction": "more" } ] }""", "written", "required: when this prediction was made" },
        { """{ "configuration": "rewritten", "written": "last Tuesday", "changes": [ { "metric": "held", "direction": "more" } ] }""", "written", "is not a date" },
        { """{ "configuration": "rewritten", "written": "2026-11-24", "changes": [ { "metric": "held", "check": "cites-note", "direction": "more" } ] }""", "changes[0]", "names one thing" },
        { """{ "configuration": "rewritten", "written": "2026-11-24", "changes": [ { "metric": "held", "direction": "better" } ] }""", "changes[0].direction", "\"more\" or \"less\"" },
        { """{ "configuration": "rewritten", "written": "2026-11-24", "changes": [ { "metric": "happiness", "direction": "more" } ] }""", "changes[0].metric", "not a number a try gives" },
        { """{ "configuration": "rewritten", "written": "2026-11-24", "changes": [ { "check": "gone", "direction": "more" } ] }""", "changes[0].check", "not a check any stage carries" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void APredictionsFile_IsRefusedWhereItIsWrong(string body, string field, string says)
    {
        using var repo = Repo("rewritten", body);
        var (predictions, problems) = Load(repo);
        Assert.Empty(predictions);
        Assert.Contains(problems, p => p.File == "exams/axes-answers/predictions/rewritten.json" && p.Field == field && p.Message.Contains(says));
    }
}
