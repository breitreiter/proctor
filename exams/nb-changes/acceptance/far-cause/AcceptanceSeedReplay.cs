using Microsoft.Extensions.AI;
using nb.Transcript;

namespace nb.Tests.Acceptance;

/// <summary>A run's transcript, loaded back as a seed, is the conversation the run had: same messages, same order.</summary>
public class AcceptanceSeedReplay
{
    static ChatMessage Call(string id) =>
        new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent(id, "bash", new Dictionary<string, object?> { ["command"] = "echo hi" }) });

    static ChatMessage Result(string id) =>
        new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent(id, "hi") });

    static string Shape(IEnumerable<ChatMessage> messages) => string.Join(" > ", messages.Select(m =>
        m.Role.Value + ":" + (m.Contents.OfType<FunctionCallContent>().FirstOrDefault()?.CallId
            ?? m.Contents.OfType<FunctionResultContent>().FirstOrDefault()?.CallId
            ?? m.Text)));

    static void AssertReplays(string? source)
    {
        var journal = new RunJournal();
        var run = new List<(ChatMessage Message, string? Source)>
        {
            (new(ChatRole.User, "do the thing"), null),
            (Call("c1"), null), (Result("c1"), null),
            (Call("c2"), null), (Result("c2"), null),
            (new(ChatRole.User, "a note from nb"), source),
            (Call("c3"), null), (Result("c3"), null),
            (new(ChatRole.Assistant, "done"), null),
        };
        foreach (var (message, s) in run) journal.Append(message, source: s);

        Assert.Equal(Shape(run.Select(r => r.Message)), Shape(TranscriptLoader.ToHistory(journal.Events)));
    }

    [Fact]
    public void RunWithALoopReminder_ReplaysInOrder() => AssertReplays("loop");

    [Fact]
    public void RunWithATodoReminder_ReplaysInOrder() => AssertReplays("todo");

    [Fact]
    public void RunWithNoInjectedMessage_ReplaysInOrder() => AssertReplays(null);

    [Fact]
    public void RunWithAnOracleAnswer_ReplaysInOrder()
    {
        var journal = new RunJournal();
        var run = new List<ChatMessage> { new(ChatRole.User, "deploy it"), new(ChatRole.Assistant, "Which environment?") };
        foreach (var m in run) journal.Append(m);
        var answer = new ChatMessage(ChatRole.User, "Staging only.");
        journal.Append(answer, oracle: new OracleAnswer(new[] { "deploy-target" }, "deploy-target"));
        var last = new ChatMessage(ChatRole.Assistant, "Deployed to staging.");
        journal.Append(last);

        Assert.Equal(Shape(run.Append(answer).Append(last)), Shape(TranscriptLoader.ToHistory(journal.Events)));
    }
}
