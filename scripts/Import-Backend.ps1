param(
    [string]$Version = "dev",
    [string]$InputDir = "docker/exports/components",
    [string]$TarPath
)

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "Import-ComponentImage.ps1"

$params = @{
    Component = "backend"
    Version = $Version
    InputDir = $InputDir
}

if ($PSBoundParameters.ContainsKey("TarPath")) { $params.TarPath = $TarPath }

& $scriptPath @params
