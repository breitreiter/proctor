using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class SuiteTests
{
    [Fact]
    public void SmokeEval_Loads()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        var suite = repo.LoadSuite("smoke");

        Assert.Equal("smoke", suite.Id);
        Assert.Equal(2, suite.Arms.Count);
        Assert.Equal(3, suite.Tasks.Count);
        Assert.Equal(12, suite.PlannedCells);
        Assert.Equal(["exit_ok", "says-expected"], suite.Grading.Pass);
        Assert.StartsWith("sha256:", suite.Hash);
        Assert.Equal(suite.Hash, repo.LoadSuite("smoke").Hash);
    }

    [Fact]
    public void CheckDescriptions_AreDeclaredForScripts_AndDerivedForBuiltIns()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        var described = repo.LoadSuite("smoke").CheckDescriptions();
        Assert.Equal("the last assistant message is not empty", described["answer-nonempty"]);
        Assert.Equal("nb exits with 'ok'", described["exit_ok"]);
        Assert.Equal("no denied tool calls", described["no_denials"]);
        Assert.Equal("at most 3 tool calls and at most 100,000 tokens", described["under_budget"]);
        Assert.Equal("answer contains is what the task expects", described["says-expected"]);
        Assert.Equal("changes touch all of note.txt", described["touches-note"]);
    }

    [Fact]
    public void FixtureScriptCheck_NeedsADescription()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = JsonNode.Parse("{\"fixture-ok\": {\"script\": \"checks/ok.sh\"}}"));
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures", "note", "checks"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "note", "checks", "ok.sh"), "#!/bin/sh\nexit 0\n");
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("fixture.json") && p.Field == "checks.fixture-ok.description");
    }

    [Fact]
    public void LegacySpellings_StillLoad()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        Directory.Move(Path.Combine(repo.Root, "suites"), Path.Combine(repo.Root, "evals"));
        File.Move(Path.Combine(repo.Root, "evals/smoke/suite.json"), Path.Combine(repo.Root, "evals/smoke/eval.json"));
        Directory.Move(Path.Combine(repo.Root, "evals/smoke/tasks"), Path.Combine(repo.Root, "evals/smoke/cases"));

        var suite = repo.LoadSuite("smoke");
        Assert.Equal(3, suite.Tasks.Count);
        Assert.Equal(Path.Combine(repo.Root, "evals", "smoke"), suite.Dir);
        Assert.Equal(suite.Hash, repo.LoadSuite("smoke").Hash);
    }

    [Fact]
    public void TaskChecks_JoinTheUnionForThatTaskOnly()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse("{\"plain-only\": {\"answer_contains\": \"world\"}}"));

        var suite = repo.LoadSuite("smoke");
        var plain = suite.Tasks.Single(c => c.Id == "plain");
        var loops = suite.Tasks.Single(c => c.Id == "loops");
        Assert.Contains("plain-only", suite.ChecksFor(plain).Keys);
        Assert.Equal(suite.Dir, suite.ChecksFor(plain)["plain-only"].Dir);
        Assert.DoesNotContain("plain-only", suite.ChecksFor(loops).Keys);
        Assert.Equal("plain-only", suite.CheckNames.Last());
        Assert.Equal("the answer contains 'world'", suite.CheckDescriptions()["plain-only"]);
    }

    [Fact]
    public void TaskChecks_AreValidated_AndDeclaredAtOneLevel()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse(
            "{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"Bad\": {\"exit_reason\": \"ok\"}, \"nope\": {\"bogus\": 1}, \"s\": {\"script\": \"checks/answer-nonempty.sh\"}}"));
        var problems = repo.Problems("smoke");
        Assert.All(problems, p => Assert.EndsWith("plain.json", p.File));
        Assert.Contains(problems, p => p.Field == "checks.exit_ok" && p.Message.Contains("also declared by the suite"));
        Assert.Contains(problems, p => p.Field == "checks.Bad");
        Assert.Contains(problems, p => p.Field == "checks.nope.bogus");
        Assert.Contains(problems, p => p.Field == "checks.s.description");
    }

    [Fact]
    public void PassMayNameACheck_OnlyWhenEveryTaskCarriesIt()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => e["grading"]!["pass"]!.AsArray().Add("own"));
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"answer_contains\": \"hello\"}}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.pass" && p.Message.Contains("task 'loops'"));

        repo.EditJson("smoke/tasks/loops.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/tasks/uses-bash.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"tools_used\": [\"bash\"]}}"));
        var suite = repo.LoadSuite("smoke");
        Assert.Contains("own", suite.Grading.Pass!);
        Assert.False(suite.CheckDescriptions().ContainsKey("own"));   // three tasks, three criteria: no one sentence is the check's
        Assert.Equal("nb exits with 'ok'", suite.ChecksFor(suite.Tasks.Single(t => t.Id == "loops")).Values.Select(k => Checks.Describe(k.Spec)).Last());
        Assert.All(suite.Tasks, t => Assert.Equal("task", suite.ChecksFor(t)["own"].Level));
    }

    [Fact]
    public void CheckDescriptions_KeepOneSentence_WhenEveryTaskAgrees()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        foreach (var task in new[] { "plain", "loops", "uses-bash" })
            repo.EditJson($"smoke/tasks/{task}.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        Assert.Equal("nb exits with 'ok'", repo.LoadSuite("smoke").CheckDescriptions()["own"]);
    }

    [Fact]
    public void Hash_ChangesWhenATaskChanges()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        var before = repo.LoadSuite("smoke").Hash;
        repo.EditJson("smoke/tasks/plain.json", c => c["prompt"] = "MOCK:response=goodbye");
        Assert.NotEqual(before, repo.LoadSuite("smoke").Hash);
    }

    [Theory]
    [InlineData("id", "\"wrong\"", "smoke/suite.json", "id")]
    [InlineData("tags", "[\"deterministic\"]", "smoke/suite.json", "tags")]
    [InlineData("labels", "{\"Area\": \"x\"}", "smoke/suite.json", "labels.Area")]
    [InlineData("labels", "{\"area\": 1}", "smoke/suite.json", "labels.area")]
    [InlineData("labels", "{\"area\": []}", "smoke/suite.json", "labels.area")]
    [InlineData("labels", "{\"area\": [\"x\", \"\"]}", "smoke/suite.json", "labels.area")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"judge\": {}}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.judge")]
    [InlineData("arms", "[]", "smoke/suite.json", "arms")]
    [InlineData("hooks", "{\"run\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/suite.json", "hooks.run")]
    [InlineData("hooks", "{\"task\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/suite.json", "hooks.task")]
    [InlineData("hooks", "{\"arm\": {\"setup\": \"hooks/missing.sh\"}}", "smoke/suite.json", "hooks.arm.setup")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"y\"]}", "smoke/suite.json", "grading.pass")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"y\"]}", "smoke/suite.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"x\"]}", "smoke/suite.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"bogus\": 1}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.bogus")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"oracle_hit\": [\"k\"]}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.oracle_hit")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"files_touched\": {\"paths\": []}}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.files_touched")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/nope.sh\"}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"not_script\": \"checks/answer-nonempty.sh\", \"description\": \"d\"}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.not_script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/answer-nonempty.sh\"}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\", \"description\": \" \"}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"description\": \"only words\"}}, \"pass\": [\"x\"]}", "smoke/suite.json", "grading.checks.x")]
    [InlineData("description", "\"\"", "smoke/suite.json", "description")]
    public void SuiteJson_ProblemsNameFileAndField(string field, string json, string file, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => e[field] = JsonNode.Parse(json));

        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File.EndsWith(file) && p.Field == expectedField);
    }

    [Theory]
    [InlineData("{\"id\": \"a\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\"}", "arms[1].id")]
    [InlineData("{\"id\": \"c\", \"runner\": \"command\", \"harness\": \"codex\", \"command\": \"codex\"}", "arms[1].runner")]
    [InlineData("{\"id\": \"c\", \"runner\": \"nb\", \"harness\": \"nb\"}", "arms[1].provider")]
    [InlineData("{\"id\": \"c\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\", \"samples\": 0}", "arms[1].samples")]
    [InlineData("{\"id\": \"Bad Id\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\"}", "arms[1].id")]
    public void Arms_AreValidated(string secondArm, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => e["arms"]![1] = JsonNode.Parse(secondArm));

        Assert.Contains(repo.Problems("smoke"), p => p.Field == expectedField);
    }

    [Fact]
    public void Labels_MergeFixtureThenEvalThenCase_AndReachEveryRow()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        var fixtureFile = Path.Combine(repo.Root, "fixtures", "note", "fixture.json");
        var fixture = JsonNode.Parse(File.ReadAllText(fixtureFile))!.AsObject();
        fixture["labels"] = JsonNode.Parse("{\"stack\": \"none\", \"area\": \"fixture\"}");
        File.WriteAllText(fixtureFile, fixture.ToJsonString());
        repo.EditJson("smoke/suite.json", e => e["labels"] = JsonNode.Parse("{\"area\": [\"suite/a\", \"suite/b\"], \"kind\": \"smoke\"}"));
        repo.EditJson("smoke/tasks/plain.json", c => c["labels"] = JsonNode.Parse("{\"kind\": \"plain\"}"));

        var suite = repo.LoadSuite("smoke");
        Assert.False(suite.Judged);
        var plain = suite.LabelsFor(suite.Tasks.Single(c => c.Id == "plain"));
        Assert.Equal(["none"], plain["stack"]);
        Assert.Equal(["suite/a", "suite/b"], plain["area"]);
        Assert.Equal(["plain"], plain["kind"]);
        Assert.Equal(["smoke"], suite.LabelsFor(suite.Tasks.Single(c => c.Id == "loops"))["kind"]);
        Assert.Equal(["plain", "smoke"], suite.AllLabels()["kind"].Order());
        Assert.Equal("area=suite/a,suite/b kind=plain stack=none", Labels.Format(plain));
    }

    [Theory]
    [InlineData("kind", true)]
    [InlineData("kind=plain", true)]
    [InlineData("kind=pl*", true)]
    [InlineData("area=suite/*", true)]
    [InlineData("area=*", false)]
    [InlineData("area=**", true)]
    [InlineData("kind=smoke", false)]
    [InlineData("owner", false)]
    public void Labels_FilterByKeyOrValueGlob(string filter, bool expected)
    {
        var labels = new Dictionary<string, List<string>> { ["kind"] = ["plain"], ["area"] = ["suite/a"] };
        Assert.Equal(expected, Labels.Matches(labels, filter));
    }

    [Fact]
    public void Case_IdMustMatchFileName_AndPromptRequired()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.Write("smoke/tasks/renamed.json", "{\"id\": \"other\"}");

        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File.EndsWith("tasks/renamed.json") && p.Field == "id");
        Assert.Contains(problems, p => p.File.EndsWith("tasks/renamed.json") && p.Field == "prompt");
    }

    [Fact]
    public void Template_UnknownPlaceholderIsReported()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.Write("smoke/program.nb", "provider {{provider}}\nrun {{question}}\n");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("program.nb") && p.Field == "{{question}}");
    }

    [Fact]
    public void MalformedJson_IsAProblemNotACrash()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.Write("smoke/tasks/broken.json", "{ not json");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("tasks/broken.json"));
    }

    [Fact]
    public void Fixtures_AreLoadedByName_AndTheirChecksAndExpectMergeIntoTheCase()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        var suite = repo.LoadSuite("smoke");
        var fixture = Assert.Single(suite.Fixtures).Value;
        Assert.Equal("note", fixture.Id);
        Assert.StartsWith("sha256:", fixture.Hash);

        var loops = suite.Tasks.Single(c => c.Id == "loops");
        Assert.Null(loops.Expect);
        Assert.Equal("OK", suite.ExpectFor(loops)!["answer_contains"]!.GetValue<string>());
        var plain = suite.Tasks.Single(c => c.Id == "plain");
        Assert.Equal("hello", suite.ExpectFor(plain)!["answer_contains"]!.GetValue<string>());

        // A fixture check joins the task's set, resolved against the fixture directory.
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures/note/checks"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures/note/checks/ok.sh"), "#!/usr/bin/env bash\necho fine\n");
        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"fixture-ok\": {\"script\": \"checks/ok.sh\", \"description\": \"ok\"}}"));
        var before = suite.Hash;
        suite = repo.LoadSuite("smoke");
        Assert.NotEqual(before, suite.Hash);
        var checks = suite.ChecksFor(plain);
        Assert.Equal(Path.Combine(repo.Root, "fixtures", "note"), checks["fixture-ok"].Dir);
        Assert.Equal(suite.Dir, checks["exit_ok"].Dir);
        Assert.Contains("fixture-ok", suite.CheckNames);
    }

    [Fact]
    public void Fixtures_ProblemsNameTheFileAndField()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["fixture"] = "nope");
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "nope") && p.Message.Contains("no such fixture"));

        repo.EditJson("smoke/tasks/plain.json", c => c["fixture"] = "note");
        repo.EditJson("smoke/suite.json", e => e["grading"]!["pass"]!.AsArray().Add("builds"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.pass" && p.Message.Contains("fixture 'note'") && p.Message.Contains("declares it"));

        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/suite.json", e => e["grading"]!["pass"]!.AsArray().RemoveAt(2));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.checks.exit_ok" && p.Message.Contains("also declared by fixture"));

        repo.EditJson("../fixtures/note/fixture.json", f => { f.Remove("checks"); f["source"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/x\"}"); });
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "note", "fixture.json") && p.Field == "source.rev");
    }

    [Fact]
    public void Bundles_AreValidatedAgainstTheRepositoryRoot()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => e["arms"]![1]!["bundle"]!["path"] = "bundles/nope");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "arms[1].bundle.path");
        repo.EditJson("smoke/suite.json", e => e["arms"]![1]!["bundle"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/b\"}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "arms[1].bundle.rev");
        repo.EditJson("smoke/suite.json", e => e["arms"]![1]!["bundle"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/b\", \"rev\": \"abc\"}"));
        Assert.Empty(repo.Problems("smoke"));
    }
}
