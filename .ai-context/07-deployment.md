# 07 — Deployment Guide

## Build Agent

```powershell
# Self-contained single-file executable, Windows x64
dotnet publish src/PolicyCollector.Agent `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/agent

# Build MSI (yêu cầu WiX Toolset v4)
wix build installer/setup.wxs `
  -d Version=1.0.0 `
  -o dist/PolicyCollector-1.0.0-x64.msi
```

---

## Cấu trúc MSI

```
installer/setup.wxs:
  - Install dir: C:\Program Files\PolicyCollector\
  - Config dir:  C:\ProgramData\PolicyCollector\
  - Log dir:     C:\ProgramData\PolicyCollector\logs\
  - Set ACLs trên cả 3 thư mục
  - Tạo Windows Service (sc create)
  - Custom Action: nhận API key khi install dialog hoặc silent via property
  - Custom Action: lưu API key vào Credential Manager
  - Start service sau install
  - Stop + delete service khi uninstall
  - Giữ logs và config khi uninstall (ProgramData)

Silent install properties:
  BACKEND_URL    = "https://collector.internal/api/v1/ingest"
  API_KEY        = "your-key-here"
  INTERVAL_MIN   = "60"
```

---

## Phương thức triển khai hàng loạt

### Phương thức 1: GPO Software Installation (Domain)

```
Yêu cầu: MSI phải trên network share (DFS recommended)
         GPO Software Installation chỉ hỗ trợ per-computer install

Bước:
1. Copy MSI lên \\corp.local\NETLOGON\PolicyCollector\
2. Mở Group Policy Management Console
3. Tạo GPO mới: "Deploy PolicyCollector"
4. Computer Configuration → Software Settings → Software installation
5. New Package → chọn MSI từ network share (UNC path)
6. Deployment method: Assigned
7. Advanced → Modifications: chọn .mst nếu cần customize
8. Link GPO vào OU chứa máy target
9. Máy sẽ cài lúc reboot tiếp theo (hoặc gpupdate /force + reboot)

Giới hạn: Không thể pass API_KEY qua GPO Software Installation trực tiếp
  → Dùng .mst transform file hoặc deploy config qua separate GPO preference
```

### Phương thức 2: GPO + PowerShell Script (Recommended)

```
Ưu điểm: Full control, có thể pass tham số, không cần reboot

GPO:
  Computer Configuration → Windows Settings → Scripts → Startup

Script: \\corp.local\NETLOGON\PolicyCollector\Install-PolicyCollector.ps1

# Install-PolicyCollector.ps1
param()
$msiPath = "\\corp.local\NETLOGON\PolicyCollector\PolicyCollector-1.0.0-x64.msi"
$logPath = "C:\Windows\Temp\PolicyCollector-Install.log"
$apiKey  = "your-production-api-key"
$backendUrl = "https://collector.corp.local/api/v1/ingest"

# Kiểm tra đã cài chưa
$installed = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* |
  Where-Object DisplayName -eq "PolicyCollector"
if ($installed) { exit 0 }

# Silent install
$args = "/i `"$msiPath`" /qn /l*v `"$logPath`" BACKEND_URL=`"$backendUrl`" API_KEY=`"$apiKey`""
Start-Process msiexec.exe -ArgumentList $args -Wait -NoNewWindow
```

### Phương thức 3: Microsoft Intune (Modern Management)

```
1. Upload MSI lên Intune → Apps → Windows → Line-of-business app
2. App type: Windows app (Win32) — yêu cầu .intunewin package
   - Công cụ: Microsoft Win32 Content Prep Tool
   - intunewinapputil.exe -c dist -s PolicyCollector-1.0.0-x64.msi -o dist\intune

3. Install command:
   msiexec /i "PolicyCollector-1.0.0-x64.msi" /qn BACKEND_URL="https://..." API_KEY="..."

4. Uninstall command:
   msiexec /x {PRODUCT-GUID} /qn

5. Detection rule:
   Registry: HKLM\SOFTWARE\PolicyCollector\InstalledVersion exists

6. Assign to Device Group
```

### Phương thức 4: SCCM / Configuration Manager

