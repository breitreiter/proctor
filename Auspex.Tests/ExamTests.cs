using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

public class ExamTests
{
    [Fact]
    public void SmokeExam_Loads()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");

        Assert.Equal("smoke", exam.Id);
        Assert.Equal(2, exam.Assistants.Count);
        Assert.Equal(["plain", "uses-bash", "loops", "writes-a-file"], exam.Stages.Select(t => t.Id));
        Assert.Equal(2, exam.Attempts);
        Assert.Equal(["exit_ok", "says-expected"], exam.Grading.Correctness);
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
        Assert.Equal("the attempt ends with 'ok'", described["exit_ok"]);
        Assert.Equal("no denied tool calls", described["no_denials"]);
        Assert.Equal("at most 3 tool calls and at most 100,000 tokens", described["under_budget"]);
        Assert.Equal("the answer contains what the stage expects", described["says-expected"]);
        Assert.Equal("changes touch exactly made.txt", described["made-a-file"]);
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
    public void AStagesChecks_JoinTheUnionForThatStageOnly()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditStage("smoke", "plain", c => c["checks"] = JsonNode.Parse("{\"plain-only\": {\"answer_contains\": \"world\"}}"));

        var exam = repo.LoadExam("smoke");
        var plain = exam.Stages.Single(c => c.Id == "plain");
        var loops = exam.Stages.Single(c => c.Id == "loops");
        Assert.Contains("plain-only", exam.ChecksFor(plain).Keys);
        Assert.Equal(exam.Dir, exam.ChecksFor(plain)["plain-only"].Dir);
        Assert.DoesNotContain("plain-only", exam.ChecksFor(loops).Keys);
        Assert.Contains("plain-only", exam.CheckNames);
        Assert.Equal("the answer contains 'world'", exam.CheckDescriptions()["plain-only"]);
    }

    [Fact]
    public void AStagesChecks_AreValidated_AndDeclaredAtOneLevel()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditStage("smoke", "plain", c => c["checks"] = JsonNode.Parse(
            "{\"exit_ok\": {\"exit_reason\": \"ok\"}, \"Bad\": {\"exit_reason\": \"ok\"}, \"nope\": {\"bogus\": 1}, \"s\": {\"script\": \"checks/answer-nonempty.sh\"}}"));
        var problems = repo.Problems("smoke");
        Assert.All(problems, p => Assert.EndsWith("exam.json", p.File));
        Assert.Contains(problems, p => p.Field == "stages[0].checks.exit_ok" && p.Message.Contains("also declared by the exam"));
        Assert.Contains(problems, p => p.Field == "stages[0].checks.Bad");
        Assert.Contains(problems, p => p.Field == "stages[0].checks.nope.bogus");
        Assert.Contains(problems, p => p.Field == "stages[0].checks.s.description");
    }

    [Fact]
    public void ACheckEachStageDeclares_KeepsOneSentence_OnlyWhenEveryStageAgrees()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        foreach (var stage in new[] { "plain", "loops", "uses-bash" })
            repo.EditStage("smoke", stage, c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        var exam = repo.LoadExam("smoke");
        Assert.Equal("the attempt ends with 'ok'", exam.CheckDescriptions()["own"]);
        Assert.Equal("stage", exam.ChecksFor(exam.StageOf("loops"))["own"].Level);
        // A correctness check must reach every stage: one that a stage lacks is named with the stage.
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().Add("own"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.correctness" && p.Message.Contains("'writes-a-file'"));
        repo.EditStage("smoke", "writes-a-file", c => c["checks"]!["own"] = JsonNode.Parse("{\"exit_reason\": \"ok\"}"));
        Assert.Contains("own", repo.LoadExam("smoke").Grading.Correctness!);

        repo.EditStage("smoke", "loops", c => c["checks"] = JsonNode.Parse("{\"own\": {\"tools_used\": [\"bash\"]}}"));
        Assert.False(repo.LoadExam("smoke").CheckDescriptions().ContainsKey("own"));   // two criteria: no one sentence is the check's
    }

    [Fact]
    public void Hash_ChangesWhenAStageChanges()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var before = repo.LoadExam("smoke").Hash;
        repo.EditStage("smoke", "plain", c => c["prompt"] = "MOCK:response=goodbye");
        Assert.NotEqual(before, repo.LoadExam("smoke").Hash);
    }

    [Theory]
    [InlineData("id", "\"wrong\"", "smoke/exam.json", "id")]
    [InlineData("tags", "[\"deterministic\"]", "smoke/exam.json", "tags")]
    [InlineData("labels", "{\"Area\": \"x\"}", "smoke/exam.json", "labels.Area")]
    [InlineData("labels", "{\"area\": 1}", "smoke/exam.json", "labels.area")]
    [InlineData("labels", "{\"area\": []}", "smoke/exam.json", "labels.area")]
    [InlineData("labels", "{\"area\": [\"x\", \"\"]}", "smoke/exam.json", "labels.area")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"judge\": {}}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.judge")]
    [InlineData("attempts", "0", "smoke/exam.json", "attempts")]
    [InlineData("hooks", "{\"run\": {\"setup\": \"hooks/configuration-setup.sh\"}}", "smoke/exam.json", "hooks.run")]
    [InlineData("hooks", "{\"task\": {\"setup\": \"hooks/configuration-setup.sh\"}}", "smoke/exam.json", "hooks.task")]
    [InlineData("hooks", "{\"configuration\": {\"setup\": \"hooks/missing.sh\"}}", "smoke/exam.json", "hooks.configuration.setup")]
    [InlineData("hooks", "{\"arm\": {\"setup\": \"hooks/configuration-setup.sh\"}}", "smoke/exam.json", "hooks.arm")]
    [InlineData("hooks", "{\"sample\": {\"setup\": \"hooks/try-setup.sh\"}}", "smoke/exam.json", "hooks.sample")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"y\"]}", "smoke/exam.json", "grading.correctness")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"x\"], \"integrity\": [\"y\"]}", "smoke/exam.json", "grading.integrity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"x\"], \"integrity\": [\"x\"]}", "smoke/exam.json", "grading.integrity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"]}", "smoke/exam.json", "grading.pass")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"x\"], \"validity\": []}", "smoke/exam.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"x\"], \"quality\": [\"x\"]}", "smoke/exam.json", "grading.quality")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"correctness\": [\"x\"], \"quality\": [\"y\"]}", "smoke/exam.json", "grading.quality")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"decide\": \"is the answer right?\", \"expect\": \"yes\"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.correctness")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"bogus\": 1}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.bogus")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"oracle_hit\": [\"k\"]}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"files_touched\": {\"paths\": []}}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.files_touched")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/nope.sh\"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"not_script\": \"checks/answer-nonempty.sh\", \"description\": \"d\"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.not_script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/answer-nonempty.sh\"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\", \"description\": \" \"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.description")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"description\": \"only words\"}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x")]
    [InlineData("description", "\"\"", "smoke/exam.json", "description")]
    public void ExamJson_ProblemsNameFileAndField(string field, string json, string file, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e[field] = JsonNode.Parse(json));

        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File.EndsWith(file) && p.Field == expectedField);
    }

    [Fact]
    public void Labels_MergeFixtureThenExam_AndAStageHasNone()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var fixtureFile = Path.Combine(repo.Root, "fixtures", "note", "fixture.json");
        var fixture = JsonNode.Parse(File.ReadAllText(fixtureFile))!.AsObject();
        fixture["labels"] = JsonNode.Parse("{\"stack\": \"none\", \"area\": \"fixture\"}");
        File.WriteAllText(fixtureFile, fixture.ToJsonString());
        repo.EditJson("smoke/exam.json", e => e["labels"] = JsonNode.Parse("{\"area\": [\"exam/a\", \"exam/b\"], \"kind\": \"smoke\"}"));

        var exam = repo.LoadExam("smoke");
        Assert.False(exam.Judged);
        var labels = exam.AllLabels();
        Assert.Equal(["none"], labels["stack"]);
        Assert.Equal(["exam/a", "exam/b"], labels["area"]);
        Assert.Equal(["smoke"], labels["kind"]);
        Assert.Equal("area=exam/a,exam/b kind=smoke stack=none", Labels.Format(labels));
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
        repo.Write("smoke/exam.json", "{ not json");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("smoke/exam.json"));
    }

    [Fact]
    public void Fixtures_AreLoadedByName_AndTheirChecksAndExpectMergeIntoTheTask()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");
        var fixture = Assert.Single(exam.Fixtures).Value;
        Assert.Equal("note", fixture.Id);
        Assert.StartsWith("sha256:", fixture.Hash);

        var loops = exam.Stages.Single(c => c.Id == "loops");
        Assert.Null(loops.Expect);
        Assert.Equal("OK", exam.ExpectFor(loops)!["answer_contains"]!.GetValue<string>());
        var plain = exam.Stages.Single(c => c.Id == "plain");
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
        repo.EditJson("smoke/exam.json", e => e["fixture"] = "nope");
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "nope") && p.Message.Contains("no such fixture"));

        repo.EditJson("smoke/exam.json", e => e["fixture"] = "note");
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().Add("builds"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.correctness" && p.Message.Contains("fixture 'note'") && p.Message.Contains("declares it"));

        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().RemoveAt(2));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.checks.exit_ok" && p.Message.Contains("also declared by fixture"));

        repo.EditJson("../fixtures/note/fixture.json", f => { f.Remove("checks"); f["source"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/x\"}"); });
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "note", "fixture.json") && p.Field == "source.rev");
    }

    [Theory]
    [InlineData("smoke/exam.json", "grading.checks", "grading.checks.nothing")]
    [InlineData("smoke/exam.json", "grading.correctness", "grading.correctness")]
    [InlineData("smoke/exam.json", "grading.integrity", "grading.integrity")]
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
                case null when parts[^1] == "integrity": parent[parts[^1]] = new JsonArray((JsonNode?)null); break;
                case null: parent[parts[^1]] = new JsonObject { ["nothing"] = null }; break;
            }
        });
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith(Path.GetFileName(file)) && p.Field == field);
    }

    [Fact]
    public void AnExam_LoadsItsStagesInOrder_OnTheOneFixture()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");

        var exam = repo.LoadExam("smoke");
        Assert.All(exam.Stages, s => Assert.Equal("note", s.Fixture));
        Assert.Equal("The model answers in one turn with no tools", Exam.Describe(exam.Stages[0]));
        // A stage's expected values and the exam's checks reach it.
        Assert.Equal("hello", exam.ExpectFor(exam.Stages[0])!["answer_contains"]!.GetValue<string>());
        Assert.Contains("says-expected", exam.ChecksFor(exam.Stages[0]).Keys);

        // The order is exam.json's, not the ids'.
        repo.EditJson("smoke/exam.json", e => { var stages = e["stages"]!.AsArray(); var last = stages[3]!; stages.RemoveAt(3); stages.Insert(0, last); });
        Assert.Equal(["writes-a-file", "plain", "uses-bash", "loops"], repo.LoadExam("smoke").Stages.Select(s => s.Id));
    }

    [Fact]
    public void AnExam_IsOneJob_SoAnyStageChangesEveryStagesHash_AndTheStandardDoesNot()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var before = repo.LoadExam("smoke").StageHashes;
        Assert.Single(before.Values.Distinct());

        // What grades or labels a stage is not what the agent meets.
        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["about"] = "Another sentence");
        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["expect"] = JsonNode.Parse("""{"answer_contains": "world"}"""));
        repo.EditJson("smoke/exam.json", e => e["stages"]![0]!["checks"] = JsonNode.Parse("""{"own": {"exit_reason": "ok"}}"""));
        repo.EditJson("smoke/exam.json", e => e["grading"]!["checks"]!["exit_ok"]!["description"] = "the run ended on its own");
        repo.EditJson("smoke/exam.json", e => e["grading"]!["integrity"] = JsonNode.Parse("[]"));
        Assert.Equal(before, repo.LoadExam("smoke").StageHashes);

        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["prompt"] = "MOCK:response=goodbye");
        var changed = repo.LoadExam("smoke").StageHashes;
        Assert.Single(changed.Values.Distinct());
        Assert.NotEqual(before["loops"], changed["loops"]);

        // The order is part of the job: a stage meets what the stages before it left.
        repo.EditJson("smoke/exam.json", e => { var stages = e["stages"]!.AsArray(); var last = stages[2]!; stages.RemoveAt(2); stages.Insert(0, last); });
        Assert.NotEqual(changed["loops"], repo.LoadExam("smoke").StageHashes["loops"]);
    }

    [Theory]
    [InlineData("""{"id": "plain", "prompt": "x"}""", "stages[4].id")]
    [InlineData("""{"id": "Not_A_Segment", "prompt": "x"}""", "stages[4].id")]
    [InlineData("""{"id": "fourth"}""", "stages[4].prompt")]
    [InlineData("""{"id": "fourth", "prompt": "x", "fixture": "note"}""", "stages[4].fixture")]
    [InlineData("""{"id": "fourth", "prompt": "x", "labels": {"area": "docs"}}""", "stages[4].labels")]
    [InlineData("""{"id": "fourth", "prompt": "x", "description": "a sentence"}""", "stages[4].description")]
    [InlineData("""{"id": "fourth", "prompt": "x", "about": " "}""", "stages[4].about")]
    [InlineData("""{"id": "fourth", "prompt": "x", "checks": {"own": {"no_such_field": 1}}}""", "stages[4].checks.own.no_such_field")]
    [InlineData("""{"id": "fourth", "prompt": "x", "checks": {"exit_ok": {"exit_reason": "ok"}}}""", "stages[4].checks.exit_ok")]
    [InlineData("null", "stages[4]")]
    public void AStage_ProblemsNameExamJsonAndTheStage(string stage, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["stages"]!.AsArray().Add(JsonNode.Parse(stage)));
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("smoke/exam.json") && p.Field == expectedField);
    }

    [Fact]
    public void AnExam_NeedsAtLeastOneStage()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["stages"] = new JsonArray());
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "stages" && p.Message.Contains("at least one"));
    }

    [Fact]
    public void StageIds_ThatWillNotReadAsALabel_AreNamed_NeverRefused()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        Assert.Empty(repo.LoadExam("smoke").UnreadableStageIds());
        foreach (var id in new[] { "stage-12", "q3", "7", "how-long-clear-waits-with-no-terminal" })
            repo.EditJson("smoke/exam.json", e => e["stages"]!.AsArray().Add(new JsonObject { ["id"] = id, ["prompt"] = "x" }));
        Assert.Equal(["stage-12", "q3", "7", "how-long-clear-waits-with-no-terminal"], repo.LoadExam("smoke").UnreadableStageIds());
    }
}
