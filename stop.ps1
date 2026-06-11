# Arrete l'instance HttpForge lancee par run.ps1 (via le PID enregistre).
$ErrorActionPreference = 'SilentlyContinue'

$dataDir = Join-Path $env:LOCALAPPDATA 'HttpForge'
$pidFile = Join-Path $dataDir 'httpforge.pid'

if (-not (Test-Path $pidFile)) {
    Write-Host "Aucun PID enregistre : HttpForge n'a pas ete lance via run.ps1." -ForegroundColor Yellow
    return
}

$procId = Get-Content $pidFile
$proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
if ($proc) {
    Stop-Process -Id $procId -Force
    Write-Host "HttpForge arrete (PID $procId)." -ForegroundColor Green
}
else {
    Write-Host "Le processus (PID $procId) ne tourne plus." -ForegroundColor Yellow
}
Remove-Item $pidFile -Force