```
1. Create Application → Windows Installer (*.msi)
2. Deployment type: Script Installer (nếu cần pass API_KEY)
3. Install program:
   msiexec /i PolicyCollector-1.0.0-x64.msi /qn BACKEND_URL="..." API_KEY="..."
4. Detection method: File exists C:\Program Files\PolicyCollector\PolicyCollector.Agent.exe
5. Deploy to Collection → Required (mandatory install)
```

---

## Phân phối cấu hình qua GPO (ADMX)

```
File cần tạo:
  installer/admx/PolicyCollector.admx   → Template definitions
  installer/admx/PolicyCollector.adml   → Ngôn ngữ (en-US, vi-VN)

Copy vào:
  \\corp.local\SYSVOL\corp.local\Policies\PolicyDefinitions\PolicyCollector.admx
  \\corp.local\SYSVOL\corp.local\Policies\PolicyDefinitions\en-US\PolicyCollector.adml

Sau đó trong GPMC:
  Computer Configuration → Administrative Templates → PolicyCollector
  - Backend URL
  - Collection Interval (minutes)
  - Enabled Modules (checkboxes)
```

---

## Cập nhật Agent

### Quy trình update

```
1. Build MSI phiên bản mới với ProductVersion bump
2. WiX: MajorUpgrade action tự động uninstall version cũ → cài mới
3. Dữ liệu trong ProgramData giữ nguyên (config, offline queue, logs)
4. Deploy qua cùng phương thức ban đầu (GPO / Intune / SCCM)

Lưu ý: Không cần stop service thủ công — MSI custom action xử lý
```

### Staged rollout

```
Phase 1: IT Lab (5-10 máy) — 1 tuần monitor
Phase 2: Pilot (50 máy đại diện mỗi OU) — 2 tuần
Phase 3: Toàn bộ — rolling theo OU
```

---

## Backend Deployment

### Docker Compose (small scale, ≤ 500 endpoints)

```bash
docker compose -f docker/compose.prod.yml up -d

# Cấu hình qua .env file (không commit .env vào git)
POSTGRES_PASSWORD=<strong-password>
REDIS_PASSWORD=<strong-password>
BACKEND_API_KEY=<32-byte-random>
BACKEND_HMAC_SECRET=<32-byte-random>
TLS_CERT_PATH=/certs/fullchain.pem
TLS_KEY_PATH=/certs/privkey.pem
```

### Kubernetes (medium scale, 500-5000 endpoints)

```yaml
# Xem k8s/ directory cho manifests
# Key components:
# - Deployment: backend (3 replicas)
# - Deployment: storage-worker (2 replicas)
# - Deployment: alert-worker (1 replica)
# - StatefulSet: postgres
# - StatefulSet: redis
# - Service: backend (ClusterIP + Ingress)
# - Secret: api-keys, db-passwords
# - ConfigMap: backend config
```

---

## Health Checks

### Agent

```
Kiểm tra service đang chạy:
  Get-Service PolicyCollectorSvc | Select-Object Status

Log location: C:\ProgramData\PolicyCollector\logs\agent-YYYY-MM-DD.log

Indicators hoạt động bình thường:
  "Collection cycle completed" — mỗi N phút
  "Payload sent successfully"  — sau mỗi collect
```

### Backend

```
GET /health           → 200 OK + { "status": "healthy", "db": "ok", "queue": "ok" }
GET /health/ready     → 200 khi DB + Redis ready (dùng cho k8s readiness probe)
GET /health/live      → 200 khi process alive (dùng cho k8s liveness probe)
```

---

## Rollback

```
Agent rollback:
  - Giữ MSI phiên bản cũ trên network share
  - Uninstall version mới: msiexec /x {NEW-GUID} /qn
  - Cài version cũ: msiexec /i OldVersion.msi /qn ...
  - Config trong ProgramData không bị ảnh hưởng

Backend rollback:
  - Docker: docker compose down && docker compose -f compose.prev.yml up -d
  - K8s: kubectl rollout undo deployment/backend
  - DB: chạy rollback SQL script thủ công (xem Data/Migrations/rollbacks/)
```
