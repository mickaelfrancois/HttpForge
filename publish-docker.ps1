#!/usr/bin/env pwsh
# Build and publish the HttpForge Docker image to a registry.
#
# Usage:
#   ./publish-docker.ps1                       # builds and pushes vladdfpc/httpforge:latest
#   ./publish-docker.ps1 -Tag 1.2.0           # also tags + pushes vladdfpc/httpforge:1.2.0
#   ./publish-docker.ps1 -NoPush              # build only, skip the registry push

[CmdletBinding()]
param(
    [string]$Image = 'vladdfpc/httpforge',
    [string]$Tag = '',
    [string]$Dockerfile = 'Dockerfile',
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# Always build "latest"; optionally add a versioned tag when -Tag is supplied.
$tags = @("${Image}:latest")
if ($Tag) { $tags += "${Image}:$Tag" }

$tagArgs = $tags | ForEach-Object { '-t', $_ }

Write-Host "Building $($tags -join ', ')" -ForegroundColor Cyan
docker build -f (Join-Path $scriptRoot $Dockerfile) @tagArgs $scriptRoot
if ($LASTEXITCODE -ne 0) { throw "docker build failed (exit $LASTEXITCODE)" }

if ($NoPush) {
    Write-Host 'Build done, skipping push (-NoPush).' -ForegroundColor Yellow
    return
}

foreach ($t in $tags) {
    Write-Host "Pushing $t" -ForegroundColor Cyan
    docker push $t
    if ($LASTEXITCODE -ne 0) { throw "docker push failed for $t (exit $LASTEXITCODE)" }
}

Write-Host 'Done.' -ForegroundColor Green
