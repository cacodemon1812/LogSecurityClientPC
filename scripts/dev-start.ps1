# Start PolicyCollector dev environment
param(
    [string]$PostgresPassword = "devpassword",
    [string]$RedisPassword = "devredis",
    [string]$BackendApiKey = "dev-api-key-minimum-32-chars-here!!",
    [string]$GrafanaPassword = "devgrafana"
)

$ErrorActionPreference = "Stop"

$env:POSTGRES_PASSWORD = $PostgresPassword
$env:REDIS_PASSWORD = $RedisPassword
$env:BACKEND_API_KEY = $BackendApiKey
$env:GRAFANA_PASSWORD = $GrafanaPassword

Write-Host "Starting PolicyCollector dev environment..."
Write-Host "  Postgres password: ****"
Write-Host "  Redis password: ****"
Write-Host "  Backend API Key: ****"
Write-Host "  Grafana password: ****"
Write-Host ""

docker compose -f docker/compose.dev.yml up -d

Write-Host "Waiting for services to be healthy..."
$timeout = 120
$elapsed = 0
$unhealthy = $true

while ($unhealthy -and $elapsed -lt $timeout) {
    Start-Sleep -Seconds 3
    $elapsed += 3

    $ps = docker compose -f docker/compose.dev.yml ps --format json 2>$null | ConvertFrom-Json
    $unhealthy = $ps | Where-Object { $_.Health -ne "healthy" -and $_.Health -ne "" }

    if ($unhealthy) {
        Write-Host "Waiting for services... (${elapsed}s/${timeout}s)"
    }
}

if ($unhealthy) {
    Write-Host "Warning: Some services did not become healthy within timeout" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✓ Services ready:" -ForegroundColor Green
Write-Host "  Backend API:  http://localhost:8080" -ForegroundColor Cyan
Write-Host "  Grafana:      http://localhost:3000  (admin/$GrafanaPassword)" -ForegroundColor Cyan
Write-Host "  PostgreSQL:   localhost:5432" -ForegroundColor Cyan
Write-Host "  Redis:        localhost:6379" -ForegroundColor Cyan
Write-Host ""
Write-Host "Test ingest endpoint:"
Write-Host "  curl -X POST http://localhost:8080/api/v1/ingest \" -ForegroundColor Gray
Write-Host "    -H 'X-Api-Key: $BackendApiKey' \" -ForegroundColor Gray
Write-Host "    -H 'Content-Type: application/json' \" -ForegroundColor Gray
Write-Host "    -d @test/sample-payload.json" -ForegroundColor Gray
Write-Host ""
Write-Host "View logs:"
Write-Host "  docker compose -f docker/compose.dev.yml logs -f backend" -ForegroundColor Gray
