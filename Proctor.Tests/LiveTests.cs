using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>The live mark (D9): exams/&lt;exam&gt;/live.json names one variant by id and hash; a mark whose variant is gone or changed is broken, not ignored.</summary>
public class LiveTests
{
    [Fact]
    public void Live_WritesTheVariantsIdAndHash_AndAnotherVariantReplacesIt()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");
        Assert.Null(Live.Load(exam));

        Assert.Equal(0, Verbs.Live(repo.Root, "smoke", "a"));
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, "exams/smoke/live.json")))!;
        Assert.Equal("a", file["variant"]!["id"]!.GetValue<string>());
        Assert.Equal(exam.Variants.Single(v => v.Id == "a").Hash, file["variant"]!["hash"]!.GetValue<string>());
        Assert.True(DateTime.TryParse(file["since"]!.GetValue<string>(), out _));
        Assert.Null(Live.Load(exam)!.Broken(exam));

        Assert.Equal(0, Verbs.Live(repo.Root, "smoke", "b"));
        Assert.Equal("b", Live.Load(exam)!.Variant.Id);
        Assert.Equal(0, Verbs.Live(repo.Root, "smoke", null));
        Assert.Throws<ProctorException>(() => Verbs.Live(repo.Root, "smoke", "nope"));
    }

    [Fact]
    public void AMarkWhoseVariantChangedOrWentAway_IsBroken_AndSaysWhich()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        Verbs.Live(repo.Root, "smoke", "a");

        repo.EditVariant("smoke", "a", v => v["model"] = "another");
        var exam = repo.LoadExam("smoke");
        var live = Live.Load(exam)!;
        Assert.StartsWith("its file has changed since it was marked (", live.Broken(exam));
        Assert.Contains("nothing is treated as live", live.Warning(exam, live.Broken(exam)!));

        // Marking it again takes today's hash, and the mark holds.
        Verbs.Live(repo.Root, "smoke", "a");
        Assert.Null(Live.Load(exam)!.Broken(exam));

        repo.RemoveVariant("smoke", "a");
        exam = repo.LoadExam("smoke");
        Assert.Equal("it has no file under variants/", Live.Load(exam)!.Broken(exam));
    }

    /// <summary>The report end to end: nothing live compares nothing; a live variant is the reference; a mark whose variant changed is said in stats.json and compares nothing.</summary>
    [Fact]
    public void Report_ComparesWithTheLiveVariant_OnlyWhileItsMarkHolds()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 1);
        var exam = repo.LoadExam("smoke");
        Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);
        var statsFile = Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "stats.json");

        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        var stats = JsonNode.Parse(File.ReadAllText(statsFile))!;
        Assert.Null(stats["live"]);
        Assert.Empty(stats["comparisons"]!.AsArray());

        Verbs.Live(repo.Root, "smoke", "b");
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        stats = JsonNode.Parse(File.ReadAllText(statsFile))!;
        Assert.Equal("b", stats["live"]!["variant"]!.GetValue<string>());
        Assert.Null(stats["live"]!["not_reference"]);
        var cmp = Assert.Single(stats["comparisons"]!.AsArray())!;
        Assert.Equal(("a", "b"), (cmp["variant"]!.GetValue<string>(), cmp["vs"]!.GetValue<string>()));
        Assert.Contains("Against the live variant, b", File.ReadAllText(Path.Combine(Layout.ReportExplore(repo.Root, "smoke"), "summary.md")));

        repo.EditVariant("smoke", "b", v => v["note"] = "a note is not the variant");      // the hash ignores the note: the mark holds
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        Assert.Null(JsonNode.Parse(File.ReadAllText(statsFile))!["live"]!["not_reference"]);
        repo.EditVariant("smoke", "b", v => v["model"] = "mock-2");
        Assert.Equal(0, Verbs.Report(repo.Root, "smoke"));
        stats = JsonNode.Parse(File.ReadAllText(statsFile))!;
        Assert.StartsWith("its file has changed since it was marked", stats["live"]!["not_reference"]!.GetValue<string>());
        Assert.Empty(stats["comparisons"]!.AsArray());
    }

    [Fact]
    public void AFileThatIsNotAMark_IsRefusedPlainly()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");
        repo.Write("smoke/live.json", "{ \"variant\": \"a\" ");
        Assert.Contains("not JSON", Assert.Throws<ProctorException>(() => Live.Load(exam)).Message);
        repo.Write("smoke/live.json", "{ \"since\": \"2026-11-15T10:00:00Z\" }");
        Assert.Contains("not a live mark", Assert.Throws<ProctorException>(() => Live.Load(exam)).Message);
    }
}
