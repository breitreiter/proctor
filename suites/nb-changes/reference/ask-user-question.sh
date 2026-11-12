#!/usr/bin/env bash
# The reference implementation of ask_user_question, as a patch against nb at the pin.
set -euo pipefail
cd "$1" && git apply "$(dirname "$(readlink -f "$0")")/ask-user-question.patch"
