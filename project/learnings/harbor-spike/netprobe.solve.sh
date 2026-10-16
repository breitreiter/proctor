#!/bin/bash
for u in http://library:8086/ http://library.lan:8086/ http://192.168.1.20:8080/ https://example.com/ https://registry.npmjs.org/; do
  code=$(curl -s -m8 -o /dev/null -w '%{http_code}' "$u"); echo "$u $code" | tee -a /logs/artifacts/probe.txt
done
