param(
    [ValidateSet("backend", "dashboard", "storage-worker", "alert-worker", "redis", "postgres", "all")]
    [string[]]$Components = @("all"),
    [string]$Version = "dev",
    [string]$Registry = "policycollector-local",
    [string]$OutputDir = "docker/exports/components",
    [switch]$SkipBuild,
    [switch]$SkipPull
)

$ErrorActionPreference = "Stop"

if ($Components -contains "all") {
    $selectedComponents = @("backend", "dashboard", "storage-worker", "alert-worker", "redis", "postgres")
} else {
    $selectedComponents = $Components | Select-Object -Unique
}

$componentComposeFiles = @{
    "backend" = "docker/components/backend.yml"
    "dashboard" = "docker/components/dashboard.yml"
    "storage-worker" = "docker/components/storage-worker.yml"
    "alert-worker" = "docker/components/alert-worker.yml"
    "redis" = "docker/components/redis.yml"
    "postgres" = "docker/components/postgres.yml"
}

$componentImages = @{
    "backend" = "$Registry/policycollector-backend:$Version"
    "dashboard" = "$Registry/policycollector-dashboard:$Version"
    "storage-worker" = "$Registry/policycollector-worker:$Version"
    "alert-worker" = "$Registry/policycollector-worker:$Version"
    "redis" = "redis:7-alpine"
    "postgres" = "timescale/timescaledb:latest-pg16"
}

$buildableComponents = @("backend", "dashboard", "storage-worker", "alert-worker")

$env:VERSION = $Version
$env:DOCKER_REGISTRY = $Registry

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Selected components: $($selectedComponents -join ', ')" -ForegroundColor Cyan
Write-Host "Registry: $Registry" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan
Write-Host ""

foreach ($component in $selectedComponents) {
    $composeFile = $componentComposeFiles[$component]
    $image = $componentImages[$component]

    if (-not $SkipBuild -and $buildableComponents -contains $component) {
        Write-Host "[BUILD] $component" -ForegroundColor Yellow
        docker compose -f $composeFile build $component
    }

    if ($component -in @("redis", "postgres") -and -not $SkipPull) {
        Write-Host "[PULL] $component" -ForegroundColor Yellow
        docker pull $image
    }

    $tarName = "policycollector-$component-$Version.tar"
    $tarPath = Join-Path $OutputDir $tarName

    Write-Host "[EXPORT] $component -> $tarPath" -ForegroundColor Green
    docker save -o $tarPath $image

    Write-Host "  Done: $tarName" -ForegroundColor Green
    Write-Host ""
}

Write-Host "Completed build/export for selected components." -ForegroundColor Cyan
