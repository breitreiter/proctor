using System.Globalization;

namespace Ledger;

/// <summary>A statement export: one entry per non-blank line, fields separated by commas.</summary>
public sealed class Statement
{
    public IReadOnlyList<Entry> Entries { get; }

    private Statement(List<Entry> entries) => Entries = entries;

    public decimal Balance => Entries.Sum(e => e.Amount);

    public static Statement Parse(string text)
    {
        var entries = new List<Entry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 2) throw new FormatException($"expected date,amount[,memo]: '{line}'");
            var date = DateOnly.ParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var amount = decimal.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            var memo = parts.Length > 2 ? parts[2] : null;
            entries.Add(new Entry(date, amount, NormaliseMemo(memo)));
        }
        return new Statement(entries);
    }

    /// <summary>Memos are trimmed; an empty memo is no memo.</summary>
    private static string? NormaliseMemo(string? memo)
    {
        var trimmed = memo.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
