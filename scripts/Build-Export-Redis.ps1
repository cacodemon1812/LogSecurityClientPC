param(
    [string]$Version = "dev",
    [string]$OutputDir = "docker/exports/components",
    [switch]$SkipPull
)

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "Build-Export-ComponentImages.ps1"

$params = @{
    Components = "redis"
    Version = $Version
    OutputDir = $OutputDir
}

if ($SkipPull) { $params.SkipPull = $true }

& $scriptPath @params
