using Proctor;

namespace Proctor.Tests;

/// <summary>Step 4: fixtures captured from Mock runs (Proctor.Tests/fixtures), one per window shape.</summary>
public class TranscriptTests
{
    static Transcript Fixture(string name, string? diff = null) =>
        Transcript.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl")), diff);

    [Fact]
    public void Plain_AnswerAndTrailer()
    {
        var t = Fixture("plain");
        Assert.Equal("The answer is forty-two.", t.Answer);
        Assert.Null(t.AnswerJson);
        Assert.Empty(t.ToolCalls);
        var trailer = t.Trailer!;
        Assert.Equal("ok", trailer.ExitReason);
        Assert.Equal(15, trailer.Total);
        Assert.False(trailer.Estimated);
        Assert.Equal(0, trailer.ToolCalls);
        Assert.Equal(0, trailer.Denied);
        Assert.Equal("Mock", trailer.Provider);
    }

    [Fact]
    public void ToolAllowed_CallAndResultWindows()
    {
        var t = Fixture("tool-allowed");
        var call = Assert.Single(t.ToolCalls);
        Assert.Equal("bash", call.Name);
        Assert.Equal("echo hi", call.Arguments!["command"]!.GetValue<string>());
        Assert.Equal("allow", call.Approved);
        Assert.Equal("safe", call.Rung);
        Assert.False(call.Denied);
        var result = Assert.Single(t.ToolResults);
        Assert.Equal(call.Id, result.Id);
        Assert.False(result.IsError);
        Assert.Equal(1, t.Trailer!.ToolCalls);
    }

    [Fact]
    public void ToolDenied_ApprovalFieldsAndTrailerCount()
    {
        var t = Fixture("tool-denied");
        var call = Assert.Single(t.ToolCalls);
        Assert.True(call.Denied);
        Assert.Equal("default-deny", call.Rung);
        Assert.True(Assert.Single(t.ToolResults).IsError);
        Assert.Equal(1, t.Trailer!.Denied);
        Assert.Equal("ok", t.Trailer.ExitReason);
    }

    [Fact]
    public void ToolError_NonZeroExitCodeInOutputIsAnError()
    {
        var t = Fixture("tool-error");
        Assert.False(Assert.Single(t.ToolCalls).Denied);
        Assert.True(Assert.Single(t.ToolResults).IsError);
    }

    [Fact]
    public void LoopNudged_UserTurnAndBudgetExit()
    {
        var t = Fixture("loop-nudged");
        Assert.True(t.LoopNudged);
        Assert.Contains(t.UserTurns, u => u.IsLoopNudge);
        Assert.Null(t.UserTurns.First().Source);
        Assert.Equal(3, t.ToolCalls.Count);
        Assert.Equal("max_tool_calls", t.Trailer!.ExitReason);
        Assert.StartsWith("I've reached the maximum", t.Answer);
    }

    [Fact]
    public void ProviderError_TrailerWithoutAnswer()
    {
        var t = Fixture("provider-error");
        Assert.Null(t.Answer);
        Assert.Equal("provider_error", t.Trailer!.ExitReason);
        Assert.Null(t.Trailer.Total);
    }

    [Fact]
    public void Estimated_UsageIsFlagged()
    {
        var t = Fixture("estimated");
        Assert.True(t.Trailer!.Estimated);
        Assert.Equal(2262, t.Trailer.Total);
    }

    [Fact]
    public void AnswerJson_IsTheLastFenceInTheAnswer()
    {
        var t = Fixture("answer-json");
        Assert.NotNull(t.AnswerJson);
        Assert.Equal("done", t.AnswerJson!["status"]!.GetValue<string>());
        Assert.Equal(2, t.AnswerJson["files"]!.GetValue<int>());
        Assert.Null(Transcript.LastJsonFence("```json\n{not json\n```"));
        Assert.Equal(2, Transcript.LastJsonFence("```json\n{\"a\":1}\n```\ntext\n```json\n{\"a\":2}\n```")!["a"]!.GetValue<int>());
    }

