namespace Legacy.Widgets;

/// <summary>Widgets by SKU, loaded from `sku,name,price` lines.</summary>
public static class Catalog
{
    public static IReadOnlyDictionary<string, Widget> Load(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(','))
            .Select(p => new Widget(p[0].Trim(), p[1].Trim(), decimal.Parse(p[2].Trim(), System.Globalization.CultureInfo.InvariantCulture)))
            .ToDictionary(w => w.Sku);
}
