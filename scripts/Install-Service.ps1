#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install/Update/Remove PolicyCollector Windows Service.

.PARAMETER Action
    install  — copy binaries, create service (default)
    update   — stop service, replace binaries, restart service
    remove   — stop service, delete service, optionally keep data

.PARAMETER SourceDir
    Path to the directory containing PolicyCollector.Agent.exe.
    Defaults to <script_parent>\dist\agent.

.PARAMETER BackendUrl
    URL of the ingest endpoint, e.g. https://backend.corp.local/api/v1/ingest

.PARAMETER ApiKey
    API key to authenticate with the backend (written to appsettings.json).

.PARAMETER ServiceAccount
    Windows account under which the service runs.
    Supported values:
      LocalSystem              — (default) full privileges; secedit/auditpol/gpresult work
      NT AUTHORITY\NetworkService — network access, no local admin; policy collectors partial
      NT AUTHORITY\LocalService   — no network; policy collectors partial
      DOMAIN\User              — custom domain/local account; must supply -ServicePassword
                                  and the account must be in local Administrators for full data
.PARAMETER ServicePassword
    Password for a custom domain/local service account.
    Leave empty for built-in accounts (LocalSystem, NetworkService, LocalService).

.NOTES
    When the service runs as a non-Administrator account the following collectors
    will return partial data (registry-based checks still work):
      SecurityPolicy — PasswordPolicy and UserRights empty (secedit requires admin)
                       AuditPolicy empty (auditpol requires SeSecurityPrivilege)
      GPO            — may fail (gpresult /SCOPE COMPUTER requires admin)
      BitLocker      — may fail (Get-BitLockerVolume requires admin)
    The RegistryAudit and ActiveDirectory collectors work without elevation.

.EXAMPLE
    .\Install-Service.ps1 -BackendUrl "https://backend.corp.local/api/v1/ingest" -ApiKey "secret"

    .\Install-Service.ps1 -BackendUrl "https://backend.corp.local/api/v1/ingest" -ApiKey "secret" `
        -ServiceAccount "CORP\svc-policycollector" -ServicePassword "P@ssw0rd"

    .\Install-Service.ps1 -Action update

    .\Install-Service.ps1 -Action remove
#>

param(
    [ValidateSet("install", "update", "remove")]
    [string]$Action = "install",

    [string]$SourceDir = "",

    [string]$BackendUrl = "",
    [string]$ApiKey = "",

    [string]$InstallDir = "C:\Program Files\PolicyCollector",
    [string]$DataDir = "C:\ProgramData\PolicyCollector",

    [string]$ServiceName = "PolicyCollectorSvc",
    [string]$ServiceDisplayName = "PolicyCollector Security Agent",
    [string]$ServiceDescription = "Collects security policy snapshots and forwards them to the backend.",

    [string]$ServiceAccount = "LocalSystem",
    [string]$ServicePassword = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── helpers ─────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)] [string[]]$Arguments,
        [Parameter(Mandatory = $true)] [string]$ErrorContext
    )
    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "sc.exe failed while $ErrorContext (exit code $LASTEXITCODE).`n$details"
    }
    return $output
}

function Stop-ServiceIfRunning {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Write-Step "Stopping $ServiceName..."
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus("Stopped", "00:00:30")
        Write-Ok "Service stopped"
    }
}

# Returns $true for built-in high-privilege accounts that do not need a password
function IsBuiltInAccount([string]$account) {
    $builtIn = @(
        "LocalSystem",
        "",
        "NT AUTHORITY\SYSTEM",
        "NT AUTHORITY\NetworkService",
        "NT AUTHORITY\LocalService"
    )
    return $builtIn -contains $account
}

# Returns $true when the account is expected to have local admin rights
function IsElevatedAccount([string]$account) {
    return ($account -eq "LocalSystem" -or $account -eq "" -or $account -eq "NT AUTHORITY\SYSTEM")
}

# ── resolve source dir ───────────────────────────────────────────────────────

if ($SourceDir -eq "") {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $SourceDir = Join-Path (Split-Path -Parent $scriptRoot) "dist\agent"
}

$agentExe = Join-Path $SourceDir "PolicyCollector.Agent.exe"

# ── REMOVE ───────────────────────────────────────────────────────────────────

