using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// Grades over time (plans/axes-build.md, slice 5), on the scripted exam with known answers: a grade names the standard
/// it was made under and an earlier one is kept beside it; a check may apply in some configurations only; a try with no
/// grade under the checks as they are now is not yet graded; and what moved with nothing re-run is said.
/// </summary>
public class StandardTests
{
    /// <summary>One try each of the shipped and rewritten guides on both assistants, graded.</summary>
    static TestRepo Made()
    {
        var repo = new TestRepo();
        repo.CopyExam("axes-answers");
        repo.EditJson("axes-answers/exam.json", e => e["attempts"] = 1);
        foreach (var name in new[] { "shipped", "shipped-b", "rewritten", "rewritten-b" })
        {
            var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("axes-answers"), null, null, name);
            Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null, keys);
        }
        Grade_(repo);
        return repo;
    }

    static List<Grade.AttemptGrade> Grade_(TestRepo repo, string? only = null)
    {
        var exam = repo.LoadExam("axes-answers");
        var current = Bank.Select(repo.Root, exam).Current.Where(a => only is null || exam.Axes!.Of(a.Variant)!.Name == only).ToList();
        return Grade.Attempts(repo.Root, exam, current, TextWriter.Null);
    }

    static (ConfigurationsFile Stats, Exam Exam) Report(TestRepo repo)
    {
        var exam = repo.LoadExam("axes-answers");
        var current = Bank.Select(repo.Root, exam).Current;
        return (Configurations.Compute(repo.Root, exam, current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, current), null).Stats, exam);
    }

    static string StageOf(TestRepo repo, Exam exam, string name) =>
        Bank.Select(repo.Root, exam).Current.Single(a => exam.Axes!.Of(a.Variant)!.Name == name).Dir(repo.Root);

    [Fact]
    public void AGrade_NamesTheStandardItWasMadeUnder_AndARegradeUnderAnotherIsKeptBesideIt()
    {
        using var repo = Made();
        var exam = repo.LoadExam("axes-answers");
        var stage = StageOf(repo, exam, "shipped");
        var first = Assert.Single(Standard.History(stage));
        Assert.Equal(Standard.Hash(exam, exam.Tasks[0]), first.Standard);
        Assert.Equal(exam.Hash, first.ExamHash);
        Assert.True(Standard.Current(exam, exam.Tasks[0], stage));

        // Graded again under the same checks, the record is replaced: one standard, one file.
        Grade_(repo);
        Assert.Single(Standard.History(stage));

        // A prompt is not the standard; a check's description, its script or the expected values are.
        repo.EditJson("axes-answers/exam.json", e => e["description"] = "said another way");
        Assert.Equal(first.Standard, Standard.Hash(repo.LoadExam("axes-answers"), exam.Tasks[0]));
        repo.EditJson("axes-answers/exam.json", e => e["grading"]!["checks"]!["tidy"] = JsonNode.Parse("""{ "max_tool_calls": 3 }"""));
        exam = repo.LoadExam("axes-answers");
        Assert.NotEqual(first.Standard, Standard.Hash(exam, exam.Tasks[0]));

        // Until graded again the try is not yet graded: it is in no number, and a comparison of it is refused.
        Assert.False(Standard.Current(exam, exam.Tasks[0], stage));
        var (stats, _) = Report(repo);
        Assert.All(stats.Configurations.Where(c => c.Name is "shipped" or "rewritten"), c => Assert.Equal(("not_graded", 1), (c.State, c.NotGraded)));
        Assert.Contains("not yet graded under the checks as they are now", Assert.Throws<ProctorException>(() => Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, null, null)).Message);
        Assert.Equal(1, Answers.Coverage(repo.Root, exam, null).Configurations.Single(c => c.Name == "shipped").NotGraded);

        // Graded lazily: one configuration is read, the rest stay not yet graded.
        Grade_(repo, only: "shipped");
        (stats, _) = Report(repo);
        Assert.Equal(("made", 0), (stats.Configurations.Single(c => c.Name == "shipped").State, stats.Configurations.Single(c => c.Name == "shipped").NotGraded));
        Assert.Equal("not_graded", stats.Configurations.Single(c => c.Name == "rewritten").State);
        // Both grades are on file, the old one under the old standard; and the new check is one found later.
        Assert.Equal(2, Standard.History(stage).Count);
        Assert.DoesNotContain("tidy", Standard.History(stage)[0].Checks.Keys);
        var added = Assert.Single(stats.StandardMoved!);
        Assert.Equal(("tidy", "added", 1), (added.Check, added.Kind, added.Stages));
    }

    [Fact]
    public void ACheckNarrowedToSomeValues_DoesNotApplyElsewhere_AndItsVerdictsThereAreWithdrawn()
    {
        using var repo = Made();
        var (before, _) = Report(repo);
        Assert.All(before.Configurations.Where(c => c.Made > 0), c => Assert.Equal(4, c.HeadlineChecks));
        Assert.Null(before.StandardMoved);

        repo.EditJson("axes-answers/exam.json", e => e["grading"]!["checks"]!["cites-note"]!["when"] = "assistant != mock-b");
        var grades = Grade_(repo);
        // Neither pass, fail nor undecided, on the assistant it no longer applies to; and still decided on the other.
        Assert.All(grades.Where(g => g.Variant.EndsWith("assistant=mock-b")), g => Assert.Equal(Verdict.NotApplicable, g.Checks!["cites-note"].Result));
        Assert.All(grades.Where(g => g.Variant.EndsWith("assistant=mock")), g => Assert.Contains(g.Checks!["cites-note"].Result, new[] { Verdict.Pass, Verdict.Fail }));
        Assert.Equal("applies only when assistant != mock-b", grades.First(g => g.Variant.EndsWith("assistant=mock-b")).Checks!["cites-note"].Reason);

        var (after, exam) = Report(repo);
        // A try there holds of three checks now, not four: the check is no part of what it can hold.
        Assert.Equal([4, 3, 4, 3], after.Configurations.Where(c => c.Made > 0).Select(c => c.HeadlineChecks));
        Assert.All(after.Configurations.Where(c => c.Name is "shipped-b" or "rewritten-b"), c => Assert.Equal(3, c.Tries!.Rows[0].Of));
        // The report says the standard moved, with what was withdrawn, and so does a comparison that reads those tries.
        var moved = Assert.Single(after.StandardMoved!);
        Assert.Equal(("cites-note", "narrowed", 2), (moved.Check, moved.Kind, moved.Stages));
        Assert.Equal(2, moved.Passed + moved.Failed);
        Assert.Contains("Nothing was re-run; these numbers moved because the standard did.", moved.Sentence);
        Assert.Contains("## The standard moved", Configurations.Markdown(after, new FrictionRollup("friction-rollup/1", exam.Id, [], null)));
        var onB = Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, new Dictionary<string, string> { ["assistant"] = "mock-b" }, null);
        Assert.Contains(onB.Cautions, c => c.StartsWith("The standard moved: cites-note no longer applies"));
        Assert.DoesNotContain("cites-note", onB.Cells.Single(c => c.State == "compared").Checks!.Select(k => k.Check));
        // A comparison that reads none of them carries no such caution.
        Assert.DoesNotContain(Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, new Dictionary<string, string> { ["assistant"] = "mock" }, null).Cautions, c => c.StartsWith("The standard moved"));
    }

    [Fact]
    public void ACondition_NamesOnlyWhatTheExamDeclares_AndGoesWithAxes()
    {
        using var repo = new TestRepo();
        repo.CopyExam("axes-answers");
        void When(string text) => repo.EditJson("axes-answers/exam.json", e => e["grading"]!["checks"]!["cites-note"]!["when"] = text);
        When("assistant != gone");
        Assert.Contains(repo.Problems("axes-answers"), p => p.Field == "grading.checks.cites-note.when" && p.Message.Contains("'gone' is not a value of axis 'assistant'"));
        When("colour = red");
        Assert.Contains(repo.Problems("axes-answers"), p => p.Field == "grading.checks.cites-note.when" && p.Message.Contains("'colour' is not a declared axis"));
        When("assistant >= 2");
        Assert.Contains(repo.Problems("axes-answers"), p => p.Message.Contains("only = and !="));
        When("assistant = mock");
        Assert.Empty(repo.Problems("axes-answers"));

        using var plain = new TestRepo();
        plain.CopyExam("smoke");
        plain.EditJson("smoke/exam.json", e => e["grading"]!["checks"]!["exit_ok"]!["when"] = "docs = v2");
        Assert.Contains(plain.Problems("smoke"), p => p.Field == "grading.checks.exit_ok.when" && p.Message.StartsWith("goes with axes"));
    }
}
