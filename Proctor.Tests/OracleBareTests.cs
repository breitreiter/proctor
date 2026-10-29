using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

// The oracle on the bare path (plans/oracle-in-proctor.md, work item 2): proctor prepends nb's own directives and nb's
// in-process oracle answers. One cell per ending, on nb's Mock provider, whose verdict rides on the scripted reply.
public class OracleBareTests
{
    // An answer that is the subject's next scripted turn too, as nb's evals/fixtures/oracle-sheet.md chains them.
    const string Sheet = """
        # Answers

        ## deploy-target
        MOCK:response=Deploying to staging. MOCK:oracle=DONE

        ## again
        MOCK:response=And again? MOCK:oracle=again
        """;

    static (TestRepo Repo, string CellDir) RunOne(string prompt, object? oracle = null)
    {
        var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.Write("smoke/oracle/plain.md", Sheet);
        foreach (var other in new[] { "loops", "uses-bash" }) File.Delete(Path.Combine(repo.Root, "suites", "smoke", "tasks", other + ".json"));
        repo.EditJson("smoke/tasks/plain.json", t => { t["prompt"] = prompt; t["oracle"] = "oracle/plain.md"; });
        repo.EditJson("smoke/suite.json", s =>
        {
            s["arms"]!.AsArray().RemoveAt(1);
            s["arms"]![0]!["samples"] = 1;
            if (oracle is not null) s["oracle"] = System.Text.Json.JsonSerializer.SerializeToNode(oracle);
        });
        var suite = repo.LoadSuite("smoke");
        var id = Runner.Start(repo.Root, suite, Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        return (repo, Layout.Cell(Layout.Experiment(repo.Root, id), "a", "plain", 1));
    }

    static Verdict Check(string cellDir, string spec)
    {
        var task = new TaskDef("plain", null, "p", null, Oracle: "oracle/plain.md");
        return Checks.Evaluate(JsonNode.Parse(spec)!.AsObject(), new CellContext("/nonexistent", cellDir, "/work", "exp", "a", task, 1), Transcript.Read(cellDir));
    }

    [Fact]
    public void AHit_IsAnsweredFromTheSheet_AndTheRunGoesOn()
    {
        var (repo, cell) = RunOne("MOCK:response=Which environment should I deploy to? MOCK:oracle=deploy-target");
        using var _ = repo;
        var t = Transcript.Read(cell);

        Assert.Equal("ok", t.Trailer!.ExitReason);
        var answer = Assert.Single(t.UserTurns, u => u.IsOracle);
        Assert.Equal(["deploy-target"], answer.Keys);
        Assert.Equal("Deploying to staging.", t.Answer!.Replace("MOCK:oracle=DONE", "").Trim());
        Assert.Equal(1, t.Trailer.OracleTurns);
        Assert.Equal(Verdict.Pass, Check(cell, """{"oracle_hit": ["deploy-target"]}""").Result);
        Assert.Equal(Verdict.Fail, Check(cell, """{"oracle_hit": ["deploy-target", "region"]}""").Result);
        Assert.Equal(Verdict.Pass, Check(cell, """{"oracle_turns": {"max": 1}}""").Result);
        Assert.Equal(Verdict.Fail, Check(cell, """{"oracle_turns": {"max": 0}}""").Result);
        Assert.Equal(Verdict.Pass, Check(cell, """{"oracle_misses": {"max": 0}}""").Result);
    }

    [Fact]
    public void AMiss_EndsTheRun_AsOracleMiss()
    {
        var (repo, cell) = RunOne("MOCK:response=What colour should the button be? MOCK:oracle=MISS");
        using var _ = repo;
        var t = Transcript.Read(cell);

        Assert.Equal("oracle_miss", t.Trailer!.ExitReason);
        Assert.DoesNotContain(t.UserTurns, u => u.IsOracle);
        Assert.Equal(Verdict.Fail, Check(cell, """{"oracle_misses": {"max": 0}}""").Result);
        Assert.Equal(Verdict.Fail, Check(cell, """{"oracle_hit": ["deploy-target"]}""").Result);
    }

    [Fact]
    public void TheSuitesTurns_BoundTheAnswers_AndTheJudgeIsTheNamedEntry()
    {
        var (repo, cell) = RunOne("MOCK:response=Again? MOCK:oracle=again", new { turns = 2, judge = "MockB" });
        using var _ = repo;
        var t = Transcript.Read(cell);

        Assert.Equal("oracle_budget", t.Trailer!.ExitReason);
        Assert.Equal(2, t.UserTurns.Count(u => u.IsOracle));
        var program = File.ReadAllText(Path.Combine(cell, Layout.ProgramFile));
        Assert.StartsWith($"oracle @{Path.Combine(repo.Root, "suites", "smoke", "oracle", "plain.md")}\noracle provider MockB\nbudget oracle_turns 2\n", program);
        // The compiled program carries the sheet's text, not its path: a runner's container never needs the file.
        var compiled = File.ReadAllText(Path.Combine(cell, Layout.CompiledProgramFile));
        Assert.Contains("deploy-target", compiled);
        Assert.DoesNotContain("plain.md", compiled);
    }

    [Fact]
    public void AnOracleCheck_WithNoOracleTask_IsAProblem()
    {
        using var repo = new TestRepo();
        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", s => s["grading"]!["checks"]!["answered"] = JsonNode.Parse("""{"oracle_turns": {"max": 1}}"""));
        Assert.Contains(repo.Problems("smoke"), p => p.File.EndsWith("suite.json") && p.Field == "grading.checks.answered" && p.Message.Contains("no task names an answer sheet"));
    }

    [Fact]
    public void OracleHit_OnATaskWithNoSheet_IsAnError_NotAFail()
    {
        var t = Transcript.Parse("""{"type":"result","exit_reason":"ok"}""" + "\n", null);
        var cell = new CellContext("/nonexistent", Path.GetTempPath(), "/work", "exp", "a", new TaskDef("c", null, "p", null), 1);
        Assert.Equal(Verdict.Error, Checks.Evaluate(JsonNode.Parse("""{"oracle_hit": ["x"]}""")!.AsObject(), cell, t).Result);
    }

    [Theory]
    [InlineData("""{"oracle_hit": ["deploy-target", "region"]}""", "the oracle answers with deploy-target, region")]
    [InlineData("""{"oracle_turns": {"max": 0}}""", "no oracle answers")]
    [InlineData("""{"oracle_misses": {"max": 0}}""", "the agent asks nothing the answer sheet lacks")]
    public void OracleChecks_DescribeThemselves(string spec, string description) =>
        Assert.Equal(description, Checks.Describe(JsonNode.Parse(spec)!.AsObject()));

    [Theory]
    [InlineData("""{"oracle_hit": []}""")]
    [InlineData("""{"oracle_hit": "deploy-target"}""")]
    [InlineData("""{"oracle_turns": 2}""")]
    [InlineData("""{"oracle_misses": {}}""")]
    [InlineData("""{"denied_calls": 2}""")]   // a bare number threw from validation before the oracle checks shared this rule
    public void OracleChecks_AreShapeChecked(string spec) =>
        Assert.NotEmpty(Checks.ValidateCheck(JsonNode.Parse(spec)!.AsObject(), "/nonexistent"));
}
