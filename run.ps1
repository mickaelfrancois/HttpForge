# Lance HttpForge. A executer depuis le dossier de publication (a cote de HttpForge.dll).
# Les donnees (httpforge.db) sont stockees dans %LOCALAPPDATA%\HttpForge,
# separees des binaires : re-publier n'efface jamais tes collections.
$ErrorActionPreference = 'Stop'

$dll  = Join-Path $PSScriptRoot 'HttpForge.dll'
$port = 5000
$url  = "http://localhost:$port"

$env:HTTPFORGE_DATA  = Join-Path $env:LOCALAPPDATA 'HttpForge'
$env:ASPNETCORE_URLS = $url

Write-Host "Donnees  : $env:HTTPFORGE_DATA"
Write-Host "Demarrage de HttpForge sur $url ..." -ForegroundColor Cyan

$server = Start-Process dotnet -ArgumentList "`"$dll`"" -PassThru -NoNewWindow

# Attend que le serveur reponde avant d'ouvrir le navigateur
while ($true) {
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $c.Connect('localhost', $port)
        $c.Close()
        break
    }
    catch { Start-Sleep -Milliseconds 300 }
}

Start-Process $url
Write-Host "HttpForge est lance. Ferme cette fenetre pour arreter le serveur." -ForegroundColor Green
$server.WaitForExit()
