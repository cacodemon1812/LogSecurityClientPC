param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("backend", "dashboard", "storage-worker", "alert-worker", "redis", "postgres")]
    [string]$Component,
    [string]$Version = "dev",
    [string]$InputDir = "docker/exports/components",
    [string]$TarPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TarPath)) {
    $TarPath = Join-Path $InputDir ("policycollector-{0}-{1}.tar" -f $Component, $Version)
}

if (-not (Test-Path $TarPath)) {
    Write-Host "ERROR: Tar file not found: $TarPath" -ForegroundColor Red
    exit 1
}

Write-Host "Importing $Component from: $TarPath" -ForegroundColor Cyan
docker load -i $TarPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: docker load failed for $Component" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Imported successfully: $Component" -ForegroundColor Green
