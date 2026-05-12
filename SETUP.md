# PolicyCollector — Setup & Operations Guide

## Contents

1. [Prerequisites](#1-prerequisites)
2. [Dev environment (Docker Compose)](#2-dev-environment-docker-compose)
3. [Build Docker images](#3-build-docker-images)
4. [Build & install the Windows Agent service](#4-build--install-the-windows-agent-service)
5. [Agent configuration reference](#5-agent-configuration-reference)
6. [Backend configuration reference](#6-backend-configuration-reference)
7. [GPO override via ADMX](#7-gpo-override-via-admx)
8. [mTLS setup](#8-mtls-setup)
9. [First-time database seed](#9-first-time-database-seed)
10. [Production deployment notes](#10-production-deployment-notes)

---

## 1. Prerequisites

| Tool | Minimum version | Purpose |
|------|----------------|---------|
| .NET SDK | 8.0 | Build Agent + Backend |
| Docker Desktop / Docker Engine | 24+ | Run backend stack |
| WiX Toolset v4 (`wix` CLI) | 4.x | Build MSI installer |
| PowerShell | 5.1 or 7+ | Dev scripts |
| psql client | 14+ | Seed API key (optional) |

---

## 2. Dev environment (Docker Compose)

The full dev stack (TimescaleDB, Redis, Backend, Storage Worker, Alert Worker, Grafana) runs from one command.

```powershell
# Start all services (passwords are optional — defaults shown in brackets)
.\scripts\dev-start.ps1 `
    -PostgresPassword "devpassword" `
    -RedisPassword "devredis" `
    -BackendApiKey "dev-api-key-minimum-32-chars-here!!" `
    -GrafanaPassword "devgrafana"
```

After startup:

| Service | URL | Default credentials |
|---------|-----|---------------------|
| Backend API | http://localhost:8080 | — |
| Health check | http://localhost:8080/health | — |
| Grafana | http://localhost:3000 | admin / devgrafana |
| PostgreSQL | localhost:5432 | pcollector / devpassword |
| Redis | localhost:6379 | password: devredis |

**Stop all services:**

```powershell
.\scripts\dev-stop.ps1
```

**View logs:**

```powershell
docker compose -f docker/compose.dev.yml logs -f backend
docker compose -f docker/compose.dev.yml logs -f storage-worker
```

**Test the ingest endpoint:**

```powershell
$headers = @{ "X-Api-Key" = "dev-api-key-minimum-32-chars-here!!"; "Content-Type" = "application/json" }
Invoke-RestMethod -Method POST -Uri "http://localhost:8080/api/v1/ingest" `
    -Headers $headers -InFile "test/sample-payload.json"
```

---

## 3. Build Docker images

All images are built from the repo root. The Dockerfiles use multi-stage builds.

### Backend API image

```bash
docker build -f docker/Dockerfile -t policycollector-backend:latest .
```

### Worker image (Storage Worker + Alert Worker share the same image)

```bash
docker build -f docker/Dockerfile.worker -t policycollector-worker:latest .
```

### Build & push to a registry

```bash
TAG="v1.0.0"
REGISTRY="registry.corp.local"

docker build -f docker/Dockerfile -t $REGISTRY/policycollector-backend:$TAG .
docker build -f docker/Dockerfile.worker -t $REGISTRY/policycollector-worker:$TAG .

docker push $REGISTRY/policycollector-backend:$TAG
docker push $REGISTRY/policycollector-worker:$TAG
```

### Run with Docker Compose (production)

```bash
# Copy compose.prod.yml and edit the environment variables
docker compose -f docker/compose.prod.yml up -d
```

---

## 4. Build & install the Windows Agent service

### Option A — MSI installer (recommended for deployment)

Requires WiX 4 CLI installed (`dotnet tool install --global wix`).

```powershell
.\scripts\Build-Installer.ps1 -Version "1.0.0"
# Output: dist/PolicyCollector-1.0.0-x64.msi
```

**Silent install via Group Policy or SCCM:**

```cmd
msiexec /i "PolicyCollector-1.0.0-x64.msi" /qn /l*v "%TEMP%\PC-Install.log" ^
    BACKEND_URL="https://collector.corp.local/api/v1/ingest" ^
    API_KEY="your-32-char-minimum-api-key-here" ^
    HMAC_SECRET="your-base64-encoded-secret" ^
    INTERVAL_MIN="60"
```

The installer:
- Installs the agent to `%ProgramFiles%\PolicyCollector\`
- Writes initial `appsettings.json` to `%ProgramData%\PolicyCollector\`
- Saves the API key and HMAC secret to **Windows Credential Manager** (not on disk)
- Creates and starts the `PolicyCollectorSvc` Windows service (auto-start, LocalSystem)
- Configures the service to auto-restart on failure (3 attempts, 1-minute delay)

**Uninstall:**

```cmd
msiexec /x "PolicyCollector-1.0.0-x64.msi" /qn
```

### Option B — Manual install (for testing)

```powershell
# 1. Publish self-contained executable
dotnet publish src/PolicyCollector.Agent `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -o C:\PolicyCollector

# 2. Create the service
sc.exe create PolicyCollectorSvc `
    binPath= "C:\PolicyCollector\PolicyCollector.Agent.exe" `
    start= auto obj= LocalSystem

sc.exe description PolicyCollectorSvc "Collects system policy configuration"

sc.exe failure PolicyCollectorSvc reset= 86400 `
    actions= restart/60000/restart/60000/restart/60000

# 3. Configure (edit before starting)
notepad "C:\ProgramData\PolicyCollector\appsettings.json"

# 4. Start
sc.exe start PolicyCollectorSvc
```

**Service control commands:**

```powershell
Start-Service PolicyCollectorSvc
Stop-Service PolicyCollectorSvc
Restart-Service PolicyCollectorSvc
Get-Service PolicyCollectorSvc

# View logs
Get-Content "C:\ProgramData\PolicyCollector\logs\agent-*.log" -Tail 50
```

---

## 5. Agent configuration reference

Config file location: `%ProgramData%\PolicyCollector\appsettings.json`

```jsonc
{
  "Agent": {
    "IntervalMinutes": 60,          // Collection interval (15–1440 min)
    "CollectOnStartup": true,        // Run collection immediately on service start
    "CollectorTimeoutSeconds": 30,   // Per-collector timeout

    "Modules": {
      "GPO": true,                   // Group Policy Objects
      "SecurityPolicy": true,        // Password, UAC, TLS, RDP, Audit policy
      "Firewall": true,              // Windows Firewall profiles
      "Defender": true,              // Windows Defender status
      "BitLocker": true,             // BitLocker volume status
      "AppInventory": true,          // Installed Win32 apps
      "AppxPackages": false,         // UWP / AppX packages
      "Services": true,              // Windows Services
      "ScheduledTasks": true,        // Scheduled Tasks
      "StartupEntries": true         // Startup programs
    }
  },

  "Transport": {
    "BackendUrl": "https://collector.corp.local/api/v1/ingest",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "InitialRetryDelaySeconds": 10,
    "UseMtls": false,                // Set true to enable mTLS (see section 8)
    "ClientCertStore": "LocalMachine",
    "ClientCertThumbprint": ""       // Thumbprint of the client certificate
  },

  "LocalQueue": {
    "MaxAgeHours": 168,              // Discard queued payloads older than 7 days
    "MaxEntries": 1000,              // Maximum queued items
    "RetryIntervalMinutes": 5        // Retry interval for failed submissions
  },

  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [{
      "Name": "File",
      "Args": {
        "path": "C:\\ProgramData\\PolicyCollector\\logs\\agent-.log",
        "rollingInterval": "Day",
        "fileSizeLimitBytes": 10485760,
        "retainedFileCountLimit": 7
      }
    }]
  }
}
```

**Security notes for Agent config:**
- Never put the API key in `appsettings.json`. The MSI installer saves it to Windows Credential Manager automatically. For manual installs, use `cmdkey`:
  ```cmd
  cmdkey /generic:PolicyCollector/ApiKey /user:agent /pass:"your-api-key-here"
  ```
- The `%ProgramData%\PolicyCollector\` directory ACL is set to SYSTEM + Administrators only.
- Log directory ACL is similarly restricted.

---

## 6. Backend configuration reference

Configuration is injected via environment variables in Docker (12-factor), or via `appsettings.json` for bare-metal installs.

**Environment variable format:** Use `__` as the section separator (e.g., `Backend__ApiKey`).

```jsonc
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=policycollector;Username=pcollector;Password=CHANGE_ME",
    "Redis": "localhost:6379,password=CHANGE_ME,abortConnect=false"
  },

  "Backend": {
    "ApiKey": "",           // NOT stored here in production — use env var or secrets manager
    "HmacSecret": null,     // Optional: base64-encoded HMAC-SHA256 secret for payload signing
    "HmacRequired": false,  // Set true to require HMAC on all ingest requests
    "SupportedSchemaVersion": "1.0",
    "AlertWebhookUrl": null  // Optional: webhook URL for policy violation alerts
  }
}
```

**Key environment variables for Docker Compose:**

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__Postgres` | PostgreSQL connection string |
| `ConnectionStrings__Redis` | Redis connection string |
| `Backend__ApiKey` | API key for agent authentication (≥32 chars) |
| `Backend__HmacSecret` | HMAC secret (base64-encoded, optional) |
| `Backend__HmacRequired` | `true` to require HMAC signature |
| `Backend__AlertWebhookUrl` | Webhook URL for violation alerts |
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` |
| `ASPNETCORE_HTTP_PORTS` | HTTP port inside container (default `8080`) |

**Generate a secure API key:**

```bash
# Linux / WSL / Git Bash
openssl rand -base64 48

# PowerShell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

---

## 7. GPO override via ADMX

The ADMX template allows managing agent settings via Group Policy without touching local config files.

**Install ADMX:**

1. Copy `installer/admx/PolicyCollector.admx` to `%SystemRoot%\PolicyDefinitions\`
2. Copy `installer/admx/en-US/PolicyCollector.adml` to `%SystemRoot%\PolicyDefinitions\en-US\`
3. For domain policy: copy both files to `\\<DOMAIN>\SYSVOL\<DOMAIN>\Policies\PolicyDefinitions\`

**Available GPO settings** (`Computer Configuration > Administrative Templates > PolicyCollector`):

| Policy name | Registry path | Description |
|-------------|--------------|-------------|
| BackendUrl | `SOFTWARE\Policies\PolicyCollector\BackendUrl` | Override backend URL |
| IntervalMinutes | `SOFTWARE\Policies\PolicyCollector\IntervalMinutes` | Override collection interval (15–1440) |
| EnableGPO | `SOFTWARE\Policies\PolicyCollector\Modules\GPO` | Enable/disable GPO collection module |

GPO settings override `appsettings.json` at runtime. The agent reads from `HKLM\SOFTWARE\Policies\PolicyCollector` on each collection cycle.

---

## 8. mTLS setup

For production, enable mutual TLS between agents and the backend.

### Backend side

Terminate TLS at a reverse proxy (nginx, Traefik, or IIS) with client certificate validation, or configure Kestrel directly in `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": { "Path": "/certs/server.pfx", "Password": "..." },
        "ClientCertificateMode": "RequireCertificate"
      }
    }
  }
}
```

### Agent side

1. Issue a client certificate from your internal CA for each managed machine.
2. Import the certificate into the `LocalMachine` store.
3. In `appsettings.json`:

```json
{
  "Transport": {
    "UseMtls": true,
    "ClientCertStore": "LocalMachine",
    "ClientCertThumbprint": "AABBCC..." 
  }
}
```

The certificate thumbprint can also be set via GPO (see section 7) or registry:
`HKLM\SOFTWARE\Policies\PolicyCollector\ClientCertThumbprint`

---

## 9. First-time database seed

The backend runs DbUp migrations automatically on startup. After the first startup, seed the API key:

**Using the provided script (requires psql on PATH):**

```bash
./scripts/seed-api-key.sh "your-api-key-here" localhost 5432 policycollector pcollector devpassword
```

**Manually with psql:**

```sql
INSERT INTO api_keys (key_hash, prefix, active, created_at, expires_at, description)
VALUES (
    crypt('your-api-key-here', gen_salt('bf')),
    LEFT('your-api-key-here', 8),
    true,
    NOW(),
    NOW() + INTERVAL '1 year',
    'Production API Key'
) ON CONFLICT DO NOTHING;
```

> The `crypt()` function requires the `pgcrypto` extension. The migration scripts enable it automatically.

**Verify the backend accepts the key:**

```bash
curl -s -o /dev/null -w "%{http_code}" \
    -H "X-Api-Key: your-api-key-here" \
    http://localhost:8080/health/ready
# Expected: 200
```

---

## 10. Production deployment notes

### Minimum recommended setup

- 2 replicas of `policycollector-backend` behind a load balancer
- 2 replicas of `policycollector-worker` (storage + alert each)
- PostgreSQL with TimescaleDB (managed or self-hosted)
- Redis 7+ with AOF persistence

### Security checklist

- [ ] Replace all default passwords (`devpassword`, `devredis`, etc.)
- [ ] Generate a new API key (≥32 bytes entropy) — see section 6
- [ ] Set `Backend__HmacRequired: true` and configure `Backend__HmacSecret` for payload integrity
- [ ] Enable mTLS (section 8) or terminate TLS at a proxy with certificate pinning
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` — disables detailed error pages
- [ ] Restrict `%ProgramData%\PolicyCollector\` ACL to SYSTEM + Administrators only
- [ ] Rotate API keys annually (update `api_keys` table, then redeploy agents)
- [ ] Enable TimescaleDB compression policy for `collection_snapshots` (reduces storage ~80%)

### TimescaleDB compression (run after 7+ days of data)

```sql
-- Enable compression on the snapshots hypertable
ALTER TABLE collection_snapshots SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'hostname'
);

-- Compress chunks older than 7 days automatically
SELECT add_compression_policy('collection_snapshots', INTERVAL '7 days');
```

### Log retention

Agent logs are capped at 7 files × 10 MB = 70 MB per machine by default.

For centralized logging, add a Serilog sink to the agent config:

```json
{
  "Serilog": {
    "WriteTo": [
      { "Name": "File", "Args": { "path": "..." } },
      {
        "Name": "Seq",
        "Args": { "serverUrl": "https://seq.corp.local" }
      }
    ]
  }
}
```

### Health endpoints

| Path | Description |
|------|-------------|
| `GET /health` | Combined live + ready check |
| `GET /health/live` | Liveness (process alive) |
| `GET /health/ready` | Readiness (DB + Redis reachable) |

Use `/health/ready` for load balancer health checks and Kubernetes readiness probes.
