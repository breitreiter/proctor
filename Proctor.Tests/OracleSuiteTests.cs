using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

// A task's answer sheet and the suite's oracle block, as loaded and validated (plans/oracle-in-proctor.md, work item 1).
public class OracleSuiteTests
{
    const string Sheet = "# Answers\n\n## deploy-target\nStaging only.\n";

    /// <summary>The gate suite with its plain task naming a sheet: one bare nb arm and one through Harbor.</summary>
    static TestRepo GateWithOracle(string sheet = Sheet, string path = "oracle/plain.md")
    {
        var repo = new TestRepo();
        repo.CopySuite("gate");
        repo.Write("gate/oracle/plain.md", sheet);
        repo.EditJson("gate/tasks/plain.json", t => t["oracle"] = path);
        return repo;
    }

    [Fact]
    public void AnOracleTask_Loads_WithItsSheetOnTheHost_AndNbsDefaultTurns()
    {
        using var repo = GateWithOracle();
        var suite = repo.LoadSuite("gate");
        var plain = suite.Tasks.Single(t => t.Id == "plain");

        Assert.Equal(Path.Combine(suite.Dir, "oracle", "plain.md"), suite.OracleOf(plain));
        Assert.Null(suite.OracleOf(suite.Tasks.Single(t => t.Id == "writes")));
        Assert.Equal(8, suite.OracleTurns);
    }

    [Fact]
    public void TheOracleBlock_SetsTurns_AndTheJudge()
    {
        using var repo = GateWithOracle();
        repo.EditJson("gate/suite.json", s => s["oracle"] = JsonNode.Parse("""{"judge": "Judge", "turns": 3}"""));
        var suite = repo.LoadSuite("gate");

        Assert.Equal(3, suite.OracleTurns);
        Assert.Equal("Judge", suite.Def.Oracle!.Judge);
    }

    [Fact]
    public void TheSheet_IsPartOfTheSuiteHash()
    {
        using var repo = GateWithOracle();
        var before = repo.LoadSuite("gate").Hash;
        repo.Write("gate/oracle/plain.md", Sheet + "\n## region\nus-east.\n");
        Assert.NotEqual(before, repo.LoadSuite("gate").Hash);
    }

    [Theory]
    [InlineData("../../fixtures/note/repo/note.txt", "a path inside the suite directory")]
    [InlineData("oracle/missing.md", "answer sheet not found: oracle/missing.md")]
    public void TheSheet_MustBeInsideTheSuite_AndExist(string path, string message)
    {
        using var repo = GateWithOracle(path: path);
        Assert.Contains(repo.Problems("gate"), p => p.File.EndsWith("plain.json") && p.Field == "oracle" && p.Message.Contains(message));
    }

    [Fact]
    public void ASheetWithNoHeadedEntry_IsAProblem()
    {
        using var repo = GateWithOracle(sheet: "Staging only.\n");
        Assert.Contains(repo.Problems("gate"), p => p.Field == "oracle" && p.Message.Contains("no headed entries"));
    }

    [Fact]
    public void TheOracleBlock_NeedsATaskToServe_AndAtLeastOneTurn()
    {
        using var repo = new TestRepo();
        repo.CopySuite("gate");
        repo.EditJson("gate/suite.json", s => s["oracle"] = JsonNode.Parse("""{"turns": 0, "judge": ""}"""));
        var problems = repo.Problems("gate");

        Assert.Contains(problems, p => p.Field == "oracle" && p.Message.Contains("no task names an answer sheet"));
        Assert.Contains(problems, p => p.Field == "oracle.turns");
        Assert.Contains(problems, p => p.Field == "oracle.judge");
    }

    // Decided 2026-10-29: bridged, a vendor agent leaves no ATIF, so an oracle runs on nb arms only.
    [Fact]
    public void AVendorArm_IsRefused_WhenATaskHasAnOracle()
    {
        using var repo = GateWithOracle();
        repo.EditJson("gate/suite.json", s => s["arms"]!.AsArray().Add(JsonNode.Parse(
            """{"id": "cc", "agent": "claude-code", "model": "claude-haiku-5-5", "agent_version": "2.1.290"}""")));
        var problem = Assert.Single(repo.Problems("gate"));

        Assert.Equal("arms[2].agent", problem.Field);
        Assert.StartsWith("not yet: task plain names an answer sheet", problem.Message);
    }

    [Theory]
    [InlineData("oracle @answers.md", "the suite's oracle block sets the oracle")]
    [InlineData("budget oracle_turns 2", "set oracle.turns in suite.json")]
    public void TheTemplate_MayNotStateTheOracle_WhenATaskHasOne(string directive, string message)
    {
        using var repo = GateWithOracle();
        var template = File.ReadAllText(Path.Combine(repo.Root, "suites", "gate", "program.nb"));
        repo.Write("gate/program.nb", directive + "\n" + template);
        Assert.Contains(repo.Problems("gate"), p => p.File.EndsWith("program.nb") && p.Message.Contains(message));
    }

    [Theory]
    [InlineData("provider {{provider}}\nrun Task: {{prompt}}\n")]
    [InlineData("provider {{provider}}\nrun {{prompt}}\nrun and again\n")]
    [InlineData("run {{prompt}}\nprovider {{provider}}\n")]
    public void OnAnNbAgentArm_TheTemplateMustEndInItsOneRun(string template)
    {
        using var repo = GateWithOracle();
        repo.Write("gate/program.nb", template);
        Assert.Contains(repo.Problems("gate"), p => p.File.EndsWith("program.nb") && p.Message.Contains("exactly `run {{prompt}}`"));
    }

    [Fact]
    public void TheRunRule_IsTheBridgesOnly_AndIgnoresCommentsAndContinuations()
    {
        // Bare arms only: the program runs the prompt itself, so any run is fine.
        using var bare = GateWithOracle();
        bare.EditJson("gate/suite.json", s =>
        {
            s["arms"]!.AsArray().RemoveAt(1);
            s.Remove("harbor");
            s.Remove("limits");
        });
        bare.Write("gate/program.nb", "provider {{provider}}\nrun Task: {{prompt}}\n");
        Assert.Empty(bare.Problems("gate"));

        using var bridged = GateWithOracle();
        bridged.Write("gate/program.nb", "# a comment\nprovider {{provider}}\nsystem one \\\nrun two\n\nrun {{prompt}}\n# trailing\n");
        Assert.Empty(bridged.Problems("gate"));
    }

    // Until the oracle's run paths land, a suite that names a sheet loads but does not run without its oracle.
    [Fact]
    public void Running_AnOracleSuite_IsNotYet()
    {
        using var repo = GateWithOracle();
        var suite = repo.LoadSuite("gate");
        var config = Suite.LoadConfig(repo.Root, new List<Problem>());

        var e = Assert.Throws<ProctorException>(() => Runner.Start(repo.Root, suite, config, null, null, "test", TextWriter.Null));
        Assert.StartsWith("not yet: task plain names an answer sheet", e.Message);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "runs")));
    }
}
