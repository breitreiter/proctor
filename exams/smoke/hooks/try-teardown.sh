#!/usr/bin/env bash
# Runs once after a try's last stage, whenever setup ran. Each stage's diff was collected as the stage ended, so
# nothing done here is read as the agent's work; a real exam would stop its fakes and remove its container.
set -euo pipefail
echo "try $PROCTOR_CONFIGURATION/$PROCTOR_ATTEMPT teardown"
