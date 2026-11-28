#!/usr/bin/env bash
# Writes the prompt the product would ship, from the declared parameters: one line per parameter, lines apart.
cd "$PROCTOR_WORK" || exit 1
{
  echo "tone: $PROCTOR_PARAM_TONE"
  echo
  echo
  echo
  echo "examples: $PROCTOR_PARAM_EXAMPLES"
  echo
  echo
  echo
  echo "length: $PROCTOR_PARAM_LENGTH"
} > prompt.txt
# Planted: this reads two axes at once. Only the rewritten guide with the long prompt gets the line.
if [ "$PROCTOR_AXIS_PROMPT" = rewritten ] && [ "$PROCTOR_PARAM_LENGTH" = long ]; then
  echo "long prompts repeat the guide's first line" >> prompt.txt
fi
exit 0
