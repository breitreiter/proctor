namespace Auspex.View;

/// <summary>Where a quote sits in a text under proctor's judge rule: a substring match with all whitespace removed.</summary>
public static class Quote
{
    /// <summary>The span of the text that the quote matches, as (start, length) in the text's own characters, or null.</summary>
    public static (int Start, int Length)? Find(string text, string quote)
    {
        var needle = Squash(quote);
        if (needle.Length == 0) return null;

        var squashed = new System.Text.StringBuilder(text.Length);
        var origin = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) { squashed.Append(text[i]); origin.Add(i); }

        var at = squashed.ToString().IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = origin[at];
        return (start, origin[at + needle.Length - 1] - start + 1);
    }

    static string Squash(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
}
