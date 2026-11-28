using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// A value built alone (plans/axes-build.md, slice 3b): what it changed from the reference build, a configuration held against its values alone, and the starting state a try records.
/// Nothing here runs a model.
/// </summary>
public class BuildTests
{
    /// <summary>
    /// smoke as one stage and three axes on a setup hook that writes prompt.txt from the declared parameters, as a product
    /// would write its prompt: tone on the first line, examples on a line well apart. The hook also stamps a file that is
    /// never the same twice, and can be given a line that reads both parameters at once.
    /// </summary>
    static TestRepo Smoke(string extra = "", Action<JsonObject>? edit = null)
    {
        var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/values/docs/shipped/README.md", "shipped\n");
        repo.Write("smoke/values/docs/rewritten/README.md", "rewritten\n");
        repo.Write("smoke/program.nb", "provider {{provider}}\nharness {{harness}}\nloop 2\nbudget tool_calls 3\napproval bash *\nrun {{prompt}}\n");
        WriteHook(repo, extra);
        repo.EditJson("smoke/exam.json", e =>
        {
            e["hooks"] = JsonNode.Parse("""{ "try": { "setup": "hooks/prompt.sh" } }""");
            e["attempts"] = 1;
            e["params"] = JsonNode.Parse("""{ "TONE": { "default": "plain" }, "EXAMPLES": { "default": "none" } }""");
            e["stages"] = JsonNode.Parse("""[ { "id": "write", "prompt": "MOCK:tool=bash echo mine > made.txt" } ]""");
            e["axes"] = JsonNode.Parse("""
                {
                  "docs": { "about": "the README", "live": "shipped", "values": {
                    "shipped":   { "note": "as it ships", "files": { "path": "values/docs/shipped" } },
                    "rewritten": { "note": "the README rewritten", "files": { "path": "values/docs/rewritten" } } } },
                  "tone": { "about": "how the prompt speaks", "live": "plain", "values": { "plain": { "note": "plainly" }, "terse": { "note": "the prompt's tone line says terse", "set": { "TONE": "terse" } } } },
                  "examples": { "about": "worked examples in the prompt", "reference": "none", "values": { "none": { "note": "none" }, "two": { "note": "two examples at the end of the prompt", "set": { "EXAMPLES": "two" } } } },
                  "assistant": { "about": "who does the work", "values": { "mock": { "note": "nb on Mock", "provider": "Mock", "model": "mock" } } }
                }
                """);
            e["configurations"] = JsonNode.Parse("""
                { "shipped": { "docs": "shipped", "tone": "plain", "examples": "none", "assistant": "mock" },
                  "terse-two": { "docs": "shipped", "tone": "terse", "examples": "two", "assistant": "mock" } }
                """);
            e["grading"] = JsonNode.Parse("""{ "checks": { "exit_ok": { "exit_reason": "ok" }, "wrote": { "files_touched": { "paths": ["made.txt"], "mode": "exactly" } } }, "correctness": ["exit_ok", "wrote"] }""");
            edit?.Invoke(e);
        });
        return repo;
    }

