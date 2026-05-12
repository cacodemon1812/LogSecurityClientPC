# Stop PolicyCollector dev environment

$ErrorActionPreference = "Stop"

Write-Host "Stopping PolicyCollector dev environment..."
docker compose -f docker/compose.dev.yml down

Write-Host "✓ Environment stopped" -ForegroundColor Green
Write-Host ""
Write-Host "To remove volumes (data loss):"
Write-Host "  docker compose -f docker/compose.dev.yml down -v" -ForegroundColor Gray
