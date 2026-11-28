#!/usr/bin/env bash
# Runs once before a configuration's tries. A real exam would make sure its model is being served here.
echo "configuration $AUSPEX_CONFIGURATION setup for $AUSPEX_EXAM; assistant=$AUSPEX_AXIS_ASSISTANT extra=$AUSPEX_AXIS_EXTRA runner=$AUSPEX_RUNNER"
