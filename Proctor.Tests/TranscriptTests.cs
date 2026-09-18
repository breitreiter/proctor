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
