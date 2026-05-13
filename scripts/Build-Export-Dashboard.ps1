param(
    [string]$Version = "dev",
    [string]$Registry = "policycollector-local",
    [string]$OutputDir = "docker/exports/components",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "Build-Export-ComponentImages.ps1"

$params = @{
    Components = "dashboard"
    Version = $Version
    Registry = $Registry
    OutputDir = $OutputDir
}

if ($SkipBuild) { $params.SkipBuild = $true }

& $scriptPath @params
