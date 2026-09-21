namespace Ledger;

public sealed record Entry(DateOnly Date, decimal Amount, string? Memo);
