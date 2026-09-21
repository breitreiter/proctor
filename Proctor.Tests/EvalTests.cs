using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

public class EvalTests
{
    [Fact]
    public void SmokeEval_Loads()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        var eval = repo.LoadEval("smoke");

        Assert.Equal("smoke", eval.Id);
        Assert.Equal(2, eval.Arms.Count);
        Assert.Equal(3, eval.Cases.Count);
        Assert.Equal(12, eval.PlannedCells);
        Assert.Equal(["exit_ok", "says-expected"], eval.Grading.Pass);
        Assert.StartsWith("sha256:", eval.Hash);
        Assert.Equal(eval.Hash, repo.LoadEval("smoke").Hash);
    }

    [Fact]
    public void Hash_ChangesWhenACaseChanges()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        var before = repo.LoadEval("smoke").Hash;
        repo.EditJson("smoke/cases/plain.json", c => c["prompt"] = "MOCK:response=goodbye");
        Assert.NotEqual(before, repo.LoadEval("smoke").Hash);
    }

    [Theory]
    [InlineData("id", "\"wrong\"", "smoke/eval.json", "id")]
    [InlineData("tags", "[\"nightly\"]", "smoke/eval.json", "tags")]
    [InlineData("arms", "[]", "smoke/eval.json", "arms")]
    [InlineData("hooks", "{\"run\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/eval.json", "hooks.run")]
    [InlineData("hooks", "{\"case\": {\"setup\": \"hooks/arm-setup.sh\"}}", "smoke/eval.json", "hooks.case")]
    [InlineData("hooks", "{\"arm\": {\"setup\": \"hooks/missing.sh\"}}", "smoke/eval.json", "hooks.arm.setup")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"y\"]}", "smoke/eval.json", "grading.pass")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"y\"]}", "smoke/eval.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"exit_reason\": \"ok\"}}, \"pass\": [\"x\"], \"validity\": [\"x\"]}", "smoke/eval.json", "grading.validity")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"bogus\": 1}}, \"pass\": [\"x\"]}", "smoke/eval.json", "grading.checks.x.bogus")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"oracle_hit\": [\"k\"]}}, \"pass\": [\"x\"]}", "smoke/eval.json", "grading.checks.x.oracle_hit")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"files_touched\": {\"paths\": []}}}, \"pass\": [\"x\"]}", "smoke/eval.json", "grading.checks.x.files_touched")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"script\": \"checks/nope.sh\"}}, \"pass\": [\"x\"]}", "smoke/eval.json", "grading.checks.x.script")]
    [InlineData("grading", "{\"checks\": {\"x\": {\"not_script\": \"checks/answer-nonempty.sh\"}}, \"pass\": [\"x\"]}", "smoke/eval.json", "grading.checks.x.not_script")]
    public void EvalJson_ProblemsNameFileAndField(string field, string json, string file, string expectedField)
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.EditJson("smoke/eval.json", e => e[field] = JsonNode.Parse(json));

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
        repo.CopyEval("smoke");
        repo.EditJson("smoke/eval.json", e => e["arms"]![1] = JsonNode.Parse(secondArm));

        Assert.Contains(repo.Problems("smoke"), p => p.Field == expectedField);
    }

    [Fact]
    public void Case_IdMustMatchFileName_AndPromptRequired()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/cases/renamed.json", "{\"id\": \"other\"}");

        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.File.EndsWith("cases/renamed.json") && p.Field == "id");
        Assert.Contains(problems, p => p.File.EndsWith("cases/renamed.json") && p.Field == "prompt");
    }

    [Fact]
    public void Template_UnknownPlaceholderIsReported()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/program.nb", "provider {{provider}}\nrun {{question}}\n");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("program.nb") && p.Field == "{{question}}");
    }

    [Fact]
    public void MalformedJson_IsAProblemNotACrash()
    {
        using var repo = new TestRepo();
        repo.CopyEval("smoke");
        repo.Write("smoke/cases/broken.json", "{ not json");

        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("cases/broken.json"));
    }
}
