using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Axes, values and configurations as declared (plans/axes-build.md, slice 1): what loads, and what is refused and where.</summary>
public class AxesTests
{
    /// <summary>smoke as stages with two axes: docs (two overlays, as-shipped live) and assistant (two Mock providers). variants/ is gone.</summary>
    static TestRepo Smoke(Action<JsonObject>? edit = null)
    {
        var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        Directory.Delete(Path.Combine(repo.Root, "exams", "smoke", "variants"), recursive: true);
        repo.Write("smoke/values/docs/as-shipped/README.md", "as shipped\n");
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten\n");
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nrun {{prompt}}\n");
        repo.EditJson("smoke/exam.json", e =>
        {
            e.Remove("hooks");
            e["axes"] = JsonNode.Parse("""
                {
                  "docs": { "about": "the README", "live": "as-shipped", "values": {
                    "as-shipped": { "note": "as it ships", "files": { "path": "values/docs/as-shipped" } },
                    "rewritten":  { "files": { "path": "values/docs/rewritten" } } } },
                  "assistant": { "values": {
                    "a": { "provider": "Mock", "model": "mock" },
                    "b": { "provider": "MockB", "model": "mock" } } }
                }
                """);
            edit?.Invoke(e);
        });
        return repo;
    }

    static JsonObject Axis(JsonObject exam, string axis) => exam["axes"]![axis]!.AsObject();
    static JsonObject Value(JsonObject exam, string axis, string value) => Axis(exam, axis)["values"]![value]!.AsObject();

    [Fact]
    public void Axes_SpanEveryConfiguration_InDeclaredOrder_EachWithWhoDoesTheWork()
    {
        using var repo = Smoke();
        var exam = repo.LoadExam("smoke");
        var axes = exam.Axes!;

        Assert.Equal(["docs=as-shipped,assistant=a", "docs=as-shipped,assistant=b", "docs=rewritten,assistant=a", "docs=rewritten,assistant=b"], axes.Configurations.Select(c => c.Key));
        Assert.Equal("docs=as-shipped × assistant=a", axes.Configurations[0].Label);
        Assert.Equal("as-shipped", axes.Def["docs"].Live);
        Assert.Null(axes.Def["assistant"].Live);
        // Who does the work stands where a variant did: nb, bare, on the value's provider.
        Assert.Equal(axes.Configurations.Select(c => c.Key), exam.Variants.Select(v => v.Id));
        Assert.All(exam.Variants, v => Assert.True(v is { Runner: "nb", Harness: "nb", IsAgent: false }));
        Assert.Equal(["Mock", "MockB", "Mock", "MockB"], exam.Variants.Select(v => v.Provider));
        Assert.Equal(axes.Configurations.Select(c => c.Hash), exam.Variants.Select(v => v.Hash));
    }

    [Fact]
    public void AValuesIdentity_IsWhatItSetsAndCarries_NeverItsNote()
    {
        using var repo = Smoke();
        var before = repo.LoadExam("smoke").Axes!;

        repo.EditJson("smoke/exam.json", e => Value(e, "docs", "as-shipped")["note"] = "said another way");
        var renoted = repo.LoadExam("smoke").Axes!;
        Assert.Equal(before.ValueHashes, renoted.ValueHashes);

        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten again\n");
        var edited = repo.LoadExam("smoke").Axes!;
        Assert.NotEqual(before.ValueHashes[("docs", "rewritten")], edited.ValueHashes[("docs", "rewritten")]);
        Assert.Equal(before.ValueHashes[("docs", "as-shipped")], edited.ValueHashes[("docs", "as-shipped")]);
        // A configuration's identity is its values': only those holding the changed value move.
        Assert.Equal(before.Configurations[0].Hash, edited.Configurations[0].Hash);
        Assert.NotEqual(before.Configurations[2].Hash, edited.Configurations[2].Hash);
    }

    [Fact]
    public void AStagesCondition_SaysWhereItApplies_AndAnExclusionRemovesAConfiguration()
    {
        using var repo = Smoke(e =>
        {
            e["stages"]![0]!["when"] = "docs = rewritten and assistant != b";
            e["exclude"] = JsonNode.Parse("""[ { "docs": "rewritten", "assistant": "b" } ]""");
            e["configurations"] = JsonNode.Parse("""{ "shipped": { "docs": "as-shipped", "assistant": "a" } }""");
        });
        var exam = repo.LoadExam("smoke");
        var axes = exam.Axes!;

        Assert.Equal([2, 2, 3, 2], axes.Configurations.Select(c => exam.Tasks.Count(s => axes.Applies(s, c))));
        Assert.Equal([false, false, false, true], axes.Configurations.Select(c => c.Excluded));
        Assert.Equal("shipped", axes.Configurations[0].Name);
        // An excluded configuration is never made, so nobody stands in for it.
        Assert.Equal(3, exam.Variants.Count);
    }

    [Fact]
    public void AWorldAxis_NamesTheFixturePerValue_AndAStageBelongsToTheWorldItsConditionNames()
    {
        using var repo = Smoke(e =>
        {
            e.Remove("fixture");
            Axis(e, "docs").Remove("live");
            e["axes"]!["repository"] = JsonNode.Parse("""{ "values": { "note": { "fixture": "note" }, "empty": { "fixture": "empty" } } }""");
            e["stages"]![0]!["when"] = "repository = note";
            e["stages"]![1]!["when"] = "repository = empty";
            e["stages"]![2]!["when"] = "repository = empty";
        });
        repo.CopyFixture("empty");
        var exam = repo.LoadExam("smoke");

        Assert.Equal("repository", exam.Axes!.WorldAxis);
        Assert.Equal(["note", "empty", "empty"], exam.Tasks.Select(s => s.Fixture));
        Assert.Equal(["empty", "note"], exam.Fixtures.Keys.Order());
        var inNote = exam.Axes.Configurations.First(c => c["repository"] == "note");
        Assert.Equal("note", exam.Axes.FixtureOf(inNote, exam.Def.Fixture));
    }

