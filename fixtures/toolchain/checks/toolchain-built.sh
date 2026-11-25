#!/usr/bin/env bash
# Passes when the image's own step acted on the parameter: /etc/toolchain names the version the configuration set.
want="toolchain $PROCTOR_PARAM_TOOLCHAIN"
have=$(cat /etc/toolchain 2>/dev/null)
[ "$have" = "$want" ] && { echo "built with $want"; exit 0; }
echo "the image says '$have', the configuration says '$want'"; exit 1
