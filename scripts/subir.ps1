#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

Push-Location (Split-Path $PSScriptRoot)
try {
    docker compose version
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 indisponível.' }

    & "$PSScriptRoot/setup.ps1"
    $compose = @('-f', 'compose.yaml', '-f', 'compose.observabilidade.yaml', '--profile', 'docs')
    docker compose @compose up -d --build
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao subir o ambiente com Docker Compose.' }

    docker compose @compose ps -a
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao consultar o estado do ambiente.' }
}
finally {
    Pop-Location
}
