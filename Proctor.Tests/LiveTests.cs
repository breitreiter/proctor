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
