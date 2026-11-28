using Auspex;

namespace Auspex.Tests;

/// <summary>
/// What proctor answers when asked (plans/axes-build.md, slice 3c), on the scripted exam with known answers
/// (Auspex.Tests/exams/axes-answers): the rewritten guide is better on one assistant and worse on the other, tone and
/// examples only ever moved together, and one named configuration was never run.
/// </summary>
public class AnswersTests
{
    static readonly Lazy<TestRepo> Made = new(() =>
    {
        var repo = new TestRepo();
        repo.CopyExam("axes-answers");
        foreach (var name in new[] { "shipped", "shipped-b", "rewritten", "rewritten-b", "terse-two" })
        {
            var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("axes-answers"), null, null, name);
            Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null, keys);
        }
        return repo;
    });

    static (TestRepo Repo, Exam Exam) Graded()
    {
        var repo = Made.Value;
        var exam = repo.LoadExam("axes-answers");
        Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);
        return (repo, exam);
    }

    [Fact]
    public void AComparison_ThatPointsDifferentWays_IsBetterHereWorseThere_AndIsNotReadAsOne()
    {
        var (repo, exam) = Graded();
        var answer = Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, null, null);

        Assert.Equal(("shipped", "better_here_worse_there", null), (answer.Against["prompt"], answer.Verdict, answer.Confirmed));
        Assert.Null(answer.Configuration);
        Assert.Null(answer.ReadAsOne);
        Assert.Contains("better here, worse there", answer.Sentence);
        var compared = answer.Cells.Where(c => c.State == "compared").ToList();
        Assert.Equal(["better", "worse"], compared.Select(c => c.Held!.Direction));
        Assert.Equal(["mock", "mock-b"], compared.Select(c => c.Holding["assistant"]));
        // The rewritten guide with the long prompt is named and was not run here: a row of its own, never left out.
        Assert.Equal("neither_run", answer.Cells.Single(c => c.Holding["length"] == "long").State);

        // Every number has its tries behind it, each with an address and its directory in the bank.
        var cell = compared[0];
        // Which check: counts per correctness check, each side, with the check's own sentence.
        Assert.Equal(["answers-question", "cites-note", "one-sentence", "says-when-unsure"], cell.Checks!.Select(k => k.Check));
        Assert.Equal(cell.Value.Tries.Sum(t => t.Held), cell.Checks!.Sum(k => k.Value));
        Assert.Equal("the answer is one sentence", cell.Checks![2].About);
        Assert.Equal((4, 4), (cell.Value.Tries.Count, cell.Against.Tries.Count));
        Assert.All(cell.Value.Tries, t => Assert.True(Directory.Exists(Path.Combine(repo.Root, t.Path))));
        Assert.All(cell.Value.Tries, t => Assert.Equal($"/axes-answers/try/{t.Id}", t.Address));
        Assert.Equal("rewritten", cell.Value.Name);
        // Every scripted try makes one tool call: compared as the other measures are, and no different.
        Assert.Equal(("same", 16), (cell.ToolCalls!.Direction, cell.ToolCalls.Pairings));
        Assert.All(cell.Value.Tries, t => Assert.Equal(1, t.ToolCalls));
        Assert.StartsWith("/axes-answers/compare?change=prompt%3Drewritten&against=prompt%3Dshipped&as_of=", answer.Address);

        // Held to one assistant it is one setting, read alone, and says what four tries a side could have shown.
        var onMock = Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, new Dictionary<string, string> { ["assistant"] = "mock" }, null);
        Assert.Equal("better", onMock.Verdict);
        Assert.Single(onMock.Cells, c => c.State == "compared");
        Assert.True(onMock.Power!.CouldConfirm);
        Assert.Contains("&within=assistant%3Dmock", onMock.Address);
    }

    [Fact]
    public void TwoAxesThatOnlyMovedTogether_AreSaidToBe_AndASettingNotRunIsNotRun()
    {
        var (repo, exam) = Graded();
        var answer = Answers.Compare(repo.Root, exam, "tone", "terse", null, null, null);
        Assert.Equal("nothing_to_compare", answer.Verdict);
        Assert.Equal(("examples", "two", "none"), (answer.MovedWith[0].Axis, answer.MovedWith[0].WithValue, answer.MovedWith[0].WithAgainst));
        Assert.Contains(answer.Cautions, c => c.StartsWith("Cannot be told apart from examples: every try with tone=terse had examples=two"));
        Assert.Contains("(no tone=plain there)", Assert.Single(answer.NotRun));
        Assert.Null(answer.Power);

        // Asked against a value outright, and refused where the question has no meaning.
        Assert.Equal("worse", Answers.Compare(repo.Root, exam, "prompt", "shipped", "rewritten", new Dictionary<string, string> { ["assistant"] = "mock" }, null).Verdict);
        Assert.Contains("against itself", Assert.Throws<AuspexException>(() => Answers.Compare(repo.Root, exam, "prompt", "shipped", null, null, null)).Message);
        Assert.Contains("is an axis the comparison is along", Assert.Throws<AuspexException>(() => Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, new Dictionary<string, string> { ["prompt"] = "shipped" }, null)).Message);
    }

    [Fact]
    public void AConfiguration_IsComparedByEverythingItChanges_Together_AgainstWhatIsLive()
    {
        var (repo, exam) = Graded();
        Configuration Named(string name) => exam.Axes!.Named.Single(c => c.Name == name);

        // Two values off live: the change is both, taken together, against the live value of each.
        var answer = Answers.Compare(repo.Root, exam, Named("terse-two"), null, null, null);
        Assert.Equal("terse-two", answer.Configuration);
        Assert.Equal(["tone", "examples"], answer.Change.Keys);
        Assert.Equal(("terse", "two", "plain", "none"), (answer.Change["tone"], answer.Change["examples"], answer.Against["tone"], answer.Against["examples"]));
        Assert.StartsWith("tone=terse × examples=two against tone=plain × examples=none", answer.Sentence);
        var cell = Assert.Single(answer.Cells);
        Assert.Equal(("compared", "terse-two", "shipped"), (cell.State, cell.Value.Name, cell.Against.Name));
        // Together they can be compared, where neither could alone; and the answer says it is of the two together.
        Assert.Empty(answer.MovedWith);
        Assert.Contains(answer.Cautions, c => c.StartsWith("This compares 2 values changed together (tone=terse × examples=two)"));
        Assert.StartsWith("/axes-answers/compare?change=tone%3Dterse%2Cexamples%3Dtwo&against=tone%3Dplain%2Cexamples%3Dnone&within=prompt%3Dshipped%2Clength%3Dshort&as_of=", answer.Address);

        // One value off live, on the assistant that is not ours to choose: the same change wherever the exam carries it, each assistant a setting.
        var onB = Answers.Compare(repo.Root, exam, Named("rewritten-b"), null, null, null);
        Assert.Equal(["prompt"], onB.Change.Keys);
        Assert.Equal("better_here_worse_there", onB.Verdict);
        Assert.StartsWith("prompt=rewritten against prompt=shipped: better here, worse there", onB.Sentence);
        // Our own other choices are held as the configuration has them: the long prompt is another candidate, and not in this answer.
        Assert.Equal(("plain", "none", "short"), (onB.Within!["tone"], onB.Within["examples"], onB.Within["length"]));
        Assert.Equal(["mock", "mock-b"], onB.Cells.Select(c => c.Holding["assistant"]));

        // Against another configuration by name: the change is every axis on which the two differ.
        var two = Answers.Compare(repo.Root, exam, Named("terse-two"), Named("rewritten"), null, null);
        Assert.Equal(["prompt", "tone", "examples"], two.Change.Keys);
        Assert.Equal("rewritten", Assert.Single(two.Cells).Against.Name);

        Assert.Contains("is what is in use", Assert.Throws<AuspexException>(() => Answers.Compare(repo.Root, exam, Named("shipped-b"), null, null, null)).Message);
        Assert.Contains("against itself", Assert.Throws<AuspexException>(() => Answers.Compare(repo.Root, exam, Named("rewritten"), Named("rewritten"), null, null)).Message);
    }

    [Fact]
    public void AnAnswer_IsAsOfATime_AndSaysHowManyTriesCameAfter()
    {
        var (repo, exam) = Graded();
        var before = Answers.Compare(repo.Root, exam, "prompt", "rewritten", null, null, Answers.ParseAsOf("2020-01-01"));
        Assert.Equal(("2020-01-01T23:59Z", "nothing_to_compare", 16), (before.AsOf, before.Verdict, before.TriesSince));
        Assert.All(before.Cells, c => Assert.Empty(c.Value.Tries));
        Assert.EndsWith("&as_of=2020-01-01T23%3A59Z", before.Address);
        Assert.Contains("expects a date", Assert.Throws<AuspexException>(() => Answers.ParseAsOf("yesterday")).Message);
    }

    [Fact]
    public void Coverage_CountsTriesByValueAndConfiguration_AndShowsWhatLiveSpans()
    {
        var (repo, exam) = Graded();
        var coverage = Answers.Coverage(repo.Root, exam, null);

        // Named and never run is a row with no tries; so is one named and not yet run by this test.
        Assert.Equal(8, coverage.Configurations.Count);
        Assert.Equal(["long", "long-b", "rewritten-long"], coverage.Configurations.Where(c => c.Tries == 0).Select(c => c.Name).Order());
        var terse = coverage.Configurations.Single(c => c.Name == "terse-two");
        Assert.Equal((4, 4, false), (terse.Tries, terse.Wanted, terse.Live));
        Assert.Equal(["tone=terse", "examples=two"], terse.OffLive);
        Assert.Equal(0, terse.DaysSinceLast);

        // The assistant is not ours to choose: what is live spans both, and both have tries.
        var span = coverage.LiveSpan!;
        Assert.Equal(["assistant"], span.Across);
        Assert.Equal((2, 2, true), (span.Configurations, span.WithTries, span.Listed));
        Assert.Equal(["shipped", "shipped-b"], span.Rows.Select(r => r.Name));

        var long_ = coverage.Values.Single(v => v is { Axis: "length", Value: "long" });
        Assert.Equal((0, 0, null), (long_.Configurations, long_.Tries, long_.Last));
        Assert.True(coverage.Values.Single(v => v is { Axis: "assistant", Value: "mock" }).Reference);
    }

    [Fact]
    public void AComparison_IsRefused_WhileATryInItIsNotGraded()
    {
        using var repo = new TestRepo();
        repo.CopyExam("axes-answers");
        repo.EditJson("axes-answers/exam.json", e => e["attempts"] = 1);
        foreach (var name in new[] { "shipped", "rewritten" })
        {
            var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("axes-answers"), null, null, name);
            Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null, keys);
        }
        var refused = Assert.Throws<AuspexException>(() => Answers.Compare(repo.Root, repo.LoadExam("axes-answers"), "prompt", "rewritten", null, null, null));
        Assert.Contains("2 stages of the tries in this comparison are not yet graded under the checks as they are now", refused.Message);
        // Coverage reads no grade, so it answers.
        Assert.Equal(2, Answers.Coverage(repo.Root, repo.LoadExam("axes-answers"), null).Configurations.Count(c => c.Tries == 1));
    }
}
