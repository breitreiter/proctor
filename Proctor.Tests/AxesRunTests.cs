using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// A configuration run bare against nb's Mock provider (plans/axes-build.md, slice 2): the values' files laid over the
/// checkout, the declared parameters handed to the program, a try kept under an id of its own, and a changed value refused.
/// </summary>
public class AxesRunTests
{
    /// <summary>smoke as two stages and two axes: docs (a README laid over the checkout; shipped is live) and tone (a parameter the program reads).</summary>
    static TestRepo Smoke(Action<JsonObject>? edit = null)
    {
        var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        Directory.Delete(Path.Combine(repo.Root, "exams", "smoke", "variants"), recursive: true);
        repo.Write("smoke/values/docs/shipped/README.md", "shipped\n");
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten\n");
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nloop 2\nbudget tool_calls 3\napproval bash *\n# tone {{param.TONE}}, docs {{axis.docs}}\nrun {{prompt}}\n");
        repo.EditJson("smoke/exam.json", e =>
        {
            e.Remove("hooks");
            e["attempts"] = 1;
            e["params"] = JsonNode.Parse("""{ "TONE": { "default": "plain" } }""");
            e["stages"] = JsonNode.Parse("""
                [ { "id": "read", "prompt": "MOCK:tool=bash cat README.md" },
                  { "id": "write", "prompt": "MOCK:tool=bash echo mine > made.txt" } ]
                """);
            e["axes"] = JsonNode.Parse("""
                {
                  "docs": { "live": "shipped", "values": {
                    "shipped":   { "files": { "path": "values/docs/shipped" } },
                    "rewritten": { "files": { "path": "values/docs/rewritten" } } } },
                  "tone": { "values": { "plain": {}, "terse": { "set": { "TONE": "terse" } } } },
                  "assistant": { "values": { "mock": { "provider": "Mock", "model": "mock" } } }
                }
                """);
            e["grading"] = JsonNode.Parse("""
                { "checks": { "exit_ok": { "exit_reason": "ok" }, "wrote": { "files_touched": { "paths": ["made.txt"], "mode": "exactly" } } }, "pass": ["exit_ok"] }
                """);
            edit?.Invoke(e);
        });
        return repo;
    }

