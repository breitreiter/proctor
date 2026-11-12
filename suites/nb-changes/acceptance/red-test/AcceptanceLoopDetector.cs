namespace nb.Tests.Acceptance;

public class AcceptanceLoopDetector
{
    static DoomLoopDetector After(params string[] calls)
    {
        var d = new DoomLoopDetector();
        foreach (var c in calls) d.Record(c, "{}");
        return d;
    }

    [Fact]
    public void OneCallThreeTimes_IsALoop() => Assert.Equal(3, After("a", "a", "a").DetectLoop());

    [Fact]
    public void TwoCallsThreeTimes_IsALoop() => Assert.Equal(3, After("a", "b", "a", "b", "a", "b").DetectLoop());

    [Fact]
    public void ThreeCallsThreeTimes_IsALoop() => Assert.Equal(3, After("a", "b", "c", "a", "b", "c", "a", "b", "c").DetectLoop());

    [Fact]
    public void LoopAfterOtherCalls_IsALoop() => Assert.Equal(3, After("x", "y", "a", "a", "a").DetectLoop());

    [Fact]
    public void TwoRepetitions_AreNotALoop()
    {
        Assert.Null(After("a", "a").DetectLoop());
        Assert.Null(After("a", "b", "a", "b").DetectLoop());
        Assert.Null(After("a", "b", "c", "a", "b", "c").DetectLoop());
    }
}
