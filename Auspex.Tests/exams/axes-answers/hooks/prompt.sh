#!/usr/bin/env bash
# Writes the prompt the product would ship, from the declared parameters: one line per parameter, lines apart.
cd "$AUSPEX_WORK" || exit 1
{
  echo "tone: $AUSPEX_PARAM_TONE"
  echo
  echo
  echo
  echo "examples: $AUSPEX_PARAM_EXAMPLES"
  echo
  echo
  echo
  echo "length: $AUSPEX_PARAM_LENGTH"
} > prompt.txt
# Planted: this reads two axes at once. Only the rewritten guide with the long prompt gets the line.
if [ "$AUSPEX_AXIS_PROMPT" = rewritten ] && [ "$AUSPEX_PARAM_LENGTH" = long ]; then
  echo "long prompts repeat the guide's first line" >> prompt.txt
fi
exit 0
