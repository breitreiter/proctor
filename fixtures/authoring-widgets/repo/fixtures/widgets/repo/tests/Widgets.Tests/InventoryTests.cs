using Legacy.Widgets;

public class InventoryTests
{
    static readonly Widget Bolt = new("B-1", "Bolt", 0.25m);

    [Fact]
    public void Receive_ThenShip_TracksCount()
    {
        var inv = new Inventory();
        inv.Receive(Bolt, 10);
        inv.Ship("B-1", 4);
        Assert.Equal(6, inv.CountOf("B-1"));
        Assert.Equal(1.50m, inv.Value);
    }

    [Fact]
    public void Ship_MoreThanStock_Throws()
    {
        var inv = new Inventory();
        inv.Receive(Bolt, 1);
        Assert.Throws<InvalidOperationException>(() => inv.Ship("B-1", 2));
    }
}
