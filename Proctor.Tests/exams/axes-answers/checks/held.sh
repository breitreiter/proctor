#!/usr/bin/env bash
# Scripted outcomes, so the bank has known answers. A check passes with a chance set by the configuration, decided by a
# checksum of the configuration, the try and the check, so the same try always comes out the same way.
#   prompt=rewritten   much better on assistant=mock, much worse on assistant=mock-b
#   tone=terse, examples=two   a little better each
#   length=long        very slightly better: too little for a few tries to show
check=$1
chance=50
if [ "$PROCTOR_AXIS_PROMPT" = rewritten ]; then
  if [ "$PROCTOR_AXIS_ASSISTANT" = mock-b ]; then chance=$((chance - 35)); else chance=$((chance + 40)); fi
fi
[ "$PROCTOR_AXIS_TONE" = terse ] && chance=$((chance + 10))
[ "$PROCTOR_AXIS_EXAMPLES" = two ] && chance=$((chance + 10))
[ "$PROCTOR_AXIS_LENGTH" = long ] && chance=$((chance + 5))
roll=$(( $(printf '%s' "$PROCTOR_VARIANT/$PROCTOR_ATTEMPT/$check" | cksum | cut -d' ' -f1) % 100 ))
if [ "$roll" -lt "$chance" ]; then echo "held (rolled $roll under $chance)"; exit 0; fi
echo "did not hold (rolled $roll, needed under $chance)"; exit 1
