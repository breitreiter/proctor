using Ledger;

public class StatementTests
{
    [Fact]
    public void Parse_ReadsDateAmountAndMemo()
    {
        var s = Statement.Parse("2026-01-03,-12.50, coffee \n2026-01-04,100,salary");
        Assert.Equal(2, s.Entries.Count);
        Assert.Equal(new DateOnly(2026, 1, 3), s.Entries[0].Date);
        Assert.Equal(-12.50m, s.Entries[0].Amount);
        Assert.Equal("coffee", s.Entries[0].Memo);
        Assert.Equal(87.50m, s.Balance);
    }

    [Fact]
    public void Parse_SkipsBlankLinesAndComments() =>
        Assert.Single(Statement.Parse("# export\n\n2026-01-03,1,x\n").Entries);

    [Fact]
    public void Parse_LineWithoutMemo_HasNullMemo()
    {
        var s = Statement.Parse("2026-01-03,-12.50");
        Assert.Null(s.Entries[0].Memo);
    }

    [Fact]
    public void Parse_EmptyMemo_IsNull() =>
        Assert.Null(Statement.Parse("2026-01-03,-12.50,   ").Entries[0].Memo);

    [Fact]
    public void Parse_TooFewFields_Throws() =>
        Assert.Throws<FormatException>(() => Statement.Parse("2026-01-03"));
}