if ($Action -eq "remove") {
    Write-Step "Removing $ServiceName..."
    Stop-ServiceIfRunning

    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Invoke-Sc -Arguments @("delete", $ServiceName) -ErrorContext "deleting service '$ServiceName'" | Out-Null
        Write-Ok "Service deleted"
    }
    else {
        Write-Warn "Service not found — nothing to delete"
    }

    if (Test-Path $InstallDir) {
        Remove-Item -Recurse -Force $InstallDir
        Write-Ok "Removed $InstallDir"
    }

    Write-Host ""
    Write-Host "Data directory $DataDir was NOT removed." -ForegroundColor Yellow
    Write-Host "Delete manually if no longer needed: Remove-Item -Recurse '$DataDir'"
    exit 0
}

# ── validate source for install/update ───────────────────────────────────────

if (-not (Test-Path $agentExe)) {
    Write-Error "Agent exe not found at: $agentExe`nRun 'dotnet publish' first or set -SourceDir."
    exit 1
}

# ── UPDATE ───────────────────────────────────────────────────────────────────

if ($Action -eq "update") {
    Write-Step "Updating $ServiceName..."
    Stop-ServiceIfRunning

    Write-Step "Copying new binaries to $InstallDir..."
    Copy-Item -Path "$SourceDir\*" -Destination $InstallDir -Recurse -Force -Exclude "appsettings.json"
    Write-Ok "Binaries updated"

    Start-Service -Name $ServiceName
    Write-Ok "Service restarted"
    exit 0
}

# ── INSTALL ───────────────────────────────────────────────────────────────────

Write-Step "Installing PolicyCollector Agent..."
Write-Host "  Source   : $SourceDir"
Write-Host "  Install  : $InstallDir"
Write-Host "  Data     : $DataDir"
Write-Host "  Service  : $ServiceName"
Write-Host "  Account  : $ServiceAccount"

# Warn when running as a non-admin account — some collectors will have partial data
if (-not (IsElevatedAccount $ServiceAccount)) {
    Write-Host ""
    Write-Warn "Service account '$ServiceAccount' is not LocalSystem."
    Write-Warn "The following collectors will return partial data (registry-based checks still work):"
    Write-Warn "  SecurityPolicy — PasswordPolicy/AuditPolicy empty (secedit + auditpol require admin)"
    Write-Warn "  GPO            — may fail (gpresult /SCOPE COMPUTER requires admin)"
    Write-Warn "  BitLocker      — may fail (Get-BitLockerVolume requires admin)"
    Write-Warn "Add the account to the local Administrators group for full data collection."
    Write-Host ""
}

# 1. Create directories
foreach ($dir in @($InstallDir, "$DataDir\logs")) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Ok "Created $dir"
    }
}

# 2. Copy binaries (skip appsettings.json if config already exists)
$configDest = "$DataDir\appsettings.json"
Copy-Item -Path "$SourceDir\*" -Destination $InstallDir -Recurse -Force -Exclude "appsettings.json"
Write-Ok "Binaries copied"