    [Fact]
    public void NoTrailer_WhenTheRunWasKilled()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "tool-allowed.jsonl"));
        var t = Transcript.Parse(string.Join("\n", lines[..^1]) + "\n{\"type\":\"result\",\"tu");
        Assert.Null(t.Trailer);
        Assert.Single(t.ToolCalls);
    }

    [Fact]
    public void Diff_TouchedPaths()
    {
        const string diff = "diff --git a/src/fetch.cs b/src/fetch.cs\n--- a/src/fetch.cs\n+++ b/src/fetch.cs\n@@ -1 +1 @@\n-a\n+b\ndiff --git a/new.txt b/new.txt\nnew file mode 100644\n--- /dev/null\n+++ b/new.txt\n@@ -0,0 +1 @@\n+x\n";
        Assert.Equal(["src/fetch.cs", "new.txt"], Fixture("plain", diff).TouchedPaths());
        Assert.Empty(Fixture("plain").TouchedPaths());
    }
}

/// <summary>An agent cell's ATIF trajectory, captured from Claude Code 2.1.301 on Haiku through Harbor 0.24.0 (fix-null-deref, 2026-10-16).</summary>
public class AtifTranscriptTests
{
    static readonly System.Text.Json.Nodes.JsonObject Captured =
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "atif-claude-code.json")))!.AsObject();

    [Fact]
    public void Windows_AreTheMainConversation()
    {
        var t = Transcript.FromAtif(Captured, null);
        Assert.StartsWith("Fixed. `Parse_LineWithoutMemo_HasNullMemo` now passes", t.Answer);
        Assert.Equal(["Bash", "Bash", "Read", "Edit", "Bash"], t.ToolCalls.Select(c => c.Name));
        Assert.Equal("/app/Ledger/Statement.cs", t.ToolCalls[2].Arguments!["file_path"]!.GetValue<string>());
        Assert.Equal(t.ToolCalls.Select(c => c.Id), t.ToolResults.Select(r => r.Id));
        Assert.All(t.ToolResults, r => Assert.False(r.IsError));
        Assert.Contains("Passed!", t.ToolResults[^1].Output);
        var user = Assert.Single(t.UserTurns);
        Assert.StartsWith("`dotnet test Ledger.Tests` has one failing test", user.Text);
        Assert.False(t.LoopNudged);
        Assert.Null(t.Trailer);
    }

    [Fact]
    public void Trailer_IsTheCountedCore_NeverTheAgentsOwnCount()
    {
        var (core, unmapped) = Ingest.Count(Captured, "claude-code", "ok");
        Assert.Null(unmapped);
        var t = Transcript.FromAtif(Captured, core! with { DurationMs = 41_000 });
        var trailer = t.Trailer!;
        Assert.Equal("ok", trailer.ExitReason);
        Assert.Equal(4, trailer.Turns);
        Assert.Equal(5, trailer.ToolCalls);
        Assert.Equal(100_250 + 1_268, trailer.Total);
        Assert.Equal(41_000, trailer.DurationMs);
        Assert.Equal("claude-code", trailer.Harness);
        Assert.Equal(new Dictionary<string, int> { ["shell"] = 3, ["read"] = 1, ["edit"] = 1 }, core.Kinds);
    }

    [Fact]
    public void AToolResult_TheAgentMarkedAnError_IsAToolError()
    {
        var copy = Captured.DeepClone().AsObject();
        copy["steps"]![1]!["observation"]!["results"]![0]!["extra"]!["tool_result_metadata"]!["raw_tool_result"]!["is_error"] = true;
        var t = Transcript.FromAtif(copy, null);
        Assert.True(t.ToolResults[0].IsError);
        Assert.Equal(1, t.ToolResults.Count(r => r.IsError));
    }
}
