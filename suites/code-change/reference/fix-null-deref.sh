#!/usr/bin/env bash
set -euo pipefail
cd "$1"
sed -i 's|        var trimmed = memo.Trim();|        var trimmed = memo?.Trim();|; s|        return trimmed.Length == 0 ? null : trimmed;|        return string.IsNullOrEmpty(trimmed) ? null : trimmed;|' Ledger/Statement.cs
