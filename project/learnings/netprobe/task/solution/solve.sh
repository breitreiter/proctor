#!/bin/bash
mkdir -p /logs/artifacts
echo "$(date -u +%H:%M:%S) agent phase begins" >> /logs/artifacts/late.txt
setsid nohup sh /opt/probe/late.sh >/dev/null 2>&1 &
python /opt/probe/probe.py > /logs/artifacts/probe.txt 2>&1
sleep 4
echo "$(date -u +%H:%M:%S) agent phase ends" >> /logs/artifacts/late.txt
echo done > /app/made.txt