    static string Run(TestRepo repo)
    {
        var log = new StringWriter();
        Runner.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);
        return log.ToString();
    }

    /// <summary>The tries in the bank, by the configuration each manifest names.</summary>
    static Dictionary<string, string> Tries(TestRepo repo) =>
        Directory.GetDirectories(Path.Combine(repo.Root, "bank", "smoke")).ToDictionary(
            dir => JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "stages", "1-read", "manifest.json")))!["variant"]!["id"]!.GetValue<string>(), dir => dir);

    [Fact]
    public void EveryConfiguration_IsRunInItsOwnTry_NamedByAnId_WithItsValuesInTheManifest()
    {
        using var repo = Smoke();
        var log = Run(repo);
        Assert.Contains("4 attempts made: 4 whole", log);

        var tries = Tries(repo);
        Assert.Equal(4, tries.Count);
        Assert.All(tries.Values, dir => Assert.Matches(@"^\d{8}-\d{4}-[a-z0-9]{4}$", Path.GetFileName(dir)));

        var dir = tries["docs=rewritten,tone=terse,assistant=mock"];
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "stages", "2-write", "manifest.json")))!;
        Assert.Equal(Path.GetFileName(dir), manifest["try"]!.GetValue<string>());
        Assert.Equal("rewritten", manifest["configuration"]!["docs"]!["id"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["configuration"]!["tone"]!["hash"]!.GetValue<string>());
        Assert.StartsWith("sha256:", manifest["workspace_hash"]!.GetValue<string>());

        // The value's files were in the checkout the agent met, and a declared parameter reached the program.
        Assert.Contains("rewritten", File.ReadAllText(Path.Combine(dir, "stages", "1-read", "transcript.jsonl")));
        Assert.Contains("# tone terse, docs rewritten", File.ReadAllText(Path.Combine(dir, "stages", "1-read", "program.nb")));
        Assert.Contains("# tone plain, docs shipped", File.ReadAllText(Path.Combine(tries["docs=shipped,tone=plain,assistant=mock"], "stages", "1-read", "program.nb")));

        // The overlay is part of what a stage starts from: the diff holds only what the agent changed.
        var diff = File.ReadAllText(Path.Combine(dir, "stages", "2-write", "diff.patch"));
        Assert.Contains("made.txt", diff);
        Assert.DoesNotContain("README.md", diff);
        Assert.Equal("", File.ReadAllText(Path.Combine(dir, "stages", "1-read", "diff.patch")));

        // Nothing is left to make, and grading reads the tries where they are.
        Assert.Contains("nothing to make", Run(repo));
        var grades = Grade.Attempts(repo.Root, repo.LoadExam("smoke"), Bank.Select(repo.Root, repo.LoadExam("smoke")).Current, TextWriter.Null);
        Assert.Equal(8, grades.Count);
        Assert.All(grades, g => Assert.True(g.Pass));
        Assert.All(grades, g => Assert.Equal(g.Task == "write" ? Verdict.Pass : Verdict.Fail, g.Checks!["wrote"].Result));
    }

    [Fact]
    public void AStage_RunsOnlyWhereItsConditionHolds_AndATryIsTheStagesThatApply()
    {
        using var repo = Smoke(e => e["stages"]![1]!["when"] = "tone = terse");
        Run(repo);
        var tries = Tries(repo);
        Assert.False(Directory.Exists(Path.Combine(tries["docs=shipped,tone=plain,assistant=mock"], "stages", "2-write")));
        Assert.True(Directory.Exists(Path.Combine(tries["docs=shipped,tone=terse,assistant=mock"], "stages", "2-write")));
        // A try of one stage fills its configuration as a try of two fills another's.
        Assert.Contains("nothing to make", Run(repo));

        // The report: every configuration a row, each value against its axis's live one, a try's checks counted of the stages it has.
        var exam = repo.LoadExam("smoke");
        var current = Bank.Select(repo.Root, exam).Current;
        Grade.Attempts(repo.Root, exam, current, TextWriter.Null);
        var (stats, _) = Configurations.Compute(repo.Root, exam, current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, current), null);
        Assert.Equal(Configurations.Schema, stats.Schema);
        Assert.All(stats.Configurations, c => Assert.Equal("made", c.State));
        Assert.Equal([1, 2, 1, 2], stats.Configurations.Select(c => c.HeadlineChecks));
        Assert.Equal([true, true, false, false], stats.Configurations.Select(c => c.Live));
        var docs = Assert.Single(stats.Comparisons);
        Assert.Equal(("docs", "rewritten", "shipped"), (docs.Axis, docs.Value, docs.Live));
        Assert.Equal(["tone=plain × assistant=mock", "tone=terse × assistant=mock"], docs.Cells.Select(c => string.Join(" × ", c.Holding.Select(h => $"{h.Key}={h.Value}"))));
        Assert.Equal(["tone"], docs.Pooled!.Across);
        Assert.Contains("over 2 of 2 configurations", docs.Sentence);

        // A configuration nobody ran is not run, in the row and in the comparison's own words.
        repo.EditJson("smoke/exam.json", e => e["axes"]!["tone"]!["values"]!["warm"] = JsonNode.Parse("""{ "set": { "TONE": "warm" } }"""));
        exam = repo.LoadExam("smoke");
        current = Bank.Select(repo.Root, exam).Current;
        (stats, _) = Configurations.Compute(repo.Root, exam, current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, current), null);
        Assert.Equal(2, stats.Configurations.Count(c => c.State == "not_run"));
        Assert.Contains("Not run: tone=warm × assistant=mock (neither side)", stats.Comparisons[0].Sentence);
    }

    [Fact]
    public void AValueWhoseContentChanged_UnderTheNameItMadeAttemptsBy_IsRefused()
    {
        using var repo = Smoke();
        Run(repo);
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten again\n");
        var refused = Assert.Throws<ProctorException>(() => Run(repo));
        Assert.Contains("axes.docs.values.rewritten: this is not the docs=rewritten that made attempts", refused.Message);
        Assert.Contains("give it a new name", refused.Message);

        // Under a new name it is a new value: its configurations are made, and the others stand.
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten\n");
        repo.Write("smoke/values/docs/rewritten-2/README.md", "rewritten again\n");
        repo.EditJson("smoke/exam.json", e => e["axes"]!["docs"]!["values"]!["rewritten-2"] = JsonNode.Parse("""{ "files": { "path": "values/docs/rewritten-2" } }"""));
        Assert.Contains("2 attempts made: 2 whole", Run(repo));
        Assert.Equal(6, Tries(repo).Count);
    }
}
