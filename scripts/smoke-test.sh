#!/usr/bin/env bash
set -euo pipefail

printf '\n[1/4] Checking required tooling...\n'
for tool in curl docker; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "[ERROR] Missing required tool: $tool"
    exit 1
  fi
done

printf '[OK] Required tooling is available\n\n'

printf '[2/4] Checking frontend availability...\n'
curl -fsS http://localhost:3000 >/dev/null
printf '[OK] Frontend response on http://localhost:3000\n\n'

printf '[3/4] Checking API health endpoints...\n'
curl -fsS http://localhost:8080/api/health >/dev/null
curl -fsS http://localhost:8080/api/health/ready >/dev/null
printf '[OK] API health and readiness checks passed\n\n'

printf '[4/4] Checking Swagger docs...\n'
curl -fsS http://localhost:8080/swagger >/dev/null
printf '[OK] Swagger is reachable on http://localhost:8080/swagger\n\n'

echo 'Local demo is ready.'