    static void WriteHook(TestRepo repo, string extra, string between = "two\\nthree\\nfour\\nfive\\nsix\\n")
    {
        repo.Write("smoke/hooks/prompt.sh", $$"""
            #!/usr/bin/env bash
            cd "$PROCTOR_WORK"
            printf 'tone: %s\n{{between}}' "$PROCTOR_PARAM_TONE" > prompt.txt
            [ "$PROCTOR_PARAM_EXAMPLES" = two ] && echo "example one; example two" >> prompt.txt
            {{extra}}
            date +%s%N > built-at.txt
            exit 0
            """.Replace("            ", ""));
        File.SetUnixFileMode(Path.Combine(repo.Root, "exams", "smoke", "hooks", "prompt.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static BuildResult Build(TestRepo repo, (string, string)? only = null) =>
        Proctor.Build.Run(repo.Root, repo.LoadExam("smoke"), Exam.LoadConfig(repo.Root, []), null, null, only, TextWriter.Null);

    [Fact]
    public void EachValue_IsBuiltAlone_AndItsChangeIsWhatDiffersFromTheReferenceBuild()
    {
        using var repo = Smoke();
        var result = Build(repo);

        Assert.Equal("docs=shipped,tone=plain,examples=none,assistant=mock", result.Reference);
        // A path two builds of the reference disagree on is the build's own, and in nobody's change.
        Assert.Equal(["built-at.txt"], result.Noise);
        // Every value that is not its axis's reference, and no other: an axis of one value has nothing to build.
        Assert.Equal(["docs=rewritten", "tone=terse", "examples=two"], result.Values.Select(v => $"{v.Axis}={v.Value}"));

        var docs = result.Values[0];
        Assert.Equal(("README.md", "changed"), (Assert.Single(docs.Paths).Path, docs.Paths[0].Change));
        Assert.Contains("-shipped", docs.Diff);
        Assert.Contains("+rewritten", docs.Diff);
        Assert.Empty(docs.Sets);
        Assert.StartsWith("git:", docs.Tree);

        // A parameter reaches the workspace only through the base's script, and the build shows that it did.
        var tone = result.Values[1];
        Assert.Equal("terse", tone.Sets["TONE"]);
        Assert.Equal("prompt.txt", Assert.Single(tone.Paths).Path);
        Assert.Contains("+tone: terse", tone.Diff);
        Assert.Contains("  says: the prompt's tone line says terse", Proctor.Build.Describe(tone, withDiff: false));

        // The scratch checkouts are gone, and each change is kept for whoever reviews it.
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".proctor", "builds", "smoke", "work")));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".proctor", "builds", "smoke", "tone=terse.json")));
    }

    [Fact]
    public void ATreeParameter_IsReadAsPartOfWhatAValueChanges_ThoughItIsOutsideTheCheckout()
    {
        // readme-answers: the docs value sets a directory the program reads; nothing of it is in the workspace.
        using var repo = new TestRepo();
        repo.CopyExam("readme-answers");
        var change = Assert.Single(Proctor.Build.Run(repo.Root, repo.LoadExam("readme-answers"), Exam.LoadConfig(repo.Root, []), null, null, null, TextWriter.Null).Values);
        Assert.Equal(("docs", "for-scripts"), (change.Axis, change.Value));
        Assert.Equal(["params/README/README.md"], change.Paths.Select(p => p.Path));
        Assert.Contains("+", change.Diff);
        Assert.NotNull(change.Why);
    }

    [Fact]
    public void AValueThatQuietlyDoesASecondThing_ShowsBothInItsChange()
    {
        using var repo = Smoke("""[ "$PROCTOR_PARAM_TONE" = terse ] && echo fixed > note.txt""");
        var tone = Assert.Single(Build(repo, ("tone", "terse")).Values);
        Assert.Equal(["note.txt", "prompt.txt"], tone.Paths.Select(p => p.Path));
    }

    [Fact]
    public void AConfiguration_IsHeldAgainstItsValuesAlone_AndAScriptThatReadsTwoAxesIsFound()
    {
        // Two values that write to lines apart in one file add up, and the configuration is what they come to.
        using var repo = Smoke();
        var sound = Assert.Single(Build(repo).Combinations);
        Assert.Equal(("terse-two", 0, 0), (sound.Name, sound.Differs.Count, sound.Unclear.Count));

        // A line written only when both are set is in neither value's change: the configuration is more than its values.
        WriteHook(repo, """[ "$PROCTOR_PARAM_TONE" = terse ] && [ "$PROCTOR_PARAM_EXAMPLES" = two ] && echo "keep the examples short too" >> prompt.txt""");
        var more = Assert.Single(Build(repo).Combinations);
        Assert.Contains("prompt.txt (not what the changes of tone=terse and examples=two alone come to)", Assert.Single(more.Differs));

        // Two values that write neighbouring lines cannot be added up at all, and that is said, not guessed.
        WriteHook(repo, "", between: "");
        var unclear = Assert.Single(Build(repo).Combinations);
        Assert.Empty(unclear.Differs);
        Assert.Contains("prompt.txt (tone=terse and examples=two each change it alone", Assert.Single(unclear.Unclear));
    }

    [Fact]
    public void ACombinationsCheck_IsKeptWithTheExam_AndHoldsWhileTheConfigurationAndTheBaseAreAsTheyWere()
    {
        const string key = "docs=shipped,tone=terse,examples=two,assistant=mock";
        using var repo = Smoke();
        var exam = repo.LoadExam("smoke");
        // Only a configuration more than one value off the reference has anything to check.
        Assert.Equal([key], Combinations.Load(exam).Stand(exam).Keys);
        Assert.Equal(Combinations.NotChecked, Combinations.Load(exam).Stand(exam)[key].State);

        Assert.Equal(0, Verbs.Build(repo.Root, "smoke", null, null, null));
        var file = Path.Combine(repo.Root, "exams", "smoke", "combinations.json");
        Assert.Equal(key, JsonNode.Parse(File.ReadAllText(file))!["combinations"]![0]!["configuration"]!.GetValue<string>());
        Assert.Equal(Combinations.Sum, Combinations.Load(exam).Stand(exam)[key].State);
        // Nothing records whether anyone read what a value changes.
        Assert.DoesNotContain("review", File.ReadAllText(file));

        // Built again with nothing changed, the file is left as it is: no date moves in a committed file.
        var before = File.ReadAllText(file);
        Assert.Equal(0, Verbs.Build(repo.Root, "smoke", null, null, null));
        Assert.Equal(before, File.ReadAllText(file));

        // A changed base asks for the check again: the hook is part of what was measured.
        WriteHook(repo, "echo more >> prompt.txt");
        exam = repo.LoadExam("smoke");
        Assert.Equal(Combinations.NotChecked, Combinations.Load(exam).Stand(exam)[key].State);
    }

    [Fact]
    public void Build_SaysWhy_WhenNothingCanBeHeldStill()
    {
        // With no reference on an axis nothing can be held still, so nothing can be built alone.
        using var repo = Smoke(edit: e => e["axes"]!["examples"]!.AsObject().Remove("reference"));
        Assert.Contains("axis 'examples' has no live value and names no reference", Assert.Throws<ProctorException>(() => Verbs.Build(repo.Root, "smoke", null, null, null)).Message);
        var exam = repo.LoadExam("smoke");
        Assert.All(Combinations.Load(exam).Stand(exam).Values, s => Assert.Equal(Combinations.NotChecked, s.State));
    }

    [Fact]
    public void ATry_StartsFromWhatSetupLeft_SoItsDiffIsTheAgentsAlone_AndARegradeRebuildsIt()
    {
        using var repo = Smoke();
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("smoke"), null, null, "terse-two");
        Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null, keys);
        var tryDir = Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, "bank", "smoke")));
        var stage = Path.Combine(tryDir, "stages", "1-write");

        // The starting state is recorded, and is the one `build` finds for the same configuration.
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(stage, "manifest.json")))!;
        Assert.StartsWith("git:", manifest["start_tree"]!.GetValue<string>());
        // What the hook wrote is in the try's setup patch and not in the stage's diff, so a check of what the agent touched sees only the agent.
        Assert.Contains("prompt.txt", File.ReadAllText(Path.Combine(tryDir, "setup.patch")));
        var diff = File.ReadAllText(Path.Combine(stage, "diff.patch"));
        Assert.Contains("made.txt", diff);
        Assert.DoesNotContain("prompt.txt", diff);

        var current = Bank.Select(repo.Root, exam).Current;
        Assert.All(Grade.Attempts(repo.Root, exam, current, TextWriter.Null), g => Assert.True(g.Pass));

        // With the checkout gone, the fixture, the values' files, the setup patch and the diff are the workspace again.
        Directory.Delete(Path.Combine(repo.Root, ".proctor", "work"), recursive: true);
        var rebuilt = Path.Combine(repo.Root, "rebuilt");
        Assert.Null(Checkout.Restore(exam.FixtureOf(exam.Tasks[0])!, rebuilt, stage, exam.Axes!.Overlays(exam.Axes.Of(keys[0])!, exam.Dir), Path.Combine(tryDir, "setup.patch")));
        Assert.StartsWith("tone: terse", File.ReadAllText(Path.Combine(rebuilt, "prompt.txt")));
        Assert.Equal("mine\n", File.ReadAllText(Path.Combine(rebuilt, "made.txt")));
    }
}
