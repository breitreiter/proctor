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
                    "rewritten":  { "note": "rewritten", "files": { "path": "values/docs/rewritten" } } } },
                  "assistant": { "about": "who does the work", "reference": "a", "values": {
                    "a": { "note": "nb on Mock", "provider": "Mock", "model": "mock" },
                    "b": { "note": "nb on the second Mock", "provider": "MockB", "model": "mock" } } }
                }
                """);
            edit?.Invoke(e);
        });
        return repo;
    }

    static JsonObject Axis(JsonObject exam, string axis) => exam["axes"]![axis]!.AsObject();
    static JsonObject Value(JsonObject exam, string axis, string value) => Axis(exam, axis)["values"]![value]!.AsObject();

    /// <summary>All four configurations of the two axes, named, and not in the axes' order.</summary>
    const string EveryOne = """
                { "shipped": { "docs": "as-shipped", "assistant": "a" }, "shipped-b": { "docs": "as-shipped", "assistant": "b" },
                  "rewritten-b": { "docs": "rewritten", "assistant": "b" }, "rewritten": { "docs": "rewritten", "assistant": "a" } }
                """;

    [Fact]
    public void AnExamCarriesTheConfigurationsItNames_InTheOrderOfTheAxes_EachWithWhoDoesTheWork()
    {
        // Nobody spans the cross product: an exam that names none carries none, and any one is made when asked for.
        using var none = Smoke();
        Assert.Empty(none.LoadExam("smoke").Axes!.Configurations);
        Assert.Empty(none.LoadExam("smoke").Variants);
        Assert.Equal("docs=rewritten × assistant=b", none.LoadExam("smoke").Axes!.Of("docs=rewritten,assistant=b")!.Label);
        Assert.Null(none.LoadExam("smoke").Axes!.Of("docs=gone,assistant=b"));

        using var repo = Smoke(e => e["configurations"] = JsonNode.Parse(EveryOne));
        var exam = repo.LoadExam("smoke");
        var axes = exam.Axes!;
        Assert.Equal(["shipped", "shipped-b", "rewritten", "rewritten-b"], axes.Configurations.Select(c => c.Name));

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
        Assert.Equal(before.Of("docs=as-shipped,assistant=a")!.Hash, edited.Of("docs=as-shipped,assistant=a")!.Hash);
        Assert.NotEqual(before.Of("docs=rewritten,assistant=a")!.Hash, edited.Of("docs=rewritten,assistant=a")!.Hash);
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

        string[] every = ["docs=as-shipped,assistant=a", "docs=as-shipped,assistant=b", "docs=rewritten,assistant=a", "docs=rewritten,assistant=b"];
        Assert.Equal([2, 2, 3, 2], every.Select(key => exam.Tasks.Count(s => axes.Applies(s, axes.Of(key)!))));
        Assert.Equal([false, false, false, true], every.Select(key => axes.Of(key)!.Excluded));
        Assert.Equal("shipped", axes.Of(every[0])!.Name);
        // Only what is named is carried, so one configuration stands where a variant did.
        Assert.Equal(["docs=as-shipped,assistant=a"], exam.Variants.Select(v => v.Id));

        // Naming the excluded one is refused, where it is named.
        repo.EditJson("smoke/exam.json", e => e["configurations"]!["never"] = JsonNode.Parse("""{ "docs": "rewritten", "assistant": "b" }"""));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "configurations.never" && p.Message.Contains("is excluded"));
    }

    [Fact]
    public void AWorldAxis_NamesTheFixturePerValue_AndAStageBelongsToTheWorldItsConditionNames()
    {
        using var repo = Smoke(e =>
        {
            e.Remove("fixture");
            Axis(e, "docs").Remove("live");
            e["axes"]!["repository"] = JsonNode.Parse("""{ "about": "the repository", "values": { "note": { "note": "a repository with a note", "fixture": "note" }, "empty": { "note": "nothing", "fixture": "empty" } } }""");
            e["stages"]![0]!["when"] = "repository = note";
            e["stages"]![1]!["when"] = "repository = empty";
            e["stages"]![2]!["when"] = "repository = empty";
        });
        repo.CopyFixture("empty");
        var exam = repo.LoadExam("smoke");

        Assert.Equal("repository", exam.Axes!.WorldAxis);
        Assert.Equal(["note", "empty", "empty"], exam.Tasks.Select(s => s.Fixture));
        Assert.Equal(["empty", "note"], exam.Fixtures.Keys.Order());
        var inNote = exam.Axes.Of("docs=as-shipped,assistant=a,repository=note")!;
        Assert.Equal("note", exam.Axes.FixtureOf(inNote, exam.Def.Fixture));
    }

    public static TheoryData<string, string, string> Refusals => new()
    {
        // what is wrong, the edit as JSON-path = value pairs, where the problem is reported, a word of what it says
        { "axes.docs.live=gone", "axes.docs.live", "not a value of this axis" },
        { "axes.docs.about=", "axes.docs.about", "required: a sentence saying what the axis is" },
        { "axes.docs.values.rewritten.note=", "axes.docs.values.rewritten.note", "required: a sentence saying what this value changes" },
        { "axes.assistant.reference=gone", "axes.assistant.reference", "not a value of this axis" },
        { "axes.docs.reference=rewritten", "axes.docs.reference", "which is its reference" },
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
        using var repo = Smoke(e => e["axes"]!["extra"] = JsonNode.Parse("""{ "about": "another axis", "values": { "on": { "note": "its README", "files": { "path": "values/extra/on" } }, "off": { "note": "nothing" } } }"""));
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
    public void ANamedConfigurationWithNoStage_IsRefused_WhereItIsNamed()
    {
        using var repo = Smoke(e => { foreach (var stage in e["stages"]!.AsArray()) stage!["when"] = "assistant = a"; });
        Assert.Empty(repo.Problems("smoke"));
        repo.EditJson("smoke/exam.json", e => e["configurations"] = JsonNode.Parse("""{ "other": { "docs": "as-shipped", "assistant": "b" } }"""));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "configurations.other" && p.Message.Contains("no stage applies in docs=as-shipped × assistant=b"));
    }

    [Fact]
    public void AValueAlone_IsThatValueWithEveryOtherAxisAtItsReference()
    {
        using var repo = Smoke();
        var axes = repo.LoadExam("smoke").Axes!;
        // The live value is an axis's reference; an axis with none names one; an axis of one value needs neither.
        Assert.Equal("as-shipped", axes.ReferenceOf("docs"));
        Assert.Equal("a", axes.ReferenceOf("assistant"));
        Assert.Equal("docs=rewritten,assistant=a", axes.Alone(new Dictionary<string, string> { ["docs"] = "rewritten" }, out _)!.Key);
        Assert.Equal("docs=as-shipped,assistant=b", axes.Alone(new Dictionary<string, string> { ["assistant"] = "b" }, out _)!.Key);

        repo.EditJson("smoke/exam.json", e => Axis(e, "assistant").Remove("reference"));
        axes = repo.LoadExam("smoke").Axes!;
        Assert.Null(axes.Alone(new Dictionary<string, string> { ["docs"] = "rewritten" }, out var problem));
        Assert.Contains("axis 'assistant' has no live value and names no reference", problem);
        Assert.Contains("--value assistant=<value> (a, b)", problem);
    }
}
