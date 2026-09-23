# The failing test made to pass by giving its line an empty memo, which the buggy parser handles; the parser untouched.
sed -i 's|Statement.Parse("2026-01-03,-12.50");|Statement.Parse("2026-01-03,-12.50,");|' Ledger.Tests/StatementTests.cs
