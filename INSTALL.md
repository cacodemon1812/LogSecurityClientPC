# PolicyCollector — Hướng dẫn Cài đặt & Kiểm tra

## Mục lục

1. [Tổng quan kiến trúc](#1-tổng-quan-kiến-trúc)
2. [Yêu cầu hệ thống](#2-yêu-cầu-hệ-thống)
3. [Build source code](#3-build-source-code)
4. [Cài đặt Backend (Docker)](#4-cài-đặt-backend-docker)
5. [Cài đặt Agent (Windows Service)](#5-cài-đặt-agent-windows-service)
6. [Cấu hình](#6-cấu-hình)
7. [Kiểm tra từng bước](#7-kiểm-tra-từng-bước)
8. [Xử lý lỗi thường gặp](#8-xử-lý-lỗi-thường-gặp)

---

## 1. Tổng quan kiến trúc

```
[Windows PC]                    [Docker / Linux Server]
PolicyCollector.Agent.exe  ──►  Backend API (port 8080)
  - Thu thập GPO, Firewall,         │
    Defender, BitLocker,            ▼ Redis queue
    Apps, Services...           StorageWorker ──► TimescaleDB (PostgreSQL)
  - Gửi JSON qua HTTP POST      AlertWorker ──► Webhook alerts
  - Local queue khi offline     Grafana (port 3000) ──► Dashboard
```

Luồng dữ liệu:
1. Agent thu thập snapshot → POST `/api/v1/ingest`
2. Backend nhận, đẩy vào **Redis queue**
3. **StorageWorker** lấy từ queue, lưu vào **TimescaleDB**, chạy diff và phát hiện vi phạm
4. **AlertWorker** gửi webhook khi có vi phạm nghiêm trọng

---

## 2. Yêu cầu hệ thống

### Backend server (Linux / Docker)

| Phần mềm | Phiên bản tối thiểu |
|----------|---------------------|
| Docker Engine | 24+ |
| Docker Compose | v2 (plugin, không phải v1) |
| RAM | 2 GB (dev), 4 GB (prod) |
| Disk | 10 GB (dev) |

### Build machine (Windows)

| Phần mềm | Phiên bản |
|----------|-----------|
| .NET SDK | 8.0+ |
| PowerShell | 5.1 hoặc 7+ |
| Git | bất kỳ |

### Máy cài Agent

| Yêu cầu | Ghi chú |
|---------|---------|
| Windows 10/11 hoặc Windows Server 2019+ | |
| .NET runtime | **Không cần** — agent là self-contained exe |
| Quyền Administrator | Để cài Windows service |
| Kết nối mạng tới Backend | Port 8080 (HTTP) hoặc 443 (HTTPS prod) |

---

## 3. Build source code

Tất cả lệnh chạy từ thư mục gốc repo (`d:\3.Code\LogSecurityClientPC`).

### 3.1 Build Agent (Windows exe)

```powershell
dotnet publish src/PolicyCollector.Agent `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o dist/agent/
```

Output: `dist/agent/PolicyCollector.Agent.exe` (~82 MB, self-contained, không cần .NET runtime)

### 3.2 Build Docker images (Backend + Worker)

```bash
# Backend API
docker build -f docker/Dockerfile -t policycollector-backend:dev .

# Worker (dùng chung cho StorageWorker và AlertWorker)
docker build -f docker/Dockerfile.worker -t policycollector-worker:dev .
```

> **Lưu ý:** Build Docker phải chạy từ **repo root** (không phải thư mục docker/),
> vì Dockerfile cần copy toàn bộ src/.

### 3.3 Build tất cả cùng lúc (dev)

```powershell
# Build agent exe
dotnet publish src/PolicyCollector.Agent -c Release -r win-x64 --self-contained true -o dist/agent/

# Build và khởi động docker stack (tự build image nếu chưa có)
.\scripts\dev-start.ps1
```

---

## 4. Cài đặt Backend (Docker)

### 4.1 Khởi động môi trường dev

```powershell
# Dùng mật khẩu mặc định (dev only)
.\scripts\dev-start.ps1

# Hoặc tùy chỉnh mật khẩu
.\scripts\dev-start.ps1 `
    -PostgresPassword "myPgPass123" `
    -RedisPassword "myRedisPass" `
    -BackendApiKey "my-api-key-minimum-32-characters!!" `
    -GrafanaPassword "myGrafana"
```

Script sẽ:
1. Set các biến môi trường
2. Chạy `docker compose -f docker/compose.dev.yml up -d`
3. Chờ tất cả service healthy (timeout 120s)

Sau khi khởi động xong:

| Service | URL | Thông tin |
|---------|-----|-----------|
| Backend API | http://localhost:8080 | API Key: `dev-api-key-minimum-32-chars-here!!` |
| Health check | http://localhost:8080/health | Không cần API key |
| Grafana | http://localhost:3000 | admin / devgrafana |
| PostgreSQL | localhost:5432 | pcollector / devpassword |
| Redis | localhost:6379 | password: devredis |

### 4.2 Kiểm tra các service đang chạy

```bash
docker compose -f docker/compose.dev.yml ps
```

Kết quả mong đợi — tất cả `STATUS` là `running (healthy)`:

```
NAME               STATUS              PORTS
backend            running (healthy)   0.0.0.0:8080->8080/tcp
postgres           running (healthy)   0.0.0.0:5432->5432/tcp
redis              running (healthy)   0.0.0.0:6379->6379/tcp
storage-worker     running             
alert-worker       running             
grafana            running (healthy)   0.0.0.0:3000->3000/tcp
```

### 4.3 Xem log

```bash
# Log backend
docker compose -f docker/compose.dev.yml logs -f backend

# Log storage worker
docker compose -f docker/compose.dev.yml logs -f storage-worker

# Log tất cả
docker compose -f docker/compose.dev.yml logs -f
```

### 4.4 Dừng và dọn dẹp

```powershell
# Dừng (giữ nguyên data)
.\scripts\dev-stop.ps1

# Dừng và xóa toàn bộ data (volumes)
docker compose -f docker/compose.dev.yml down -v
```

---

## 5. Cài đặt Agent (Windows Service)

### 5.1 Cài nhanh (khuyến nghị)

Chạy PowerShell với quyền **Administrator**:

```powershell
cd "d:\3.Code\LogSecurityClientPC"

.\scripts\Install-Service.ps1 `
    -BackendUrl "http://localhost:8080/api/v1/ingest" `
    -ApiKey "dev-api-key-minimum-32-chars-here!!"
```

Script tạo:
- `C:\Program Files\PolicyCollector\` — file thực thi
- `C:\ProgramData\PolicyCollector\appsettings.json` — config (chỉnh sửa được)
- `C:\ProgramData\PolicyCollector\logs\` — log file
- Windows Service `PolicyCollectorSvc` (auto-start, LocalSystem)
- Failure recovery: tự restart sau 1 phút / 2 phút / 5 phút

### 5.2 Khởi động service

```powershell
# Khởi động
Start-Service PolicyCollectorSvc

# Kiểm tra trạng thái
Get-Service PolicyCollectorSvc

# Xem log realtime
Get-Content "C:\ProgramData\PolicyCollector\logs\agent-*.log" -Tail 50 -Wait
```

### 5.3 Các lệnh quản lý service

```powershell
Start-Service   PolicyCollectorSvc
Stop-Service    PolicyCollectorSvc
Restart-Service PolicyCollectorSvc

# Xóa service
.\scripts\Install-Service.ps1 -Action remove

# Cập nhật binary sau khi build mới
.\scripts\Install-Service.ps1 -Action update
```

### 5.4 Chạy thử không cài service (test nhanh)

Có thể chạy trực tiếp `dist\agent\PolicyCollector.Agent.exe` để kiểm tra trước khi cài service.
Agent sẽ đọc `appsettings.json` trong cùng thư mục.

Trước khi chạy, chỉnh `Transport.BackendUrl` và `Transport.ApiKey` trong `dist\agent\appsettings.json`:

```json
{
  "Transport": {
    "BackendUrl": "http://localhost:8080/api/v1/ingest",
    "ApiKey": "dev-api-key-minimum-32-chars-here!!"
  }
}
```

> Agent chạy bằng double-click sẽ dừng ngay khi đóng cửa sổ console.
> Để chạy liên tục → cài làm service (mục 5.1).

---

## 6. Cấu hình

### 6.1 Cấu hình Agent

File: `C:\ProgramData\PolicyCollector\appsettings.json`

```jsonc
{
  "Agent": {
    "IntervalMinutes": 60,         // Thu thập mỗi 60 phút
    "CollectOnStartup": true,      // Thu thập ngay khi service khởi động
    "CollectorTimeoutSeconds": 30, // Timeout mỗi collector

    "Modules": {
      "GPO": true,            // Group Policy Objects
      "SecurityPolicy": true, // Password, UAC, RDP, TLS, Audit policy
      "Firewall": true,       // Windows Firewall profiles
      "Defender": true,       // Windows Defender / AV status
      "BitLocker": true,      // Trạng thái mã hóa BitLocker
      "AppInventory": true,   // Danh sách ứng dụng đã cài (Win32)
      "AppxPackages": false,  // Ứng dụng UWP (Store apps)
      "Services": true,       // Windows Services
      "ScheduledTasks": true, // Scheduled Tasks
      "StartupEntries": true  // Chương trình khởi động cùng Windows
    }
  },

  "Transport": {
    "BackendUrl": "http://BACKEND_HOST:8080/api/v1/ingest",
    "ApiKey": "YOUR_API_KEY_HERE",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "InitialRetryDelaySeconds": 10,
    "UseMtls": false              // true nếu dùng mTLS (xem SETUP.md mục 8)
  },

  "LocalQueue": {
    "MaxAgeHours": 168,           // Giữ payload tối đa 7 ngày khi offline
    "MaxEntries": 1000,
    "RetryIntervalMinutes": 5
  },

  "Serilog": {
    "Using": ["Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    },
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

Sau khi sửa config, restart service để áp dụng:

```powershell
Restart-Service PolicyCollectorSvc
```

### 6.2 Cấu hình Backend (biến môi trường Docker)

| Biến | Bắt buộc | Mô tả |
|------|----------|-------|
| `ConnectionStrings__Postgres` | Có | PostgreSQL connection string |
| `ConnectionStrings__Redis` | Có | Redis connection string |
| `Backend__ApiKey` | Có | API key xác thực agent (≥ 32 ký tự) |
| `Backend__HmacSecret` | Không | Secret HMAC-SHA256 (base64) để ký payload |
| `Backend__HmacRequired` | Không | `true` = bắt buộc HMAC (mặc định `false`) |
| `Backend__SupportedSchemaVersion` | Không | Schema version hỗ trợ (mặc định `"1.0"`) |
| `Backend__AlertWebhookUrl` | Không | Webhook URL khi phát hiện vi phạm |
| `WORKER_TYPE` | Không | `storage` hoặc `alert` để chạy worker-only mode |
| `ASPNETCORE_ENVIRONMENT` | Không | `Development` hoặc `Production` |

**Sinh API key an toàn:**

```powershell
# PowerShell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

---

## 7. Kiểm tra từng bước

Tất cả lệnh `curl` có thể thay bằng PowerShell `Invoke-RestMethod` hoặc dùng Postman/Insomnia.

**Biến dùng xuyên suốt:**
```powershell
$BASE = "http://localhost:8080"
$KEY  = "dev-api-key-minimum-32-chars-here!!"
$H    = @{ "X-Api-Key" = $KEY; "Content-Type" = "application/json" }
```

---

### 7.1 Kiểm tra Backend health

```powershell
Invoke-RestMethod "$BASE/health"
# Hoặc
curl http://localhost:8080/health
```

Kết quả mong đợi:
```json
{"status":"Healthy","results":{"database":{"status":"Healthy"},"redis":{"status":"Healthy"}}}
```

Nếu kết quả `Degraded` hoặc `Unhealthy` → xem log:
```bash
docker compose -f docker/compose.dev.yml logs backend --tail 30
```

---

### 7.2 Gửi snapshot thử (ingest test)

Tạo file `test-payload.json` (hoặc copy nội dung bên dưới):

```json
{
  "schema_version": "1.0",
  "collection_id": "11111111-2222-3333-4444-555555555555",
  "collected_at": "2026-05-13T10:00:00+07:00",
  "agent_version": "1.0.0",
  "host": {
    "hostname": "TEST-PC-01",
    "fqdn": "TEST-PC-01.corp.local",
    "domain": "corp.local",
    "domain_joined": true,
    "os_name": "Windows 11 Pro",
    "os_version": "10.0.22631",
    "os_build": "22631.3447",
    "os_edition": "Professional",
    "architecture": "x64",
    "ip_addresses": ["192.168.1.100"]
  },
  "security_policy": {
    "min_password_length": 12,
    "password_complexity": true,
    "max_password_age": 90,
    "uac_enabled": true,
    "remote_desktop_enabled": false,
    "rdp_nla_required": true,
    "screen_lock_timeout": 15
  },
  "firewall": {
    "domain_profile_enabled": true,
    "private_profile_enabled": true,
    "public_profile_enabled": true
  },
  "defender": {
    "antivirus_enabled": true,
    "real_time_protection": true,
    "definitions_up_to_date": true,
    "last_scan": "2026-05-12T23:00:00Z"
  },
  "bitlocker": [
    {
      "drive_letter": "C:",
      "protection_status": "On",
      "encryption_method": "XtsAes256",
      "key_protectors": ["RecoveryPassword", "Tpm"]
    }
  ],
  "applications": [
    {
      "display_name": "Google Chrome",
      "display_version": "124.0.6367.91",
      "publisher": "Google LLC",
      "install_date": "20240101"
    },
    {
      "display_name": "Microsoft Visual Studio Code",
      "display_version": "1.89.0",
      "publisher": "Microsoft Corporation",
      "install_date": "20240201"
    }
  ]
}
```

**Gửi với PowerShell:**

```powershell
$body = Get-Content test-payload.json -Raw
Invoke-RestMethod -Method POST -Uri "$BASE/api/v1/ingest" -Headers $H -Body $body
```

**Gửi với curl:**

```bash
curl -s -X POST http://localhost:8080/api/v1/ingest \
  -H "X-Api-Key: dev-api-key-minimum-32-chars-here!!" \
  -H "Content-Type: application/json" \
  -d @test-payload.json
```

Kết quả mong đợi (HTTP 202):
```json
{"ingestion_id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"}
```

> Sau khi ingest, chờ ~2-3 giây để StorageWorker xử lý trước khi kiểm tra các endpoint bên dưới.

---

### 7.3 Kiểm tra danh sách host

```powershell
Invoke-RestMethod -Uri "$BASE/api/v1/hosts" -Headers $H | ConvertTo-Json -Depth 5
```

```bash
curl -s -H "X-Api-Key: dev-api-key-minimum-32-chars-here!!" \
  http://localhost:8080/api/v1/hosts | python3 -m json.tool
```

Kết quả mong đợi:
```json
{
  "total": 1,
  "page": 1,
  "size": 50,
  "items": [
    {
      "hostname": "TEST-PC-01",
      "domain": "corp.local",
      "os_version": "10.0.22631",
      "last_seen": "2026-05-13T03:00:00Z",
      "status": "online",
      "open_violations": 0
    }
  ]
}
```

**Lọc theo domain hoặc status:**

```powershell
Invoke-RestMethod -Uri "$BASE/api/v1/hosts?domain=corp.local&status=online" -Headers $H
Invoke-RestMethod -Uri "$BASE/api/v1/hosts?sort=last_seen&order=desc&size=20" -Headers $H
```

---

### 7.4 Xem snapshot mới nhất của host

```powershell
Invoke-RestMethod -Uri "$BASE/api/v1/hosts/TEST-PC-01/latest" -Headers $H | ConvertTo-Json -Depth 10
```

Kết quả trả về toàn bộ JSON payload gốc đã gửi lên.

---

### 7.5 Kiểm tra vi phạm chính sách

```powershell
Invoke-RestMethod -Uri "$BASE/api/v1/policy/violations" -Headers $H | ConvertTo-Json -Depth 5
```

```bash
curl -s -H "X-Api-Key: dev-api-key-minimum-32-chars-here!!" \
  "http://localhost:8080/api/v1/policy/violations?resolved=false" | python3 -m json.tool
```

**Lọc theo hostname, severity, rule:**

```powershell
# Chỉ vi phạm critical của TEST-PC-01
Invoke-RestMethod -Uri "$BASE/api/v1/policy/violations?hostname=TEST-PC-01&severity=critical" -Headers $H

# Vi phạm đã resolve
Invoke-RestMethod -Uri "$BASE/api/v1/policy/violations?resolved=true" -Headers $H

# Lọc theo rule cụ thể
Invoke-RestMethod -Uri "$BASE/api/v1/policy/violations?ruleId=PASS-001" -Headers $H
```

---

### 7.6 Kiểm tra inventory ứng dụng

```powershell
Invoke-RestMethod -Uri "$BASE/api/v1/apps/inventory" -Headers $H | ConvertTo-Json -Depth 5
```

```bash
curl -s -H "X-Api-Key: dev-api-key-minimum-32-chars-here!!" \
  "http://localhost:8080/api/v1/apps/inventory?name=chrome" | python3 -m json.tool
```

Kết quả mong đợi:
```json
{
  "total": 2,
  "page": 1,
  "size": 100,
  "items": [
    {
      "display_name": "Google Chrome",
      "version": "124.0.6367.91",
      "publisher": "Google LLC",
      "machine_count": 1,
      "last_seen": null
    }
  ]
}
```

**Lọc:**

```powershell
# Tìm theo tên app
Invoke-RestMethod -Uri "$BASE/api/v1/apps/inventory?name=chrome" -Headers $H

# Tìm theo publisher
Invoke-RestMethod -Uri "$BASE/api/v1/apps/inventory?publisher=microsoft" -Headers $H

# App trên máy cụ thể
Invoke-RestMethod -Uri "$BASE/api/v1/apps/inventory?hostname=TEST-PC-01" -Headers $H
```

---

### 7.7 Kiểm tra policy rules

```powershell
# Xem tất cả rules
Invoke-RestMethod -Uri "$BASE/api/v1/admin/rules" -Headers $H | ConvertTo-Json -Depth 5

# Thống kê tổng quan
Invoke-RestMethod -Uri "$BASE/api/v1/admin/stats" -Headers $H

# Bật/tắt một rule
$body = '{"enabled": false, "severity": null}'
Invoke-RestMethod -Method PUT -Uri "$BASE/api/v1/admin/rules/PASS-001" -Headers $H -Body $body
```

---

### 7.8 Xuất báo cáo CSV

```powershell
# Báo cáo compliance
Invoke-RestMethod -Uri "$BASE/api/v1/reports/compliance" -Headers $H -OutFile compliance.csv

# Báo cáo vi phạm
Invoke-RestMethod -Uri "$BASE/api/v1/reports/violations" -Headers $H -OutFile violations.csv

# Lọc theo domain
Invoke-RestMethod -Uri "$BASE/api/v1/reports/compliance?domain=corp.local" -Headers $H -OutFile compliance.csv
```

---

### 7.9 Kiểm tra diff giữa hai snapshot

```powershell
# Lấy danh sách snapshot của host
$snaps = Invoke-RestMethod -Uri "$BASE/api/v1/hosts/TEST-PC-01/snapshots" -Headers $H

# Xem diff giữa 2 snapshot
$fromId = "snapshot-guid-1"
$toId   = "snapshot-guid-2"
Invoke-RestMethod -Uri "$BASE/api/v1/hosts/TEST-PC-01/diff?from=$fromId&to=$toId" -Headers $H
```

---

### 7.10 Kiểm tra Agent đang chạy đúng

**Xem log service:**

```powershell
Get-Content "C:\ProgramData\PolicyCollector\logs\agent-*.log" -Tail 50
```

Log mong đợi khi agent gửi thành công:

```
2026-05-13 10:00:01 [INF] Collection started
2026-05-13 10:00:02 [INF] Collected HostInfo
2026-05-13 10:00:03 [INF] Collected GpoResult (42 policies)
2026-05-13 10:00:04 [INF] Collected SecurityPolicy
2026-05-13 10:00:05 [INF] Collected FirewallResult
2026-05-13 10:00:05 [INF] Collected DefenderResult
2026-05-13 10:00:06 [INF] Collected BitLocker (1 volumes)
2026-05-13 10:00:08 [INF] Collected AppInventory (127 apps)
2026-05-13 10:00:09 [INF] Payload size: 48532 bytes
2026-05-13 10:00:09 [INF] Transport: POST http://localhost:8080/api/v1/ingest => 202 Accepted
2026-05-13 10:00:09 [INF] Collection completed in 8.3s
```

**Xem Event Log:**

```powershell
Get-EventLog -LogName Application -Source PolicyCollectorSvc -Newest 10
```

---

### 7.11 Kiểm tra nhanh toàn bộ (smoke test)

Chạy lần lượt và xác nhận không có lỗi:

```powershell
$BASE = "http://localhost:8080"
$KEY  = "dev-api-key-minimum-32-chars-here!!"
$H    = @{ "X-Api-Key" = $KEY }

Write-Host "1. Health check..."
(Invoke-RestMethod "$BASE/health").status  # → Healthy

Write-Host "2. Hosts list..."
(Invoke-RestMethod -Uri "$BASE/api/v1/hosts" -Headers $H).total  # → số >= 0

Write-Host "3. Violations..."
(Invoke-RestMethod -Uri "$BASE/api/v1/policy/violations" -Headers $H).total  # → số >= 0

Write-Host "4. App inventory..."
(Invoke-RestMethod -Uri "$BASE/api/v1/apps/inventory" -Headers $H).total  # → số >= 0

Write-Host "5. Policy rules..."
(Invoke-RestMethod -Uri "$BASE/api/v1/admin/rules" -Headers $H).Count  # → số rules

Write-Host "6. Admin stats..."
Invoke-RestMethod -Uri "$BASE/api/v1/admin/stats" -Headers $H  # → object với counts

Write-Host "All checks passed!"
```

---

## 8. Xử lý lỗi thường gặp

### Agent: "No Serilog assemblies were found"

**Nguyên nhân:** File `appsettings.json` thiếu section `"Using": ["Serilog.Sinks.File"]`

**Sửa:** Thêm vào `appsettings.json`:

```json
"Serilog": {
  "Using": ["Serilog.Sinks.File"],
  ...
}
```

Sau đó rebuild:
```powershell
dotnet publish src/PolicyCollector.Agent -c Release -r win-x64 --self-contained true -o dist/agent/
```

---

### Agent: "Unable to connect to the remote server"

**Nguyên nhân:** BackendUrl sai hoặc Backend chưa chạy.

**Kiểm tra:**
```powershell
# Xem BackendUrl trong config
Get-Content "C:\ProgramData\PolicyCollector\appsettings.json" | Select-String "BackendUrl"

# Test kết nối thủ công
Invoke-WebRequest "http://localhost:8080/health" -UseBasicParsing
```

---

### Backend: HTTP 401 Unauthorized

**Nguyên nhân:** API key không khớp.

**Kiểm tra:**
```powershell
# Backend nhận key nào?
docker compose -f docker/compose.dev.yml exec backend env | grep API_KEY

# Agent gửi key nào?
Get-Content "C:\ProgramData\PolicyCollector\appsettings.json" | Select-String "ApiKey"
```

---

### Backend: HTTP 422 Unprocessable Entity

**Nguyên nhân:** Schema version không khớp.

```json
{"error": "Schema version not supported", "detail": "Supported: 1.0"}
```

Agent cũ dùng schema `"1"` — backend yêu cầu `"1.0"`. Rebuild agent là đủ.

---

### Docker: "no such service: storage-worker" hoặc container crash

```bash
# Xem log để tìm lỗi
docker compose -f docker/compose.dev.yml logs storage-worker --tail 50

# Kiểm tra image đã build chưa
docker images | grep policycollector

# Rebuild nếu cần
docker build -f docker/Dockerfile.worker -t policycollector-worker:dev .
```

---

### Docker: Build lỗi "project.assets.json was not found"

**Nguyên nhân:** Thư mục `obj/` từ Windows bị copy vào Linux container.

**Sửa:** Xác nhận file `.dockerignore` tồn tại ở repo root và chứa:
```
**/bin
**/obj
```

Sau đó build lại:
```bash
docker build --no-cache -f docker/Dockerfile -t policycollector-backend:dev .
```

---

### PostgreSQL: "cannot create unique index without partition column"

**Nguyên nhân:** Migration cũ — đã được sửa trong `001_initial_schema.sql`.

**Sửa:** Xóa data cũ và chạy lại migration:
```bash
docker compose -f docker/compose.dev.yml down -v
docker compose -f docker/compose.dev.yml up -d
```

---

### Agent service không khởi động (Event ID 7024)

```powershell
# Xem lý do trong Event Log
Get-EventLog -LogName System -Source "Service Control Manager" -Newest 5 |
    Where-Object { $_.Message -like "*PolicyCollector*" }

# Xem log của agent
Get-Content "C:\ProgramData\PolicyCollector\logs\agent-*.log" -Tail 30
```

Nguyên nhân phổ biến:
- Config file không tồn tại → chạy lại `Install-Service.ps1`
- BackendUrl chưa được cấu hình → sửa `appsettings.json`
- Port 8080 bị firewall chặn → mở port hoặc đổi URL

---

### Grafana không hiển thị data

1. Truy cập http://localhost:3000 (admin / devgrafana)
2. **Configuration → Data Sources** → kiểm tra PostgreSQL data source
3. Connection string: `Host=postgres;Port=5432;Database=policycollector;User=pcollector;Password=devpassword`
4. Click **Save & Test** → phải hiển thị "Data source is working"

---

*Tài liệu chi tiết thêm: [SETUP.md](SETUP.md) — tham chiếu cấu hình đầy đủ, mTLS, GPO ADMX, production checklist.*