# 3. Write appsettings.json to DataDir (if it doesn't exist yet)
if (-not (Test-Path $configDest)) {
    $resolvedBackendUrl = if ($BackendUrl -ne "") { $BackendUrl } else { "https://BACKEND_HOST/api/v1/ingest" }
    $resolvedApiKey     = if ($ApiKey -ne "")     { $ApiKey }     else { "REPLACE_WITH_API_KEY" }

    $config = @"
{
  "Agent": {
    "IntervalMinutes": 60,
    "CollectOnStartup": true,
    "CollectorTimeoutSeconds": 30,
    "Modules": {
      "GPO": true,
      "SecurityPolicy": true,
      "Firewall": true,
      "Defender": true,
      "BitLocker": true,
      "AppInventory": true,
      "AppxPackages": false,
      "Services": true,
      "ScheduledTasks": true,
      "StartupEntries": true,
      "ActiveDirectory": true,
      "RegistryAudit": true
    }
  },
  "Transport": {
    "BackendUrl": "$resolvedBackendUrl",
    "ApiKey": "$resolvedApiKey",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "InitialRetryDelaySeconds": 10,
    "UseMtls": false,
    "ClientCertStore": "LocalMachine",
    "ClientCertThumbprint": ""
  },
  "LocalQueue": {
    "MaxAgeHours": 168,
    "MaxEntries": 1000,
    "RetryIntervalMinutes": 5
  },
  "Serilog": {
    "Using": ["Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "C:\\ProgramData\\PolicyCollector\\logs\\agent-.log",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": 10485760,
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
"@
    Set-Content -Path $configDest -Value $config -Encoding utf8
    Write-Ok "Config written to $configDest"

    if ($resolvedBackendUrl -like "*BACKEND_HOST*" -or $resolvedApiKey -like "*REPLACE*") {
        Write-Warn "Edit $configDest and set BackendUrl + ApiKey before starting the service!"
    }
}
else {
    Write-Warn "Config already exists at $configDest — skipped (not overwritten)"
}

# 4. Set ACLs on DataDir for the service account
$acl = Get-Acl $DataDir

# Always ensure SYSTEM has full control
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "NT AUTHORITY\SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($systemRule)

# Grant the service account FullControl if it is not LocalSystem / SYSTEM
if (-not (IsElevatedAccount $ServiceAccount)) {
    $svcRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceAccount, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($svcRule)
    Write-Ok "Permissions granted to '$ServiceAccount' on $DataDir"
}

Set-Acl -Path $DataDir -AclObject $acl
Write-Ok "Permissions set on $DataDir"

# 5. Create or update Windows service
$exePath = Join-Path $InstallDir "PolicyCollector.Agent.exe"
$binPath = "`"$exePath`" --contentRoot `"$DataDir`""

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Warn "Service already exists — updating binary path"
    Invoke-Sc -Arguments @("config", $ServiceName, "binPath=", $binPath) `
              -ErrorContext "updating binary path for service '$ServiceName'" | Out-Null
}
else {
    if (IsBuiltInAccount $ServiceAccount) {
        # Built-in accounts: New-Service handles LocalSystem; use sc.exe for NetworkService/LocalService
        if ($ServiceAccount -eq "LocalSystem" -or $ServiceAccount -eq "") {
            New-Service `
                -Name $ServiceName `
                -BinaryPathName $binPath `
                -DisplayName $ServiceDisplayName `
                -Description $ServiceDescription `
                -StartupType Automatic | Out-Null
        }
        else {
            # NetworkService / LocalService via sc.exe (New-Service doesn't accept these directly)
            Invoke-Sc -Arguments @(
                "create", $ServiceName,
                "binPath=",    $binPath,
                "start=",      "auto",
                "obj=",        $ServiceAccount,
                "DisplayName=", $ServiceDisplayName
            ) -ErrorContext "creating service '$ServiceName'" | Out-Null
        }
    }
    else {
        # Custom domain/local account — requires password
        if ($ServicePassword -eq "") {
            throw "ServicePassword is required when ServiceAccount is not a built-in account. " +
                  "Provide -ServicePassword or use LocalSystem / NetworkService / LocalService."
        }

        Invoke-Sc -Arguments @(
            "create", $ServiceName,
            "binPath=",    $binPath,
            "start=",      "auto",
            "obj=",        $ServiceAccount,
            "password=",   $ServicePassword,
            "DisplayName=", $ServiceDisplayName
        ) -ErrorContext "creating service '$ServiceName'" | Out-Null
    }

    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        throw "Service '$ServiceName' was not found after create command completed."
    }

    Write-Ok "Service created"
}

Invoke-Sc -Arguments @("description", $ServiceName, $ServiceDescription) `
          -ErrorContext "setting description for service '$ServiceName'" | Out-Null

# 6. Configure failure recovery: restart after 1 min / 2 min / 5 min
Invoke-Sc -Arguments @("failure", $ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/120000/restart/300000") `
          -ErrorContext "configuring failure recovery for service '$ServiceName'" | Out-Null
Write-Ok "Failure recovery configured"

# 7. Done
Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Edit config : $configDest"
Write-Host "     - Set Transport.BackendUrl"
Write-Host "     - Set Transport.ApiKey"
Write-Host ""
Write-Host "  2. Start service:"
Write-Host "     Start-Service $ServiceName"
Write-Host ""
Write-Host "  3. Check status:"
Write-Host "     Get-Service $ServiceName"
Write-Host "     Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Host ""
Write-Host "  4. View logs:"
Write-Host "     Get-Content '$DataDir\logs\agent-*.log' -Tail 50"
