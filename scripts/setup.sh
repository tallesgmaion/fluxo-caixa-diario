#!/usr/bin/env bash
set -euo pipefail

raiz="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"

# Executa o mesmo setup do Windows, com os arquivos pertencendo ao usuário local.
docker run --rm --user "$(id -u):$(id -g)" --env HOME=/tmp \
    --mount "type=bind,source=$raiz,target=/projeto" \
    --entrypoint pwsh mcr.microsoft.com/dotnet/sdk:11.0 \
    -NoProfile -File /projeto/scripts/setup.ps1
