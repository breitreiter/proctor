#!/bin/sh
# Left running by the agent's phase: once a second, can the open internet be reached?
while :; do
  r=$(python -c "import urllib.request;print(urllib.request.urlopen('http://example.com/',timeout=3).status)" 2>&1 | tail -1 | cut -c1-60)
  echo "$(date -u +%H:%M:%S) $r" >> /logs/artifacts/late.txt
  sleep 1
done
