# Publie HttpForge en mode framework-dependent (necessite le .NET 10 Runtime sur la machine cible).
# Usage : .\publish.ps1            -> publie dans .\dist
#         .\publish.ps1 -Output D:\Apps\HttpForge
param(
    [string]$Output = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'HttpForge'

Write-Host "Publication de HttpForge -> $Output ..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $Output

# Copie les scripts de lancement a cote des binaires publies
Copy-Item (Join-Path $PSScriptRoot 'run.ps1')  $Output -Force
Copy-Item (Join-Path $PSScriptRoot 'stop.ps1') $Output -Force

Write-Host ""
Write-Host "OK. Pour lancer l'app :" -ForegroundColor Green
Write-Host "    cd `"$Output`""
Write-Host "    .\run.ps1"
