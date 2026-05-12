param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

Write-Host "Building PolicyCollector MSI Installer v$Version"
Write-Host ""

# 1. Build Agent (self-contained)
Write-Host "[1/4] Building agent executable..."
$agentBinPath = "installer/bin/agent"
Remove-Item -Path $agentBinPath -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/PolicyCollector.Agent `
    -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $agentBinPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Agent build failed"
    exit 1
}
Write-Host "  ✓ Agent built successfully`n"

# 2. Build Custom Action DLL
Write-Host "[2/4] Building custom actions DLL..."
$customActionPath = "installer/CustomActions"
dotnet build "$customActionPath/PolicyCollector.CA.csproj" `
    -c Release `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Custom Actions build failed"
    exit 1
}
Write-Host "  ✓ Custom actions built successfully`n"

# 3. Build MSI with WiX
Write-Host "[3/4] Building MSI..."
$msiOutput = "dist/PolicyCollector-$Version-x64.msi"
Remove-Item -Path $msiOutput -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path dist -Force | Out-Null

# Generate GUIDs for components (in production, these should be stable/consistent)
$agentExeGuid = [guid]::NewGuid().ToString()
$configGuid = [guid]::NewGuid().ToString()
$installAclGuid = [guid]::NewGuid().ToString()
$dataAclGuid = [guid]::NewGuid().ToString()
$serviceGuid = [guid]::NewGuid().ToString()

wix build installer/setup.wxs `
    -d Version=$Version `
    -d AgentBin=$agentBinPath `
    -d CustomActionBin="installer/CustomActions/bin/Release" `
    -d SourceDir="src/PolicyCollector.Agent" `
    -d AgentExeGuid=$agentExeGuid `
    -d ConfigGuid=$configGuid `
    -d InstallAclGuid=$installAclGuid `
    -d DataAclGuid=$dataAclGuid `
    -d ServiceGuid=$serviceGuid `
    -o $msiOutput

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: MSI build failed"
    exit 1
}
Write-Host "  ✓ MSI built successfully`n"

# 4. Verify output
Write-Host "[4/4] Verifying output..."
if (Test-Path $msiOutput) {
    $size = (Get-Item $msiOutput).Length / 1MB
    Write-Host "  ✓ MSI created: $msiOutput ($([Math]::Round($size, 2)) MB)`n"
} else {
    Write-Host "ERROR: MSI file not found after build"
    exit 1
}

Write-Host "========================================"
Write-Host "Build completed successfully!"
Write-Host "========================================"
Write-Host ""
Write-Host "MSI Path: $((Resolve-Path $msiOutput).Path)"
Write-Host ""
Write-Host "Silent install example:"
Write-Host "  msiexec /i `"$msiOutput`" /qn /l*v `"%TEMP%\PC-Install.log`" \"
Write-Host "    BACKEND_URL=`"https://collector.corp.local/api/v1/ingest`" \"
Write-Host "    API_KEY=`"your-32-char-minimum-api-key-here`" \"
Write-Host "    HMAC_SECRET=`"your-base64-encoded-secret`" \"
Write-Host "    INTERVAL_MIN=`"60`""
Write-Host ""
