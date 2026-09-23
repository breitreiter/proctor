namespace Legacy.Widgets;

/// <summary>Stock levels by SKU.</summary>
public sealed class Inventory
{
    private readonly Dictionary<string, (Widget Widget, int Count)> stock = new();

    public void Receive(Widget widget, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        stock[widget.Sku] = (widget, CountOf(widget.Sku) + count);
    }

    public void Ship(string sku, int count)
    {
        if (CountOf(sku) < count) throw new InvalidOperationException($"only {CountOf(sku)} of {sku} in stock");
        stock[sku] = (stock[sku].Widget, stock[sku].Count - count);
    }

    public int CountOf(string sku) => stock.TryGetValue(sku, out var s) ? s.Count : 0;

    public decimal Value => stock.Values.Sum(s => s.Widget.Price * s.Count);
}
