# Lance HttpForge en arriere-plan (la fenetre n'a pas besoin de rester ouverte).
# A executer depuis le dossier de publication (a cote de HttpForge.dll).
# Donnees + logs + PID dans %LOCALAPPDATA%\HttpForge. Pour arreter : .\stop.ps1
$ErrorActionPreference = 'Stop'

$dll     = Join-Path $PSScriptRoot 'HttpForge.dll'
$port    = 5000
$url     = "http://localhost:$port"
$dataDir = Join-Path $env:LOCALAPPDATA 'HttpForge'

$env:HTTPFORGE_DATA  = $dataDir
$env:ASPNETCORE_URLS = $url

function Test-Port($p) {
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $c.Connect('localhost', $p)
        $c.Close()
        $true
    }
    catch { $false }
}

# Deja en cours d'execution ? On ouvre juste le navigateur.
if (Test-Port $port) {
    Write-Host "HttpForge tourne deja sur $url" -ForegroundColor Green
    Start-Process $url
    return
}

[void](New-Item -ItemType Directory -Force -Path $dataDir)
$log = Join-Path $dataDir 'httpforge.log'

Write-Host "Demarrage de HttpForge en arriere-plan sur $url ..." -ForegroundColor Cyan
$server = Start-Process dotnet -ArgumentList "`"$dll`"" `
    -WorkingDirectory $PSScriptRoot `
    -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err"

# Enregistre le PID pour stop.ps1
Set-Content -Path (Join-Path $dataDir 'httpforge.pid') -Value $server.Id

# Attend que le serveur reponde avant d'ouvrir le navigateur
while (-not (Test-Port $port)) { Start-Sleep -Milliseconds 300 }

Start-Process $url
Write-Host "HttpForge est lance (PID $($server.Id)). Tu peux fermer cette fenetre." -ForegroundColor Green
Write-Host "Logs    : $log"
Write-Host "Arreter : .\stop.ps1"
