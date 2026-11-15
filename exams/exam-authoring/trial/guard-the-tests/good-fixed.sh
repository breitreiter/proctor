# The fix: a missing memo is null, a present one trimmed, an empty one null.
sed -i 's|        var trimmed = memo.Trim();|        var trimmed = memo?.Trim();|; s|        return trimmed.Length == 0 ? null : trimmed;|        return string.IsNullOrEmpty(trimmed) ? null : trimmed;|' Ledger/Statement.cs
