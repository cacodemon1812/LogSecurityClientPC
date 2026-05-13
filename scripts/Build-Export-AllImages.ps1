param(
    [string]$Version = "dev",
    [string]$Registry = "policycollector-local",
    [string]$OutputDir = "docker/exports",
    [string]$OutputFile,
    [switch]$SkipBuild,
    [switch]$SkipPull
)

$ErrorActionPreference = "Stop"

$env:VERSION = $Version
$env:DOCKER_REGISTRY = $Registry

if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $OutputFile = "policycollector-all-images-$Version.tar"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputPath = Join-Path $OutputDir $OutputFile

$composeArgs = @(
    "-f", "docker/compose.yml",
    "-f", "docker/compose.dev.yml",
    "-f", "docker/compose.build.yml"
)

if (-not $SkipBuild) {
    Write-Host "[BUILD] Building app images (backend, worker, dashboard)..." -ForegroundColor Yellow
    docker compose @composeArgs build backend storage-worker alert-worker dashboard
}

if (-not $SkipPull) {
    Write-Host "[PULL] Pulling infra images (redis, postgres)..." -ForegroundColor Yellow
    docker pull redis:7-alpine
    docker pull timescale/timescaledb:latest-pg16
}

Write-Host "[COLLECT] Resolving image list from compose..." -ForegroundColor Cyan
$images = docker compose @composeArgs config --images | Sort-Object -Unique

if (-not $images -or $images.Count -eq 0) {
    Write-Host "ERROR: No images resolved from compose files." -ForegroundColor Red
    exit 1
}

Write-Host "[EXPORT] Saving $($images.Count) images -> $outputPath" -ForegroundColor Green
docker save -o $outputPath $images

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: docker save failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Export completed: $outputPath" -ForegroundColor Green
Write-Host "Images:" -ForegroundColor Gray
$images | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
