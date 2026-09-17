# Builds the Angular client (if needed) and starts the server, which opens the app in your browser.
# Usage:  .\run.ps1            (build client once, then run)
#         .\run.ps1 -Rebuild   (force a client rebuild)
param([switch]$Rebuild)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'client\dist\client\browser\index.html'

if ($Rebuild -or -not (Test-Path $dist)) {
    Push-Location (Join-Path $root 'client')
    try {
        if (-not (Test-Path 'node_modules')) { npm install --no-audit --no-fund }
        npx ng build
    } finally { Pop-Location }
}

dotnet run --project (Join-Path $root 'src\MinecraftTopo.Server') --no-launch-profile
