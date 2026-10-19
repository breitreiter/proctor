using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>nb's JSONL converted to ATIF reads as the same windows as nb's JSONL read directly, for every captured Mock transcript.</summary>
public class NbAtifTests
{
    public static TheoryData<string> Captured() => new(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.jsonl").Select(Path.GetFileNameWithoutExtension)!);

    static string Jsonl(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl"));

    /// <summary>The ATIF reading, as an nb cell in Harbor is graded: the converted trajectory with proctor's counted core as the trailer.</summary>
    static Transcript ThroughAtif(string jsonl, string diff = "")
    {
        var (trajectory, unmapped) = NbAtif.Convert(jsonl, "0.9.0");
        Assert.Null(unmapped);
        var exit = trajectory!["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok";
        var (core, coreUnmapped) = Ingest.Count(trajectory, Arm.Nb, exit);
        Assert.Null(coreUnmapped);
        return Transcript.FromAtif(trajectory, core, diff);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void EveryWindow_ReadsTheSame_AsNbsJsonl(string name)
    {
        var bare = Transcript.Parse(Jsonl(name), "");
        var atif = ThroughAtif(Jsonl(name));
        Assert.Equal(bare.Answer, atif.Answer);
        Assert.Equal(bare.AnswerJson?.ToJsonString(), atif.AnswerJson?.ToJsonString());
        Assert.Equal(bare.ToolCalls.Select(c => (c.Id, c.Name, c.Arguments?.ToJsonString(), c.Approved, c.ApprovalReason)),
            atif.ToolCalls.Select(c => (c.Id, c.Name, c.Arguments?.ToJsonString(), c.Approved, c.ApprovalReason)));
        Assert.Equal(bare.ToolResults.Select(r => (r.Id, r.Output, r.Result?.ToJsonString(), r.IsError)),
            atif.ToolResults.Select(r => (r.Id, r.Output, r.Result?.ToJsonString(), r.IsError)));
        Assert.Equal(bare.UserTurns, atif.UserTurns);
        Assert.Equal(bare.LoopNudged, atif.LoopNudged);
        if (bare.Trailer is null) return;
        var (b, a) = (bare.Trailer, atif.Trailer!);
        Assert.Equal((b.ExitReason, b.Input, b.Output, b.Total, b.Estimated, b.ToolCalls, b.Provider, b.Denied, b.OracleTurns),
            (a.ExitReason, a.Input, a.Output, a.Total, a.Estimated, a.ToolCalls, a.Provider, a.Denied, a.OracleTurns));
    }

    [Fact]
    public void Steps_AreOneAgentStepPerTurn_WithItsCallsAndResults()
    {
        var (trajectory, _) = NbAtif.Convert(Jsonl("loop-nudged"), "0.9.0");
        var steps = trajectory!["steps"]!.AsArray();
        Assert.Equal(["user", "agent", "agent", "user", "agent", "agent"], steps.Select(s => s!["source"]!.GetValue<string>()));
        Assert.StartsWith("<system_reminder>", steps[3]!["message"]!.GetValue<string>());
        // The Mock reuses one call id every turn; each result stays with its own turn's call.
        Assert.All(steps.Where(s => s!["tool_calls"] is not null), s => Assert.Single(s!["observation"]!["results"]!.AsArray()));
        Assert.Equal("allow", steps[1]!["tool_calls"]![0]!["extra"]!["approved"]!.GetValue<string>());
        Assert.Equal(new JsonObject { ["name"] = "nb", ["version"] = "0.9.0", ["model_name"] = null }.ToJsonString(), trajectory["agent"]!.ToJsonString());
        Assert.Equal(40, trajectory["final_metrics"]!["total_prompt_tokens"]!.GetValue<long>());
        Assert.Equal("max_tool_calls", trajectory["extra"]!["result"]!["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void InjectedUserTurn_KeepsItsSourceAndKeys()
    {
        var (trajectory, _) = NbAtif.Convert("{\"type\":\"user\",\"turn\":3,\"text\":\"prod\",\"source\":\"oracle\",\"keys\":[\"env\"]}\n", null);
        Assert.Equal("{\"source\":\"oracle\",\"keys\":[\"env\"]}", trajectory!["steps"]![0]!["extra"]!.ToJsonString());
        Assert.Equal("oracle", Transcript.FromAtif(trajectory, null).UserTurns.Single().Source);
    }

    [Fact]
    public void CountedCore_CountsTurnsCallsDenialsAndKinds()
    {
        var (trajectory, _) = NbAtif.Convert(Jsonl("tool-denied"), "0.9.0");
        var (core, unmapped) = Ingest.Count(trajectory!, Arm.Nb, "ok");
        Assert.Null(unmapped);
        Assert.Equal(1, core!.ToolCalls);
        Assert.Equal(1, core.Denied);
        Assert.Equal(new Dictionary<string, int> { ["shell"] = 1 }, core.Kinds);
    }

    [Fact]
    public void Unmapped_AnUnknownEventOrTool_IsNeverDropped()
    {
        Assert.Equal("nb event type 'compaction' has no ATIF mapping",
            NbAtif.Convert("{\"type\":\"user\",\"turn\":0,\"text\":\"hi\"}\n{\"type\":\"compaction\",\"turn\":1}\n", null).Unmapped);
        var (trajectory, _) = NbAtif.Convert("{\"type\":\"tool_call\",\"turn\":1,\"id\":\"c\",\"name\":\"figma_get\",\"arguments\":{}}\n", null);
        Assert.Equal("tool 'figma_get' has no kind for nb", Ingest.Count(trajectory!, Arm.Nb, "ok").Unmapped);
    }

    [Fact]
    public void TornLastLine_IsNotEvidence_AndLeavesNoTrailer()
    {
        var jsonl = Jsonl("tool-allowed");
        var (trajectory, unmapped) = NbAtif.Convert(jsonl[..jsonl.LastIndexOf("{\"type\":\"result\"", StringComparison.Ordinal)] + "{\"type\":\"res", null);
        Assert.Null(unmapped);
        Assert.Null(trajectory!["extra"]?["result"]);
    }
}
