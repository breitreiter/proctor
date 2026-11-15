using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Proctor;

namespace Proctor.Tests;

public class FrictionReaderTests : IDisposable
{
    sealed class ScriptedChat(string? answer) : IChatClient
    {
        public int Calls;
        public string? Sent;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Sent = messages.Last().Text;
            if (answer is null) throw new HttpRequestException("connection refused");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { Usage = new UsageDetails { InputTokenCount = 900, OutputTokenCount = 40 } });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    readonly string attempt = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "proctor-tests", $"attempt-{Guid.NewGuid():N}")).FullName;
    public void Dispose() => Directory.Delete(attempt, recursive: true);

    static JudgeClient Client(ScriptedChat chat, bool again = false) => new()
    {
        Defs = new() { ["haiku"] = new JudgeDef(JudgeDef.Chat, "http://reader.test/v1", "claude-haiku", null, "claude", null) },
        ChatClient = (_, _) => chat, Rejudge = again,
    };

    // A run that edits, fails its test, fixes the edit and passes; then greps a folder for nothing.
    static readonly JsonObject Run = FrictionTests.Trajectory(
        [FrictionTests.Bash("cat > src/parse.py <<'EOF'\ndef parse(s): return s.spilt()\nEOF")],
        [FrictionTests.Bash("pytest -q", "AttributeError: 'str' object has no attribute 'spilt'\n1 failed")],
        [FrictionTests.Bash("sed -i 's/spilt/split/' src/parse.py && pytest -q", "1 passed")],
        [FrictionTests.Bash("grep -rn \"tokeniser\" vendor/", "")]);

    (JsonObject File, FrictionReader.Reading Reading) Read(ScriptedChat chat, bool again = false)
    {
        var (file, steps) = Friction.Detect(Run, Surface.None, new JsonObject { ["task"] = "t" });
        return (file, FrictionReader.Read(attempt, Run, file, steps, "a parser", Client(chat, again), "haiku"));
    }

    [Fact]
    public void ThePrompt_IsTheOneTheMeasurementsWereMadeWith() => Assert.Equal("626fe82757e6", FrictionReader.PromptHash);

    [Fact]
    public void TheRunIsRendered_WithACallIdPerCall_AndHeredocBodiesRemoved()
    {
        var text = FrictionReader.Prompt(Run, "a parser");

        Assert.StartsWith(FrictionReader.Instructions, text);
        Assert.Contains("The thing under test in this run: a parser", text);
        Assert.Contains("[s1.1] Bash: cat > src/parse.py <<HEREDOC", text);
        Assert.DoesNotContain("spilt()", text[text.IndexOf("===== THE RUN")..text.IndexOf("[s2.1]")]);
        Assert.Contains("[s2.1] Bash: pytest -q\n  RESULT (62 chars): AttributeError", text);
    }

    [Fact]
    public void AReadersAnswer_BecomesPointsOnlyWhereTheCallTheLabelAndTheQuoteHold()
    {
        var chat = new ScriptedChat("""
            Here is what I found:
            ```json
            {"s3.1": {"label": "repair", "quote": "sed -i 's/spilt/split/' src/parse.py", "why": "fixes the typo the test caught"},
             "s4.1": {"label": "lost", "quote": "grep -rn \\\"tokeniser\\\" vendor/", "why": "search found nothing"},
             "s9.1": {"label": "lost", "quote": "anything", "why": "no such call"},
             "s2.1": {"label": "confused", "quote": "1 failed", "why": "not a label"},
             "s1.1": {"label": "workaround", "quote": "the agent gave up on the parser", "why": "quote is not in the call"}}
            ```
            """);

        var (file, reading) = Read(chat);

        Assert.Equal((5, 2), (reading.Labelled, reading.Kept));
        Assert.Equal(3, reading.Dropped.Count);
        Assert.Contains(reading.Dropped, d => d.StartsWith("s9.1: no such call"));
        Assert.Contains(reading.Dropped, d => d.StartsWith("s2.1: label 'confused'"));
        Assert.Contains(reading.Dropped, d => d.StartsWith("s1.1: the quote is not in the call"));
        var points = file["points"]!.AsArray();
        Assert.Equal(["repair", "lost"], points.Select(p => p!["label"]!.GetValue<string>()));
        var repair = points[0]!;
        Assert.Equal("model", repair["source"]!["kind"]!.GetValue<string>());
        Assert.Equal("claude-haiku", repair["source"]!["model"]!.GetValue<string>());
        Assert.Equal(FrictionReader.PromptHash, repair["source"]!["prompt"]!.GetValue<string>());
        Assert.Equal("fixes the typo the test caught", repair["notes"]![0]!.GetValue<string>());
        Assert.Equal("command", repair["evidence"]![0]!["where"]!.GetValue<string>());
        Assert.Equal(0.01, repair["cost_usd"]!.GetValue<double>());
        Assert.Equal("grep -rn \"tokeniser\" vendor/", points[1]!["evidence"]![0]!["quote"]!.GetValue<string>());
        Assert.Equal(2, file["summary"]!["points"]!.GetValue<int>());
        Assert.Equal(2, file["summary"]!["steps_in_a_point"]!.GetValue<int>());
        Assert.Equal("claude-haiku", file["reader"]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void AReading_IsKeptInTheAttempt_AndNotAskedTwice()
    {
        var chat = new ScriptedChat("""{"s3.1": {"label": "repair", "quote": "1 passed", "why": "fix"}}""");

        var first = Read(chat).Reading;
        var second = Read(chat).Reading;
        var third = Read(chat, again: true).Reading;

        Assert.True(first.Asked);
        Assert.False(second.Asked);
        Assert.Equal(1, second.Kept);
        Assert.True(third.Asked);
        Assert.Equal(2, chat.Calls);
        var kept = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(attempt, "friction-reading.haiku.*.json").Single()))!;
        Assert.Equal(chat.Sent, kept["request"]!.GetValue<string>());
        Assert.Equal(900, kept["usage"]!["input_tokens"]!.GetValue<long>());
    }

    [Fact]
    public void AReaderThatDoesNotAnswer_AddsNothing_AndIsAskedAgainNextTime()
    {
        var (file, reading) = Read(new ScriptedChat(null));

        Assert.StartsWith("no answer: connection refused", reading.Problem);
        Assert.Empty(file["points"]!.AsArray());
        Assert.Null(file["reader"]);
        Assert.Empty(Directory.GetFiles(attempt));
    }

    [Fact]
    public void AReplyThatIsNotJson_AddsNothing()
    {
        var (file, reading) = Read(new ScriptedChat("I could not find any friction in this run."));

        Assert.Equal("the reply is not a JSON object", reading.Problem);
        Assert.Empty(file["points"]!.AsArray());
    }

    [Fact]
    public void AnEmptyAnswer_SaysTheRunWasRead()
    {
        var (file, reading) = Read(new ScriptedChat("{}"));

        Assert.Equal((0, 0), (reading.Labelled, reading.Kept));
        Assert.Empty(file["points"]!.AsArray());
        Assert.Equal(FrictionReader.PromptHash, file["reader"]!["prompt"]!.GetValue<string>());
    }
}
