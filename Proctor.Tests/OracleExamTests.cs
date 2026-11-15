using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

// A task's answer sheet and the exam's oracle block, as loaded and validated (plans/oracle-in-proctor.md, work item 1).
public class OracleExamTests
{
    const string Sheet = "# Answers\n\n## deploy-target\nStaging only.\n";

    /// <summary>The gate exam with its plain task naming a sheet: one bare nb variant and one through Harbor.</summary>
    static TestRepo GateWithOracle(string sheet = Sheet, string path = "oracle/plain.md")
    {
        var repo = new TestRepo();
        repo.CopyExam("gate");
        repo.Write("gate/oracle/plain.md", sheet);
        repo.EditJson("gate/tasks/plain.json", t => t["oracle"] = path);
        return repo;
    }

    [Fact]
    public void AnOracleTask_Loads_WithItsSheetOnTheHost_AndNbsDefaultTurns()
    {
        using var repo = GateWithOracle();
        var exam = repo.LoadExam("gate");
        var plain = exam.Tasks.Single(t => t.Id == "plain");

        Assert.Equal(Path.Combine(exam.Dir, "oracle", "plain.md"), exam.OracleOf(plain));
        Assert.Null(exam.OracleOf(exam.Tasks.Single(t => t.Id == "writes")));
        Assert.Equal(8, exam.OracleTurns);
    }

    [Fact]
    public void TheOracleBlock_SetsTurns_AndTheJudge()
    {
        using var repo = GateWithOracle();
        repo.EditJson("gate/exam.json", s => s["oracle"] = JsonNode.Parse("""{"judge": "Judge", "turns": 3}"""));
        var exam = repo.LoadExam("gate");

        Assert.Equal(3, exam.OracleTurns);
        Assert.Equal("Judge", exam.Def.Oracle!.Judge);
    }

    [Fact]
    public void TheSheet_IsPartOfTheExamHash()
    {
        using var repo = GateWithOracle();
        var before = repo.LoadExam("gate").Hash;
        repo.Write("gate/oracle/plain.md", Sheet + "\n## region\nus-east.\n");
        Assert.NotEqual(before, repo.LoadExam("gate").Hash);
    }

    [Theory]
    [InlineData("../../fixtures/note/repo/note.txt", "a path inside the exam directory")]
    [InlineData("oracle/missing.md", "answer sheet not found: oracle/missing.md")]
    public void TheSheet_MustBeInsideTheExam_AndExist(string path, string message)
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
        repo.CopyExam("gate");
        repo.EditJson("gate/exam.json", s => s["oracle"] = JsonNode.Parse("""{"turns": 0, "judge": ""}"""));
        var problems = repo.Problems("gate");

        Assert.Contains(problems, p => p.Field == "oracle" && p.Message.Contains("no task names an answer sheet"));
        Assert.Contains(problems, p => p.Field == "oracle.turns");
        Assert.Contains(problems, p => p.Field == "oracle.judge");
    }

    // Decided 2026-10-29: bridged, a vendor agent leaves no ATIF, so an oracle runs on nb variants only.
    [Fact]
    public void AVendorVariant_IsRefused_WhenATaskHasAnOracle()
    {
        using var repo = GateWithOracle();
        repo.AddVariant("gate", """{"id": "cc", "agent": "claude-code", "model": "claude-haiku-5-5", "agent_version": "2.1.290"}""");
        var problem = Assert.Single(repo.Problems("gate"));

        Assert.Equal(("exams/gate/exam.json", "variants"), (problem.File, problem.Field));
        Assert.StartsWith("not yet: variant 'cc' is claude-code, and task plain names an answer sheet", problem.Message);
    }

    [Theory]
    [InlineData("oracle @answers.md", "the exam's oracle block sets the oracle")]
    [InlineData("budget oracle_turns 2", "set oracle.turns in exam.json")]
    public void TheTemplate_MayNotStateTheOracle_WhenATaskHasOne(string directive, string message)
    {
        using var repo = GateWithOracle();
        var template = File.ReadAllText(Path.Combine(repo.Root, "exams", "gate", "program.nb"));
        repo.Write("gate/program.nb", directive + "\n" + template);
        Assert.Contains(repo.Problems("gate"), p => p.File.EndsWith("program.nb") && p.Message.Contains(message));
    }

    [Theory]
    [InlineData("provider {{provider}}\nrun Task: {{prompt}}\n")]
    [InlineData("provider {{provider}}\nrun {{prompt}}\nrun and again\n")]
    [InlineData("run {{prompt}}\nprovider {{provider}}\n")]
    public void OnAnNbAgentVariant_TheTemplateMustEndInItsOneRun(string template)
    {
        using var repo = GateWithOracle();
        repo.Write("gate/program.nb", template);
        Assert.Contains(repo.Problems("gate"), p => p.File.EndsWith("program.nb") && p.Message.Contains("exactly `run {{prompt}}`"));
    }

    [Fact]
    public void TheRunRule_IsTheBridgesOnly_AndIgnoresCommentsAndContinuations()
    {
        // Bare variants only: the program runs the prompt itself, so any run is fine.
        using var bare = GateWithOracle();
        bare.RemoveVariant("gate", "nb-harbor");
        bare.EditJson("gate/exam.json", s =>
        {
            s.Remove("harbor");
            s.Remove("limits");
        });
        bare.Write("gate/program.nb", "provider {{provider}}\nrun Task: {{prompt}}\n");
        Assert.Empty(bare.Problems("gate"));

        using var bridged = GateWithOracle();
        bridged.Write("gate/program.nb", "# a comment\nprovider {{provider}}\nsystem one \\\nrun two\n\nrun {{prompt}}\n# trailing\n");
        Assert.Empty(bridged.Problems("gate"));
    }
}
