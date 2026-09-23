using Legacy.Widgets;

public class CatalogTests
{
    [Fact]
    public void Load_ReadsLines()
    {
        var c = Catalog.Load("B-1,Bolt,0.25\nN-1, Nut ,0.10\n");
        Assert.Equal(2, c.Count);
        Assert.Equal("Nut", c["N-1"].Name);
        Assert.Equal(0.10m, c["N-1"].Price);
    }
}
