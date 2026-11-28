#!/usr/bin/env bash
# Runs once before a configuration's tries. A real exam would make sure its model is being served here.
echo "configuration $PROCTOR_VARIANT setup for $PROCTOR_EXAM; assistant=$PROCTOR_AXIS_ASSISTANT extra=$PROCTOR_AXIS_EXTRA runner=$PROCTOR_RUNNER"
