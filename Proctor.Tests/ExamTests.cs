using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class ExamTests
{
    [Fact]
    public void SmokeEval_Loads()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");

        Assert.Equal("smoke", exam.Id);
        Assert.Equal(2, exam.Arms.Count);
        Assert.Equal(3, exam.Tasks.Count);
        Assert.Equal(12, exam.PlannedCells);
        Assert.Equal(["exit_ok", "says-expected"], exam.Grading.Pass);
        Assert.StartsWith("sha256:", exam.Hash);
        Assert.Equal(exam.Hash, repo.LoadExam("smoke").Hash);
    }

    [Fact]
    public void CheckDescriptions_AreDeclaredForScripts_AndDerivedForBuiltIns()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var described = repo.LoadExam("smoke").CheckDescriptions();
        Assert.Equal("the last assistant message is not empty", described["answer-nonempty"]);
        Assert.Equal("the run ends with 'ok'", described["exit_ok"]);
        Assert.Equal("no denied tool calls", described["no_denials"]);
        Assert.Equal("at most 3 tool calls and at most 100,000 tokens", described["under_budget"]);
        Assert.Equal("the answer contains what the task expects", described["says-expected"]);
        Assert.Equal("changes touch all of note.txt", described["touches-note"]);
    }

    [Fact]
    public void FixtureScriptCheck_NeedsADescription()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = JsonNode.Parse("{\"fixture-ok\": {\"script\": \"checks/ok.sh\"}}"));
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures", "note", "checks"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "note", "checks", "ok.sh"), "#!/bin/sh\nexit 0\n");
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("fixture.json") && p.Field == "checks.fixture-ok.description");
    }

    [Fact]
    public void TaskChecks_JoinTheUnionForThatTaskOnly()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse("{\"plain-only\": {\"answer_contains\": \"world\"}}"));

        var exam = repo.LoadExam("smoke");
        var plain = exam.Tasks.Single(c => c.Id == "plain");
        var loops = exam.Tasks.Single(c => c.Id == "loops");
        Assert.Contains("plain-only", exam.ChecksFor(plain).Keys);
        Assert.Equal(exam.Dir, exam.ChecksFor(plain)["plain-only"].Dir);
        Assert.DoesNotContain("plain-only", exam.ChecksFor(loops).Keys);
        Assert.Equal("plain-only", exam.CheckNames.Last());
        Assert.Equal("the answer contains 'world'", exam.CheckDescriptions()["plain-only"]);
    }

    [Fact]
    public void TaskChecks_AreValidated_AndDeclaredAtOneLevel()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse(
            "{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"Bad\": {\"exit_reason\": \"ok\"}, \"nope\": {\"bogus\": 1}, \"s\": {\"script\": \"checks/answer-nonempty.sh\"}}"));
        var problems = repo.Problems("smoke");
        Assert.All(problems, p => Assert.EndsWith("plain.json", p.File));
        Assert.Contains(problems, p => p.Field == "checks.exit_ok" && p.Message.Contains("also declared by the exam"));
        Assert.Contains(problems, p => p.Field == "checks.Bad");
        Assert.Contains(problems, p => p.Field == "checks.nope.bogus");
        Assert.Contains(problems, p => p.Field == "checks.s.description");
    }

    [Fact]
    public void PassMayNameACheck_OnlyWhenEveryTaskCarriesIt()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["grading"]!["pass"]!.AsArray().Add("own"));
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"answer_contains\": \"hello\"}}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.pass" && p.Message.Contains("task 'loops'"));

        repo.EditJson("smoke/tasks/loops.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/tasks/uses-bash.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"tools_used\": [\"bash\"]}}"));
        var exam = repo.LoadExam("smoke");
        Assert.Contains("own", exam.Grading.Pass!);
        Assert.False(exam.CheckDescriptions().ContainsKey("own"));   // three tasks, three criteria: no one sentence is the check's
        Assert.Equal("the run ends with 'ok'", exam.ChecksFor(exam.Tasks.Single(t => t.Id == "loops")).Values.Select(k => Checks.Describe(k.Spec)).Last());
        Assert.All(exam.Tasks, t => Assert.Equal("task", exam.ChecksFor(t)["own"].Level));
    }

    [Fact]
    public void CheckDescriptions_KeepOneSentence_WhenEveryTaskAgrees()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        foreach (var task in new[] { "plain", "loops", "uses-bash" })
            repo.EditJson($"smoke/tasks/{task}.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        Assert.Equal("the run ends with 'ok'", repo.LoadExam("smoke").CheckDescriptions()["own"]);
    }

    [Fact]
    public void Hash_ChangesWhenATaskChanges()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var before = repo.LoadExam("smoke").Hash;
        repo.EditJson("smoke/tasks/plain.json", c => c["prompt"] = "MOCK:response=goodbye");
        Assert.NotEqual(before, repo.LoadExam("smoke").Hash);
    }

    [Theory]
    [InlineData("id", "\"wrong\"", "smoke/exam.json", "id")]
    [InlineData("tags", "[\"deterministic\"]", "smoke/exam.json", "tags")]
    [InlineData("labels", "{\"Area\": \"x\"}", "smoke/exam.json", "labels.Area")]
    [InlineData("labels", "{\"area\": 1}", "smoke/exam.json", "labels.area")]
    [InlineData("labels", "{\"area\": []}", "smoke/exam.json", "labels.area")]
    [InlineData("labels", "{\"area\": [\"x\", \"\"]}", "smoke/exam.json", "labels.area")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"judge\": {}}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.judge")]
    [InlineData("arms", "[]", "smoke/exam.json", "arms")]
    [InlineData("hooks", "{\"run\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/exam.json", "hooks.run")]
    [InlineData("hooks", "{\"task\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/exam.json", "hooks.task")]
    [InlineData("hooks", "{\"arm\": {\"setup\": \"hooks/missing.sh\"}}", "smoke/exam.json", "hooks.arm.setup")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"y\"]}", "smoke/exam.json", "grading.pass")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"y\"]}", "smoke/exam.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"x\"]}", "smoke/exam.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"bogus\": 1}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.bogus")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"oracle_hit\": [\"k\"]}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"files_touched\": {\"paths\": []}}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.files_touched")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/nope.sh\"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"not_script\": \"checks/answer-nonempty.sh\", \"description\": \"d\"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.not_script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/answer-nonempty.sh\"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\", \"description\": \" \"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"description\": \"only words\"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.checks.x")]
    [InlineData("description", "\"\"", "smoke/exam.json", "description")]
    public void ExamJson_ProblemsNameFileAndField(string field, string json, string file, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e[field] = JsonNode.Parse(json));

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
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["arms"]![1] = JsonNode.Parse(secondArm));

        Assert.Contains(repo.Problems("smoke"), p => p.Field == expectedField);
    }

    [Fact]
    public void Labels_MergeFixtureThenEvalThenCase_AndReachEveryRow()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var fixtureFile = Path.Combine(repo.Root, "fixtures", "note", "fixture.json");
        var fixture = JsonNode.Parse(File.ReadAllText(fixtureFile))!.AsObject();
        fixture["labels"] = JsonNode.Parse("{\"stack\": \"none\", \"area\": \"fixture\"}");
        File.WriteAllText(fixtureFile, fixture.ToJsonString());
        repo.EditJson("smoke/exam.json", e => e["labels"] = JsonNode.Parse("{\"area\": [\"exam/a\", \"exam/b\"], \"kind\": \"smoke\"}"));
        repo.EditJson("smoke/tasks/plain.json", c => c["labels"] = JsonNode.Parse("{\"kind\": \"plain\"}"));

        var exam = repo.LoadExam("smoke");
        Assert.False(exam.Judged);
        var plain = exam.LabelsFor(exam.Tasks.Single(c => c.Id == "plain"));
        Assert.Equal(["none"], plain["stack"]);
        Assert.Equal(["exam/a", "exam/b"], plain["area"]);
        Assert.Equal(["plain"], plain["kind"]);
        Assert.Equal(["smoke"], exam.LabelsFor(exam.Tasks.Single(c => c.Id == "loops"))["kind"]);
        Assert.Equal(["plain", "smoke"], exam.AllLabels()["kind"].Order());
        Assert.Equal("area=exam/a,exam/b kind=plain stack=none", Labels.Format(plain));
    }

    [Theory]
    [InlineData("kind", true)]
    [InlineData("kind=plain", true)]
    [InlineData("kind=pl*", true)]
    [InlineData("area=exam/*", true)]
    [InlineData("area=*", false)]
    [InlineData("area=**", true)]
    [InlineData("kind=smoke", false)]
    [InlineData("owner", false)]
    public void Labels_FilterByKeyOrValueGlob(string filter, bool expected)
    {
        var labels = new Dictionary<string, List<string>> { ["kind"] = ["plain"], ["area"] = ["exam/a"] };
        Assert.Equal(expected, Labels.Matches(labels, filter));
    }

    [Fact]
    public void Case_IdMustMatchFileName_AndPromptRequired()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/tasks/renamed.json", "{\"id\": \"other\"}");

        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File.EndsWith("tasks/renamed.json") && p.Field == "id");
        Assert.Contains(problems, p => p.File.EndsWith("tasks/renamed.json") && p.Field == "prompt");
    }

    [Fact]
    public void Template_UnknownPlaceholderIsReported()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/program.nb", "provider {{provider}}\nrun {{question}}\n");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("program.nb") && p.Field == "{{question}}");
    }

    [Fact]
    public void MalformedJson_IsAProblemNotACrash()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/tasks/broken.json", "{ not json");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("tasks/broken.json"));
    }

    [Fact]
    public void Fixtures_AreLoadedByName_AndTheirChecksAndExpectMergeIntoTheCase()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");
        var fixture = Assert.Single(exam.Fixtures).Value;
        Assert.Equal("note", fixture.Id);
        Assert.StartsWith("sha256:", fixture.Hash);

        var loops = exam.Tasks.Single(c => c.Id == "loops");
        Assert.Null(loops.Expect);
        Assert.Equal("OK", exam.ExpectFor(loops)!["answer_contains"]!.GetValue<string>());
        var plain = exam.Tasks.Single(c => c.Id == "plain");
        Assert.Equal("hello", exam.ExpectFor(plain)!["answer_contains"]!.GetValue<string>());

        // A fixture check joins the task's set, resolved against the fixture directory.
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures/note/checks"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures/note/checks/ok.sh"), "#!/usr/bin/env bash\necho fine\n");
        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"fixture-ok\": {\"script\": \"checks/ok.sh\", \"description\": \"ok\"}}"));
        var before = exam.Hash;
        exam = repo.LoadExam("smoke");
        Assert.NotEqual(before, exam.Hash);
        var checks = exam.ChecksFor(plain);
        Assert.Equal(Path.Combine(repo.Root, "fixtures", "note"), checks["fixture-ok"].Dir);
        Assert.Equal(exam.Dir, checks["exit_ok"].Dir);
        Assert.Contains("fixture-ok", exam.CheckNames);
    }

    [Fact]
    public void Fixtures_ProblemsNameTheFileAndField()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/tasks/plain.json", c => c["fixture"] = "nope");
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "nope") && p.Message.Contains("no such fixture"));

        repo.EditJson("smoke/tasks/plain.json", c => c["fixture"] = "note");
        repo.EditJson("smoke/exam.json", e => e["grading"]!["pass"]!.AsArray().Add("builds"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.pass" && p.Message.Contains("fixture 'note'") && p.Message.Contains("declares it"));

        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/exam.json", e => e["grading"]!["pass"]!.AsArray().RemoveAt(2));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.checks.exit_ok" && p.Message.Contains("also declared by fixture"));

        repo.EditJson("../fixtures/note/fixture.json", f => { f.Remove("checks"); f["source"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/x\"}"); });
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "note", "fixture.json") && p.Field == "source.rev");
    }

    [Fact]
    public void Bundles_AreValidatedAgainstTheRepositoryRoot()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["arms"]![1]!["bundle"]!["path"] = "bundles/nope");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "arms[1].bundle.path");
        repo.EditJson("smoke/exam.json", e => e["arms"]![1]!["bundle"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/b\"}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "arms[1].bundle.rev");
        repo.EditJson("smoke/exam.json", e => e["arms"]![1]!["bundle"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/b\", \"rev\": \"abc\"}"));
        Assert.Empty(repo.Problems("smoke"));
    }

    [Theory]
    [InlineData("smoke/exam.json", "arms", "arms[2]")]
    [InlineData("smoke/exam.json", "grading.checks", "grading.checks.nothing")]
    [InlineData("smoke/exam.json", "grading.pass", "grading.pass")]
    [InlineData("smoke/exam.json", "grading.validity", "grading.validity")]
    [InlineData("smoke/tasks/plain.json", "checks", "checks.nothing")]
    [InlineData("../fixtures/note/fixture.json", "checks", "checks.nothing")]
    public void ANullEntry_IsAProblem_NotACrash(string file, string path, string field)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson(file, e =>
        {
            var parts = path.Split('.');
            var parent = parts[..^1].Aggregate((JsonNode)e, (n, p) => n[p]!);
            switch (parent[parts[^1]])
            {
                case JsonArray a: a.Add(null); break;
                case JsonObject o: o["nothing"] = null; break;
                case null when parts[^1] == "validity": parent[parts[^1]] = new JsonArray((JsonNode?)null); break;
                case null: parent[parts[^1]] = new JsonObject { ["nothing"] = null }; break;
            }
        });
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith(Path.GetFileName(file)) && p.Field == field);
    }
}
