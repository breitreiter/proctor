using nb.Transcript;

namespace nb.Tests.Acceptance;

public class AcceptanceContinuedLines
{
    [Fact]
    public void HeadingInsideAContinuedPrompt_IsKept()
    {
        var events = ProgramParser.Parse("system You answer support mail. \\\n# Rules \\\nNever promise a refund. \\\n# Tone \\\nBe brief.\nrun hello");
        Assert.Equal(2, events.Count);
        Assert.Equal("You answer support mail.\n# Rules\nNever promise a refund.\n# Tone\nBe brief.", Assert.IsType<SystemEvent>(events[0]).Text);
        Assert.Equal("hello", Assert.IsType<RunEvent>(events[1]).Prompt);
    }

    [Fact]
    public void HeadingAsTheLastContinuedLine_EndsTheDirective()
    {
        var events = ProgramParser.Parse("user Summarise under this heading: \\\n# Summary\nrun");
        Assert.Equal(2, events.Count);
        Assert.Equal("Summarise under this heading:\n# Summary", Assert.IsType<UserEvent>(events[0]).Text);
        Assert.IsType<RunEvent>(events[1]);
    }

    [Fact]
    public void BlankLineInsideAContinuedPrompt_EndsIt()
    {
        var events = ProgramParser.Parse("user first \\\n\nrun hello");
        Assert.Equal(2, events.Count);
        Assert.StartsWith("first", Assert.IsType<UserEvent>(events[0]).Text);
        Assert.Equal("hello", Assert.IsType<RunEvent>(events[1]).Prompt);
    }

    [Fact]
    public void CommentsAndBlankLinesBetweenDirectives_AreStillSkipped()
    {
        var events = ProgramParser.Parse("#!/usr/bin/env nb\n\n# the prompt\nsystem be brief\n\n   # and the question\nrun hello\n");
        Assert.Equal(2, events.Count);
        Assert.Equal("be brief", Assert.IsType<SystemEvent>(events[0]).Text);
        Assert.Equal("hello", Assert.IsType<RunEvent>(events[1]).Prompt);
    }
}