    public static TheoryData<string, string, string> Refusals => new()
    {
        // what is wrong, the edit as JSON-path = value pairs, where the problem is reported, a word of what it says
        { "axes.docs.live=gone", "axes.docs.live", "not a value of this axis" },
        { "axes.docs.values.rewritten.files.path=values/docs/missing", "axes.docs.values.rewritten.files.path", "no such directory" },
        { "axes.docs.values.rewritten.files.to=../out", "axes.docs.values.rewritten.files.to", "inside the checkout" },
        { "axes.assistant.values.a.route=sideways", "axes.assistant.values.a.route", "not a route" },
        { "axes.docs.values.rewritten.model=mock", "axes.assistant", "which axis 'docs' also sets" },
        { "axes.docs.values.rewritten.set.UNDECLARED=1", "axes.docs.values.rewritten.set.UNDECLARED", "not a parameter the base declares" },
        { "stages.0.when=docs = gone", "stages[0].when", "not a value of axis 'docs'" },
        { "stages.0.when=colour = red", "stages[0].when", "not a declared axis" },
        { "stages.0.when=docs >= 2", "stages[0].when", "only = and !=" },
        { "axes.assistant.values.a.agent=claude-code", "axes", "agent_version" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ARefusal_NamesItsFieldInExamJson_AndSaysWhatToChange(string edit, string field, string says)
    {
        var (path, value) = (edit[..edit.IndexOf('=')].Split('.'), edit[(edit.IndexOf('=') + 1)..]);
        using var repo = Smoke(e =>
        {
            JsonNode at = e;
            foreach (var step in path[..^1])
                at = int.TryParse(step, out var i) ? at[i]! : at[step] ?? (at[step] = new JsonObject());
            at[path[^1]] = value;
        });
        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File == "exams/smoke/exam.json" && p.Field == field && p.Message.Contains(says));
    }

    [Fact]
    public void TwoAxes_CannotWriteOnePath_NorSetOneParameter()
    {
        using var repo = Smoke(e => e["axes"]!["extra"] = JsonNode.Parse("""{ "values": { "on": { "files": { "path": "values/extra/on" } }, "off": {} } }"""));
        repo.Write("smoke/values/extra/on/README.md", "another axis's README\n");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "axes.extra" && p.Message.Contains("writes README.md") && p.Message.Contains("axis 'docs' also writes"));

        // Moved aside with `to`, the two overlays no longer meet.
        repo.EditJson("smoke/exam.json", e => Value(e, "extra", "on")["files"]!["to"] = "extra");
        Assert.Empty(repo.Problems("smoke"));
    }

    [Fact]
    public void AParameter_IsSetOnlyWhenTheBaseDeclaresIt_InTheShapeDeclared()
    {
        using var repo = Smoke(e =>
        {
            Value(e, "docs", "rewritten")["set"] = JsonNode.Parse("""{ "TONE": "terse", "SITE": { "path": "values/docs/rewritten" } }""");
        });
        Assert.Equal(2, repo.Problems("smoke").Count(p => p.Message.Contains("not a parameter the base declares")));

        // Text declared by the exam (the program template reads it), a tree by the fixture.
        repo.EditJson("smoke/exam.json", e => e["params"] = JsonNode.Parse("""{ "TONE": { "default": "plain" } }"""));
        repo.EditJson("../fixtures/note/fixture.json", f => f["params"] = JsonNode.Parse("""{ "SITE": { "tree": true } }"""));
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nsystem Be {{param.TONE}} about {{axis.docs}}.\nrun {{prompt}}\n");
        Assert.Empty(repo.Problems("smoke"));

        repo.EditJson("smoke/exam.json", e => Value(e, "docs", "rewritten")["set"]!["SITE"] = "not a tree");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "axes.docs.values.rewritten.set.SITE" && p.Message.Contains("declared as a tree"));
    }

    [Fact]
    public void AnExamWithAxes_IsStages_HasNoVariantFiles_AndNoBundle()
    {
        using var repo = Smoke();
        repo.AddVariant("smoke", """{ "id": "c", "runner": "nb", "harness": "nb", "provider": "Mock", "model": "mock" }""");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "axes" && p.Message.Contains("also has variants"));
        repo.RemoveVariant("smoke", "c");

        repo.Write("smoke/program.nb", "# {{bundle}}\nprovider {{provider}}\nharness {{harness}}\nrun {{prompt}}\n");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "{{bundle}}" && p.Message.Contains("{{work}}"));

        // Without axes, what goes with them is refused by name.
        using var plain = new TestRepo();
        plain.CopyExam("smoke");
        plain.EditJson("smoke/exam.json", e => e["exclude"] = new JsonArray());
        Assert.Contains(plain.Problems("smoke"), p => p.Field == "exclude" && p.Message == "goes with axes");
    }

    [Fact]
    public void AConfigurationWithNoStage_IsRefused_UntilItIsExcluded()
    {
        using var repo = Smoke(e => { foreach (var stage in e["stages"]!.AsArray()) stage!["when"] = "assistant = a"; });
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "stages" && p.Message.Contains("no stage applies in docs=as-shipped × assistant=b"));
        repo.EditJson("smoke/exam.json", e => e["exclude"] = JsonNode.Parse("""[ { "assistant": "b" } ]"""));
        Assert.Empty(repo.Problems("smoke"));
    }
}
