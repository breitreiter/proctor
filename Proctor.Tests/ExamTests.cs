using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class ExamTests
{
    [Fact]
    public void SmokeExam_Loads()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");

        Assert.Equal("smoke", exam.Id);
        Assert.Equal(2, exam.Variants.Count);
        Assert.Equal(3, exam.Tasks.Count);
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
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().Add("own"));
        repo.EditJson("smoke/tasks/plain.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"answer_contains\": \"hello\"}}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.correctness" && p.Message.Contains("task 'loops'"));

        repo.EditJson("smoke/tasks/loops.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/tasks/uses-bash.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"tools_used\": [\"bash\"]}}"));
        var exam = repo.LoadExam("smoke");
        Assert.Contains("own", exam.Grading.Correctness!);
        Assert.False(exam.CheckDescriptions().ContainsKey("own"));   // three tasks, three criteria: no one sentence is the check's
        Assert.Equal("the attempt ends with 'ok'", exam.ChecksFor(exam.Tasks.Single(t => t.Id == "loops")).Values.Select(k => Checks.Describe(k.Spec)).Last());
        Assert.All(exam.Tasks, t => Assert.Equal("task", exam.ChecksFor(t)["own"].Level));
    }

    [Fact]
    public void CheckDescriptions_KeepOneSentence_WhenEveryTaskAgrees()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        foreach (var task in new[] { "plain", "loops", "uses-bash" })
            repo.EditJson($"smoke/tasks/{task}.json", c => c["checks"] = JsonNode.Parse("{\"own\": {\"exit_reason\": \"ok\"}}"));
        Assert.Equal("the attempt ends with 'ok'", repo.LoadExam("smoke").CheckDescriptions()["own"]);
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
    [InlineData("grading", "{\"checks\": {\"x\": {\"judge\": {}}}, \"correctness\": [\"x\"]}", "smoke/exam.json", "grading.checks.x.judge")]
    [InlineData("variants", "[]", "smoke/exam.json", "variants")]
    [InlineData("attempts", "0", "smoke/exam.json", "attempts")]
    [InlineData("hooks", "{\"run\": {\"setup\": \"hooks/variant-setup.sh\"}}", "smoke/exam.json", "hooks.run")]
    [InlineData("hooks", "{\"task\": {\"setup\": \"hooks/variant-setup.sh\"}}", "smoke/exam.json", "hooks.task")]
    [InlineData("hooks", "{\"variant\": {\"setup\": \"hooks/missing.sh\"}}", "smoke/exam.json", "hooks.variant.setup")]
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

    [Theory]
    [InlineData("{\"id\": \"a\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\"}", "id")]
    [InlineData("{\"id\": \"b\", \"runner\": \"command\", \"harness\": \"codex\", \"command\": \"codex\"}", "runner")]
    [InlineData("{\"id\": \"b\", \"runner\": \"nb\", \"harness\": \"nb\"}", "provider")]
    [InlineData("{\"id\": \"b\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\", \"attempts\": 3}", "attempts")]
    [InlineData("{\"id\": \"b\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\", \"description\": \"x\"}", "description")]
    [InlineData("{\"id\": \"b\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\", \"note\": \" \"}", "note")]
    [InlineData("{\"id\": \"Bad Id\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\"}", "id")]
    [InlineData("null", "")]
    public void Variants_AreValidated_EachInItsOwnFile(string secondVariant, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Write("smoke/variants/b.json", secondVariant);

        Assert.Contains(repo.Problems("smoke"), p => p.File == "exams/smoke/variants/b.json" && p.Field == expectedField);
    }

    [Fact]
    public void VariantsInExamJson_AreRefused_SayingWhereTheyGo()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["variants"] = JsonNode.Parse("[{\"id\": \"a\", \"runner\": \"nb\", \"harness\": \"nb\", \"provider\": \"Mock\"}]"));

        var problem = Assert.Single(repo.Problems("smoke"));
        Assert.Equal(("exams/smoke/exam.json", "variants"), (problem.File, problem.Field));
        Assert.Contains("variants/<id>.json", problem.Message);
    }

    [Fact]
    public void AnExamWithNoVariantFiles_IsAProblem()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        Directory.Delete(Path.Combine(repo.Root, "exams", "smoke", "variants"), recursive: true);

        Assert.Contains(repo.Problems("smoke"), p => p.File == "exams/smoke/variants" && p.Message.StartsWith("no variants"));
    }

    [Fact]
    public void AVariantsHash_IsItsDeclaration_NotItsIdOrNote_AndTheExamHashLeavesItOut()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var before = repo.LoadExam("smoke");
        var a = before.Variants.Single(v => v.Id == "a").Hash;

        repo.EditVariant("smoke", "a", v => v["note"] = "the plain variant");
        Assert.Equal(a, repo.LoadExam("smoke").Variants.Single(v => v.Id == "a").Hash);
        repo.RenameVariant("smoke", "a", "renamed");
        Assert.Equal(a, repo.LoadExam("smoke").Variants.Single(v => v.Id == "renamed").Hash);

        repo.EditVariant("smoke", "renamed", v => v["model"] = "other");
        var changed = repo.LoadExam("smoke");
        Assert.NotEqual(a, changed.Variants.Single(v => v.Id == "renamed").Hash);
        Assert.Equal(before.Hash, changed.Hash);

        // b's bundle counts by content: a file changed in it is a changed variant.
        var b = before.Variants.Single(v => v.Id == "b").Hash;
        File.AppendAllText(Path.Combine(repo.Root, "bundles", "smoke", "extra.md"), "more\n");
        Assert.NotEqual(b, repo.LoadExam("smoke").Variants.Single(v => v.Id == "b").Hash);
    }

    [Fact]
    public void ATasksHash_CoversWhatTheAgentMeets_NotTheChecksOrTheOtherTasks()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var before = repo.LoadExam("smoke").TaskHashes;
        Assert.Equal(3, before.Values.Distinct().Count());

        repo.EditJson("smoke/tasks/plain.json", t => t["prompt"] = "Something else.");
        var after = repo.LoadExam("smoke").TaskHashes;
        Assert.NotEqual(before["plain"], after["plain"]);
        Assert.Equal(before["loops"], after["loops"]);

        // The checks and the expected values are the standard: changing them is a regrade, never a rerun.
        repo.EditJson("smoke/exam.json", e => e["grading"]!["checks"]!["exit_ok"]!["description"] = "the run ended on its own");
        repo.EditJson("smoke/exam.json", e => e["grading"]!["integrity"] = JsonNode.Parse("[]"));
        repo.EditJson("smoke/tasks/plain.json", t => t["expect"] = JsonNode.Parse("""{"answer": "another"}"""));
        Assert.Equal(after, repo.LoadExam("smoke").TaskHashes);
    }

    [Fact]
    public void Labels_MergeFixtureThenExamThenTask_AndReachEveryRow()
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
    public void Task_IdMustMatchFileName_AndPromptRequired()
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
    public void Fixtures_AreLoadedByName_AndTheirChecksAndExpectMergeIntoTheTask()
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
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().Add("builds"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.correctness" && p.Message.Contains("fixture 'note'") && p.Message.Contains("declares it"));

        repo.EditJson("../fixtures/note/fixture.json", f => f["checks"] = System.Text.Json.Nodes.JsonNode.Parse("{\"exit_ok\": {\"exit_reason\": \"ok\"}}"));
        repo.EditJson("smoke/exam.json", e => e["grading"]!["correctness"]!.AsArray().RemoveAt(2));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "grading.checks.exit_ok" && p.Message.Contains("also declared by fixture"));

        repo.EditJson("../fixtures/note/fixture.json", f => { f.Remove("checks"); f["source"] = System.Text.Json.Nodes.JsonNode.Parse("{\"git\": \"https://example.invalid/x\"}"); });
        Assert.Contains(repo.Problems("smoke"), p => p.File == Path.Combine("fixtures", "note", "fixture.json") && p.Field == "source.rev");
    }

    [Fact]
    public void Bundles_AreValidatedAgainstTheRepositoryRoot()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditVariant("smoke", "b", v => v["bundle"]!["path"] = "bundles/nope");
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "bundle.path");
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse("{\"git\": \"https://example.invalid/b\"}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "bundle.rev");
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse("{\"path\": \"bundles/smoke\", \"paths\": [\"README.md\"]}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "bundle.paths" && p.Message.StartsWith("goes with git"));
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse("{\"git\": \"https://example.invalid/b\", \"rev\": \"abc\", \"paths\": [\"../up\"]}"));
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "bundle.paths" && p.Message.Contains("without .."));
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse("{\"git\": \"https://example.invalid/b\", \"rev\": \"abc\", \"paths\": [\"docs/\", \"README.md\"]}"));
        Assert.Empty(repo.Problems("smoke"));
        repo.EditVariant("smoke", "b", v => v["bundle"] = JsonNode.Parse("{\"git\": \"https://example.invalid/b\", \"rev\": \"abc\"}"));
        Assert.Empty(repo.Problems("smoke"));
    }

    [Theory]
    [InlineData("smoke/exam.json", "grading.checks", "grading.checks.nothing")]
    [InlineData("smoke/exam.json", "grading.correctness", "grading.correctness")]
    [InlineData("smoke/exam.json", "grading.integrity", "grading.integrity")]
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
                case null when parts[^1] == "integrity": parent[parts[^1]] = new JsonArray((JsonNode?)null); break;
                case null: parent[parts[^1]] = new JsonObject { ["nothing"] = null }; break;
            }
        });
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith(Path.GetFileName(file)) && p.Field == field);
    }

    [Fact]
    public void AStagedExam_LoadsItsStagesInOrder_OnTheOneFixture()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");

        var exam = repo.LoadExam("smoke");
        Assert.True(exam.Staged);
        Assert.Equal(["loops", "plain", "uses-bash"], exam.Tasks.Select(s => s.Id));
        Assert.All(exam.Tasks, s => Assert.Equal("note", s.Fixture));
        Assert.Equal("The model answers in one turn with no tools", Exam.Describe(exam.Tasks[1]));
        // A stage's expected values and the exam's checks reach it as a task's do.
        Assert.Equal("hello", exam.ExpectFor(exam.Tasks[1])!["answer_contains"]!.GetValue<string>());
        Assert.Contains("says-expected", exam.ChecksFor(exam.Tasks[1]).Keys);

        // The order is exam.json's, not the ids'.
        repo.EditJson("smoke/exam.json", e => { var stages = e["stages"]!.AsArray(); var last = stages[2]!; stages.RemoveAt(2); stages.Insert(0, last); });
        Assert.Equal(["uses-bash", "loops", "plain"], repo.LoadExam("smoke").Tasks.Select(s => s.Id));
    }

    [Fact]
    public void AStagedExam_IsOneJob_SoAnyStageChangesEveryStagesHash_AndTheStandardDoesNot()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        var before = repo.LoadExam("smoke").TaskHashes;
        Assert.Single(before.Values.Distinct());

        // What grades or labels a stage is not what the agent meets.
        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["about"] = "Another sentence");
        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["expect"] = JsonNode.Parse("""{"answer_contains": "world"}"""));
        repo.EditJson("smoke/exam.json", e => e["stages"]![0]!["checks"] = JsonNode.Parse("""{"own": {"exit_reason": "ok"}}"""));
        Assert.Equal(before, repo.LoadExam("smoke").TaskHashes);

        repo.EditJson("smoke/exam.json", e => e["stages"]![1]!["prompt"] = "MOCK:response=goodbye");
        var changed = repo.LoadExam("smoke").TaskHashes;
        Assert.Single(changed.Values.Distinct());
        Assert.NotEqual(before["loops"], changed["loops"]);

        // The order is part of the job: a stage meets what the stages before it left.
        repo.EditJson("smoke/exam.json", e => { var stages = e["stages"]!.AsArray(); var last = stages[2]!; stages.RemoveAt(2); stages.Insert(0, last); });
        Assert.NotEqual(changed["loops"], repo.LoadExam("smoke").TaskHashes["loops"]);
    }

    [Fact]
    public void ATaskExamsHashes_AreWhatTheyWere()
    {
        // Stages share the hashing code; a banked attempt of a task exam must not go stale because of it.
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        var exam = repo.LoadExam("smoke");
        Assert.False(exam.Staged);
        Assert.Equal(3, exam.TaskHashes.Values.Distinct().Count());
    }

    [Theory]
    [InlineData("""{"id": "plain", "prompt": "x"}""", "stages[3].id")]
    [InlineData("""{"id": "Not_A_Segment", "prompt": "x"}""", "stages[3].id")]
    [InlineData("""{"id": "fourth"}""", "stages[3].prompt")]
    [InlineData("""{"id": "fourth", "prompt": "x", "fixture": "note"}""", "stages[3].fixture")]
    [InlineData("""{"id": "fourth", "prompt": "x", "labels": {"area": "docs"}}""", "stages[3].labels")]
    [InlineData("""{"id": "fourth", "prompt": "x", "description": "a sentence"}""", "stages[3].description")]
    [InlineData("""{"id": "fourth", "prompt": "x", "about": " "}""", "stages[3].about")]
    [InlineData("""{"id": "fourth", "prompt": "x", "checks": {"own": {"no_such_field": 1}}}""", "stages[3].checks.own.no_such_field")]
    [InlineData("""{"id": "fourth", "prompt": "x", "checks": {"exit_ok": {"exit_reason": "ok"}}}""", "stages[3].checks.exit_ok")]
    [InlineData("null", "stages[3]")]
    public void AStage_ProblemsNameExamJsonAndTheStage(string stage, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        repo.EditJson("smoke/exam.json", e => e["stages"]!.AsArray().Add(JsonNode.Parse(stage)));
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("smoke/exam.json") && p.Field == expectedField);
    }

    [Fact]
    public void AnExam_IsTasksOrStages_NotBoth_AndAFixtureOfItsOwnGoesWithStages()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => e["fixture"] = "note");
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("smoke/exam.json") && p.Field == "fixture");

        repo.EditJson("smoke/exam.json", e => e["stages"] = JsonNode.Parse("""[{"id": "only", "prompt": "x"}]"""));
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("smoke/exam.json") && p.Field == "stages");

        repo.EditJson("smoke/exam.json", e => e["stages"] = new JsonArray());
        Assert.Contains(repo.Problems("smoke"), p => p.Field == "stages" && p.Message.Contains("at least one"));
    }

    [Fact]
    public void StageIds_ThatWillNotReadAsALabel_AreNamed_NeverRefused()
    {
        using var repo = new TestRepo();
        repo.CopyExam("smoke");
        repo.Stage("smoke");
        Assert.Empty(repo.LoadExam("smoke").UnreadableStageIds());
        foreach (var id in new[] { "stage-12", "q3", "7", "how-long-clear-waits-with-no-terminal" })
            repo.EditJson("smoke/exam.json", e => e["stages"]!.AsArray().Add(new JsonObject { ["id"] = id, ["prompt"] = "x" }));
        Assert.Equal(["stage-12", "q3", "7", "how-long-clear-waits-with-no-terminal"], repo.LoadExam("smoke").UnreadableStageIds());
    }
}
