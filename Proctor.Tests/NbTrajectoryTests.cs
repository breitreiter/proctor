using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>
/// nb's own ATIF (--trajectory) reads as the same windows as the JSONL of the same run, for every pair in
/// fixtures/nb-run (captured by its capture.sh). The bare path grades the JSONL and the Harbor path the trajectory,
/// so this is what keeps the two paths grading alike.
/// </summary>
public class NbTrajectoryTests
{
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "nb-run");

    public static TheoryData<string> Pairs() => new(Directory.GetFiles(Dir, "*.jsonl").Select(Path.GetFileNameWithoutExtension)!);

    static JsonObject Trajectory(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, name + ".trajectory.json")))!.AsObject();

    /// <summary>The ATIF reading, as an nb cell in Harbor is graded: the trajectory with proctor's counted core.</summary>
    static Transcript ThroughAtif(JsonObject trajectory)
    {
        var exit = trajectory["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok";
        var (core, unmapped) = Ingest.Count(trajectory, Arm.Nb, exit);
        Assert.Null(unmapped);
        return Transcript.FromAtif(trajectory, core, "");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryWindow_ReadsTheSame_AsTheRunsJsonl(string name)
    {
        var bare = Transcript.Parse(File.ReadAllText(Path.Combine(Dir, name + ".jsonl")), "");
        var atif = ThroughAtif(Trajectory(name));
        Assert.Equal(bare.Answer, atif.Answer);
        Assert.Equal(bare.AnswerJson?.ToJsonString(), atif.AnswerJson?.ToJsonString());
        Assert.Equal(bare.ToolCalls.Select(c => (c.Id, c.Name, c.Arguments?.ToJsonString(), c.Approved, c.ApprovalReason)),
            atif.ToolCalls.Select(c => (c.Id, c.Name, c.Arguments?.ToJsonString(), c.Approved, c.ApprovalReason)));
        Assert.Equal(bare.ToolResults.Select(r => (r.Id, r.Output, r.Result?.ToJsonString(), r.IsError)),
            atif.ToolResults.Select(r => (r.Id, r.Output, r.Result?.ToJsonString(), r.IsError)));
        Assert.Equal(bare.UserTurns, atif.UserTurns);
        Assert.Equal(bare.LoopNudged, atif.LoopNudged);
        var (b, a) = (bare.Trailer!, atif.Trailer!);
        Assert.Equal((b.ExitReason, b.Input, b.Output, b.Total, b.Estimated, b.ToolCalls, b.Provider, b.Denied, b.OracleTurns),
            (a.ExitReason, a.Input, a.Output, a.Total, a.Estimated, a.ToolCalls, a.Provider, a.Denied, a.OracleTurns));
    }

    [Fact]
    public void Steps_AreOneAgentStepPerTurn_WithItsCallsResultsAndMetrics()
    {
        var trajectory = Trajectory("loop-nudged");
        var steps = trajectory["steps"]!.AsArray();
        Assert.Equal("ATIF-v1.7", trajectory["schema_version"]!.GetValue<string>());
        Assert.Contains(steps, s => s!["message"]!.GetValue<string>().StartsWith("<system_reminder>"));
        // The Mock reuses one call id every turn; each result stays with its own turn's call.
        Assert.All(steps.Where(s => s!["tool_calls"] is not null), s => Assert.Single(s!["observation"]!["results"]!.AsArray()));
        // Every model call is measured; nb's own "reached the maximum" note is a step with no model call behind it.
        var agent = steps.Where(s => s!["source"]!.GetValue<string>() == "agent").ToList();
        Assert.All(agent.Where(s => s!["llm_call_count"] is not null), s => Assert.NotNull(s!["metrics"]?["prompt_tokens"]));
        Assert.Single(agent, s => s!["llm_call_count"] is null);
        Assert.Equal("max_tool_calls", trajectory["extra"]!["result"]!["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void InjectedUserTurn_KeepsItsSourceAndKeys()
    {
        var oracle = Trajectory("oracle")["steps"]!.AsArray().Single(s => s!["extra"]?["source"] is not null)!;
        Assert.Equal("{\"source\":\"oracle\",\"keys\":[\"deploy-target\"]}", oracle["extra"]!.ToJsonString());
        Assert.Equal("oracle", ThroughAtif(Trajectory("oracle")).UserTurns.Single(u => u.Source is not null).Source);
    }

    [Fact]
    public void CountedCore_CountsTurnsCallsDenialsAndKinds()
    {
        var (core, unmapped) = Ingest.Count(Trajectory("tool-denied"), Arm.Nb, "ok");
        Assert.Null(unmapped);
        Assert.Equal(1, core!.ToolCalls);
        Assert.Equal(1, core.Denied);
        Assert.Equal(new Dictionary<string, int> { ["shell"] = 1 }, core.Kinds);
    }

    [Fact]
    public void Unmapped_AnUnknownTool_IsNeverDropped()
    {
        var trajectory = new JsonObject
        {
            ["steps"] = new JsonArray(new JsonObject
            {
                ["step_id"] = 1, ["source"] = "agent", ["message"] = "",
                ["tool_calls"] = new JsonArray(new JsonObject { ["tool_call_id"] = "c", ["function_name"] = "figma_get", ["arguments"] = new JsonObject() }),
            }),
        };
        Assert.Equal("tool 'figma_get' has no kind for nb", Ingest.Count(trajectory, Arm.Nb, "ok").Unmapped);
    }
}
