# Start PolicyCollector dev environment
param(
    [string]$PostgresPassword = "devpassword",
    [string]$RedisPassword = "devredis",
    [string]$BackendApiKey = "dev-api-key-minimum-32-chars-here!!",
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$env:POSTGRES_PASSWORD = $PostgresPassword
$env:REDIS_PASSWORD = $RedisPassword
$env:BACKEND_API_KEY = $BackendApiKey

$composeFiles = @("-f", "docker/compose.yml", "-f", "docker/compose.dev.yml")
if ($Build) { $composeFiles += @("-f", "docker/compose.build.yml") }

Write-Host "Starting PolicyCollector dev environment..." -ForegroundColor Cyan
if ($Build) { Write-Host "  Mode: build from source" } else { Write-Host "  Mode: prebuilt images (use -Build to rebuild)" }
Write-Host ""

$upArgs = $composeFiles + @("up", "-d")
if ($Build) { $upArgs += "--build" }
& docker compose @upArgs

Write-Host ""
Write-Host "Waiting for services to be healthy..." -ForegroundColor Yellow
$timeout = 120
$elapsed = 0

while ($elapsed -lt $timeout) {
    Start-Sleep -Seconds 3
    $elapsed += 3

    try {
        $psArgs = $composeFiles + @("ps", "--format", "json")
        $ps = & docker compose @psArgs 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
        $notReady = @($ps | Where-Object { $_.Health -notin @("healthy", "") -and $_.State -eq "running" })
        $failed   = @($ps | Where-Object { $_.State -eq "exited" })

        if ($failed.Count -gt 0) {
            Write-Host "ERROR: Services exited: $($failed.Name -join ', ')" -ForegroundColor Red
            break
        }
        if ($notReady.Count -eq 0) { break }
        Write-Host "  Waiting... (${elapsed}s/${timeout}s) — $($notReady.Name -join ', ') not ready yet"
    } catch { }
}

Write-Host ""
Write-Host "Services ready:" -ForegroundColor Green
Write-Host "  Backend API : http://localhost:8080" -ForegroundColor Cyan
Write-Host "  Dashboard   : http://localhost:3000  (admin / Admin@123456)" -ForegroundColor Cyan
Write-Host "  PostgreSQL  : localhost:5432" -ForegroundColor Cyan
Write-Host "  Redis       : localhost:6379" -ForegroundColor Cyan
Write-Host ""
Write-Host "Logs:" -ForegroundColor Gray
Write-Host "  docker compose -f docker/compose.yml -f docker/compose.dev.yml logs -f backend" -ForegroundColor Gray
