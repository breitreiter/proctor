using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// A configuration run bare against nb's Mock provider (plans/axes-build.md, slice 2): the values' files laid over the
/// checkout, the declared parameters handed to the program, a try kept under an id of its own, and a changed value refused.
/// </summary>
public class AxesRunTests
{
    /// <summary>
    /// smoke as two stages and two axes: docs (a README laid over the checkout; shipped is live) and tone (a parameter
    /// the program reads; plain is its reference). All four configurations are named, since run makes what is named.
    /// </summary>
    static TestRepo Smoke(Action<JsonObject>? edit = null)
    {
        var repo = new TestRepo();
        repo.CopyExam("smoke");
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
                  "docs": { "about": "the README", "live": "shipped", "values": {
                    "shipped":   { "note": "as it ships", "files": { "path": "values/docs/shipped" } },
                    "rewritten": { "note": "rewritten", "files": { "path": "values/docs/rewritten" } } } },
                  "tone": { "about": "how the program speaks", "reference": "plain", "values": { "plain": { "note": "plainly" }, "terse": { "note": "tersely", "set": { "TONE": "terse" } } } },
                  "assistant": { "about": "who does the work", "values": { "mock": { "note": "nb on Mock", "provider": "Mock", "model": "mock" } } }
                }
                """);
            e["configurations"] = JsonNode.Parse("""
                { "shipped":   { "docs": "shipped",   "tone": "plain", "assistant": "mock" }, "shipped-terse":   { "docs": "shipped",   "tone": "terse", "assistant": "mock" },
                  "rewritten": { "docs": "rewritten", "tone": "plain", "assistant": "mock" }, "rewritten-terse": { "docs": "rewritten", "tone": "terse", "assistant": "mock" } }
                """);
            e["grading"] = JsonNode.Parse("""
                { "checks": { "exit_ok": { "exit_reason": "ok" }, "wrote": { "files_touched": { "paths": ["made.txt"], "mode": "exactly" } } }, "correctness": ["exit_ok"] }
                """);
            edit?.Invoke(e);
        });
        return repo;
    }

    /// <summary>`proctor run smoke`, with --value and --configuration as given: what the verb would make, made.</summary>
    static string Run(TestRepo repo, List<string>? values = null, string? configuration = null)
    {
        var log = new StringWriter();
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("smoke"), null, values, configuration);
        Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", log, keys);
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
        Assert.Equal([1, 2, 1, 2], stats.Configurations.Select(c => c.CorrectnessChecks));
        Assert.Equal([true, true, false, false], stats.Configurations.Select(c => c.Live));
        var docs = Assert.Single(stats.Comparisons);
        Assert.Equal(("docs", "rewritten", "shipped"), (docs.Axis, docs.Value, docs.Live));
        Assert.Equal(["tone=plain × assistant=mock", "tone=terse × assistant=mock"], docs.Cells.Select(c => string.Join(" × ", c.Holding.Select(h => $"{h.Key}={h.Value}"))));
        Assert.Equal(["tone"], docs.Pooled!.Across);
        Assert.Contains("over 2 of 2 configurations", docs.Sentence);

        // A configuration named and not run is not run, in the row and in the comparison's own words. One nobody named is nowhere.
        repo.EditJson("smoke/exam.json", e =>
        {
            e["axes"]!["tone"]!["values"]!["warm"] = JsonNode.Parse("""{ "note": "warmly", "set": { "TONE": "warm" } }""");
            e["configurations"]!["rewritten-warm"] = JsonNode.Parse("""{ "docs": "rewritten", "tone": "warm", "assistant": "mock" }""");
        });
        exam = repo.LoadExam("smoke");
        current = Bank.Select(repo.Root, exam).Current;
        (stats, _) = Configurations.Compute(repo.Root, exam, current, Bank.Of(repo.Root, exam), Results.Collect(repo.Root, exam, current), null);
        Assert.Equal(["docs=rewritten,tone=warm,assistant=mock"], stats.Configurations.Where(c => c.State == "not_run").Select(c => c.Key));
        Assert.Equal(5, stats.Configurations.Count);
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

        // Under a new name it is a new value: asked for alone it is made, and the others stand.
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten\n");
        repo.Write("smoke/values/docs/rewritten-2/README.md", "rewritten again\n");
        repo.EditJson("smoke/exam.json", e => e["axes"]!["docs"]!["values"]!["rewritten-2"] = JsonNode.Parse("""{ "note": "rewritten again", "files": { "path": "values/docs/rewritten-2" } }"""));
        Assert.Contains("nothing to make", Run(repo));
        Assert.Contains("1 attempt made: 1 whole", Run(repo, ["docs=rewritten-2"]));
        Assert.Equal(5, Tries(repo).Count);
    }

    [Fact]
    public void Run_MakesWhatIsNamed_OrAValueAlone_AndNeverTheCrossProduct()
    {
        using var repo = Smoke(e => e["configurations"] = JsonNode.Parse("""{ "shipped": { "docs": "shipped", "tone": "plain", "assistant": "mock" } }"""));
        Assert.Contains("1 attempt made: 1 whole", Run(repo));
        Assert.Equal(["docs=shipped,tone=plain,assistant=mock"], Tries(repo).Keys);

        // --value is that value with every other axis at its reference: the live docs, and the tone the axis names.
        Assert.Contains("1 attempt made: 1 whole", Run(repo, ["tone=terse"]));
        Assert.Contains("1 attempt made: 1 whole", Run(repo, ["docs=rewritten"]));
        Assert.Equal(["docs=rewritten,tone=plain,assistant=mock", "docs=shipped,tone=plain,assistant=mock", "docs=shipped,tone=terse,assistant=mock"], Tries(repo).Keys.Order());

        // Once made, a configuration is carried without a name: it is read, listed and reported like a named one.
        var exam = repo.LoadExam("smoke");
        Assert.Equal(3, exam.Axes!.Configurations.Count);
        Assert.Equal(["shipped"], exam.Axes.Named.Select(c => c.Name));
        Assert.Empty(Bank.Select(repo.Root, exam).Stale);
        Assert.Equal(6, Bank.Select(repo.Root, exam).Current.Count);

        // A plain run tops up what is named and leaves the rest as it is.
        repo.EditJson("smoke/exam.json", e => e["attempts"] = 2);
        Assert.Contains("1 attempt made: 1 whole", Run(repo));
        Assert.Equal(4, Directory.GetDirectories(Path.Combine(repo.Root, "bank", "smoke")).Length);
        Assert.Contains("nothing to make", Run(repo, configuration: "shipped"));
        Assert.Contains("1 attempt made: 1 whole", Run(repo, ["docs=rewritten"]));
    }

    [Fact]
    public void Run_SaysWhatToName_WhenNothingIs_AndWhatIsMissing_WhenAValueCannotStandAlone()
    {
        using var repo = Smoke(e => { e.Remove("configurations"); e["axes"]!["tone"]!.AsObject().Remove("reference"); });
        var none = Assert.Throws<ProctorException>(() => Run(repo));
        Assert.Contains("names no configuration, and `run` makes only what is named", none.Message);

        var unheld = Assert.Throws<ProctorException>(() => Run(repo, ["docs=rewritten"]));
        Assert.Contains("axis 'tone' has no live value and names no reference", unheld.Message);
        // Saying the value for the axis that has no reference is enough.
        Assert.Contains("1 attempt made: 1 whole", Run(repo, ["docs=rewritten", "tone=terse"]));

        Assert.Contains("one or the other", Assert.Throws<ProctorException>(() => Run(repo, ["docs=rewritten"], "shipped")).Message);
        Assert.Contains("names no such configuration (none)", Assert.Throws<ProctorException>(() => Run(repo, configuration: "shipped")).Message);

        repo.EditJson("smoke/exam.json", e => e["exclude"] = JsonNode.Parse("""[ { "docs": "shipped", "tone": "terse" } ]"""));
        Assert.Contains("is excluded by exclude", Assert.Throws<ProctorException>(() => Run(repo, ["docs=shipped", "tone=terse"])).Message);
    }

    [Fact]
    public void AValuesFiles_MayComeFromAGitRevision_OnlyThePathsItNames_AndThoseArePartOfItsIdentity()
    {
        using var repo = Smoke();
        var source = Directory.CreateDirectory(Path.Combine(repo.Root, "upstream")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "docs", "deep"));
        File.WriteAllText(Path.Combine(source, "README.md"), "from the revision\n");
        File.WriteAllText(Path.Combine(source, "docs", "deep", "page.md"), "page\n");
        File.WriteAllText(Path.Combine(source, "secret.md"), "not among the paths\n");
        foreach (var args in new[] { "init -q", "add -A", "-c user.email=t@t -c user.name=t commit -q -m one" })
            Assert.Equal(0, Subprocess.Run("git", args.Split(' '), source).ExitCode);
        var rev = Subprocess.Run("git", ["rev-parse", "HEAD"], source).Stdout.Trim();
        void Paths(string paths) => repo.EditJson("smoke/exam.json", e =>
            e["axes"]!["docs"]!["values"]!["rewritten"]!["files"] = new JsonObject { ["git"] = source, ["rev"] = rev, ["paths"] = JsonNode.Parse(paths) });

        Paths("""["docs/", "README.md"]""");
        var whole = repo.LoadExam("smoke").Axes.ValueHashes[("docs", "rewritten")];
        Run(repo, configuration: "rewritten");
        var read = Path.Combine(Tries(repo)["docs=rewritten,tone=plain,assistant=mock"], "stages", "1-read");
        // The stage read the README the revision holds, laid over the checkout before it began.
        Assert.Contains("from the revision", File.ReadAllText(Path.Combine(read, "transcript.jsonl")));
        var entry = Bank.Entries(repo.Root, "smoke").Single();
        var work = Layout.Work(repo.Root, "smoke", entry.Attempts[0].Id!);
        Assert.True(File.Exists(Path.Combine(work, "docs", "deep", "page.md")));
        Assert.False(File.Exists(Path.Combine(work, "secret.md")));

        // Another subset of the same revision is another value, which the name that made tries may not become.
        Paths("""["README.md"]""");
        Assert.NotEqual(whole, repo.LoadExam("smoke").Axes.ValueHashes[("docs", "rewritten")]);
        Assert.Contains("this is not the docs=rewritten that made attempts", Assert.Throws<ProctorException>(() => Run(repo, configuration: "rewritten")).Message);

        Paths("""["docs/", "README.md"]""");

        // A path the revision does not have stops the run, and is not an empty tree.
        repo.EditJson("smoke/exam.json", e => e["axes"]!["docs"]!["values"]!["later"] = new JsonObject
        {
            ["note"] = "a later draft",
            ["files"] = new JsonObject { ["git"] = source, ["rev"] = rev, ["paths"] = new JsonArray("missing.md") },
        });
        Assert.Contains("docs=later: missing.md is not in", Assert.Throws<ProctorException>(() => Run(repo, values: ["docs=later"])).Message);
    }
}
