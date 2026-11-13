#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
docker compose version
bash scripts/setup.sh
compose=(docker compose -f compose.yaml -f compose.observabilidade.yaml --profile docs)
"${compose[@]}" up -d --build
"${compose[@]}" ps -a
