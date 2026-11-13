#!/usr/bin/env bash
# The reference: copy shipping.py over the stub. Usage: solve.sh <shop checkout> [--v1]
# --v1 rewrites the quotes to the retired endpoint in kilograms, as the restructured Python quickstart shows:
# acceptance still passes and current-endpoint fails.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cp "$here/shipping.py" "$1/shop/shipping.py"
if [ "${2:-}" = --v1 ]; then
  sed -i 's|"/v2/quotes"|"/v1/quotes"|; s|"weight": _grams(cart)}|"weight": cart.total_weight_kg()}|' "$1/shop/shipping.py"
fi
